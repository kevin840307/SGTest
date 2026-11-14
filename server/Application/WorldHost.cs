using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using SGTest.Server.Domain;
using SGTest.Server.Transport;

namespace SGTest.Server.Application;

public readonly record struct Command(int Id,int Op,object?[] Body,Session? Session=null);

public sealed class WorldHost(World world,int map,string fingerprint,string populationHash) : BackgroundService
{
    public readonly Channel<Command> Commands=Channel.CreateBounded<Command>(8192);
    readonly Dictionary<int,Session> sessions=[];
    readonly List<double> timings=[];
    public World World { get; }=world;
    public bool Submit(Command command) => Commands.Writer.TryWrite(command);
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Monotonic deadline, independent of rendering and socket I/O. Catch up
        // elapsed fixed steps; never change simulation delta to hide overload.
        var clock=Stopwatch.StartNew(); double deadline=1.0/30;
        while(!stop.IsCancellationRequested)
        {
            double remaining=deadline-clock.Elapsed.TotalSeconds;
            if(remaining>0) { await Task.Delay(TimeSpan.FromSeconds(remaining),stop); continue; }
            long started=Stopwatch.GetTimestamp();
            for(int count=0;count<2048 && Commands.Reader.TryRead(out var command);count++) Handle(command);
            World.Step(1.0/30);
            foreach(var action in World.Events)
                foreach(var session in sessions.Values)
                    if(session.Ready && Near(World.Players[session.Id].Position,action.Position))
                        session.Send(4,action.Kind,action.Id,action.Facing,action.Pose,action.Started,action.Hp,action.Revision,action.Position.X,action.Position.Y,World.Tick,action.Definition);
            World.Events.Clear();
            if(World.Tick%3==0) foreach(var session in sessions.Values) if(session.Ready) Snapshot(session);
            if(World.Tick%15==0) foreach(var session in sessions.Values) if(session.Ready) Ground(session);
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if(World.Tick%300==0)
            {
                timings.Sort(); Console.WriteLine($"server_metrics tick={World.Tick} tick_p95_ms={timings[(int)(timings.Count*.95)]:F3} tick_p99_ms={timings[(int)(timings.Count*.99)]:F3} players={sessions.Count}"); timings.Clear();
            }
            deadline+=1.0/30;
        }
    }
    static bool Near(Vector2 a,Vector2 b) => Vector2.DistanceSquared(a,b)<=900*900;
    void Handle(Command command)
    {
        int id=command.Id; var body=command.Body;
        if(command.Op==-1 && command.Session is {} joining)
        {
            sessions.Add(id,joining); World.Join(id); joining.Send(0,id); joining.Send(1,map,fingerprint,populationHash);
            Console.WriteLine($"player {id} joined"); return;
        }
        if(command.Op==-2) { sessions.Remove(id); World.Players.Remove(id); return; }
        if(!sessions.TryGetValue(id,out var session)) return;
        try
        {
            switch(command.Op)
            {
                case 24 when body.Length==3 && Wire.Integer(body[0])==map && body[1] is string hash && hash==fingerprint && body[2] is string population && population==populationHash:
                    session.Ready=true; break;
                case 20 when session.Ready && body.Length==2 && body[1] is byte[] input && input.Length is >0 and <=36 && input.Length%12==0:
                    uint epoch=checked((uint)Wire.Integer(body[0]));
                    for(int at=0;at<input.Length;at+=12)
                        World.QueueInput(id,epoch,BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(at)),
                            new Vector2(BitConverter.ToSingle(input,at+4),BitConverter.ToSingle(input,at+8)));
                    break;
                case 21 when session.Ready && body.Length==1:
                    int first=World.NextDrop;
                    if(World.Attack(id,checked((int)Wire.Integer(body[0]))) && first!=World.NextDrop)
                        foreach(var peer in sessions.Values) if(peer.Ready) Ground(peer);
                    break;
                case 22 when session.Ready && body.Length==1:
                    if(World.Pickup(id,checked((int)Wire.Integer(body[0]))))
                    {
                        session.Send(6,(object)World.Players[id].Inventory.Select(p=>(object?)new object?[]{p.Key,p.Value}).ToArray());
                        foreach(var peer in sessions.Values) if(peer.Ready) Ground(peer);
                    }
                    break;
                case 23 when body.Length==1: session.Send(8,Wire.Real(body[0]),World.Time); break;
                default: throw new InvalidDataException("Unknown command/schema");
            }
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException) { session.Disconnect(); }
    }
    void Owner(Session s)
    {
        var p=World.Players[s.Id];
        s.Send(2,World.Tick,World.Time,p.Acknowledged,p.LifeEpoch,p.Position.X,p.Position.Y,p.Hp,p.Facing,p.Pose,p.ActionTime,p.Revision);
    }
    void Snapshot(Session s)
    {
        Vector2 center=World.Players[s.Id].Position;
        for(int kind=0;kind<2;kind++)
        {
            IEnumerable<KeyValuePair<int,Actor>> source=kind==0
                ? World.Players.Select(p=>new KeyValuePair<int,Actor>(p.Key,p.Value))
                : World.Monsters.Select(p=>new KeyValuePair<int,Actor>(p.Key,p.Value));
            var rows=source.Where(p=>Near(center,p.Value.Position)).ToArray();
            var current=rows.Select(p=>p.Key).ToHashSet();
            var removed=s.Seen[kind].Except(current).Select(id=>(object?)id).ToArray();
            if(removed.Length>0) s.Send(7,World.Tick,kind,removed);
            s.Seen[kind]=current;
            foreach(var chunk in rows.Chunk(SnapshotCodec.RowsPerPacket))
                s.Send(3,World.Tick,World.Time,kind,SnapshotCodec.Encode(chunk));
        }
        Owner(s);
    }
    void Ground(Session s)
    {
        Vector2 center=World.Players[s.Id].Position;
        var current=World.Drops.Where(p=>Near(center,p.Value.Position)).ToDictionary(p=>p.Key,p=>p.Value);
        var added=current.Where(p=>!s.SeenDrops.Contains(p.Key)).Select(p=>(object?)new object?[]{p.Key,p.Value.Position.X,p.Value.Position.Y,p.Value.Item}).ToArray();
        var removed=s.SeenDrops.Except(current.Keys).Select(id=>(object?)id).ToArray();
        foreach(var chunk in added.Chunk(20)) s.Send(5,chunk,Array.Empty<object?>());
        foreach(var chunk in removed.Chunk(128)) s.Send(5,Array.Empty<object?>(),chunk);
        s.SeenDrops.Clear(); s.SeenDrops.UnionWith(current.Keys);
    }
}
