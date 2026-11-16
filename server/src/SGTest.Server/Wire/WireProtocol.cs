using System.Buffers.Binary;
using System.Text;
using SGTest.Server.World;

namespace SGTest.Server.Wire;

public enum WireOpcode : ushort
{
    MoveTarget = 1,
    MoveReceipt = 2,
    NpcInteract = 3,
    NpcInteractResult = 4,
    AuthRequest = 10,
    AuthResult = 11,
    CharacterList = 12,
    CharacterChoose = 13,
    CharacterChooseResult = 14,
    WorldReady = 20,
    EntitySpawn = 21,
    EntityDespawn = 22,
    EntityDelta = 23
}

public enum AuthWireStatus : byte { Success = 0, InvalidCredentials = 1, Disabled = 2 }
public enum CharacterChooseWireStatus : byte { Success = 0, NotOwned = 1, WorldRejected = 2 }

public readonly record struct WireFrameHeader(int FrameBytes, ushort Version, WireOpcode Opcode);
public readonly record struct MoveTargetWire(long MapEpoch, uint MovementSequence, int TargetX, int TargetY);
public readonly record struct MoveReceiptWire(long MapEpoch, uint MovementSequence, MoveReceiptStatus Status, long ServerTick);
public readonly record struct NpcInteractWire(long MapEpoch, uint InteractionSequence, int NpcSpawnId);
public readonly record struct NpcInteractResultWire(
    long RequestMapEpoch, uint InteractionSequence, NpcPortalStatus Status,
    long ResultMapEpoch);
public readonly record struct AuthRequestWire(string Username, string Password);
public readonly record struct AuthResultWire(AuthWireStatus Status);
public readonly record struct CharacterSummaryWire(long CharacterId, byte Slot, string Name);
public sealed record CharacterListWire(IReadOnlyList<CharacterSummaryWire> Characters);
public readonly record struct CharacterChooseWire(long CharacterId);
public readonly record struct CharacterChooseResultWire(
    CharacterChooseWireStatus Status, long CharacterId, int MapId,
    double X, double Y, long MapEpoch);
public readonly record struct WorldReadyWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId,
    int MapId, float X, float Y);
public readonly record struct EntityStateWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId,
    long ConnectionEpoch, long EntityMapEpoch, int MapId,
    long Revision, float X, float Y, bool IsMoving, uint MovementActionSequence);
public readonly record struct EntityDespawnWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId, long ConnectionEpoch);

/// <summary>
/// Version 1 complete-frame codec. Transport/TLS/socket buffering is separate.
/// Header = uint32 frameBytes + uint16 version + uint16 opcode, little-endian.
/// Identity credentials are allowed only inside the mandatory TLS authorization phase.
/// ActorId and ConnectionEpoch remain server-owned and absent from gameplay commands.
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
    public const int NpcInteractFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int NpcInteractResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8;
    public const int AuthResultFrameBytes = HeaderBytes + 1;
    public const int CharacterChooseFrameBytes = HeaderBytes + 8;
    public const int CharacterChooseResultFrameBytes = HeaderBytes + 37;
    public const int MaxAuthRequestFrameBytes = HeaderBytes + 4 + 32 + 256;
    public const int MaxCharacterListFrameBytes = 4096;
    // Single-precision coordinates suffice across the original ~15k-pixel maps
    // (sub-0.002px resolution), saving bandwidth vs storing double on wire.
    public const int WorldReadyFrameBytes = 44;
    public const int EntityStateFrameBytes = 73;
    public const int EntityDespawnFrameBytes = 40;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] EncodeMoveTarget(MoveTargetWire value)
    {
        if (value.MapEpoch <= 0 || value.MovementSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
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
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.MoveTarget) return false;
        long mapEpoch = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4));
        if (mapEpoch <= 0 || sequence == 0) return false;
        value = new(mapEpoch, sequence,
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24, 4)));
        return true;
    }

    public static byte[] EncodeNpcInteract(NpcInteractWire value)
    {
        if (value.MapEpoch <= 0 || value.InteractionSequence == 0 || value.NpcSpawnId <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] frame = NewFrame(WireOpcode.NpcInteract, NpcInteractFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.InteractionSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeNpcInteract(ReadOnlySpan<byte> frame, out NpcInteractWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) || header.Opcode != WireOpcode.NpcInteract)
            return false;
        value = new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)));
        return value.MapEpoch > 0 && value.InteractionSequence != 0 && value.NpcSpawnId > 0;
    }

    public static byte[] EncodeNpcInteractResult(NpcInteractResultWire value)
    {
        if (value.RequestMapEpoch <= 0 || value.InteractionSequence == 0 ||
            (byte)value.Status > (byte)NpcPortalStatus.DestinationRejected ||
            value.ResultMapEpoch <= 0 ||
            (value.Status == NpcPortalStatus.Accepted
                ? value.ResultMapEpoch <= value.RequestMapEpoch
                : value.ResultMapEpoch != value.RequestMapEpoch))
            throw new ArgumentOutOfRangeException(nameof(value));
        byte[] frame = NewFrame(WireOpcode.NpcInteractResult, NpcInteractResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.RequestMapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.InteractionSequence);
        frame[20] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21, 8), value.ResultMapEpoch);
        return frame;
    }

    public static bool TryDecodeNpcInteractResult(ReadOnlySpan<byte> frame,
        out NpcInteractResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) || header.Opcode != WireOpcode.NpcInteractResult)
            return false;
        var status = (NpcPortalStatus)frame[20];
        value = new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            status, BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21, 8)));
        return value.RequestMapEpoch > 0 && value.InteractionSequence != 0 &&
               (byte)status <= (byte)NpcPortalStatus.DestinationRejected &&
               (status == NpcPortalStatus.Accepted
                   ? value.ResultMapEpoch > value.RequestMapEpoch
                   : value.ResultMapEpoch == value.RequestMapEpoch);
    }

    public static byte[] EncodeMoveReceipt(MoveReceiptWire value)
    {
        if (value.MapEpoch <= 0 || value.MovementSequence == 0 || value.ServerTick < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.MoveReceipt, MoveReceiptFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.MovementSequence);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(20, 2), EncodeStatus(value.Status));
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22, 8), value.ServerTick);
        return frame;
    }

    public static bool TryDecodeMoveReceipt(ReadOnlySpan<byte> frame, out MoveReceiptWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.MoveReceipt) return false;
        long mapEpoch = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8));
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4));
        ushort rawStatus = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(20, 2));
        long tick = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22, 8));
        if (mapEpoch <= 0 || sequence == 0 || tick < 0 || !TryDecodeStatus(rawStatus, out var status)) return false;
        value = new(mapEpoch, sequence, status, tick);
        return true;
    }

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

    public static byte[] EncodeWorldReady(WorldReadyWire value)
    {
        if (!ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                           value.MapId, value.X, value.Y))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.WorldReady, WorldReadyFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.ActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32, 4), value.MapId);
        WriteFloat(frame.AsSpan(36, 4), value.X);
        WriteFloat(frame.AsSpan(40, 4), value.Y);
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
            ReadFloat(frame.Slice(36, 4)), ReadFloat(frame.Slice(40, 4)));
        return ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                             value.MapId, value.X, value.Y);
    }

    public static byte[] EncodeEntityState(WireOpcode opcode, EntityStateWire value)
    {
        if (opcode is not (WireOpcode.EntitySpawn or WireOpcode.EntityDelta) ||
            !ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                           value.MapId, value.X, value.Y) ||
            value.ConnectionEpoch <= 0 || value.EntityMapEpoch <= 0 || value.Revision <= 0)
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
        return frame;
    }

    public static bool TryDecodeEntityState(ReadOnlySpan<byte> frame,
        out WireOpcode opcode, out EntityStateWire value)
    {
        opcode = 0; value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode is not (WireOpcode.EntitySpawn or WireOpcode.EntityDelta) ||
            frame[68] > 1)
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
            frame[68] != 0, BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(69, 4)));
        return ValidMapState(value.ReceiverMapEpoch, value.ServerTick, value.ActorId,
                             value.MapId, value.X, value.Y) &&
               value.ConnectionEpoch > 0 && value.EntityMapEpoch > 0 && value.Revision > 0;
    }

    public static byte[] EncodeEntityDespawn(EntityDespawnWire value)
    {
        if (value.ReceiverMapEpoch <= 0 || value.ServerTick < 0 ||
            value.ActorId <= 0 || value.ConnectionEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.EntityDespawn, EntityDespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32, 8), value.ConnectionEpoch);
        return frame;
    }

    public static bool TryDecodeEntityDespawn(ReadOnlySpan<byte> frame, out EntityDespawnWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) || header.Opcode != WireOpcode.EntityDespawn)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32, 8)));
        return value.ReceiverMapEpoch > 0 && value.ServerTick >= 0 &&
               value.ActorId > 0 && value.ConnectionEpoch > 0;
    }

    private static bool ValidMapState(long receiverMapEpoch, long tick,
        long actorId, int mapId, float x, float y) =>
        receiverMapEpoch > 0 && tick >= 0 && actorId > 0 && mapId > 0 &&
        float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0;

    private static void WriteFloat(Span<byte> target, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(target, BitConverter.SingleToInt32Bits(value));

    private static float ReadFloat(ReadOnlySpan<byte> source) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    public static bool TryReadHeader(ReadOnlySpan<byte> frame, out WireFrameHeader header)
    {
        header = default;
        if (frame.Length < HeaderBytes || frame.Length > MaximumFrameBytes) return false;
        int advertised = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(0, 4));
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(4, 2));
        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(6, 2));
        if (advertised != frame.Length || advertised < HeaderBytes || version != Version) return false;
        WireOpcode opcode = raw switch
        {
            1 => WireOpcode.MoveTarget, 2 => WireOpcode.MoveReceipt,
            3 => WireOpcode.NpcInteract, 4 => WireOpcode.NpcInteractResult,
            10 => WireOpcode.AuthRequest, 11 => WireOpcode.AuthResult,
            12 => WireOpcode.CharacterList, 13 => WireOpcode.CharacterChoose,
            14 => WireOpcode.CharacterChooseResult,
            20 => WireOpcode.WorldReady, 21 => WireOpcode.EntitySpawn,
            22 => WireOpcode.EntityDespawn, 23 => WireOpcode.EntityDelta, _ => 0
        };
        if (opcode == 0) return false;
        bool sizeOk = opcode switch
        {
            WireOpcode.MoveTarget => advertised == MoveTargetFrameBytes,
            WireOpcode.MoveReceipt => advertised == MoveReceiptFrameBytes,
            WireOpcode.NpcInteract => advertised == NpcInteractFrameBytes,
            WireOpcode.NpcInteractResult => advertised == NpcInteractResultFrameBytes,
            WireOpcode.AuthRequest => advertised is >= 23 and <= MaxAuthRequestFrameBytes,
            WireOpcode.AuthResult => advertised == AuthResultFrameBytes,
            WireOpcode.CharacterList => advertised is >= 9 and <= MaxCharacterListFrameBytes,
            WireOpcode.CharacterChoose => advertised == CharacterChooseFrameBytes,
            WireOpcode.CharacterChooseResult => advertised == CharacterChooseResultFrameBytes,
            WireOpcode.WorldReady => advertised == WorldReadyFrameBytes,
            WireOpcode.EntitySpawn or WireOpcode.EntityDelta => advertised == EntityStateFrameBytes,
            WireOpcode.EntityDespawn => advertised == EntityDespawnFrameBytes,
            _ => false
        };
        if (!sizeOk) return false;
        header = new(advertised, version, opcode); return true;
    }

    private static byte[] NewFrame(WireOpcode opcode, int frameBytes)
    {
        if (frameBytes < HeaderBytes || frameBytes > MaximumFrameBytes) throw new ArgumentOutOfRangeException(nameof(frameBytes));
        var frame = new byte[frameBytes];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), frameBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)opcode);
        return frame;
    }

    private static ushort EncodeStatus(MoveReceiptStatus status) => status switch
    {
        MoveReceiptStatus.Accepted => 0, MoveReceiptStatus.UnknownActor => 1,
        MoveReceiptStatus.OldConnectionEpoch => 2, MoveReceiptStatus.OldMapEpoch => 3,
        MoveReceiptStatus.ReplayedSequence => 4, MoveReceiptStatus.InvalidTarget => 5,
        MoveReceiptStatus.TargetBlocked => 6, MoveReceiptStatus.PathBudgetExhausted => 7,
        MoveReceiptStatus.PathNotFound => 8, MoveReceiptStatus.TickRouteBudgetExhausted => 9,
        MoveReceiptStatus.RouteBecameBlocked => 10, _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static bool TryDecodeStatus(ushort raw, out MoveReceiptStatus status)
    {
        status = raw switch
        {
            0 => MoveReceiptStatus.Accepted, 1 => MoveReceiptStatus.UnknownActor,
            2 => MoveReceiptStatus.OldConnectionEpoch, 3 => MoveReceiptStatus.OldMapEpoch,
            4 => MoveReceiptStatus.ReplayedSequence, 5 => MoveReceiptStatus.InvalidTarget,
            6 => MoveReceiptStatus.TargetBlocked, 7 => MoveReceiptStatus.PathBudgetExhausted,
            8 => MoveReceiptStatus.PathNotFound, 9 => MoveReceiptStatus.TickRouteBudgetExhausted,
            10 => MoveReceiptStatus.RouteBecameBlocked, _ => (MoveReceiptStatus)(-1)
        };
        return raw <= 10;
    }
}
