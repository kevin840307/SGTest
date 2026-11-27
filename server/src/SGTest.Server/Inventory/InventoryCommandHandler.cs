using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Visibility;

namespace SGTest.Server.Inventory;

/// <summary>
/// Trusted inventory acquisition, equipment transfer and item discard
/// for a single authenticated session. Retains the original shared gameplay
/// gate and authoritative SQLite transaction owner, including UID and epoch checks.
/// </summary>
internal sealed class InventoryCommandHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly IReadOnlyDictionary<int,EquipmentRule>? _equipmentRules;
    private readonly IReadOnlyDictionary<int,EquipmentCombatRule>? _equipmentCombatRules;
    private readonly CharacterProgressionRules? _progressionRules;
    private readonly long _pickupWorldGeneration;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal InventoryCommandHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat, IReadOnlyDictionary<int,EquipmentRule>? equipmentRules,
        long pickupWorldGeneration, WorldResultRouter router, SemaphoreSlim gameplayGate,
        IReadOnlyDictionary<int,EquipmentCombatRule>? equipmentCombatRules = null,
        CharacterProgressionRules? progressionRules = null)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _equipmentRules = equipmentRules;
        _equipmentCombatRules = equipmentCombatRules;
        _progressionRules = progressionRules;
        _pickupWorldGeneration = pickupWorldGeneration;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandlePickupAsync(TrustedGroundPickup request)
    {
        var binding = new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_router.IsCurrentBinding(binding))
                return;
            ActorSnapshot? snapshot = await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            PickupResult result;
            if (snapshot is not { } actor ||
                actor.ConnectionEpoch != request.ConnectionEpoch ||
                actor.MapEpoch != request.RequestMapEpoch ||
                actor.ActorId != request.ActorId)
                result = new(PickupStatus.StaleGeneration,request.DropId);
            else if (_combat?.IsIncapacitated(actor) ?? actor.CombatDead)
                result = new(PickupStatus.PlayerDead,request.DropId);
            else
                result = await _identity.ClaimGroundDropAsync(
                    new TrustedPickupContext(actor.ActorId,
                        _pickupWorldGeneration == 0
                            ? actor.ConnectionEpoch // isolated fixture only
                            : SqliteIdentityStore.ComposePickupConnectionEpoch(
                                _pickupWorldGeneration,actor.ConnectionEpoch),
                        actor.MapEpoch,
                        request.PickupSequence,actor.MapId,actor.X,actor.Y),
                    request.DropId).ConfigureAwait(false);

            // SQLite commits first. Never remove on rejection or
            // client intent alone. A failed notification is terminal,
            // not a fabricated successful response.
            if (result.Status == PickupStatus.Granted &&
                !_router.TryPostGroundDropClaimed(result.DropId))
                throw new IOException(
                    "Committed drop claimed but shared AOI notification was full");

            var sent = _router.QueueGroundPickupResult(request,result);
            if (sent != GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable pickup result could not be queued: " + sent);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleEquipmentAsync(TrustedEquipmentCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            var transferKind=request.Kind==TrustedEquipmentCommandKind.Equip
                ? EquipmentTransferKind.Equip:EquipmentTransferKind.Unequip;
            EquipmentTransferResult result;
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                result=new(EquipmentTransferStatus.StaleGeneration,
                    transferKind,request.ItemUid,0,0,0);
            }
            else
            {
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                var context=new TrustedEquipmentContext(
                    actor.ActorId,durableConnection,actor.MapEpoch,
                    request.EquipmentSequence);
                if(request.Kind==TrustedEquipmentCommandKind.Equip)
                {
                    result=_equipmentRules is null
                        ? new(EquipmentTransferStatus.NoRules,
                            EquipmentTransferKind.Equip,request.ItemUid,0,0,0)
                        : await _identity.EquipInventoryItemAsync(
                            context,request.ItemUid,_equipmentRules)
                            .ConfigureAwait(false);
                }
                else
                {
                    result=await _identity.UnequipItemAsync(
                        context,request.ItemUid).ConfigureAwait(false);
                }
            }
            var sent=_router.QueueEquipmentTransferResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable equipment transfer result could not be queued: "+sent);
            if(result.Status==EquipmentTransferStatus.Applied&&
               _router.IsCurrentBinding(binding))
            {
                var equipped=await _identity.ListEquippedItemsAsync(
                    request.ActorId).ConfigureAwait(false);
                var presentation=PlayerEquipmentWorldStateUpdate.FromEquipped(
                    binding,request.EquipmentSequence,equipped);
                if(!_router.TryPostPlayerEquipmentWorldState(presentation))
                    throw new IOException("Player equipment AOI presentation queue is full");
                await RefreshEquipmentProfileAsync(binding,equipped)
                    .ConfigureAwait(false);
            }
        }
        finally { _gameplayGate.Release(); }
    }

    private async Task RefreshEquipmentProfileAsync(
        ConnectionBinding binding,IReadOnlyList<EquippedItem> equipped)
    {
        if(!_router.IsCurrentBinding(binding))return;
        var actor=await _world.TrustedGetActorAsync(binding.ActorId)
            .ConfigureAwait(false);
        if(actor is not { } current||
           current.ConnectionEpoch!=binding.ConnectionEpoch||
           current.MapEpoch!=binding.MapEpoch)
            return;

        var stored=await _identity.GetCharacterCombatProfileAsync(binding.ActorId)
            .ConfigureAwait(false);
        if(stored is null)
        {
            if(!_router.TryUpdateTrustedCombatProfile(binding,null))
                throw new IOException("Committed equipment profile lost current binding");
            return;
        }

        CharacterCombatProfileData effective=stored.Data;
        if(effective.Trust==CombatProfileTrust.ProfileOnlyVerified)
        {
            if(SourceVerifiedUnarmedCombatRules.TryPromoteEmptyEquipment(
                effective,equipped.Count,_world.TickHz,out var unarmed))
                effective=unarmed;
            else if(_equipmentCombatRules is not null&&
                    SourceVerifiedEquipmentCombatRules.TryPromote(
                        effective,equipped,_equipmentCombatRules,_world.TickHz,
                        out var equippedCombat))
                effective=equippedCombat;
        }

        bool combatEnabled=effective.Trust is
            CombatProfileTrust.Verified or CombatProfileTrust.SyntheticTest;
        if(_combat is not null)
        {
            if(combatEnabled)
            {
                if(!_combat.TryApplyLiveProfile(current,effective))
                    throw new IOException(
                        "Committed equipment could not refresh live combat profile");
            }
            else
            {
                await _combat.SuspendAndDrainAsync(
                    current.ActorId,current.ConnectionEpoch,current.MapEpoch)
                    .ConfigureAwait(false);
            }
        }

        TrustedCombatProfile? trusted=combatEnabled
            ?new(effective.Attack,effective.Defense,effective.Hit,effective.Dodge,
                effective.AttackRange,effective.AttackCooldownTicks,effective.Level)
            :null;
        if(!_router.TryUpdateTrustedCombatProfile(binding,trusted))
            throw new IOException(
                "Committed equipment could not refresh trusted attack profile");

        var experience=await _identity.GetCharacterExperienceAsync(binding.ActorId)
            .ConfigureAwait(false);
        var state=CharacterProfileProgressionHandler.BuildState(
            effective,binding.ActorId,experience?.Experience??0,_progressionRules);
        var sent=_router.QueuePlayerProfileState(binding,state);
        if(sent!=GatewaySendStatus.Enqueued)
            throw new IOException(
                "Committed equipment profile state could not be queued: "+sent);
    }

    internal async Task HandleDiscardAsync(TrustedInventoryDiscard request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            InventoryDiscardResult result;
            if(snapshot is not { } actor ||
                actor.ActorId!=request.ActorId ||
                actor.ConnectionEpoch!=request.ConnectionEpoch ||
                actor.MapEpoch!=request.RequestMapEpoch)
                result=new(InventoryDiscardStatus.StaleGeneration,request.ItemUid);
            else
            {
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                result=await _identity.DiscardInventoryItemAsync(
                    new TrustedInventoryDiscardContext(
                        actor.ActorId,durableConnection,actor.MapEpoch,
                        request.DiscardSequence),
                    request.ItemUid,request.Quantity).ConfigureAwait(false);
            }
            var sent=_router.QueueInventoryDiscardResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable inventory discard receipt could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }
}
