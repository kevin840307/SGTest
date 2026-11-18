using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

public sealed record WorldAoiOptions(
    int EnterHalfWidth = 640,
    int EnterHalfHeight = 400,
    int ExitMargin = 96,
    int BucketSize = 256,
    int MaxEntities = 10000,
    int MaxObservers = 256,
    int MaxVisiblePerObserver = 2048,
    int MaxPendingEventsPerObserver = 4096);

public enum AoiEventKind : byte
{
    WorldReady, Spawn, Delta, Despawn, SelfState,
    MonsterSpawn, MonsterDelta, MonsterDespawn,
    GroundDropSpawn, GroundDropDespawn,
    PlayerAttackVisual, PlayerSkillVisual, PlayerDamageVisual, PlayerRespawnVisual
}

/// <summary>
/// WorldReady includes the trusted viewer snapshot. Spawn/Delta include the
/// authoritative entity snapshot; Despawn identifies the previous entity
/// generation. ReceiverMapEpoch guards transfer/reconnect before wire encoding.
/// </summary>
public readonly record struct PlayerAttackVisual(
    long AttackerActorId,long AttackerConnectionEpoch,uint AttackSequence,
    long TargetEntityId,NormalAttackStatus Status,int Damage,
    long TargetRevision,byte Facing);
public readonly record struct PlayerSkillVisual(
    long AttackerActorId,long AttackerConnectionEpoch,uint SkillSequence,
    int SkillId,long TargetEntityId,SkillUseStatus Status,int Damage,
    long TargetRevision,byte Facing);
public readonly record struct PlayerDamageVisual(
    long VictimActorId,long VictimConnectionEpoch,uint DamageSequence,
    long AttackerMonsterEntityId,long AttackerMonsterRevision,
    int Damage,bool Died,byte Facing);
public readonly record struct PlayerRespawnVisual(
    long ActorId,long ConnectionEpoch,uint RespawnSequence,byte Facing);

public readonly record struct AoiEvent(
    AoiEventKind Kind, long Tick, long ReceiverMapEpoch,
    ActorSnapshot? Entity = null, long EntityActorId = 0, long EntityConnectionEpoch = 0,
    MonsterSnapshot? Monster = null, long MonsterEntityId = 0, long MonsterRevision = 0,
    GroundDropRecord? GroundDrop = null, long GroundDropId = 0,
    PlayerAttackVisual? PlayerAttack = null,
    PlayerSkillVisual? PlayerSkill = null,
    PlayerDamageVisual? PlayerDamage = null,
    PlayerRespawnVisual? PlayerRespawn = null);

public sealed class AoiBackpressureException : Exception
{
    public AoiBackpressureException(string message) : base(message) { }
}

/// <summary>
/// Single-writer, transport-independent AOI for authoritative player and monster snapshots.
/// Spatial buckets are per-map; only candidate buckets around a moving viewer
/// are scanned. Each entity update examines observers on its own map, not every
/// entity in the World. No DB, socket, TLS or renderer runs here.
///
/// Call Apply in WorldResultRouter.ChangedBatches order; DO NOT feed unordered
/// events or stale generations. Player and monster Spawn/Delta/Despawn are retained in
/// bounded per-observer FIFO queues for the next reliable wire delivery slice.
/// </summary>
public sealed class WorldAoi
{
    private readonly WorldAoiOptions _limits;
    private readonly Dictionary<long, ActorSnapshot> _entities = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _buckets = new();
    private readonly Dictionary<long, MonsterSnapshot> _monsters = new();
    private readonly Dictionary<long, GroundDropRecord> _groundDrops = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _groundBuckets = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _monsterBuckets = new();
    private readonly Dictionary<long, Observer> _observers = new();
    private long _lastTick;

    private readonly record struct Seen(long ConnectionEpoch, long MapEpoch, long Revision);

    private sealed class Observer
    {
        public ActorSnapshot Self;
        public readonly Dictionary<long, Seen> Visible = new();
        public readonly Dictionary<long, long> VisibleMonsters = new();
        public readonly HashSet<long> VisibleGroundDrops = new();
        public readonly Queue<AoiEvent> Pending = new();

        public Observer(ActorSnapshot actor) => Self = actor;
    }

    public WorldAoi(WorldAoiOptions? options = null)
    {
        _limits = options ?? new WorldAoiOptions();
        if (_limits.EnterHalfWidth is < 32 or > 4096 ||
            _limits.EnterHalfHeight is < 32 or > 4096 ||
            _limits.ExitMargin is < 0 or > 2048 ||
            _limits.BucketSize is < 64 or > 1024 ||
            _limits.MaxEntities is < 1 or > 100000 ||
            _limits.MaxObservers is < 1 or > 10000 ||
            _limits.MaxVisiblePerObserver is < 1 or > 10000 ||
            _limits.MaxPendingEventsPerObserver is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public int EntityCount => _entities.Count;
    public int MonsterCount => _monsters.Count;
    public int GroundDropCount => _groundDrops.Count;
    public int TrackedEntityCount => _entities.Count + _monsters.Count + _groundDrops.Count;
    public int ObserverCount => _observers.Count;
    public long LastTick => _lastTick;

    public int PendingEvents(long actorId) =>
        _observers.TryGetValue(actorId, out var observer) ? observer.Pending.Count : 0;

    public bool TryDequeue(long actorId, long connectionEpoch, long mapEpoch, out AoiEvent result)
    {
        result = default;
        if (!_observers.TryGetValue(actorId, out var observer) ||
            observer.Self.ConnectionEpoch != connectionEpoch ||
            observer.Self.MapEpoch != mapEpoch ||
            observer.Pending.Count == 0)
            return false;
        result = observer.Pending.Dequeue();
        return true;
    }

    /// <summary>
    /// Peek/confirm is intentionally split: a reliable AOI event is removed
    /// only AFTER the authenticated Gateway accepted it into bounded egress.
    /// These methods share the same single AOI consumer thread as Apply.
    /// </summary>
    public bool TryPeek(long actorId, long connectionEpoch, long mapEpoch, out AoiEvent result)
    {
        result = default;
        return _observers.TryGetValue(actorId, out var observer) &&
               observer.Self.ConnectionEpoch == connectionEpoch &&
               observer.Self.MapEpoch == mapEpoch &&
               observer.Pending.TryPeek(out result);
    }

    public bool TryConfirmQueued(long actorId, long connectionEpoch, long mapEpoch)
    {
        if (!_observers.TryGetValue(actorId, out var observer) ||
            observer.Self.ConnectionEpoch != connectionEpoch ||
            observer.Self.MapEpoch != mapEpoch ||
            observer.Pending.Count == 0)
            return false;
        observer.Pending.Dequeue();
        return true;
    }

    public bool TryResync(TrustedWorldResyncRequest request)
    {
        if (!_observers.TryGetValue(request.ActorId, out var observer) ||
            !_entities.TryGetValue(request.ActorId, out var current) ||
            observer.Self.ConnectionEpoch != request.ConnectionEpoch ||
            observer.Self.MapEpoch != request.MapEpoch ||
            current.ConnectionEpoch != request.ConnectionEpoch ||
            current.MapEpoch != request.MapEpoch)
            return false;
        observer.Pending.Clear();
        observer.Visible.Clear();
        observer.VisibleMonsters.Clear();
        observer.VisibleGroundDrops.Clear();
        observer.Self = current;
        Enqueue(observer, new(AoiEventKind.WorldReady, _lastTick, current.MapEpoch, current));
        Enqueue(observer, new(AoiEventKind.SelfState, _lastTick, current.MapEpoch, current));
        RefreshObserver(observer, _lastTick);
        return true;
    }

    public void Apply(WorldChangedBatch batch) =>
        Apply(batch, Array.Empty<MonsterSnapshot>());

    /// <summary>
    /// Atomically applies one authoritative World Tick plus monster snapshots
    /// derived from THAT SAME Tick. Monster spatial state is staged first without
    /// emitting; player Join/Move then refreshes against the latest monster state;
    /// the final pass notifies observers that player refresh did not already touch.
    /// </summary>
    public void Apply(WorldChangedBatch batch,
                      IReadOnlyList<MonsterSnapshot> monsterChanged)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(monsterChanged);
        if (batch.Tick <= _lastTick)
            throw new InvalidOperationException("AOI batches must be strictly ordered by World Tick");
        _lastTick = batch.Tick;

        var staged = new List<MonsterSnapshot>(monsterChanged.Count);
        foreach (var monster in monsterChanged.OrderBy(x => x.EntityId))
            if (StageMonster(monster))
                staged.Add(monster);

        if (batch.Lifecycle is not null)
        {
            // Preserve trusted registration/disconnect/transfer ordering, even
            // when several operations occur between two 30Hz ticks.
            foreach (var lifecycle in batch.Lifecycle)
            {
                if (lifecycle.Kind == ActorLifecycleKind.Joined)
                    Joined(lifecycle.Actor, batch.Tick);
                else if (lifecycle.Kind == ActorLifecycleKind.Left)
                    Left(lifecycle.Actor, batch.Tick);
                else
                    throw new InvalidOperationException("Unknown trusted lifecycle event");
            }
        }
        foreach (var changed in batch.Changed)
            Changed(changed, batch.Tick);

        // RefreshMonster is idempotent at the observer's seen revision. A viewer
        // already refreshed by Join/Move will not receive a duplicate Delta.
        foreach (var monster in staged)
            foreach (var observer in _observers.Values)
                if (observer.Self.MapId == monster.MapId)
                    RefreshMonster(observer, monster, batch.Tick);
    }

    /// <summary>
    /// Applies authoritative monster changes on the SAME World tick already
    /// accepted by Apply(). This preserves one ordered AOI timeline and avoids a
    /// second monster interest manager. The caller may coalesce several AI events
    /// for one monster to its latest snapshot before calling this method.
    /// </summary>
    public void ApplyMonsters(long tick, IReadOnlyList<MonsterSnapshot> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (tick != _lastTick || tick <= 0)
            throw new InvalidOperationException(
                "Monster AOI changes must share the current authoritative World Tick");
        foreach (var monster in changed)
            ChangedMonster(monster, tick);
    }

    /// <summary>
    /// Enqueue one authoritative normal-attack presentation event only to
    /// observers that currently see BOTH the attacker and target monster.
    /// Gameplay HP/damage authority remains in MonsterRuntime/MonsterDelta.
    /// </summary>
    public int EnqueuePlayerAttackVisual(
        TrustedNormalAttack request,NormalAttackOutcome outcome)
    {
        if(_lastTick<=0 || outcome.ServerTick!=_lastTick ||
            request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.AttackSequence==0 ||
            outcome.TargetEntityId!=request.TargetEntityId ||
            outcome.TargetRevision<=0 ||
            outcome.Status is not (NormalAttackStatus.Applied or NormalAttackStatus.Miss) ||
            (outcome.Status==NormalAttackStatus.Applied && outcome.Damage<=0) ||
            (outcome.Status==NormalAttackStatus.Miss && outcome.Damage!=0))
            throw new InvalidOperationException(
                "Invalid authoritative player attack visual");

        if(!_entities.TryGetValue(request.ActorId,out var attacker) ||
            attacker.ConnectionEpoch!=request.ConnectionEpoch ||
            attacker.MapEpoch!=request.RequestMapEpoch ||
            !_monsters.TryGetValue(request.TargetEntityId,out var target) ||
            attacker.MapId!=target.MapId)
            return 0;

        var visual=new PlayerAttackVisual(
            attacker.ActorId,attacker.ConnectionEpoch,request.AttackSequence,
            request.TargetEntityId,outcome.Status,outcome.Damage,
            outcome.TargetRevision,attacker.Facing);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=attacker.MapId ||
                !observer.VisibleMonsters.ContainsKey(request.TargetEntityId))
                continue;
            bool seesAttacker=
                observer.Self.ActorId==attacker.ActorId &&
                observer.Self.ConnectionEpoch==attacker.ConnectionEpoch &&
                observer.Self.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker &&
                observer.Visible.TryGetValue(attacker.ActorId,out var seen))
                seesAttacker=seen.ConnectionEpoch==attacker.ConnectionEpoch &&
                    seen.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker) continue;
            Enqueue(observer,new(AoiEventKind.PlayerAttackVisual,_lastTick,
                observer.Self.MapEpoch,PlayerAttack:visual));
            queued++;
        }
        return queued;
    }

    public int EnqueuePlayerSkillVisual(
        TrustedSkillUse request,SkillUseOutcome outcome)
    {
        if(_lastTick<=0 || outcome.ServerTick!=_lastTick ||
            request.ActorId<=0 || request.ConnectionEpoch<=0 ||
            request.RequestMapEpoch<=0 || request.SkillSequence==0 ||
            request.SkillId<=0 || outcome.SkillId!=request.SkillId ||
            outcome.Status is not (SkillUseStatus.Applied or SkillUseStatus.Miss))
            throw new InvalidOperationException(
                "Invalid authoritative player skill visual");

        if(!_entities.TryGetValue(request.ActorId,out var attacker) ||
            attacker.ConnectionEpoch!=request.ConnectionEpoch ||
            attacker.MapEpoch!=request.RequestMapEpoch)
            return 0;

        IReadOnlyList<SkillHitOutcome> hits;
        if(outcome.Hits is { Count: >0 } areaHits)
        {
            if(request.TargetEntityId!=0 || outcome.TargetEntityId!=0 ||
                outcome.Status!=SkillUseStatus.Applied ||
                outcome.Damage!=0 || outcome.CurrentHp!=0 ||
                outcome.MaxHp!=0 || outcome.TargetRevision!=0)
                throw new InvalidOperationException(
                    "Area skill summary/hit payload diverged");
            hits=areaHits;
        }
        else if(outcome.Monster is { } monster)
        {
            if(request.TargetEntityId<=0 ||
                outcome.TargetEntityId!=request.TargetEntityId ||
                monster.EntityId!=request.TargetEntityId ||
                outcome.TargetRevision<=0 ||
                (outcome.Status==SkillUseStatus.Applied && outcome.Damage<=0) ||
                (outcome.Status==SkillUseStatus.Miss && outcome.Damage!=0))
                throw new InvalidOperationException(
                    "Single skill visual payload diverged");
            hits=[new(monster,outcome.Damage)];
        }
        else
        {
            throw new InvalidOperationException(
                "Applied/Miss skill has no authoritative hit payload");
        }

        int queued=0;
        foreach(var hit in hits)
        {
            var monster=hit.Monster;
            if(monster.EntityId<=0 || monster.MapId!=attacker.MapId ||
                monster.EntityRevision<=0 || hit.Damage<=0 ||
                !_monsters.ContainsKey(monster.EntityId))
                throw new InvalidOperationException(
                    "Area skill hit is not an authoritative visible monster");
            var visual=new PlayerSkillVisual(
                attacker.ActorId,attacker.ConnectionEpoch,request.SkillSequence,
                request.SkillId,monster.EntityId,SkillUseStatus.Applied,
                hit.Damage,monster.EntityRevision,attacker.Facing);

            foreach(var observer in _observers.Values)
            {
                if(observer.Self.MapId!=attacker.MapId ||
                    !observer.VisibleMonsters.ContainsKey(monster.EntityId))
                    continue;
                bool seesAttacker=
                    observer.Self.ActorId==attacker.ActorId &&
                    observer.Self.ConnectionEpoch==attacker.ConnectionEpoch &&
                    observer.Self.MapEpoch==attacker.MapEpoch;
                if(!seesAttacker &&
                    observer.Visible.TryGetValue(attacker.ActorId,out var seen))
                    seesAttacker=seen.ConnectionEpoch==attacker.ConnectionEpoch &&
                        seen.MapEpoch==attacker.MapEpoch;
                if(!seesAttacker)continue;
                Enqueue(observer,new(AoiEventKind.PlayerSkillVisual,_lastTick,
                    observer.Self.MapEpoch,PlayerSkill:visual));
                queued++;
            }
        }
        return queued;
    }

    /// <summary>
    /// Publish one already-durable Monster→Player damage event to observers
    /// that currently see the exact victim generation. HP/death authority stays
    /// in PlayerCombatSettlement/SQLite; this event is presentation-only.
    /// </summary>
    public int EnqueuePlayerDamageVisual(PlayerDamageSettlement result)
    {
        if(_lastTick<=0 || result.ActorId<=0 || result.ConnectionEpoch<=0 ||
            result.MapEpoch<=0 || result.DamageSequence==0 ||
            result.ServerTick<0 || result.ServerTick>_lastTick ||
            result.AttackerMonsterEntityId<=0 ||
            result.AttackerMonsterRevision<=0 || result.Damage<=0 ||
            result.MaxHp<=0 || result.CurrentHp<0 ||
            result.CurrentHp>result.MaxHp ||
            result.Died!=(result.CurrentHp==0))
            throw new InvalidOperationException(
                "Invalid authoritative player damage visual");

        if(!_entities.TryGetValue(result.ActorId,out var victim) ||
            victim.ConnectionEpoch!=result.ConnectionEpoch ||
            victim.MapEpoch!=result.MapEpoch)
            return 0;

        var visual=new PlayerDamageVisual(
            victim.ActorId,victim.ConnectionEpoch,result.DamageSequence,
            result.AttackerMonsterEntityId,result.AttackerMonsterRevision,
            result.Damage,result.Died,victim.Facing);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=victim.MapId) continue;
            bool seesVictim=
                observer.Self.ActorId==victim.ActorId &&
                observer.Self.ConnectionEpoch==victim.ConnectionEpoch &&
                observer.Self.MapEpoch==victim.MapEpoch;
            if(!seesVictim &&
                observer.Visible.TryGetValue(victim.ActorId,out var seen))
                seesVictim=seen.ConnectionEpoch==victim.ConnectionEpoch &&
                    seen.MapEpoch==victim.MapEpoch;
            if(!seesVictim) continue;
            Enqueue(observer,new(AoiEventKind.PlayerDamageVisual,_lastTick,
                observer.Self.MapEpoch,PlayerDamage:visual));
            queued++;
        }
        return queued;
    }

    /// <summary>
    /// Publish an authenticated durable respawn presentation event after the
    /// World death gate was cleared. It carries no HP authority.
    /// </summary>
    public int EnqueuePlayerRespawnVisual(
        ActorSnapshot actor,uint respawnSequence)
    {
        if(_lastTick<=0 || actor.ActorId<=0 || actor.ConnectionEpoch<=0 ||
            actor.MapEpoch<=0 || actor.MapId<=0 || actor.EntityRevision<=0 ||
            respawnSequence==0 || actor.Facing is <1 or >8)
            throw new InvalidOperationException(
                "Invalid authoritative player respawn visual");
        if(!_entities.TryGetValue(actor.ActorId,out var current) ||
            current.ConnectionEpoch!=actor.ConnectionEpoch ||
            current.MapEpoch!=actor.MapEpoch ||
            current.MapId!=actor.MapId)
            return 0;

        var visual=new PlayerRespawnVisual(
            actor.ActorId,actor.ConnectionEpoch,respawnSequence,actor.Facing);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=actor.MapId) continue;
            bool seesActor=
                observer.Self.ActorId==actor.ActorId &&
                observer.Self.ConnectionEpoch==actor.ConnectionEpoch &&
                observer.Self.MapEpoch==actor.MapEpoch;
            if(!seesActor &&
                observer.Visible.TryGetValue(actor.ActorId,out var seen))
                seesActor=seen.ConnectionEpoch==actor.ConnectionEpoch &&
                    seen.MapEpoch==actor.MapEpoch;
            if(!seesActor) continue;
            Enqueue(observer,new(AoiEventKind.PlayerRespawnVisual,_lastTick,
                observer.Self.MapEpoch,PlayerRespawn:visual));
            queued++;
        }
        return queued;
    }

    /// <summary>Restore committed active drops before the first World batch.
    /// No database call runs inside this AOI class or on the World tick.</summary>
    public void SeedGroundDrops(IReadOnlyList<GroundDropRecord> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (_lastTick != 0 || _observers.Count != 0 || _groundDrops.Count != 0)
            throw new InvalidOperationException(
                "Ground drop bootstrap must occur before the first AOI World tick");
        foreach (var drop in active)
            StageGroundDrop(drop);
    }

    /// <summary>Apply one committed SQLite batch on the existing AOI owner,
    /// using the current authoritative World Tick; no extra AOI engine.</summary>
    public void ApplyGroundDrops(long tick, IReadOnlyList<GroundDropRecord> committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (tick <= 0 || tick != _lastTick)
            throw new InvalidOperationException("Drops must share an active AOI World tick");
        foreach (var drop in committed)
        {
            if (!StageGroundDrop(drop)) continue;
            foreach (var observer in _observers.Values)
                if (observer.Self.MapId == drop.MapId)
                    RefreshGroundDrop(observer,drop,tick);
        }
    }

    /// <summary>Used after a future authoritative pickup commits. A picked
    /// drop leaves all nearby observers via the same reliable AOI FIFO.</summary>
    public void RemoveGroundDrop(long tick,long dropId)
    {
        if (tick <= 0 || tick != _lastTick || dropId <= 0)
            throw new ArgumentOutOfRangeException(nameof(dropId));
        if (!_groundDrops.Remove(dropId,out var drop)) return;
        RemoveGroundBucket(drop);
        foreach (var observer in _observers.Values)
            if (observer.VisibleGroundDrops.Remove(dropId))
                Enqueue(observer,new(AoiEventKind.GroundDropDespawn,tick,
                    observer.Self.MapEpoch,GroundDropId:dropId));
    }

    private bool StageGroundDrop(GroundDropRecord drop)
    {
        if (drop.DropId <= 0 || drop.MapId is < 1 or > 65535 ||
            !double.IsFinite(drop.X) || !double.IsFinite(drop.Y) ||
            drop.X < 0 || drop.Y < 0 ||
            drop.ItemDefinitionId is < 1 or > 100_000_000 ||
            drop.Quantity is < 1 or > 1_000_000 ||
            drop.PickedByCharacterId is not null)
            throw new InvalidOperationException("Invalid committed ground drop");
        if (_groundDrops.TryGetValue(drop.DropId,out var prior))
        {
            if (prior != drop)
                throw new InvalidOperationException("Immutable drop UID content changed");
            return false;
        }
        if (TrackedEntityCount >= _limits.MaxEntities)
            throw new AoiBackpressureException("Ground drop AOI entity capacity exceeded");
        _groundDrops.Add(drop.DropId,drop);
        var key=(drop.MapId,Bucket(drop.X),Bucket(drop.Y));
        if(!_groundBuckets.TryGetValue(key,out var entries))
            _groundBuckets.Add(key,entries=new HashSet<long>());
        entries.Add(drop.DropId);
        return true;
    }

    private void RefreshGroundDrop(Observer observer,GroundDropRecord drop,long tick)
    {
        bool visible=observer.VisibleGroundDrops.Contains(drop.DropId);
        if (!Within(observer.Self,drop,visible))
        {
            if (visible)
            {
                observer.VisibleGroundDrops.Remove(drop.DropId);
                Enqueue(observer,new(AoiEventKind.GroundDropDespawn,tick,
                    observer.Self.MapEpoch,GroundDropId:drop.DropId));
            }
            return;
        }
        if (visible) return; // immutable until picked, never per-tick resend
        if (observer.Visible.Count + observer.VisibleMonsters.Count +
            observer.VisibleGroundDrops.Count >= _limits.MaxVisiblePerObserver)
            throw new AoiBackpressureException("AOI visible ground entity cap exceeded");
        observer.VisibleGroundDrops.Add(drop.DropId);
        Enqueue(observer,new(AoiEventKind.GroundDropSpawn,tick,
            observer.Self.MapEpoch,GroundDrop:drop));
    }

    private void RemoveGroundBucket(GroundDropRecord drop)
    {
        var key=(drop.MapId,Bucket(drop.X),Bucket(drop.Y));
        if(_groundBuckets.TryGetValue(key,out var entries))
        {
            entries.Remove(drop.DropId);
            if(entries.Count==0) _groundBuckets.Remove(key);
        }
    }

    private void Joined(ActorSnapshot actor, long tick)
    {
        ValidateActor(actor);
        if (_entities.ContainsKey(actor.ActorId))
            throw new InvalidOperationException("AOI received Join without preceding trusted Leave");
        if (TrackedEntityCount >= _limits.MaxEntities ||
            _observers.Count >= _limits.MaxObservers)
            throw new AoiBackpressureException("AOI entity/observer capacity exceeded");

        Store(actor);
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == actor.MapId)
                RefreshEntity(observer, actor, tick);

        var viewer = new Observer(actor);
        _observers.Add(actor.ActorId, viewer);
        Enqueue(viewer, new(AoiEventKind.WorldReady, tick, actor.MapEpoch, actor));
        RefreshObserver(viewer, tick);
    }

    private void Left(ActorSnapshot actor, long tick)
    {
        if (!_entities.TryGetValue(actor.ActorId, out var old) ||
            old.ConnectionEpoch != actor.ConnectionEpoch ||
            old.MapEpoch != actor.MapEpoch ||
            old.MapId != actor.MapId)
            throw new InvalidOperationException("Stale or untracked AOI Leave");

        RemoveBucket(old);
        _entities.Remove(actor.ActorId);
        // After disconnect or a trusted map transfer, the old observer queue is
        // no longer deliverable. New map gets a fresh WorldReady and generation.
        _observers.Remove(actor.ActorId);
        foreach (var observer in _observers.Values)
        {
            if (observer.Visible.Remove(actor.ActorId))
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    observer.Self.MapEpoch, null, old.ActorId, old.ConnectionEpoch));
        }
    }

    private void Changed(ActorSnapshot actor, long tick)
    {
        ValidateActor(actor);
        if (!_entities.TryGetValue(actor.ActorId, out var previous))
            throw new InvalidOperationException("AOI delta before trusted Join");
        if (actor.ConnectionEpoch != previous.ConnectionEpoch ||
            actor.MapEpoch != previous.MapEpoch || actor.MapId != previous.MapId)
            throw new InvalidOperationException("AOI map/connection changed without trusted lifecycle");
        if (actor.EntityRevision < previous.EntityRevision)
            throw new InvalidOperationException("AOI revision regressed");
        if (actor.EntityRevision == previous.EntityRevision)
            return; // same-tick replay of identical revision
        RemoveBucket(previous);
        Store(actor);
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == actor.MapId)
                RefreshEntity(observer, actor, tick);
        if (_observers.TryGetValue(actor.ActorId, out var moving))
        {
            moving.Self = actor;
            Enqueue(moving, new(AoiEventKind.SelfState, tick, actor.MapEpoch, actor));
            RefreshObserver(moving, tick);
        }
    }

    private void ChangedMonster(MonsterSnapshot monster, long tick)
    {
        if (!StageMonster(monster)) return;
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == monster.MapId)
                RefreshMonster(observer, monster, tick);
    }

    private bool StageMonster(MonsterSnapshot monster)
    {
        ValidateMonster(monster);
        if (!_monsters.TryGetValue(monster.EntityId, out var previous))
        {
            if (TrackedEntityCount >= _limits.MaxEntities)
                throw new AoiBackpressureException("AOI entity capacity exceeded");
            StoreMonster(monster);
            return true;
        }

        if (monster.EnemyId != previous.EnemyId ||
            monster.SourceIndex != previous.SourceIndex ||
            monster.MapId != previous.MapId ||
            monster.MaxHp != previous.MaxHp ||
            monster.SpawnX != previous.SpawnX ||
            monster.SpawnY != previous.SpawnY)
            throw new InvalidOperationException(
                "Monster AOI immutable identity changed without population rebuild");
        if (monster.EntityRevision < previous.EntityRevision)
            throw new InvalidOperationException("Monster AOI revision regressed");
        if (monster.EntityRevision == previous.EntityRevision)
            return false;

        RemoveMonsterBucket(previous);
        StoreMonster(monster);
        return true;
    }

    private void RefreshObserver(Observer observer, long tick)
    {
        var viewer = observer.Self;
        foreach (var entry in observer.Visible.ToArray())
        {
            if (!_entities.TryGetValue(entry.Key, out var actor) || !Within(viewer, actor, true))
            {
                observer.Visible.Remove(entry.Key);
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    viewer.MapEpoch, null, entry.Key, entry.Value.ConnectionEpoch));
            }
        }
        foreach (var entry in observer.VisibleGroundDrops.ToArray())
        {
            if (!_groundDrops.TryGetValue(entry,out var drop) ||
                !Within(viewer,drop,true))
            {
                observer.VisibleGroundDrops.Remove(entry);
                Enqueue(observer,new(AoiEventKind.GroundDropDespawn,tick,
                    viewer.MapEpoch,GroundDropId:entry));
            }
        }
        foreach (var entry in observer.VisibleMonsters.ToArray())
        {
            if (!_monsters.TryGetValue(entry.Key, out var monster) ||
                !Within(viewer, monster, true))
            {
                observer.VisibleMonsters.Remove(entry.Key);
                long revision = monster.EntityId > 0 ? monster.EntityRevision : entry.Value;
                Enqueue(observer, new(AoiEventKind.MonsterDespawn, tick,
                    viewer.MapEpoch, MonsterEntityId: entry.Key,
                    MonsterRevision: revision));
            }
        }

        // Scan only the bounded map-specific spatial buckets surrounding this
        // observer, not the entire entity dictionaries each movement tick.
        int minX = Bucket(viewer.X - _limits.EnterHalfWidth);
        int maxX = Bucket(viewer.X + _limits.EnterHalfWidth);
        int minY = Bucket(viewer.Y - _limits.EnterHalfHeight);
        int maxY = Bucket(viewer.Y + _limits.EnterHalfHeight);
        var candidates = new HashSet<long>();
        for (int bx = minX; bx <= maxX; bx++)
            for (int by = minY; by <= maxY; by++)
                if (_buckets.TryGetValue((viewer.MapId, bx, by), out var ids))
                    candidates.UnionWith(ids);
        foreach (long id in candidates.OrderBy(id => id))
            if (_entities.TryGetValue(id, out var actor))
                RefreshEntity(observer, actor, tick);

        candidates.Clear();
        for (int bx = minX; bx <= maxX; bx++)
            for (int by = minY; by <= maxY; by++)
                if (_monsterBuckets.TryGetValue((viewer.MapId, bx, by), out var ids))
                    candidates.UnionWith(ids);
        foreach (long id in candidates.OrderBy(id => id))
            if (_monsters.TryGetValue(id, out var monster))
                RefreshMonster(observer, monster, tick);

        candidates.Clear();
        for (int bx = minX; bx <= maxX; bx++)
            for (int by = minY; by <= maxY; by++)
                if (_groundBuckets.TryGetValue((viewer.MapId,bx,by),out var drops))
                    candidates.UnionWith(drops);
        foreach(long id in candidates.OrderBy(id=>id))
            if(_groundDrops.TryGetValue(id,out var drop))
                RefreshGroundDrop(observer,drop,tick);
    }

    private void RefreshEntity(Observer observer, ActorSnapshot actor, long tick)
    {
        if (observer.Self.ActorId == actor.ActorId) return;
        bool visible = observer.Visible.TryGetValue(actor.ActorId, out var seen);
        if (!Within(observer.Self, actor, visible))
        {
            if (visible)
            {
                observer.Visible.Remove(actor.ActorId);
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch));
            }
            return;
        }
        if (!visible)
        {
            if (observer.Visible.Count + observer.VisibleMonsters.Count + observer.VisibleGroundDrops.Count >=
                _limits.MaxVisiblePerObserver)
                throw new AoiBackpressureException("AOI visible entity cap exceeded");
            observer.Visible.Add(actor.ActorId,
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
        }
        else if (seen.ConnectionEpoch != actor.ConnectionEpoch || seen.MapEpoch != actor.MapEpoch)
        {
            observer.Visible[actor.ActorId] =
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision);
            Enqueue(observer, new(AoiEventKind.Despawn, tick,
                observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
        }
        else if (actor.EntityRevision > seen.Revision)
        {
            observer.Visible[actor.ActorId] =
                new(actor.ConnectionEpoch, actor.MapEpoch, actor.EntityRevision);
            Enqueue(observer, new(AoiEventKind.Delta, tick, observer.Self.MapEpoch, actor));
        }
    }

    private void RefreshMonster(Observer observer, MonsterSnapshot monster, long tick)
    {
        bool visible = observer.VisibleMonsters.TryGetValue(monster.EntityId, out var seenRevision);
        if (!Within(observer.Self, monster, visible))
        {
            if (visible)
            {
                observer.VisibleMonsters.Remove(monster.EntityId);
                Enqueue(observer, new(AoiEventKind.MonsterDespawn, tick,
                    observer.Self.MapEpoch, MonsterEntityId: monster.EntityId,
                    MonsterRevision: monster.EntityRevision));
            }
            return;
        }
        if (!visible)
        {
            if (observer.Visible.Count + observer.VisibleMonsters.Count + observer.VisibleGroundDrops.Count >=
                _limits.MaxVisiblePerObserver)
                throw new AoiBackpressureException("AOI visible entity cap exceeded");
            observer.VisibleMonsters.Add(monster.EntityId, monster.EntityRevision);
            Enqueue(observer, new(AoiEventKind.MonsterSpawn, tick,
                observer.Self.MapEpoch, Monster: monster));
        }
        else if (monster.EntityRevision > seenRevision)
        {
            observer.VisibleMonsters[monster.EntityId] = monster.EntityRevision;
            Enqueue(observer, new(AoiEventKind.MonsterDelta, tick,
                observer.Self.MapEpoch, Monster: monster));
        }
    }

    private bool Within(ActorSnapshot viewer, ActorSnapshot actor, bool alreadyVisible)
    {
        if (viewer.MapId != actor.MapId) return false;
        int margin = alreadyVisible ? _limits.ExitMargin : 0;
        return Math.Abs(viewer.X - actor.X) <= _limits.EnterHalfWidth + margin &&
               Math.Abs(viewer.Y - actor.Y) <= _limits.EnterHalfHeight + margin;
    }

    private bool Within(ActorSnapshot viewer, MonsterSnapshot monster, bool alreadyVisible)
    {
        if (viewer.MapId != monster.MapId) return false;
        int margin = alreadyVisible ? _limits.ExitMargin : 0;
        return Math.Abs(viewer.X - monster.X) <= _limits.EnterHalfWidth + margin &&
               Math.Abs(viewer.Y - monster.Y) <= _limits.EnterHalfHeight + margin;
    }

    private bool Within(ActorSnapshot viewer, GroundDropRecord drop, bool alreadyVisible)
    {
        if (viewer.MapId != drop.MapId) return false;
        int margin=alreadyVisible?_limits.ExitMargin:0;
        return Math.Abs(viewer.X-drop.X)<=_limits.EnterHalfWidth+margin &&
               Math.Abs(viewer.Y-drop.Y)<=_limits.EnterHalfHeight+margin;
    }

    private static void ValidateMonster(MonsterSnapshot monster)
    {
        if (monster.EntityId <= 0 || monster.EnemyId <= 0 || monster.SourceIndex < 0 ||
            monster.MapId <= 0 || monster.EntityRevision <= 0 ||
            monster.CurrentHp < 0 || monster.MaxHp <= 0 ||
            monster.CurrentHp > monster.MaxHp || monster.TargetActorId < 0 ||
            monster.Facing is < 1 or > 8 ||
            !double.IsFinite(monster.X) || !double.IsFinite(monster.Y) ||
            !double.IsFinite(monster.SpawnX) || !double.IsFinite(monster.SpawnY) ||
            monster.X < 0 || monster.Y < 0 || monster.SpawnX < 0 || monster.SpawnY < 0)
            throw new InvalidOperationException("Invalid authoritative AOI monster snapshot");
    }

    private static void ValidateActor(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 || actor.MapEpoch <= 0 ||
            actor.MapId <= 0 || actor.EntityRevision <= 0 || actor.Facing is < 1 or > 8 ||
            !double.IsFinite(actor.X) || !double.IsFinite(actor.Y) ||
            actor.X < 0 || actor.Y < 0)
            throw new InvalidOperationException("Invalid authoritative AOI actor snapshot");
    }

    private int Bucket(double value) => (int)Math.Floor(value / _limits.BucketSize);

    private void Store(ActorSnapshot actor)
    {
        _entities[actor.ActorId] = actor;
        var key = (actor.MapId, Bucket(actor.X), Bucket(actor.Y));
        if (!_buckets.TryGetValue(key, out var ids))
            _buckets.Add(key, ids = new HashSet<long>());
        ids.Add(actor.ActorId);
    }

    private void RemoveBucket(ActorSnapshot actor)
    {
        var key = (actor.MapId, Bucket(actor.X), Bucket(actor.Y));
        if (_buckets.TryGetValue(key, out var ids))
        {
            ids.Remove(actor.ActorId);
            if (ids.Count == 0) _buckets.Remove(key);
        }
    }

    private void StoreMonster(MonsterSnapshot monster)
    {
        _monsters[monster.EntityId] = monster;
        var key = (monster.MapId, Bucket(monster.X), Bucket(monster.Y));
        if (!_monsterBuckets.TryGetValue(key, out var ids))
            _monsterBuckets.Add(key, ids = new HashSet<long>());
        ids.Add(monster.EntityId);
    }

    private void RemoveMonsterBucket(MonsterSnapshot monster)
    {
        var key = (monster.MapId, Bucket(monster.X), Bucket(monster.Y));
        if (_monsterBuckets.TryGetValue(key, out var ids))
        {
            ids.Remove(monster.EntityId);
            if (ids.Count == 0) _monsterBuckets.Remove(key);
        }
    }

    private void Enqueue(Observer observer, AoiEvent value)
    {
        if (observer.Pending.Count >= _limits.MaxPendingEventsPerObserver)
            throw new AoiBackpressureException("AOI reliable event backlog exceeded");
        observer.Pending.Enqueue(value);
    }
}
