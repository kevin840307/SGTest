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
    private readonly SqliteIdentityStore _identity;

    public ClanCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        var clan=await _identity.GetClanForCharacterAsync(binding.ActorId).ConfigureAwait(false);
        QueueSnapshot(binding,clan,router);
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
                (status,clan)=await _identity.JoinClanAsync(
                    request.ActorId,request.ClanId).ConfigureAwait(false);
                resultClanId=clan?.ClanId??request.ClanId;
                break;
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

    private static void BroadcastSnapshot(
        ClanRecord clan,WorldResultRouter router,long alreadySentActorId=0)
    {
        foreach(var member in clan.Members)
        {
            if(member.CharacterId==alreadySentActorId)continue;
            if(router.TryGetCurrentBinding(member.CharacterId,out var binding))
                QueueSnapshot(binding,clan,router);
        }
    }

    private static void QueueSnapshot(
        ConnectionBinding binding,ClanRecord? clan,WorldResultRouter router)
    {
        var snapshot=clan is null
            ? new ClanSnapshotWire(binding.MapEpoch,0,0,0,string.Empty,[])
            : new ClanSnapshotWire(
                binding.MapEpoch,clan.ClanId,clan.Revision,clan.LeaderCharacterId,
                clan.Name,clan.Members.Select(x=>new ClanMemberWire(
                    x.CharacterId,(byte)x.Role,x.Name)).ToArray(),clan.Announcement);
        var status=router.QueueClanSnapshot(binding,snapshot);
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Clan snapshot queue backpressured");
    }
}
