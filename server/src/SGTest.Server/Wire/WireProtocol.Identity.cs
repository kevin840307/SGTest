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

/// <summary>Account authorization, registration and character-selection wire V1 operations.</summary>
public static partial class WireProtocol
{
    public static byte[] EncodeAuthRequest(AuthRequestWire value)
    {
        byte[] username = StrictUtf8.GetBytes(value.Username ?? string.Empty);
        byte[] password = StrictUtf8.GetBytes(value.Password ?? string.Empty);
        if (username.Length is < 3 or > 32 || password.Length is < 8 or > 256)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size = HeaderBytes + 4 + username.Length + password.Length;
        var frame = NewFrame(WireOpcode.AuthRequest, size);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8, 2), (ushort)username.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10, 2), (ushort)password.Length);
        username.CopyTo(frame.AsSpan(12));
        password.CopyTo(frame.AsSpan(12 + username.Length));
        return frame;
    }

    public static bool TryDecodeAuthRequest(ReadOnlySpan<byte> frame, out AuthRequestWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.AuthRequest || frame.Length < 23) return false;
        int u = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(8, 2));
        int p = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(10, 2));
        if (u is < 3 or > 32 || p is < 8 or > 256 || 12 + u + p != frame.Length) return false;
        try
        {
            value = new(StrictUtf8.GetString(frame.Slice(12, u)),
                StrictUtf8.GetString(frame.Slice(12 + u, p)));
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public static byte[] EncodeAuthResult(AuthResultWire value)
    {
        if ((byte)value.Status > (byte)AuthWireStatus.Disabled) throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.AuthResult, AuthResultFrameBytes);
        frame[8] = (byte)value.Status;
        return frame;
    }

    public static bool TryDecodeAuthResult(ReadOnlySpan<byte> frame, out AuthResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.AuthResult || frame[8] > 2) return false;
        value = new((AuthWireStatus)frame[8]);
        return true;
    }

    public static byte[] EncodeAccountRegisterRequest(AccountRegisterRequestWire value)
    {
        byte[] username = StrictUtf8.GetBytes(value.Username ?? string.Empty);
        byte[] password = StrictUtf8.GetBytes(value.Password ?? string.Empty);
        if (username.Length is < 3 or > 32 || password.Length is < 8 or > 256)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size = HeaderBytes + 4 + username.Length + password.Length;
        var frame = NewFrame(WireOpcode.AccountRegisterRequest, size);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8, 2), (ushort)username.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(10, 2), (ushort)password.Length);
        username.CopyTo(frame.AsSpan(12));
        password.CopyTo(frame.AsSpan(12 + username.Length));
        return frame;
    }

    public static bool TryDecodeAccountRegisterRequest(
        ReadOnlySpan<byte> frame,out AccountRegisterRequestWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.AccountRegisterRequest||
           frame.Length<23)
            return false;
        int usernameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(8,2));
        int passwordLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(10,2));
        if(usernameLength is <3 or >32||passwordLength is <8 or >256||
           12+usernameLength+passwordLength!=frame.Length)
            return false;
        try
        {
            value=new(
                StrictUtf8.GetString(frame.Slice(12,usernameLength)),
                StrictUtf8.GetString(frame.Slice(12+usernameLength,passwordLength)));
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodeAccountRegisterResult(AccountRegisterResultWire value)
    {
        if((byte)value.Status>(byte)AccountRegisterWireStatus.RateLimited)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(
            WireOpcode.AccountRegisterResult,AccountRegisterResultFrameBytes);
        frame[8]=(byte)value.Status;
        return frame;
    }

    public static bool TryDecodeAccountRegisterResult(
        ReadOnlySpan<byte> frame,out AccountRegisterResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.AccountRegisterResult||
           frame[8]>(byte)AccountRegisterWireStatus.RateLimited)
            return false;
        value=new((AccountRegisterWireStatus)frame[8]);
        return true;
    }

    public static byte[] EncodeCharacterList(CharacterListWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Characters.Count > 32) throw new ArgumentOutOfRangeException(nameof(value));
        var encodedNames = new byte[value.Characters.Count][];
        int size = HeaderBytes + 1;
        var ids = new HashSet<long>();
        var slots = new HashSet<byte>();
        for (int i = 0; i < value.Characters.Count; i++)
        {
            var c = value.Characters[i];
            byte[] name = StrictUtf8.GetBytes(c.Name ?? string.Empty);
            if (c.CharacterId <= 0 || c.Slot > 31 || name.Length is < 1 or > 96 ||
                !ids.Add(c.CharacterId) || !slots.Add(c.Slot))
                throw new ArgumentOutOfRangeException(nameof(value));
            encodedNames[i] = name;
            size = checked(size + 11 + name.Length);
        }
        if (size > MaxCharacterListFrameBytes) throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.CharacterList, size);
        frame[8] = (byte)value.Characters.Count;
        int offset = 9;
        for (int i = 0; i < value.Characters.Count; i++)
        {
            var c = value.Characters[i]; var name = encodedNames[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset, 8), c.CharacterId); offset += 8;
            frame[offset++] = c.Slot;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset, 2), (ushort)name.Length); offset += 2;
            name.CopyTo(frame.AsSpan(offset)); offset += name.Length;
        }
        return frame;
    }

    public static bool TryDecodeCharacterList(ReadOnlySpan<byte> frame, out CharacterListWire value)
    {
        value = new(Array.Empty<CharacterSummaryWire>());
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterList) return false;
        int count = frame[8];
        if (count > 32) return false;
        int offset = 9;
        var list = new List<CharacterSummaryWire>(count);
        var ids = new HashSet<long>(); var slots = new HashSet<byte>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (offset + 11 > frame.Length) return false;
                long id = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset, 8)); offset += 8;
                byte slot = frame[offset++];
                int len = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset, 2)); offset += 2;
                if (id <= 0 || slot > 31 || len is < 1 or > 96 || offset + len > frame.Length ||
                    !ids.Add(id) || !slots.Add(slot)) return false;
                string name = StrictUtf8.GetString(frame.Slice(offset, len)); offset += len;
                list.Add(new(id, slot, name));
            }
        }
        catch (DecoderFallbackException) { return false; }
        if (offset != frame.Length) return false;
        value = new(list);
        return true;
    }


    public static byte[] EncodeCharacterCreate(CharacterCreateWire value)
    {
        byte[] name = StrictUtf8.GetBytes(value.Name ?? string.Empty);
        if (value.Slot > 31 || name.Length is < 1 or > 96)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size = HeaderBytes + 3 + name.Length;
        var frame = NewFrame(WireOpcode.CharacterCreate, size);
        frame[8] = value.Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(9, 2), (ushort)name.Length);
        name.CopyTo(frame.AsSpan(11));
        return frame;
    }

    public static bool TryDecodeCharacterCreate(
        ReadOnlySpan<byte> frame, out CharacterCreateWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterCreate ||
            frame.Length < HeaderBytes + 4)
            return false;
        byte slot = frame[8];
        int length = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(9, 2));
        if (slot > 31 || length is < 1 or > 96 || 11 + length != frame.Length)
            return false;
        try
        {
            value = new(slot, StrictUtf8.GetString(frame.Slice(11, length)));
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public static byte[] EncodeCharacterCreateResult(CharacterCreateResultWire value)
    {
        if ((byte)value.Status > (byte)CharacterCreateWireStatus.Unavailable)
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] name = StrictUtf8.GetBytes(value.Name ?? string.Empty);
        if (value.Status == CharacterCreateWireStatus.Success)
        {
            if (value.CharacterId <= 0 || value.Slot > 31 || name.Length is < 1 or > 96)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if (value.CharacterId != 0 || value.Slot > 31 || name.Length != 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size = HeaderBytes + 12 + name.Length;
        var frame = NewFrame(WireOpcode.CharacterCreateResult, size);
        frame[8] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(9, 8), value.CharacterId);
        frame[17] = value.Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(18, 2), (ushort)name.Length);
        if (name.Length > 0) name.CopyTo(frame.AsSpan(20));
        return frame;
    }

    public static bool TryDecodeCharacterCreateResult(
        ReadOnlySpan<byte> frame, out CharacterCreateResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) ||
            h.Opcode != WireOpcode.CharacterCreateResult ||
            frame.Length < HeaderBytes + 12)
            return false;
        byte raw = frame[8];
        long characterId = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(9, 8));
        byte slot = frame[17];
        int length = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(18, 2));
        if (raw > (byte)CharacterCreateWireStatus.Unavailable || slot > 31 ||
            length > 96 || 20 + length != frame.Length)
            return false;
        var status = (CharacterCreateWireStatus)raw;
        try
        {
            string name = length == 0
                ? string.Empty : StrictUtf8.GetString(frame.Slice(20, length));
            if (status == CharacterCreateWireStatus.Success)
            {
                if (characterId <= 0 || length < 1) return false;
            }
            else if (characterId != 0 || length != 0) return false;
            value = new(status, characterId, slot, name);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public static byte[] EncodeCharacterChoose(CharacterChooseWire value)
    {
        if (value.CharacterId <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.CharacterChoose, CharacterChooseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.CharacterId);
        return frame;
    }

    public static bool TryDecodeCharacterChoose(ReadOnlySpan<byte> frame, out CharacterChooseWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterChoose) return false;
        long id = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        if (id <= 0) return false;
        value = new(id); return true;
    }

    public static byte[] EncodeCharacterChooseResult(CharacterChooseResultWire value)
    {
        if ((byte)value.Status > (byte)CharacterChooseWireStatus.WorldRejected) throw new ArgumentOutOfRangeException(nameof(value));
        if (value.Status == CharacterChooseWireStatus.Success)
        {
            if (value.CharacterId <= 0 || value.MapId <= 0 || value.MapEpoch <= 0 ||
                !double.IsFinite(value.X) || !double.IsFinite(value.Y)) throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if (value.CharacterId != 0 || value.MapId != 0 || value.MapEpoch != 0 || value.X != 0 || value.Y != 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.CharacterChooseResult, CharacterChooseResultFrameBytes);
        frame[8] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(9, 8), value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(17, 4), value.MapId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21, 8), BitConverter.DoubleToInt64Bits(value.X));
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29, 8), BitConverter.DoubleToInt64Bits(value.Y));
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37, 8), value.MapEpoch);
        return frame;
    }

    public static bool TryDecodeCharacterChooseResult(ReadOnlySpan<byte> frame, out CharacterChooseResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterChooseResult || frame[8] > 2) return false;
        var status = (CharacterChooseWireStatus)frame[8];
        long id = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(9, 8));
        int map = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(17, 4));
        double x = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21, 8)));
        double y = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29, 8)));
        long epoch = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37, 8));
        if (status == CharacterChooseWireStatus.Success)
        {
            if (id <= 0 || map <= 0 || epoch <= 0 || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        }
        else if (id != 0 || map != 0 || epoch != 0 || x != 0 || y != 0) return false;
        value = new(status, id, map, x, y, epoch); return true;
    }

}
