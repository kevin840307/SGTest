using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using SGTest.Server.Application;
using SGTest.Server.Domain;
using SGTest.Server.Infrastructure;
using SGTest.Server.Transport;

var options=args.Where(a=>a.StartsWith("--") && a.Contains('=')).Select(a=>a[2..].Split('=',2)).ToDictionary(a=>a[0],a=>a[1]);
int Option(string name,int fallback) => options.TryGetValue(name,out var text) ? int.Parse(text) : fallback;
bool arena=args.Contains("--combat-arena");
int map=arena ? 0 : Option("map",13), port=Option("port",27888);
string content=Path.GetFullPath(options.GetValueOrDefault("content",Path.Combine(AppContext.BaseDirectory,"../../../content")));
string fingerprint="", populationHash="";
Population? population=null;
CollisionGrid? grid=null;
if(map!=0)
{
    string folder=Path.Combine(content,"maps",map.ToString());
    using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"world.json")));
    var metadata=manifest.RootElement;
    if(metadata.GetProperty("version").GetInt32()!=2) throw new InvalidDataException("Unsupported map version");
    string collision=Path.Combine(folder,"blocking.bin");
    if(!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(collision))).ToLowerInvariant().Equals(metadata.GetProperty("collision_sha256").GetString(),StringComparison.Ordinal))
        throw new InvalidDataException("Map hash mismatch");
    grid=CollisionGrid.Load(collision); fingerprint=metadata.GetProperty("fingerprint").GetString()!;
    string populationPath=Path.Combine(folder,"population.json");
    population=Population.Load(populationPath,map);
    populationHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(populationPath))).ToLowerInvariant();
}
var world=new World(grid); world.Populate(arena ? 1 : Math.Clamp(Option("monsters",population is null ? 80 : -1),-1,10000),arena ? 0 : Math.Clamp(Option("drops",0),0,5000),population);
if(arena) { world.Monsters[1].Position=new(1230,800); world.Monsters[1].Damage=50; }
string accountMode=options.GetValueOrDefault("accounts","sqlite").ToLowerInvariant();
string databasePath=Path.GetFullPath(options.GetValueOrDefault("db",Path.Combine(AppContext.BaseDirectory,"../../../data/sgtest.db")));
IAccountStore accountStore=accountMode switch
{
    "memory" => new MemoryAccountStore(),
    "sqlite" => new SqliteAccountStore(databasePath),
    _ => throw new ArgumentException("--accounts must be sqlite or memory")
};
var accounts=new AccountService(accountStore);
var host=new WorldHost(world,map,fingerprint,populationHash,accounts);
Console.WriteLine(accountMode=="sqlite" ? $"account_store=sqlite db={databasePath}" : "account_store=memory");
var builder=WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://{options.GetValueOrDefault("bind","127.0.0.1")}:{port}");
builder.Services.AddSingleton<IHostedService>(host);
var app=builder.Build(); app.UseWebSockets(new WebSocketOptions{KeepAliveInterval=TimeSpan.FromSeconds(15)});
int nextId=1;
app.MapGet("/health",()=>Results.Ok(new{protocol=2,map,players=host.ActivePlayers,accounts=accountMode}));
app.Map("/game",async context=>
{
    if(!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode=400; return; }
    using var socket=await context.WebSockets.AcceptWebSocketAsync();
    int id=Interlocked.Increment(ref nextId); var session=new Session(id,socket);
    using var stop=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    if(!host.Submit(new(id,-1,[],session))) { socket.Abort(); return; }
    var writer=session.Write(stop.Token);
    byte[] buffer=new byte[Wire.MaxPacket];
    try
    {
        int count=0, rate=0; long window=Environment.TickCount64;
        while(socket.State==WebSocketState.Open)
        {
            if(count==buffer.Length) throw new InvalidDataException("Oversized packet");
            var received=await socket.ReceiveAsync(buffer.AsMemory(count),stop.Token);
            if(received.MessageType==WebSocketMessageType.Close) break;
            if(received.MessageType!=WebSocketMessageType.Binary) throw new InvalidDataException("Expected MessagePack binary");
            count+=received.Count;
            if(!received.EndOfMessage) continue;
            long now=Environment.TickCount64;
            if(now-window>=1000) { window=now; rate=0; }
            if(++rate>240) throw new InvalidDataException("Command rate exceeded");
            var message=Wire.Decode(buffer[..count]); count=0;
            if(!host.Submit(new(id,checked((int)Wire.Integer(message[1])),(object?[])message[2]!))) throw new InvalidDataException("World queue full");
        }
    }
    catch(Exception ex) when(ex is WebSocketException or OperationCanceledException or InvalidDataException or OverflowException)
    { Console.WriteLine($"peer {id} disconnected: {ex.Message}"); }
    finally
    {
        socket.Abort(); stop.Cancel(); session.Outbound.Writer.TryComplete();
        // Disconnect must be enqueued even during input saturation.
        await host.Commands.Writer.WriteAsync(new(id,-2,[]));
        try { await writer; } catch(Exception ex) when(ex is WebSocketException or OperationCanceledException or InvalidDataException) { }
    }
});
Console.WriteLine($"population_loaded monsters={world.Monsters.Count} definitions={world.Monsters.Values.Select(m=>m.Definition).Distinct().Count()}");
Console.WriteLine($"SGTest C# server listening ws://127.0.0.1:{port}/game map={map}");
await app.RunAsync();
