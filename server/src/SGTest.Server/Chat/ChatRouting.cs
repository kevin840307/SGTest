using SGTest.Server.Clan;
using SGTest.Server.Gateway;
using SGTest.Server.Party;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;

namespace SGTest.Server.Chat;

/// <summary>
/// Routes trusted chat over existing authoritative session bindings.
/// Authentication and chat sequence validation remain in Gateway; this
/// stateless policy owns only channel recipient selection and delivery.
/// </summary>
public static class ChatRouting
{
    public static async Task RouteAsync(TrustedChatCommand request,
        WorldResultRouter router,PartyCoordinator? parties,
        Func<long,long,Task<bool>>? isIgnored=null,ClanCoordinator? clans=null)
    {
        var sender=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        if(!router.IsCurrentBinding(sender)||
           !router.TryGetPlayerIdentity(
               request.ActorId,request.ConnectionEpoch,out var identity))
            return;

        if(request.Channel==ChatChannel.World)
        {
            const int worldChannelMinimumLevel=10; // Old server chat.cfg: [WorldChannel] lvllimit=10
            if(request.PlayerLevel<worldChannelMinimumLevel)
                return;

            foreach(var recipient in router.SnapshotActiveBindings())
            {
                bool self=recipient==sender;
                if(!self&&isIgnored is not null&&
                   await isIgnored(recipient.ActorId,request.ActorId).ConfigureAwait(false))
                    continue;
                var delivery=new ChatDeliveryWire(
                    recipient.MapEpoch,request.ActorId,request.ConnectionEpoch,
                    request.ChatSequence,ChatChannel.World,request.BubbleType,
                    identity.DisplayName,request.Message);
                var queued=router.QueueChatDelivery(recipient,delivery);
                if(queued!=GatewaySendStatus.Enqueued)
                {
                    if(self)
                        throw new IOException(
                            "Reliable self world-chat delivery could not be queued: "+queued);
                    router.CloseCurrentBinding(recipient);
                }
            }
            return;
        }

        if(request.Channel==ChatChannel.Nearby)
        {
            foreach(var recipient in router.SnapshotActiveBindings())
            {
                bool self=recipient==sender;
                if(!self&&
                   (!router.IsVisibleActor(sender,recipient)||
                    !router.IsVisibleActor(recipient,sender)))
                    continue;
                if(!self&&isIgnored is not null&&
                   await isIgnored(recipient.ActorId,request.ActorId).ConfigureAwait(false))
                    continue;
                var delivery=new ChatDeliveryWire(
                    recipient.MapEpoch,request.ActorId,request.ConnectionEpoch,
                    request.ChatSequence,request.Channel,request.BubbleType,
                    identity.DisplayName,request.Message);
                var queued=router.QueueChatDelivery(recipient,delivery);
                if(self&&queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable self nearby-chat delivery could not be queued: "+queued);
            }
            return;
        }

        if(request.Channel==ChatChannel.Warband)
        {
            if(clans is null)return;
            var recipients=await clans.GetChatRecipientsAsync(sender,router)
                .ConfigureAwait(false);
            foreach(var recipient in recipients)
            {
                bool self=recipient.ActorId==request.ActorId&&
                    recipient.ConnectionEpoch==request.ConnectionEpoch;
                if(!self&&isIgnored is not null&&
                   await isIgnored(recipient.ActorId,request.ActorId).ConfigureAwait(false))
                    continue;
                var delivery=new ChatDeliveryWire(
                    recipient.MapEpoch,request.ActorId,request.ConnectionEpoch,
                    request.ChatSequence,ChatChannel.Warband,request.BubbleType,
                    identity.DisplayName,request.Message);
                var queued=router.QueueChatDelivery(recipient,delivery);
                if(queued!=GatewaySendStatus.Enqueued)
                {
                    if(self)
                        throw new IOException(
                            "Reliable self warband-chat delivery could not be queued: "+queued);
                    router.CloseCurrentBinding(recipient);
                }
            }
            return;
        }

        if(request.Channel==ChatChannel.Party)
        {
            if(parties is null)return;
            if(!parties.TryGetRecipients(sender,router,out var recipients))
                return;
            foreach(var recipient in recipients)
            {
                bool self=recipient.ActorId==request.ActorId&&
                    recipient.ConnectionEpoch==request.ConnectionEpoch;
                if(!self&&isIgnored is not null&&
                   await isIgnored(recipient.ActorId,request.ActorId).ConfigureAwait(false))
                    continue;
                var delivery=new ChatDeliveryWire(
                    recipient.MapEpoch,request.ActorId,request.ConnectionEpoch,
                    request.ChatSequence,ChatChannel.Party,request.BubbleType,
                    identity.DisplayName,request.Message);
                var queued=router.QueueChatDelivery(recipient,delivery);
                if(queued!=GatewaySendStatus.Enqueued)
                {
                    if(self)
                        throw new IOException(
                            "Reliable self party-chat delivery could not be queued: "+queued);
                    router.CloseCurrentBinding(recipient);
                }
            }
            return;
        }

        bool whisperByName=!string.IsNullOrEmpty(request.TargetName);
        if(request.Channel!=ChatChannel.Whisper||
           (request.TargetActorId>0)==whisperByName||
           request.TargetActorId<0)
            throw new InvalidOperationException(
                "Unsupported trusted chat target selector reached authorization");

        ConnectionBinding target=default;
        PlayerIdentityPresentation targetIdentity=default;
        bool targetFound=whisperByName
            ?router.TryResolveOnlinePlayerByName(
                request.TargetName,out target,out targetIdentity)
            :router.TryGetCurrentBinding(request.TargetActorId,out target)&&
             router.TryGetPlayerIdentity(
                target.ActorId,target.ConnectionEpoch,out targetIdentity);

        WhisperResultWire result;
        if(targetFound&&target.ActorId==request.ActorId)
        {
            result=new(
                request.RequestMapEpoch,request.ChatSequence,
                WhisperResultStatus.SelfTarget,target.ActorId,
                0,string.Empty);
        }
        else if(!targetFound)
        {
            result=new(
                request.RequestMapEpoch,request.ChatSequence,
                WhisperResultStatus.TargetUnavailable,request.TargetActorId,
                0,string.Empty);
        }
        else if(isIgnored is not null&&
                await isIgnored(target.ActorId,request.ActorId).ConfigureAwait(false))
        {
            result=new(
                request.RequestMapEpoch,request.ChatSequence,
                WhisperResultStatus.TargetUnavailable,request.TargetActorId,
                0,string.Empty);
        }
        else
        {
            var delivery=new ChatDeliveryWire(
                target.MapEpoch,request.ActorId,request.ConnectionEpoch,
                request.ChatSequence,ChatChannel.Whisper,request.BubbleType,
                identity.DisplayName,request.Message);
            var delivered=router.QueueChatDelivery(target,delivery);
            result=delivered==GatewaySendStatus.Enqueued
                ?new WhisperResultWire(
                    request.RequestMapEpoch,request.ChatSequence,
                    WhisperResultStatus.Delivered,target.ActorId,
                    target.ConnectionEpoch,targetIdentity.DisplayName)
                :new WhisperResultWire(
                    request.RequestMapEpoch,request.ChatSequence,
                    WhisperResultStatus.TargetUnavailable,
                    target.ActorId,0,string.Empty);
        }

        var resultQueued=router.QueueWhisperResult(request,result);
        if(resultQueued!=GatewaySendStatus.Enqueued)
            throw new IOException(
                "Reliable whisper sender result could not be queued: "+
                resultQueued);
        return;
    }
}
