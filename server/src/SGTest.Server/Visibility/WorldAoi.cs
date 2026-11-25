using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using SGTest.Server.Mecha;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Visibility;

public sealed record WorldAoiOptions(
    int EnterHalfWidth = 640,
    int EnterHalfHeight = 400,
    int ExitMargin = 96,
    int BucketSize = 256,
    int MaxEntities = 10000,
    int MaxObservers = 256,
    int MaxVisiblePerObserver = 2048,
    int MaxPendingEventsPerObserver = 4096,
    int RemotePlayerDeltaIntervalTicks = 3,
    double RemotePlayerDeltaDistance = 24);

public enum AoiEventKind : byte
{
    WorldReady, Spawn, Delta, Despawn, SelfState, PlayerIdentity, PlayerMovementIntent,
    MonsterSpawn, MonsterDelta, MonsterDespawn,
    GroundDropSpawn, GroundDropDespawn,
    PetSpawn, PetDelta, PetDespawn, PetAttackVisual, PetSkillVisual,
    MechaState,
    PlayerAttackVisual, PlayerSkillVisual, PlayerDamageVisual, PlayerRespawnVisual,
    PvpCombatVisual, PvpSkillVisual
}

/// <summary>
/// WorldReady includes the trusted viewer snapshot. Spawn/Delta include the
/// authoritative entity snapshot; Despawn identifies the previous entity
/// generation. ReceiverMapEpoch guards transfer/reconnect before wire encoding.
/// </summary>
public readonly record struct PetAttackVisual(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    uint AttackSequence,long PetUid,long TargetEntityId,
    NormalAttackStatus Status,int Damage,long TargetRevision);
public readonly record struct PetSkillVisual(
    long OwnerActorId,long OwnerConnectionEpoch,long OwnerMapEpoch,
    uint SkillSequence,long PetUid,int SkillId,long TargetEntityId,
    SkillUseStatus Status,int Damage,long TargetRevision);
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
public readonly record struct PlayerIdentityPresentation(
    long ActorId,long ConnectionEpoch,long EntityMapEpoch,string DisplayName,
    PvpMode Mode,bool RedMan,long ServerUnixMilliseconds,
    long PinkUntilUnixMilliseconds,long PvpUpdatedUnixMilliseconds,
    CharacterAppearance? Appearance = null);
public readonly record struct PlayerPvpCombatVisual(
    long AttackerActorId,long AttackerConnectionEpoch,uint AttackSequence,
    long VictimActorId,long VictimConnectionEpoch,uint DamageSequence,
    int Damage,bool Died,byte AttackerFacing,byte VictimFacing);
public readonly record struct PlayerPvpSkillVisual(
    long AttackerActorId,long AttackerConnectionEpoch,uint SkillSequence,
    int SkillId,long VictimActorId,long VictimConnectionEpoch,
    uint DamageSequence,int Damage,bool Died,
    byte AttackerFacing,byte VictimFacing);

public readonly record struct AoiEvent(
    AoiEventKind Kind, long Tick, long ReceiverMapEpoch,
    ActorSnapshot? Entity = null, long EntityActorId = 0, long EntityConnectionEpoch = 0,
    MonsterSnapshot? Monster = null, long MonsterEntityId = 0, long MonsterRevision = 0,
    GroundDropRecord? GroundDrop = null, long GroundDropId = 0,
    PetWorldPresence? Pet = null,
    MechaWorldStateUpdate? Mecha = null,
    PetAttackVisual? PetAttack = null,
    PetSkillVisual? PetSkill = null,
    PlayerAttackVisual? PlayerAttack = null,
    PlayerSkillVisual? PlayerSkill = null,
    PlayerDamageVisual? PlayerDamage = null,
    PlayerRespawnVisual? PlayerRespawn = null,
    PlayerIdentityPresentation? PlayerIdentity = null,
    PlayerPvpCombatVisual? PvpCombat = null,
    PlayerPvpSkillVisual? PvpSkill = null,
    long EntityMapEpoch = 0);

public sealed class AoiBackpressureException : Exception
{
    public AoiBackpressureException(string message) : base(message) { }
}

/// <summary>
/// Single-writer, transport-independent AOI for authoritative player and monster snapshots.
/// Spatial buckets are per-map; only candidate buckets around a moving viewer
/// are scanned. Each entity update examines observers on its own map, not every
/// entity in the World. No DB, transport or renderer runs here.
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
    private readonly Dictionary<long, PetWorldPresence> _petsByOwner = new();
    private readonly Dictionary<long, MechaWorldStateUpdate> _mechasByOwner = new();
    private readonly Dictionary<long, PetFollowTarget> _petFollowTargets = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _groundBuckets = new();
    private readonly Dictionary<(int Map, int X, int Y), HashSet<long>> _monsterBuckets = new();
    private readonly Dictionary<long, Observer> _observers = new();
    private long _lastTick;

    private readonly record struct Seen(
        long ConnectionEpoch, long MapEpoch,
        long SourceRevision, long WireRevision, long SentTick,
        double X, double Y, bool IsMoving, uint MovementActionSequence,
        byte Facing, bool CombatDead,
        uint MovementIntentSequence, bool MovementIntentMoving);

    private readonly record struct PetFollowTarget(
        long OwnerConnectionEpoch,long OwnerMapEpoch,long QueuedTick,double X,double Y);

    private sealed class Observer
    {
        public ActorSnapshot Self;
        public readonly Dictionary<long, Seen> Visible = new();
        public readonly Dictionary<long, long> VisibleMonsters = new();
        public readonly HashSet<long> VisibleGroundDrops = new();
        public readonly Dictionary<long, PetWorldPresence> VisiblePets = new();
        public readonly Dictionary<long, MechaWorldStateUpdate> VisibleMechas = new();
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
            _limits.MaxPendingEventsPerObserver is < 1 or > 65536 ||
            _limits.RemotePlayerDeltaIntervalTicks is < 1 or > 30 ||
            !double.IsFinite(_limits.RemotePlayerDeltaDistance) ||
            _limits.RemotePlayerDeltaDistance is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public int EntityCount => _entities.Count;
    public int MonsterCount => _monsters.Count;
    public int GroundDropCount => _groundDrops.Count;
    public int PetCount => _petsByOwner.Count;
    public int TrackedEntityCount =>
        _entities.Count + _monsters.Count + _groundDrops.Count + _petsByOwner.Count;
    public int ObserverCount => _observers.Count;
    public long LastTick => _lastTick;

    public bool TryGetPet(
        long ownerActorId,long connectionEpoch,long mapEpoch,long petUid,
        out PetWorldPresence pet)
    {
        pet=default;
        return ownerActorId>0&&connectionEpoch>0&&mapEpoch>0&&petUid>0&&
            _petsByOwner.TryGetValue(ownerActorId,out pet)&&
            pet.OwnerConnectionEpoch==connectionEpoch&&
            pet.OwnerMapEpoch==mapEpoch&&pet.PetUid==petUid;
    }

    public bool TryGetActivePet(
        long ownerActorId,long connectionEpoch,long mapEpoch,
        out PetWorldPresence pet)
    {
        pet=default;
        return ownerActorId>0&&connectionEpoch>0&&mapEpoch>0&&
            _petsByOwner.TryGetValue(ownerActorId,out pet)&&
            pet.OwnerConnectionEpoch==connectionEpoch&&
            pet.OwnerMapEpoch==mapEpoch&&pet.PetUid>0;
    }

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
        observer.VisiblePets.Clear();
        observer.VisibleMechas.Clear();
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

        // Pet movement deliberately trails one accepted World tick behind the owner.
        // Advancing BEFORE this tick's actor deltas means every pet waypoint came from
        // a position that the authoritative player movement pipeline already accepted.
        AdvancePetFollowTargets(batch.Tick);

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
    /// Applies committed active-pet identity on the same single AOI owner. Pet
    /// visibility follows owner visibility; the spawn baseline position comes from
    /// the authoritative owner snapshot until independent pet movement is added.
    /// </summary>
    public void ApplyPetState(PetWorldStateUpdate update)
    {
        if(update.OwnerActorId<=0||update.OwnerConnectionEpoch<=0||
           update.OwnerMapEpoch<=0)
            throw new InvalidOperationException("Invalid pet AOI update");
        if(update.Present)
        {
            if(update.PetUid<=0||update.DefinitionId is <=0 or >100000000||
               update.EvolutionId is <0 or >65535||update.Level is <=0 or >1000000)
                throw new InvalidOperationException("Invalid active pet AOI identity");
        }
        else if(update.PetUid!=0||update.DefinitionId!=0||update.EvolutionId!=0||update.Level!=0)
            throw new InvalidOperationException("Inactive pet AOI update carried identity");

        if(_petsByOwner.TryGetValue(update.OwnerActorId,out var previous))
        {
            if(update.OwnerConnectionEpoch<previous.OwnerConnectionEpoch||
               (update.OwnerConnectionEpoch==previous.OwnerConnectionEpoch&&
                update.OwnerMapEpoch<previous.OwnerMapEpoch))
                return;
            if(!update.Present||previous.PetUid!=update.PetUid||
               previous.DefinitionId!=update.DefinitionId||previous.EvolutionId!=update.EvolutionId||
               previous.Level!=update.Level||
               previous.OwnerConnectionEpoch!=update.OwnerConnectionEpoch||
               previous.OwnerMapEpoch!=update.OwnerMapEpoch)
                HidePet(previous);
        }

        if(!update.Present)
        {
            _petFollowTargets.Remove(update.OwnerActorId);
            _petsByOwner.Remove(update.OwnerActorId);
            return;
        }

        _petFollowTargets.Remove(update.OwnerActorId);
        double x=0,y=0;
        if(_entities.TryGetValue(update.OwnerActorId,out var activeOwner)&&
           activeOwner.ConnectionEpoch==update.OwnerConnectionEpoch&&
           activeOwner.MapEpoch==update.OwnerMapEpoch)
        {
            x=activeOwner.X;
            y=activeOwner.Y;
        }
        var next=new PetWorldPresence(
            update.OwnerActorId,update.OwnerConnectionEpoch,update.OwnerMapEpoch,
            update.PetUid,update.DefinitionId,update.EvolutionId,update.Level,x,y);
        if(!_petsByOwner.ContainsKey(update.OwnerActorId)&&
           TrackedEntityCount>=_limits.MaxEntities)
            throw new AoiBackpressureException("Pet AOI entity capacity exceeded");
        _petsByOwner[update.OwnerActorId]=next;
        if(_entities.TryGetValue(update.OwnerActorId,out var owner)&&
           owner.ConnectionEpoch==update.OwnerConnectionEpoch&&
           owner.MapEpoch==update.OwnerMapEpoch)
            foreach(var observer in _observers.Values)
                RefreshPet(observer,owner,_lastTick);
    }

    /// <summary>
    /// Applies committed mounted-Mecha identity to the same AOI owner as the
    /// player. Mecha has no independent position here: it is presentation state
    /// attached to the exact player generation and follows that owner's AOI.
    /// </summary>
    public void ApplyMechaState(MechaWorldStateUpdate update)
    {
        if(update.OwnerActorId<=0||update.OwnerConnectionEpoch<=0||
           update.OwnerMapEpoch<=0)
            throw new InvalidOperationException("Invalid Mecha AOI update");
        if(update.Mounted)
        {
            if(update.MechaUid<=0||update.DefinitionId is <=0 or >100000000||
               update.Level is <=0 or >1000000)
                throw new InvalidOperationException("Invalid mounted Mecha AOI identity");
        }
        else if(update.MechaUid!=0||update.DefinitionId!=0||update.Level!=0)
            throw new InvalidOperationException("Unmounted Mecha AOI update carried identity");

        if(_mechasByOwner.TryGetValue(update.OwnerActorId,out var previous))
        {
            if(update.OwnerConnectionEpoch<previous.OwnerConnectionEpoch||
               (update.OwnerConnectionEpoch==previous.OwnerConnectionEpoch&&
                update.OwnerMapEpoch<previous.OwnerMapEpoch))
                return;
            // A mounted Mecha is presentation attached to one player generation.
            // Switching identity/level inside that generation is atomic: publish the
            // replacement directly so clients never render an artificial dismount flash.
            if(!update.Mounted||
               previous.OwnerConnectionEpoch!=update.OwnerConnectionEpoch||
               previous.OwnerMapEpoch!=update.OwnerMapEpoch)
                HideMecha(previous);
        }

        if(!update.Mounted)
        {
            _mechasByOwner.Remove(update.OwnerActorId);
            return;
        }

        _mechasByOwner[update.OwnerActorId]=update;
        if(_entities.TryGetValue(update.OwnerActorId,out var owner)&&
           owner.ConnectionEpoch==update.OwnerConnectionEpoch&&
           owner.MapEpoch==update.OwnerMapEpoch)
            foreach(var observer in _observers.Values)
                RefreshMecha(observer,owner,_lastTick);
    }

    public int EnqueuePlayerIdentity(PlayerIdentityPresentation identity)
    {
        if(_lastTick<=0||identity.ActorId<=0||identity.ConnectionEpoch<=0||
           identity.EntityMapEpoch<=0||string.IsNullOrWhiteSpace(identity.DisplayName)||
           identity.Mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           identity.ServerUnixMilliseconds<=0||
           identity.PinkUntilUnixMilliseconds<0||
           identity.PvpUpdatedUnixMilliseconds<0)
            throw new InvalidOperationException(
                "Invalid authoritative player identity");
        if(!_entities.TryGetValue(identity.ActorId,out var actor)||
           actor.ConnectionEpoch!=identity.ConnectionEpoch||
           actor.MapEpoch!=identity.EntityMapEpoch)
            return 0;

        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=actor.MapId)continue;
            bool sees=
                observer.Self.ActorId==actor.ActorId&&
                observer.Self.ConnectionEpoch==actor.ConnectionEpoch&&
                observer.Self.MapEpoch==actor.MapEpoch;
            if(!sees&&observer.Visible.TryGetValue(actor.ActorId,out var seen))
                sees=seen.ConnectionEpoch==actor.ConnectionEpoch&&
                    seen.MapEpoch==actor.MapEpoch;
            if(!sees)continue;
            Enqueue(observer,new(
                AoiEventKind.PlayerIdentity,_lastTick,observer.Self.MapEpoch,
                PlayerIdentity:identity));
            queued++;
        }
        return queued;
    }

    /// <summary>
    /// Enqueue one authoritative Pet attack presentation event only to remote
    /// observers that currently see BOTH the exact Pet generation and target.
    /// The owner receives PetAttackResult separately, so it is deliberately
    /// excluded here to prevent duplicate local animation playback.
    /// </summary>
    public int EnqueuePetAttackVisual(
        TrustedPetAttack request,NormalAttackOutcome outcome)
    {
        if(_lastTick<=0||outcome.ServerTick!=_lastTick||request.ActorId<=0||
           request.ConnectionEpoch<=0||request.RequestMapEpoch<=0||
           request.AttackSequence==0||request.PetUid<=0||
           outcome.TargetEntityId!=request.TargetEntityId||outcome.TargetRevision<=0||
           outcome.Status is not (NormalAttackStatus.Applied or NormalAttackStatus.Miss)||
           (outcome.Status==NormalAttackStatus.Applied&&outcome.Damage<=0)||
           (outcome.Status==NormalAttackStatus.Miss&&outcome.Damage!=0))
            throw new InvalidOperationException("Invalid authoritative pet attack visual");

        if(!TryGetPet(request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.PetUid,out var pet)||
           !_entities.TryGetValue(request.ActorId,out var owner)||
           owner.ConnectionEpoch!=request.ConnectionEpoch||
           owner.MapEpoch!=request.RequestMapEpoch||
           !_monsters.TryGetValue(request.TargetEntityId,out var target)||
           owner.MapId!=target.MapId)
            return 0;

        var visual=new PetAttackVisual(
            pet.OwnerActorId,pet.OwnerConnectionEpoch,pet.OwnerMapEpoch,
            request.AttackSequence,pet.PetUid,request.TargetEntityId,
            outcome.Status,outcome.Damage,outcome.TargetRevision);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.ActorId==owner.ActorId&&
               observer.Self.ConnectionEpoch==owner.ConnectionEpoch&&
               observer.Self.MapEpoch==owner.MapEpoch)
                continue;
            if(observer.Self.MapId!=owner.MapId||
               !observer.VisibleMonsters.ContainsKey(request.TargetEntityId)||
               !observer.VisiblePets.TryGetValue(owner.ActorId,out var seenPet)||
               !SamePetIdentity(seenPet,pet))
                continue;
            Enqueue(observer,new(AoiEventKind.PetAttackVisual,_lastTick,
                observer.Self.MapEpoch,PetAttack:visual));
            queued++;
        }
        return queued;
    }

    /// <summary>
    /// Enqueue one source-gated Pet skill presentation event only to remote
    /// observers that currently see the exact Pet generation and target.
    /// </summary>
    public int EnqueuePetSkillVisual(
        TrustedPetSkill request,SkillUseOutcome outcome)
    {
        if(_lastTick<=0||outcome.ServerTick!=_lastTick||request.ActorId<=0||
           request.ConnectionEpoch<=0||request.RequestMapEpoch<=0||
           request.SkillSequence==0||request.PetUid<=0||request.SkillId<=0||
           outcome.SkillId!=request.SkillId||
           outcome.TargetEntityId!=request.TargetEntityId||outcome.TargetRevision<=0||
           outcome.Status is not (SkillUseStatus.Applied or SkillUseStatus.Miss)||
           (outcome.Status==SkillUseStatus.Applied&&outcome.Damage<=0)||
           (outcome.Status==SkillUseStatus.Miss&&outcome.Damage!=0))
            throw new InvalidOperationException("Invalid authoritative pet skill visual");

        if(!TryGetPet(request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch,
                request.PetUid,out var pet)||
           !_entities.TryGetValue(request.ActorId,out var owner)||
           owner.ConnectionEpoch!=request.ConnectionEpoch||
           owner.MapEpoch!=request.RequestMapEpoch||
           !_monsters.TryGetValue(request.TargetEntityId,out var target)||
           owner.MapId!=target.MapId)
            return 0;

        var visual=new PetSkillVisual(
            pet.OwnerActorId,pet.OwnerConnectionEpoch,pet.OwnerMapEpoch,
            request.SkillSequence,pet.PetUid,request.SkillId,request.TargetEntityId,
            outcome.Status,outcome.Damage,outcome.TargetRevision);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.ActorId==owner.ActorId&&
               observer.Self.ConnectionEpoch==owner.ConnectionEpoch&&
               observer.Self.MapEpoch==owner.MapEpoch)
                continue;
            if(observer.Self.MapId!=owner.MapId||
               !observer.VisibleMonsters.ContainsKey(request.TargetEntityId)||
               !observer.VisiblePets.TryGetValue(owner.ActorId,out var seenPet)||
               !SamePetIdentity(seenPet,pet))
                continue;
            Enqueue(observer,new(AoiEventKind.PetSkillVisual,_lastTick,
                observer.Self.MapEpoch,PetSkill:visual));
            queued++;
        }
        return queued;
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
    public int EnqueuePlayerPvpCombatVisual(PlayerDamageSettlement result)
    {
        if(_lastTick<=0||result.SourceKind!=PlayerDamageSourceKind.PlayerPvp||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.ServerTick<0||
           result.ServerTick>_lastTick||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerMapEpoch<=0||
           result.AttackerAttackSequence==0||
           result.Damage<=0||result.AttackerActorId==result.ActorId||
           result.AttackerFacing is <1 or >8||result.VictimFacing is <1 or >8)
            throw new InvalidOperationException(
                "Invalid authoritative PvP combat visual");

        // Each player owns an independent MapEpoch. The settled event carries
        // the attacker's generation separately from the victim's MapEpoch.
        if(!_entities.TryGetValue(result.AttackerActorId,out var attacker)||
           attacker.ConnectionEpoch!=result.AttackerConnectionEpoch||
           attacker.MapEpoch!=result.AttackerMapEpoch||
           !_entities.TryGetValue(result.ActorId,out var victim)||
           victim.ConnectionEpoch!=result.ConnectionEpoch||
           victim.MapEpoch!=result.MapEpoch||
           attacker.MapId!=victim.MapId)
            return 0;

        var visual=new PlayerPvpCombatVisual(
            attacker.ActorId,attacker.ConnectionEpoch,
            result.AttackerAttackSequence,
            victim.ActorId,victim.ConnectionEpoch,result.DamageSequence,
            result.Damage,result.Died,result.AttackerFacing,result.VictimFacing);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=attacker.MapId)continue;
            bool seesAttacker=
                observer.Self.ActorId==attacker.ActorId&&
                observer.Self.ConnectionEpoch==attacker.ConnectionEpoch&&
                observer.Self.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker&&observer.Visible.TryGetValue(
                attacker.ActorId,out var seenAttacker))
                seesAttacker=
                    seenAttacker.ConnectionEpoch==attacker.ConnectionEpoch&&
                    seenAttacker.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker)continue;

            bool seesVictim=
                observer.Self.ActorId==victim.ActorId&&
                observer.Self.ConnectionEpoch==victim.ConnectionEpoch&&
                observer.Self.MapEpoch==victim.MapEpoch;
            if(!seesVictim&&observer.Visible.TryGetValue(
                victim.ActorId,out var seenVictim))
                seesVictim=
                    seenVictim.ConnectionEpoch==victim.ConnectionEpoch&&
                    seenVictim.MapEpoch==victim.MapEpoch;
            if(!seesVictim)continue;

            Enqueue(observer,new(AoiEventKind.PvpCombatVisual,_lastTick,
                observer.Self.MapEpoch,PvpCombat:visual));
            queued++;
        }
        return queued;
    }

    public int EnqueuePlayerPvpSkillVisual(PlayerDamageSettlement result)
    {
        if(_lastTick<=0||result.SourceKind!=PlayerDamageSourceKind.PlayerSkill||
           result.ActorId<=0||result.ConnectionEpoch<=0||result.MapEpoch<=0||
           result.DamageSequence==0||result.ServerTick<0||
           result.ServerTick>_lastTick||result.AttackerActorId<=0||
           result.AttackerConnectionEpoch<=0||result.AttackerMapEpoch<=0||
           result.AttackerSkillSequence==0||
           result.AttackerSkillId is <1 or >100_000_000||result.Damage<=0||
           result.AttackerActorId==result.ActorId||
           result.AttackerFacing is <1 or >8||result.VictimFacing is <1 or >8)
            throw new InvalidOperationException("Invalid authoritative PvP skill visual");
        // Each player owns an independent MapEpoch. The settled event carries
        // the attacker's generation separately from the victim's MapEpoch.
        if(!_entities.TryGetValue(result.AttackerActorId,out var attacker)||
           attacker.ConnectionEpoch!=result.AttackerConnectionEpoch||
           attacker.MapEpoch!=result.AttackerMapEpoch||
           !_entities.TryGetValue(result.ActorId,out var victim)||
           victim.ConnectionEpoch!=result.ConnectionEpoch||
           victim.MapEpoch!=result.MapEpoch||attacker.MapId!=victim.MapId)
            return 0;
        var visual=new PlayerPvpSkillVisual(
            attacker.ActorId,attacker.ConnectionEpoch,
            result.AttackerSkillSequence,result.AttackerSkillId,
            victim.ActorId,victim.ConnectionEpoch,result.DamageSequence,
            result.Damage,result.Died,result.AttackerFacing,result.VictimFacing);
        int queued=0;
        foreach(var observer in _observers.Values)
        {
            if(observer.Self.MapId!=attacker.MapId)continue;
            bool seesAttacker=observer.Self.ActorId==attacker.ActorId&&
                observer.Self.ConnectionEpoch==attacker.ConnectionEpoch&&
                observer.Self.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker&&observer.Visible.TryGetValue(attacker.ActorId,out var sa))
                seesAttacker=sa.ConnectionEpoch==attacker.ConnectionEpoch&&
                    sa.MapEpoch==attacker.MapEpoch;
            if(!seesAttacker)continue;
            bool seesVictim=observer.Self.ActorId==victim.ActorId&&
                observer.Self.ConnectionEpoch==victim.ConnectionEpoch&&
                observer.Self.MapEpoch==victim.MapEpoch;
            if(!seesVictim&&observer.Visible.TryGetValue(victim.ActorId,out var sv))
                seesVictim=sv.ConnectionEpoch==victim.ConnectionEpoch&&
                    sv.MapEpoch==victim.MapEpoch;
            if(!seesVictim)continue;
            Enqueue(observer,new(AoiEventKind.PvpSkillVisual,_lastTick,
                observer.Self.MapEpoch,PvpSkill:visual));
            queued++;
        }
        return queued;
    }

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
        PreflightGroundDropBatch(active);
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
        // A single SQLite death batch is indivisible. Check immutable UIDs,
        // shared capacity and every observer's reliable FIFO before publishing
        // the first drop; otherwise a later rejection leaves half a death
        // visible to some players and permanently loses the rest of the batch.
        PreflightGroundDropBatch(committed);
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

    private void PreflightGroundDropBatch(IReadOnlyList<GroundDropRecord> committed)
    {
        var newDrops = new Dictionary<long, GroundDropRecord>();
        foreach (var drop in committed)
        {
            ValidateGroundDrop(drop);
            if (_groundDrops.TryGetValue(drop.DropId, out var prior))
            {
                if (prior != drop)
                    throw new InvalidOperationException("Immutable drop UID content changed");
            }
            else if (!newDrops.TryAdd(drop.DropId, drop) &&
                     newDrops[drop.DropId] != drop)
                throw new InvalidOperationException("Conflicting drop UID inside one batch");
        }
        if (newDrops.Count > _limits.MaxEntities - TrackedEntityCount)
            throw new AoiBackpressureException("Ground drop AOI entity capacity exceeded");

        foreach (var observer in _observers.Values)
        {
            int incoming = 0;
            foreach (var drop in newDrops.Values)
                if (Within(observer.Self, drop, false))
                    incoming++;
            if (observer.Visible.Count + observer.VisibleMonsters.Count +
                observer.VisibleGroundDrops.Count + observer.VisiblePets.Count +
                incoming > _limits.MaxVisiblePerObserver)
                throw new AoiBackpressureException("AOI visible ground entity cap exceeded");
            if (incoming > _limits.MaxPendingEventsPerObserver - observer.Pending.Count)
                throw new AoiBackpressureException("AOI reliable event backlog exceeded");
        }
    }

    private static void ValidateGroundDrop(GroundDropRecord drop)
    {
        if (drop.DropId <= 0 || drop.MapId is < 1 or > 65535 ||
            !double.IsFinite(drop.X) || !double.IsFinite(drop.Y) ||
            drop.X < 0 || drop.Y < 0 ||
            drop.ItemDefinitionId is < 1 or > 100_000_000 ||
            drop.Quantity is < 1 or > 1_000_000 ||
            drop.PickedByCharacterId is not null)
            throw new InvalidOperationException("Invalid committed ground drop");
    }

    private bool StageGroundDrop(GroundDropRecord drop)
    {
        ValidateGroundDrop(drop);
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
            observer.VisibleGroundDrops.Count + observer.VisiblePets.Count >=
            _limits.MaxVisiblePerObserver)
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
            {
                RefreshEntity(observer, actor, tick);
                RefreshPet(observer, actor, tick);
                RefreshMecha(observer, actor, tick);
            }

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

        if(_petsByOwner.TryGetValue(actor.ActorId,out var activePet)&&
           activePet.OwnerConnectionEpoch==actor.ConnectionEpoch&&
           activePet.OwnerMapEpoch==actor.MapEpoch)
        {
            HidePet(activePet);
            _petFollowTargets.Remove(actor.ActorId);
        }
        if(_mechasByOwner.TryGetValue(actor.ActorId,out var activeMecha)&&
           activeMecha.OwnerConnectionEpoch==actor.ConnectionEpoch&&
           activeMecha.OwnerMapEpoch==actor.MapEpoch)
            HideMecha(activeMecha);
        RemoveBucket(old);
        _entities.Remove(actor.ActorId);
        // After disconnect or a trusted map transfer, the old observer queue is
        // no longer deliverable. New map gets a fresh WorldReady and generation.
        _observers.Remove(actor.ActorId);
        foreach (var observer in _observers.Values)
        {
            if (observer.Visible.Remove(actor.ActorId))
                Enqueue(observer, new(AoiEventKind.Despawn, tick,
                    observer.Self.MapEpoch, null, old.ActorId, old.ConnectionEpoch,
                    EntityMapEpoch:old.MapEpoch));
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
        QueuePetFollowTarget(actor,tick);
        foreach (var observer in _observers.Values)
            if (observer.Self.MapId == actor.MapId)
            {
                RefreshEntity(observer, actor, tick);
                RefreshPet(observer, actor, tick);
                RefreshMecha(observer, actor, tick);
            }
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
                    viewer.MapEpoch, null, entry.Key, entry.Value.ConnectionEpoch,
                    EntityMapEpoch:entry.Value.MapEpoch));
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
        foreach(var entry in observer.VisiblePets.ToArray())
        {
            bool ownerVisible=entry.Key==viewer.ActorId || observer.Visible.ContainsKey(entry.Key);
            if(!_petsByOwner.TryGetValue(entry.Key,out var pet)||
               pet.PetUid!=entry.Value.PetUid||
               pet.OwnerConnectionEpoch!=entry.Value.OwnerConnectionEpoch||
               !ownerVisible)
            {
                observer.VisiblePets.Remove(entry.Key);
                Enqueue(observer,new(AoiEventKind.PetDespawn,tick,
                    viewer.MapEpoch,Pet:entry.Value));
            }
        }
        foreach(var entry in observer.VisibleMechas.ToArray())
        {
            bool ownerVisible=entry.Key==viewer.ActorId || observer.Visible.ContainsKey(entry.Key);
            if(!_mechasByOwner.TryGetValue(entry.Key,out var mecha)||
               !SameMechaIdentity(mecha,entry.Value)||!ownerVisible)
            {
                observer.VisibleMechas.Remove(entry.Key);
                Enqueue(observer,new(AoiEventKind.MechaState,tick,viewer.MapEpoch,
                    Mecha:UnmountedMecha(entry.Value)));
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
            {
                RefreshEntity(observer, actor, tick);
                RefreshPet(observer,actor,tick);
                RefreshMecha(observer,actor,tick);
            }
        RefreshPet(observer,viewer,tick);
        RefreshMecha(observer,viewer,tick);

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
                    observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch,
                    EntityMapEpoch:seen.MapEpoch));
            }
            return;
        }
        if (!visible)
        {
            if (observer.Visible.Count + observer.VisibleMonsters.Count +
                observer.VisibleGroundDrops.Count + observer.VisiblePets.Count >=
                _limits.MaxVisiblePerObserver)
                throw new AoiBackpressureException("AOI visible entity cap exceeded");
            observer.Visible.Add(actor.ActorId, SeenFrom(actor, tick));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
            if (actor.IsMoving)
                Enqueue(observer, new(AoiEventKind.PlayerMovementIntent, tick,
                    observer.Self.MapEpoch, actor));
        }
        else if (seen.ConnectionEpoch != actor.ConnectionEpoch || seen.MapEpoch != actor.MapEpoch)
        {
            observer.Visible[actor.ActorId] = SeenFrom(actor, tick);
            Enqueue(observer, new(AoiEventKind.Despawn, tick,
                observer.Self.MapEpoch, null, actor.ActorId, seen.ConnectionEpoch,
                    EntityMapEpoch:seen.MapEpoch));
            Enqueue(observer, new(AoiEventKind.Spawn, tick, observer.Self.MapEpoch, actor));
            if (actor.IsMoving)
                Enqueue(observer, new(AoiEventKind.PlayerMovementIntent, tick,
                    observer.Self.MapEpoch, actor));
        }
        else
        {
            bool movementIntentChanged =
                actor.MovementActionSequence != seen.MovementIntentSequence ||
                actor.IsMoving != seen.MovementIntentMoving;
            if (actor.EntityRevision > seen.SourceRevision && ShouldQueueRemoteDelta(seen, actor, tick))
            {
                long wireRevision = checked(seen.WireRevision + 1);
                var wireActor = actor with { EntityRevision = wireRevision };
                observer.Visible[actor.ActorId] = SeenFrom(actor, tick, wireRevision);
                Enqueue(observer, new(AoiEventKind.Delta, tick, observer.Self.MapEpoch, wireActor));
            }
            if (movementIntentChanged)
            {
                if (!observer.Visible.TryGetValue(actor.ActorId, out var current))
                    throw new InvalidOperationException("AOI movement intent lost visible entity");
                observer.Visible[actor.ActorId] = current with
                {
                    MovementIntentSequence = actor.MovementActionSequence,
                    MovementIntentMoving = actor.IsMoving
                };
                Enqueue(observer, new(AoiEventKind.PlayerMovementIntent, tick,
                    observer.Self.MapEpoch, actor));
            }
        }
    }

    private static Seen SeenFrom(ActorSnapshot actor, long tick, long? wireRevision = null) => new(
        actor.ConnectionEpoch, actor.MapEpoch,
        actor.EntityRevision, wireRevision ?? actor.EntityRevision, tick,
        actor.X, actor.Y, actor.IsMoving, actor.MovementActionSequence,
        actor.Facing, actor.CombatDead,
        actor.MovementActionSequence, actor.IsMoving);

    private bool ShouldQueueRemoteDelta(Seen seen, ActorSnapshot actor, long tick)
    {
        // Observable motion boundaries stay immediate. MovementActionSequence
        // is required for the owner's reconciliation, but remote presentation
        // does not consume it as an event; it can travel with the next checkpoint.
        if (actor.IsMoving != seen.IsMoving ||
            actor.Facing != seen.Facing ||
            actor.CombatDead != seen.CombatDead)
            return true;
        if (!actor.IsMoving)
            return actor.X != seen.X || actor.Y != seen.Y;
        if (tick - seen.SentTick >= _limits.RemotePlayerDeltaIntervalTicks)
            return true;
        double dx = actor.X - seen.X;
        double dy = actor.Y - seen.Y;
        double distance = _limits.RemotePlayerDeltaDistance;
        return dx * dx + dy * dy >= distance * distance;
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
            if (observer.Visible.Count + observer.VisibleMonsters.Count +
                observer.VisibleGroundDrops.Count + observer.VisiblePets.Count >=
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

    private void QueuePetFollowTarget(ActorSnapshot owner,long tick)
    {
        if(!_petsByOwner.TryGetValue(owner.ActorId,out var pet)||
           pet.OwnerConnectionEpoch!=owner.ConnectionEpoch||
           pet.OwnerMapEpoch!=owner.MapEpoch)
            return;
        _petFollowTargets[owner.ActorId]=new(
            owner.ConnectionEpoch,owner.MapEpoch,tick,owner.X,owner.Y);
    }

    private void AdvancePetFollowTargets(long tick)
    {
        foreach(var entry in _petFollowTargets.ToArray())
        {
            var target=entry.Value;
            if(target.QueuedTick>=tick)continue;
            if(!_petsByOwner.TryGetValue(entry.Key,out var pet)||
               pet.OwnerConnectionEpoch!=target.OwnerConnectionEpoch||
               pet.OwnerMapEpoch!=target.OwnerMapEpoch||
               !_entities.TryGetValue(entry.Key,out var owner)||
               owner.ConnectionEpoch!=target.OwnerConnectionEpoch||
               owner.MapEpoch!=target.OwnerMapEpoch)
            {
                _petFollowTargets.Remove(entry.Key);
                continue;
            }

            _petFollowTargets.Remove(entry.Key);
            if(pet.X==target.X&&pet.Y==target.Y)continue;
            pet=pet with {X=target.X,Y=target.Y};
            _petsByOwner[entry.Key]=pet;
            foreach(var observer in _observers.Values)
                RefreshPet(observer,owner,tick);
        }
    }

    private void HidePet(PetWorldPresence pet)
    {
        foreach(var observer in _observers.Values)
            if(observer.VisiblePets.TryGetValue(pet.OwnerActorId,out var seen)&&
               seen.PetUid==pet.PetUid&&
               seen.OwnerConnectionEpoch==pet.OwnerConnectionEpoch)
            {
                observer.VisiblePets.Remove(pet.OwnerActorId);
                Enqueue(observer,new(AoiEventKind.PetDespawn,_lastTick,
                    observer.Self.MapEpoch,Pet:seen));
            }
    }

    private void RefreshPet(Observer observer,ActorSnapshot owner,long tick)
    {
        if(!_petsByOwner.TryGetValue(owner.ActorId,out var pet)||
           pet.OwnerConnectionEpoch!=owner.ConnectionEpoch)
        {
            if(observer.VisiblePets.Remove(owner.ActorId,out var stale))
                Enqueue(observer,new(AoiEventKind.PetDespawn,tick,
                    observer.Self.MapEpoch,Pet:stale));
            return;
        }

        if(pet.OwnerMapEpoch!=owner.MapEpoch)
        {
            // A trusted map transfer establishes a fresh generation. Re-anchor
            // immediately; old-map trail targets must never leak into the new map.
            pet=pet with {OwnerMapEpoch=owner.MapEpoch,X=owner.X,Y=owner.Y};
            _petsByOwner[owner.ActorId]=pet;
            _petFollowTargets.Remove(owner.ActorId);
        }

        bool ownerVisible=observer.Self.ActorId==owner.ActorId&&
            observer.Self.ConnectionEpoch==owner.ConnectionEpoch&&
            observer.Self.MapEpoch==owner.MapEpoch;
        if(!ownerVisible&&observer.Visible.TryGetValue(owner.ActorId,out var seenOwner))
            ownerVisible=seenOwner.ConnectionEpoch==owner.ConnectionEpoch&&
                seenOwner.MapEpoch==owner.MapEpoch;
        if(!ownerVisible)
        {
            if(observer.VisiblePets.Remove(owner.ActorId,out var hidden))
                Enqueue(observer,new(AoiEventKind.PetDespawn,tick,
                    observer.Self.MapEpoch,Pet:hidden));
            return;
        }

        if(observer.VisiblePets.TryGetValue(owner.ActorId,out var current))
        {
            if(SamePetIdentity(current,pet))
            {
                if(current.X==pet.X&&current.Y==pet.Y)return;
                observer.VisiblePets[owner.ActorId]=pet;
                Enqueue(observer,new(AoiEventKind.PetDelta,tick,
                    observer.Self.MapEpoch,Pet:pet));
                return;
            }
            observer.VisiblePets.Remove(owner.ActorId);
            Enqueue(observer,new(AoiEventKind.PetDespawn,tick,
                observer.Self.MapEpoch,Pet:current));
        }
        if(observer.Visible.Count+observer.VisibleMonsters.Count+
           observer.VisibleGroundDrops.Count+observer.VisiblePets.Count>=
           _limits.MaxVisiblePerObserver)
            throw new AoiBackpressureException("AOI visible pet cap exceeded");
        observer.VisiblePets[owner.ActorId]=pet;
        Enqueue(observer,new(AoiEventKind.PetSpawn,tick,
            observer.Self.MapEpoch,Pet:pet));
    }

    private static bool SamePetIdentity(PetWorldPresence left,PetWorldPresence right) =>
        left.OwnerActorId==right.OwnerActorId&&
        left.OwnerConnectionEpoch==right.OwnerConnectionEpoch&&
        left.OwnerMapEpoch==right.OwnerMapEpoch&&
        left.PetUid==right.PetUid&&
        left.DefinitionId==right.DefinitionId&&
        left.EvolutionId==right.EvolutionId&&
        left.Level==right.Level;

    private void HideMecha(MechaWorldStateUpdate mecha)
    {
        foreach(var observer in _observers.Values)
            if(observer.VisibleMechas.TryGetValue(mecha.OwnerActorId,out var seen)&&
               seen.OwnerConnectionEpoch==mecha.OwnerConnectionEpoch&&
               seen.OwnerMapEpoch==mecha.OwnerMapEpoch)
            {
                observer.VisibleMechas.Remove(mecha.OwnerActorId);
                Enqueue(observer,new(AoiEventKind.MechaState,_lastTick,
                    observer.Self.MapEpoch,Mecha:UnmountedMecha(seen)));
            }
    }

    private void RefreshMecha(Observer observer,ActorSnapshot owner,long tick)
    {
        if(!_mechasByOwner.TryGetValue(owner.ActorId,out var mecha)||
           mecha.OwnerConnectionEpoch!=owner.ConnectionEpoch)
        {
            if(observer.VisibleMechas.Remove(owner.ActorId,out var stale))
                Enqueue(observer,new(AoiEventKind.MechaState,tick,
                    observer.Self.MapEpoch,Mecha:UnmountedMecha(stale)));
            return;
        }

        if(mecha.OwnerMapEpoch!=owner.MapEpoch)
        {
            mecha=mecha with {OwnerMapEpoch=owner.MapEpoch};
            _mechasByOwner[owner.ActorId]=mecha;
        }

        bool ownerVisible=observer.Self.ActorId==owner.ActorId&&
            observer.Self.ConnectionEpoch==owner.ConnectionEpoch&&
            observer.Self.MapEpoch==owner.MapEpoch;
        if(!ownerVisible&&observer.Visible.TryGetValue(owner.ActorId,out var seenOwner))
            ownerVisible=seenOwner.ConnectionEpoch==owner.ConnectionEpoch&&
                seenOwner.MapEpoch==owner.MapEpoch;
        if(!ownerVisible)
        {
            if(observer.VisibleMechas.Remove(owner.ActorId,out var hidden))
                Enqueue(observer,new(AoiEventKind.MechaState,tick,
                    observer.Self.MapEpoch,Mecha:UnmountedMecha(hidden)));
            return;
        }

        if(observer.VisibleMechas.TryGetValue(owner.ActorId,out var current)&&
           SameMechaIdentity(current,mecha))
            return;
        observer.VisibleMechas[owner.ActorId]=mecha;
        Enqueue(observer,new(AoiEventKind.MechaState,tick,
            observer.Self.MapEpoch,Mecha:mecha));
    }

    private static MechaWorldStateUpdate UnmountedMecha(MechaWorldStateUpdate source) =>
        new(source.OwnerActorId,source.OwnerConnectionEpoch,source.OwnerMapEpoch,
            false,0,0,0);

    private static bool SameMechaIdentity(
        MechaWorldStateUpdate left,MechaWorldStateUpdate right) =>
        left.OwnerActorId==right.OwnerActorId&&
        left.OwnerConnectionEpoch==right.OwnerConnectionEpoch&&
        left.OwnerMapEpoch==right.OwnerMapEpoch&&
        left.Mounted==right.Mounted&&
        left.MechaUid==right.MechaUid&&
        left.DefinitionId==right.DefinitionId&&
        left.Level==right.Level;

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
