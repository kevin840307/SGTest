using System.Buffers.Binary;
using System.Text;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

public enum WireOpcode : ushort
{
    MoveTarget = 1,
    MoveReceipt = 2,
    NpcInteract = 3,
    NpcInteractResult = 4,
    NormalAttack = 5,
    NormalAttackResult = 6,
    PlayerCombatState = 7,
    PlayerDamage = 8,
    AuthRequest = 10,
    AuthResult = 11,
    CharacterList = 12,
    CharacterChoose = 13,
    CharacterChooseResult = 14,
    WorldReady = 20,
    EntitySpawn = 21,
    EntityDespawn = 22,
    EntityDelta = 23,
    SelfState = 24,
    WorldReadyAck = 25,
    WorldResyncRequest = 26,
    MonsterSpawn = 27,
    MonsterDelta = 28,
    MonsterDespawn = 29,
    PlayerRespawn = 30,
    PlayerRespawnResult = 31,
    GroundDropSpawn = 32,
    GroundDropDespawn = 33,
    GroundPickup = 34,
    GroundPickupResult = 35,
    InventorySnapshot = 36,
    RecoveryUse = 37,
    RecoveryUseResult = 38,
    InventoryDiscard = 39,
    InventoryDiscardResult = 40,
    PlayerAttackVisual = 41
}

public enum AuthWireStatus : byte { Success = 0, InvalidCredentials = 1, Disabled = 2 }
public enum CharacterChooseWireStatus : byte { Success = 0, NotOwned = 1, WorldRejected = 2 }
public enum PlayerRespawnStatus : byte
{
    Accepted = 0,
    NotDead = 1,
    CombatUnavailable = 2,
    StaleGeneration = 3,
    ReplayedSequence = 4
}

public readonly record struct WireFrameHeader(int FrameBytes, ushort Version, WireOpcode Opcode);
public readonly record struct MoveTargetWire(long MapEpoch, uint MovementSequence, int TargetX, int TargetY);
public readonly record struct MoveReceiptWire(long MapEpoch, uint MovementSequence, MoveReceiptStatus Status, long ServerTick);
public readonly record struct NpcInteractWire(long MapEpoch, uint InteractionSequence, int NpcSpawnId);
public readonly record struct NpcInteractResultWire(
    long RequestMapEpoch, uint InteractionSequence, NpcPortalStatus Status,
    long ResultMapEpoch);
public readonly record struct NormalAttackWire(
    long MapEpoch, uint AttackSequence, long TargetEntityId);
public readonly record struct NormalAttackResultWire(
    long MapEpoch, uint AttackSequence, NormalAttackStatus Status,
    long ServerTick, long TargetEntityId, int Damage,
    int CurrentHp, int MaxHp, long TargetRevision);
public readonly record struct PlayerAttackVisualWire(
    long ReceiverMapEpoch,long ServerTick,long AttackerActorId,
    long AttackerConnectionEpoch,uint AttackSequence,long TargetEntityId,
    NormalAttackStatus Status,int Damage,long TargetRevision,byte Facing);
public readonly record struct PlayerCombatStateWire(
    long MapEpoch, int CurrentHp, int MaxHp);
public readonly record struct PlayerDamageWire(
    long MapEpoch, uint DamageSequence, long ServerTick,
    long AttackerMonsterEntityId, long AttackerMonsterRevision,
    int Damage, int CurrentHp, int MaxHp, bool Died);
public readonly record struct PlayerRespawnWire(
    long MapEpoch, uint RespawnSequence);
public readonly record struct PlayerRespawnResultWire(
    long MapEpoch, uint RespawnSequence, PlayerRespawnStatus Status,
    int CurrentHp, int MaxHp);
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
    int MapId, float X, float Y, byte Facing = 1);
public readonly record struct EntityStateWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId,
    long ConnectionEpoch, long EntityMapEpoch, int MapId,
    long Revision, float X, float Y, bool IsMoving, uint MovementActionSequence, byte Facing = 1);
public readonly record struct EntityDespawnWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId, long ConnectionEpoch);
public readonly record struct WorldReadyAckWire(long MapEpoch, long ServerTick);
public readonly record struct WorldResyncRequestWire(long MapEpoch);
public readonly record struct MonsterStateWire(
    long ReceiverMapEpoch, long ServerTick, long EntityId,
    int EnemyId, int SourceIndex, int MapId, float X, float Y,
    long Revision, int CurrentHp, int MaxHp, MonsterAiState State,
    byte Facing, long TargetActorId);
public readonly record struct MonsterDespawnWire(
    long ReceiverMapEpoch, long ServerTick, long EntityId, long Revision);
public readonly record struct GroundDropWire(
    long ReceiverMapEpoch, long ServerTick, long DropId, int MapId,
    float X, float Y, int ItemDefinitionId, int Quantity);
public readonly record struct GroundDropDespawnWire(
    long ReceiverMapEpoch, long ServerTick, long DropId);
public readonly record struct RecoveryUseWire(
    long MapEpoch,uint UseSequence,long ItemUid);
public readonly record struct RecoveryUseResultWire(
    long MapEpoch,uint UseSequence,RecoveryUseStatus Status,
    long ItemUid,int ItemDefinitionId,int RemainingQuantity,
    int CurrentHp,int MaxHp);
public readonly record struct InventoryDiscardWire(
    long MapEpoch,uint DiscardSequence,long ItemUid,int Quantity);
public readonly record struct InventoryDiscardResultWire(
    long MapEpoch,uint DiscardSequence,InventoryDiscardStatus Status,
    long ItemUid,int ItemDefinitionId,int DiscardedQuantity,int RemainingQuantity);
public readonly record struct GroundPickupWire(
    long MapEpoch,uint PickupSequence,long DropId);
public readonly record struct GroundPickupResultWire(
    long MapEpoch,uint PickupSequence,PickupStatus Status,
    long DropId,long ItemUid,int ItemDefinitionId,int Quantity);
public readonly record struct InventoryItemWire(
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId);
public sealed record InventorySnapshotWire(
    long CharacterId,int StartIndex,bool Complete,
    IReadOnlyList<InventoryItemWire> Items);

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
    public const int NormalAttackFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int NormalAttackResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 4 + 4 + 4 + 8;
    public const int PlayerAttackVisualFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 4 + 8 + 1 + 4 + 8 + 1;
    public const int PlayerCombatStateFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int PlayerDamageFrameBytes = HeaderBytes + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 4 + 1;
    public const int PlayerRespawnFrameBytes = HeaderBytes + 8 + 4;
    public const int PlayerRespawnResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 4 + 4;
    public const int AuthResultFrameBytes = HeaderBytes + 1;
    public const int CharacterChooseFrameBytes = HeaderBytes + 8;
    public const int CharacterChooseResultFrameBytes = HeaderBytes + 37;
    public const int MaxAuthRequestFrameBytes = HeaderBytes + 4 + 32 + 256;
    public const int MaxCharacterListFrameBytes = 4096;
    // Single-precision coordinates suffice across the original ~15k-pixel maps
    // (sub-0.002px resolution), saving bandwidth vs storing double on wire.
    public const int WorldReadyFrameBytes = 45;
    public const int EntityStateFrameBytes = 74;
    public const int EntityDespawnFrameBytes = 40;
    public const int WorldReadyAckFrameBytes = HeaderBytes + 16;
    public const int WorldResyncRequestFrameBytes = HeaderBytes + 8;
    public const int MonsterStateFrameBytes = 78;
    public const int MonsterDespawnFrameBytes = 40;
    public const int GroundDropSpawnFrameBytes = 52;
    public const int GroundDropDespawnFrameBytes = 32;
    public const int RecoveryUseFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int RecoveryUseResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 4 + 4 + 4 + 4;
    public const int InventoryDiscardFrameBytes = HeaderBytes + 8 + 4 + 8 + 4;
    public const int InventoryDiscardResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 4 + 4 + 4;
    public const int GroundPickupFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int GroundPickupResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 4 + 4;
    public const int InventorySnapshotHeaderBytes = HeaderBytes + 8 + 4 + 1 + 2;
    public const int InventoryItemBytes = 24;
    public const int InventorySnapshotMaxItemsPerFrame = 128;
    public const int InventorySnapshotMaxItems = 4095;
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
            (byte)value.Status > (byte)NpcPortalStatus.CombatDead ||
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
               (byte)status <= (byte)NpcPortalStatus.CombatDead &&
               (status == NpcPortalStatus.Accepted
                   ? value.ResultMapEpoch > value.RequestMapEpoch
                   : value.ResultMapEpoch == value.RequestMapEpoch);
    }

    public static byte[] EncodeNormalAttack(NormalAttackWire value)
    {
        if (value.MapEpoch <= 0 || value.AttackSequence == 0 || value.TargetEntityId <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.NormalAttack, NormalAttackFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20, 8), value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodeNormalAttack(ReadOnlySpan<byte> frame, out NormalAttackWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.NormalAttack)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20, 8)));
        return value.MapEpoch > 0 && value.AttackSequence != 0 && value.TargetEntityId > 0;
    }

    public static byte[] EncodeNormalAttackResult(NormalAttackResultWire value)
    {
        if (!ValidNormalAttackResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.NormalAttackResult, NormalAttackResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.AttackSequence);
        frame[20] = (byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29, 8), value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37, 4), value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45, 4), value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(49, 8), value.TargetRevision);
        return frame;
    }

    public static bool TryDecodeNormalAttackResult(
        ReadOnlySpan<byte> frame, out NormalAttackResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.NormalAttackResult)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            (NormalAttackStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(45, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(49, 8)));
        return ValidNormalAttackResult(value);
    }

    public static byte[] EncodePlayerAttackVisual(PlayerAttackVisualWire value)
    {
        if(!ValidPlayerAttackVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerAttackVisual,PlayerAttackVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.TargetEntityId);
        frame[52]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(53,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(57,8),value.TargetRevision);
        frame[65]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerAttackVisual(
        ReadOnlySpan<byte> frame,out PlayerAttackVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerAttackVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            (NormalAttackStatus)frame[52],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(53,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(57,8)),
            frame[65]);
        return ValidPlayerAttackVisual(value);
    }

    public static byte[] EncodePlayerCombatState(PlayerCombatStateWire value)
    {
        if (!ValidPlayerCombatState(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerCombatState, PlayerCombatStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), value.MaxHp);
        return frame;
    }

    public static bool TryDecodePlayerCombatState(
        ReadOnlySpan<byte> frame, out PlayerCombatStateWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerCombatState)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)));
        return ValidPlayerCombatState(value);
    }

    public static byte[] EncodePlayerDamage(PlayerDamageWire value)
    {
        if (!ValidPlayerDamage(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerDamage, PlayerDamageFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28, 8), value.AttackerMonsterEntityId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36, 8), value.AttackerMonsterRevision);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44, 4), value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(52, 4), value.MaxHp);
        frame[56] = value.Died ? (byte)1 : (byte)0;
        return frame;
    }

    public static bool TryDecodePlayerDamage(
        ReadOnlySpan<byte> frame, out PlayerDamageWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerDamage)
            return false;
        byte died = frame[56];
        if (died > 1) return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36, 8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(52, 4)),
            died != 0);
        return ValidPlayerDamage(value);
    }

    public static byte[] EncodePlayerRespawn(PlayerRespawnWire value)
    {
        if (value.MapEpoch <= 0 || value.RespawnSequence == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerRespawn, PlayerRespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.RespawnSequence);
        return frame;
    }

    public static bool TryDecodePlayerRespawn(
        ReadOnlySpan<byte> frame, out PlayerRespawnWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerRespawn)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)));
        return value.MapEpoch > 0 && value.RespawnSequence != 0;
    }

    public static byte[] EncodePlayerRespawnResult(PlayerRespawnResultWire value)
    {
        if (!ValidPlayerRespawnResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerRespawnResult, PlayerRespawnResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16, 4), value.RespawnSequence);
        frame[20] = (byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25, 4), value.MaxHp);
        return frame;
    }

    public static bool TryDecodePlayerRespawnResult(
        ReadOnlySpan<byte> frame, out PlayerRespawnResultWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var h) || h.Opcode != WireOpcode.PlayerRespawnResult)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16, 4)),
            (PlayerRespawnStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25, 4)));
        return ValidPlayerRespawnResult(value);
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

    /// <summary>Server-only immutable inventory baseline; chunks must be
    /// consecutive and a final chunk is mandatory even for zero inventory.</summary>
    public static byte[] EncodeInventorySnapshot(InventorySnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.CharacterId<=0||value.StartIndex<0||
            value.StartIndex>InventorySnapshotMaxItems ||
            value.Items is null || value.Items.Count>InventorySnapshotMaxItemsPerFrame ||
            value.StartIndex+value.Items.Count>InventorySnapshotMaxItems)
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<long>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000 ||
               item.Quantity is <1 or >1_000_000 || item.SourceDropId<=0 ||
               !ids.Add(item.ItemUid))
                throw new ArgumentOutOfRangeException(nameof(value));
        int count=value.Items.Count;
        var frame=NewFrame(WireOpcode.InventorySnapshot,
            InventorySnapshotHeaderBytes+count*InventoryItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4),value.StartIndex);
        frame[20]=(byte)(value.Complete?1:0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(21,2),checked((ushort)count));
        for(int i=0;i<count;i++)
        {
            var item=value.Items[i];
            int offset=InventorySnapshotHeaderBytes+i*InventoryItemBytes;
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
        }
        return frame;
    }

    public static bool TryDecodeInventorySnapshot(
        ReadOnlySpan<byte> frame,out InventorySnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var header)||
            header.Opcode!=WireOpcode.InventorySnapshot) return false;
        long character=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4));
        byte complete=frame[20];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(21,2));
        if(character<=0||start<0||complete>1||
            count>InventorySnapshotMaxItemsPerFrame||
            start+count>InventorySnapshotMaxItems ||
            frame.Length!=InventorySnapshotHeaderBytes+count*InventoryItemBytes)
            return false;
        var items=new List<InventoryItemWire>(count);
        var ids=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=InventorySnapshotHeaderBytes+i*InventoryItemBytes;
            var item=new InventoryItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)));
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000 ||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<=0||
               !ids.Add(item.ItemUid)) return false;
            items.Add(item);
        }
        value=new(character,start,complete==1,items);
        return true;
    }

    public static byte[] EncodeRecoveryUse(RecoveryUseWire value)
    {
        if(value.MapEpoch<=0||value.UseSequence==0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RecoveryUse,RecoveryUseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.UseSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        return frame;
    }
    public static bool TryDecodeRecoveryUse(ReadOnlySpan<byte> frame,
        out RecoveryUseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.RecoveryUse)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0 && value.UseSequence>0 && value.ItemUid>0;
    }
    public static byte[] EncodeRecoveryUseResult(RecoveryUseResultWire value)
    {
        if(!ValidRecoveryUseResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RecoveryUseResult,RecoveryUseResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.UseSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.RemainingQuantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.MaxHp);
        return frame;
    }
    public static bool TryDecodeRecoveryUseResult(ReadOnlySpan<byte> frame,
        out RecoveryUseResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.RecoveryUseResult)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (RecoveryUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)));
        return ValidRecoveryUseResult(value);
    }
    private static bool ValidRecoveryUseResult(RecoveryUseResultWire value)
    {
        if(value.MapEpoch<=0||value.UseSequence==0||value.ItemUid<=0||
            (byte)value.Status>(byte)RecoveryUseStatus.CombatDiverged)
            return false;
        if(value.Status is RecoveryUseStatus.Granted or RecoveryUseStatus.AlreadyGranted)
            return value.ItemDefinitionId is >0 and <=100_000_000 &&
                value.RemainingQuantity is >=0 and <=1_000_000 &&
                value.CurrentHp>0 && value.MaxHp>=value.CurrentHp;
        return value.ItemDefinitionId==0 && value.RemainingQuantity==0 &&
            value.CurrentHp==0 && value.MaxHp==0;
    }

    public static byte[] EncodeInventoryDiscard(InventoryDiscardWire value)
    {
        if(value.MapEpoch<=0||value.DiscardSequence==0||value.ItemUid<=0||
            value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.InventoryDiscard,InventoryDiscardFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DiscardSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeInventoryDiscard(ReadOnlySpan<byte> frame,
        out InventoryDiscardWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.InventoryDiscard)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.DiscardSequence>0&&value.ItemUid>0&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeInventoryDiscardResult(InventoryDiscardResultWire value)
    {
        if(!ValidInventoryDiscardResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.InventoryDiscardResult,
            InventoryDiscardResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DiscardSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.DiscardedQuantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.RemainingQuantity);
        return frame;
    }

    public static bool TryDecodeInventoryDiscardResult(ReadOnlySpan<byte> frame,
        out InventoryDiscardResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.InventoryDiscardResult)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (InventoryDiscardStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)));
        return ValidInventoryDiscardResult(value);
    }

    private static bool ValidInventoryDiscardResult(InventoryDiscardResultWire value)
    {
        if(value.MapEpoch<=0||value.DiscardSequence==0||value.ItemUid<=0||
            (byte)value.Status>(byte)InventoryDiscardStatus.StaleGeneration)
            return false;
        if(value.Status is InventoryDiscardStatus.Applied or
            InventoryDiscardStatus.AlreadyApplied)
            return value.ItemDefinitionId is >0 and <=100_000_000 &&
                value.DiscardedQuantity is >0 and <=1_000_000 &&
                value.RemainingQuantity is >=0 and <=1_000_000;
        return value.ItemDefinitionId==0&&value.DiscardedQuantity==0&&
            value.RemainingQuantity==0;
    }

    public static byte[] EncodeGroundPickup(GroundPickupWire value)
    {
        if (value.MapEpoch<=0||value.PickupSequence==0||value.DropId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GroundPickup,GroundPickupFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PickupSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.DropId);
        return frame;
    }
    public static bool TryDecodeGroundPickup(ReadOnlySpan<byte> frame,out GroundPickupWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GroundPickup)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.PickupSequence>0&&value.DropId>0;
    }
    public static byte[] EncodeGroundPickupResult(GroundPickupResultWire value)
    {
        if (!ValidPickupResult(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GroundPickupResult,GroundPickupResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PickupSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.DropId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Quantity);
        return frame;
    }
    public static bool TryDecodeGroundPickupResult(
        ReadOnlySpan<byte> frame,out GroundPickupResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GroundPickupResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PickupStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)));
        return ValidPickupResult(value);
    }
    private static bool ValidPickupResult(GroundPickupResultWire value)
    {
        if (value.MapEpoch<=0 || value.PickupSequence==0 || value.DropId<=0 ||
            (byte)value.Status>(byte)PickupStatus.PlayerDead ||
            value.ItemUid<0 || value.ItemDefinitionId<0 || value.Quantity<0)
            return false;
        return value.Status is PickupStatus.Granted or PickupStatus.AlreadyGranted
            ? value.ItemUid>0 && value.ItemDefinitionId is >0 and <=100_000_000 &&
              value.Quantity is >0 and <=1_000_000
            : value.ItemUid==0 && value.ItemDefinitionId==0 && value.Quantity==0;
    }

    public static byte[] EncodeGroundDropSpawn(GroundDropWire value)
    {
        if (!ValidGroundDrop(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.GroundDropSpawn,GroundDropSpawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.DropId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32,4),value.MapId);
        WriteFloat(frame.AsSpan(36,4),value.X);
        WriteFloat(frame.AsSpan(40,4),value.Y);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeGroundDropSpawn(ReadOnlySpan<byte> frame,out GroundDropWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
           header.Opcode != WireOpcode.GroundDropSpawn) return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(32,4)),
            ReadFloat(frame.Slice(36,4)),ReadFloat(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48,4)));
        return ValidGroundDrop(value);
    }

    public static byte[] EncodeGroundDropDespawn(GroundDropDespawnWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||value.DropId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GroundDropDespawn,GroundDropDespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.DropId);
        return frame;
    }

    public static bool TryDecodeGroundDropDespawn(
        ReadOnlySpan<byte> frame,out GroundDropDespawnWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header)||
           header.Opcode!=WireOpcode.GroundDropDespawn) return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.ReceiverMapEpoch>0&&value.ServerTick>=0&&value.DropId>0;
    }

    private static bool ValidGroundDrop(GroundDropWire value) =>
        value.ReceiverMapEpoch>0 && value.ServerTick>=0 && value.DropId>0 &&
        value.MapId is >=1 and <=65535 &&
        float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        value.X>=0 && value.Y>=0 &&
        value.ItemDefinitionId is >=1 and <=100_000_000 &&
        value.Quantity is >=1 and <=1_000_000;

    public static byte[] EncodeMonsterDespawn(MonsterDespawnWire value)
    {
        if (value.ReceiverMapEpoch <= 0 || value.ServerTick < 0 ||
            value.EntityId <= 0 || value.Revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.MonsterDespawn, MonsterDespawnFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16, 8), value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24, 8), value.EntityId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32, 8), value.Revision);
        return frame;
    }

    public static bool TryDecodeMonsterDespawn(
        ReadOnlySpan<byte> frame, out MonsterDespawnWire value)
    {
        value = default;
        if (!TryReadHeader(frame, out var header) ||
            header.Opcode != WireOpcode.MonsterDespawn)
            return false;
        value = new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24, 8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32, 8)));
        return value.ReceiverMapEpoch > 0 && value.ServerTick >= 0 &&
               value.EntityId > 0 && value.Revision > 0;
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

    private static bool ValidPlayerRespawnResult(PlayerRespawnResultWire value)
    {
        if (value.MapEpoch <= 0 || value.RespawnSequence == 0 ||
            (byte)value.Status > (byte)PlayerRespawnStatus.ReplayedSequence ||
            value.CurrentHp < 0 || value.MaxHp < 0 ||
            value.CurrentHp > value.MaxHp)
            return false;
        if (value.Status == PlayerRespawnStatus.Accepted)
            return value.MaxHp > 0 && value.CurrentHp == value.MaxHp;
        if (value.Status == PlayerRespawnStatus.NotDead)
            return value.MaxHp > 0 && value.CurrentHp > 0;
        return true;
    }

    private static bool ValidPlayerAttackVisual(PlayerAttackVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0 || value.ServerTick<0 ||
            value.AttackerActorId<=0 || value.AttackerConnectionEpoch<=0 ||
            value.AttackSequence==0 || value.TargetEntityId<=0 ||
            value.TargetRevision<=0 || value.Facing is <1 or >8)
            return false;
        if(value.Status==NormalAttackStatus.Applied)
            return value.Damage>0;
        if(value.Status==NormalAttackStatus.Miss)
            return value.Damage==0;
        return false;
    }

    private static bool ValidPlayerCombatState(PlayerCombatStateWire value) =>
        value.MapEpoch > 0 && value.MaxHp > 0 &&
        value.CurrentHp >= 0 && value.CurrentHp <= value.MaxHp;

    private static bool ValidPlayerDamage(PlayerDamageWire value) =>
        value.MapEpoch > 0 && value.DamageSequence != 0 &&
        value.ServerTick >= 0 && value.AttackerMonsterEntityId > 0 &&
        value.AttackerMonsterRevision > 0 && value.Damage > 0 &&
        value.MaxHp > 0 && value.CurrentHp >= 0 &&
        value.CurrentHp <= value.MaxHp &&
        value.Died == (value.CurrentHp == 0);

    private static bool ValidNormalAttackResult(NormalAttackResultWire value)
    {
        if (value.MapEpoch <= 0 || value.AttackSequence == 0 ||
            (byte)value.Status > (byte)NormalAttackStatus.ReplayedSequence ||
            value.ServerTick < 0 || value.TargetEntityId <= 0 ||
            value.Damage < 0 || value.CurrentHp < 0 || value.MaxHp < 0 ||
            value.TargetRevision < 0)
            return false;
        if (value.MaxHp > 0 && value.CurrentHp > value.MaxHp) return false;
        if (value.Status == NormalAttackStatus.Applied)
            return value.Damage > 0 && value.MaxHp > 0 && value.TargetRevision > 0;
        if (value.Status == NormalAttackStatus.Miss)
            return value.Damage == 0 && value.MaxHp > 0 && value.TargetRevision > 0;
        return value.Damage == 0;
    }

    private static bool ValidMonsterState(MonsterStateWire value) =>
        value.ReceiverMapEpoch > 0 && value.ServerTick >= 0 &&
        value.EntityId > 0 && value.EnemyId > 0 && value.SourceIndex >= 0 &&
        value.MapId > 0 && float.IsFinite(value.X) && float.IsFinite(value.Y) &&
        value.X >= 0 && value.Y >= 0 && value.Revision > 0 &&
        value.MaxHp > 0 && value.CurrentHp >= 0 && value.CurrentHp <= value.MaxHp &&
        (byte)value.State <= (byte)MonsterAiState.Dead &&
        value.Facing is >= 1 and <= 8 && value.TargetActorId >= 0;

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
            5 => WireOpcode.NormalAttack, 6 => WireOpcode.NormalAttackResult,
            7 => WireOpcode.PlayerCombatState, 8 => WireOpcode.PlayerDamage,
            10 => WireOpcode.AuthRequest, 11 => WireOpcode.AuthResult,
            12 => WireOpcode.CharacterList, 13 => WireOpcode.CharacterChoose,
            14 => WireOpcode.CharacterChooseResult,
            20 => WireOpcode.WorldReady, 21 => WireOpcode.EntitySpawn,
            22 => WireOpcode.EntityDespawn, 23 => WireOpcode.EntityDelta,
            24 => WireOpcode.SelfState, 25 => WireOpcode.WorldReadyAck,
            26 => WireOpcode.WorldResyncRequest, 27 => WireOpcode.MonsterSpawn,
            28 => WireOpcode.MonsterDelta, 29 => WireOpcode.MonsterDespawn,
            30 => WireOpcode.PlayerRespawn, 31 => WireOpcode.PlayerRespawnResult,
            32 => WireOpcode.GroundDropSpawn, 33 => WireOpcode.GroundDropDespawn,
            34 => WireOpcode.GroundPickup, 35 => WireOpcode.GroundPickupResult,
            36 => WireOpcode.InventorySnapshot, 37 => WireOpcode.RecoveryUse,
            38 => WireOpcode.RecoveryUseResult,
            39 => WireOpcode.InventoryDiscard,
            40 => WireOpcode.InventoryDiscardResult,
            41 => WireOpcode.PlayerAttackVisual, _ => 0
        };
        if (opcode == 0) return false;
        bool sizeOk = opcode switch
        {
            WireOpcode.MoveTarget => advertised == MoveTargetFrameBytes,
            WireOpcode.MoveReceipt => advertised == MoveReceiptFrameBytes,
            WireOpcode.NpcInteract => advertised == NpcInteractFrameBytes,
            WireOpcode.NpcInteractResult => advertised == NpcInteractResultFrameBytes,
            WireOpcode.NormalAttack => advertised == NormalAttackFrameBytes,
            WireOpcode.NormalAttackResult => advertised == NormalAttackResultFrameBytes,
            WireOpcode.PlayerCombatState => advertised == PlayerCombatStateFrameBytes,
            WireOpcode.PlayerDamage => advertised == PlayerDamageFrameBytes,
            WireOpcode.AuthRequest => advertised is >= 23 and <= MaxAuthRequestFrameBytes,
            WireOpcode.AuthResult => advertised == AuthResultFrameBytes,
            WireOpcode.CharacterList => advertised is >= 9 and <= MaxCharacterListFrameBytes,
            WireOpcode.CharacterChoose => advertised == CharacterChooseFrameBytes,
            WireOpcode.CharacterChooseResult => advertised == CharacterChooseResultFrameBytes,
            WireOpcode.WorldReady => advertised == WorldReadyFrameBytes,
            WireOpcode.EntitySpawn or WireOpcode.EntityDelta or WireOpcode.SelfState => advertised == EntityStateFrameBytes,
            WireOpcode.EntityDespawn => advertised == EntityDespawnFrameBytes,
            WireOpcode.WorldReadyAck => advertised == WorldReadyAckFrameBytes,
            WireOpcode.WorldResyncRequest => advertised == WorldResyncRequestFrameBytes,
            WireOpcode.MonsterSpawn or WireOpcode.MonsterDelta => advertised == MonsterStateFrameBytes,
            WireOpcode.MonsterDespawn => advertised == MonsterDespawnFrameBytes,
            WireOpcode.PlayerRespawn => advertised == PlayerRespawnFrameBytes,
            WireOpcode.PlayerRespawnResult => advertised == PlayerRespawnResultFrameBytes,
            WireOpcode.GroundDropSpawn => advertised == GroundDropSpawnFrameBytes,
            WireOpcode.GroundDropDespawn => advertised == GroundDropDespawnFrameBytes,
            WireOpcode.GroundPickup => advertised == GroundPickupFrameBytes,
            WireOpcode.GroundPickupResult => advertised == GroundPickupResultFrameBytes,
            WireOpcode.RecoveryUse => advertised == RecoveryUseFrameBytes,
            WireOpcode.RecoveryUseResult => advertised == RecoveryUseResultFrameBytes,
            WireOpcode.InventoryDiscard => advertised == InventoryDiscardFrameBytes,
            WireOpcode.InventoryDiscardResult => advertised == InventoryDiscardResultFrameBytes,
            WireOpcode.PlayerAttackVisual => advertised == PlayerAttackVisualFrameBytes,
            WireOpcode.InventorySnapshot => advertised >= InventorySnapshotHeaderBytes &&
                advertised <= InventorySnapshotHeaderBytes +
                    InventorySnapshotMaxItemsPerFrame*InventoryItemBytes &&
                (advertised-InventorySnapshotHeaderBytes)%InventoryItemBytes==0,
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
        MoveReceiptStatus.RouteBecameBlocked => 10, MoveReceiptStatus.CombatDead => 11,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
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
            10 => MoveReceiptStatus.RouteBecameBlocked, 11 => MoveReceiptStatus.CombatDead,
            _ => (MoveReceiptStatus)(-1)
        };
        return raw <= 11;
    }
}
