using System.Buffers.Binary;
using SGTest.Server.Identity;

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
            value.Hit < 0 || value.Dodge < 0 || value.AvailableAttributePoints < 0 ||
            (value.VerifiedStats & ~PlayerProfileStatVerification.All) != 0)
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
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(68,4), value.AvailableAttributePoints);
        // Preserve legacy V1 semantics: 0=None and 1=All. Partial verification
        // is an in-frame extension marked by bit 7, with the low nibble as mask.
        frame[72] = value.VerifiedStats switch
        {
            PlayerProfileStatVerification.None => 0,
            PlayerProfileStatVerification.All => 1,
            _ => (byte)(0x80 | (byte)value.VerifiedStats)
        };
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
        byte verificationByte=frame[72];
        PlayerProfileStatVerification verifiedStats;
        if(verificationByte==0)
            verifiedStats=PlayerProfileStatVerification.None;
        else if(verificationByte==1)
            verifiedStats=PlayerProfileStatVerification.All;
        else if((verificationByte&0xF0)==0x80 &&
                (verificationByte&0x0F)!=0)
            verifiedStats=(PlayerProfileStatVerification)(verificationByte&0x0F);
        else
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
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(68,4)),
            verifiedStats);
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

    public static byte[] EncodePlayerAttributeAllocate(PlayerAttributeAllocateWire value)
    {
        if (value.AttributeSequence == 0 || !Enum.IsDefined(value.Attribute))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerAttributeAllocate,PlayerAttributeAllocateFrameBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8,4),value.AttributeSequence);
        frame[12]=(byte)value.Attribute;
        return frame;
    }

    public static bool TryDecodePlayerAttributeAllocate(
        ReadOnlySpan<byte> frame,out PlayerAttributeAllocateWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.PlayerAttributeAllocate||
           frame.Length!=PlayerAttributeAllocateFrameBytes)
            return false;
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(8,4));
        var attribute=(CharacterAttributeKind)frame[12];
        if(sequence==0 || !Enum.IsDefined(attribute))
            return false;
        value=new(sequence,attribute);
        return true;
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
