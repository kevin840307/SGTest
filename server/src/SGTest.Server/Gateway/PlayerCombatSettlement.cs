using System.Threading.Channels;
using SGTest.Server.Identity;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public enum CombatPostStatus : byte
{
    Accepted,
    TargetUnavailable,
    Backpressured
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
    bool Died);

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
/// Bounded single-writer durability boundary for Monster→Player damage.
/// AI/AOI only reserve projected HP and enqueue. SQLite is committed off the
/// 30Hz World/AOI path; a reliable result exists only after that transaction.
/// </summary>
public sealed class PlayerCombatSettlement
{
    private sealed class State
    {
        public required long ConnectionEpoch;
        public required long MapEpoch;
        public required int Defense;
        public required int MaxHp;
        public required int CommittedHp;
        public required int ProjectedHp;
        public uint DamageSequence;
        public int Pending;
        public bool Suspended;
        public bool Closing;
        public TaskCompletionSource<bool>? Drain;
    }

    private readonly record struct QueuedHit(
        long ActorId, long ConnectionEpoch, long MapEpoch, uint DamageSequence,
        long ServerTick, long AttackerMonsterEntityId, long AttackerMonsterRevision,
        int Damage, int ExpectedHpAfter);

    private readonly SqliteIdentityStore _store;
    private readonly Channel<QueuedHit> _input;
    private readonly Channel<PlayerDamageSettlement> _results;
    private readonly Func<PlayerDamageSettlement, GatewaySendStatus>? _delivery;
    private readonly object _gate = new();
    private readonly Dictionary<long, State> _states = [];
    private Exception? _fault;
    private int _started;
    private long _accepted, _committed, _damage, _deaths, _backpressure;

    public PlayerCombatSettlement(
        SqliteIdentityStore store,
        PlayerCombatSettlementOptions? options = null,
        Func<PlayerDamageSettlement, GatewaySendStatus>? delivery = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _delivery = delivery;
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
            profile.CurrentHp > profile.MaxHp || profile.Defense < 0)
            return false;
        lock (_gate)
        {
            ThrowIfFaulted();
            if (_states.ContainsKey(actor.ActorId)) return false;
            _states.Add(actor.ActorId, new State
            {
                ConnectionEpoch = actor.ConnectionEpoch,
                MapEpoch = actor.MapEpoch,
                Defense = profile.Defense,
                MaxHp = profile.MaxHp,
                CommittedHp = profile.CurrentHp,
                ProjectedHp = profile.CurrentHp,
                Suspended = true
            });
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

            int computed = Math.Max(1, attack.Damage - state.Defense / 5);
            int applied = Math.Min(computed, state.ProjectedHp);
            if (state.DamageSequence == uint.MaxValue)
                throw new InvalidOperationException("Player damage sequence exhausted");
            uint sequence = state.DamageSequence + 1;
            int hpAfter = state.ProjectedHp - applied;
            if (state.Pending == 0)
                state.Drain = new(TaskCreationOptions.RunContinuationsAsynchronously);
            state.DamageSequence = sequence;
            state.ProjectedHp = hpAfter;
            state.Pending++;
            var queued = new QueuedHit(
                target.ActorId, target.ConnectionEpoch, target.MapEpoch,
                sequence, attack.Tick, attack.Monster.EntityId,
                attack.Monster.EntityRevision, applied, hpAfter);
            if (!_input.Writer.TryWrite(queued))
            {
                state.Pending--;
                state.ProjectedHp += applied;
                state.DamageSequence--;
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
                var committed = await _store.ApplyCharacterDamageSettlementAsync(
                    hit.ActorId, hit.Damage, cancellationToken).ConfigureAwait(false);
                if (committed is null ||
                    committed.DamageApplied != hit.Damage ||
                    committed.Profile.Data.CurrentHp != hit.ExpectedHpAfter)
                    throw new PlayerCombatSettlementException(
                        "SQLite combat HP diverged from reserved authoritative timeline");

                var result = new PlayerDamageSettlement(
                    hit.ActorId, hit.ConnectionEpoch, hit.MapEpoch,
                    hit.DamageSequence, hit.ServerTick,
                    hit.AttackerMonsterEntityId, hit.AttackerMonsterRevision,
                    hit.Damage, committed.Profile.Data.CurrentHp,
                    committed.Profile.Data.MaxHp, committed.Died);

                if (_delivery is null)
                {
                    if (!_results.Writer.TryWrite(result))
                        throw new PlayerCombatSettlementException(
                            "Combat result queue full; committed reliable damage cannot be dropped");
                }
                else
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
                Interlocked.Increment(ref _committed);
                Interlocked.Add(ref _damage, result.Damage);
                if (result.Died) Interlocked.Increment(ref _deaths);
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
