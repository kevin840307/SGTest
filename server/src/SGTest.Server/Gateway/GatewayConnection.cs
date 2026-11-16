using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

public sealed record GatewayConnectionOptions(
    int MaxBufferedInputBytes = WireProtocol.MaximumFrameBytes,
    int MaxReliableFrames = 256,
    int MaxReliableBytes = 256 * 1024);

public readonly record struct ConnectionBinding(
    long ActorId,
    long ConnectionEpoch,
    long MapEpoch)
{
    public static ConnectionBinding FromTrustedSnapshot(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 || actor.MapEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(actor),
                "Trusted actor binding requires positive identity and generations");
        return new(actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch);
    }
}

public enum GatewaySendStatus
{
    Enqueued,
    GenerationMismatch,
    Backpressured,
    Closed
}

/// <summary>
/// Transport-independent state for one authenticated connection.
/// Authentication/TLS/socket lifetime live above this class; raw client bytes can
/// never choose ActorId or ConnectionEpoch. Reconnect creates a NEW instance.
/// </summary>
public sealed class GatewayConnection
{
    private sealed class ReliableFrameQueue
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _frames = new();
        private readonly int _maxFrames;
        private readonly int _maxBytes;
        private readonly SemaphoreSlim _available = new(0);
        private int _bytes;

        public ReliableFrameQueue(int maxFrames, int maxBytes)
        {
            _maxFrames = maxFrames;
            _maxBytes = maxBytes;
        }

        public int Count { get { lock (_gate) return _frames.Count; } }
        public int Bytes { get { lock (_gate) return _bytes; } }

        public bool TryEnqueue(byte[] frame)
        {
            lock (_gate)
            {
                if (_frames.Count >= _maxFrames || frame.Length > _maxBytes - _bytes)
                    return false;
                _frames.Enqueue(frame);
                _bytes += frame.Length;
            }
            _available.Release();
            return true;
        }

        public bool TryDequeue(out byte[] frame)
        {
            if (!_available.Wait(0))
            {
                frame = Array.Empty<byte>();
                return false;
            }
            lock (_gate)
            {
                if (!_frames.TryDequeue(out frame!))
                    throw new InvalidOperationException("Reliable queue signal/state diverged");
                _bytes -= frame.Length;
                return true;
            }
        }

        public async ValueTask<byte[]?> DequeueAsync(CancellationToken cancellationToken)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_frames.TryDequeue(out var frame))
                    return null; // Close may clear after the signal was acquired.
                _bytes -= frame.Length;
                return frame;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _frames.Clear();
                _bytes = 0;
                while (_available.Wait(0)) { }
            }
        }
    }

    private readonly object _bindingGate = new();
    private readonly object _receiveGate = new();
    private readonly WireStreamParser _parser;
    private readonly ReliableFrameQueue _reliable;
    private ConnectionBinding _binding;
    private bool _closed;

    public GatewayConnection(ActorSnapshot trustedActor,
                             GatewayConnectionOptions? options = null)
    {
        var configured = options ?? new GatewayConnectionOptions();
        if (configured.MaxBufferedInputBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxBufferedInputBytes > WireProtocol.MaximumFrameBytes * 4 ||
            configured.MaxReliableFrames is < 1 or > 4096 ||
            configured.MaxReliableBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxReliableBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid bounded connection limits");

        _binding = ConnectionBinding.FromTrustedSnapshot(trustedActor);
        _parser = new WireStreamParser(configured.MaxBufferedInputBytes);
        _reliable = new ReliableFrameQueue(configured.MaxReliableFrames, configured.MaxReliableBytes);
    }

    public ConnectionBinding Binding
    {
        get { lock (_bindingGate) return _binding; }
    }

    public bool IsClosed
    {
        get { lock (_bindingGate) return _closed; }
    }

    public int BufferedInputBytes => _parser.BufferedBytes;
    public int ReliableFrames => _reliable.Count;
    public int ReliableBytes => _reliable.Bytes;

    /// <summary>
    /// Same physical/authenticated connection may advance MapEpoch after a trusted
    /// transfer. A new ConnectionEpoch MUST create a new GatewayConnection.
    /// </summary>
    public bool TryUpdateTrustedBinding(ActorSnapshot trustedActor)
    {
        ConnectionBinding next;
        try { next = ConnectionBinding.FromTrustedSnapshot(trustedActor); }
        catch (ArgumentOutOfRangeException) { return false; }

        lock (_bindingGate)
        {
            if (_closed ||
                next.ActorId != _binding.ActorId ||
                next.ConnectionEpoch != _binding.ConnectionEpoch ||
                next.MapEpoch < _binding.MapEpoch)
                return false;
            _binding = next;
            return true;
        }
    }

    /// <summary>
    /// Single receive-loop entry point. The callback must be nonblocking.
    /// Backpressure retains the complete frame in the parser; caller should stop
    /// socket reads and call ResumeClientIngress after World ingress has capacity.
    /// </summary>
    public WireFeedStatus FeedClientBytes(ReadOnlySpan<byte> bytes,
                                          Func<MoveTargetCommand, bool> postMove,
                                          out int commandsAccepted)
    {
        ArgumentNullException.ThrowIfNull(postMove);
        lock (_receiveGate)
        {
            if (IsClosed)
            {
                commandsAccepted = 0;
                return WireFeedStatus.Closed;
            }
            return _parser.Feed(bytes, frame => HandleClientFrame(frame, postMove),
                out commandsAccepted);
        }
    }

    public WireFeedStatus ResumeClientIngress(Func<MoveTargetCommand, bool> postMove,
                                              out int commandsAccepted)
    {
        ArgumentNullException.ThrowIfNull(postMove);
        lock (_receiveGate)
        {
            if (IsClosed)
            {
                commandsAccepted = 0;
                return WireFeedStatus.Closed;
            }
            return _parser.Resume(frame => HandleClientFrame(frame, postMove),
                out commandsAccepted);
        }
    }

    private WireFrameDisposition HandleClientFrame(
        ReadOnlySpan<byte> frame,
        Func<MoveTargetCommand, bool> postMove)
    {
        if (!WireProtocol.TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.MoveTarget ||
            !WireProtocol.TryDecodeMoveTarget(frame, out var move))
            return WireFrameDisposition.ProtocolViolation;

        var bound = Binding;
        var command = new MoveTargetCommand(
            bound.ActorId,
            bound.ConnectionEpoch,
            move.MapEpoch,
            move.MovementSequence,
            move.TargetX,
            move.TargetY);

        return postMove(command)
            ? WireFrameDisposition.Accepted
            : WireFrameDisposition.Backpressured;
    }

    /// <summary>
    /// Reliable authoritative receipt queue. Never DropOldest.
    /// A receipt from another Actor/Connection or a future MapEpoch is not routable
    /// on this connection. Old-map receipts on the SAME connection are allowed so
    /// the client can discard them using the receipt MapEpoch.
    /// </summary>
    public GatewaySendStatus QueueReceipt(MoveReceipt receipt)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (receipt.ActorId != bound.ActorId ||
                receipt.ConnectionEpoch != bound.ConnectionEpoch ||
                receipt.MapEpoch <= 0 ||
                receipt.MapEpoch > bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;

            var frame = WireProtocol.EncodeMoveReceipt(new MoveReceiptWire(
                receipt.MapEpoch,
                receipt.MovementSequence,
                receipt.Status,
                receipt.Tick));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued
                : GatewaySendStatus.Backpressured;
        }
    }

    /// <summary>
    /// Reliable AOI event (WorldReady/Spawn/Delta/Despawn), already filtered by
    /// trusted WorldAoi. A future-map or stale-map AOI event must never leak
    /// across the current connection/map generation. Shares receipt FIFO.
    /// </summary>
    public GatewaySendStatus QueueAoi(AoiEvent evt)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (evt.ReceiverMapEpoch != bound.MapEpoch || evt.Tick < 0)
                return GatewaySendStatus.GenerationMismatch;
            byte[] frame;
            switch (evt.Kind)
            {
                case AoiEventKind.WorldReady when evt.Entity is { } ready &&
                     ready.ActorId == bound.ActorId &&
                     ready.ConnectionEpoch == bound.ConnectionEpoch &&
                     ready.MapEpoch == bound.MapEpoch:
                    frame = WireProtocol.EncodeWorldReady(new(
                        evt.ReceiverMapEpoch, evt.Tick, ready.ActorId,
                        ready.MapId, (float)ready.X, (float)ready.Y));
                    break;

                case AoiEventKind.Spawn or AoiEventKind.Delta
                    when evt.Entity is { } entity && entity.ActorId != bound.ActorId:
                    frame = WireProtocol.EncodeEntityState(
                        evt.Kind == AoiEventKind.Spawn
                            ? WireOpcode.EntitySpawn : WireOpcode.EntityDelta,
                        new(evt.ReceiverMapEpoch, evt.Tick, entity.ActorId,
                            entity.ConnectionEpoch, entity.MapEpoch, entity.MapId,
                            entity.EntityRevision, (float)entity.X, (float)entity.Y,
                            entity.IsMoving, entity.MovementActionSequence));
                    break;

                case AoiEventKind.Despawn
                    when evt.EntityActorId > 0 && evt.EntityConnectionEpoch > 0:
                    frame = WireProtocol.EncodeEntityDespawn(new(
                        evt.ReceiverMapEpoch, evt.Tick, evt.EntityActorId,
                        evt.EntityConnectionEpoch));
                    break;

                default:
                    return GatewaySendStatus.GenerationMismatch;
            }
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued
                : GatewaySendStatus.Backpressured;
        }
    }

    public bool TryDequeueReliable(out byte[] frame)
    {
        if (IsClosed)
        {
            frame = Array.Empty<byte>();
            return false;
        }
        return _reliable.TryDequeue(out frame);
    }

    public async ValueTask<byte[]?> DequeueReliableAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return null;
        return await _reliable.DequeueAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Close()
    {
        lock (_bindingGate)
        {
            if (_closed) return;
            _closed = true;
        }
        _reliable.Clear();
    }
}
