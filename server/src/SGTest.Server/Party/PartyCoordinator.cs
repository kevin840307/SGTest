using SGTest.Server.Visibility;
using SGTest.Server.Gateway;
using System.Diagnostics;
using SGTest.Server.Wire;

namespace SGTest.Server.Party;

/// <summary>
/// Server-owned online Party membership. Parties are intentionally ephemeral:
/// no SQLite row survives disconnect/restart. ActorId/ConnectionEpoch and names
/// are taken only from the authenticated router, never from client payloads.
/// </summary>
public sealed class PartyCoordinator
{
    private sealed class Party
    {
        public required long PartyId;
        public required long LeaderActorId;
        public uint Revision = 1;
        public readonly List<long> Members = [];
        public readonly Dictionary<long,long> Connections = [];
    }

    // A pending invite is valid only for the authenticated target socket that
    // received it; ActorId alone is not a session identity across reconnects.
    private readonly record struct Invitation(
        long PartyId,long InviterActorId,long InviterConnectionEpoch,
        long TargetConnectionEpoch,long CreatedTimestamp);

    // A bounded online invite, not a persistent mailbox. Clock is monotonic;
    // tests can inject a deterministic timestamp without sleeping.
    public const int InvitationLifetimeSeconds = 60;
    private readonly Func<long> _timestamp;

    public PartyCoordinator(Func<long>? timestamp = null)
    {
        _timestamp=timestamp ?? Stopwatch.GetTimestamp;
    }

    private bool InviteExpired(Invitation invite)
    {
        long current=_timestamp();
        return current<invite.CreatedTimestamp ||
            Stopwatch.GetElapsedTime(invite.CreatedTimestamp,current)
                >=TimeSpan.FromSeconds(InvitationLifetimeSeconds);
    }

    private readonly object _gate = new();
    private readonly Dictionary<long,Party> _parties = [];
    private readonly Dictionary<long,long> _partyByActor = [];
    private readonly Dictionary<long,Invitation> _invitesByTarget = [];
    private long _nextPartyId = 1;

    public int PartyCount { get { lock(_gate)return _parties.Count; } }

    public Task HandleAsync(
        TrustedPartyCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        lock(_gate)
        {
            var sender=new ConnectionBinding(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
            if(!router.IsCurrentBinding(sender))
                return Task.CompletedTask;

            switch(request.Action)
            {
                case PartyCommandAction.Invite:
                    HandleInviteLocked(request,sender,router);
                    break;
                case PartyCommandAction.Accept:
                    HandleAcceptLocked(request,sender,router);
                    break;
                case PartyCommandAction.Reject:
                    HandleRejectLocked(request,sender,router);
                    break;
                case PartyCommandAction.Leave:
                    HandleLeaveLocked(request,sender,router);
                    break;
                case PartyCommandAction.Kick:
                    HandleKickLocked(request,sender,router);
                    break;
                case PartyCommandAction.Promote:
                    HandlePromoteLocked(request,sender,router);
                    break;
                default:
                    throw new InvalidOperationException("Unhandled party action");
            }
        }
        return Task.CompletedTask;
    }

    public bool TryGetRecipients(
        ConnectionBinding sender,WorldResultRouter router,
        out ConnectionBinding[] recipients)
    {
        ArgumentNullException.ThrowIfNull(router);
        lock(_gate)
        {
            recipients=[];
            if(!router.IsCurrentBinding(sender)||
               !_partyByActor.TryGetValue(sender.ActorId,out long partyId)||
               !_parties.TryGetValue(partyId,out var party)||
               !party.Connections.TryGetValue(sender.ActorId,out long connection)||
               connection!=sender.ConnectionEpoch)
                return false;
            var result=new List<ConnectionBinding>(party.Members.Count);
            foreach(long actorId in party.Members)
            {
                if(!party.Connections.TryGetValue(actorId,out long expectedConnection)||
                   !router.TryGetCurrentBinding(actorId,out var current)||
                   current.ConnectionEpoch!=expectedConnection)
                    continue;
                result.Add(current);
            }
            recipients=result.ToArray();
            return recipients.Any(x=>x.ActorId==sender.ActorId&&
                x.ConnectionEpoch==sender.ConnectionEpoch);
        }
    }

    /// <summary>
    /// Session-scoped membership: stale old-socket cleanup cannot remove a newer
    /// generation because the party stores the exact connection generation.
    /// </summary>
    public void Disconnect(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        lock(_gate)
        {
            // Old socket teardown must not erase a different generation's
            // freshly delivered invite (reconnect/replace is asynchronous).
            if(_invitesByTarget.TryGetValue(binding.ActorId,out var pending) &&
               pending.TargetConnectionEpoch==binding.ConnectionEpoch)
                _invitesByTarget.Remove(binding.ActorId);

            if(!_partyByActor.TryGetValue(binding.ActorId,out long partyId)||
               !_parties.TryGetValue(partyId,out var party)||
               !party.Connections.TryGetValue(binding.ActorId,out long connection)||
               connection!=binding.ConnectionEpoch)
                return;

            RemoveMemberLocked(party,binding.ActorId,router,sendCleared:false);
        }
    }

    private void HandleInviteLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        bool byName=!string.IsNullOrEmpty(request.TargetName);
        if((request.TargetActorId>0)==byName)
        {
            QueueResultLocked(request,PartyResultStatus.TargetUnavailable,
                request.PartyId,0,router);
            return;
        }

        ConnectionBinding target;
        PlayerIdentityPresentation targetIdentity;
        long targetActorId=request.TargetActorId;
        if(byName)
        {
            if(!router.TryResolveOnlinePlayerByName(
                    request.TargetName,out target,out targetIdentity))
            {
                QueueResultLocked(request,PartyResultStatus.TargetUnavailable,
                    request.PartyId,0,router);
                return;
            }
            targetActorId=target.ActorId;
        }
        else if(!router.TryGetCurrentBinding(targetActorId,out target)||
                !router.TryGetPlayerIdentity(
                    target.ActorId,target.ConnectionEpoch,out targetIdentity))
        {
            QueueResultLocked(request,PartyResultStatus.TargetUnavailable,
                request.PartyId,targetActorId,router);
            return;
        }

        if(targetActorId==request.ActorId)
        {
            QueueResultLocked(request,PartyResultStatus.CannotTargetSelf,
                request.PartyId,targetActorId,router);
            return;
        }
        if(_partyByActor.ContainsKey(targetActorId))
        {
            QueueResultLocked(request,PartyResultStatus.AlreadyInParty,
                request.PartyId,targetActorId,router);
            return;
        }
        if(_invitesByTarget.TryGetValue(targetActorId,out var prior))
        {
            if(prior.TargetConnectionEpoch!=target.ConnectionEpoch||
               InviteExpired(prior))
                _invitesByTarget.Remove(targetActorId);
            else
            {
                QueueResultLocked(request,PartyResultStatus.TargetHasPendingInvite,
                    request.PartyId,targetActorId,router);
                return;
            }
        }

        Party party;
        bool created=false;
        if(_partyByActor.TryGetValue(request.ActorId,out long currentPartyId))
        {
            if(!_parties.TryGetValue(currentPartyId,out party!))
                throw new InvalidOperationException("Party membership index is corrupt");
            if(request.PartyId!=party.PartyId)
            {
                QueueResultLocked(request,PartyResultStatus.InviteStale,
                    party.PartyId,targetActorId,router);
                return;
            }
            if(party.LeaderActorId!=request.ActorId)
            {
                QueueResultLocked(request,PartyResultStatus.NotLeader,
                    party.PartyId,targetActorId,router);
                return;
            }
        }
        else
        {
            if(request.PartyId!=0)
            {
                QueueResultLocked(request,PartyResultStatus.NotInParty,
                    0,targetActorId,router);
                return;
            }
            long id=checked(_nextPartyId++);
            party=new Party{PartyId=id,LeaderActorId=request.ActorId};
            party.Members.Add(request.ActorId);
            party.Connections.Add(request.ActorId,request.ConnectionEpoch);
            _parties.Add(id,party);
            _partyByActor.Add(request.ActorId,id);
            created=true;
        }

        if(party.Members.Count>=WireProtocol.PartyMaxMembers)
        {
            QueueResultLocked(request,PartyResultStatus.PartyFull,
                party.PartyId,targetActorId,router);
            return;
        }

        // The invitation must enter the recipient's reliable FIFO before the
        // inviter receives Success. A full/closed queue must not leave a
        // pending invite or a newly-created one-person party behind.
        // HandleAsync serializes invite/accept under _gate, so rollback cannot
        // race an Accept. A recipient that saw an invite before a later sender
        // enqueue failure will get InviteMissing instead of joining a ghost party.
        try
        {
            QueueInviteLocked(target,party,request.ActorId,
                request.ConnectionEpoch,router);
            _invitesByTarget.Add(targetActorId,
                new(party.PartyId,request.ActorId,request.ConnectionEpoch,
                    target.ConnectionEpoch,_timestamp()));
            QueueResultLocked(request,PartyResultStatus.Success,
                party.PartyId,targetActorId,router);
        }
        catch
        {
            _invitesByTarget.Remove(targetActorId);
            if(created)
            {
                _partyByActor.Remove(request.ActorId);
                _parties.Remove(party.PartyId);
            }
            throw;
        }
        if(created)BroadcastSnapshotLocked(party,router);
    }

    private void HandleAcceptLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        if(_partyByActor.ContainsKey(request.ActorId))
        {
            QueueResultLocked(request,PartyResultStatus.AlreadyInParty,
                request.PartyId,request.TargetActorId,router);
            return;
        }
        if(!_invitesByTarget.TryGetValue(request.ActorId,out var invite))
        {
            QueueResultLocked(request,PartyResultStatus.InviteMissing,
                request.PartyId,request.TargetActorId,router);
            return;
        }
        if(invite.TargetConnectionEpoch!=request.ConnectionEpoch||
           InviteExpired(invite)||
           invite.PartyId!=request.PartyId||
           invite.InviterActorId!=request.TargetActorId||
           !_parties.TryGetValue(invite.PartyId,out var party)||
           party.LeaderActorId!=invite.InviterActorId||
           !party.Connections.TryGetValue(
               invite.InviterActorId,out long inviterConnection)||
           inviterConnection!=invite.InviterConnectionEpoch||
           !router.TryGetCurrentBinding(invite.InviterActorId,out var inviterCurrent)||
           inviterCurrent.ConnectionEpoch!=invite.InviterConnectionEpoch)
        {
            QueueResultLocked(request,PartyResultStatus.InviteStale,
                request.PartyId,request.TargetActorId,router);
            _invitesByTarget.Remove(request.ActorId);
            return;
        }
        if(party.Members.Count>=WireProtocol.PartyMaxMembers)
        {
            QueueResultLocked(request,PartyResultStatus.PartyFull,
                party.PartyId,request.TargetActorId,router);
            _invitesByTarget.Remove(request.ActorId);
            return;
        }

        // A successful Accept must be deliverable before membership changes.
        // If the recipient's reliable FIFO is full, retain the invitation so
        // it can be retried without creating an unacknowledged party member.
        QueueResultLocked(request,PartyResultStatus.Success,
            party.PartyId,request.TargetActorId,router);
        _invitesByTarget.Remove(request.ActorId);
        party.Members.Add(request.ActorId);
        party.Connections.Add(request.ActorId,request.ConnectionEpoch);
        _partyByActor.Add(request.ActorId,party.PartyId);
        party.Revision=checked(party.Revision+1);
        BroadcastSnapshotLocked(party,router);
    }

    private void HandleRejectLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        if(!_invitesByTarget.TryGetValue(request.ActorId,out var invite))
        {
            QueueResultLocked(request,PartyResultStatus.InviteMissing,
                request.PartyId,request.TargetActorId,router);
            return;
        }
        if(invite.TargetConnectionEpoch!=request.ConnectionEpoch||
           InviteExpired(invite)||
           invite.PartyId!=request.PartyId||
           invite.InviterActorId!=request.TargetActorId)
        {
            QueueResultLocked(request,PartyResultStatus.InviteStale,
                request.PartyId,request.TargetActorId,router);
            _invitesByTarget.Remove(request.ActorId);
            return;
        }
        // Keep the pending invitation if its reliable rejection receipt fails.
        QueueResultLocked(request,PartyResultStatus.Success,
            request.PartyId,request.TargetActorId,router);
        _invitesByTarget.Remove(request.ActorId);
    }

    private void HandleLeaveLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        if(!TryOwnPartyLocked(request,out var party,out var status))
        {
            QueueResultLocked(request,status,request.PartyId,0,router);
            return;
        }
        QueueResultLocked(request,PartyResultStatus.Success,
            party!.PartyId,0,router);
        RemoveMemberLocked(party,request.ActorId,router,sendCleared:true);
    }

    private void HandleKickLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        if(!TryOwnPartyLocked(request,out var party,out var status))
        {
            QueueResultLocked(request,status,request.PartyId,request.TargetActorId,router);
            return;
        }
        if(party!.LeaderActorId!=request.ActorId)
        {
            QueueResultLocked(request,PartyResultStatus.NotLeader,
                party.PartyId,request.TargetActorId,router);
            return;
        }
        if(request.TargetActorId==request.ActorId||
           !party.Connections.ContainsKey(request.TargetActorId))
        {
            QueueResultLocked(request,PartyResultStatus.TargetNotMember,
                party.PartyId,request.TargetActorId,router);
            return;
        }
        QueueResultLocked(request,PartyResultStatus.Success,
            party.PartyId,request.TargetActorId,router);
        RemoveMemberLocked(party,request.TargetActorId,router,sendCleared:true);
    }

    private void HandlePromoteLocked(
        TrustedPartyCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        if(!TryOwnPartyLocked(request,out var party,out var status))
        {
            QueueResultLocked(request,status,request.PartyId,request.TargetActorId,router);
            return;
        }
        if(party!.LeaderActorId!=request.ActorId)
        {
            QueueResultLocked(request,PartyResultStatus.NotLeader,
                party.PartyId,request.TargetActorId,router);
            return;
        }
        if(request.TargetActorId==request.ActorId||
           !party.Connections.ContainsKey(request.TargetActorId))
        {
            QueueResultLocked(request,PartyResultStatus.TargetNotMember,
                party.PartyId,request.TargetActorId,router);
            return;
        }
        // A failed reliable receipt must not silently transfer leadership
        // or invalidate outstanding invitations.
        QueueResultLocked(request,PartyResultStatus.Success,
            party.PartyId,request.TargetActorId,router);
        party.LeaderActorId=request.TargetActorId;
        party.Revision=checked(party.Revision+1);
        InvalidateInvitesForPartyLocked(party.PartyId);
        BroadcastSnapshotLocked(party,router);
    }

    private bool TryOwnPartyLocked(
        TrustedPartyCommand request,out Party? party,out PartyResultStatus status)
    {
        party=null;
        if(!_partyByActor.TryGetValue(request.ActorId,out long partyId)||
           !_parties.TryGetValue(partyId,out party))
        {
            status=PartyResultStatus.NotInParty;
            return false;
        }
        if(request.PartyId!=partyId)
        {
            status=PartyResultStatus.InviteStale;
            return false;
        }
        if(!party.Connections.TryGetValue(request.ActorId,out long connection)||
           connection!=request.ConnectionEpoch)
        {
            status=PartyResultStatus.NotInParty;
            return false;
        }
        status=PartyResultStatus.Success;
        return true;
    }

    private void RemoveMemberLocked(
        Party party,long actorId,WorldResultRouter router,bool sendCleared)
    {
        if(!party.Connections.TryGetValue(actorId,out long connection))return;
        party.Members.Remove(actorId);
        party.Connections.Remove(actorId);
        _partyByActor.Remove(actorId);
        party.Revision=checked(party.Revision+1);

        if(sendCleared&&router.TryGetCurrentBinding(actorId,out var current)&&
           current.ConnectionEpoch==connection)
            QueueClearedLocked(current,party.PartyId,party.Revision,router);

        if(party.Members.Count==0)
        {
            _parties.Remove(party.PartyId);
            InvalidateInvitesForPartyLocked(party.PartyId);
            return;
        }

        if(party.LeaderActorId==actorId)
        {
            party.LeaderActorId=party.Members[0];
            InvalidateInvitesForPartyLocked(party.PartyId);
        }
        BroadcastSnapshotLocked(party,router);
    }

    private void InvalidateInvitesForPartyLocked(long partyId)
    {
        foreach(long target in _invitesByTarget
            .Where(x=>x.Value.PartyId==partyId).Select(x=>x.Key).ToArray())
            _invitesByTarget.Remove(target);
    }

    private void QueueResultLocked(
        TrustedPartyCommand request,PartyResultStatus status,
        long partyId,long targetActorId,WorldResultRouter router)
    {
        var result=new PartyResultWire(
            request.RequestMapEpoch,request.PartySequence,request.Action,
            status,Math.Max(0,partyId),Math.Max(0,targetActorId));
        var queued=router.QueuePartyResult(request,result);
        if(queued!=GatewaySendStatus.Enqueued)
            throw new IOException("Reliable party result could not be queued: "+queued);
    }

    private void QueueInviteLocked(
        ConnectionBinding target,Party party,long inviterActorId,
        long inviterConnectionEpoch,WorldResultRouter router)
    {
        if(!router.TryGetPlayerIdentity(
                inviterActorId,inviterConnectionEpoch,out var inviterIdentity))
            throw new IOException("Party inviter identity disappeared before invite");
        var invite=new PartyInviteWire(
            target.MapEpoch,party.PartyId,party.Revision,inviterActorId,
            inviterConnectionEpoch,inviterIdentity.DisplayName);
        var queued=router.QueuePartyInvite(target,invite);
        if(queued!=GatewaySendStatus.Enqueued)
            throw new IOException("Reliable party invite could not be queued: "+queued);
    }

    private void QueueClearedLocked(
        ConnectionBinding target,long partyId,uint revision,WorldResultRouter router)
    {
        var queued=router.QueuePartySnapshot(target,new(
            target.MapEpoch,partyId,revision,0,Array.Empty<PartyMemberWire>()));
        if(queued!=GatewaySendStatus.Enqueued)
            router.CloseCurrentBinding(target);
    }

    private void BroadcastSnapshotLocked(Party party,WorldResultRouter router)
    {
        var members=new List<PartyMemberWire>(party.Members.Count);
        var recipients=new List<ConnectionBinding>(party.Members.Count);
        foreach(long actorId in party.Members)
        {
            if(!party.Connections.TryGetValue(actorId,out long expectedConnection)||
               !router.TryGetCurrentBinding(actorId,out var binding)||
               binding.ConnectionEpoch!=expectedConnection||
               !router.TryGetPlayerIdentity(
                   actorId,expectedConnection,out var identity))
                continue;
            recipients.Add(binding);
            members.Add(new(actorId,expectedConnection,identity.DisplayName));
        }

        foreach(var recipient in recipients)
        {
            var queued=router.QueuePartySnapshot(recipient,new(
                recipient.MapEpoch,party.PartyId,party.Revision,
                party.LeaderActorId,members));
            if(queued!=GatewaySendStatus.Enqueued)
                router.CloseCurrentBinding(recipient);
        }
    }
}
