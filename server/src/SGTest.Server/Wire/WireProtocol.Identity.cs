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

    private static bool ValidAppearance(CharacterAppearance value) =>
        value.Race is >= 0 and <= 4 && value.HairIndex is >= 0 and <= 65535 &&
        value.HairColor is >= 0 and <= 7 && value.BodyIndex is >= 0 and <= 255 &&
        value.LegIndex is >= 0 and <= 255 && value.SleeveColor is >= 0 and <= 255;

    private static void WriteAppearance(Span<byte> target, CharacterAppearance value)
    {
        if (!ValidAppearance(value) || target.Length != CharacterAppearanceBytes)
            throw new ArgumentOutOfRangeException(nameof(value));
        target[0] = (byte)value.Race;
        BinaryPrimitives.WriteUInt16LittleEndian(target.Slice(1,2),(ushort)value.HairIndex);
        target[3] = (byte)value.HairColor;
        target[4] = (byte)value.BodyIndex;
        target[5] = (byte)value.LegIndex;
        target[6] = (byte)value.SleeveColor;
    }

    private static bool ReadAppearance(ReadOnlySpan<byte> source, out CharacterAppearance value)
    {
        value = default;
        if (source.Length != LegacyCharacterAppearanceBytes &&
            source.Length != BodyLegCharacterAppearanceBytes &&
            source.Length != CharacterAppearanceBytes) return false;
        value = new(source[0],BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(1,2)),source[3],
            source.Length >= BodyLegCharacterAppearanceBytes ? source[4] : 0,
            source.Length >= BodyLegCharacterAppearanceBytes ? source[5] : 0,
            source.Length == CharacterAppearanceBytes ? source[6] : 0);
        return ValidAppearance(value);
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
        var choices = value.CreationAppearances ?? Array.Empty<CharacterAppearance>();
        if (choices.Count > MaxCreationAppearanceChoices || choices.Any(c => !ValidAppearance(c)) ||
            choices.Distinct().Count() != choices.Count)
            throw new ArgumentOutOfRangeException(nameof(value));
        bool extended = choices.Count > 0 || value.Characters.Any(c => c.Appearance is not null);
        if (extended) size = checked(size + 3 +
            value.Characters.Count * (1 + CharacterAppearanceBytes) +
            choices.Count * CharacterAppearanceBytes);
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
        if (extended)
        {
            frame[offset++] = 3; // v3 adds synchronized original HK2 sleeve/wrist color.
            foreach (var character in value.Characters)
            {
                frame[offset++] = character.Appearance is null ? (byte)0 : (byte)1;
                if (character.Appearance is { } appearance)
                    WriteAppearance(frame.AsSpan(offset,CharacterAppearanceBytes),appearance);
                offset += CharacterAppearanceBytes;
            }
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(offset,2),(ushort)choices.Count); offset += 2;
            foreach (var choice in choices)
            {
                WriteAppearance(frame.AsSpan(offset,CharacterAppearanceBytes),choice);
                offset += CharacterAppearanceBytes;
            }
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
        if (offset == frame.Length) { value = new(list); return true; }
        if (frame.Length-offset < 3) return false;
        byte appearanceVersion=frame[offset++];
        int appearanceBytes=appearanceVersion switch
        {
            1 => LegacyCharacterAppearanceBytes,
            2 => BodyLegCharacterAppearanceBytes,
            3 => CharacterAppearanceBytes,
            _ => 0
        };
        if (appearanceBytes==0 ||
            frame.Length-offset < count*(1+appearanceBytes)+2) return false;
        for (int i=0;i<count;i++)
        {
            byte known=frame[offset++];
            var bytes=frame.Slice(offset,appearanceBytes); offset+=appearanceBytes;
            if (known>1 || !ReadAppearance(bytes,out var appearance) ||
                (known==0 && appearance!=default)) return false;
            list[i]=list[i] with { Appearance=known==1?appearance:null };
        }
        int choiceCount=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(offset,2)); offset+=2;
        if (choiceCount>MaxCreationAppearanceChoices ||
            frame.Length-offset!=choiceCount*appearanceBytes) return false;
        var choices=new List<CharacterAppearance>(choiceCount);
        var seen=new HashSet<CharacterAppearance>();
        for(int i=0;i<choiceCount;i++)
        {
            if(!ReadAppearance(frame.Slice(offset,appearanceBytes),out var choice)||!seen.Add(choice))
                return false;
            choices.Add(choice); offset+=appearanceBytes;
        }
        value = new(list,choices);
        return true;
    }


    public static byte[] EncodeCharacterCreate(CharacterCreateWire value)
    {
        byte[] name = StrictUtf8.GetBytes(value.Name ?? string.Empty);
        if (value.Slot > 31 || name.Length is < 1 or > 96)
            throw new ArgumentOutOfRangeException(nameof(value));

        byte[]? password = null;
        try
        {
            int suffixBytes;
            if (value.Creation is { } creation)
            {
                password = StrictUtf8.GetBytes(creation.CharacterPassword ?? string.Empty);
                if (password.Length is < 1 or > MaxCharacterPasswordBytes)
                    throw new ArgumentOutOfRangeException(nameof(value));
                suffixBytes = 14 + password.Length;
            }
            else suffixBytes = value.Appearance is null ? 0 : CharacterAppearanceBytes;

            int size = HeaderBytes + 3 + name.Length + suffixBytes;
            var frame = NewFrame(WireOpcode.CharacterCreate, size);
            frame[8] = value.Slot;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(9, 2), (ushort)name.Length);
            name.CopyTo(frame.AsSpan(11));
            int offset = 11 + name.Length;

            if (value.Creation is { } settings)
            {
                frame[offset++] = 4; // v4 adds original synchronized sleeve/wrist color.
                frame[offset++] = value.Appearance is null ? (byte)0 : (byte)1;
                if (value.Appearance is { } appearance)
                    WriteAppearance(frame.AsSpan(offset,CharacterAppearanceBytes),appearance);
                else frame.AsSpan(offset,CharacterAppearanceBytes).Clear();
                offset += CharacterAppearanceBytes;
                frame[offset++] = settings.Strength;
                frame[offset++] = settings.Dexterity;
                frame[offset++] = settings.Constitution;
                frame[offset++] = settings.Wisdom;
                frame[offset++] = checked((byte)password!.Length);
                password.CopyTo(frame.AsSpan(offset));
            }
            else if (value.Appearance is { } appearance)
                WriteAppearance(frame.AsSpan(offset,CharacterAppearanceBytes),appearance);
            return frame;
        }
        finally
        {
            if (password is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(password);
        }
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
        int offset = 11 + length;
        if (slot > 31 || length is < 1 or > 96 || offset > frame.Length)
            return false;
        try
        {
            string name = StrictUtf8.GetString(frame.Slice(11,length));
            if (offset == frame.Length)
            {
                value = new(slot,name);
                return true;
            }
            if (frame.Length == offset + LegacyCharacterAppearanceBytes ||
                frame.Length == offset + BodyLegCharacterAppearanceBytes ||
                frame.Length == offset + CharacterAppearanceBytes)
            {
                if (!ReadAppearance(frame.Slice(offset,frame.Length-offset),out var legacyAppearance))
                    return false;
                value = new(slot,name,legacyAppearance);
                return true;
            }
            if (frame.Length < offset + 11) return false;
            byte payloadVersion=frame[offset++];
            int appearanceBytes=payloadVersion switch
            {
                2 => LegacyCharacterAppearanceBytes,
                3 => BodyLegCharacterAppearanceBytes,
                4 => CharacterAppearanceBytes,
                _ => 0
            };
            if (appearanceBytes==0 || frame.Length < offset + 1 + appearanceBytes + 5)
                return false;
            byte appearanceKnown = frame[offset++];
            if (appearanceKnown > 1)
                return false;
            if (!ReadAppearance(frame.Slice(offset,appearanceBytes),out var decodedAppearance))
                return false;
            offset += appearanceBytes;
            if (appearanceKnown == 0 && decodedAppearance != default)
                return false;
            var creation = new CharacterCreationWire(
                frame[offset++], frame[offset++], frame[offset++], frame[offset++],
                string.Empty);
            int passwordBytes = frame[offset++];
            if (passwordBytes is < 1 or > MaxCharacterPasswordBytes ||
                frame.Length != offset + passwordBytes)
                return false;
            string characterPassword = StrictUtf8.GetString(frame.Slice(offset,passwordBytes));
            value = new(slot,name,appearanceKnown == 1 ? decodedAppearance : null,
                creation with { CharacterPassword = characterPassword });
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public static byte[] EncodeCharacterCreateResult(CharacterCreateResultWire value)
    {
        if ((byte)value.Status > (byte)CharacterCreateWireStatus.InvalidCreationSettings)
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] name = StrictUtf8.GetBytes(value.Name ?? string.Empty);
        if (value.Status == CharacterCreateWireStatus.Success)
        {
            if (value.CharacterId <= 0 || value.Slot > 31 || name.Length is < 1 or > 96)
                throw new ArgumentOutOfRangeException(nameof(value));
        }
        else if (value.CharacterId != 0 || value.Slot > 31 || name.Length != 0 || value.Appearance is not null)
            throw new ArgumentOutOfRangeException(nameof(value));
        int size = HeaderBytes + 12 + name.Length +
            (value.Appearance is null ? 0 : CharacterAppearanceBytes);
        var frame = NewFrame(WireOpcode.CharacterCreateResult, size);
        frame[8] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(9, 8), value.CharacterId);
        frame[17] = value.Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(18, 2), (ushort)name.Length);
        if (name.Length > 0) name.CopyTo(frame.AsSpan(20));
        if (value.Appearance is { } appearance)
            WriteAppearance(frame.AsSpan(20+name.Length,CharacterAppearanceBytes),appearance);
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
        if (raw > (byte)CharacterCreateWireStatus.InvalidCreationSettings || slot > 31 ||
            length > 96 || (20 + length != frame.Length &&
                20 + length + LegacyCharacterAppearanceBytes != frame.Length &&
                20 + length + BodyLegCharacterAppearanceBytes != frame.Length &&
                20 + length + CharacterAppearanceBytes != frame.Length))
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
            else if (characterId != 0 || length != 0 || frame.Length != 20) return false;
            CharacterAppearance? appearance=null;
            int remaining=frame.Length-(20+length);
            if(remaining is LegacyCharacterAppearanceBytes or
                BodyLegCharacterAppearanceBytes or CharacterAppearanceBytes)
            {
                if(!ReadAppearance(frame.Slice(20+length,remaining),out var decoded)) return false;
                appearance=decoded;
            }
            value = new(status, characterId, slot, name,appearance);
            return true;
        }
        catch (DecoderFallbackException) { return false; }
    }

    public static byte[] EncodeCharacterChoose(CharacterChooseWire value)
    {
        if (value.CharacterId <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        byte[]? proof = null;
        if (value.CharacterPassword is not null)
        {
            CharacterCreationRules.ValidatePassword(value.CharacterPassword);
            proof = StrictUtf8.GetBytes(value.CharacterPassword);
        }
        var frame = NewFrame(WireOpcode.CharacterChoose,
            CharacterChooseFrameBytes + (proof is null ? 0 : 2 + proof.Length));
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.CharacterId);
        if (proof is not null)
        {
            frame[16] = 1;
            frame[17] = checked((byte)proof.Length);
            proof.CopyTo(frame.AsSpan(18));
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(proof);
        }
        return frame;
    }

    public static bool TryDecodeCharacterChoose(ReadOnlySpan<byte> frame, out CharacterChooseWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterChoose) return false;
        long id = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        if (id <= 0) return false;
        if (frame.Length == CharacterChooseFrameBytes) { value = new(id); return true; }
        if (frame[16] != 1 || frame[17] is < 4 or > MaxCharacterPasswordBytes || frame.Length != 18 + frame[17])
            return false;
        try
        {
            string password = StrictUtf8.GetString(frame.Slice(18));
            CharacterCreationRules.ValidatePassword(password);
            value = new(id,password); return true;
        }
        catch (ArgumentException) { return false; }
    }

    public static byte[] EncodeCharacterChooseResult(CharacterChooseResultWire value)
    {
        if ((byte)value.Status > (byte)CharacterChooseWireStatus.PasswordLocked) throw new ArgumentOutOfRangeException(nameof(value));
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
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.CharacterChooseResult ||
            frame[8] > (byte)CharacterChooseWireStatus.PasswordLocked) return false;
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

    // Account-authenticated character management; credentials never appear in result DTOs.
    // Payload: action(1) characterId(8) oldLength(1) newLength(1) oldUTF8 newUTF8.
    public static byte[] EncodeCharacterManage(CharacterManageWire value)
    {
        if(value.CharacterId<=0 || value.Action is not (
            CharacterManageAction.ChangePassword or CharacterManageAction.Delete))
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Action==CharacterManageAction.ChangePassword)
            CharacterCreationRules.ValidatePassword(value.NewPassword!);
        else if(value.NewPassword is not null)
            throw new ArgumentException("Delete must not contain new password",nameof(value));
        if(value.OldPassword is not null)
            CharacterCreationRules.ValidatePassword(value.OldPassword);
        if(value.Action==CharacterManageAction.Delete && value.OldPassword is null)
            throw new ArgumentException("Deletion requires secondary proof",nameof(value));
        byte[] oldBytes=value.OldPassword is null ? [] : StrictUtf8.GetBytes(value.OldPassword);
        byte[] newBytes=value.NewPassword is null ? [] : StrictUtf8.GetBytes(value.NewPassword);
        try
        {
            if(oldBytes.Length>MaxCharacterPasswordBytes || newBytes.Length>MaxCharacterPasswordBytes)
                throw new ArgumentOutOfRangeException(nameof(value));
            var frame=NewFrame(WireOpcode.CharacterManage,HeaderBytes+11+oldBytes.Length+newBytes.Length);
            frame[8]=(byte)value.Action;
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(9,8),value.CharacterId);
            frame[17]=checked((byte)oldBytes.Length);
            frame[18]=checked((byte)newBytes.Length);
            oldBytes.CopyTo(frame.AsSpan(19));
            newBytes.CopyTo(frame.AsSpan(19+oldBytes.Length));
            return frame;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(oldBytes);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(newBytes);
        }
    }

    public static bool TryDecodeCharacterManage(ReadOnlySpan<byte> frame,out CharacterManageWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.CharacterManage ||
            frame.Length<HeaderBytes+11 || frame.Length>CharacterManageMaxFrameBytes)
            return false;
        byte action=frame[8], oldLength=frame[17], newLength=frame[18];
        long id=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(9,8));
        if(id<=0 || action is <1 or >2 || oldLength>MaxCharacterPasswordBytes ||
            newLength>MaxCharacterPasswordBytes || frame.Length!=19+oldLength+newLength ||
            (action==2 && (oldLength==0 || newLength!=0)) ||
            (action==1 && newLength==0)) return false;
        try
        {
            string? oldPassword=oldLength==0 ? null : StrictUtf8.GetString(frame.Slice(19,oldLength));
            string? newPassword=newLength==0 ? null :
                StrictUtf8.GetString(frame.Slice(19+oldLength,newLength));
            if(oldPassword is not null) CharacterCreationRules.ValidatePassword(oldPassword);
            if(newPassword is not null) CharacterCreationRules.ValidatePassword(newPassword);
            value=new((CharacterManageAction)action,id,oldPassword,newPassword);
            return true;
        }
        catch(ArgumentException) { return false; }
    }

    public static byte[] EncodeCharacterManageResult(CharacterManageResultWire value)
    {
        if(value.Action is not (CharacterManageAction.ChangePassword or CharacterManageAction.Delete) ||
            (byte)value.Status>(byte)CharacterManageStatus.HasHistory || value.CharacterId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.CharacterManageResult,CharacterManageResultFrameBytes);
        frame[8]=(byte)value.Action;
        frame[9]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(10,8),value.CharacterId);
        return frame;
    }

    public static bool TryDecodeCharacterManageResult(ReadOnlySpan<byte> frame,out CharacterManageResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.CharacterManageResult ||
            frame.Length!=CharacterManageResultFrameBytes ||
            frame[8] is <1 or >2 || frame[9]>(byte)CharacterManageStatus.HasHistory)
            return false;
        long id=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(10,8));
        if(id<=0) return false;
        value=new((CharacterManageAction)frame[8],(CharacterManageStatus)frame[9],id);
        return true;
    }

}
