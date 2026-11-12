using System.Net.WebSockets;
using System.Text.Json;

namespace SGTest.Host;

internal static class WebSocketEndpoint
{
    private const int MaxFrameBytes = 64 * 1024;

    public static void Map(WebApplication app)
    {
        app.Map("/ws", async (HttpContext http, ConnectionRegistry registry, MapActor map) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            var peer = new GamePeer(registry.AllocateId(), socket);
            registry.Add(peer);
            var writer = peer.RunWriterAsync();
            try
            {
                await ReceiveLoopAsync(http, socket, peer, map);
            }
            catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested) { }
            catch (WebSocketException) { }
            catch (JsonException) { }
            finally
            {
                // Leave command is queued before removing the peer, and world
                // does not await IO. Other observers get a despawn.
                map.TryPost(new WorldCommand(CommandKind.Leave, peer.Id));
                registry.Remove(peer.Id);
                peer.Shutdown();
                await writer;
            }
        });
    }

    private static async Task ReceiveLoopAsync(
        HttpContext http, WebSocket socket, GamePeer peer, MapActor map)
    {
        var chunk = new byte[4096];
        while (socket.State == WebSocketState.Open &&
               !http.RequestAborted.IsCancellationRequested)
        {
            using var payload = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(chunk),
                    http.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType != WebSocketMessageType.Text ||
                    payload.Length + result.Count > MaxFrameBytes)
                {
                    socket.Abort();
                    return;
                }
                payload.Write(chunk, 0, result.Count);
            } while (!result.EndOfMessage);

            using var doc = JsonDocument.Parse(payload.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("type", out var action) ||
                action.ValueKind != JsonValueKind.String)
                continue;
            var kind = action.GetString();
            WorldCommand? cmd = null;
            if (kind == "join" && !peer.Joined)
            {
                string name = doc.RootElement.TryGetProperty("name", out var n)
                    && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Player" : "Player";
                cmd = new WorldCommand(CommandKind.Join, peer.Id, name);
                // Do not permit a second join while the Actor is processing
                // the first. The peer cannot forge another EntityId.
                peer.Joined = true;
            }
            else if (kind == "move" && peer.Joined &&
                     doc.RootElement.TryGetProperty("x", out var x) &&
                     doc.RootElement.TryGetProperty("y", out var y) &&
                     x.TryGetDouble(out var destX) && y.TryGetDouble(out var destY))
            {
                cmd = new WorldCommand(CommandKind.Move, peer.Id, X: destX, Y: destY);
            }
            else if (kind == "stop" && peer.Joined)
            {
                cmd = new WorldCommand(CommandKind.Stop, peer.Id);
            }

            if (cmd.HasValue && !map.TryPost(cmd.Value))
            {
                peer.Enqueue(new { Type = "error", Message = "world_command_queue_full" });
                socket.Abort();
                return;
            }
        }
    }
}
