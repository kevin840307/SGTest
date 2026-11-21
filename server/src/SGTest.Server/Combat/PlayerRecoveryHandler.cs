using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Visibility;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Combat;

/// <summary>
/// Authoritative player respawn and recovery-item coordination for one
/// authenticated session. HP settlement stays in PlayerCombatSettlement
/// and SQLite; this owner preserves receipt-before-AI-resume ordering.
/// </summary>
internal sealed class PlayerRecoveryHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly IReadOnlyDictionary<int,RecoveryItemRule>? _recoveryRules;
    private readonly long _pickupWorldGeneration;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal PlayerRecoveryHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat,
        IReadOnlyDictionary<int,RecoveryItemRule>? recoveryRules,
        long pickupWorldGeneration,
        WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _recoveryRules = recoveryRules;
        _pickupWorldGeneration = pickupWorldGeneration;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleRespawnAsync(TrustedPlayerRespawn request)
    {
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
        var binding = new ConnectionBinding(
            request.ActorId, request.ConnectionEpoch, request.RequestMapEpoch);
        if (!_router.IsCurrentBinding(binding))
            return;

        PlayerRespawnOutcome outcome;
        ActorSnapshot? revived = null;
        if (_combat is null)
        {
            outcome = new(PlayerRespawnStatus.CombatUnavailable, 0, 0);
        }
        else
        {
            outcome = await _combat.RespawnFullAsync(
                request.ActorId, request.ConnectionEpoch,
                request.RequestMapEpoch, request.RespawnSequence)
                .ConfigureAwait(false);

            if (outcome.Status == PlayerRespawnStatus.Accepted)
            {
                revived = await _world.TrustedSetCombatDeadAsync(
                    request.ActorId, request.ConnectionEpoch,
                    request.RequestMapEpoch, false).ConfigureAwait(false);
                if (revived is null)
                    outcome = new(PlayerRespawnStatus.StaleGeneration,
                        outcome.CurrentHp, outcome.MaxHp);
            }
        }

        var sent = _router.QueuePlayerRespawnResult(request, outcome);
        if (sent != GatewaySendStatus.Enqueued)
            throw new IOException(
                "Reliable player respawn result could not be queued: " + sent);

        if (outcome.Status == PlayerRespawnStatus.Accepted &&
            revived is { } visualActor &&
            !_router.TryPostPlayerRespawnVisual(
                visualActor,request.RespawnSequence))
            throw new AoiBackpressureException(
                "Committed respawn visual queue full; event cannot be dropped");

        // Queue the authoritative full-HP baseline BEFORE reopening AI
        // targetability, so later PlayerDamage cannot overtake RespawnResult.
        if (outcome.Status == PlayerRespawnStatus.Accepted &&
            revived is { } actor &&
            (_combat is null || !_combat.TryResume(actor)))
            throw new InvalidOperationException(
                "Combat generation could not resume after player respawn");
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleRecoveryItemAsync(TrustedRecoveryUse request)
    {
        // The World/AOI owner and existing auth binding supply ALL
        // identity fields. A client can only choose its inventory UID.
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            RecoveryUseResult result;
            ActorSnapshot? suspendedActor=null;
            if(snapshot is not { } actor ||
                actor.ActorId!=request.ActorId ||
                actor.ConnectionEpoch!=request.ConnectionEpoch ||
                actor.MapEpoch!=request.RequestMapEpoch)
            {
                result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
            }
            else if(_combat is null)
                result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
            else if(_recoveryRules is null)
                result=new(RecoveryUseStatus.NoRule,request.ItemUid);
            else
            {
                // AI reservations must be blocked and all outstanding
                // SQLite damage committed before the HP + item TX.
                await _combat.SuspendAndDrainAsync(
                    actor.ActorId,actor.ConnectionEpoch,
                    actor.MapEpoch).ConfigureAwait(false);
                suspendedActor=actor;
                if(!_combat.TryGetSuspendedHp(actor,out int hp))
                    result=new(RecoveryUseStatus.CombatUnavailable,request.ItemUid);
                else if(hp<=0)
                    result=new(RecoveryUseStatus.Dead,request.ItemUid);
                else
                {
                    long durableConnection=_pickupWorldGeneration==0
                        ? actor.ConnectionEpoch // isolated test only
                        : SqliteIdentityStore.ComposePickupConnectionEpoch(
                            _pickupWorldGeneration,actor.ConnectionEpoch);
                    result=await _identity.UseVerifiedRecoveryItemAsync(
                        new TrustedRecoveryUseContext(
                            actor.ActorId,durableConnection,
                            actor.MapEpoch,request.UseSequence,hp),
                        request.ItemUid,_recoveryRules).ConfigureAwait(false);
                    if(result.Status==RecoveryUseStatus.Granted &&
                        !_combat.TryApplyCommittedRecovery(actor,result))
                        throw new InvalidOperationException(
                            "Committed recovery diverged from reserved Combat state");
                }
            }
            var sent=_router.QueueRecoveryUseResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable recovery receipt could not be queued: "+sent);
            // Resume AI only AFTER reliable result is in the same FIFO,
            // ensuring later PlayerDamage cannot overtake HP restoration.
            if(suspendedActor is { } resumed &&
                !_combat!.TryResume(resumed))
                throw new InvalidOperationException(
                    "Recovery Combat generation could not resume");
        }
        finally { _gameplayGate.Release(); }
    }
}
