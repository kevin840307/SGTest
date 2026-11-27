using SGTest.Server.Skill;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.World;
using SGTest.Server.Identity;
using SGTest.Server.Pet;
using SGTest.Server.Party;

namespace SGTest.Server.Visibility;

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
    private readonly CharacterExperienceSettlement? _experience;
    private readonly PartyCoordinator? _parties;
    private int _started;
    private long _processedBatches, _framesQueued;

    public WorldAoiPump(WorldResultRouter router, WorldAoi? aoi = null,
                        WorldAoiPumpOptions? options = null,
                        MonsterWorldCoordinator? monsters = null,
                        PlayerCombatSettlement? playerCombat = null,
                        MonsterLootSettlement? loot = null,
                        CharacterExperienceSettlement? experience = null,
                        PartyCoordinator? parties = null,
                        IReadOnlyList<GroundDropRecord>? initialGroundDrops = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _aoi = aoi ?? new WorldAoi();
        _options = options ?? new WorldAoiPumpOptions();
        _monsters = monsters;
        _playerCombat = playerCombat;
        _loot = loot;
        _experience = experience;
        _parties = parties;
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

    private void TryPostExperienceDeath(MonsterSnapshot monster)
    {
        if(_experience is null)
            return;
        if(_monsters is null)
            throw new InvalidOperationException(
                "Character EXP settlement requires Monster authority");

        var contributions=_monsters.DamageContributions(monster);
        if(contributions.Count==0)
            throw new InvalidOperationException(
                "Accepted monster death lost its damage contribution ledger");

        var available=new List<(
            MonsterDamageContribution Damage,
            ConnectionBinding Binding,
            long ActivePetUid)>(contributions.Count);
        foreach(var contribution in contributions)
        {
            if(!_router.TryGetCurrentBinding(
                   contribution.ActorId,out var binding)||
               !_monsters.TryGetActor(contribution.ActorId,out var actor)||
               actor.ConnectionEpoch!=binding.ConnectionEpoch)
                continue;

            long activePetUid=0;
            if(_aoi.TryGetActivePet(
                   binding.ActorId,binding.ConnectionEpoch,
                   binding.MapEpoch,out var pet))
                activePetUid=pet.PetUid;
            available.Add((contribution,binding,activePetUid));
        }
        if(available.Count==0)
            return;

        var partyIds=_parties?.SnapshotSettlementPartyIds(
            available.Select(x=>x.Binding).ToArray(),_router)
            ?? new Dictionary<long,long>();
        var grouped=new Dictionary<
            (bool IsParty,long Id),
            List<CharacterExperienceDeathParticipant>>();
        foreach(var item in available)
        {
            var key=partyIds.TryGetValue(item.Binding.ActorId,out long partyId)
                ? (true,partyId)
                : (false,item.Binding.ActorId);
            if(!grouped.TryGetValue(key,out var members))
            {
                members=[];
                grouped.Add(key,members);
            }
            members.Add(new CharacterExperienceDeathParticipant(
                item.Binding,item.Damage.AppliedDamage,item.ActivePetUid));
        }

        var groups=grouped
            .OrderBy(x=>x.Key.IsParty?0:1)
            .ThenBy(x=>x.Key.Id)
            .Select(x=>new CharacterExperienceDeathGroup(
                x.Value.Sum(member=>member.AppliedDamage),
                x.Value.OrderBy(member=>member.Binding.ActorId).ToArray()))
            .ToArray();
        long totalDamage=groups.Sum(x=>x.AppliedDamage);

        if(_experience.TryPostDeath(monster,totalDamage,groups)==
           CharacterExperienceDeathPostStatus.Backpressured)
            throw new AoiBackpressureException(
                "Character EXP settlement queue full; accepted death must not be dropped");
    }

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

                // Auth/session and PvP mutation threads publish only a tiny
                // identity invalidation. The single AOI owner resolves the
                // current trusted Gateway snapshot and appends it AFTER any
                // already-pending Spawn for that target generation.
                while(_router.PlayerIdentityUpdates.TryRead(out var identityUpdate))
                    if(_router.TryGetPlayerIdentity(
                        identityUpdate.ActorId,identityUpdate.ConnectionEpoch,
                        out var identity))
                        _aoi.EnqueuePlayerIdentity(identity);

                while(_router.PetWorldUpdates.TryRead(out var petUpdate))
                    _aoi.ApplyPetState(petUpdate);
                while(_router.MechaWorldUpdates.TryRead(out var mechaUpdate))
                    _aoi.ApplyMechaState(mechaUpdate);
                while(_router.PlayerEquipmentWorldUpdates.TryRead(out var equipmentUpdate))
                    _aoi.ApplyPlayerEquipmentState(equipmentUpdate);
                while(_router.PlayerShopWorldUpdates.TryRead(out var shopUpdate))
                    _aoi.ApplyPlayerShopState(shopUpdate);

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
                        if(changed.CurrentHp==0&&
                           changed.State==MonsterAiState.Dead)
                        {
                            if(_loot is not null&&
                               _loot.TryPostDeath(changed,attack.Profile?.Level??0)==
                                   MonsterLootPostStatus.Backpressured)
                                throw new AoiBackpressureException(
                                    "Loot settlement queue full; accepted death must not be dropped");
                            TryPostExperienceDeath(changed);
                        }
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

                while(_router.PetAttackRequests.TryRead(out var petAttack))
                {
                    bool attackerDead=_playerCombat is not null&&
                        _monsters is not null&&
                        _monsters.TryGetActor(petAttack.ActorId,out var petOwner)&&
                        _playerCombat.CurrentHp(petOwner)<=0;
                    NormalAttackOutcome outcome;
                    PetWorldPresence pet=default;
                    if(_monsters is null||attackerDead||
                       !_aoi.TryGetPet(
                           petAttack.ActorId,petAttack.ConnectionEpoch,
                           petAttack.RequestMapEpoch,petAttack.PetUid,out pet))
                        outcome=new(
                            NormalAttackStatus.CombatUnavailable,
                            Math.Max(0,_aoi.LastTick),petAttack.TargetEntityId,
                            0,0,0,0);
                    else
                        outcome=_monsters.ResolvePetNormalAttack(petAttack,pet);

                    if(outcome.Status is NormalAttackStatus.Applied or NormalAttackStatus.Miss)
                        _aoi.EnqueuePetAttackVisual(petAttack,outcome);

                    if(outcome.Status==NormalAttackStatus.Applied&&
                       outcome.Monster is { } changed)
                    {
                        _aoi.ApplyMonsters(_aoi.LastTick,[changed]);
                        if(changed.CurrentHp==0&&changed.State==MonsterAiState.Dead)
                        {
                            if(_loot is not null&&
                               _loot.TryPostDeath(changed,petAttack.Profile?.Level??0)==
                                   MonsterLootPostStatus.Backpressured)
                                throw new AoiBackpressureException(
                                    "Loot settlement queue full; accepted pet death must not be dropped");
                            TryPostExperienceDeath(changed);
                        }
                    }

                    var sent=_router.QueuePetAttackResult(petAttack,outcome);
                    if(sent is GatewaySendStatus.GenerationMismatch or GatewaySendStatus.Closed)
                        continue;
                    if(sent==GatewaySendStatus.Backpressured)
                        throw new AoiBackpressureException(
                            "Authenticated Gateway pet combat FIFO full; result cannot be dropped");
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new InvalidOperationException(
                            "Pet combat delivery accounting diverged");
                    Interlocked.Increment(ref _framesQueued);
                }

                while(_router.PetSkillRequests.TryRead(out var petSkill))
                {
                    bool attackerDead=_playerCombat is not null&&
                        _monsters is not null&&
                        _monsters.TryGetActor(petSkill.ActorId,out var petOwner)&&
                        _playerCombat.CurrentHp(petOwner)<=0;
                    SkillUseOutcome outcome;
                    PetWorldPresence pet=default;
                    if(_monsters is null||attackerDead||
                       !_aoi.TryGetPet(
                           petSkill.ActorId,petSkill.ConnectionEpoch,
                           petSkill.RequestMapEpoch,petSkill.PetUid,out pet))
                        outcome=new(
                            SkillUseStatus.CombatUnavailable,
                            Math.Max(0,_aoi.LastTick),petSkill.SkillId,
                            petSkill.TargetEntityId,0,0,0,0);
                    else
                        outcome=_monsters.ResolvePetSkill(petSkill,pet);

                    if(outcome.Status is SkillUseStatus.Applied or SkillUseStatus.Miss)
                        _aoi.EnqueuePetSkillVisual(petSkill,outcome);

                    if(outcome.Status==SkillUseStatus.Applied&&
                       outcome.Monster is { } changed)
                    {
                        _aoi.ApplyMonsters(_aoi.LastTick,[changed]);
                        if(changed.CurrentHp==0&&changed.State==MonsterAiState.Dead)
                        {
                            if(_loot is not null&&
                               _loot.TryPostDeath(changed,petSkill.Profile?.Level??0)==
                                   MonsterLootPostStatus.Backpressured)
                                throw new AoiBackpressureException(
                                    "Loot settlement queue full; accepted pet skill death must not be dropped");
                            TryPostExperienceDeath(changed);
                        }
                    }

                    var sent=_router.QueuePetSkillResult(petSkill,outcome);
                    if(sent is GatewaySendStatus.GenerationMismatch or GatewaySendStatus.Closed)
                        continue;
                    if(sent==GatewaySendStatus.Backpressured)
                        throw new AoiBackpressureException(
                            "Authenticated Gateway pet skill FIFO full; result cannot be dropped");
                    if(sent!=GatewaySendStatus.Enqueued)
                        throw new InvalidOperationException(
                            "Pet skill delivery accounting diverged");
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
                        if(action.DeliverMonsterResult)
                            DeliverSkill(action.Prepared.Request,outcome);
                        action.Completion.TrySetResult(outcome);
                    }
                    catch(Exception error)
                    {
                        action.Completion.TrySetException(error);
                        throw;
                    }
                }

                // Kept for transport-independent unit callers. Production authenticated transport
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
                while(_router.PlayerPvpCombatVisuals.TryRead(out var pvpVisual))
                    _aoi.EnqueuePlayerPvpCombatVisual(pvpVisual);
                while(_router.PlayerPvpSkillVisuals.TryRead(out var pvpSkillVisual))
                    _aoi.EnqueuePlayerPvpSkillVisual(pvpSkillVisual);
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
                        if(result!=GatewaySendStatus.Enqueued)
                            throw new InvalidOperationException("AOI delivery accounting diverged");

                        // The entity generation must enter the same reliable FIFO
                        // before its display identity. This also heals the race
                        // where World Join became visible just before Gateway attach.
                        long identityActor=0,identityConnection=0;
                        if(evt.Kind==AoiEventKind.WorldReady&&evt.Entity is { } self)
                        {
                            identityActor=self.ActorId;
                            identityConnection=self.ConnectionEpoch;
                        }
                        else if(evt.Kind==AoiEventKind.Spawn&&evt.Entity is { } spawned)
                        {
                            identityActor=spawned.ActorId;
                            identityConnection=spawned.ConnectionEpoch;
                        }
                        if(identityActor>0&&_router.TryGetPlayerIdentity(
                            identityActor,identityConnection,out var identity))
                        {
                            var identityResult=_router.QueueAoi(
                                receiver,new(AoiEventKind.PlayerIdentity,evt.Tick,
                                    receiver.MapEpoch,PlayerIdentity:identity));
                            if(identityResult is GatewaySendStatus.GenerationMismatch or
                                GatewaySendStatus.Closed)
                                break;
                            if(identityResult==GatewaySendStatus.Backpressured)
                                throw new AoiBackpressureException(
                                    "Authenticated Gateway identity FIFO full; reliable identity cannot be dropped");
                            if(identityResult!=GatewaySendStatus.Enqueued)
                                throw new InvalidOperationException(
                                    "Player identity delivery accounting diverged");
                            Interlocked.Increment(ref _framesQueued);
                        }

                        if(!_aoi.TryConfirmQueued(
                            receiver.ActorId,receiver.ConnectionEpoch,receiver.MapEpoch))
                            throw new InvalidOperationException("AOI confirmation accounting diverged");
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

        if(outcome.Status==SkillUseStatus.Applied)
        {
            MonsterSnapshot[] changed=outcome.Hits is { Count: >0 } hits
                ? hits.Select(x=>x.Monster).ToArray()
                : outcome.Monster is { } single
                    ? [single] : [];
            if(changed.Length>0)
            {
                _aoi.ApplyMonsters(_aoi.LastTick,changed);
                foreach(var monster in changed)
                    if(monster.CurrentHp==0&&monster.State==MonsterAiState.Dead)
                    {
                        if(_loot is not null&&
                           _loot.TryPostDeath(monster,skill.Profile?.Level??0)==
                               MonsterLootPostStatus.Backpressured)
                            throw new AoiBackpressureException(
                                "Loot settlement queue full; accepted skill death must not be dropped");
                        TryPostExperienceDeath(monster);
                    }
            }
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
