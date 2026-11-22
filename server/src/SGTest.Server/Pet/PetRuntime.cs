using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Pet;

/// <summary>
/// Authoritative world identity for an active pet. X/Y is the server-owned spawn
/// baseline only; independent movement/follow/pathing/combat remains a later
/// source-backed behavior gate rather than being guessed here.
/// </summary>
public readonly record struct PetWorldPresence(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    long PetUid,int DefinitionId,int EvolutionId,int Level,double X,double Y);

public readonly record struct PetWorldStateUpdate(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    long PetUid,int DefinitionId,int EvolutionId,int Level,bool Present);

/// <summary>
/// Durable pet roster/summon boundary. SQLite owns pet instances and active state;
/// the single AOI owner receives only committed active/inactive presentation state.
/// Independent follow/combat behavior is intentionally a later slice.
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
        EnsureWorldPosted(router,ToWorldUpdate(binding,pets));
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
        EnsureWorldPosted(router,ToWorldUpdate(after,pets));
    }

    public bool TryDetach(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            return false;
        return router.TryPostPetWorldState(new(
            binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,0,0,0,0,false));
    }

    private static PetSnapshotWire ToSnapshot(
        long mapEpoch,IReadOnlyList<PetInstanceRecord> pets)
    {
        long active=pets.FirstOrDefault(x=>x.IsActive)?.PetUid??0;
        return new(mapEpoch,active,pets.Select(x=>new PetSnapshotEntryWire(
            x.PetUid,x.DefinitionId,x.EvolutionId,x.Level,x.Experience)).ToArray());
    }

    private static PetWorldStateUpdate ToWorldUpdate(
        ConnectionBinding binding,IReadOnlyList<PetInstanceRecord> pets)
    {
        var active=pets.SingleOrDefault(x=>x.IsActive);
        return active is null
            ?new(binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,0,0,0,0,false)
            :new(binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
                active.PetUid,active.DefinitionId,active.EvolutionId,active.Level,true);
    }

    private static void EnsureWorldPosted(
        WorldResultRouter router,PetWorldStateUpdate update)
    {
        if(!router.TryPostPetWorldState(update))
            throw new InvalidOperationException("Pet AOI presentation queue is full");
    }

    private static void EnsureQueued(GatewaySendStatus status)
    {
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Reliable pet output queue is full");
    }
}
