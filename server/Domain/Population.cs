using System.Numerics;
using System.Text.Json;

namespace SGTest.Server.Domain;

public readonly record struct Spawn(uint Definition,Vector2 Position);
public sealed record Population(HashSet<uint> Definitions,Spawn[] Spawns)
{
    public static Population Load(string path,int map)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(path)); var root=json.RootElement;
        if(root.GetProperty("version").GetInt32()!=1 || root.GetProperty("map_id").GetInt32()!=map)
            throw new InvalidDataException("Population version/map mismatch");
        var definitions=new HashSet<uint>();
        foreach(var item in root.GetProperty("definitions").EnumerateArray())
            if(!definitions.Add(item.GetProperty("id").GetUInt32())) throw new InvalidDataException("Duplicate monster definition");
        var spawns=new List<Spawn>();
        foreach(var item in root.GetProperty("spawns").EnumerateArray())
        {
            uint id=item.GetProperty("definition").GetUInt32(); var p=item.GetProperty("p");
            var point=new Vector2(p[0].GetSingle(),p[1].GetSingle());
            if(!definitions.Contains(id) || !float.IsFinite(point.X) || !float.IsFinite(point.Y))
                throw new InvalidDataException("Invalid population spawn");
            spawns.Add(new(id,point));
        }
        if(spawns.Count is 0 or >10000) throw new InvalidDataException("Population size out of range");
        return new(definitions,spawns.ToArray());
    }
}
