using System.Numerics;
using System.Text.Json;

namespace SGTest.Server.Domain;

public readonly record struct Spawn(uint Definition,Vector2 Position);
public readonly record struct MonsterDefinition(
    uint Id,int Hp,int Damage,float MoveSpeed,float AggroRange,float AttackRange,
    float ChaseStopRange,double AttackIntervalSeconds,double RespawnSeconds)
{
    public static MonsterDefinition Default(uint id) =>
        new(id,30,10,65,500,70,45,1,2);

    public static MonsterDefinition Parse(JsonElement item,int version)
    {
        uint id=item.GetProperty("id").GetUInt32();
        if(version==1) return Default(id);
        var stats=item.GetProperty("stats");
        var result=new MonsterDefinition(
            id,
            stats.GetProperty("hp").GetInt32(),
            stats.GetProperty("damage").GetInt32(),
            stats.GetProperty("move_speed").GetSingle(),
            stats.GetProperty("aggro_range").GetSingle(),
            stats.GetProperty("attack_range").GetSingle(),
            stats.GetProperty("chase_stop_range").GetSingle(),
            stats.GetProperty("attack_interval_seconds").GetDouble(),
            stats.GetProperty("respawn_seconds").GetDouble());
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if(Id==0 || Hp is <1 or >1_000_000 || Damage is <0 or >1_000_000 ||
            !float.IsFinite(MoveSpeed) || MoveSpeed is <0 or >2000 ||
            !float.IsFinite(AggroRange) || AggroRange is <0 or >5000 ||
            !float.IsFinite(AttackRange) || AttackRange is <0 or >1000 ||
            !float.IsFinite(ChaseStopRange) || ChaseStopRange is <0 or >1000 ||
            !double.IsFinite(AttackIntervalSeconds) || AttackIntervalSeconds is <.05 or >60 ||
            !double.IsFinite(RespawnSeconds) || RespawnSeconds is <0 or >3600 ||
            AttackRange> AggroRange || ChaseStopRange> AggroRange)
            throw new InvalidDataException("Invalid monster definition stats");
    }
}

public sealed record Population(Dictionary<uint,MonsterDefinition> Definitions,Spawn[] Spawns)
{
    public Population(uint[] definitions,Spawn[] spawns)
        : this(definitions.ToDictionary(id=>id,MonsterDefinition.Default),spawns) { }

    public static Population Load(string path,int map)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(path)); var root=json.RootElement;
        int version=root.GetProperty("version").GetInt32();
        if(version is not (1 or 2) || root.GetProperty("map_id").GetInt32()!=map)
            throw new InvalidDataException("Population version/map mismatch");
        var definitions=new Dictionary<uint,MonsterDefinition>();
        foreach(var item in root.GetProperty("definitions").EnumerateArray())
        {
            var definition=MonsterDefinition.Parse(item,version);
            if(!definitions.TryAdd(definition.Id,definition))
                throw new InvalidDataException("Duplicate monster definition");
        }
        var spawns=new List<Spawn>();
        foreach(var item in root.GetProperty("spawns").EnumerateArray())
        {
            uint id=item.GetProperty("definition").GetUInt32(); var p=item.GetProperty("p");
            var point=new Vector2(p[0].GetSingle(),p[1].GetSingle());
            if(!definitions.ContainsKey(id) || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
                throw new InvalidDataException("Invalid population spawn");
            spawns.Add(new(id,point));
        }
        if(spawns.Count is 0 or >10000) throw new InvalidDataException("Population size out of range");
        return new(definitions,spawns.ToArray());
    }
}
