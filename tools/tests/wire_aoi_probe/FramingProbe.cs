using System.Buffers.Binary;

// Candidate ONLY: this is a test-only bounded TCP byte stream reassembler.
// It is not the final wire/transport implementation for the game.
internal sealed class FramingProbe
{
    public delegate void FrameConsumer(ReadOnlySpan<byte> completeFrame);
    public const int MinFrameSize = 8; // uint32 little-endian length + uint16 version + uint16 opcode
    private readonly byte[] buffer;
    private int buffered;
    private int expected = 4;
    public int BufferedBytes => buffered;
    public int AcceptedFrames { get; private set; }

    public FramingProbe(int maximumFrameSize = 65536)
    {
        if (maximumFrameSize < MinFrameSize) throw new ArgumentOutOfRangeException(nameof(maximumFrameSize));
        buffer = new byte[maximumFrameSize];
    }

    public void Reset()
    {
        buffered = 0;
        expected = 4;
    }

    public void Feed(ReadOnlySpan<byte> input, FrameConsumer deliver)
    {
        ArgumentNullException.ThrowIfNull(deliver);
        while (!input.IsEmpty)
        {
            int take = Math.Min(expected - buffered, input.Length);
            input[..take].CopyTo(buffer.AsSpan(buffered, take));
            buffered += take;
            input = input[take..];
            if (buffered != expected) continue;
            if (expected == 4)
            {
                uint advertised = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                if (advertised < MinFrameSize || advertised > buffer.Length)
                {
                    Reset(); // caller must close/resync the connection after malformed framing
                    throw new FormatException("Invalid or oversized length prefix");
                }
                expected = checked((int)advertised);
            }
            if (buffered != expected) continue;
            var frame = buffer.AsSpan(0, expected);
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2)) != 1 ||
                BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2)) != 1 ||
                frame.Length != 32) // this test's Move opcode has exactly 24 payload bytes
            {
                Reset();
                throw new FormatException("Unsupported test version, opcode, or payload length");
            }
            try
            {
                // Caller may not retain the span: the next frame reuses this bounded buffer.
                deliver(frame);
                AcceptedFrames++;
            }
            finally { Reset(); }
        }
    }
}

internal static class FramingCases
{
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("WIRE_FRAMING_FAIL " + name);
    }

    private static void MustReject(byte[] input, string name, int max = 64)
    {
        var parser = new FramingProbe(max);
        bool rejected = false;
        try { parser.Feed(input, _ => throw new Exception("bad frame delivered")); }
        catch (FormatException) { rejected = true; }
        Check(rejected && parser.BufferedBytes == 0 && parser.AcceptedFrames == 0, name);
    }

    public static void Verify(byte[] golden)
    {
        Check(golden.Length == 32, "fixture width");
        Check(new FramingProbe(32).BufferedBytes == 0, "initial state");
        for (int split = 0; split <= golden.Length; split++)
        {
            var reader = new FramingProbe(32);
            var frames = new List<string>();
            reader.Feed(golden.AsSpan(0, split), frame => frames.Add(Convert.ToHexString(frame)));
            reader.Feed(ReadOnlySpan<byte>.Empty, frame => frames.Add(Convert.ToHexString(frame)));
            reader.Feed(golden.AsSpan(split), frame => frames.Add(Convert.ToHexString(frame)));
            Check(frames.Count == 1 && frames[0] == Convert.ToHexString(golden)
                  && reader.BufferedBytes == 0 && reader.AcceptedFrames == 1, "all 33 split points");
        }

        // Deterministic fragmented and coalesced TCP-style stream, not actual socket I/O.
        var second = (byte[])golden.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(second.AsSpan(8, 4), 54321);
        var joined = golden.Concat(second).Concat(golden).ToArray();
        var expected = new[] { Convert.ToHexString(golden), Convert.ToHexString(second), Convert.ToHexString(golden) };
        for (int seed = 1; seed <= 100; seed++)
        {
            var rng = new Random(seed);
            var reader = new FramingProbe(64);
            var frames = new List<string>();
            for (int offset = 0; offset < joined.Length;)
            {
                int count = Math.Min(joined.Length - offset, rng.Next(1, 41));
                reader.Feed(joined.AsSpan(offset, count), frame => frames.Add(Convert.ToHexString(frame)));
                offset += count;
            }
            Check(frames.SequenceEqual(expected) && reader.BufferedBytes == 0
                  && reader.AcceptedFrames == 3, "fragmentation/coalescing seed " + seed);
        }

        foreach (uint length in new uint[] { 0, 1, 7, 65, uint.MaxValue })
        {
            var prefix = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(prefix, length);
            MustReject(prefix, "invalid length " + length);
        }
        var shortMove = golden.AsSpan(0, 8).ToArray();
        shortMove[0] = 8; // structurally valid header, but Move payload is missing
        MustReject(shortMove, "wrong Move payload width");
        var version = (byte[])golden.Clone();
        version[4] = 2;
        MustReject(version, "unsupported version");
        var opcode = (byte[])golden.Clone();
        opcode[6] = 255;
        MustReject(opcode, "unsupported opcode");

        var interrupted = new FramingProbe(64);
        int delivered = 0;
        interrupted.Feed(golden.AsSpan(0, 19), _ => delivered++);
        Check(delivered == 0 && interrupted.BufferedBytes == 19, "truncated stays pending");
        interrupted.Reset(); // new connection epoch must never inherit old partial bytes
        interrupted.Feed(golden, _ => delivered++);
        Check(delivered == 1 && interrupted.BufferedBytes == 0, "reset discards partial");

        var malformedAfterValid = golden.Concat(new byte[4]).ToArray();
        var accepted = new FramingProbe(64);
        int acceptedCount = 0;
        bool threw = false;
        try { accepted.Feed(malformedAfterValid, _ => acceptedCount++); }
        catch (FormatException) { threw = true; }
        Check(threw && acceptedCount == 1 && accepted.BufferedBytes == 0,
              "valid frame then rejected invalid prefix");
        Console.WriteLine("WIRE_FRAMING_PROBE PASS splits=33 randomized_streams=100 frames_each=3 invalid_prefixes=5 invalid_headers=3 reset=PASS bounded=64");
    }
}
