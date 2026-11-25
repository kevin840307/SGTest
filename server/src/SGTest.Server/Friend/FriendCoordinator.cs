using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Friend;

/// <summary>
/// Original-style friend invitation flow. Friendship is durable and bilateral only
/// after the invited player accepts; live presence comes from current Gateway bindings.
/// </summary>
public sealed class FriendCoordinator
{
    private sealed record PendingInvite(
        ConnectionBinding Requester,string RequesterName,
        ConnectionBinding Target,string TargetName);

    private readonly SqliteIdentityStore _identity;
    private readonly SemaphoreSlim _serial=new(1,1);
    private readonly Dictionary<long,PendingInvite> _invitesByTarget=new();

    public FriendCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var friends=await _identity.GetFriendsAsync(binding.ActorId).ConfigureAwait(false);
            if(friends.Count>0)
                await QueueSnapshotAsync(binding,router).ConfigureAwait(false);
            foreach(var friend in friends)
                if(router.TryGetCurrentBinding(friend.CharacterId,out var peer))
                    await QueueSnapshotAsync(peer,router).ConfigureAwait(false);
        }
        finally{_serial.Release();}
    }

    public async Task DetachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var friends=await _identity.GetFriendsAsync(binding.ActorId).ConfigureAwait(false);
            foreach(var friend in friends)
                if(router.TryGetCurrentBinding(friend.CharacterId,out var peer))
                    await QueueSnapshotAsync(
                        peer,router,binding.ActorId,failOnBackpressure:false)
                        .ConfigureAwait(false);

            if(_invitesByTarget.TryGetValue(binding.ActorId,out var received)&&
               SameConnection(received.Target,binding))
            {
                _invitesByTarget.Remove(binding.ActorId);
                if(router.TryGetCurrentBinding(received.Requester.ActorId,out var requester)&&
                   SameConnection(requester,received.Requester))
                    QueueInviteNotice(
                        requester,received.Target.ActorId,received.Target.ConnectionEpoch,
                        FriendInviteState.Cancelled,received.TargetName,router,false);
            }

            foreach(var pair in _invitesByTarget
                .Where(x=>SameConnection(x.Value.Requester,binding)).ToArray())
            {
                _invitesByTarget.Remove(pair.Key);
                var pending=pair.Value;
                if(router.TryGetCurrentBinding(pending.Target.ActorId,out var target)&&
                   SameConnection(target,pending.Target))
                    QueueInviteNotice(
                        target,pending.Requester.ActorId,pending.Requester.ConnectionEpoch,
                        FriendInviteState.Cancelled,pending.RequesterName,router,false);
            }
        }
        finally{_serial.Release();}
    }

    public async Task HandleAsync(TrustedFriendCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            var sender=new ConnectionBinding(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
            if(!router.IsCurrentBinding(sender))return;

            switch(request.Action)
            {
                case FriendCommandAction.Add:
                    await HandleInviteAsync(request,sender,router).ConfigureAwait(false);
                    return;
                case FriendCommandAction.Accept:
                    await HandleReplyAsync(request,sender,true,router).ConfigureAwait(false);
                    return;
                case FriendCommandAction.Reject:
                    await HandleReplyAsync(request,sender,false,router).ConfigureAwait(false);
                    return;
                case FriendCommandAction.Remove:
                {
                    var status=await _identity.RemoveFriendAsync(
                        request.ActorId,request.TargetActorId).ConfigureAwait(false);
                    QueueResult(request,(FriendResultStatus)status,request.TargetActorId,router);
                    if(status!=FriendStoreStatus.Success)return;
                    await QueueSnapshotAsync(sender,router).ConfigureAwait(false);
                    if(router.TryGetCurrentBinding(request.TargetActorId,out var peer))
                        await QueueSnapshotAsync(peer,router).ConfigureAwait(false);
                    return;
                }
                case FriendCommandAction.Refresh:
                    QueueResult(request,FriendResultStatus.Success,0,router);
                    await QueueSnapshotAsync(sender,router).ConfigureAwait(false);
                    return;
                default:
                    QueueResult(request,FriendResultStatus.Conflict,request.TargetActorId,router);
                    return;
            }
        }
        finally{_serial.Release();}
    }

    private async Task HandleInviteAsync(
        TrustedFriendCommand request,ConnectionBinding sender,WorldResultRouter router)
    {
        var resolved=await _identity.ResolveFriendInviteAsync(
            request.ActorId,request.TargetActorId,request.TargetName).ConfigureAwait(false);
        long target=resolved.TargetCharacterId;
        if(resolved.Status!=FriendStoreStatus.Success)
        {
            QueueResult(request,(FriendResultStatus)resolved.Status,target,router);
            return;
        }
        if(!router.TryGetCurrentBinding(target,out var targetBinding))
        {
            QueueResult(request,FriendResultStatus.TargetUnavailable,target,router);
            return;
        }
        if(_invitesByTarget.ContainsKey(target))
        {
            QueueResult(request,FriendResultStatus.TargetHasPendingInvite,target,router);
            return;
        }
        if(!router.TryGetPlayerIdentity(
            sender.ActorId,sender.ConnectionEpoch,out var requesterIdentity))
        {
            QueueResult(request,FriendResultStatus.Conflict,target,router);
            return;
        }

        var pending=new PendingInvite(
            sender,requesterIdentity.DisplayName,targetBinding,resolved.TargetName);
        _invitesByTarget[target]=pending;
        QueueResult(request,FriendResultStatus.Success,target,router);
        QueueInviteNotice(
            targetBinding,sender.ActorId,sender.ConnectionEpoch,
            FriendInviteState.Request,pending.RequesterName,router,true);
    }

    private async Task HandleReplyAsync(
        TrustedFriendCommand request,ConnectionBinding sender,bool accepted,
        WorldResultRouter router)
    {
        if(!_invitesByTarget.TryGetValue(sender.ActorId,out var pending)||
           pending.Requester.ActorId!=request.TargetActorId)
        {
            QueueResult(request,FriendResultStatus.InviteMissing,request.TargetActorId,router);
            return;
        }
        if(!SameConnection(pending.Target,sender))
        {
            _invitesByTarget.Remove(sender.ActorId);
            QueueResult(request,FriendResultStatus.InviteMissing,request.TargetActorId,router);
            return;
        }
        _invitesByTarget.Remove(sender.ActorId);

        if(!router.TryGetCurrentBinding(pending.Requester.ActorId,out var requester)||
           !SameConnection(requester,pending.Requester))
        {
            QueueResult(request,FriendResultStatus.TargetUnavailable,pending.Requester.ActorId,router);
            return;
        }

        if(!accepted)
        {
            QueueResult(request,FriendResultStatus.Success,pending.Requester.ActorId,router);
            QueueInviteNotice(
                requester,sender.ActorId,sender.ConnectionEpoch,
                FriendInviteState.Rejected,pending.TargetName,router,true);
            return;
        }

        var added=await _identity.AddFriendAsync(
            sender.ActorId,pending.Requester.ActorId).ConfigureAwait(false);
        QueueResult(request,(FriendResultStatus)added.Status,pending.Requester.ActorId,router);
        if(added.Status!=FriendStoreStatus.Success)return;

        QueueInviteNotice(
            requester,sender.ActorId,sender.ConnectionEpoch,
            FriendInviteState.Accepted,pending.TargetName,router,true);
        await QueueSnapshotAsync(sender,router).ConfigureAwait(false);
        await QueueSnapshotAsync(requester,router).ConfigureAwait(false);
    }

    private static bool SameConnection(ConnectionBinding left,ConnectionBinding right)=>
        left.ActorId==right.ActorId&&left.ConnectionEpoch==right.ConnectionEpoch;

    private static void QueueResult(
        TrustedFriendCommand request,FriendResultStatus status,long target,
        WorldResultRouter router)
    {
        var send=router.QueueFriendResult(request,new(
            request.RequestMapEpoch,request.FriendSequence,request.Action,status,Math.Max(0,target)));
        if(send is GatewaySendStatus.Closed or GatewaySendStatus.GenerationMismatch)return;
        if(send==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Friend result queue backpressured");
    }

    private static void QueueInviteNotice(
        ConnectionBinding recipient,long actorId,long actorConnectionEpoch,
        FriendInviteState state,string name,WorldResultRouter router,bool failOnBackpressure)
    {
        var status=router.QueueFriendInvite(recipient,new(
            recipient.MapEpoch,actorId,actorConnectionEpoch,state,name));
        if(status==GatewaySendStatus.Backpressured&&failOnBackpressure)
            throw new InvalidOperationException("Friend invite queue backpressured");
    }

    private async Task QueueSnapshotAsync(
        ConnectionBinding recipient,WorldResultRouter router,long forcedOfflineActorId=0,
        bool failOnBackpressure=true)
    {
        var friends=await _identity.GetFriendsAsync(recipient.ActorId).ConfigureAwait(false);
        var entries=friends.Select(friend=>
        {
            bool online=friend.CharacterId!=forcedOfflineActorId&&
                router.TryGetCurrentBinding(friend.CharacterId,out _);
            return new FriendEntryWire(friend.CharacterId,friend.Name,online);
        }).ToArray();
        var status=router.QueueFriendSnapshot(
            recipient,new FriendSnapshotWire(recipient.MapEpoch,entries));
        if(status==GatewaySendStatus.Backpressured&&failOnBackpressure)
            throw new InvalidOperationException("Friend snapshot queue backpressured");
    }
}
