using System.Buffers.Binary;
using SGTest.Server.World;

namespace SGTest.Server.Wire;

public enum WireOpcode : ushort
{
    MoveTarget = 1,
    MoveReceipt = 2
}

public readonly record struct WireFrameHeader(int FrameBytes, ushort Version, WireOpcode Opcode);

/// <summary>
/// Client command payload after the authenticated connection has already been bound to one ActorId.
/// ActorId and ConnectionEpoch are deliberately not client-controlled wire fields.
/// </summary>
public readonly record struct MoveTargetWire(
    long MapEpoch,
    uint MovementSequence,
    int TargetX,
    int TargetY);

/// <summary>
/// Authoritative receipt routed back to the already-bound player connection.
/// MapEpoch lets the client reject a late receipt after a map transition.
/// </summary>
public readonly record struct MoveReceiptWire(
    long MapEpoch,
    uint MovementSequence,
    MoveReceiptStatus Status,
    long ServerTick);

/// <summary>
/// Version 1 complete-frame codec. Transport/TLS/socket buffering is intentionally separate.
/// Layout: uint32 frameBytes + uint16 version + uint16 opcode + opcode-specific payload.
/// All integers are little-endian. No JSON/MessagePack/Protobuf runtime dependency.
/// </summary>
public static class WireProtocol
{
    public const ushort Version = 1;
    public const int HeaderBytes = 8;
    public const int MaximumFrameBytes = 65536;
    public const int MoveTargetPayloadBytes = 20;
    public const int MoveTargetFrameBytes = HeaderBytes + MoveTargetPayloadBytes;
    public const int MoveReceiptPayloadBytes = 22;
    public const int MoveReceiptFrameBytes = HeaderBytes + MoveReceiptPayloadBytes;

    public static byte[] EncodeMoveTarget(MoveTargetWire value)
    {
        if (value.MapEpoch <= 0 || value.MovementSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(value), "MapEpoch and MovementSequence must be positive");

        var frame = NewFrame(WireOpcode.MoveTarget, MoveTargetFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.MovementSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), value.TargetX);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24, 4), value.TargetY);
        return frame;
    }

    public static bool TryDecodeMoveTarget(ReadOnlySpan<byte> frame, out MoveTargetWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.MoveTarget ||
            header.FrameBytes != MoveTargetFrameBytes)
            return false;

        long mapEpoch = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4));
        if (mapEpoch <= 0 || sequence == 0) return false;

        value = new(
            mapEpoch,
            sequence,
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24, 4)));
        return true;
    }

    public static byte[] EncodeMoveReceipt(MoveReceiptWire value)
    {
        if (value.MapEpoch <= 0 || value.MovementSequence == 0 || value.ServerTick < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Receipt generation/sequence/tick invalid");
        ushort status = EncodeStatus(value.Status);

        var frame = NewFrame(WireOpcode.MoveReceipt, MoveReceiptFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.MovementSequence);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(20, 2), status);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22, 8), value.ServerTick);
        return frame;
    }

    public static bool TryDecodeMoveReceipt(ReadOnlySpan<byte> frame, out MoveReceiptWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.MoveReceipt ||
            header.FrameBytes != MoveReceiptFrameBytes)
            return false;

        long mapEpoch = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4));
        ushort rawStatus = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(20, 2));
        long tick = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22, 8));
        if (mapEpoch <= 0 || sequence == 0 || tick < 0 || !TryDecodeStatus(rawStatus, out var status))
            return false;

        value = new(mapEpoch, sequence, status, tick);
        return true;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out WireFrameHeader header)
    {
        header = default;
        if (frame.Length < HeaderBytes || frame.Length > MaximumFrameBytes)
            return false;

        int advertised = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(0, 4));
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2));
        ushort rawOpcode = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2));
        if (advertised != frame.Length || advertised < HeaderBytes || version != Version)
            return false;

        WireOpcode opcode = rawOpcode switch
        {
            (ushort)WireOpcode.MoveTarget => WireOpcode.MoveTarget,
            (ushort)WireOpcode.MoveReceipt => WireOpcode.MoveReceipt,
            _ => 0
        };
        if (opcode == 0) return false;

        int expected = opcode switch
        {
            WireOpcode.MoveTarget => MoveTargetFrameBytes,
            WireOpcode.MoveReceipt => MoveReceiptFrameBytes,
            _ => -1
        };
        if (advertised != expected) return false;

        header = new(advertised, version, opcode);
        return true;
    }

    private static byte[] NewFrame(WireOpcode opcode, int frameBytes)
    {
        var frame = new byte[frameBytes];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frameBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)opcode);
        return frame;
    }

    private static ushort EncodeStatus(MoveReceiptStatus status) => status switch
    {
        MoveReceiptStatus.Accepted => 0,
        MoveReceiptStatus.UnknownActor => 1,
        MoveReceiptStatus.OldConnectionEpoch => 2,
        MoveReceiptStatus.OldMapEpoch => 3,
        MoveReceiptStatus.ReplayedSequence => 4,
        MoveReceiptStatus.InvalidTarget => 5,
        MoveReceiptStatus.TargetBlocked => 6,
        MoveReceiptStatus.PathBudgetExhausted => 7,
        MoveReceiptStatus.PathNotFound => 8,
        MoveReceiptStatus.TickRouteBudgetExhausted => 9,
        MoveReceiptStatus.RouteBecameBlocked => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static bool TryDecodeStatus(ushort raw, out MoveReceiptStatus status)
    {
        status = raw switch
        {
            0 => MoveReceiptStatus.Accepted,
            1 => MoveReceiptStatus.UnknownActor,
            2 => MoveReceiptStatus.OldConnectionEpoch,
            3 => MoveReceiptStatus.OldMapEpoch,
            4 => MoveReceiptStatus.ReplayedSequence,
            5 => MoveReceiptStatus.InvalidTarget,
            6 => MoveReceiptStatus.TargetBlocked,
            7 => MoveReceiptStatus.PathBudgetExhausted,
            8 => MoveReceiptStatus.PathNotFound,
            9 => MoveReceiptStatus.TickRouteBudgetExhausted,
            10 => MoveReceiptStatus.RouteBecameBlocked,
            _ => (MoveReceiptStatus)(-1)
        };
        return raw <= 10;
    }
}
