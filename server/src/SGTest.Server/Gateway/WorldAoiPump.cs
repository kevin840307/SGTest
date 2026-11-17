namespace SGTest.Server.Gateway;

public sealed record WorldAoiPumpOptions(
    int FlushIntervalMilliseconds = 10,
    int MaxFramesPerObserverPerPoll = 32);

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
    private int _started;
    private long _processedBatches, _framesQueued;

    public WorldAoiPump(WorldResultRouter router, WorldAoi? aoi = null,
                        WorldAoiPumpOptions? options = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _aoi = aoi ?? new WorldAoi();
        _options = options ?? new WorldAoiPumpOptions();
        if (_options.FlushIntervalMilliseconds is < 1 or > 1000 ||
            _options.MaxFramesPerObserverPerPoll is < 1 or > 4096)
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
                    _aoi.Apply(batch);
                    Interlocked.Increment(ref _processedBatches);
                }
                while (_router.ResyncRequests.TryRead(out var request))
                    _aoi.TryResync(request);

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
    }
}
