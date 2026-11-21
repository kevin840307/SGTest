using SGTest.Server.Npc;
using SGTest.Server.Map;
using System.Diagnostics;
using System.Threading.Channels;

namespace SGTest.Server.World;

/// <summary>Host limits; default 30 Hz is a candidate. Not a validated gameplay movement speed.</summary>
public sealed record WorldHostOptions(
    int TickHz = 30,
    int TrustedControlCapacity = 128,
    int MaxTrustedControlsPerTick = 32,
    int ResultCapacity = 256,
    bool PublishIdleTicks = false);

/// <summary>
/// Monotonic fixed-deadline scheduler. No unbounded catch-up burst:
/// overdue periods are skipped and counted, rather than compressing world time.
/// Separately testable without clocks, threads, sockets or wall time.
/// </summary>
public sealed class FixedTickCadence
{
    private readonly long _period;
    public long Deadline { get; private set; }
    public long PeriodTimestampTicks => _period;

    public FixedTickCadence(int tickHz, long stopwatchFrequency, long startTimestamp)
    {
        if (tickHz is < 1 or > 120 || stopwatchFrequency < tickHz || startTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(tickHz), "Invalid fixed-step clock");
        _period = stopwatchFrequency / tickHz;
        Deadline = startTimestamp;
    }

    /// <returns>Number of deadlines omitted because work finished late.</returns>
    public long AdvanceAfter(long finishedAt)
    {
        Deadline = checked(Deadline + _period);
        if (finishedAt < Deadline) return 0;
        long skipped = checked((finishedAt - Deadline) / _period + 1);
        Deadline = checked(Deadline + checked(skipped * _period));
        return skipped;
    }
}

public sealed record WorldHostStatistics(
    long Ticks,
    long SkippedDeadlines,
    long MaxTickWorkMicroseconds,
    long PublishedResults,
    long ResultOverflowFailures,
    int ActiveActors,
    int PendingMoves,
    int PendingTrustedControls);

public sealed class WorldHostBackpressureException : Exception
{
    public WorldHostBackpressureException()
        : base("Bounded World Tick output is full. Host stopped without silently dropping authoritative events. Recover by resync/reconnect.") { }
}

/// <summary>
/// 30Hz dedicated single-writer runner. Does not expose a network socket,
/// credentials, port, unauthenticated session API, or a gameplay portal.
/// Trusted operations must ONLY be called from an upstream authorized host.
/// </summary>
public sealed class ServerWorldHost : IAsyncDisposable
{
    private sealed record TrustedControl(Action<ServerWorld> Apply, Action<Exception> Fail);

    private readonly BlockingGrid[] _maps;
    private readonly WorldLimits _worldLimits;
    private readonly WorldHostOptions _options;
    private readonly Channel<TrustedControl> _trusted;
    private readonly Channel<WorldTickResult> _results;
    private readonly object _gate = new();
    private readonly ManualResetEvent _stopping = new(false);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _worker;
    private ServerWorld? _world;
    private int _state; // 0=new, 1=running, 2=stopping, 3=terminated
    private int _trustedQueued;
    private long _ticks, _skipped, _maxWorkUs, _published, _overflow;
    private int _actors;

    public ServerWorldHost(IEnumerable<BlockingGrid> originalMaps,
                           WorldLimits? worldLimits = null,
                           WorldHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(originalMaps);
        _maps = originalMaps.ToArray();
        if (_maps.Length == 0 || _maps.Any(m => m is null))
            throw new ArgumentException("Original immutable maps must be present", nameof(originalMaps));
        _worldLimits = worldLimits ?? new WorldLimits();
        _options = options ?? new WorldHostOptions();
        if (_options.TickHz is < 1 or > 120 ||
            _options.TrustedControlCapacity is < 1 or > 65536 ||
            _options.MaxTrustedControlsPerTick is < 1 or > 65536 ||
            _options.ResultCapacity is < 1 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid bounded host limits");

        _trusted = Channel.CreateBounded<TrustedControl>(new BoundedChannelOptions(_options.TrustedControlCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
        _results = Channel.CreateBounded<WorldTickResult>(new BoundedChannelOptions(_options.ResultCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    public int TickHz => _options.TickHz;
    public Task Completion => _finished.Task;
    public ChannelReader<WorldTickResult> Results => _results.Reader;

    public WorldHostStatistics Statistics
    {
        get
        {
            var world = Volatile.Read(ref _world);
            return new WorldHostStatistics(
                Interlocked.Read(ref _ticks), Interlocked.Read(ref _skipped),
                Interlocked.Read(ref _maxWorkUs), Interlocked.Read(ref _published),
                Interlocked.Read(ref _overflow), Volatile.Read(ref _actors),
                world?.PendingCommands ?? 0, Volatile.Read(ref _trustedQueued));
        }
    }

    public Task StartAsync()
    {
        lock (_gate)
        {
            if (_state != 0) throw new InvalidOperationException("World host can start only once");
            _state = 1;
            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true, Name = "SGTest-World-Authoritative-Tick"
            };
            _worker.Start();
        }
        return _ready.Task;
    }

    /// <summary>
    /// Thread-safe bounded ingress. Does not authenticate ActorId; future Gateway
    /// MUST bind identity to the connection before invoking this method.
    /// </summary>
    public bool TryPostMove(MoveTargetCommand move)
    {
        lock (_gate)
        {
            return _state == 1 && Volatile.Read(ref _world)?.TryPostMove(move) == true;
        }
    }

    /// <summary>Only for authenticated, server-authorized lifecycle operations.</summary>
    public Task<ActorSnapshot?> TrustedRegisterAsync(long actorId, int mapId, double x, double y)
    {
        var tcs = new TaskCompletionSource<ActorSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedRegister(actorId, mapId, x, y, out var actor)
                ? actor : null),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    /// <summary>
    /// ONLY after upstream NPC/portal permission verification. Not a Client RPC.
    /// Emits trusted Leave/Join lifecycle via next World Tick (AOI/WorldReady).
    /// Upstream must still validate the NPC/portal destination.
    /// </summary>
    public Task<ActorSnapshot?> TrustedTransferAsync(
        long actorId, long connectionEpoch, long mapEpoch, int newMap, double x, double y)
    {
        var tcs = new TaskCompletionSource<ActorSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedTransfer(actorId, connectionEpoch, mapEpoch,
                newMap, x, y, out var actor) ? actor : null),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    /// <summary>
    /// Trusted authenticated NPC choice. PortalCatalog contains only audited
    /// converter-produced static transfers; player position and proximity are
    /// checked on the authoritative World owner thread, not the socket worker.
    /// </summary>
    public Task<NpcPortalOutcome> TrustedNpcInteractAsync(
        long actorId, long connectionEpoch, long mapEpoch,
        uint interactionSequence, int spawnId, PortalCatalog portals)
    {
        ArgumentNullException.ThrowIfNull(portals);
        var tcs = new TaskCompletionSource<NpcPortalOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedNpcInteract(
                actorId, connectionEpoch, mapEpoch,
                interactionSequence, spawnId, portals)),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    public Task<ActorSnapshot?> TrustedSetCombatDeadAsync(
        long actorId, long connectionEpoch, long mapEpoch, bool dead)
    {
        var tcs = new TaskCompletionSource<ActorSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedSetCombatDead(
                actorId, connectionEpoch, mapEpoch, dead, out var snapshot)
                ? snapshot : null),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    public Task<PvpAttackGeometry> TrustedGetPvpAttackGeometryAsync(
        long actorId,long connectionEpoch,long mapEpoch,long targetActorId,double attackRange)
    {
        var tcs=new TaskCompletionSource<PvpAttackGeometry>(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world=>tcs.TrySetResult(world.GetPvpAttackGeometry(
                actorId,connectionEpoch,mapEpoch,targetActorId,attackRange)),
            error=>tcs.TrySetException(error)));
        return tcs.Task;
    }

    public Task<ActorSnapshot?> TrustedGetActorAsync(long actorId)
    {
        var tcs = new TaskCompletionSource<ActorSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.GetActor(actorId)),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    public Task<ActorSnapshot?> TrustedPrepareDisconnectAsync(
        long actorId, long connectionEpoch, long mapEpoch)
    {
        var tcs = new TaskCompletionSource<ActorSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedPrepareDisconnect(
                actorId, connectionEpoch, mapEpoch, out var snapshot) ? snapshot : null),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    public Task<bool> TrustedDisconnectAsync(long actorId, long connectionEpoch, long mapEpoch)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        QueueTrusted(new TrustedControl(
            world => tcs.TrySetResult(world.TryTrustedDisconnect(actorId, connectionEpoch, mapEpoch)),
            error => tcs.TrySetException(error)));
        return tcs.Task;
    }

    private void QueueTrusted(TrustedControl control)
    {
        lock (_gate)
        {
            if (_state != 1 || !_trusted.Writer.TryWrite(control))
            {
                control.Fail(new InvalidOperationException("Trusted control ingress full or World Host stopping"));
                return;
            }
            Interlocked.Increment(ref _trustedQueued);
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_state == 0)
            {
                _state = 3;
                _ready.TrySetException(new OperationCanceledException("World Host never started"));
                _trusted.Writer.TryComplete();
                _results.Writer.TryComplete();
                _finished.TrySetResult();
            }
            else if (_state == 1)
            {
                _state = 2;
                _trusted.Writer.TryComplete();
                _stopping.Set(); // wake sleeping cadence immediately
            }
        }
        return _finished.Task;
    }

    private void WorkerLoop()
    {
        Exception? fault = null;
        try
        {
            // Construct the entire mutable World on THIS thread; never share it with other writers.
            var world = new ServerWorld(_maps, _worldLimits);
            Volatile.Write(ref _world, world);
            var cadence = new FixedTickCadence(_options.TickHz,
                Stopwatch.Frequency, Stopwatch.GetTimestamp());
            _ready.TrySetResult();

            while (!_stopping.WaitOne(0))
            {
                long now = Stopwatch.GetTimestamp();
                if (now < cadence.Deadline)
                {
                    long remaining = cadence.Deadline - now;
                    int milliseconds = Math.Clamp(
                        (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency), 1, 20);
                    _stopping.WaitOne(milliseconds); // never busy-spin while idle
                    continue;
                }

                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < _options.MaxTrustedControlsPerTick &&
                                _trusted.Reader.TryRead(out var request); i++)
                {
                    Interlocked.Decrement(ref _trustedQueued);
                    try { request.Apply(world); }
                    catch (Exception error) { request.Fail(error); }
                }

                var delta = world.Tick();
                if (_options.PublishIdleTicks ||
                    delta.Receipts.Count > 0 || delta.Changed.Count > 0 ||
                    delta.Lifecycle is { Count: > 0 })
                {
                    // Monster-enabled hosts preserve every authoritative 30Hz
                    // Tick as the single AI clock. Plain player-only hosts retain
                    // the low-volume event-only behavior.
                    if (!_results.Writer.TryWrite(delta))
                    {
                        Interlocked.Increment(ref _overflow);
                        throw new WorldHostBackpressureException();
                    }
                    Interlocked.Increment(ref _published);
                }

                long finished = Stopwatch.GetTimestamp();
                long microseconds = (long)((finished - started) * 1_000_000.0 / Stopwatch.Frequency);
                UpdateMax(ref _maxWorkUs, microseconds);
                Volatile.Write(ref _actors, world.ActorCount);
                Interlocked.Exchange(ref _ticks, delta.Tick);
                long skipped = cadence.AdvanceAfter(finished);
                if (skipped > 0) Interlocked.Add(ref _skipped, skipped);
            }
        }
        catch (Exception error)
        {
            fault = error;
            _ready.TrySetException(error);
        }
        finally
        {
            lock (_gate)
            {
                _state = 3;
                _trusted.Writer.TryComplete();
            }
            // All already-admitted trusted requests receive a terminal failure, never hang.
            while (_trusted.Reader.TryRead(out var pending))
            {
                Interlocked.Decrement(ref _trustedQueued);
                pending.Fail(fault ?? new OperationCanceledException("World Host stopped"));
            }
            _results.Writer.TryComplete(fault);
            if (fault is null) _finished.TrySetResult();
            else _finished.TrySetException(fault);
        }
    }

    private static void UpdateMax(ref long maximum, long value)
    {
        while (true)
        {
            long before = Interlocked.Read(ref maximum);
            if (value <= before || Interlocked.CompareExchange(ref maximum, value, before) == before)
                return;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            // Also release the native WaitHandle on an intentionally faulted
            // host (for example result queue overload in fail-closed mode).
            _stopping.Dispose();
        }
    }
}
