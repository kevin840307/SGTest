using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Pet;

/// <summary>
/// Authoritative world identity for an active pet. X/Y is server-owned world
/// position consumed by AOI and source-gated pet combat. Independent follow/pathing
/// behavior remains a separate source-backed gate rather than being guessed here.
/// </summary>
public readonly record struct PetWorldPresence(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    long PetUid,int DefinitionId,int EvolutionId,int Level,double X,double Y);

public readonly record struct PetWorldStateUpdate(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    long PetUid,int DefinitionId,int EvolutionId,int Level,bool Present);

/// <summary>
/// Durable Pet roster, summon lifecycle and physical equipment ownership. SQLite
/// owns Pet instances/active state/ItemUid location. Equipment legality is
/// fail-closed and must come from an operator-verified manifest; exact HK slot
/// semantics and stat transforms remain source-gated rather than guessed here.
/// </summary>
public sealed class PetCoordinator
{
    private readonly SqliteIdentityStore _identity;
    private readonly PetGrowthCurve? _growth;
    private readonly IReadOnlyDictionary<int,PetEquipmentRule>? _equipmentRules;

    public PetCoordinator(
        SqliteIdentityStore identity,PetGrowthCurve? growth=null,
        IReadOnlyDictionary<int,PetEquipmentRule>? equipmentRules=null)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
        _growth=growth;
        _equipmentRules=equipmentRules;
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        var pets=await _identity.GetPetsAsync(binding.ActorId).ConfigureAwait(false);
        var equipment=await _identity.ListPetEquippedItemsAsync(
            binding.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueuePetSnapshot(binding,ToSnapshot(binding.MapEpoch,pets)));
        EnsureQueued(router.QueuePetEquipmentSnapshot(
            binding,ToEquipmentSnapshot(binding.MapEpoch,equipment)));
        EnsureWorldPosted(router,ToWorldUpdate(binding,pets));
    }

    public async Task<PetExperienceGrantOutcome> GrantExperienceAsync(
        ConnectionBinding binding,long petUid,long amount,string grantKey,
        WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0||
           !router.TryGetCurrentBinding(binding.ActorId,out var current)||
           current.ConnectionEpoch!=binding.ConnectionEpoch||
           current.MapEpoch!=binding.MapEpoch)
            throw new InvalidOperationException(
                "Pet EXP grant requires the current trusted actor generation");
        return await GrantCommittedExperienceAsync(
            binding,petUid,amount,grantKey,router).ConfigureAwait(false);
    }

    /// <summary>
    /// Trusted post-combat settlement. The caller has already captured an exact
    /// authoritative Pet/owner generation at the lethal event, so disconnecting
    /// before the SQLite worker drains must not erase the earned grant. A current
    /// matching connection receives the refreshed roster; a superseded generation
    /// simply observes the durable result on its next attach.
    /// </summary>
    internal async Task<PetExperienceGrantOutcome> GrantCommittedExperienceAsync(
        ConnectionBinding binding,long petUid,long amount,string grantKey,
        WorldResultRouter router,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(_growth is null)
            throw new InvalidOperationException(
                "Pet growth is unavailable without an audited growth manifest");
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));

        var outcome=await _identity.ApplyPetExperienceAsync(
            binding.ActorId,petUid,amount,grantKey,_growth,cancellationToken)
            .ConfigureAwait(false);
        if(outcome.Status is not (PetExperienceGrantStatus.Applied or
            PetExperienceGrantStatus.AlreadyProcessed))
            return outcome;

        if(!router.TryGetCurrentBinding(binding.ActorId,out var after)||
           after.ConnectionEpoch!=binding.ConnectionEpoch||
           after.MapEpoch!=binding.MapEpoch)
            return outcome;
        var roster=await _identity.GetPetsAsync(
            binding.ActorId,cancellationToken).ConfigureAwait(false);
        EnsureQueued(router.QueuePetSnapshot(after,ToSnapshot(after.MapEpoch,roster)));
        EnsureWorldPosted(router,ToWorldUpdate(after,roster));
        return outcome;
    }

    public async Task HandleAsync(TrustedPetCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.TryGetCurrentBinding(request.ActorId,out var binding)||
           binding.ActorId!=request.ActorId||
           binding.ConnectionEpoch!=request.ConnectionEpoch||
           binding.MapEpoch!=request.RequestMapEpoch)
            return;

        PetActionOutcome outcome;
        if(request.Action==PetCommandAction.Refresh)
        {
            var before=await _identity.GetPetsAsync(request.ActorId).ConfigureAwait(false);
            outcome=new(
                PetActionStatus.Applied,
                before.FirstOrDefault(x=>x.IsActive)?.PetUid??0);
        }
        else if(request.Action is PetCommandAction.Equip or PetCommandAction.Unequip)
        {
            outcome=await _identity.ApplyPetEquipmentActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.PetSequence,
                request.Action==PetCommandAction.Equip
                    ?PetActionKind.Equip:PetActionKind.Unequip,
                request.PetUid,request.ItemUid,
                request.Action==PetCommandAction.Equip?_equipmentRules:null)
                .ConfigureAwait(false);
        }
        else
        {
            outcome=await _identity.ApplyPetActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.PetSequence,
                request.Action==PetCommandAction.Summon
                    ?PetActionKind.Summon:PetActionKind.Recall,
                request.PetUid).ConfigureAwait(false);
        }

        EnsureQueued(router.QueuePetResult(request,new(
            request.RequestMapEpoch,request.PetSequence,request.Action,
            (PetResultStatus)outcome.Status,request.PetUid,outcome.ActivePetUid,
            request.ItemUid,outcome.SlotId,outcome.ReplacedItemUid)));

        if(!router.TryGetCurrentBinding(request.ActorId,out var after)||
           after.ConnectionEpoch!=request.ConnectionEpoch||
           after.MapEpoch!=request.RequestMapEpoch)
            return;
        var pets=await _identity.GetPetsAsync(request.ActorId).ConfigureAwait(false);
        var equipment=await _identity.ListPetEquippedItemsAsync(
            request.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueuePetSnapshot(after,ToSnapshot(after.MapEpoch,pets)));
        EnsureQueued(router.QueuePetEquipmentSnapshot(
            after,ToEquipmentSnapshot(after.MapEpoch,equipment)));
        if(request.Action is PetCommandAction.Summon or
           PetCommandAction.Recall or PetCommandAction.Refresh)
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

    private static PetEquipmentSnapshotWire ToEquipmentSnapshot(
        long mapEpoch,IReadOnlyList<PetEquippedItem> equipment) =>
        new(mapEpoch,equipment.Select(x=>new PetEquipmentEntryWire(
            x.PetUid,x.ItemUid,x.SlotId,x.ItemDefinitionId,x.SourceDropId,
            x.EnhancementLevel,x.DurabilityRaw??-1)).ToArray());

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
