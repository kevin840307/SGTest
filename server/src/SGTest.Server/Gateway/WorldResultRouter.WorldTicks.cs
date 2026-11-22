using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Threading.Channels;
using SGTest.Server.World;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

/// <summary>
/// Exactly one World Tick result consumer and strict overflow lifecycle.
/// Authoritative Tick order and reliable receipt order remain unchanged.
/// </summary>
public sealed partial class WorldResultRouter
{
    /// <summary>
    /// Synchronous bounded fanout, called by exactly one result-consumer.
    /// Every authoritative World Tick is retained first: overflow fails before any receipt
    /// in this Tick is enqueued. Receipt queue overflow also fails closed.
    /// A failure is terminal for the caller's result pump; never retry a
    /// partially routed Tick (which could duplicate reliable events).
    /// </summary>
    public void Route(WorldTickResult tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.Tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));

        // Preserve EVERY authoritative World Tick on the single AOI/AI timeline.
        // An empty player Changed/Lifecycle batch is still the clock source for
        // map-local monster AI. It produces no player AOI traffic by itself.
        // This avoids introducing a second independent AI timer/scheduler.
        var batch = new WorldChangedBatch(tick.Tick, tick.Changed, tick.Lifecycle);
        if (!_changed.Writer.TryWrite(batch))
            FailBackpressure("Authoritative World Tick output queue full; AOI/AI consumer is too slow");
        Interlocked.Increment(ref _changedQueued);
        // Observer contract is memory-only/nonblocking. Position checkpointing
        // ignores empty actor batches; no SQLite work occurs in this call.
        _changedObserver?.Invoke(batch);

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

    /// <summary>Consumes every host Tick exactly once, independently of render cadence.</summary>
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
            _resync.Writer.TryComplete(fault);
            _attacks.Writer.TryComplete(fault);
            _skills.Writer.TryComplete(fault);
            _skillPreflights.Writer.TryComplete(fault);
            _preparedSkills.Writer.TryComplete(fault);
            _claimedGroundDrops.Writer.TryComplete(fault);
            _playerDamageVisuals.Writer.TryComplete(fault);
            _playerRespawnVisuals.Writer.TryComplete(fault);
            _playerIdentityUpdates.Writer.TryComplete(fault);
            _playerPvpCombatVisuals.Writer.TryComplete(fault);
            _playerPvpSkillVisuals.Writer.TryComplete(fault);
            _petWorldUpdates.Writer.TryComplete(fault);
        }
    }

    private void FailBackpressure(string message)
    {
        Interlocked.Increment(ref _backpressure);
        throw new WorldResultRoutingException(message);
    }
}
