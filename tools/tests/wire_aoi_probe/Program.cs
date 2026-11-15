using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

record struct MovePacket(int EntityId, int X, int Y, int ServerTick, byte Facing, ushort MapEpoch, uint Revision);
record struct Point(int Id, int X, int Y, int Kind, bool Changed);
static class Program
{
    const int PacketSize = 32;
    const int Cell = 1024;
    const int Enter = 2600;
    const int Exit = 3000;
    const int WorldSize = 24000;
    static long sink;

    static byte[] EncodeBinary(MovePacket m)
    {
        var b = new byte[PacketSize];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0,4), PacketSize);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4,2), 1); // version
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6,2), 1); // Move opcode
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8,4), m.EntityId);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12,4), m.X);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16,4), m.Y);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20,4), m.ServerTick);
        b[24] = m.Facing;
        b[25] = 0; // flags
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(26,2), m.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28,4), m.Revision);
        return b;
    }
    static MovePacket DecodeBinary(byte[] b)
    {
        if (b.Length != PacketSize || BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0,4)) != PacketSize
            || BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(4,2)) != 1
            || BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6,2)) != 1) throw new FormatException();
        return new(
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8,4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12,4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20,4)),
            b[24],
            BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(26,2)),
            BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(28,4))
        );
    }

    static void Check(bool ok, string name) {if (!ok) throw new Exception("FAIL " + name);}

    static void Bench(string name, Func<int> run, int iters=40000)
    {
        for(int j=0;j<3000;j++) sink += run();
        var med = new List<double>();
        var alloc = new List<double>();
        for(int trial=0;trial<5;trial++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var timer=Stopwatch.StartNew();
            for(int i=0;i<iters;i++) sink += run();
            timer.Stop();
            med.Add(timer.Elapsed.TotalMilliseconds * 1000 / iters);
            alloc.Add((GC.GetAllocatedBytesForCurrentThread() - before) / (double)iters);
        }
        med.Sort(); alloc.Sort();
        Console.WriteLine($"BENCH {name} us_per_message_median={med[2]:F3} alloc_bytes_per_message={alloc[2]:F1} trials=5 iterations_each={iters}");
    }

    static Dictionary<(int,int),List<Point>> Grid(List<Point> entities)
    {
        var g = new Dictionary<(int,int),List<Point>>();
        foreach(var p in entities)
        {
            var c=(p.X / Cell,p.Y / Cell);
            if(!g.TryGetValue(c,out var list))g[c]=list=new();
            list.Add(p);
        }
        return g;
    }

    static void Main()
    {
        Console.WriteLine("PROBE scope=synthetic_comparison_not_final_protocol_and_not_running_server");
        var p=new MovePacket(12345,6616,4728,42,3,1,7);
        var jsonObj=new {type="MoveEntity",entityId=p.EntityId,x=p.X,y=p.Y,serverTick=p.ServerTick,facing=p.Facing,mapEpoch=p.MapEpoch,revision=p.Revision};
        var jb=JsonSerializer.SerializeToUtf8Bytes(jsonObj);
        var bb=EncodeBinary(p);
        Check(Convert.ToHexString(bb)=="200000000100010039300000D8190000781200002A0000000300010007000000","Godot shared golden fixture");
        Check(DecodeBinary(bb)==p,"binary roundtrip");
        Check(JsonDocument.Parse(jb).RootElement.GetProperty("entityId").GetInt32()==p.EntityId,"json roundtrip");
        foreach(var bad in new[]{0,1,7,31,33,4096})
        {
            if (bad==PacketSize) continue;
            try {DecodeBinary(new byte[bad]);throw new Exception("bad packet accepted");}
            catch(FormatException) {}
        }
        var damaged=(byte[])bb.Clone(); damaged[0]=255;
        try {DecodeBinary(damaged);throw new Exception("bad length accepted");}
        catch(FormatException) {}
        byte[] legacy;
        using(var aes=Aes.Create())
        {
            aes.Mode=CipherMode.CBC; aes.Padding=PaddingMode.PKCS7;
            var encrypted=aes.EncryptCbc(jb,aes.IV,PaddingMode.PKCS7);
            legacy=System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(encrypted));
        }
        Console.WriteLine($"SIZE json_utf8={jb.Length} binary_header_payload={bb.Length} legacy_json_aescbc_base64={legacy.Length} binary_saving_vs_json_pct={(1.0-(double)bb.Length/jb.Length)*100:F1}");
        Console.WriteLine($"GOLDEN {Convert.ToHexString(bb)}");
        Bench("json_encode",()=>JsonSerializer.SerializeToUtf8Bytes(jsonObj).Length);
        Bench("json_decode",()=>{using var doc=JsonDocument.Parse(jb);return doc.RootElement.GetProperty("entityId").GetInt32();});
        Bench("binary_encode",()=>EncodeBinary(p).Length);
        Bench("binary_decode",()=>DecodeBinary(bb).EntityId);

        var rng=new Random(20261115);
        var all=new List<Point>();
        for(int i=0;i<100;i++) all.Add(new Point(i+1,rng.Next(WorldSize),rng.Next(WorldSize),0,i%3==0));
        for(int i=0;i<1000;i++) all.Add(new Point(10000+i,rng.Next(WorldSize),rng.Next(WorldSize),1,i%7==0));
        for(int i=0;i<5000;i++) all.Add(new Point(20000+i,rng.Next(WorldSize),rng.Next(WorldSize),2,false));
        var grid=Grid(all);
        long fullPairs=0,visiblePairs=0,allChanges=0,visibleChanges=0,candidateCount=0;
        var range=(int)Math.Ceiling((double)Enter/Cell);
        foreach(var observer in all.Take(100))
        {
            var brute=new HashSet<int>(); var indexed=new HashSet<int>();
            foreach(var e in all)
            {
                if(e.Id==observer.Id)continue;
                var dx=e.X-observer.X; var dy=e.Y-observer.Y;
                if((long)dx*dx+(long)dy*dy<=(long)Enter*Enter)brute.Add(e.Id);
            }
            int cx=observer.X/Cell,cy=observer.Y/Cell;
            for(int x=cx-range;x<=cx+range;x++)
            for(int y=cy-range;y<=cy+range;y++)
            {
                if(!grid.TryGetValue((x,y),out var entities))continue;
                foreach(var e in entities)
                {
                    candidateCount++;
                    if(e.Id==observer.Id)continue;
                    var dx=e.X-observer.X; var dy=e.Y-observer.Y;
                    if((long)dx*dx+(long)dy*dy<=(long)Enter*Enter)indexed.Add(e.Id);
                }
            }
            Check(brute.SetEquals(indexed),"spatial grid matches brute-force");
            fullPairs+=all.Count-1;
            visiblePairs+=indexed.Count;
            allChanges+=all.Count(x=>x.Id!=observer.Id && x.Changed);
            visibleChanges+=all.Count(x=>indexed.Contains(x.Id) && x.Changed);
        }
        bool seen=false;
        int[] distances={2500,2700,3100,2800,2500};
        bool[] expectedVisibility={true,true,false,false,true};
        for(int i=0;i<distances.Length;i++)
        {
            bool next=distances[i]<=(seen?Exit:Enter);
            Check(next==expectedVisibility[i],"AOI hysteresis");
            seen=next;
        }
        Console.WriteLine($"AOI scope=deterministic_uniform_map_{WorldSize}x{WorldSize} players=100 monsters=1000 drops=5000 enter_radius={Enter} exit_radius={Exit} cell={Cell}");
        Console.WriteLine($"AOI snapshot_full_pairs={fullPairs} snapshot_visible_pairs={visiblePairs} saved_pct={(1-(double)visiblePairs/fullPairs)*100:F2} grid_candidates={candidateCount} brute_pairs={fullPairs} correctness=PASS");
        Console.WriteLine($"AOI changed_full_pairs={allChanges} changed_visible_pairs={visibleChanges} saved_pct={(1-(double)visibleChanges/allChanges)*100:F2} reliable_events_not_simulated=true");
        Console.WriteLine($"AOI movement_binary_estimated_bytes_full={allChanges*PacketSize} movement_binary_estimated_bytes_filtered={visibleChanges*PacketSize} exclude_packet_batch_overhead=true");
        // Dense worst case: all objects fit within one observer's enter circle.
        // AOI must not be advertised as a universal network savings guarantee.
        var dense=new List<Point>();
        for(int i=0;i<all.Count;i++)
            dense.Add(new Point(all[i].Id, 6000+rng.Next(800),4600+rng.Next(800),all[i].Kind,all[i].Changed));
        long denseVisible=0;
        foreach(var observer in dense.Take(100))
        foreach(var entity in dense)
        {
            if(entity.Id==observer.Id)continue;
            var dx=entity.X-observer.X;var dy=entity.Y-observer.Y;
            if((long)dx*dx+(long)dy*dy<=(long)Enter*Enter)denseVisible++;
        }
        Check(denseVisible==fullPairs,"dense AOI sees all entities");
        Console.WriteLine($"AOI dense_synthetic_observer_pairs={denseVisible} full_pairs={fullPairs} saving_pct=0.00 correctness=PASS");
        Console.WriteLine("RESULT binary_fixture=PASS length_and_version_validation=PASS aoi_spatial_equivalence=PASS hysteresis=PASS dense_boundary=PASS");
        GC.KeepAlive(sink);
    }
}
