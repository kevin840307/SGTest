using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;

namespace SGTest.Server.Clan;

/// <summary>
/// Durable server-owned clan membership. SQLite is authoritative; the
/// coordinator only routes authenticated commands and online snapshots.
/// </summary>
public sealed class ClanCoordinator
{
    private readonly record struct Invitation(
        long ClanId,uint ClanRevision,long InviterActorId,long InviterConnectionEpoch,
        long TargetConnectionEpoch);

    private readonly SqliteIdentityStore _identity;
    private readonly object _inviteGate=new();
    private readonly Dictionary<long,Invitation> _invitesByTarget=[];

    public ClanCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        // Invitations are socket-generation scoped. A reconnect must never
        // inherit an invitation delivered to the previous authenticated socket.
        lock(_inviteGate)_invitesByTarget.Remove(binding.ActorId);
        var clan=await _identity.GetClanForCharacterAsync(binding.ActorId).ConfigureAwait(false);
        QueueSnapshot(binding,clan,router);
        if(clan is not null)
            BroadcastSnapshot(clan,router,binding.ActorId);
    }

    public async Task DetachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        lock(_inviteGate)
        {
            if(_invitesByTarget.TryGetValue(binding.ActorId,out var targetInvite)&&
               targetInvite.TargetConnectionEpoch==binding.ConnectionEpoch)
                _invitesByTarget.Remove(binding.ActorId);
            foreach(var target in _invitesByTarget
                .Where(x=>x.Value.InviterActorId==binding.ActorId&&
                    x.Value.InviterConnectionEpoch==binding.ConnectionEpoch)
                .Select(x=>x.Key).ToArray())
                _invitesByTarget.Remove(target);
        }
        var clan=await _identity.GetClanForCharacterAsync(binding.ActorId).ConfigureAwait(false);
        if(clan is null)return;
        long forceOffline=0;
        if(router.TryGetCurrentBinding(binding.ActorId,out var current)&&current==binding)
            forceOffline=binding.ActorId;
        BroadcastSnapshot(clan,router,binding.ActorId,forceOffline);
    }

    public async Task HandleAsync(TrustedClanCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        if(!router.IsCurrentBinding(binding))return;

        ClanStoreStatus status;
        ClanRecord? clan=null;
        long resultClanId=request.ClanId;
        long previousClanId=0;
        switch(request.Action)
        {
            case ClanCommandAction.Create:
                (status,clan)=await _identity.CreateClanAsync(
                    request.ActorId,request.Name).ConfigureAwait(false);
                resultClanId=clan?.ClanId??0;
                break;
            case ClanCommandAction.Join:
            {
                Invitation invite;
                lock(_inviteGate)
                {
                    if(!_invitesByTarget.TryGetValue(request.ActorId,out invite))
                    {
                        QueueResult(request,ClanResultStatus.InviteMissing,request.ClanId,router);
                        return;
                    }
                }
                if(invite.TargetConnectionEpoch!=request.ConnectionEpoch||
                   invite.ClanId!=request.ClanId||invite.InviterActorId!=request.TargetActorId||
                   !router.TryGetCurrentBinding(invite.InviterActorId,out var inviterBinding)||
                   inviterBinding.ConnectionEpoch!=invite.InviterConnectionEpoch)
                {
                    lock(_inviteGate)_invitesByTarget.Remove(request.ActorId);
                    QueueResult(request,ClanResultStatus.InviteStale,request.ClanId,router);
                    return;
                }
                var invitingClan=await _identity.GetClanForCharacterAsync(invite.InviterActorId)
                    .ConfigureAwait(false);
                if(invitingClan is null||invitingClan.ClanId!=invite.ClanId||
                   invitingClan.LeaderCharacterId!=invite.InviterActorId||
                   invitingClan.Revision!=invite.ClanRevision)
                {
                    lock(_inviteGate)_invitesByTarget.Remove(request.ActorId);
                    QueueResult(request,ClanResultStatus.InviteStale,request.ClanId,router);
                    return;
                }
                (status,clan)=await _identity.JoinClanAsync(
                    request.ActorId,request.ClanId).ConfigureAwait(false);
                resultClanId=clan?.ClanId??request.ClanId;
                lock(_inviteGate)_invitesByTarget.Remove(request.ActorId);
                break;
            }
            case ClanCommandAction.Leave:
                (status,clan,previousClanId)=await _identity.LeaveClanAsync(
                    request.ActorId).ConfigureAwait(false);
                resultClanId=previousClanId;
                break;
            case ClanCommandAction.Refresh:
                clan=await _identity.GetClanForCharacterAsync(request.ActorId)
                    .ConfigureAwait(false);
                status=clan is null?ClanStoreStatus.NotInClan:ClanStoreStatus.Success;
                resultClanId=clan?.ClanId??0;
                break;
            case ClanCommandAction.SetAnnouncement:
                (status,clan)=await _identity.SetClanAnnouncementAsync(
                    request.ActorId,request.Name).ConfigureAwait(false);
                resultClanId=clan?.ClanId??0;
                break;
            case ClanCommandAction.Kick:
                (status,clan)=await _identity.KickClanMemberAsync(
                    request.ActorId,request.TargetActorId).ConfigureAwait(false);
                resultClanId=clan?.ClanId??0;
                break;
            case ClanCommandAction.TransferLeadership:
                (status,clan)=await _identity.TransferClanLeadershipAsync(
                    request.ActorId,request.TargetActorId).ConfigureAwait(false);
                resultClanId=clan?.ClanId??0;
                break;
            case ClanCommandAction.Invite:
            {
                if(request.TargetActorId==request.ActorId)
                {
                    QueueResult(request,ClanResultStatus.CannotTargetSelf,0,router);
                    return;
                }
                var invitingClan=await _identity.GetClanForCharacterAsync(request.ActorId)
                    .ConfigureAwait(false);
                if(invitingClan is null)
                {
                    QueueResult(request,ClanResultStatus.NotInClan,0,router);
                    return;
                }
                if(invitingClan.LeaderCharacterId!=request.ActorId)
                {
                    QueueResult(request,ClanResultStatus.NotLeader,invitingClan.ClanId,router);
                    return;
                }
                if(!router.TryGetCurrentBinding(request.TargetActorId,out var target))
                {
                    QueueResult(request,ClanResultStatus.TargetUnavailable,invitingClan.ClanId,router);
                    return;
                }
                if(await _identity.GetClanForCharacterAsync(request.TargetActorId)
                    .ConfigureAwait(false) is not null)
                {
                    QueueResult(request,ClanResultStatus.AlreadyInClan,invitingClan.ClanId,router);
                    return;
                }
                var invite=new Invitation(
                    invitingClan.ClanId,invitingClan.Revision,request.ActorId,
                    request.ConnectionEpoch,target.ConnectionEpoch);
                lock(_inviteGate)
                {
                    if(_invitesByTarget.ContainsKey(request.TargetActorId))
                    {
                        QueueResult(request,ClanResultStatus.TargetHasPendingInvite,
                            invitingClan.ClanId,router);
                        return;
                    }
                    _invitesByTarget.Add(request.TargetActorId,invite);
                }
                try
                {
                    QueueInvite(target,invitingClan,request,router);
                }
                catch
                {
                    lock(_inviteGate)_invitesByTarget.Remove(request.TargetActorId);
                    throw;
                }
                QueueResult(request,ClanResultStatus.Success,invitingClan.ClanId,router);
                return;
            }
            case ClanCommandAction.RejectInvite:
            {
                Invitation invite;
                lock(_inviteGate)
                {
                    if(!_invitesByTarget.TryGetValue(request.ActorId,out invite))
                    {
                        QueueResult(request,ClanResultStatus.InviteMissing,request.ClanId,router);
                        return;
                    }
                    if(invite.TargetConnectionEpoch!=request.ConnectionEpoch||
                       invite.ClanId!=request.ClanId||invite.InviterActorId!=request.TargetActorId)
                    {
                        _invitesByTarget.Remove(request.ActorId);
                        QueueResult(request,ClanResultStatus.InviteStale,request.ClanId,router);
                        return;
                    }
                    _invitesByTarget.Remove(request.ActorId);
                }
                QueueResult(request,ClanResultStatus.Success,request.ClanId,router);
                return;
            }
            default:
                status=ClanStoreStatus.Conflict;
                break;
        }

        var send=router.QueueClanResult(request,new(
            request.RequestMapEpoch,request.ClanSequence,request.Action,
            (ClanResultStatus)status,resultClanId));
        if(send is GatewaySendStatus.Closed or GatewaySendStatus.GenerationMismatch)
            return;
        if(send==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Clan result queue backpressured");

        if(status!=ClanStoreStatus.Success)
            return;

        // Sender always receives an authoritative snapshot; Leave gets a clear
        // snapshot even when the clan continues for other members.
        var senderClan=await _identity.GetClanForCharacterAsync(request.ActorId)
            .ConfigureAwait(false);
        QueueSnapshot(binding,senderClan,router);

        // Membership, leadership and announcement changes fan out only to
        // current authenticated generations. A kicked online target receives an
        // explicit clear snapshot before the remaining clan is broadcast.
        if(request.Action==ClanCommandAction.Kick&&request.TargetActorId>0&&
           router.TryGetCurrentBinding(request.TargetActorId,out var kicked))
            QueueSnapshot(kicked,null,router);
        if(clan is not null)
            BroadcastSnapshot(clan,router,request.ActorId);
    }

    public async Task<ConnectionBinding[]> GetChatRecipientsAsync(
        ConnectionBinding sender,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.IsCurrentBinding(sender))
            return [];

        var clan=await _identity.GetClanForCharacterAsync(sender.ActorId)
            .ConfigureAwait(false);
        if(clan is null||!clan.Members.Any(x=>x.CharacterId==sender.ActorId))
            return [];

        var recipients=new List<ConnectionBinding>(clan.Members.Count);
        foreach(var member in clan.Members)
        {
            if(router.TryGetCurrentBinding(member.CharacterId,out var current))
                recipients.Add(current);
        }

        return recipients.Any(x=>x.ActorId==sender.ActorId&&
                x.ConnectionEpoch==sender.ConnectionEpoch)
            ?recipients.ToArray():[];
    }

    private static void QueueResult(
        TrustedClanCommand request,ClanResultStatus status,long clanId,
        WorldResultRouter router)
    {
        var queued=router.QueueClanResult(request,new(
            request.RequestMapEpoch,request.ClanSequence,request.Action,status,Math.Max(0,clanId)));
        if(queued==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Clan result queue backpressured");
    }

    private static void QueueInvite(
        ConnectionBinding target,ClanRecord clan,TrustedClanCommand request,
        WorldResultRouter router)
    {
        if(!router.TryGetPlayerIdentity(
                request.ActorId,request.ConnectionEpoch,out var identity))
            throw new IOException("Clan inviter identity disappeared before invite");
        var queued=router.QueueClanInvite(target,new(
            target.MapEpoch,clan.ClanId,clan.Revision,request.ActorId,
            request.ConnectionEpoch,identity.DisplayName,clan.Name));
        if(queued!=GatewaySendStatus.Enqueued)
            throw new IOException("Reliable clan invite could not be queued: "+queued);
    }

    private static void BroadcastSnapshot(
        ClanRecord clan,WorldResultRouter router,long alreadySentActorId=0,
        long forceOfflineActorId=0)
    {
        foreach(var member in clan.Members)
        {
            if(member.CharacterId==alreadySentActorId)continue;
            if(router.TryGetCurrentBinding(member.CharacterId,out var binding))
                QueueSnapshot(binding,clan,router,forceOfflineActorId);
        }
    }

    private static void QueueSnapshot(
        ConnectionBinding binding,ClanRecord? clan,WorldResultRouter router,
        long forceOfflineActorId=0)
    {
        var snapshot=clan is null
            ? new ClanSnapshotWire(binding.MapEpoch,0,0,0,string.Empty,[])
            : new ClanSnapshotWire(
                binding.MapEpoch,clan.ClanId,clan.Revision,clan.LeaderCharacterId,
                clan.Name,clan.Members.Select(x=>new ClanMemberWire(
                    x.CharacterId,(byte)x.Role,
                    x.CharacterId!=forceOfflineActorId&&
                        router.TryGetCurrentBinding(x.CharacterId,out _),
                    x.Name)).ToArray(),clan.Announcement);
        var status=router.QueueClanSnapshot(binding,snapshot);
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Clan snapshot queue backpressured");
    }
}
