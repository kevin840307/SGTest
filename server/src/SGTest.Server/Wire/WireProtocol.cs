using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using System.Buffers.Binary;
using System.Text;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Wire;

// Wire enums and payload DTOs are declared in WireContracts.cs.


/// <summary>
/// Version 1 complete-frame codec. Transport/authenticated transport/socket buffering is separate.
/// Header = uint32 frameBytes + uint16 version + uint16 opcode, little-endian.
/// Identity credentials are allowed only inside the mandatory authenticated transport authorization phase.
/// ActorId and ConnectionEpoch remain server-owned and absent from gameplay commands.
/// </summary>
public static partial class WireProtocol
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
    public const int SkillUseFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int SkillUseResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 4 + 8 + 4 + 4 + 4 + 8;
    public const int PlayerSkillVisualFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 4 + 4 + 8 + 1 + 4 + 8 + 1;
    public const int PlayerAttackVisualFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 4 + 8 + 1 + 4 + 8 + 1;
    public const int PlayerDamageVisualFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 4 + 8 + 8 + 4 + 1 + 1;
    public const int PlayerRespawnVisualFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 4 + 1;
    public const int PlayerCombatStateFrameBytes = HeaderBytes + 8 + 4 + 4 + 4 + 4;
    public const int PlayerDamageFrameBytes = HeaderBytes + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 4 + 1;
    public const int PlayerRespawnFrameBytes = HeaderBytes + 8 + 4;
    public const int PlayerRespawnResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 4 + 4;
    public const int AuthResultFrameBytes = HeaderBytes + 1;
    public const int AccountRegisterResultFrameBytes = HeaderBytes + 1;
    public const int CharacterChooseFrameBytes = HeaderBytes + 8;
    public const int MaxCharacterPasswordBytes = 16;
    public const int MaxCharacterChooseFrameBytes = CharacterChooseFrameBytes + 2 + MaxCharacterPasswordBytes;
    public const int CharacterChooseResultFrameBytes = HeaderBytes + 37;
    public const int CharacterManageMaxFrameBytes = HeaderBytes + 8 + 3 + 2*MaxCharacterPasswordBytes;
    public const int CharacterManageResultFrameBytes = HeaderBytes + 1 + 1 + 8;
    public const int ClanMaxNameBytes = 72;
    public const int ClanCommandHeaderBytes = HeaderBytes + 8 + 4 + 1 + 8 + 2;
    public const int ClanCommandMaximumFrameBytes = ClanCommandHeaderBytes + ClanMaxNameBytes;
    public const int ClanResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 1 + 8;
    public const int ClanSnapshotHeaderBytes = HeaderBytes + 8 + 8 + 4 + 8 + 2 + 1;
    public const int ClanMemberHeaderBytes = 8 + 1 + 2;
    public const int ClanMaximumMembers = 100;
    public const int ClanSnapshotMaximumFrameBytes = ClanSnapshotHeaderBytes + ClanMaxNameBytes + ClanMaximumMembers * (ClanMemberHeaderBytes + PlayerIdentityMaxNameBytes);
    public const int LegacyCharacterAppearanceBytes = 4;
    public const int BodyLegCharacterAppearanceBytes = 6;
    public const int CharacterAppearanceBytes = 7;
    public const int MaxCharacterCreateFrameBytes =
        HeaderBytes + 1 + 2 + 96 + 14 + MaxCharacterPasswordBytes;
    public const int MaxCharacterCreateResultFrameBytes =
        HeaderBytes + 1 + 8 + 1 + 2 + 96 + CharacterAppearanceBytes;
    public const int MaxAuthRequestFrameBytes = HeaderBytes + 4 + 32 + 256;
    public const int MaxAccountRegisterRequestFrameBytes =
        HeaderBytes + 4 + 32 + 256;
    public const int MaxCreationAppearanceChoices = 7000;
    public const int MaxCharacterListFrameBytes = HeaderBytes + 1 + 32 * (11 + 96) +
        3 + 32 * (1 + CharacterAppearanceBytes) +
        MaxCreationAppearanceChoices * CharacterAppearanceBytes;
    // Single-precision coordinates suffice across the original ~15k-pixel maps
    // (sub-0.002px resolution), saving bandwidth vs storing double on wire.
    public const int WorldReadyFrameBytes = 45;
    public const int EntityStateFrameBytes = 74;
    public const int EntityDespawnFrameBytes = 40;
    public const int PlayerMovementIntentFrameBytes = 62;
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
    public const int InventoryItemBytes = 29;
    public const int InventorySnapshotMaxItemsPerFrame = 128;
    public const int InventorySnapshotMaxItems = 4095;
    public const int SkillBookSnapshotHeaderBytes = HeaderBytes + 8 + 4 + 1 + 2;
    public const int SkillBookEntryBytes = 5;
    public const int SkillBookSnapshotMaxSkillsPerFrame = 256;
    public const int SkillBookSnapshotMaxSkills = 4095;
    public const int ShopOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int ShopBuyFrameBytes = HeaderBytes + 8 + 4 + 4 + 4 + 4;
    public const int ShopSellFrameBytes = HeaderBytes + 8 + 4 + 4 + 8 + 4 + 4;
    public const int ShopTransactionResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 4 + 8 + 8 + 4 + 4 + 4;
    public const int ShopSnapshotHeaderBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 8 + 2;
    public const int ShopItemBytes = 20;
    public const int ShopSnapshotMaxItems = 256;
    public const int StorageOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int StorageMoveFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int StorageSnapshotHeaderBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 4 + 1 + 2;
    public const int StorageItemBytes = 29;
    public const int StorageSnapshotMaxItemsPerFrame = 128;
    public const int StorageSnapshotMaxItems = 4095;
    public const int StorageTransferResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 4 + 8 + 4 + 4 + 8;
    public const int EquipmentSnapshotHeaderBytes = HeaderBytes + 8 + 4 + 1 + 2;
    public const int EquipmentItemBytes = 29;
    public const int EquipmentSnapshotMaxItemsPerFrame = 128;
    public const int EquipmentSnapshotMaxItems = 255;
    public const int EquipmentMoveFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int EquipmentTransferResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 8 + 4 + 4 + 8;
    public const int BankOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int BankSnapshotFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 1 + 8 + 8;
    public const int BankMoveFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int BankTransactionResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 4 + 1 + 8 + 8 + 8;
    public const int RepairOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int RepairSnapshotHeaderBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 8 + 4 + 1 + 2;
    public const int RepairPreviewItemBytes = 29;
    public const int RepairSnapshotMaxItemsPerFrame = 128;
    public const int RepairSnapshotMaxItems = 4095;
    public const int RepairItemFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int RepairResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 8 + 4 + 1 + 4 + 4 + 8 + 8;
    public const int EnhancementOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int EnhancementSnapshotHeaderBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 8 + 4 + 1 + 2;
    public const int EnhancementPreviewItemBytes = 28;
    public const int EnhancementSnapshotMaxItemsPerFrame = 128;
    public const int EnhancementSnapshotMaxItems = 4095;
    public const int EnhancementItemFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int EnhancementResultFrameBytes = 70;
    public const int PvpModeCommandFrameBytes = HeaderBytes + 8 + 4 + 1;
    public const int PvpModeResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 1 + 1;
    public const int PvpAttackFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int PvpAttackResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 4 + 1;
    public const int PvpDamageFrameBytes = HeaderBytes + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 4 + 4 + 1;
    public const int PlayerIdentityHeaderBytes =
        HeaderBytes + 8 + 8 + 8 + 8 + 1 + 1 + 8 + 8 + 8 + 2;
    public const int PlayerIdentityMaxNameBytes = 96;
    public const int PlayerIdentityMaximumFrameBytes =
        PlayerIdentityHeaderBytes + PlayerIdentityMaxNameBytes +
        1 + CharacterAppearanceBytes;
    public const int PvpCombatVisualFrameBytes =
        HeaderBytes + 8 + 8 + 8 + 8 + 4 + 8 + 8 + 4 + 4 + 1 + 1 + 1;
    public const int PvpSkillFrameBytes = HeaderBytes + 8 + 4 + 4 + 8;
    public const int PvpSkillResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 8 + 4 + 8 + 4 + 1;
    public const int PvpSkillVisualFrameBytes =
        HeaderBytes + 8 + 8 + 8 + 8 + 4 + 4 + 8 + 8 + 4 + 4 + 1 + 1 + 1;
    public const int GmCapabilityFrameBytes = HeaderBytes + 1;
    public const int GmPvpNameCommandFrameBytes = HeaderBytes + 8 + 4 + 8 + 1;
    public const int GmPvpNameResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 1;
    public const int PlayerShopCommandFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 8 + 8 + 8 + 8;
    public const int PlayerShopResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 8 + 8 + 8 + 4 + 4 + 8 + 4 + 8 + 8 + 8 + 1;
    public const int PlayerShopSoldFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 8;
    public const int ChatCommandHeaderBytes = HeaderBytes + 8 + 4 + 1 + 1 + 2;
    public const int ChatDeliveryHeaderBytes = HeaderBytes + 8 + 8 + 8 + 4 + 1 + 1 + 2 + 2;
    public const int ChatMaxMessageBytes = 256;
    public const int ChatMaxSenderNameBytes = PlayerIdentityMaxNameBytes;
    public const int ChatCommandMaximumFrameBytes = ChatCommandHeaderBytes + ChatMaxMessageBytes;
    public const int ChatDeliveryMaximumFrameBytes =
        ChatDeliveryHeaderBytes + ChatMaxSenderNameBytes + ChatMaxMessageBytes;
    public const int WhisperCommandHeaderBytes = HeaderBytes + 8 + 4 + 8 + 1 + 2;
    public const int WhisperCommandMaximumFrameBytes =
        WhisperCommandHeaderBytes + ChatMaxMessageBytes + 2 + ChatMaxSenderNameBytes;
    public const int WhisperResultHeaderBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 2;
    public const int WhisperResultMaximumFrameBytes =
        WhisperResultHeaderBytes + ChatMaxSenderNameBytes;
    public const int PartyCommandFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8;
    public const int PartyCommandMaximumFrameBytes =
        PartyCommandFrameBytes + PlayerIdentityMaxNameBytes;
    public const int PartyResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 1 + 8 + 8;
    public const int PartyInviteHeaderBytes = HeaderBytes + 8 + 8 + 4 + 8 + 8 + 2;
    public const int PartyInviteMaximumFrameBytes =
        PartyInviteHeaderBytes + PlayerIdentityMaxNameBytes;
    public const int PartySnapshotHeaderBytes = HeaderBytes + 8 + 8 + 4 + 8 + 1;
    public const int PartyMemberHeaderBytes = 8 + 8 + 2;
    public const int PartyMaxMembers = 8;
    public const int PartySnapshotMaximumFrameBytes =
        PartySnapshotHeaderBytes + PartyMaxMembers *
            (PartyMemberHeaderBytes + PlayerIdentityMaxNameBytes);
    public const int QuestOpenFrameBytes = HeaderBytes + 8 + 4 + 4;
    public const int QuestChooseFrameBytes = HeaderBytes + 8 + 4 + 4 + 4 + 4;
    public const int QuestViewHeaderBytes =
        HeaderBytes + 8 + 4 + 1 + 4 + 4 + 4 + 1 + 4 + 4 + 2 + 2 + 1;
    public const int QuestViewMaxTitleBytes = 256;
    public const int QuestViewMaxTextBytes = 4096;
    public const int QuestViewMaxLabelBytes = 512;
    public const int QuestViewMaxChoices = 16;
    public const int QuestViewMaximumFrameBytes =
        QuestViewHeaderBytes + QuestViewMaxTitleBytes + QuestViewMaxTextBytes +
        QuestViewMaxChoices*(4+2+QuestViewMaxLabelBytes);
    public const int TradeCommandFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 8 + 8;
    public const int TradeSnapshotHeaderBytes = 59;
    public const int TradeOfferItemBytes = 29;
    public const int TradeMaxItemsPerSide = 32;
    public const int TradeTerminalFrameBytes = HeaderBytes + 8 + 8 + 4 + 1 + 8;
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

    private static bool ValidPlayerSkillVisual(PlayerSkillVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0 || value.ServerTick<0 ||
            value.AttackerActorId<=0 || value.AttackerConnectionEpoch<=0 ||
            value.SkillSequence==0 || value.SkillId is <1 or >100_000_000 ||
            value.TargetEntityId<=0 || value.TargetRevision<=0 ||
            value.Facing is <1 or >8)
            return false;
        if(value.Status==SkillUseStatus.Applied)
            return value.Damage>0;
        if(value.Status==SkillUseStatus.Miss)
            return value.Damage==0;
        return false;
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

    private static bool ValidPlayerDamageVisual(PlayerDamageVisualWire value) =>
        value.ReceiverMapEpoch>0 && value.ServerTick>=0 &&
        value.VictimActorId>0 && value.VictimConnectionEpoch>0 &&
        value.DamageSequence!=0 && value.AttackerMonsterEntityId>0 &&
        value.AttackerMonsterRevision>0 && value.Damage>0 &&
        value.Facing is >=1 and <=8;

    private static bool ValidPlayerRespawnVisual(PlayerRespawnVisualWire value) =>
        value.ReceiverMapEpoch>0 && value.ServerTick>=0 &&
        value.ActorId>0 && value.ConnectionEpoch>0 &&
        value.RespawnSequence!=0 && value.Facing is >=1 and <=8;

    private static bool ValidPlayerCombatState(PlayerCombatStateWire value) =>
        value.MapEpoch > 0 && value.MaxHp > 0 &&
        value.CurrentHp >= 0 && value.CurrentHp <= value.MaxHp &&
        value.MaxMp >= 0 && value.CurrentMp >= 0 &&
        value.CurrentMp <= value.MaxMp;

    private static bool ValidPlayerDamage(PlayerDamageWire value) =>
        value.MapEpoch > 0 && value.DamageSequence != 0 &&
        value.ServerTick >= 0 && value.AttackerMonsterEntityId > 0 &&
        value.AttackerMonsterRevision > 0 && value.Damage > 0 &&
        value.MaxHp > 0 && value.CurrentHp >= 0 &&
        value.CurrentHp <= value.MaxHp &&
        value.Died == (value.CurrentHp == 0);

    private static bool ValidSkillUseResult(SkillUseResultWire value)
    {
        if(value.MapEpoch<=0 || value.SkillSequence==0 ||
            value.SkillId is <1 or >100_000_000 ||
            (byte)value.Status>(byte)SkillUseStatus.TooManyTargets ||
            value.ServerTick<0 || value.TargetEntityId<0 ||
            value.Damage<0 || value.CurrentHp<0 || value.MaxHp<0 ||
            value.TargetRevision<0)
            return false;
        if(value.MaxHp>0 && value.CurrentHp>value.MaxHp)return false;
        if(value.Status==SkillUseStatus.Applied)
        {
            if(value.TargetEntityId==0)
                return value.Damage==0 && value.CurrentHp==0 &&
                    value.MaxHp==0 && value.TargetRevision==0;
            return value.Damage>0 && value.MaxHp>0 && value.TargetRevision>0;
        }
        if(value.Status==SkillUseStatus.Miss)
            return value.TargetEntityId>0 && value.Damage==0 &&
                value.MaxHp>0 && value.TargetRevision>0;
        if(value.TargetEntityId==0)
            return value.Damage==0 && value.CurrentHp==0 &&
                value.MaxHp==0 && value.TargetRevision==0;
        return value.Damage==0;
    }

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
            15 => WireOpcode.CharacterCreate, 16 => WireOpcode.CharacterCreateResult,
            17 => WireOpcode.AccountRegisterRequest,
            18 => WireOpcode.AccountRegisterResult,
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
            41 => WireOpcode.PlayerAttackVisual,
            42 => WireOpcode.PlayerDamageVisual,
            43 => WireOpcode.PlayerRespawnVisual,
            44 => WireOpcode.SkillUse, 45 => WireOpcode.SkillUseResult,
            46 => WireOpcode.PlayerSkillVisual,
            47 => WireOpcode.ShopOpen, 48 => WireOpcode.ShopSnapshot,
            49 => WireOpcode.ShopBuy, 50 => WireOpcode.ShopSell,
            51 => WireOpcode.ShopTransactionResult,
            52 => WireOpcode.StorageOpen, 53 => WireOpcode.StorageSnapshot,
            54 => WireOpcode.StorageDeposit, 55 => WireOpcode.StorageWithdraw,
            56 => WireOpcode.StorageTransferResult,
            57 => WireOpcode.EquipmentSnapshot, 58 => WireOpcode.EquipmentEquip,
            59 => WireOpcode.EquipmentUnequip, 60 => WireOpcode.EquipmentTransferResult,
            61 => WireOpcode.BankOpen, 62 => WireOpcode.BankSnapshot,
            63 => WireOpcode.BankDeposit, 64 => WireOpcode.BankWithdraw,
            65 => WireOpcode.BankTransactionResult,
            66 => WireOpcode.RepairOpen, 67 => WireOpcode.RepairSnapshot,
            68 => WireOpcode.RepairItem, 69 => WireOpcode.RepairResult,
            70 => WireOpcode.TradeCommand, 71 => WireOpcode.TradeSnapshot,
            72 => WireOpcode.TradeTerminal,
            73 => WireOpcode.QuestOpen, 74 => WireOpcode.QuestView,
            75 => WireOpcode.QuestChoose,
            76 => WireOpcode.PvpModeCommand, 77 => WireOpcode.PvpModeResult,
            78 => WireOpcode.PvpAttack, 79 => WireOpcode.PvpAttackResult,
            80 => WireOpcode.PvpDamage, 81 => WireOpcode.PlayerIdentity,
            82 => WireOpcode.PvpCombatVisual, 83 => WireOpcode.PvpSkill,
            84 => WireOpcode.PvpSkillResult, 85 => WireOpcode.PvpSkillVisual,
            86 => WireOpcode.GmCapability, 87 => WireOpcode.GmPvpNameCommand,
            88 => WireOpcode.GmPvpNameResult,
            89 => WireOpcode.PlayerShopCommand, 90 => WireOpcode.PlayerShopResult,
            91 => WireOpcode.PlayerShopSold,
            92 => WireOpcode.ChatCommand, 93 => WireOpcode.ChatDelivery,
            94 => WireOpcode.EnhancementOpen,
            95 => WireOpcode.EnhancementSnapshot,
            96 => WireOpcode.EnhancementItem,
            97 => WireOpcode.EnhancementResult,
            98 => WireOpcode.WhisperCommand,
            99 => WireOpcode.WhisperResult,
            100 => WireOpcode.PartyCommand,
            101 => WireOpcode.PartyResult,
            102 => WireOpcode.PartyInvite,
            103 => WireOpcode.PartySnapshot,
            104 => WireOpcode.SkillBookSnapshot,
            105 => WireOpcode.CharacterManage,
            106 => WireOpcode.CharacterManageResult,
            107 => WireOpcode.ClanCommand,
            108 => WireOpcode.ClanResult,
            109 => WireOpcode.ClanSnapshot,
            110 => WireOpcode.PlayerMovementIntent,
            111 => WireOpcode.PetCommand,
            112 => WireOpcode.PetResult,
            113 => WireOpcode.PetSnapshot,
            114 => WireOpcode.PetEntitySpawn,
            115 => WireOpcode.PetEntityDespawn,
            116 => WireOpcode.PetEntityDelta,
            117 => WireOpcode.PetAttack,
            118 => WireOpcode.PetAttackResult,
            119 => WireOpcode.PetAttackVisual,
            _ => 0
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
            WireOpcode.AccountRegisterRequest =>
                advertised is >= 23 and <= MaxAccountRegisterRequestFrameBytes,
            WireOpcode.AccountRegisterResult =>
                advertised == AccountRegisterResultFrameBytes,
            WireOpcode.CharacterList => advertised is >= 9 and <= MaxCharacterListFrameBytes,
            WireOpcode.CharacterChoose => advertised == CharacterChooseFrameBytes ||
                advertised is >= CharacterChooseFrameBytes + 2 and <= MaxCharacterChooseFrameBytes,
            WireOpcode.CharacterChooseResult => advertised == CharacterChooseResultFrameBytes,
            WireOpcode.CharacterManage => advertised >= HeaderBytes+11 && advertised <= CharacterManageMaxFrameBytes,
            WireOpcode.CharacterManageResult => advertised == CharacterManageResultFrameBytes,
            WireOpcode.ClanCommand => advertised >= ClanCommandHeaderBytes && advertised <= ClanCommandMaximumFrameBytes,
            WireOpcode.ClanResult => advertised == ClanResultFrameBytes,
            WireOpcode.ClanSnapshot => advertised >= ClanSnapshotHeaderBytes && advertised <= ClanSnapshotMaximumFrameBytes,
            WireOpcode.PetCommand => advertised == PetCommandFrameBytes,
            WireOpcode.PetResult => advertised == PetResultFrameBytes,
            WireOpcode.PetSnapshot => advertised >= PetSnapshotHeaderBytes &&
                advertised <= PetSnapshotMaximumFrameBytes &&
                (advertised-PetSnapshotHeaderBytes)%PetSnapshotEntryBytes==0,
            WireOpcode.PetEntitySpawn => advertised == PetEntitySpawnFrameBytes,
            WireOpcode.PetEntityDespawn => advertised == PetEntityDespawnFrameBytes,
            WireOpcode.PetEntityDelta => advertised == PetEntityDeltaFrameBytes,
            WireOpcode.PetAttack => advertised == PetAttackFrameBytes,
            WireOpcode.PetAttackResult => advertised == PetAttackResultFrameBytes,
            WireOpcode.PetAttackVisual => advertised == PetAttackVisualFrameBytes,
            WireOpcode.CharacterCreate => advertised is >= 12 and <= MaxCharacterCreateFrameBytes,
            WireOpcode.CharacterCreateResult => advertised is >= 20 and <= MaxCharacterCreateResultFrameBytes,
            WireOpcode.WorldReady => advertised == WorldReadyFrameBytes,
            WireOpcode.EntitySpawn or WireOpcode.EntityDelta or WireOpcode.SelfState => advertised == EntityStateFrameBytes,
            WireOpcode.EntityDespawn => advertised == EntityDespawnFrameBytes,
            WireOpcode.PlayerMovementIntent => advertised == PlayerMovementIntentFrameBytes,
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
            WireOpcode.PlayerDamageVisual => advertised == PlayerDamageVisualFrameBytes,
            WireOpcode.PlayerRespawnVisual => advertised == PlayerRespawnVisualFrameBytes,
            WireOpcode.SkillUse => advertised == SkillUseFrameBytes,
            WireOpcode.SkillUseResult => advertised == SkillUseResultFrameBytes,
            WireOpcode.PlayerSkillVisual => advertised == PlayerSkillVisualFrameBytes,
            WireOpcode.ShopOpen => advertised == ShopOpenFrameBytes,
            WireOpcode.ShopSnapshot => advertised >= ShopSnapshotHeaderBytes &&
                advertised <= ShopSnapshotHeaderBytes + ShopSnapshotMaxItems * ShopItemBytes &&
                (advertised - ShopSnapshotHeaderBytes) % ShopItemBytes == 0,
            WireOpcode.ShopBuy => advertised == ShopBuyFrameBytes,
            WireOpcode.ShopSell => advertised == ShopSellFrameBytes,
            WireOpcode.ShopTransactionResult => advertised == ShopTransactionResultFrameBytes,
            WireOpcode.StorageOpen => advertised == StorageOpenFrameBytes,
            WireOpcode.StorageSnapshot => advertised >= StorageSnapshotHeaderBytes &&
                advertised <= StorageSnapshotHeaderBytes +
                    StorageSnapshotMaxItemsPerFrame*StorageItemBytes &&
                (advertised-StorageSnapshotHeaderBytes)%StorageItemBytes==0,
            WireOpcode.StorageDeposit or WireOpcode.StorageWithdraw =>
                advertised == StorageMoveFrameBytes,
            WireOpcode.StorageTransferResult =>
                advertised == StorageTransferResultFrameBytes,
            WireOpcode.EquipmentSnapshot => advertised >= EquipmentSnapshotHeaderBytes &&
                advertised <= EquipmentSnapshotHeaderBytes +
                    EquipmentSnapshotMaxItemsPerFrame*EquipmentItemBytes &&
                (advertised-EquipmentSnapshotHeaderBytes)%EquipmentItemBytes==0,
            WireOpcode.EquipmentEquip or WireOpcode.EquipmentUnequip =>
                advertised == EquipmentMoveFrameBytes,
            WireOpcode.EquipmentTransferResult =>
                advertised == EquipmentTransferResultFrameBytes,
            WireOpcode.BankOpen => advertised == BankOpenFrameBytes,
            WireOpcode.BankSnapshot => advertised == BankSnapshotFrameBytes,
            WireOpcode.BankDeposit or WireOpcode.BankWithdraw =>
                advertised == BankMoveFrameBytes,
            WireOpcode.BankTransactionResult =>
                advertised == BankTransactionResultFrameBytes,
            WireOpcode.RepairOpen => advertised == RepairOpenFrameBytes,
            WireOpcode.RepairSnapshot => advertised >= RepairSnapshotHeaderBytes &&
                advertised <= RepairSnapshotHeaderBytes +
                    RepairSnapshotMaxItemsPerFrame*RepairPreviewItemBytes &&
                (advertised-RepairSnapshotHeaderBytes)%RepairPreviewItemBytes==0,
            WireOpcode.RepairItem => advertised == RepairItemFrameBytes,
            WireOpcode.RepairResult => advertised == RepairResultFrameBytes,
            WireOpcode.EnhancementOpen => advertised == EnhancementOpenFrameBytes,
            WireOpcode.EnhancementSnapshot =>
                advertised >= EnhancementSnapshotHeaderBytes &&
                advertised <= EnhancementSnapshotHeaderBytes +
                    EnhancementSnapshotMaxItemsPerFrame*EnhancementPreviewItemBytes &&
                (advertised-EnhancementSnapshotHeaderBytes)%
                    EnhancementPreviewItemBytes==0,
            WireOpcode.EnhancementItem => advertised == EnhancementItemFrameBytes,
            WireOpcode.EnhancementResult => advertised == EnhancementResultFrameBytes,
            WireOpcode.TradeCommand => advertised == TradeCommandFrameBytes,
            WireOpcode.TradeSnapshot => advertised >= TradeSnapshotHeaderBytes &&
                advertised <= TradeSnapshotHeaderBytes +
                    2*TradeMaxItemsPerSide*TradeOfferItemBytes &&
                (advertised-TradeSnapshotHeaderBytes)%TradeOfferItemBytes==0,
            WireOpcode.TradeTerminal => advertised == TradeTerminalFrameBytes,
            WireOpcode.QuestOpen => advertised == QuestOpenFrameBytes,
            WireOpcode.QuestView => advertised >= QuestViewHeaderBytes &&
                advertised <= QuestViewMaximumFrameBytes,
            WireOpcode.QuestChoose => advertised == QuestChooseFrameBytes,
            WireOpcode.PvpModeCommand => advertised == PvpModeCommandFrameBytes,
            WireOpcode.PvpModeResult => advertised == PvpModeResultFrameBytes,
            WireOpcode.PvpAttack => advertised == PvpAttackFrameBytes,
            WireOpcode.PvpAttackResult => advertised == PvpAttackResultFrameBytes,
            WireOpcode.PvpDamage => advertised == PvpDamageFrameBytes,
            WireOpcode.PlayerIdentity =>
                advertised > PlayerIdentityHeaderBytes &&
                advertised <= PlayerIdentityMaximumFrameBytes,
            WireOpcode.PvpCombatVisual => advertised == PvpCombatVisualFrameBytes,
            WireOpcode.PvpSkill => advertised == PvpSkillFrameBytes,
            WireOpcode.PvpSkillResult => advertised == PvpSkillResultFrameBytes,
            WireOpcode.PvpSkillVisual => advertised == PvpSkillVisualFrameBytes,
            WireOpcode.GmCapability => advertised == GmCapabilityFrameBytes,
            WireOpcode.GmPvpNameCommand => advertised == GmPvpNameCommandFrameBytes,
            WireOpcode.GmPvpNameResult => advertised == GmPvpNameResultFrameBytes,
            WireOpcode.PlayerShopCommand => advertised == PlayerShopCommandFrameBytes,
            WireOpcode.PlayerShopResult => advertised == PlayerShopResultFrameBytes,
            WireOpcode.PlayerShopSold => advertised == PlayerShopSoldFrameBytes,
            WireOpcode.ChatCommand => advertised >= ChatCommandHeaderBytes + 1 && advertised <= ChatCommandMaximumFrameBytes,
            WireOpcode.ChatDelivery => advertised >= ChatDeliveryHeaderBytes + 2 && advertised <= ChatDeliveryMaximumFrameBytes,
            WireOpcode.WhisperCommand =>
                advertised >= WhisperCommandHeaderBytes+1 &&
                advertised <= WhisperCommandMaximumFrameBytes,
            WireOpcode.WhisperResult =>
                advertised >= WhisperResultHeaderBytes &&
                advertised <= WhisperResultMaximumFrameBytes,
            WireOpcode.PartyCommand =>
                advertised >= PartyCommandFrameBytes &&
                advertised <= PartyCommandMaximumFrameBytes,
            WireOpcode.PartyResult => advertised == PartyResultFrameBytes,
            WireOpcode.PartyInvite =>
                advertised >= PartyInviteHeaderBytes+1 &&
                advertised <= PartyInviteMaximumFrameBytes,
            WireOpcode.PartySnapshot =>
                advertised >= PartySnapshotHeaderBytes &&
                advertised <= PartySnapshotMaximumFrameBytes,
            WireOpcode.InventorySnapshot => advertised >= InventorySnapshotHeaderBytes &&
                advertised <= InventorySnapshotHeaderBytes +
                    InventorySnapshotMaxItemsPerFrame*InventoryItemBytes &&
                (advertised-InventorySnapshotHeaderBytes)%InventoryItemBytes==0,
            WireOpcode.SkillBookSnapshot =>
                advertised >= SkillBookSnapshotHeaderBytes &&
                advertised <= SkillBookSnapshotHeaderBytes +
                    SkillBookSnapshotMaxSkillsPerFrame*SkillBookEntryBytes &&
                (advertised-SkillBookSnapshotHeaderBytes)%SkillBookEntryBytes==0,
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
