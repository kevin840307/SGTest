using System.Diagnostics;
using System.Threading.Channels;
using SGTest.Domain;

namespace SGTest.Host;

internal enum CommandKind { Join, Move, Stop, Leave }

internal readonly record struct WorldCommand(
    CommandKind Kind, long EntityId, string Name = "",
    double X = 0, double Y = 0);

/// <summary>
/// Exactly one task owns all mutable WorldSimulation state; socket IO is
/// dispatched into bounded peer queues, and DB IO is absent from tick paths.
/// </summary>
internal sealed class MapActor(ConnectionRegistry peers, ILogger<MapActor> logger)
    : BackgroundService
{
    private readonly WorldSimulation _world = new();
    private readonly Channel<WorldCommand> _commands = Channel.CreateBounded<WorldCommand>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true, SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private long _currentTick;
    public long CurrentTick => Volatile.Read(ref _currentTick);
    public bool TryPost(WorldCommand command) => _commands.Writer.TryWrite(command);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / WorldSimulation.TickRate));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var started = Stopwatch.GetTimestamp();
                var processed = 0;
                while (processed < 4096 && _commands.Reader.TryRead(out var command))
                {
                    Apply(command);
                    processed++;
                }
                foreach (var evt in _world.Step())
                    peers.Broadcast(evt);
                Volatile.Write(ref _currentTick, _world.Tick);
                var elapsed = Stopwatch.GetElapsedTime(started);
                if (elapsed.TotalMilliseconds > 33.33)
                    logger.LogWarning("MAP_TICK_OVER_BUDGET elapsed_ms={Elapsed} commands={Commands}",
                        elapsed.TotalMilliseconds, processed);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private void Apply(WorldCommand cmd)
    {
        switch (cmd.Kind)
        {
            case CommandKind.Join:
                if (!peers.TryGet(cmd.EntityId, out var peer) || peer is null)
                    return;
                var existing = _world.Snapshot();
                if (!_world.TryJoin(cmd.EntityId, cmd.Name, out var spawned)
                    || spawned is null)
                    return;
                // Per-connection single writer preserves welcome->snapshot->changes.
                peer.Joined = true;
                peer.Enqueue(new
                {
                    Type = "welcome", OwnedId = cmd.EntityId,
                    MapEpoch = WorldSimulation.MapEpoch, ServerTick = _world.Tick
                });
                foreach (var evt in existing)
                    peer.Enqueue(evt);
                peer.Enqueue(spawned);
                peers.Broadcast(spawned, cmd.EntityId);
                return;

            case CommandKind.Move:
                if (_world.TryMove(cmd.EntityId, new Point2(cmd.X, cmd.Y), out var moved)
                    && moved is not null)
                    peers.Broadcast(moved);
                return;

            case CommandKind.Stop:
                if (_world.TryStop(cmd.EntityId, out var stopped) && stopped is not null)
                    peers.Broadcast(stopped);
                return;

            case CommandKind.Leave:
                if (_world.TryLeave(cmd.EntityId, out var left) && left is not null)
                    peers.Broadcast(left, cmd.EntityId);
                return;
        }
    }
}
