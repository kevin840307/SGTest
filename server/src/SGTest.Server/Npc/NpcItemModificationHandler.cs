using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;
using SGTest.Server.Wire;

namespace SGTest.Server.Npc;

/// <summary>
/// NPC equipment repair and enhancement command settlement after the trusted
/// authenticated transport/gameplay binding. Shares the authenticated socket's gameplay gate with
/// trades, pickups and map transfers; no independent transport or persistence.
/// </summary>
internal sealed class NpcItemModificationHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly NpcRepairCatalog? _repairs;
    private readonly IReadOnlyDictionary<int,RepairItemRule>? _repairItemRules;
    private readonly NpcEnhancementCatalog? _enhancers;
    private readonly IReadOnlyDictionary<int,EquipmentEnhancementItemRule>? _enhancementItemRules;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal NpcItemModificationHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat, long pickupWorldGeneration,
        NpcRepairCatalog? repairs, IReadOnlyDictionary<int,RepairItemRule>? repairItemRules,
        NpcEnhancementCatalog? enhancers,
        IReadOnlyDictionary<int,EquipmentEnhancementItemRule>? enhancementItemRules,
        WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _pickupWorldGeneration = pickupWorldGeneration;
        _repairs = repairs;
        _repairItemRules = repairItemRules;
        _enhancers = enhancers;
        _enhancementItemRules = enhancementItemRules;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleRepairAsync(TrustedRepairCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                if(request.Kind==TrustedRepairCommandKind.Open)
                {
                    var stale=_router.QueueRepairSnapshot(
                        request,RepairOpenStatus.StaleGeneration,null);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale repair snapshot could not be queued: "+stale);
                }
                else
                {
                    var staleResult=new RepairResult(
                        RepairStatus.StaleGeneration,request.NpcSpawnId,
                        request.ItemUid,0,RepairItemLocation.None,0,0,0,0);
                    var stale=_router.QueueRepairResult(request,staleResult);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale repair result could not be queued: "+stale);
                }
                return;
            }

            bool dead=_combat?.IsIncapacitated(actor) ?? actor.CombatDead;
            RepairNpcAccessStatus access=dead
                ? RepairNpcAccessStatus.PlayerDead
                : _repairs is null||_repairItemRules is null
                    ? RepairNpcAccessStatus.UnknownRepair
                    : _repairs.Access(actor,request.NpcSpawnId);

            if(request.Kind==TrustedRepairCommandKind.Open)
            {
                RepairOpenStatus status=access switch
                {
                    RepairNpcAccessStatus.Allowed=>RepairOpenStatus.Opened,
                    RepairNpcAccessStatus.UnknownRepair=>RepairOpenStatus.UnknownRepair,
                    RepairNpcAccessStatus.TooFar=>RepairOpenStatus.TooFar,
                    RepairNpcAccessStatus.PlayerDead=>RepairOpenStatus.PlayerDead,
                    _=>RepairOpenStatus.StaleGeneration
                };
                RepairPreview? preview=null;
                if(status==RepairOpenStatus.Opened)
                {
                    if(_repairs is null||_repairItemRules is null||
                       !_repairs.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                        throw new InvalidOperationException(
                            "Repair rule vanished after access check");
                    var rule=new TrustedRepairNpcRule(
                        point.NpcSpawnId,point.RepairRate,point.SourceReference);
                    preview=await _identity.ListRepairPreviewAsync(
                        actor.ActorId,rule,_repairItemRules).ConfigureAwait(false);
                }
                var queued=_router.QueueRepairSnapshot(request,status,preview);
                if(queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable repair snapshot could not be queued: "+queued);
                return;
            }

            RepairResult result;
            if(access!=RepairNpcAccessStatus.Allowed)
            {
                var rejected=access switch
                {
                    RepairNpcAccessStatus.UnknownRepair=>RepairStatus.UnknownRepair,
                    RepairNpcAccessStatus.TooFar=>RepairStatus.TooFar,
                    RepairNpcAccessStatus.PlayerDead=>RepairStatus.PlayerDead,
                    _=>RepairStatus.StaleGeneration
                };
                result=new(rejected,request.NpcSpawnId,request.ItemUid,
                    0,RepairItemLocation.None,0,0,0,0);
            }
            else
            {
                if(_repairs is null||_repairItemRules is null||
                   !_repairs.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                    throw new InvalidOperationException(
                        "Repair rule vanished after access check");
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                var context=new TrustedRepairContext(
                    actor.ActorId,durableConnection,actor.MapEpoch,
                    request.RepairSequence);
                var rule=new TrustedRepairNpcRule(
                    point.NpcSpawnId,point.RepairRate,point.SourceReference);
                result=await _identity.RepairItemAsync(
                    context,RepairAccessStatus.Allowed,rule,
                    _repairItemRules,request.ItemUid).ConfigureAwait(false);
            }
            var sent=_router.QueueRepairResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable repair result could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleEnhancementAsync(TrustedEnhancementCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                if(request.Kind==TrustedEnhancementCommandKind.Open)
                {
                    var stale=_router.QueueEnhancementSnapshot(
                        request,EnhancementOpenStatus.StaleGeneration,null);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale enhancement snapshot could not be queued: "+
                            stale);
                }
                else
                {
                    var staleResult=new EnhancementResult(
                        EnhancementStatus.StaleGeneration,
                        request.NpcSpawnId,request.ItemUid,0,
                        EnhancementItemLocation.None,0,0,0,false,0,0,0,0);
                    var stale=_router.QueueEnhancementResult(request,staleResult);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale enhancement result could not be queued: "+
                            stale);
                }
                return;
            }

            bool dead=_combat?.IsIncapacitated(actor) ?? actor.CombatDead;
            EnhancementNpcAccessStatus access=dead
                ? EnhancementNpcAccessStatus.PlayerDead
                : _enhancers is null||_enhancementItemRules is null
                    ? EnhancementNpcAccessStatus.UnknownEnhancer
                    : _enhancers.Access(actor,request.NpcSpawnId);

            if(request.Kind==TrustedEnhancementCommandKind.Open)
            {
                EnhancementOpenStatus status=access switch
                {
                    EnhancementNpcAccessStatus.Allowed=>
                        EnhancementOpenStatus.Opened,
                    EnhancementNpcAccessStatus.UnknownEnhancer=>
                        EnhancementOpenStatus.UnknownEnhancer,
                    EnhancementNpcAccessStatus.TooFar=>
                        EnhancementOpenStatus.TooFar,
                    EnhancementNpcAccessStatus.PlayerDead=>
                        EnhancementOpenStatus.PlayerDead,
                    _=>EnhancementOpenStatus.StaleGeneration
                };
                EnhancementPreview? preview=null;
                if(status==EnhancementOpenStatus.Opened)
                {
                    if(_enhancers is null||_enhancementItemRules is null||
                       !_enhancers.TryGet(
                           actor.MapId,request.NpcSpawnId,out var point))
                        throw new InvalidOperationException(
                            "Enhancement rule vanished after access check");
                    var rule=new TrustedEnhancementNpcRule(
                        point.NpcSpawnId,point.SourceReference);
                    preview=await _identity.ListEnhancementPreviewAsync(
                        actor.ActorId,rule,_enhancementItemRules)
                        .ConfigureAwait(false);
                }
                var queued=_router.QueueEnhancementSnapshot(request,status,preview);
                if(queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable enhancement snapshot could not be queued: "+
                        queued);
                return;
            }

            EnhancementResult result;
            if(access!=EnhancementNpcAccessStatus.Allowed)
            {
                var rejected=access switch
                {
                    EnhancementNpcAccessStatus.UnknownEnhancer=>
                        EnhancementStatus.UnknownEnhancer,
                    EnhancementNpcAccessStatus.TooFar=>
                        EnhancementStatus.TooFar,
                    EnhancementNpcAccessStatus.PlayerDead=>
                        EnhancementStatus.PlayerDead,
                    _=>EnhancementStatus.StaleGeneration
                };
                result=new(rejected,request.NpcSpawnId,request.ItemUid,0,
                    EnhancementItemLocation.None,0,0,0,false,0,0,0,0);
            }
            else
            {
                if(_enhancers is null||_enhancementItemRules is null||
                   !_enhancers.TryGet(
                       actor.MapId,request.NpcSpawnId,out var point))
                    throw new InvalidOperationException(
                        "Enhancement rule vanished after access check");
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                var context=new TrustedEnhancementContext(
                    actor.ActorId,durableConnection,actor.MapEpoch,
                    request.EnhancementSequence);
                var rule=new TrustedEnhancementNpcRule(
                    point.NpcSpawnId,point.SourceReference);
                result=await _identity.EnhanceItemAsync(
                    context,EnhancementAccessStatus.Allowed,rule,
                    _enhancementItemRules,request.ItemUid)
                    .ConfigureAwait(false);
            }
            var sent=_router.QueueEnhancementResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable enhancement result could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }
}
