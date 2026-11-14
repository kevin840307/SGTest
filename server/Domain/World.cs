using System.Numerics;

namespace SGTest.Server.Domain;

public class Actor
{
    public Vector2 Position, Velocity;
    public int Hp = 30, Facing = 1, Pose;
    public double ActionTime, Respawn, LastAttack = -100;
    public uint Revision;
}
public sealed class Player : Actor
{
    public double LastInput, ProtectedUntil;
    public uint Acknowledged, MaximumInput, LifeEpoch = 1;
    public Vector2 LastMotion;
    public readonly Dictionary<uint,Vector2> Inputs = [];
    public readonly Dictionary<int,int> Inventory = [];
}
public sealed class Monster : Actor
{
    public int MaxHp = 30, Damage = 10;
    public float MoveSpeed = 65, AggroRange = 500, AttackRange = 70, ChaseStopRange = 45;
    public double AttackIntervalSeconds = 1, RespawnSeconds = 2;
    public float Phase;
    public uint Definition;
    public Vector2 Home;
}
public readonly record struct Drop(Vector2 Position,int Item);
public readonly record struct ActionEvent(int Kind,int Id,Vector2 Position,int Hp,int Facing,int Pose,double Started,uint Revision,uint Definition);
public readonly record struct PendingAttack(int Target,double Due,double Started);

// Single writer. Domain has no sockets, Godot resources or database calls.
public sealed class World
{
    static readonly int[] Headings=[6,7,8,1,2,3,4,5];
    public const float PlayerSpeed = 230, MonsterSpeed = 65;
    public const int PlayerHp = 100, MonsterHp = 30, MaximumDrops = 5000;
    public readonly Dictionary<int,Player> Players = [];
    public readonly Dictionary<int,Monster> Monsters = [];
    public readonly Dictionary<int,Drop> Drops = [];
    public readonly List<ActionEvent> Events = [];
    readonly Dictionary<int,PendingAttack> attacks = [];
    public CollisionGrid? Navigation { get; }
    public Vector2 Bounds { get; }
    public Vector2 Spawn { get; }
    public long Tick { get; private set; }
    public double Time { get; private set; }
    public int NextDrop { get; private set; } = 1;
    public int DropRevision { get; private set; }

    public World(CollisionGrid? navigation = null)
    {
        Navigation = navigation; Bounds = navigation?.Bounds ?? new Vector2(2400,1600);
        Spawn = Place(new Vector2(1200,800));
    }
    public static bool Newer(uint value,uint old) => unchecked(value-old) is > 0 and < 0x80000000;
    public static bool Active(Actor a,double time) => a.Pose >= 2 && time-a.ActionTime < (a.Pose==4 ? 2 : .35);
    public static int Heading(Vector2 vector)
    {
        int n = (int)MathF.Round(MathF.Atan2(vector.Y,vector.X)/(MathF.PI/4),MidpointRounding.AwayFromZero);
        return Headings[(n%8+8)%8];
    }
    public Vector2 Place(Vector2 point)
    {
        point = Vector2.Clamp(point,Vector2.Zero,Bounds-new Vector2(.001f));
        return Navigation?.Nearest(point) ?? point;
    }
    public Vector2 Move(Vector2 from,Vector2 to)
    {
        to = Vector2.Clamp(to,Vector2.Zero,Bounds-new Vector2(.001f));
        return Navigation?.Slide(from,to) ?? to;
    }
    bool Reachable(Vector2 a,Vector2 b) => Navigation?.Allows(a,b) ?? true;
    public Player Join(int id)
    {
        var player = new Player { Hp=PlayerHp,Position=Place(Spawn+new Vector2(20*(Players.Count%8),0)),ProtectedUntil=Time+2 };
        Players.Add(id,player); return player;
    }
    static void ApplyDefinition(Monster monster,MonsterDefinition definition)
    {
        definition.Validate();
        monster.Definition=definition.Id;
        monster.MaxHp=definition.Hp; monster.Hp=definition.Hp; monster.Damage=definition.Damage;
        monster.MoveSpeed=definition.MoveSpeed; monster.AggroRange=definition.AggroRange;
        monster.AttackRange=definition.AttackRange; monster.ChaseStopRange=definition.ChaseStopRange;
        monster.AttackIntervalSeconds=definition.AttackIntervalSeconds;
        monster.RespawnSeconds=definition.RespawnSeconds;
    }
    public void Populate(int count,int drops,Population? population=null)
    {
        Monsters.Clear(); attacks.Clear(); Drops.Clear(); NextDrop=1;
        var random = new Random(8413);
        for (int i=0;i<count;i++)
            Monsters.Add(i+1,new Monster { Position=Place(new Vector2(random.Next(80,2320),random.Next(80,1520))),
                Phase=(i%17)*.037f,ActionTime=-(i%17)*.037 });
        for (int i=0;i<Math.Min(drops,MaximumDrops);i++)
            Drops.Add(NextDrop++,new Drop(Place(new Vector2(random.Next(2400),random.Next(1600))),1001));
        DropRevision++;
        if(population is not null)
        {
            // Explicit stress counts cycle the map's real definition palette;
            // default count=-1 uses the original source spawn positions.
            if(count<0)
            {
                Monsters.Clear();
                for(int i=0;i<population.Spawns.Length;i++)
                {
                    var spawn=population.Spawns[i];
                    var monster=new Monster{Position=Place(spawn.Position),
                        Phase=(i%17)*.037f,ActionTime=-(i%17)*.037};
                    ApplyDefinition(monster,population.Definitions[spawn.Definition]);
                    Monsters[i+1]=monster;
                }
            }
            else foreach(var (id,m) in Monsters)
            {
                uint definition=population.Spawns[(id-1)%population.Spawns.Length].Definition;
                ApplyDefinition(m,population.Definitions[definition]);
            }
        }
        foreach(var monster in Monsters.Values) monster.Home=monster.Position;
    }
    public bool QueueInput(int id,uint epoch,uint sequence,Vector2 motion)
    {
        if (!Players.TryGetValue(id,out var p) || p.Hp<=0 || p.LifeEpoch!=epoch ||
            !float.IsFinite(motion.X) || !float.IsFinite(motion.Y) || Math.Abs(motion.X)>1 || Math.Abs(motion.Y)>1) return false;
        uint distance=unchecked(sequence-p.Acknowledged);
        if (distance==0 || distance>64 || !p.Inputs.TryAdd(sequence,motion.LengthSquared()>1 ? Vector2.Normalize(motion) : motion)) return false;
        if (Newer(sequence,p.MaximumInput)) p.MaximumInput=sequence;
        p.LastInput=Time; return true;
    }
    void Consume(Player p)
    {
        p.Velocity=Vector2.Zero;
        if (Time-p.LastInput>.5) { p.Inputs.Clear(); p.Acknowledged=p.MaximumInput; p.LastMotion=Vector2.Zero; return; }
        if (!Newer(p.MaximumInput,p.Acknowledged)) return;
        uint sequence=unchecked(p.Acknowledged+1);
        if (p.Inputs.Remove(sequence,out var motion)) p.LastMotion=motion;
        p.Acknowledged=sequence; p.Velocity=p.LastMotion*PlayerSpeed;
    }
    void Pose(int kind,int id,Actor a,int pose)
    {
        a.Pose=pose; a.ActionTime=Time; a.Revision++;
        Events.Add(new(kind,id,a.Position,a.Hp,a.Facing,pose,Time,a.Revision,a is Monster m ? m.Definition : 0));
    }
    void Locomotion(Actor a,int pose,double phase=0)
    {
        if (a.Pose==pose) return;
        a.Pose=pose; a.ActionTime=Time-phase; a.Revision++;
    }
    public void Step(double delta)
    {
        Tick++; Time+=delta;
        foreach (var (id,p) in Players)
        {
            if (p.Hp<=0)
            {
                if (Time>=p.Respawn)
                {
                    p.Hp=PlayerHp; p.Position=Place(Spawn+new Vector2(20*(id%8),0)); p.ProtectedUntil=Time+2;
                    p.LifeEpoch++; p.Inputs.Clear(); p.Acknowledged=0; p.MaximumInput=0; p.LastMotion=Vector2.Zero;
                    Pose(0,id,p,0);
                }
                continue;
            }
            Consume(p); p.Position=Move(p.Position,p.Position+p.Velocity*(float)delta);
            if (!Active(p,Time))
            {
                Locomotion(p,p.Velocity.LengthSquared()>.01 ? 1 : 0);
                if (p.Velocity.LengthSquared()>.01) p.Facing=Heading(p.Velocity);
            }
        }
        ResolveAttacks();
        foreach (var (id,m) in Monsters)
        {
            if (m.Hp>0 && !Active(m,Time) && m.Velocity.LengthSquared()>0)
            {
                var old=m.Position; m.Position=Move(old,old+m.Velocity*(float)delta);
                if (Vector2.DistanceSquared(old,m.Position)>.000001) m.Facing=Heading(m.Position-old);
            }
            if (delta>=.2 || (id-1)%6==Tick%6) Think(id,m);
        }
    }
    void Think(int id,Monster m)
    {
        if (m.Hp<=0)
        {
            if (Time>=m.Respawn)
            {
                m.Hp=m.MaxHp; m.Position=Place(m.Home); m.Velocity=Vector2.Zero;
                Pose(1,id,m,0);
            }
            return;
        }
        if (Active(m,Time)) return;
        float closest=m.AggroRange; int target=0; Vector2 destination=default;
        foreach (var (pid,p) in Players)
        {
            float distance=Vector2.Distance(m.Position,p.Position);
            if (p.Hp>0 && distance<closest) { closest=distance; target=pid; destination=p.Position; }
        }
        if (target!=0 && closest<=m.AttackRange && Reachable(m.Position,destination) && Time>=Players[target].ProtectedUntil && Time-m.LastAttack>=m.AttackIntervalSeconds)
        {
            m.LastAttack=Time; m.Facing=Heading(destination-m.Position); m.Velocity=Vector2.Zero;
            Pose(1,id,m,2); attacks[id]=new(target,Time+.2,Time); return;
        }
        if (closest<m.AggroRange && closest>m.ChaseStopRange)
        {
            m.Velocity=Vector2.Normalize(destination-m.Position)*m.MoveSpeed; m.Facing=Heading(m.Velocity);
            Locomotion(m,1,m.Phase);
        }
        else { m.Velocity=Vector2.Zero; Locomotion(m,0,m.Phase); }
    }
    void ResolveAttacks()
    {
        foreach (var (id,pending) in attacks.ToArray())
        {
            if (Time<pending.Due) continue;
            attacks.Remove(id);
            if (!Monsters.TryGetValue(id,out var m) || !Players.TryGetValue(pending.Target,out var p) || m.Hp<=0 ||
                m.Pose!=2 || m.ActionTime!=pending.Started || p.Hp<=0 || Time<p.ProtectedUntil ||
                Vector2.Distance(m.Position,p.Position)>m.AttackRange || !Reachable(m.Position,p.Position)) continue;
            p.Hp=Math.Max(0,p.Hp-m.Damage);
            if (p.Hp==0) { p.Velocity=Vector2.Zero; p.Inputs.Clear(); p.LastMotion=Vector2.Zero; p.Respawn=Time+3; }
            Pose(0,pending.Target,p,p.Hp==0 ? 4 : 3);
        }
    }
    public bool Attack(int player,int monster)
    {
        if (!Players.TryGetValue(player,out var p) || !Monsters.TryGetValue(monster,out var m) || p.Hp<=0 || m.Hp<=0 ||
            Time-p.LastAttack<.35 || Vector2.Distance(p.Position,m.Position)>160 || !Reachable(p.Position,m.Position)) return false;
        p.LastAttack=Time; p.Facing=Heading(m.Position-p.Position); Pose(0,player,p,2);
        m.Hp-=10; m.Velocity=Vector2.Zero; attacks.Remove(monster);
        if (m.Hp<=0)
        {
            foreach (int item in new[]{1001,1002,1003})
            {
                if (Drops.Count>=MaximumDrops) break;
                var at=Place(m.Position+new Vector2((item-1002)*22,0));
                Drops[NextDrop++]=new(Reachable(m.Position,at) ? at : m.Position,item);
            }
            m.Respawn=Time+m.RespawnSeconds; DropRevision++;
        }
        Pose(1,monster,m,m.Hp<=0 ? 4 : 3); return true;
    }
    public bool Pickup(int player,int drop)
    {
        if (!Players.TryGetValue(player,out var p) || p.Hp<=0 || !Drops.TryGetValue(drop,out var d) ||
            Vector2.Distance(p.Position,d.Position)>100 || !Reachable(p.Position,d.Position)) return false;
        Drops.Remove(drop); p.Inventory[d.Item]=p.Inventory.GetValueOrDefault(d.Item)+1; DropRevision++; return true;
    }
}
