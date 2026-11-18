using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

public sealed record WorldAoiPumpOptions(
    int FlushIntervalMilliseconds = 10,
    int MaxFramesPerObserverPerPoll = 32,
    int MaxCommittedLootBatchesPerPoll = 8);

/// <summary>
/// One non-World background AOI consumer. Bounded fanout, a small idle delay,
/// no busy polling and no render-FPS dependency. WorldResultRouter remains the
/// only WorldHost.Results reader, while this pump is the only ChangedBatches
/// reader. The same authenticated Gateway reliable FIFO carries AOI and ACKs.
/// </summary>
public sealed class WorldAoiPump
{
    private readonly WorldResultRouter _router;
    private readonly WorldAoi _aoi;
    private readonly WorldAoiPumpOptions _options;
    private readonly MonsterWorldCoordinator? _monsters;
    private readonly PlayerCombatSettlement? _playerCombat;
    private readonly MonsterLootSettlement? _loot;
    private int _started;
    private long _processedBatches, _framesQueued;

    public WorldAoiPump(WorldResultRouter router, WorldAoi? aoi = null,
                        WorldAoiPumpOptions? options = null,
                        MonsterWorldCoordinator? monsters = null,
                        PlayerCombatSettlement? playerCombat = null,
                        MonsterLootSettlement? loot = null,
                        IReadOnlyList<GroundDropRecord>? initialGroundDrops = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _aoi = aoi ?? new WorldAoi();
        _options = options ?? new WorldAoiPumpOptions();
        _monsters = monsters;
        _playerCombat = playerCombat;
        _loot = loot;
        if (initialGroundDrops is { Count: > 0 })
            _aoi.SeedGroundDrops(initialGroundDrops);
        if (_options.FlushIntervalMilliseconds is < 1 or > 1000 ||
            _options.MaxFramesPerObserverPerPoll is < 1 or > 4096 ||
            _options.MaxCommittedLootBatchesPerPoll is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public long ProcessedBatches => Interlocked.Read(ref _processedBatches);
    public long FramesQueued => Interlocked.Read(ref _framesQueued);
    public WorldAoi Aoi => _aoi;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Exactly one AOI event pump is allowed");

        try
        {
            while (true)
            {
                while (_router.ChangedBatches.TryRead(out var batch))
                {
                    // Compute the map-local monster result from this authoritative
                    // World batch FIRST, but publish it only after player AOI has
                    // applied the same batch. A joining observer therefore sees
                    // exactly one latest MonsterSpawn rather than stale Spawn +
                    // same-tick Delta, while visibility uses the new player position.
                    MonsterStateBatch? monsterBatch =
                        _monsters?.Process(batch,
                            _playerCombat is null
                                ? null
                                : actor => _playerCombat.CurrentHp(actor));

                    if (_playerCombat is not null &&
                        _monsters is not null &&
                        monsterBatch?.Events is { Count: > 0 } events)
                    {
                        foreach (var evt in events)
                        {
                            if (evt.Kind != MonsterWorldEventKind.Attack)
                                continue;
                            if (!_monsters.TryGetActor(evt.TargetActorId, out var target))
                                continue;
                            var posted = _playerCombat.TryPostMonsterHit(target, evt);
                            if (posted == CombatPostStatus.Backpressured)
                                throw new AoiBackpressureException(
                                    "Player combat settlement input full; committed hit cannot be invented or dropped");
                        }
                    }

                    _aoi.Apply(batch,
                        monsterBatch?.Changed ?? Array.Empty<MonsterSnapshot>());
                    Interlocked.Increment(ref _processedBatches);
                }
                while (_router.ResyncRequests.TryRead(out var request))
                    _aoi.TryResync(request);

                while (_router.AttackRequests.TryRead(out var attack))
                {
                    bool attackerDead = _playerCombat is not null &&
                        _monsters is not null &&
                        _monsters.TryGetActor(attack.ActorId, out var attackingActor) &&
                        _playerCombat.CurrentHp(attackingActor) <= 0;
                    NormalAttackOutcome outcome = _monsters is null || attackerDead
                        ? new(NormalAttackStatus.CombatUnavailable,
                            Math.Max(0, _aoi.LastTick), attack.TargetEntityId,
                            0, 0, 0, 0)
                        : _monsters.ResolveNormalAttack(attack);

                    if (outcome.Status is NormalAttackStatus.Applied or NormalAttackStatus.Miss)
                        _aoi.EnqueuePlayerAttackVisual(attack,outcome);

                    if (outcome.Status == NormalAttackStatus.Applied &&
                        outcome.Monster is { } changed)
                    {
                        _aoi.ApplyMonsters(_aoi.LastTick, [changed]);
                        // Only successful Server-computed lethal attacks mint a
                        // death event, never replay/cooldown/other result status.
                        if (_loot is not null && changed.CurrentHp == 0 &&
                            changed.State == MonsterAiState.Dead &&
                            _loot.TryPostDeath(changed) == MonsterLootPostStatus.Backpressured)
                            throw new AoiBackpressureException(
                                "Loot settlement queue full; accepted death must not be dropped");
                    }

                    var sent = _router.QueueNormalAttackResult(attack, outcome);
                    if (sent is GatewaySendStatus.GenerationMismatch or GatewaySendStatus.Closed)
                        continue;
                    if (sent == GatewaySendStatus.Backpressured)
                        throw new AoiBackpressureException(
                            "Authenticated Gateway combat FIFO full; result cannot be dropped");
                    if (sent != GatewaySendStatus.Enqueued)
                        throw new InvalidOperationException("Combat delivery accounting diverged");
                    Interlocked.Increment(ref _framesQueued);
                }

                while(_router.SkillPreflightRequests.TryRead(out var preflight))
                {
                    MonsterWorldCoordinator.SkillPrepareResult result;
                    bool attackerDead=_playerCombat is not null &&
                        _monsters is not null &&
                        _monsters.TryGetActor(
                            preflight.Request.ActorId,out var preflightActor) &&
                        _playerCombat.CurrentHp(preflightActor)<=0;
                    if(_monsters is null || attackerDead)
                        result=new(null,new SkillUseOutcome(
                            SkillUseStatus.CombatUnavailable,
                            Math.Max(0,_aoi.LastTick),
                            preflight.Request.SkillId,
                            preflight.Request.TargetEntityId,0,0,0,0));
                    else
                        result=_monsters.PrepareSkill(preflight.Request);
                    preflight.Completion.TrySetResult(result);
                }

                while(_router.PreparedSkillActions.TryRead(out var action))
                {
                    try
                    {
                        SkillUseOutcome outcome=_monsters is null
                            ? new(SkillUseStatus.CombatUnavailable,
                                Math.Max(0,_aoi.LastTick),
                                action.Prepared.Request.SkillId,
                                action.Prepared.Request.TargetEntityId,0,0,0,0)
                            : action.CancelStatus is { } cancelled
                                ? _monsters.CancelPreparedSkill(
                                    action.Prepared,cancelled)
                                : _monsters.CommitPreparedSkill(action.Prepared);
                        DeliverSkill(action.Prepared.Request,outcome);
                        action.Completion.TrySetResult(outcome);
                    }
                    catch(Exception error)
                    {
                        action.Completion.TrySetException(error);
                        throw;
                    }
                }

                // Kept for transport-independent unit callers. Production TLS
                // uses PrepareSkillAsync -> durable resource settlement ->
                // FinishPreparedSkillAsync instead of this immediate path.
                while(_router.SkillRequests.TryRead(out var skill))
                {
                    bool attackerDead=_playerCombat is not null &&
                        _monsters is not null &&
                        _monsters.TryGetActor(skill.ActorId,out var skillActor) &&
                        _playerCombat.CurrentHp(skillActor)<=0;
                    SkillUseOutcome outcome=_monsters is null || attackerDead
                        ? new(SkillUseStatus.CombatUnavailable,
                            Math.Max(0,_aoi.LastTick),skill.SkillId,
                            skill.TargetEntityId,0,0,0,0)
                        : _monsters.ResolveSkill(skill);
                    DeliverSkill(skill,outcome);
                }

                // Player damage/respawn presentation is posted only after
                // durable settlement / trusted death-gate transition. Fold it
                // into the SAME AOI owner and reliable FIFO; never mutate AOI
                // from the SQLite worker or socket threads.
                while (_router.PlayerDamageVisuals.TryRead(out var damageVisual))
                    _aoi.EnqueuePlayerDamageVisual(damageVisual);
                while (_router.PlayerRespawnVisuals.TryRead(out var respawnVisual))
                    _aoi.EnqueuePlayerRespawnVisual(
                        respawnVisual.Actor,respawnVisual.RespawnSequence);

                // The worker only publishes after SQLite commit. Consume on
                // this same AOI owner thread; never poll SQLite per World tick.
                if (_loot is not null && _aoi.LastTick > 0)
                    for (int batchIndex = 0;
                         batchIndex < _options.MaxCommittedLootBatchesPerPoll;
                         batchIndex++)
                    {
                        if (!_loot.CommittedDrops.TryRead(out var committed))
                            break;
                        _aoi.ApplyGroundDrops(_aoi.LastTick,committed);
                    }

                // Only successful SQLite commits invalidate shared ground AOI;
                // never remove a drop based on a client's optimistic intent.
                if (_aoi.LastTick > 0)
                    for (int index = 0; index < 32; index++)
                    {
                        if (!_router.ClaimedGroundDrops.TryRead(out long dropId))
                            break;
                        _aoi.RemoveGroundDrop(_aoi.LastTick,dropId);
                    }

                foreach (var receiver in _router.SnapshotActiveBindings())
                {
                    for (int count = 0; count < _options.MaxFramesPerObserverPerPoll; count++)
                    {
                        if (!_aoi.TryPeek(receiver.ActorId, receiver.ConnectionEpoch,
                                         receiver.MapEpoch, out var evt))
                            break;
                        var result = _router.QueueAoi(receiver, evt);
                        if (result is GatewaySendStatus.GenerationMismatch or GatewaySendStatus.Closed)
                            break; // binding was superseded; do not send or discard old events
                        if (result == GatewaySendStatus.Backpressured)
                            throw new AoiBackpressureException(
                                "Authenticated Gateway AOI FIFO full; no reliable event was discarded");
                        if (result != GatewaySendStatus.Enqueued ||
                            !_aoi.TryConfirmQueued(receiver.ActorId, receiver.ConnectionEpoch,
                                                   receiver.MapEpoch))
                            throw new InvalidOperationException("AOI delivery accounting diverged");
                        Interlocked.Increment(ref _framesQueued);
                    }
                }

                if (_router.ChangedBatches.Completion.IsCompleted)
                {
                    // Propagate a fault from the underlying WorldResultRouter.
                    await _router.ChangedBatches.Completion.ConfigureAwait(false);
                    foreach (var receiver in _router.SnapshotActiveBindings())
                        if (_aoi.PendingEvents(receiver.ActorId) > 0)
                            throw new AoiBackpressureException(
                                "AOI source stopped with pending reliable events");
                    return;
                }

                await Task.Delay(_options.FlushIntervalMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            var stopped=new OperationCanceledException(
                "AOI skill owner stopped before prepared skill completed");
            while(_router.SkillPreflightRequests.TryRead(out var preflight))
                preflight.Completion.TrySetException(stopped);
            while(_router.PreparedSkillActions.TryRead(out var action))
                action.Completion.TrySetException(stopped);
        }
    }

    private void DeliverSkill(TrustedSkillUse skill,SkillUseOutcome outcome)
    {
        if(outcome.Status is SkillUseStatus.Applied or SkillUseStatus.Miss)
            _aoi.EnqueuePlayerSkillVisual(skill,outcome);

        if(outcome.Status==SkillUseStatus.Applied &&
            outcome.Monster is { } changed)
        {
            _aoi.ApplyMonsters(_aoi.LastTick,[changed]);
            if(_loot is not null && changed.CurrentHp==0 &&
                changed.State==MonsterAiState.Dead &&
                _loot.TryPostDeath(changed)==MonsterLootPostStatus.Backpressured)
                throw new AoiBackpressureException(
                    "Loot settlement queue full; accepted skill death must not be dropped");
        }

        var sent=_router.QueueSkillUseResult(skill,outcome);
        if(sent is GatewaySendStatus.GenerationMismatch or GatewaySendStatus.Closed)
            return;
        if(sent==GatewaySendStatus.Backpressured)
            throw new AoiBackpressureException(
                "Authenticated Gateway skill FIFO full; result cannot be dropped");
        if(sent!=GatewaySendStatus.Enqueued)
            throw new InvalidOperationException("Skill delivery accounting diverged");
        Interlocked.Increment(ref _framesQueued);
    }
}
