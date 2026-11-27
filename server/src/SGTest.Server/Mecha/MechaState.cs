using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Mecha;

/// <summary>
/// Player-attached authoritative Mecha presentation state. Recovered2 evidence
/// keeps the driven Mecha on the player model (type/body/weapon/engine) rather
/// than as an independently positioned world entity, so AOI currently carries
/// committed mounted identity. Exact HK part visuals remain source-gated.
/// </summary>
public readonly record struct MechaWorldStateUpdate(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    bool Mounted,long MechaUid,int DefinitionId,int Level,
    int WeaponDefinitionId=0,int EngineDefinitionId=0);

/// <summary>
/// Server-authoritative Mecha roster, mount lifecycle and physical equipment
/// ownership. Equipment rules are fail-closed and must come from a verified
/// manifest. Exact HK stat transforms and visual part mapping remain disabled
/// until source-backed rules are available.
/// </summary>
public sealed class MechaCoordinator
{
    private readonly SqliteIdentityStore _identity;
    private readonly IReadOnlyDictionary<int,MechaEquipmentRule>? _equipmentRules;
    private readonly MechaBatteryCatalog? _batteryRules;

    public MechaCoordinator(
        SqliteIdentityStore identity,
        IReadOnlyDictionary<int,MechaEquipmentRule>? equipmentRules=null,
        MechaBatteryCatalog? batteryRules=null)
    {
        _identity=identity??throw new ArgumentNullException(nameof(identity));
        _equipmentRules=equipmentRules;
        _batteryRules=batteryRules;
    }

    public async Task AttachAsync(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            throw new ArgumentOutOfRangeException(nameof(binding));
        var mechas=await _identity.GetMechasAsync(binding.ActorId).ConfigureAwait(false);
        var equipment=await _identity.ListMechaEquippedItemsAsync(
            binding.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueueMechaSnapshot(
            binding,ToSnapshot(binding.MapEpoch,mechas)));
        EnsureQueued(router.QueueMechaEquipmentSnapshot(
            binding,ToEquipmentSnapshot(binding.MapEpoch,equipment)));
        EnsureWorldPosted(router,ToWorldUpdate(binding,mechas,equipment));
    }

    public async Task HandleAsync(TrustedMechaCommand request,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.TryGetCurrentBinding(request.ActorId,out var binding)||
           binding.ActorId!=request.ActorId||
           binding.ConnectionEpoch!=request.ConnectionEpoch||
           binding.MapEpoch!=request.RequestMapEpoch)
            return;

        MechaActionOutcome outcome;
        if(request.Action==MechaCommandAction.Refresh)
        {
            var before=await _identity.GetMechasAsync(request.ActorId).ConfigureAwait(false);
            outcome=new(
                MechaActionStatus.Applied,
                before.FirstOrDefault(x=>x.IsMounted)?.MechaUid??0);
        }
        else if(request.Action==MechaCommandAction.ChargeBattery)
        {
            outcome=await _identity.ApplyMechaBatteryChargeAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.MechaSequence,request.MechaUid,request.ItemUid,_batteryRules)
                .ConfigureAwait(false);
        }
        else if(request.Action is MechaCommandAction.Equip or MechaCommandAction.Unequip)
        {
            outcome=await _identity.ApplyMechaEquipmentActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.MechaSequence,
                request.Action==MechaCommandAction.Equip
                    ?MechaActionKind.Equip:MechaActionKind.Unequip,
                request.MechaUid,request.ItemUid,
                request.Action==MechaCommandAction.Equip?_equipmentRules:null)
                .ConfigureAwait(false);
        }
        else
        {
            outcome=await _identity.ApplyMechaActionAsync(
                request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.MechaSequence,
                request.Action==MechaCommandAction.Mount
                    ?MechaActionKind.Mount:MechaActionKind.Dismount,
                request.MechaUid).ConfigureAwait(false);
        }

        EnsureQueued(router.QueueMechaResult(request,new(
            request.RequestMapEpoch,request.MechaSequence,request.Action,
            (MechaResultStatus)outcome.Status,request.MechaUid,outcome.MountedMechaUid,
            request.ItemUid,outcome.SlotId,outcome.ReplacedItemUid,outcome.RemainingQuantity)));

        if(!router.TryGetCurrentBinding(request.ActorId,out var after)||
           after.ConnectionEpoch!=request.ConnectionEpoch||
           after.MapEpoch!=request.RequestMapEpoch)
            return;
        var mechas=await _identity.GetMechasAsync(request.ActorId).ConfigureAwait(false);
        var equipment=await _identity.ListMechaEquippedItemsAsync(
            request.ActorId).ConfigureAwait(false);
        EnsureQueued(router.QueueMechaSnapshot(
            after,ToSnapshot(after.MapEpoch,mechas)));
        EnsureQueued(router.QueueMechaEquipmentSnapshot(
            after,ToEquipmentSnapshot(after.MapEpoch,equipment)));
        if(request.Action is MechaCommandAction.Mount or
           MechaCommandAction.Dismount or MechaCommandAction.Refresh or
           MechaCommandAction.Equip or MechaCommandAction.Unequip)
            EnsureWorldPosted(router,ToWorldUpdate(after,mechas,equipment));
    }

    public bool TryDetach(ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(binding.ActorId<=0||binding.ConnectionEpoch<=0||binding.MapEpoch<=0)
            return false;
        return router.TryPostMechaWorldState(new(
            binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
            false,0,0,0,0,0));
    }

    private MechaSnapshotWire ToSnapshot(
        long mapEpoch,IReadOnlyList<MechaInstanceRecord> mechas)
    {
        long mounted=mechas.FirstOrDefault(x=>x.IsMounted)?.MechaUid??0;
        return new(mapEpoch,mounted,mechas.Select(x=>new MechaSnapshotEntryWire(
            x.MechaUid,x.DefinitionId,x.Level,x.Experience,x.BatteryCharge,
            _batteryRules?.CapacityFor(x.DefinitionId)??0)).ToArray());
    }

    private static MechaEquipmentSnapshotWire ToEquipmentSnapshot(
        long mapEpoch,IReadOnlyList<MechaEquippedItem> equipment) =>
        new(mapEpoch,equipment.Select(x=>new MechaEquipmentEntryWire(
            x.MechaUid,x.ItemUid,x.SlotId,x.ItemDefinitionId,x.SourceDropId,
            x.EnhancementLevel,x.DurabilityRaw??-1)).ToArray());

    private static MechaWorldStateUpdate ToWorldUpdate(
        ConnectionBinding binding,IReadOnlyList<MechaInstanceRecord> mechas,
        IReadOnlyList<MechaEquippedItem> equipment)
    {
        var mounted=mechas.SingleOrDefault(x=>x.IsMounted);
        if(mounted is null)
            return new(binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
                false,0,0,0,0,0);
        int weapon=equipment.FirstOrDefault(x=>
            x.MechaUid==mounted.MechaUid&&x.SlotId==1)?.ItemDefinitionId??0;
        int engine=equipment.FirstOrDefault(x=>
            x.MechaUid==mounted.MechaUid&&x.SlotId==2)?.ItemDefinitionId??0;
        return new(binding.ActorId,binding.ConnectionEpoch,binding.MapEpoch,
            true,mounted.MechaUid,mounted.DefinitionId,mounted.Level,weapon,engine);
    }

    private static void EnsureWorldPosted(
        WorldResultRouter router,MechaWorldStateUpdate update)
    {
        if(!router.TryPostMechaWorldState(update))
            throw new InvalidOperationException("Mecha AOI presentation queue is full");
    }

    private static void EnsureQueued(GatewaySendStatus status)
    {
        if(status==GatewaySendStatus.Backpressured)
            throw new InvalidOperationException("Reliable Mecha output queue is full");
    }
}
