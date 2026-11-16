using System.Threading.Channels;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record WorldResultRouterOptions(
    int ChangedBatchCapacity = 128,
    int MaxSessions = 10000);

/// <summary>
/// Raw authoritative changed actors, NOT yet filtered by AOI.
/// Each batch retains its server Tick and is delivered exactly once through a
/// bounded queue; a future AOI/WorldReady consumer MUST drain this queue.
/// </summary>
public sealed record WorldChangedBatch(
    long Tick, IReadOnlyList<ActorSnapshot> Changed,
    IReadOnlyList<ActorLifecycleEvent>? Lifecycle = null);

public sealed record WorldResultRouterStatistics(
    long ReceiptsQueued,
    long StaleReceiptsSkipped,
    long ChangedBatchesQueued,
    long BackpressureFailures,
    int ActiveSessions);

public sealed class WorldResultRoutingException : Exception
{
    public WorldResultRoutingException(string message) : base(message) { }
}

/// <summary>
/// Single consumer of ServerWorldHost.Results. Routes reliable MoveReceipts to
/// a trusted GatewayConnection by ActorId/ConnectionEpoch, and preserves
/// authoritative Changed batches for later AOI processing instead of dropping
/// them. All queues are bounded. No database, socket or render work occurs on
/// the World single-writer thread.
/// </summary>
public sealed class WorldResultRouter
{
    private readonly object _gate = new();
    private readonly Dictionary<long, GatewayConnection> _connections = new();
    private readonly Channel<WorldChangedBatch> _changed;
    private readonly WorldResultRouterOptions _options;
    private long _queued, _stale, _changedQueued, _backpressure;
    private int _running;

    public WorldResultRouter(WorldResultRouterOptions? options = null)
    {
        _options = options ?? new WorldResultRouterOptions();
        if (_options.ChangedBatchCapacity is < 1 or > 65536 ||
            _options.MaxSessions is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(options));
        _changed = Channel.CreateBounded<WorldChangedBatch>(
            new BoundedChannelOptions(_options.ChangedBatchCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
    }

    public ChannelReader<WorldChangedBatch> ChangedBatches => _changed.Reader;

    public WorldResultRouterStatistics Statistics
    {
        get
        {
            lock (_gate)
                return new(Interlocked.Read(ref _queued), Interlocked.Read(ref _stale),
                    Interlocked.Read(ref _changedQueued), Interlocked.Read(ref _backpressure),
                    _connections.Count);
        }
    }

    /// <summary>
    /// Called ONLY after TLS Auth + CharacterChoose returned a trusted World
    /// registration. Reconnect may replace an older ConnectionEpoch, but an
    /// older/duplicate session cannot replace a newer binding.
    /// </summary>
    public bool TryAttach(GatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            if (connection.IsClosed) return false;
            var next = connection.Binding;
            if (_connections.TryGetValue(next.ActorId, out var existing))
            {
                if (ReferenceEquals(existing, connection)) return true;
                if (existing.Binding.ConnectionEpoch >= next.ConnectionEpoch) return false;
                existing.Close(); // stale socket cannot receive later authoritative receipts
            }
            else if (_connections.Count >= _options.MaxSessions)
                return false;
            _connections[next.ActorId] = connection;
            return true;
        }
    }

    /// <summary>Stale disconnects cannot detach a newer authenticated session.</summary>
    public bool TryDetach(GatewayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            var bound = connection.Binding;
            if (!_connections.TryGetValue(bound.ActorId, out var existing) ||
                !ReferenceEquals(existing, connection))
                return false;
            _connections.Remove(bound.ActorId);
            connection.Close();
            return true;
        }
    }

    /// <summary>
    /// Only a trusted transfer can advance the current Gateway's MapEpoch.
    /// Future-map receipts without that binding update fail closed.
    /// </summary>
    public bool TryAdvanceTrustedMap(GatewayConnection connection, ActorSnapshot actor)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            if (!_connections.TryGetValue(actor.ActorId, out var existing) ||
                !ReferenceEquals(existing, connection))
                return false;
            return connection.TryUpdateTrustedBinding(actor);
        }
    }

    /// <summary>
    /// Synchronous bounded fanout, called by exactly one result-consumer.
    /// Changed batches are retained first: overflow fails before any receipt
    /// in this Tick is enqueued. Receipt queue overflow also fails closed.
    /// A failure is terminal for the caller's result pump; never retry a
    /// partially routed Tick (which could duplicate reliable events).
    /// </summary>
    public void Route(WorldTickResult tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.Tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        if (tick.Changed.Count > 0 || tick.Lifecycle is { Count: > 0 })
        {
            if (!_changed.Writer.TryWrite(new WorldChangedBatch(tick.Tick, tick.Changed, tick.Lifecycle)))
                FailBackpressure("Authoritative Changed output queue full; AOI consumer is too slow");
            Interlocked.Increment(ref _changedQueued);
        }

        lock (_gate)
        {
            foreach (var receipt in tick.Receipts)
            {
                if (receipt.ActorId <= 0 || receipt.ConnectionEpoch <= 0 ||
                    receipt.MapEpoch <= 0 || receipt.MovementSequence == 0)
                    throw new WorldResultRoutingException("Invalid authoritative receipt identity");

                if (!_connections.TryGetValue(receipt.ActorId, out var connection) ||
                    connection.IsClosed)
                {
                    Interlocked.Increment(ref _stale);
                    continue; // no reachable socket after disconnect
                }
                var bound = connection.Binding;
                if (receipt.ConnectionEpoch < bound.ConnectionEpoch)
                {
                    Interlocked.Increment(ref _stale);
                    continue; // old in-flight World result after reconnect
                }
                if (receipt.ConnectionEpoch > bound.ConnectionEpoch ||
                    receipt.MapEpoch > bound.MapEpoch)
                    throw new WorldResultRoutingException(
                        "World receipt belongs to an unbound future connection/map generation");

                var status = connection.QueueReceipt(receipt);
                switch (status)
                {
                    case GatewaySendStatus.Enqueued:
                        Interlocked.Increment(ref _queued);
                        break;
                    case GatewaySendStatus.Closed:
                        Interlocked.Increment(ref _stale);
                        break;
                    case GatewaySendStatus.Backpressured:
                        FailBackpressure("Reliable receipt queue full; cannot drop or skip an ACK");
                        break;
                    default:
                        throw new WorldResultRoutingException(
                            "Trusted routing and Gateway generation disagree");
                }
            }
        }
    }

    /// <summary>Consumes host results independently of render/gameplay Tick.</summary>
    public async Task RunAsync(ChannelReader<WorldTickResult> source,
                               CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Interlocked.Exchange(ref _running, 1) != 0)
            throw new InvalidOperationException("World results must have exactly one router consumer");

        Exception? fault = null;
        try
        {
            await foreach (var tick in source.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                Route(tick);
        }
        catch (Exception error)
        {
            fault = error;
            throw;
        }
        finally
        {
            _changed.Writer.TryComplete(fault);
        }
    }

    private void FailBackpressure(string message)
    {
        Interlocked.Increment(ref _backpressure);
        throw new WorldResultRoutingException(message);
    }
}
