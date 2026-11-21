using System.Buffers.Binary;
using System.Text;
using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

/// <summary>WorldReady, AOI entity and monster state framing for the existing V1 protocol.</summary>
public static partial class WireProtocol
{
    public static byte[] EncodeWorldReadyAck(WorldReadyAckWire value)
    {
        if (value.MapEpoch <= 0 || value.ServerTick < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.WorldReadyAck, WorldReadyAckFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        return frame;
    }

    public static bool TryDecodeWorldReadyAck(ReadOnlySpan<byte> frame, out WorldReadyAckWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.WorldReadyAck)
            return false;
        value = new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
                    BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)));
        return value.MapEpoch > 0 && value.ServerTick >= 0;
    }

    public static byte[] EncodeWorldResyncRequest(WorldResyncRequestWire value)
    {
        if (value.MapEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.WorldResyncRequest, WorldResyncRequestFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        return frame;
    }

    public static bool TryDecodeWorldResyncRequest(ReadOnlySpan<byte> frame, out WorldResyncRequestWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.WorldResyncRequest)
            return false;
        value = new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)));
        return value.MapEpoch > 0;
    }

    public static byte[] EncodeWorldReady(WorldReadyWire value)
    {
        if (!ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                           value.MapId, value.X, value.Y) || value.Facing is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.WorldReady, WorldReadyFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.ActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32, 4), value.MapId);
        WriteFloat(frame.AsSpan(36, 4), value.X);
        WriteFloat(frame.AsSpan(40, 4), value.Y);
        frame[44] = value.Facing;
        return frame;
    }

    public static bool TryDecodeWorldReady(ReadOnlySpan<byte> frame, out WorldReadyWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) || header.Opcode != WireOpcode.WorldReady)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(32, 4)),
            ReadFloat(frame.Slice(36, 4)), ReadFloat(frame.Slice(40, 4)), frame[44]);
        return ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                             value.MapId, value.X, value.Y) && value.Facing is >= 1 and <= 8;
    }

    public static byte[] EncodeEntityState(WireOpcode opcode, EntityStateWire value)
    {
        if (opcode is not (WireOpcode.EntitySpawn or WireOpcode.EntityDelta or WireOpcode.SelfState) ||
            !ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                           value.MapId, value.X, value.Y) ||
            value.ConnectionEpoch <= 0 || value.EntityMapEpoch <= 0 || value.Revision <= 0 ||
            value.Facing is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(opcode, EntityStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32, 8), value.ConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40, 8), value.EntityMapEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48, 4), value.MapId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52, 8), value.Revision);
        WriteFloat(frame.AsSpan(60, 4), value.X);
        WriteFloat(frame.AsSpan(64, 4), value.Y);
        frame[68] = value.IsMoving ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(69, 4), value.MovementActionSequence);
        frame[73] = value.Facing;
        return frame;
    }

    public static bool TryDecodeEntityState(ReadOnlySpan<byte> frame,
        out WireOpcode opcode, out EntityStateWire value)
    {
        opcode = 0; value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode is not (WireOpcode.EntitySpawn or WireOpcode.EntityDelta or WireOpcode.SelfState) ||
            frame[68] > 1 || frame[73] is < 1 or > 8)
            return false;
        opcode = header.Opcode;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52, 8)),
            ReadFloat(frame.Slice(60, 4)), ReadFloat(frame.Slice(64, 4)),
            frame[68] != 0, BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(69, 4)), frame[73]);
        return ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                             value.MapId, value.X, value.Y) &&
               value.ConnectionEpoch > 0 && value.EntityMapEpoch > 0 && value.Revision > 0;
    }

    public static byte[] EncodeMonsterState(WireOpcode opcode, MonsterStateWire value)
    {
        if (opcode is not (WireOpcode.MonsterSpawn or WireOpcode.MonsterDelta) ||
            !ValidMonsterState(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(opcode, MonsterStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.EntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32, 4), value.EnemyId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36, 4), value.SourceIndex);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(40, 4), value.MapId);
        WriteFloat(frame.AsSpan(44, 4), value.X);
        WriteFloat(frame.AsSpan(48, 4), value.Y);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52, 8), value.Revision);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64, 4), value.MaxHp);
        frame[68] = (byte)value.State;
        frame[69] = value.Facing;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(70, 8), value.TargetActorId);
        return frame;
    }

    public static bool TryDecodeMonsterState(ReadOnlySpan<byte> frame,
        out WireOpcode opcode, out MonsterStateWire value)
    {
        opcode = 0; value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode is not (WireOpcode.MonsterSpawn or WireOpcode.MonsterDelta))
            return false;
        opcode = header.Opcode;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(32, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(36, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(40, 4)),
            ReadFloat(frame.Slice(44, 4)), ReadFloat(frame.Slice(48, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(64, 4)),
            (MonsterAiState)frame[68], frame[69],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(70, 8)));
        return ValidMonsterState(value);
    }

}
