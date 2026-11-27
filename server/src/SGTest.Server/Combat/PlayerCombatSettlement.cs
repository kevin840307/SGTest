using SGTest.Server.Monster;
using SGTest.Server.Gateway;
using System.Threading.Channels;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Combat;

public enum CombatPostStatus : byte
{
    Accepted,
    TargetUnavailable,
    Backpressured
}

public readonly record struct PlayerRespawnOutcome(
    PlayerRespawnStatus Status,
    int CurrentHp,
    int MaxHp);

public enum PlayerDamageSourceKind : byte
{
    Monster = 0,
    PlayerPvp = 1,
    PlayerSkill = 2
}

public readonly record struct PlayerDamageSettlement(
    long ActorId,
    long ConnectionEpoch,
    long MapEpoch,
    uint DamageSequence,
    long ServerTick,
    long AttackerMonsterEntityId,
    long AttackerMonsterRevision,
    int Damage,
    int CurrentHp,
    int MaxHp,
    bool Died,
    PlayerDamageSourceKind SourceKind = PlayerDamageSourceKind.Monster,
    long AttackerActorId = 0,long AttackerConnectionEpoch = 0,
    uint AttackerAttackSequence = 0,bool VictimWasNormalWhite = false,
    byte AttackerFacing = 0,byte VictimFacing = 0,
    int AttackerSkillId = 0,uint AttackerSkillSequence = 0,
    long AttackerMapEpoch = 0,
    NormalAttackStatus Status = NormalAttackStatus.Applied,
    uint VisualSequence = 0);

public sealed record PlayerCombatSettlementOptions(int Capacity = 256);

public sealed record PlayerCombatSettlementStatistics(
    long HitsAccepted,
    long HitsCommitted,
    long DamageCommitted,
    long DeathsCommitted,
    long BackpressureFailures,
    int ActiveActors,
    int PendingHits);

public sealed class PlayerCombatSettlementException : Exception
{
    public PlayerCombatSettlementException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Bounded single-writer authority boundary for Monster→Player combat.
/// AI/AOI only reserve projected HP and enqueue. Applied damage commits SQLite
/// off the 30Hz path before delivery; authoritative MISS emits only its reliable
/// visual result and deliberately performs no HP transaction.
/// </summary>
public sealed class PlayerCombatSettlement
{
    private sealed class State
    {
        public required long ConnectionEpoch;
        public required long MapEpoch;
        public required int Level;
        public required int Defense;
        public required int Dodge;
        public required int MaxHp;
        public required int CommittedHp;
        public required int ProjectedHp;
        public uint DamageSequence;
        public uint CombatVisualSequence;
        public uint LastRespawnSequence;
        public int Pending;
        public bool Suspended;
        public bool Closing;
        public TaskCompletionSource<bool>? Drain;
    }

    private readonly record struct QueuedHit(
        long ActorId, long ConnectionEpoch, long MapEpoch, uint DamageSequence,
        long ServerTick, long AttackerMonsterEntityId, long AttackerMonsterRevision,
        int Damage, int ExpectedHpAfter,
        PlayerDamageSourceKind SourceKind = PlayerDamageSourceKind.Monster,
        long AttackerActorId = 0,long AttackerConnectionEpoch = 0,
        uint AttackerAttackSequence = 0,bool VictimWasNormalWhite = false,
        byte AttackerFacing = 0,byte VictimFacing = 0,
        int AttackerSkillId = 0,uint AttackerSkillSequence = 0,
        TaskCompletionSource<PlayerDamageSettlement>? Completion = null,
        long AttackerMapEpoch = 0,
        NormalAttackStatus Status = NormalAttackStatus.Applied,
        uint VisualSequence = 0,
        int ExpectedMaxHp = 0);

    private readonly SqliteIdentityStore _store;
    private readonly Channel<QueuedHit> _input;
    private readonly Channel<PlayerDamageSettlement> _results;
    private readonly Func<PlayerDamageSettlement, GatewaySendStatus>? _delivery;
    private readonly Func<PlayerDamageSettlement, Task>? _committedLifecycle;
    private readonly Func<int,int> _monsterDamageRoll;
    private readonly Func<int,int> _monsterHitRoll;
    private readonly object _gate = new();
    private readonly Dictionary<long, State> _states = [];
    private Exception? _fault;
    private int _started;
    private long _accepted, _committed, _damage, _deaths, _backpressure;

    public PlayerCombatSettlement(
        SqliteIdentityStore store,
        PlayerCombatSettlementOptions? options = null,
        Func<PlayerDamageSettlement, GatewaySendStatus>? delivery = null,
        Func<PlayerDamageSettlement, Task>? committedLifecycle = null,
        Func<int,int>? monsterDamageRoll = null,
        Func<int,int>? monsterHitRoll = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _delivery = delivery;
        _committedLifecycle = committedLifecycle;
        _monsterDamageRoll = monsterDamageRoll ?? (max => Random.Shared.Next(max));
        _monsterHitRoll = monsterHitRoll ?? (max => Random.Shared.Next(max));
        var configured = options ?? new();
        if (configured.Capacity is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options));
        _input = Channel.CreateBounded<QueuedHit>(new BoundedChannelOptions(configured.Capacity)
        {
            SingleReader = true, SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _results = Channel.CreateBounded<PlayerDamageSettlement>(
            new BoundedChannelOptions(configured.Capacity)
            {
                SingleReader = true, SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public ChannelReader<PlayerDamageSettlement> Results => _results.Reader;

    public PlayerCombatSettlementStatistics Statistics
    {
        get
        {
            lock (_gate)
                return new(
                    Interlocked.Read(ref _accepted),
                    Interlocked.Read(ref _committed),
                    Interlocked.Read(ref _damage),
                    Interlocked.Read(ref _deaths),
                    Interlocked.Read(ref _backpressure),
                    _states.Count,
                    _states.Values.Sum(x => x.Pending));
        }
    }

    public bool TryAttach(ActorSnapshot actor, CharacterCombatProfileData profile)
    {
        ValidateActor(actor);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.CurrentHp < 0 || profile.MaxHp < 1 ||
            profile.CurrentHp > profile.MaxHp || profile.Level is < 1 or > ushort.MaxValue ||
            profile.Defense is < 0 or > ushort.MaxValue ||
            profile.Dodge is < 0 or > ushort.MaxValue)
            return false;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (_states.ContainsKey(actor.ActorId)) return false;
            _states.Add(actor.ActorId, new State
            {
                ConnectionEpoch = actor.ConnectionEpoch,
                MapEpoch = actor.MapEpoch,
                Level = profile.Level,
                Defense = profile.Defense,
                Dodge = profile.Dodge,
                MaxHp = profile.MaxHp,
                CommittedHp = profile.CurrentHp,
                ProjectedHp = profile.CurrentHp,
                Suspended = true
            });
            return true;
        }
    }

    public bool TryApplyLiveProfile(
        ActorSnapshot actor,CharacterCombatProfileData profile)
    {
        ValidateActor(actor);
        ArgumentNullException.ThrowIfNull(profile);
        if(profile.CurrentHp<0||profile.MaxHp<1||profile.CurrentHp>profile.MaxHp||
           profile.Level is <1 or >ushort.MaxValue||
           profile.Defense is <0 or >ushort.MaxValue||
           profile.Dodge is <0 or >ushort.MaxValue)
            return false;
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(actor.ActorId,out var state))
            {
                _states.Add(actor.ActorId,new State
                {
                    ConnectionEpoch=actor.ConnectionEpoch,
                    MapEpoch=actor.MapEpoch,
                    Level=profile.Level,
                    Defense=profile.Defense,
                    Dodge=profile.Dodge,
                    MaxHp=profile.MaxHp,
                    CommittedHp=profile.CurrentHp,
                    ProjectedHp=profile.CurrentHp,
                    Suspended=false
                });
                return true;
            }
            if(state.ConnectionEpoch!=actor.ConnectionEpoch||
               state.MapEpoch!=actor.MapEpoch||state.Closing||
               state.MaxHp!=profile.MaxHp)
                return false;
            state.Level=profile.Level;
            state.Defense=profile.Defense;
            state.Dodge=profile.Dodge;
            // Equipment commands are admitted only after GameplayReady. If an
            // earlier fail-closed equipment refresh suspended this generation,
            // its drain is complete before this method is called.
            if(state.Suspended&&state.Pending==0)
                state.Suspended=false;
            return true;
        }
    }

    public int CurrentHp(ActorSnapshot actor)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(actor.ActorId, out var state) ||
                state.ConnectionEpoch != actor.ConnectionEpoch ||
                state.MapEpoch != actor.MapEpoch ||
                state.Suspended || state.Closing)
                return 0;
            return state.ProjectedHp;
        }
    }

    internal bool IsIncapacitated(ActorSnapshot actor)
    {
        lock (_gate)
        {
            // A profile-only actor has no settlement slot. A tracked actor
            // still uses projected HP so a lethal reservation blocks immediately.
            return actor.CombatDead ||
                (_states.TryGetValue(actor.ActorId, out var state) &&
                 (state.ConnectionEpoch != actor.ConnectionEpoch ||
                  state.MapEpoch != actor.MapEpoch || state.Suspended ||
                  state.Closing || state.ProjectedHp <= 0));
        }
    }

    /// <summary>Read committed HP/MaxHP only while the exact generation is
    /// suspended and every authoritative damage reservation has drained.
    /// Resource settlement uses this immutable HP baseline while SQLite owns MP.</summary>
    public bool TryGetSuspendedCombatState(
        ActorSnapshot actor,out int hp,out int maxHp)
    {
        hp=0;
        maxHp=0;
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(actor.ActorId,out var state) ||
                state.ConnectionEpoch!=actor.ConnectionEpoch ||
                state.MapEpoch!=actor.MapEpoch || !state.Suspended ||
                state.Closing || state.Pending!=0 ||
                state.ProjectedHp!=state.CommittedHp)
                return false;
            hp=state.CommittedHp;
            maxHp=state.MaxHp;
            return true;
        }
    }

    public bool TryGetSuspendedHp(ActorSnapshot actor,out int hp)
    {
        return TryGetSuspendedCombatState(actor,out hp,out _);
    }

    /// <summary>Synchronize the projection after SQLite atomically consumed
    /// one item and healed the same HP. Must be called before reliable result
    /// delivery and before resuming Monster AI targetability.</summary>
    public bool TryApplyCommittedRecovery(ActorSnapshot actor,RecoveryUseResult result)
    {
        if(result.Status!=RecoveryUseStatus.Granted ||
            result.PreviousHp<=0 || result.CurrentHp<result.PreviousHp ||
            result.MaxHp<result.CurrentHp)
            return false;
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(actor.ActorId,out var state) ||
                state.ConnectionEpoch!=actor.ConnectionEpoch ||
                state.MapEpoch!=actor.MapEpoch ||
                !state.Suspended||state.Closing||state.Pending!=0 ||
                state.CommittedHp!=result.PreviousHp ||
                state.ProjectedHp!=result.PreviousHp ||
                state.MaxHp!=result.MaxHp)
                return false;
            state.CommittedHp=result.CurrentHp;
            state.ProjectedHp=result.CurrentHp;
            return true;
        }
    }

    /// <summary>Synchronize one already-durable player Heal with the same HP
    /// projection used by Monster/PvP damage. Caller must hold the target's
    /// gameplay gate and suspend/drain that exact actor generation first.</summary>
    public bool TryApplyCommittedSkillHeal(
        ActorSnapshot target,SkillHealResult result)
    {
        if(result.Status!=SkillHealStatus.Applied ||
           result.TargetCharacterId!=target.ActorId||
           result.HealApplied<=0||result.TargetPreviousHp<=0||
           result.TargetCurrentHp!=result.TargetPreviousHp+result.HealApplied||
           result.TargetCurrentHp>result.TargetMaxHp)
            return false;
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(target.ActorId,out var state)||
               state.ConnectionEpoch!=target.ConnectionEpoch||
               state.MapEpoch!=target.MapEpoch||
               !state.Suspended||state.Closing||state.Pending!=0||
               state.CommittedHp!=result.TargetPreviousHp||
               state.ProjectedHp!=result.TargetPreviousHp||
               state.MaxHp!=result.TargetMaxHp)
                return false;
            state.CommittedHp=result.TargetCurrentHp;
            state.ProjectedHp=result.TargetCurrentHp;
            return true;
        }
    }

    public CombatPostStatus TryPostMonsterHit(
        ActorSnapshot target, MonsterWorldEvent attack)
    {
        ValidateActor(target);
        if (attack.Kind != MonsterWorldEventKind.Attack ||
            attack.TargetActorId != target.ActorId ||
            attack.Tick < 0 || attack.Monster.EntityId <= 0 ||
            attack.Monster.EntityRevision <= 0 ||
            attack.Monster.MapId != target.MapId || attack.Damage <= 0)
            throw new ArgumentOutOfRangeException(nameof(attack));

        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(target.ActorId, out var state) ||
                state.ConnectionEpoch != target.ConnectionEpoch ||
                state.MapEpoch != target.MapEpoch ||
                state.Suspended || state.Closing || state.ProjectedHp <= 0)
                return CombatPostStatus.TargetUnavailable;

            bool sourceBacked = attack.AttackerLevel > 0;
            bool hit = !sourceBacked || NormalAttackHitRules.IsHit(
                attack.AttackerLevel, attack.AttackerHit,
                state.Level, state.Dodge, _monsterHitRoll(100));
            var status = hit ? NormalAttackStatus.Applied : NormalAttackStatus.Miss;

            int applied = 0;
            int hpAfter = state.ProjectedHp;
            uint damageSequence = 0;
            if (hit)
            {
                int computed = sourceBacked
                    ? NormalAttackDamageRules.CalculatePhysicalHit(
                        attack.Damage, attack.AttackerLevel, state.Defense, state.Level,
                        _monsterDamageRoll)
                    : Math.Max(1, attack.Damage - state.Defense / 5);
                applied = Math.Min(computed, state.ProjectedHp);
                if (state.DamageSequence == uint.MaxValue)
                    throw new InvalidOperationException("Player damage sequence exhausted");
                damageSequence = state.DamageSequence + 1;
                hpAfter -= applied;
            }

            if (state.CombatVisualSequence == uint.MaxValue)
                throw new InvalidOperationException("Player combat visual sequence exhausted");
            uint visualSequence = state.CombatVisualSequence + 1;
            if (state.Pending == 0)
                state.Drain = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (hit)
            {
                state.DamageSequence = damageSequence;
                state.ProjectedHp = hpAfter;
            }
            state.CombatVisualSequence = visualSequence;
            state.Pending++;
            var queued = new QueuedHit(
                target.ActorId, target.ConnectionEpoch, target.MapEpoch,
                damageSequence, attack.Tick, attack.Monster.EntityId,
                attack.Monster.EntityRevision, applied, hpAfter,
                Status: status, VisualSequence: visualSequence,
                ExpectedMaxHp: state.MaxHp);
            if (!_input.Writer.TryWrite(queued))
            {
                state.Pending--;
                state.CombatVisualSequence--;
                if (hit)
                {
                    state.ProjectedHp += applied;
                    state.DamageSequence--;
                }
                if (state.Pending == 0)
                {
                    state.Drain?.TrySetResult(true);
                    state.Drain = null;
                }
                Interlocked.Increment(ref _backpressure);
                return CombatPostStatus.Backpressured;
            }
            Interlocked.Increment(ref _accepted);
            return CombatPostStatus.Accepted;
        }
    }

    public CombatPostStatus TryPostPvpHit(
        ActorSnapshot target,ActorSnapshot attacker,uint attackSequence,int rawAttack,long serverTick,
        bool victimWasNormalWhite,out Task<PlayerDamageSettlement>? completion)
    {
        ValidateActor(target); ValidateActor(attacker); completion=null;
        if(attacker.ActorId==target.ActorId||attacker.MapId!=target.MapId||
           attackSequence==0||rawAttack<0||rawAttack>100_000_000||serverTick<0)
            throw new ArgumentOutOfRangeException(nameof(attacker));
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(target.ActorId,out var state)||
               state.ConnectionEpoch!=target.ConnectionEpoch||state.MapEpoch!=target.MapEpoch||
               state.Suspended||state.Closing||state.ProjectedHp<=0)
                return CombatPostStatus.TargetUnavailable;
            int applied=Math.Min(Math.Max(1,rawAttack-state.Defense/5),state.ProjectedHp);
            if(state.DamageSequence==uint.MaxValue)throw new InvalidOperationException("Player damage sequence exhausted");
            uint sequence=state.DamageSequence+1; int hpAfter=state.ProjectedHp-applied;
            if(state.Pending==0)state.Drain=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs=new TaskCompletionSource<PlayerDamageSettlement>(TaskCreationOptions.RunContinuationsAsynchronously);
            state.DamageSequence=sequence; state.ProjectedHp=hpAfter; state.Pending++;
            var queued=new QueuedHit(target.ActorId,target.ConnectionEpoch,target.MapEpoch,
                sequence,serverTick,0,0,applied,hpAfter,PlayerDamageSourceKind.PlayerPvp,
                attacker.ActorId,attacker.ConnectionEpoch,attackSequence,victimWasNormalWhite,
                attacker.Facing,target.Facing,0,0,tcs,attacker.MapEpoch);
            if(!_input.Writer.TryWrite(queued))
            {
                state.Pending--; state.ProjectedHp+=applied; state.DamageSequence--;
                if(state.Pending==0){state.Drain?.TrySetResult(true);state.Drain=null;}
                Interlocked.Increment(ref _backpressure); return CombatPostStatus.Backpressured;
            }
            completion=tcs.Task; Interlocked.Increment(ref _accepted); return CombatPostStatus.Accepted;
        }
    }

    public CombatPostStatus TryPostPvpSkillHit(
        ActorSnapshot target,ActorSnapshot attacker,uint skillSequence,int skillId,
        int fixedDamage,long serverTick,bool victimWasNormalWhite,
        out Task<PlayerDamageSettlement>? completion)
    {
        ValidateActor(target);
        ValidateActor(attacker);
        completion=null;
        if(attacker.ActorId==target.ActorId||attacker.MapId!=target.MapId||
           skillSequence==0||skillId is <1 or >100_000_000||
           fixedDamage is <1 or >100_000_000||serverTick<0)
            throw new ArgumentOutOfRangeException(nameof(attacker));
        lock(_gate)
        {
            ThrowIfFaulted();
            if(!_states.TryGetValue(target.ActorId,out var state)||
               state.ConnectionEpoch!=target.ConnectionEpoch||
               state.MapEpoch!=target.MapEpoch||state.Suspended||state.Closing||
               state.ProjectedHp<=0)
                return CombatPostStatus.TargetUnavailable;
            int applied=Math.Min(fixedDamage,state.ProjectedHp);
            if(state.DamageSequence==uint.MaxValue)
                throw new InvalidOperationException("Player damage sequence exhausted");
            uint sequence=state.DamageSequence+1;
            int hpAfter=state.ProjectedHp-applied;
            if(state.Pending==0)
                state.Drain=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var tcs=new TaskCompletionSource<PlayerDamageSettlement>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            state.DamageSequence=sequence;
            state.ProjectedHp=hpAfter;
            state.Pending++;
            var queued=new QueuedHit(
                target.ActorId,target.ConnectionEpoch,target.MapEpoch,
                sequence,serverTick,0,0,applied,hpAfter,
                PlayerDamageSourceKind.PlayerSkill,
                attacker.ActorId,attacker.ConnectionEpoch,0,victimWasNormalWhite,
                attacker.Facing,target.Facing,skillId,skillSequence,tcs,attacker.MapEpoch);
            if(!_input.Writer.TryWrite(queued))
            {
                state.Pending--;
                state.ProjectedHp+=applied;
                state.DamageSequence--;
                if(state.Pending==0)
                {
                    state.Drain?.TrySetResult(true);
                    state.Drain=null;
                }
                Interlocked.Increment(ref _backpressure);
                return CombatPostStatus.Backpressured;
            }
            completion=tcs.Task;
            Interlocked.Increment(ref _accepted);
            return CombatPostStatus.Accepted;
        }
    }

    public Task SuspendAndDrainAsync(
        long actorId, long connectionEpoch, long mapEpoch)
    {
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!TryState(actorId, connectionEpoch, mapEpoch, out var state))
                return Task.CompletedTask;
            return SuspendLocked(state);
        }
    }

    public Task SuspendAndDrainAsync(long actorId, long connectionEpoch)
    {
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state) ||
                state.ConnectionEpoch != connectionEpoch)
                return Task.CompletedTask;
            return SuspendLocked(state);
        }
    }

    public bool TryResume(ActorSnapshot actor)
    {
        ValidateActor(actor);
        return TryResumeGeneration(
            actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch);
    }

    public bool ResumeIfAttached(
        long actorId, long connectionEpoch, long mapEpoch)
    {
        if (actorId <= 0 || connectionEpoch <= 0 || mapEpoch <= 0)
            return false;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state))
                return true;
            if (state.Closing || state.Pending != 0 ||
                state.ConnectionEpoch != connectionEpoch ||
                mapEpoch < state.MapEpoch)
                return false;
            state.MapEpoch = mapEpoch;
            state.Suspended = false;
            return true;
        }
    }

    public bool TryResumeGeneration(
        long actorId, long connectionEpoch, long mapEpoch)
    {
        if (actorId <= 0 || connectionEpoch <= 0 || mapEpoch <= 0)
            return false;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state) ||
                state.Closing || state.Pending != 0 ||
                state.ConnectionEpoch != connectionEpoch ||
                mapEpoch < state.MapEpoch)
                return false;
            state.MapEpoch = mapEpoch;
            state.Suspended = false;
            return true;
        }
    }

    /// <summary>
    /// Respawn is serialized against pending damage by the same combat owner.
    /// This method leaves the actor suspended after a successful durable HP
    /// restore; the caller must first clear the trusted World death gate, queue
    /// the reliable RespawnResult, and only then TryResume the generation.
    /// </summary>
    public async Task<PlayerRespawnOutcome> RespawnFullAsync(
        long actorId, long connectionEpoch, long mapEpoch, uint respawnSequence,
        CancellationToken cancellationToken = default)
    {
        if (actorId <= 0 || connectionEpoch <= 0 || mapEpoch <= 0 ||
            respawnSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(respawnSequence));

        Task priorDrain;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state))
                return new(PlayerRespawnStatus.CombatUnavailable, 0, 0);
            if (state.ConnectionEpoch != connectionEpoch ||
                state.MapEpoch != mapEpoch || state.Closing)
                return new(PlayerRespawnStatus.StaleGeneration,
                    state.ProjectedHp, state.MaxHp);
            if (respawnSequence <= state.LastRespawnSequence)
                return new(PlayerRespawnStatus.ReplayedSequence,
                    state.ProjectedHp, state.MaxHp);
            state.LastRespawnSequence = respawnSequence;
            if (state.ProjectedHp > 0)
                return new(PlayerRespawnStatus.NotDead,
                    state.ProjectedHp, state.MaxHp);

            state.Suspended = true;
            priorDrain = state.Pending == 0
                ? Task.CompletedTask
                : state.Drain?.Task ?? throw new InvalidOperationException(
                    "Pending combat state missing drain completion");
        }

        await priorDrain.WaitAsync(cancellationToken).ConfigureAwait(false);

        TaskCompletionSource<bool> operationDrain;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state) ||
                state.ConnectionEpoch != connectionEpoch ||
                state.MapEpoch != mapEpoch || state.Closing)
                return new(PlayerRespawnStatus.StaleGeneration, 0, 0);
            if (state.ProjectedHp != 0 || state.CommittedHp != 0)
                throw new PlayerCombatSettlementException(
                    "Dead respawn timeline diverged before durable restore");
            if (state.Pending != 0)
                throw new PlayerCombatSettlementException(
                    "Respawn acquired combat state with pending damage");
            operationDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
            state.Pending = 1;
            state.Drain = operationDrain;
        }

        CharacterCombatProfile restored;
        try
        {
            restored = await _store.RestoreCharacterFullHpAsync(
                    actorId, cancellationToken).ConfigureAwait(false)
                ?? throw new PlayerCombatSettlementException(
                    "Persisted combat profile was not dead during authoritative respawn");

            lock (_gate)
            {
                if (!_states.TryGetValue(actorId, out var state) ||
                    state.ConnectionEpoch != connectionEpoch ||
                    state.MapEpoch != mapEpoch)
                    throw new PlayerCombatSettlementException(
                        "Combat generation disappeared during durable respawn");
                state.CommittedHp = restored.Data.CurrentHp;
                state.ProjectedHp = restored.Data.CurrentHp;
                state.MaxHp = restored.Data.MaxHp;
                state.Pending = 0;
                state.Drain?.TrySetResult(true);
                state.Drain = null;
            }
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _fault = error;
                if (_states.TryGetValue(actorId, out var state) &&
                    state.ConnectionEpoch == connectionEpoch)
                {
                    state.Pending = 0;
                    state.Drain?.TrySetException(error);
                    state.Drain = null;
                }
            }
            throw;
        }

        return new(PlayerRespawnStatus.Accepted,
            restored.Data.CurrentHp, restored.Data.MaxHp);
    }

    public async Task CloseAndDrainAsync(
        long actorId, long connectionEpoch, long mapEpoch)
    {
        await CloseAndDrainCoreAsync(
            actorId, connectionEpoch, expectedMapEpoch: mapEpoch).ConfigureAwait(false);
    }

    public async Task CloseAndDrainAsync(long actorId, long connectionEpoch)
    {
        await CloseAndDrainCoreAsync(
            actorId, connectionEpoch, expectedMapEpoch: null).ConfigureAwait(false);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException(
                "Exactly one combat settlement worker is allowed");
        Exception? fault = null;
        try
        {
            await foreach (var hit in _input.Reader.ReadAllAsync(cancellationToken)
                               .ConfigureAwait(false))
            {
                try
                {
                PlayerDamageSettlement result;
                if (hit.SourceKind==PlayerDamageSourceKind.Monster &&
                    hit.Status==NormalAttackStatus.Miss)
                {
                    if(hit.Damage!=0 || hit.DamageSequence!=0 ||
                       hit.VisualSequence==0 || hit.ExpectedMaxHp<=0)
                        throw new PlayerCombatSettlementException(
                            "Invalid authoritative monster MISS reservation");
                    result = new PlayerDamageSettlement(
                        hit.ActorId,hit.ConnectionEpoch,hit.MapEpoch,
                        0,hit.ServerTick,
                        hit.AttackerMonsterEntityId,hit.AttackerMonsterRevision,
                        0,hit.ExpectedHpAfter,hit.ExpectedMaxHp,false,
                        hit.SourceKind,Status:NormalAttackStatus.Miss,
                        VisualSequence:hit.VisualSequence);
                }
                else
                {
                    var committed = await _store.ApplyCharacterDamageSettlementAsync(
                        hit.ActorId, hit.Damage, cancellationToken).ConfigureAwait(false);
                    if (committed is null ||
                        committed.DamageApplied != hit.Damage ||
                        committed.Profile.Data.CurrentHp != hit.ExpectedHpAfter)
                        throw new PlayerCombatSettlementException(
                            "SQLite combat HP diverged from reserved authoritative timeline");

                    result = new PlayerDamageSettlement(
                        hit.ActorId, hit.ConnectionEpoch, hit.MapEpoch,
                        hit.DamageSequence, hit.ServerTick,
                        hit.AttackerMonsterEntityId, hit.AttackerMonsterRevision,
                        hit.Damage, committed.Profile.Data.CurrentHp,
                        committed.Profile.Data.MaxHp, committed.Died,
                        hit.SourceKind,hit.AttackerActorId,hit.AttackerConnectionEpoch,
                        hit.AttackerAttackSequence,hit.VictimWasNormalWhite,
                        hit.AttackerFacing,hit.VictimFacing,
                        hit.AttackerSkillId,hit.AttackerSkillSequence,
                        hit.AttackerMapEpoch,hit.Status,hit.VisualSequence);
                }

                if (_committedLifecycle is not null)
                    await _committedLifecycle(result).ConfigureAwait(false);

                if (_delivery is null)
                {
                    if (!_results.Writer.TryWrite(result))
                        throw new PlayerCombatSettlementException(
                            "Combat result queue full; authoritative combat event cannot be dropped");
                }
                else if (result.Status==NormalAttackStatus.Applied)
                {
                    var delivered = _delivery(result);
                    if (delivered != GatewaySendStatus.Enqueued)
                        throw new PlayerCombatSettlementException(
                            $"Committed combat result was not queued reliably: {delivered}");
                }

                lock (_gate)
                {
                    if (!_states.TryGetValue(hit.ActorId, out var state) ||
                        state.ConnectionEpoch != hit.ConnectionEpoch)
                        throw new PlayerCombatSettlementException(
                            "Combat session generation disappeared before commit accounting");
                    state.CommittedHp = result.CurrentHp;
                    state.MaxHp = result.MaxHp;
                    state.Pending--;
                    if (state.Pending < 0)
                        throw new PlayerCombatSettlementException(
                            "Combat pending count underflow");
                    if (state.Pending == 0)
                    {
                        state.Drain?.TrySetResult(true);
                        state.Drain = null;
                    }
                }
                if (result.Status==NormalAttackStatus.Applied)
                    Interlocked.Increment(ref _committed);
                Interlocked.Add(ref _damage, result.Damage);
                if (result.Died) Interlocked.Increment(ref _deaths);
                hit.Completion?.TrySetResult(result);
                }
                catch(Exception error)
                {
                    hit.Completion?.TrySetException(error);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error)
        {
            fault = error;
            lock (_gate)
            {
                _fault = error;
                foreach (var state in _states.Values)
                    state.Drain?.TrySetException(error);
            }
            throw;
        }
        finally
        {
            _results.Writer.TryComplete(fault);
        }
    }

    private Task SuspendLocked(State state)
    {
        state.Suspended = true;
        return state.Pending == 0
            ? Task.CompletedTask
            : state.Drain?.Task ?? throw new InvalidOperationException(
                "Pending combat state missing drain completion");
    }

    private async Task CloseAndDrainCoreAsync(
        long actorId, long connectionEpoch, long? expectedMapEpoch)
    {
        Task drain;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (!_states.TryGetValue(actorId, out var state) ||
                state.ConnectionEpoch != connectionEpoch ||
                (expectedMapEpoch is { } epoch && state.MapEpoch != epoch))
                return;
            state.Suspended = true;
            state.Closing = true;
            drain = state.Pending == 0
                ? Task.CompletedTask
                : state.Drain?.Task ?? throw new InvalidOperationException(
                    "Pending combat state missing drain completion");
        }
        await drain.ConfigureAwait(false);
        lock (_gate)
        {
            if (_states.TryGetValue(actorId, out var state) &&
                state.ConnectionEpoch == connectionEpoch &&
                state.Pending == 0 &&
                (expectedMapEpoch is null || state.MapEpoch == expectedMapEpoch.Value))
                _states.Remove(actorId);
        }
    }

    private bool TryState(
        long actorId, long connectionEpoch, long mapEpoch, out State state)
    {
        if (_states.TryGetValue(actorId, out state!) &&
            state.ConnectionEpoch == connectionEpoch &&
            state.MapEpoch == mapEpoch)
            return true;
        state = null!;
        return false;
    }

    private void ThrowIfFaulted()
    {
        if (_fault is not null)
            throw new PlayerCombatSettlementException(
                "Combat settlement worker faulted", _fault);
    }

    private static void ValidateActor(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 ||
            actor.MapEpoch <= 0 || actor.MapId <= 0)
            throw new ArgumentOutOfRangeException(nameof(actor));
    }
}
