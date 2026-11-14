using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using SGTest.Server.Domain;
using SGTest.Server.Transport;

namespace SGTest.Server.Application;

public readonly record struct Command(int Id,int Op,object?[] Body,Session? Session=null);

public sealed class WorldHost(World world,int map,string fingerprint,string populationHash,AccountService accounts) : BackgroundService
{
    // 1440x1080 plus client-side visual margin reaches roughly 1078px from
    // screen center. Keep a bounded enter/exit hysteresis so an actor cannot
    // despawn while still visible and respawn later at a newer coordinate.
    public const float InterestEnterRadius=1100f, InterestExitRadius=1250f;
    public readonly Channel<Command> Commands=Channel.CreateBounded<Command>(8192);
    readonly Dictionary<int,Session> sessions=[];
    readonly Dictionary<long,int> accountSessions=[];
    readonly SemaphoreSlim authWorkers=new(4,4);
    readonly List<double> timings=[];
    uint nextSessionEpoch;
    int activePlayers;
    public World World { get; }=world;
    public int ActivePlayers => Volatile.Read(ref activePlayers);
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
                    if(IsWorldReady(session) && InInterest(World.Players[session.Id].Position,action.Position,
                        action.Kind is >=0 and <2 && session.Seen[action.Kind].Contains(action.Id)))
                        session.Send(4,action.Kind,action.Id,action.Facing,action.Pose,action.Started,action.Hp,
                            action.Revision,action.Position.X,action.Position.Y,World.Tick,action.Definition);
            World.Events.Clear();
            if(World.Tick%3==0) foreach(var session in sessions.Values) if(IsWorldReady(session)) Snapshot(session);
            if(World.Tick%15==0) foreach(var session in sessions.Values) if(IsWorldReady(session)) Ground(session);
            timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if(World.Tick%300==0)
            {
                timings.Sort();
                Console.WriteLine($"server_metrics tick={World.Tick} tick_p95_ms={timings[(int)(timings.Count*.95)]:F3} tick_p99_ms={timings[(int)(timings.Count*.99)]:F3} players={World.Players.Count}");
                timings.Clear();
            }
            deadline+=1.0/30;
        }
    }

    static bool IsWorldReady(Session session) => session.Ready && session.CharacterId!=0;
    public static bool InInterest(Vector2 center,Vector2 position,bool alreadySeen)
    {
        float radius=alreadySeen ? InterestExitRadius : InterestEnterRadius;
        return Vector2.DistanceSquared(center,position)<=radius*radius;
    }

    void ClearInterest(Session session)
    {
        foreach(var seen in session.Seen) seen.Clear();
        session.SeenDrops.Clear();
    }

    void LeaveWorld(Session session)
    {
        session.Ready=false;
        World.Players.Remove(session.Id);
        Volatile.Write(ref activePlayers,World.Players.Count);
        session.CharacterId=0;
        ClearInterest(session);
    }

    void LogoutAccount(Session session)
    {
        long accountId=session.AccountId;
        LeaveWorld(session);
        if(accountId!=0 && accountSessions.TryGetValue(accountId,out int current) && current==session.Id)
            accountSessions.Remove(accountId);
        session.AccountId=0;
        session.SessionEpoch=0;
    }

    void DisconnectSession(Session session)
    {
        LogoutAccount(session);
        sessions.Remove(session.Id);
    }

    void SendCharacters(Session session)
    {
        object?[] rows=accounts.Characters(session.AccountId)
            .Select(c=>(object?)new object?[]{c.Id,c.Name}).ToArray();
        // Cast keeps the character row array as one protocol body element;
        // without it C# params would expand rows (and an empty list becomes no body).
        session.Send(11,(object)rows);
    }

    void ScheduleAuthentication(Session session,Func<IdentityResult> work)
    {
        if(session.AuthPending)
        {
            session.Send(10,false,"auth_pending","Authentication is already in progress.",0L);
            return;
        }
        session.AuthPending=true;
        _=Task.Run(async () =>
        {
            IdentityResult result;
            await authWorkers.WaitAsync();
            try { result=work(); }
            catch(Exception)
            {
                result=new(false,"auth_error","Authentication failed.");
            }
            finally { authWorkers.Release(); }
            if(!Commands.Writer.TryWrite(new(session.Id,-10,[result])))
                session.Disconnect();
        });
    }

    void Authenticate(Session session,IdentityResult result)
    {
        if(!result.Success)
        {
            session.Send(10,false,result.Code,result.Message,0L);
            return;
        }
        if(session.AccountId!=0 && session.AccountId!=result.AccountId)
            LogoutAccount(session);
        if(accountSessions.TryGetValue(result.AccountId,out int previousId) && previousId!=session.Id &&
            sessions.TryGetValue(previousId,out var previous))
        {
            // A new login owns the account immediately. Detach the old peer from
            // World/account first, then reliably notify it. Keep its socket alive
            // at the login screen instead of aborting before the outbound queue flushes.
            LeaveWorld(previous);
            previous.AccountId=0;
            previous.SessionEpoch=0;
            Console.WriteLine($"account {result.AccountId} replaced peer {previous.Id} with peer {session.Id}");
            previous.Send(14,false,"session_replaced","This account signed in elsewhere.");
        }
        accountSessions[result.AccountId]=session.Id;
        session.AccountId=result.AccountId;
        nextSessionEpoch=unchecked(nextSessionEpoch+1);
        if(nextSessionEpoch==0) nextSessionEpoch=1;
        session.SessionEpoch=nextSessionEpoch;
        session.Send(10,true,result.Code,result.Message,result.AccountId);
        SendCharacters(session);
    }

    void SelectCharacter(Session session,long characterId)
    {
        if(session.AccountId==0)
        {
            session.Send(13,false,"not_authenticated","Login is required.",0L,"",0L);
            return;
        }
        var character=accounts.Character(session.AccountId,characterId);
        if(character is null)
        {
            session.Send(13,false,"character_not_found","Character does not belong to this account.",0L,"",0L);
            return;
        }
        LeaveWorld(session);
        session.CharacterId=character.Id;
        World.Join(session.Id,character.Id,character.Name);
        Volatile.Write(ref activePlayers,World.Players.Count);
        session.Send(13,true,"character_selected","Entering world.",character.Id,character.Name,(long)session.SessionEpoch);
        // Existing world protocol starts only after identity/character selection.
        session.Send(0,session.Id,character.Id,character.Name,(long)session.SessionEpoch);
        session.Send(1,map,fingerprint,populationHash);
        Console.WriteLine($"player {session.Id} selected character {character.Id} {character.Name}");
    }

    void Handle(Command command)
    {
        int id=command.Id; var body=command.Body;
        if(command.Op==-1 && command.Session is {} joining)
        {
            sessions.Add(id,joining);
            Console.WriteLine($"peer {id} connected; awaiting login");
            return;
        }
        if(command.Op==-2)
        {
            if(sessions.TryGetValue(id,out var leaving)) DisconnectSession(leaving);
            return;
        }
        if(!sessions.TryGetValue(id,out var session)) return;
        try
        {
            switch(command.Op)
            {
                case -10 when body.Length==1 && body[0] is IdentityResult authResult:
                    session.AuthPending=false;
                    Authenticate(session,authResult);
                    break;
                case 10 when body.Length==2 && body[0] is string registerUser && body[1] is string registerPassword:
                    ScheduleAuthentication(session,()=>accounts.Register(registerUser,registerPassword));
                    break;
                case 11 when body.Length==2 && body[0] is string loginUser && body[1] is string loginPassword:
                    ScheduleAuthentication(session,()=>accounts.Login(loginUser,loginPassword));
                    break;
                case 12 when body.Length==1 && body[0] is string characterName:
                    var created=accounts.CreateCharacter(session.AccountId,characterName);
                    session.Send(12,created.Success,created.Code,created.Message,created.CharacterId);
                    if(created.Success) SendCharacters(session);
                    break;
                case 13 when body.Length==1:
                    SelectCharacter(session,Wire.Integer(body[0]));
                    break;
                case 14 when body.Length==0:
                    LogoutAccount(session);
                    session.Send(14,true,"logged_out","Logged out.");
                    break;
                case 24 when session.CharacterId!=0 && World.Players.ContainsKey(id) && body.Length==3 &&
                    Wire.Integer(body[0])==map && body[1] is string hash && hash==fingerprint &&
                    body[2] is string population && population==populationHash:
                    session.Ready=true;
                    break;
                case 20 when IsWorldReady(session) && body.Length==2 && body[1] is byte[] input &&
                    input.Length is >0 and <=36 && input.Length%12==0:
                    uint epoch=checked((uint)Wire.Integer(body[0]));
                    for(int at=0;at<input.Length;at+=12)
                        World.QueueInput(id,epoch,BinaryPrimitives.ReadUInt32LittleEndian(input.AsSpan(at)),
                            new Vector2(BitConverter.ToSingle(input,at+4),BitConverter.ToSingle(input,at+8)));
                    break;
                case 21 when IsWorldReady(session) && body.Length==1:
                    int first=World.NextDrop;
                    if(World.Attack(id,checked((int)Wire.Integer(body[0]))) && first!=World.NextDrop)
                        foreach(var peer in sessions.Values) if(IsWorldReady(peer)) Ground(peer);
                    break;
                case 22 when IsWorldReady(session) && body.Length==1:
                    if(World.Pickup(id,checked((int)Wire.Integer(body[0]))))
                    {
                        session.Send(6,(object)World.Players[id].Inventory.Select(p=>(object?)new object?[]{p.Key,p.Value}).ToArray());
                        foreach(var peer in sessions.Values) if(IsWorldReady(peer)) Ground(peer);
                    }
                    break;
                case 23 when body.Length==1:
                    session.Send(8,Wire.Real(body[0]),World.Time);
                    break;
                default:
                    throw new InvalidDataException("Unknown command/schema");
            }
        }
        catch(Exception ex) when(ex is InvalidDataException or OverflowException)
        {
            session.Disconnect();
        }
    }

    void Owner(Session session)
    {
        if(!World.Players.TryGetValue(session.Id,out var player)) return;
        session.Send(2,World.Tick,World.Time,player.Acknowledged,player.LifeEpoch,player.Position.X,player.Position.Y,
            player.Hp,player.Facing,player.Pose,player.ActionTime,player.Revision);
    }

    void Snapshot(Session session)
    {
        if(!World.Players.TryGetValue(session.Id,out var owner)) return;
        Vector2 center=owner.Position;
        for(int kind=0;kind<2;kind++)
        {
            IEnumerable<KeyValuePair<int,Actor>> source=kind==0
                ? World.Players.Select(p=>new KeyValuePair<int,Actor>(p.Key,p.Value))
                : World.Monsters.Select(p=>new KeyValuePair<int,Actor>(p.Key,p.Value));
            var rows=source.Where(p=>InInterest(center,p.Value.Position,session.Seen[kind].Contains(p.Key))).ToArray();
            var current=rows.Select(p=>p.Key).ToHashSet();
            var removed=session.Seen[kind].Except(current).Select(id=>(object?)id).ToArray();
            if(removed.Length>0) session.Send(7,World.Tick,kind,removed);
            session.Seen[kind]=current;
            foreach(var chunk in rows.Chunk(SnapshotCodec.RowsPerPacket))
                session.Send(3,World.Tick,World.Time,kind,SnapshotCodec.Encode(chunk));
        }
        Owner(session);
    }

    void Ground(Session session)
    {
        if(!World.Players.TryGetValue(session.Id,out var owner)) return;
        Vector2 center=owner.Position;
        var current=World.Drops.Where(p=>InInterest(center,p.Value.Position,session.SeenDrops.Contains(p.Key)))
            .ToDictionary(p=>p.Key,p=>p.Value);
        var added=current.Where(p=>!session.SeenDrops.Contains(p.Key))
            .Select(p=>(object?)new object?[]{p.Key,p.Value.Position.X,p.Value.Position.Y,p.Value.Item}).ToArray();
        var removed=session.SeenDrops.Except(current.Keys).Select(id=>(object?)id).ToArray();
        foreach(var chunk in added.Chunk(20)) session.Send(5,chunk,Array.Empty<object?>());
        foreach(var chunk in removed.Chunk(128)) session.Send(5,Array.Empty<object?>(),chunk);
        session.SeenDrops.Clear();
        session.SeenDrops.UnionWith(current.Keys);
    }
}
