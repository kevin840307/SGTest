using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SGTest.Domain;

namespace SGTest.Host;

internal sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<long, GamePeer> _peers = new();
    private long _lastId = 100;
    public long AllocateId() => Interlocked.Increment(ref _lastId);
    public void Add(GamePeer peer) => _peers[peer.Id] = peer;
    public bool TryGet(long id, out GamePeer? peer) => _peers.TryGetValue(id, out peer);
    public void Remove(long id) => _peers.TryRemove(id, out _);
    public void Broadcast(WorldEvent evt, long exceptId = 0)
    {
        foreach (var (id, peer) in _peers)
        {
            if (id != exceptId && peer.Joined)
                peer.Enqueue(evt);
        }
    }
}

internal sealed class GamePeer(long id, WebSocket socket)
{
    private readonly Channel<string> _outbound = Channel.CreateBounded<string>(
        new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public long Id { get; } = id;
    public bool Joined { get; set; }

    public bool Enqueue(object packet)
    {
        var json = JsonSerializer.Serialize(packet, WireJson);
        if (_outbound.Writer.TryWrite(json))
            return true;
        // Slow consumer: close, then reconnect+snapshot; never block MapActor
        // or silently discard a reliable event for an active connection.
        socket.Abort();
        return false;
    }

    public async Task RunWriterAsync()
    {
        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync())
            {
                if (socket.State != WebSocketState.Open)
                    return;
                var bytes = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(
                    new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                    endOfMessage: true, CancellationToken.None);
            }
        }
        catch (WebSocketException) { }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Shutdown()
    {
        _outbound.Writer.TryComplete();
        socket.Abort();
    }
}
