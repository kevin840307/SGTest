using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;

namespace SGTest.Server.Friend;

/// <summary>
/// Durable bilateral friendship with live presence derived only from current
/// authenticated Gateway bindings. SQLite never stores online/offline state.
/// </summary>
public sealed class FriendCoordinator
{
    private readonly SqliteIdentityStore _identity;
    private readonly SemaphoreSlim _serial=new(1,1);

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
            // A freshly constructed Client WorldState already owns an empty friend set.
            // Avoid injecting an unnecessary social frame ahead of WorldReady for
            // characters with no durable relationships.
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

            FriendStoreStatus status;
            long target=request.TargetActorId;
            switch(request.Action)
            {
                case FriendCommandAction.Add:
                    (status,target)=await _identity.AddFriendAsync(
                        request.ActorId,request.TargetActorId,request.TargetName)
                        .ConfigureAwait(false);
                    break;
                case FriendCommandAction.Remove:
                    status=await _identity.RemoveFriendAsync(
                        request.ActorId,request.TargetActorId).ConfigureAwait(false);
                    break;
                case FriendCommandAction.Refresh:
                    status=FriendStoreStatus.Success;
                    target=0;
                    break;
                default:
                    status=FriendStoreStatus.Conflict;
                    break;
            }

            var send=router.QueueFriendResult(request,new(
                request.RequestMapEpoch,request.FriendSequence,request.Action,
                (FriendResultStatus)status,target));
            if(send is GatewaySendStatus.Closed or GatewaySendStatus.GenerationMismatch)
                return;
            if(send==GatewaySendStatus.Backpressured)
                throw new InvalidOperationException("Friend result queue backpressured");

            if(status!=FriendStoreStatus.Success)return;
            await QueueSnapshotAsync(sender,router).ConfigureAwait(false);
            if(target>0&&router.TryGetCurrentBinding(target,out var peer))
                await QueueSnapshotAsync(peer,router).ConfigureAwait(false);
        }
        finally{_serial.Release();}
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
