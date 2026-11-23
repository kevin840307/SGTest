using System.Buffers.Binary;

namespace SGTest.Server.Wire;

/// <summary>
/// Self-only durable character/profile baseline. Combat HP/MP remain in the
/// separately ordered PlayerCombatState stream; this frame carries the stable
/// identity/gameplay fields needed by character status UI.
/// </summary>
public static partial class WireProtocol
{
    public static byte[] EncodePlayerProfileState(PlayerProfileStateWire value)
    {
        if (value.CharacterId <= 0 || value.Race < 0 || value.Career < 0 ||
            value.Level < 0 || value.Experience < 0 ||
            value.Strength < 0 || value.Dexterity < 0 ||
            value.Constitution < 0 || value.Wisdom < 0 ||
            value.Attack < 0 || value.Defense < 0 ||
            value.Hit < 0 || value.Dodge < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        var frame = NewFrame(WireOpcode.PlayerProfileState, PlayerProfileStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8), value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4), value.Race);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4), value.Career);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4), value.Level);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8), value.Experience);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36,4), value.Strength);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(40,4), value.Dexterity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4), value.Constitution);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48,4), value.Wisdom);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(52,4), value.Attack);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(56,4), value.Defense);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4), value.Hit);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64,4), value.Dodge);
        frame[68] = value.CombatStatsVerified ? (byte)1 : (byte)0;
        return frame;
    }

    public static bool TryDecodePlayerProfileState(
        ReadOnlySpan<byte> frame, out PlayerProfileStateWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.PlayerProfileState ||
            frame.Length != PlayerProfileStateFrameBytes)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(36,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(52,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(56,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(64,4)),
            frame[68] == 1);
        if (frame[68] > 1)
            return false;
        try
        {
            _ = EncodePlayerProfileState(value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    public static byte[] EncodePlayerCreationProfileState(PlayerCreationProfileStateWire value)
    {
        if (value.CharacterId <= 0 || value.Strength < 1 || value.Dexterity < 1 ||
            value.Constitution < 1 || value.Wisdom < 1)
            throw new ArgumentOutOfRangeException(nameof(value));

        var frame = NewFrame(
            WireOpcode.PlayerCreationProfileState, PlayerCreationProfileStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8), value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4), value.Strength);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4), value.Dexterity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4), value.Constitution);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4), value.Wisdom);
        return frame;
    }

    public static bool TryDecodePlayerCreationProfileState(
        ReadOnlySpan<byte> frame, out PlayerCreationProfileStateWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.PlayerCreationProfileState ||
            frame.Length != PlayerCreationProfileStateFrameBytes)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        try
        {
            _ = EncodePlayerCreationProfileState(value);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }
}
