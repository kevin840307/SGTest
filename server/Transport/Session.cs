using System.Net.WebSockets;
using System.Threading.Channels;

namespace SGTest.Server.Transport;

public sealed class Session(int id,WebSocket socket)
{
    public int Id { get; }=id;
    public long AccountId;
    public long CharacterId;
    public uint SessionEpoch;
    public bool AuthPending;
    public bool Ready;
    public readonly HashSet<int>[] Seen=[[],[]];
    public readonly HashSet<int> SeenDrops=[];
    public readonly Channel<byte[]> Outbound=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(512){SingleReader=true,SingleWriter=true});
    public void Disconnect() => socket.Abort();
    public void Send(int op,params object?[] body)
    {
        // Never block the world actor or silently discard reliable actions.
        // A peer that cannot drain its bounded queue must reconnect/resync.
        if (!Outbound.Writer.TryWrite(Wire.Packet(op,body))) socket.Abort();
    }
    public async Task Write(CancellationToken stop)
    {
        await foreach(var packet in Outbound.Reader.ReadAllAsync(stop))
            await socket.SendAsync(packet,WebSocketMessageType.Binary,true,stop);
    }
}
