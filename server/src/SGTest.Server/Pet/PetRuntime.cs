using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Pet;

/// <summary>
/// Durable pet roster/summon boundary. SQLite owns pet instances and active state;
/// this coordinator only validates current connection generations and emits
/// reliable snapshots. Follow/combat presentation is intentionally a later slice.
/// </summary>
public sealed class PetCoordinator
{
    private readonly SqliteIdentityStore _identity;

    public PetCoordinator(SqliteIdentityStore identity)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        var pets=await _identity.GetPetsAsync(binding.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueuePetSnapshot(binding,ToSnapshot(binding.MapEpoch,pets)));
    }

    public async Task HandleAsync(TrustedPetCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.TryGetCurrentBinding(request.ActorId,out var binding)||
           binding.ActorId!=request.ActorId||
           binding.ConnectionEpoch!=request.ConnectionEpoch||
           binding.MapEpoch!=request.RequestMapEpoch)
            return;

        PetResultStatus status;
        long active;
        if(request.Action==PetCommandAction.Refresh)
        {
            var before=await _identity.GetPetsAsync(request.ActorId).ConfigureAwait(false);
            active=before.FirstOrDefault(x=>x.IsActive)?.PetUid??0;
            status=PetResultStatus.Applied;
        }
        else
        {
            var outcome=await _identity.ApplyPetActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.PetSequence,
                request.Action==PetCommandAction.Summon
                    ?PetActionKind.Summon:PetActionKind.Recall,
                request.PetUid).ConfigureAwait(false);
            status=(PetResultStatus)outcome.Status;
            active=outcome.ActivePetUid;
        }

        EnsureQueued(router.QueuePetResult(request,new(
            request.RequestMapEpoch,request.PetSequence,request.Action,status,
            request.PetUid,active)));

        if(!router.TryGetCurrentBinding(request.ActorId,out var after)||
           after.ConnectionEpoch!=request.ConnectionEpoch||
           after.MapEpoch!=request.RequestMapEpoch)
            return;
        var pets=await _identity.GetPetsAsync(request.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueuePetSnapshot(after,ToSnapshot(after.MapEpoch,pets)));
    }

    private static PetSnapshotWire ToSnapshot(
        long mapEpoch,IReadOnlyList<PetInstanceRecord> pets)
    {
        long active=pets.FirstOrDefault(x=>x.IsActive)?.PetUid??0;
        return new(mapEpoch,active,pets.Select(x=>new PetSnapshotEntryWire(
            x.PetUid,x.DefinitionId,x.Level,x.Experience)).ToArray());
    }

    private static void EnsureQueued(GatewaySendStatus status)
    {
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Reliable pet output queue is full");
    }
}
