using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using Google.Protobuf;
using MessagePack;

internal enum TraceKind : ushort
{
    Move = 1,
    Combat = 2,
    Drop = 3,
    Spawn = 4
}

internal readonly record struct WireEvent(
    TraceKind Kind,
    int EntityA,
    int EntityB,
    int X,
    int Y,
    int ValueA,
    int ValueB,
    uint Tick,
    uint Sequence,
    uint Revision,
    ushort Epoch,
    byte SmallA,
    byte SmallB);

internal static class CodecComparison
{
    private const ushort Version = 1;
    private static long _sink;

    private sealed record Codec(
        string Name,
        Func<WireEvent, byte[]> Encode,
        Func<TraceKind, byte[], WireEvent> Decode);

    private static readonly Codec[] Codecs =
    [
        new("compact_binary", EncodeCompact, DecodeCompact),
        new("messagepack", EncodeMessagePack, DecodeMessagePack),
        new("protobuf", EncodeProtobuf, DecodeProtobuf)
    ];

    public static void Verify()
    {
        var fixture = new WireEvent(
            TraceKind.Move, 12345, 0, 6616, 4728, 0, 0, 42, 0, 7, 1, 3, 0);

        var trace = BuildTrace(4000);
        Console.WriteLine("CODEC_COMPARE scope=synthetic_equal_semantics_move_combat_drop_spawn payload_only_bench common_8_byte_transport_header_for_size");

        foreach (var codec in Codecs)
        {
            var encoded = trace.Select(codec.Encode).ToArray();
            for (int i = 0; i < trace.Length; i++)
            {
                var decoded = codec.Decode(trace[i].Kind, encoded[i]);
                Check(decoded == trace[i], $"{codec.Name} roundtrip index={i}");
            }

            long payloadBytes = encoded.Sum(x => (long)x.Length);
            long framedBytes = payloadBytes + (long)encoded.Length * 8;
            var perKind = new Dictionary<TraceKind, int>();
            foreach (TraceKind kind in Enum.GetValues<TraceKind>())
            {
                int index = Array.FindIndex(trace, x => x.Kind == kind);
                perKind[kind] = encoded[index].Length + 8;
            }

            Console.WriteLine(
                $"CODEC_SIZE name={codec.Name} events={trace.Length} payload_bytes={payloadBytes} " +
                $"framed_bytes={framedBytes} framed_bytes_per_event={(double)framedBytes / trace.Length:F2} " +
                $"move_frame={perKind[TraceKind.Move]} combat_frame={perKind[TraceKind.Combat]} " +
                $"drop_frame={perKind[TraceKind.Drop]} spawn_frame={perKind[TraceKind.Spawn]}");

            var fixturePayload = codec.Encode(fixture);
            var fixtureFrame = Frame(TraceKind.Move, fixturePayload);
            Console.WriteLine(
                $"CODEC_FIXTURE name={codec.Name} payload_hex={Convert.ToHexString(fixturePayload)} " +
                $"frame_hex={Convert.ToHexString(fixtureFrame)}");

            Bench(codec, trace, encoded);
        }

        var compactFixture = Frame(TraceKind.Move, EncodeCompact(fixture));
        Check(
            Convert.ToHexString(compactFixture) ==
            "200000000100010039300000D8190000781200002A0000000300010007000000",
            "compact candidate preserves existing 32-byte golden fixture");

        Console.WriteLine("CODEC_COMPARE_RESULT candidates=3 roundtrip=PASS trace_events=4000 common_header_bytes=8");
        GC.KeepAlive(_sink);
    }

    private static WireEvent[] BuildTrace(int count)
    {
        var rng = new Random(20261116);
        var trace = new WireEvent[count];
        for (int i = 0; i < count; i++)
        {
            int lane = i % 20;
            uint tick = (uint)(1000 + i / 8);
            ushort epoch = (ushort)(1 + (i / 1300));
            uint revision = (uint)(1 + i * 3);
            if (lane < 12)
            {
                trace[i] = new(
                    TraceKind.Move,
                    1000 + rng.Next(120),
                    0,
                    rng.Next(0, 15360),
                    rng.Next(0, 15360),
                    0,
                    0,
                    tick,
                    0,
                    revision,
                    epoch,
                    (byte)rng.Next(0, 8),
                    0);
            }
            else if (lane < 16)
            {
                trace[i] = new(
                    TraceKind.Combat,
                    1000 + rng.Next(120),
                    1000 + rng.Next(120),
                    0,
                    0,
                    12000 + rng.Next(20),
                    rng.Next(-25, 2500),
                    tick,
                    (uint)(50000 + i),
                    revision,
                    0,
                    (byte)rng.Next(0, 4),
                    0);
            }
            else if (lane < 19)
            {
                trace[i] = new(
                    TraceKind.Drop,
                    200000 + i,
                    rng.Next(0, 2) == 0 ? 0 : 1000 + rng.Next(120),
                    rng.Next(0, 15360),
                    rng.Next(0, 15360),
                    2000 + rng.Next(500),
                    1 + rng.Next(5),
                    0,
                    0,
                    revision,
                    epoch,
                    0,
                    0);
            }
            else
            {
                int maxHp = 500 + rng.Next(10000);
                trace[i] = new(
                    TraceKind.Spawn,
                    1000 + rng.Next(6000),
                    0,
                    rng.Next(0, 15360),
                    rng.Next(0, 15360),
                    rng.Next(0, maxHp + 1),
                    maxHp,
                    (uint)rng.Next(0, 12),
                    (uint)(90000 + i),
                    revision,
                    epoch,
                    (byte)rng.Next(0, 4),
                    (byte)rng.Next(0, 8));
            }
        }
        return trace;
    }

    private static void Bench(Codec codec, WireEvent[] trace, byte[][] preencoded)
    {
        const int batches = 120;
        const int eventsPerBatch = 256;

        for (int i = 0; i < 8 * eventsPerBatch; i++)
        {
            var e = trace[i % trace.Length];
            var b = codec.Encode(e);
            _sink += b.Length;
            _sink += codec.Decode(e.Kind, b).EntityA;
        }

        var encode = MeasureBatches(batches, eventsPerBatch, (start, count) =>
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                var bytes = codec.Encode(trace[i % trace.Length]);
                _sink += bytes.Length;
            }
        });

        var decode = MeasureBatches(batches, eventsPerBatch, (start, count) =>
        {
            int end = start + count;
            for (int i = start; i < end; i++)
            {
                int index = i % trace.Length;
                _sink += codec.Decode(trace[index].Kind, preencoded[index]).EntityA;
            }
        });

        Console.WriteLine(
            $"CODEC_BENCH name={codec.Name} " +
            $"encode_p50_us={encode.P50:F3} encode_p95_us={encode.P95:F3} encode_p99_us={encode.P99:F3} " +
            $"encode_alloc_bytes_per_event={encode.AllocPerEvent:F1} " +
            $"decode_p50_us={decode.P50:F3} decode_p95_us={decode.P95:F3} decode_p99_us={decode.P99:F3} " +
            $"decode_alloc_bytes_per_event={decode.AllocPerEvent:F1} " +
            $"batches={batches} events_per_batch={eventsPerBatch}");
    }

    private readonly record struct BenchResult(double P50, double P95, double P99, double AllocPerEvent);

    private static BenchResult MeasureBatches(int batches, int eventsPerBatch, Action<int, int> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        var samples = new double[batches];
        for (int batch = 0; batch < batches; batch++)
        {
            long start = Stopwatch.GetTimestamp();
            action(batch * eventsPerBatch, eventsPerBatch);
            long end = Stopwatch.GetTimestamp();
            samples[batch] = (end - start) * 1_000_000.0 / Stopwatch.Frequency / eventsPerBatch;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
        Array.Sort(samples);

        return new(
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            allocated / (double)(batches * eventsPerBatch));
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static byte[] Frame(TraceKind kind, byte[] payload)
    {
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frame.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)kind);
        payload.CopyTo(frame, 8);
        return frame;
    }

    private static byte[] EncodeCompact(WireEvent e)
    {
        int length = e.Kind switch
        {
            TraceKind.Move => 24,
            TraceKind.Combat => 32,
            TraceKind.Drop => 32,
            TraceKind.Spawn => 36,
            _ => throw new ArgumentOutOfRangeException(nameof(e.Kind))
        };
        var b = new byte[length];
        switch (e.Kind)
        {
            case TraceKind.Move:
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0, 4), e.EntityA);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4, 4), e.X);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8, 4), e.Y);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12, 4), e.Tick);
                b[16] = e.SmallA;
                b[17] = e.SmallB;
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(18, 2), e.Epoch);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20, 4), e.Revision);
                break;
            case TraceKind.Combat:
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0, 4), e.EntityA);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4, 4), e.EntityB);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8, 4), e.ValueA);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12, 4), e.Tick);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16, 4), e.ValueB);
                b[20] = e.SmallA;
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24, 4), e.Sequence);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28, 4), e.Revision);
                break;
            case TraceKind.Drop:
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0, 4), e.EntityA);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4, 4), e.ValueA);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8, 4), e.ValueB);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12, 4), e.X);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16, 4), e.Y);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20, 2), e.Epoch);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24, 4), e.Revision);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28, 4), e.EntityB);
                break;
            case TraceKind.Spawn:
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0, 4), e.EntityA);
                b[4] = e.SmallA;
                b[5] = e.SmallB;
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6, 2), e.Epoch);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8, 4), e.X);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12, 4), e.Y);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(16, 4), e.ValueA);
                BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20, 4), e.ValueB);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24, 4), e.Revision);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28, 4), e.Tick);
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(32, 4), e.Sequence);
                break;
        }
        return b;
    }

    private static WireEvent DecodeCompact(TraceKind kind, byte[] b)
    {
        int expected = kind switch
        {
            TraceKind.Move => 24,
            TraceKind.Combat => 32,
            TraceKind.Drop => 32,
            TraceKind.Spawn => 36,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (b.Length != expected) throw new FormatException("compact payload width");
        return kind switch
        {
            TraceKind.Move => new(
                kind,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0, 4)), 0,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8, 4)),
                0, 0,
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12, 4)),
                0,
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(20, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(18, 2)),
                b[16], b[17]),
            TraceKind.Combat => new(
                kind,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4, 4)),
                0, 0,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(24, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(28, 4)),
                0, b[20], 0),
            TraceKind.Drop => new(
                kind,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(28, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8, 4)),
                0, 0,
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(24, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(20, 2)),
                0, 0),
            TraceKind.Spawn => new(
                kind,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0, 4)), 0,
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(28, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(32, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(24, 4)),
                BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(6, 2)),
                b[4], b[5]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static byte[] EncodeMessagePack(WireEvent e)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        var writer = new MessagePackWriter(buffer);
        switch (e.Kind)
        {
            case TraceKind.Move:
                writer.WriteArrayHeader(8);
                writer.Write(e.EntityA); writer.Write(e.X); writer.Write(e.Y); writer.Write(e.Tick);
                writer.Write(e.SmallA); writer.Write(e.SmallB); writer.Write(e.Epoch); writer.Write(e.Revision);
                break;
            case TraceKind.Combat:
                writer.WriteArrayHeader(8);
                writer.Write(e.EntityA); writer.Write(e.EntityB); writer.Write(e.ValueA); writer.Write(e.Tick);
                writer.Write(e.ValueB); writer.Write(e.SmallA); writer.Write(e.Sequence); writer.Write(e.Revision);
                break;
            case TraceKind.Drop:
                writer.WriteArrayHeader(8);
                writer.Write(e.EntityA); writer.Write(e.ValueA); writer.Write(e.ValueB); writer.Write(e.X);
                writer.Write(e.Y); writer.Write(e.Epoch); writer.Write(e.Revision); writer.Write(e.EntityB);
                break;
            case TraceKind.Spawn:
                writer.WriteArrayHeader(11);
                writer.Write(e.EntityA); writer.Write(e.SmallA); writer.Write(e.X); writer.Write(e.Y);
                writer.Write(e.ValueA); writer.Write(e.ValueB); writer.Write(e.SmallB); writer.Write(e.Epoch);
                writer.Write(e.Revision); writer.Write(e.Tick); writer.Write(e.Sequence);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(e.Kind));
        }
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static WireEvent DecodeMessagePack(TraceKind kind, byte[] payload)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
        int count = reader.ReadArrayHeader();
        if (count != (kind == TraceKind.Spawn ? 11 : 8)) throw new FormatException("messagepack field count");
        return kind switch
        {
            TraceKind.Move => DecodeMessagePackMove(ref reader, kind),
            TraceKind.Combat => DecodeMessagePackCombat(ref reader, kind),
            TraceKind.Drop => DecodeMessagePackDrop(ref reader, kind),
            TraceKind.Spawn => DecodeMessagePackSpawn(ref reader, kind),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static WireEvent DecodeMessagePackMove(ref MessagePackReader reader, TraceKind kind)
    {
        int entityA = reader.ReadInt32();
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        uint tick = reader.ReadUInt32();
        byte smallA = reader.ReadByte();
        byte smallB = reader.ReadByte();
        ushort epoch = reader.ReadUInt16();
        uint revision = reader.ReadUInt32();
        return new(kind, entityA, 0, x, y, 0, 0, tick, 0, revision, epoch, smallA, smallB);
    }

    private static WireEvent DecodeMessagePackCombat(ref MessagePackReader reader, TraceKind kind)
    {
        int entityA = reader.ReadInt32();
        int entityB = reader.ReadInt32();
        int valueA = reader.ReadInt32();
        uint tick = reader.ReadUInt32();
        int valueB = reader.ReadInt32();
        byte smallA = reader.ReadByte();
        uint sequence = reader.ReadUInt32();
        uint revision = reader.ReadUInt32();
        return new(kind, entityA, entityB, 0, 0, valueA, valueB, tick, sequence, revision, 0, smallA, 0);
    }

    private static WireEvent DecodeMessagePackDrop(ref MessagePackReader reader, TraceKind kind)
    {
        int entityA = reader.ReadInt32();
        int valueA = reader.ReadInt32();
        int valueB = reader.ReadInt32();
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        ushort epoch = reader.ReadUInt16();
        uint revision = reader.ReadUInt32();
        int entityB = reader.ReadInt32();
        return new(kind, entityA, entityB, x, y, valueA, valueB, 0, 0, revision, epoch, 0, 0);
    }

    private static WireEvent DecodeMessagePackSpawn(ref MessagePackReader reader, TraceKind kind)
    {
        int entityA = reader.ReadInt32();
        byte smallA = reader.ReadByte();
        int x = reader.ReadInt32();
        int y = reader.ReadInt32();
        int valueA = reader.ReadInt32();
        int valueB = reader.ReadInt32();
        byte smallB = reader.ReadByte();
        ushort epoch = reader.ReadUInt16();
        uint revision = reader.ReadUInt32();
        uint tick = reader.ReadUInt32();
        uint sequence = reader.ReadUInt32();
        return new(kind, entityA, 0, x, y, valueA, valueB, tick, sequence, revision, epoch, smallA, smallB);
    }

    private static byte[] EncodeProtobuf(WireEvent e) => e.Kind switch
    {
        TraceKind.Move => new SGTest.WireProbe.Proto.MovePayload
        {
            EntityId = e.EntityA, X = e.X, Y = e.Y, ServerTick = e.Tick,
            Facing = e.SmallA, Flags = e.SmallB, MapEpoch = e.Epoch, Revision = e.Revision
        }.ToByteArray(),
        TraceKind.Combat => new SGTest.WireProbe.Proto.CombatPayload
        {
            SourceId = e.EntityA, TargetId = e.EntityB, SkillId = (uint)e.ValueA, ServerTick = e.Tick,
            Damage = e.ValueB, Outcome = e.SmallA, ActionSequence = e.Sequence, TargetRevision = e.Revision
        }.ToByteArray(),
        TraceKind.Drop => new SGTest.WireProbe.Proto.DropPayload
        {
            DropId = e.EntityA, ItemDefinitionId = (uint)e.ValueA, Quantity = (uint)e.ValueB,
            X = e.X, Y = e.Y, MapEpoch = e.Epoch, Revision = e.Revision, OwnerId = e.EntityB
        }.ToByteArray(),
        TraceKind.Spawn => new SGTest.WireProbe.Proto.SpawnPayload
        {
            EntityId = e.EntityA, EntityKind = e.SmallA, X = e.X, Y = e.Y, Hp = (uint)e.ValueA,
            MaxHp = (uint)e.ValueB, Facing = e.SmallB, MapEpoch = e.Epoch, Revision = e.Revision,
            Action = e.Tick, ActionSequence = e.Sequence
        }.ToByteArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(e.Kind))
    };

    private static WireEvent DecodeProtobuf(TraceKind kind, byte[] payload) => kind switch
    {
        TraceKind.Move => From(SGTest.WireProbe.Proto.MovePayload.Parser.ParseFrom(payload), kind),
        TraceKind.Combat => From(SGTest.WireProbe.Proto.CombatPayload.Parser.ParseFrom(payload), kind),
        TraceKind.Drop => From(SGTest.WireProbe.Proto.DropPayload.Parser.ParseFrom(payload), kind),
        TraceKind.Spawn => From(SGTest.WireProbe.Proto.SpawnPayload.Parser.ParseFrom(payload), kind),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static WireEvent From(SGTest.WireProbe.Proto.MovePayload p, TraceKind kind) =>
        new(kind, p.EntityId, 0, p.X, p.Y, 0, 0, p.ServerTick, 0, p.Revision, (ushort)p.MapEpoch, (byte)p.Facing, (byte)p.Flags);

    private static WireEvent From(SGTest.WireProbe.Proto.CombatPayload p, TraceKind kind) =>
        new(kind, p.SourceId, p.TargetId, 0, 0, (int)p.SkillId, p.Damage, p.ServerTick,
            p.ActionSequence, p.TargetRevision, 0, (byte)p.Outcome, 0);

    private static WireEvent From(SGTest.WireProbe.Proto.DropPayload p, TraceKind kind) =>
        new(kind, p.DropId, p.OwnerId, p.X, p.Y, (int)p.ItemDefinitionId, (int)p.Quantity,
            0, 0, p.Revision, (ushort)p.MapEpoch, 0, 0);

    private static WireEvent From(SGTest.WireProbe.Proto.SpawnPayload p, TraceKind kind) =>
        new(kind, p.EntityId, 0, p.X, p.Y, (int)p.Hp, (int)p.MaxHp, p.Action,
            p.ActionSequence, p.Revision, (ushort)p.MapEpoch, (byte)p.EntityKind, (byte)p.Facing);

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("CODEC_COMPARE_FAIL " + name);
    }
}
