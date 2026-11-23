using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Mecha;

/// <summary>
/// Server-authoritative Mecha roster and mount lifecycle. This first vertical
/// slice deliberately owns only durable identity/mounted state. Movement speed,
/// stat transforms, parts/cards and visual outlook stay disabled until backed by
/// verified HK/Old data rather than guessed constants.
/// </summary>
public sealed class MechaCoordinator
{
    private readonly SqliteIdentityStore _identity;

    public MechaCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        var mechas=await _identity.GetMechasAsync(binding.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueueMechaSnapshot(
            binding,ToSnapshot(binding.MapEpoch,mechas)));
    }

    public async Task HandleAsync(TrustedMechaCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.TryGetCurrentBinding(request.ActorId,out var binding)||
           binding.ActorId!=request.ActorId||
           binding.ConnectionEpoch!=request.ConnectionEpoch||
           binding.MapEpoch!=request.RequestMapEpoch)
            return;

        MechaResultStatus status;
        long mounted;
        if(request.Action==MechaCommandAction.Refresh)
        {
            var before=await _identity.GetMechasAsync(request.ActorId).ConfigureAwait(false);
            mounted=before.FirstOrDefault(x=>x.IsMounted)?.MechaUid??0;
            status=MechaResultStatus.Applied;
        }
        else
        {
            var outcome=await _identity.ApplyMechaActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.MechaSequence,
                request.Action==MechaCommandAction.Mount
                    ?MechaActionKind.Mount:MechaActionKind.Dismount,
                request.MechaUid).ConfigureAwait(false);
            status=(MechaResultStatus)outcome.Status;
            mounted=outcome.MountedMechaUid;
        }

        EnsureQueued(router.QueueMechaResult(request,new(
            request.RequestMapEpoch,request.MechaSequence,request.Action,status,
            request.MechaUid,mounted)));

        if(!router.TryGetCurrentBinding(request.ActorId,out var after)||
           after.ConnectionEpoch!=request.ConnectionEpoch||
           after.MapEpoch!=request.RequestMapEpoch)
            return;
        var mechas=await _identity.GetMechasAsync(request.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueueMechaSnapshot(
            after,ToSnapshot(after.MapEpoch,mechas)));
    }

    private static MechaSnapshotWire ToSnapshot(
        long mapEpoch,IReadOnlyList<MechaInstanceRecord> mechas)
    {
        long mounted=mechas.FirstOrDefault(x=>x.IsMounted)?.MechaUid??0;
        return new(mapEpoch,mounted,mechas.Select(x=>new MechaSnapshotEntryWire(
            x.MechaUid,x.DefinitionId,x.Level,x.Experience)).ToArray());
    }

    private static void EnsureQueued(GatewaySendStatus status)
    {
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Reliable Mecha output queue is full");
    }
}
