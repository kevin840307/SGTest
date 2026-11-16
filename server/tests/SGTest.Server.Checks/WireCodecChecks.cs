using System.Buffers.Binary;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class WireCodecChecks
{
    private static void Require(bool valid, string name) => Checks.Require(valid, name);

    public static void Verify()
    {
        Checks.Run("production wire v1 move target golden fixture and roundtrip", () =>
        {
            var message = new MoveTargetWire(9, 7, 6616, 4728);
            byte[] frame = WireProtocol.EncodeMoveTarget(message);
            Require(Convert.ToHexString(frame) ==
                "1C00000001000100090000000000000007000000D819000078120000",
                "MoveTarget golden bytes");
            Require(WireProtocol.TryDecodeMoveTarget(frame, out var decoded) && decoded == message,
                "MoveTarget roundtrip");
            Require(WireProtocol.TryReadHeader(frame, out var header) &&
                header == new WireFrameHeader(28, 1, WireOpcode.MoveTarget),
                "MoveTarget header");
        });

        Checks.Run("production wire v1 move receipt golden fixture and explicit status map", () =>
        {
            var message = new MoveReceiptWire(9, 7, MoveReceiptStatus.Accepted, 42);
            byte[] frame = WireProtocol.EncodeMoveReceipt(message);
            Require(Convert.ToHexString(frame) ==
                "1E0000000100020009000000000000000700000000002A00000000000000",
                "MoveReceipt golden bytes");
            Require(WireProtocol.TryDecodeMoveReceipt(frame, out var decoded) && decoded == message,
                "MoveReceipt roundtrip");
            foreach (MoveReceiptStatus status in Enum.GetValues<MoveReceiptStatus>())
            {
                var sample = new MoveReceiptWire(2, 1, status, 3);
                Require(WireProtocol.TryDecodeMoveReceipt(WireProtocol.EncodeMoveReceipt(sample), out var back) &&
                    back == sample, "status mapping " + status);
            }
        });

        Checks.Run("production wire rejects malformed length version opcode widths and semantic zero generations", () =>
        {
            var baseline = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 7, 6616, 4728));
            foreach (int width in new[] { 0, 7, 8, 27, 29, 65 })
            {
                var bad = new byte[Math.Max(width, 0)];
                if (bad.Length >= 4) BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(0, 4), bad.Length);
                Require(!WireProtocol.TryDecodeMoveTarget(bad, out _), "bad width " + width);
            }

            var wrongLength = (byte[])baseline.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(wrongLength.AsSpan(0, 4), baseline.Length + 1);
            Require(!WireProtocol.TryDecodeMoveTarget(wrongLength, out _), "advertised length mismatch");

            var wrongVersion = (byte[])baseline.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(wrongVersion.AsSpan(4, 2), 2);
            Require(!WireProtocol.TryDecodeMoveTarget(wrongVersion, out _), "wrong version");

            var wrongOpcode = (byte[])baseline.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(wrongOpcode.AsSpan(6, 2), 999);
            Require(!WireProtocol.TryDecodeMoveTarget(wrongOpcode, out _), "unknown opcode");

            var zeroEpoch = (byte[])baseline.Clone();
            zeroEpoch.AsSpan(8, 8).Clear();
            Require(!WireProtocol.TryDecodeMoveTarget(zeroEpoch, out _), "zero map epoch");

            var zeroSequence = (byte[])baseline.Clone();
            zeroSequence.AsSpan(16, 4).Clear();
            Require(!WireProtocol.TryDecodeMoveTarget(zeroSequence, out _), "zero movement sequence");

            var receipt = WireProtocol.EncodeMoveReceipt(new MoveReceiptWire(9, 7, MoveReceiptStatus.Accepted, 42));
            BinaryPrimitives.WriteUInt16LittleEndian(receipt.AsSpan(20, 2), 999);
            Require(!WireProtocol.TryDecodeMoveReceipt(receipt, out _), "unknown receipt status");
        });

        Checks.Run("production wire actor identity and connection epoch are absent from client move payload", () =>
        {
            var first = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 7, 6616, 4728));
            var second = WireProtocol.EncodeMoveTarget(new MoveTargetWire(9, 7, 6616, 4728));
            Require(first.SequenceEqual(second), "move bytes depend only on map generation sequence target");
            Require(first.Length == 28, "no ActorId or ConnectionEpoch fields added");
        });
    }
}
