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
    CharacterCreate = 15,
    CharacterCreateResult = 16,
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
    PlayerAttackVisual = 41,
    PlayerDamageVisual = 42,
    PlayerRespawnVisual = 43,
    SkillUse = 44,
    SkillUseResult = 45,
    PlayerSkillVisual = 46,
    ShopOpen = 47,
    ShopSnapshot = 48,
    ShopBuy = 49,
    ShopSell = 50,
    ShopTransactionResult = 51,
    StorageOpen = 52,
    StorageSnapshot = 53,
    StorageDeposit = 54,
    StorageWithdraw = 55,
    StorageTransferResult = 56,
    EquipmentSnapshot = 57,
    EquipmentEquip = 58,
    EquipmentUnequip = 59,
    EquipmentTransferResult = 60,
    BankOpen = 61,
    BankSnapshot = 62,
    BankDeposit = 63,
    BankWithdraw = 64,
    BankTransactionResult = 65,
    RepairOpen = 66,
    RepairSnapshot = 67,
    RepairItem = 68,
    RepairResult = 69,
    TradeCommand = 70,
    TradeSnapshot = 71,
    TradeTerminal = 72,
    QuestOpen = 73,
    QuestView = 74,
    QuestChoose = 75,
    PvpModeCommand = 76,
    PvpModeResult = 77,
    PvpAttack = 78,
    PvpAttackResult = 79,
    PvpDamage = 80,
    PlayerIdentity = 81,
    PvpCombatVisual = 82,
    PvpSkill = 83,
    PvpSkillResult = 84,
    PvpSkillVisual = 85,
    GmCapability = 86,
    GmPvpNameCommand = 87,
    GmPvpNameResult = 88,
    PlayerShopCommand = 89,
    PlayerShopResult = 90,
    PlayerShopSold = 91,
    ChatCommand = 92,
    ChatDelivery = 93
}

public enum ChatChannel : byte { Nearby = 0, Faction = 1, Warband = 2, Party = 3, Whisper = 4 }

public enum TradeCommandAction : byte
{
    Request = 1, Accept = 2, Reject = 3, AddItem = 4, RemoveItem = 5,
    SetMoney = 6, Lock = 7, FinalConfirm = 8, Cancel = 9
}
public enum TradeSessionWireState : byte
{
    InviteReceived = 1, InviteSent = 2, Active = 3, AwaitingFinalConfirm = 4
}
public enum TradeTerminalStatus : byte
{
    Success = 0, Rejected = 1, Cancelled = 2, Expired = 3,
    PeerDisconnected = 4, CommitFailed = 5, StaleGeneration = 6
}
public enum AuthWireStatus : byte { Success = 0, InvalidCredentials = 1, Disabled = 2 }
public enum CharacterChooseWireStatus : byte { Success = 0, NotOwned = 1, WorldRejected = 2 }
public enum CharacterCreateWireStatus : byte
{
    Success = 0,
    SlotOccupied = 1,
    NameTaken = 2,
    LimitReached = 3,
    InvalidName = 4,
    InvalidSlot = 5,
    Unavailable = 6
}
public enum PlayerRespawnStatus : byte
{
    Accepted = 0,
    NotDead = 1,
    CombatUnavailable = 2,
    StaleGeneration = 3,
    ReplayedSequence = 4
}

public enum ShopOpenStatus : byte
{
    Opened = 0,
    StaleGeneration = 1,
    UnknownShop = 2,
    TooFar = 3,
    PlayerDead = 4
}

public enum StorageOpenStatus : byte
{
    Opened = 0,
    StaleGeneration = 1,
    UnknownStorage = 2,
    TooFar = 3,
    PlayerDead = 4
}

public enum BankOpenStatus : byte
{
    Opened = 0,
    StaleGeneration = 1,
    UnknownBank = 2,
    TooFar = 3,
    PlayerDead = 4
}

public enum RepairOpenStatus : byte
{
    Opened = 0,
    StaleGeneration = 1,
    UnknownRepair = 2,
    TooFar = 3,
    PlayerDead = 4
}

public enum PvpModeCommandAction : byte { Query = 0, SetBlue = 1, SetWhite = 2 }
public enum PvpModeResultStatus : byte { Ready = 0, Applied = 1, Unchanged = 2, NonPvpMap = 3, StaleGeneration = 4, PlayerDead = 5, StaleSequence = 6 }
public enum PvpAttackStatus : byte
{
    Applied = 0, StaleGeneration = 1, StaleSequence = 2, SelfTarget = 3,
    NonPvpMap = 4, AttackerProtected = 5, TargetProtected = 6,
    TargetMissing = 7, AttackerDead = 8, TargetDead = 9, TooFar = 10,
    Blocked = 11, Cooldown = 12, CombatUnavailable = 13, Backpressured = 14
}

public enum GmPvpNameResultStatus : byte
{
    Applied = 0,
    Replayed = 1,
    Unauthorized = 2,
    StaleGeneration = 3,
    StaleSequence = 4,
    TargetOffline = 5
}

public enum PlayerShopCommandAction : byte
{
    Open = 1,
    Close = 2,
    Inspect = 3,
    Buy = 4
}

public enum PlayerShopResultStatus : byte
{
    Applied = 0,
    AlreadyApplied = 1,
    NoShop = 2,
    NotVisible = 3,
    StaleGeneration = 4,
    PlayerDead = 5,
    AlreadyOpen = 6,
    NotOwned = 7,
    NotTradeable = 8,
    SelfPurchase = 9,
    InsufficientMoney = 10,
    WalletLimit = 11,
    Sold = 12,
    Closed = 13,
    StaleListing = 14,
    StaleSequence = 15
}

public enum QuestViewWireStatus : byte
{
    Ready = 0,
    Applied = 1,
    AlreadyApplied = 2,
    StaleGeneration = 3,
    UnknownQuest = 4,
    TooFar = 5,
    InvalidChoice = 6,
    AlreadyCompleted = 7,
    StaleSequence = 8,
    PlayerDead = 9,
    RuleRevisionMismatch = 10
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
public readonly record struct SkillUseWire(
    long MapEpoch,uint SkillSequence,int SkillId,long TargetEntityId);
public readonly record struct SkillUseResultWire(
    long MapEpoch,uint SkillSequence,SkillUseStatus Status,long ServerTick,
    int SkillId,long TargetEntityId,int Damage,int CurrentHp,int MaxHp,
    long TargetRevision);
public readonly record struct PlayerSkillVisualWire(
    long ReceiverMapEpoch,long ServerTick,long AttackerActorId,
    long AttackerConnectionEpoch,uint SkillSequence,int SkillId,
    long TargetEntityId,SkillUseStatus Status,int Damage,
    long TargetRevision,byte Facing);
public readonly record struct PlayerAttackVisualWire(
    long ReceiverMapEpoch,long ServerTick,long AttackerActorId,
    long AttackerConnectionEpoch,uint AttackSequence,long TargetEntityId,
    NormalAttackStatus Status,int Damage,long TargetRevision,byte Facing);
public readonly record struct PlayerDamageVisualWire(
    long ReceiverMapEpoch,long ServerTick,long VictimActorId,
    long VictimConnectionEpoch,uint DamageSequence,
    long AttackerMonsterEntityId,long AttackerMonsterRevision,
    int Damage,bool Died,byte Facing);
public readonly record struct PlayerRespawnVisualWire(
    long ReceiverMapEpoch,long ServerTick,long ActorId,
    long ConnectionEpoch,uint RespawnSequence,byte Facing);
public readonly record struct PlayerCombatStateWire(
    long MapEpoch, int CurrentHp, int MaxHp, int CurrentMp, int MaxMp);
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
public readonly record struct CharacterCreateWire(byte Slot, string Name);
public readonly record struct CharacterCreateResultWire(
    CharacterCreateWireStatus Status, long CharacterId, byte Slot, string Name);
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
public readonly record struct ShopOpenWire(
    long MapEpoch,uint ShopSequence,int NpcSpawnId);
public readonly record struct ShopItemWire(
    int ItemDefinitionId,long BuyPrice,long SellPrice);
public sealed record ShopSnapshotWire(
    long MapEpoch,uint ShopSequence,ShopOpenStatus Status,
    int NpcSpawnId,long Money,IReadOnlyList<ShopItemWire> Items);
public readonly record struct ShopBuyWire(
    long MapEpoch,uint ShopSequence,int NpcSpawnId,
    int ItemDefinitionId,int Quantity);
public readonly record struct ShopSellWire(
    long MapEpoch,uint ShopSequence,int NpcSpawnId,
    long ItemUid,int ItemDefinitionId,int Quantity);
public readonly record struct ShopTransactionResultWire(
    long MapEpoch,uint ShopSequence,ShopTransactionKind Kind,
    ShopTransactionStatus Status,int NpcSpawnId,long Money,
    long ItemUid,int ItemDefinitionId,int Quantity,int RemainingQuantity);

public readonly record struct StorageOpenWire(
    long MapEpoch,uint StorageSequence,int NpcSpawnId);
public readonly record struct StorageItemWire(
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId);
public sealed record StorageSnapshotWire(
    long MapEpoch,uint StorageSequence,StorageOpenStatus Status,
    int NpcSpawnId,int StartIndex,bool Complete,
    IReadOnlyList<StorageItemWire> Items);
public readonly record struct StorageMoveWire(
    long MapEpoch,uint StorageSequence,int NpcSpawnId,long ItemUid);
public readonly record struct StorageTransferResultWire(
    long MapEpoch,uint StorageSequence,StorageTransferKind Kind,
    StorageTransferStatus Status,int NpcSpawnId,long ItemUid,
    int ItemDefinitionId,int Quantity,long SourceDropId);

public readonly record struct EquipmentItemWire(
    long ItemUid,int SlotId,int ItemDefinitionId,long SourceDropId);
public sealed record EquipmentSnapshotWire(
    long CharacterId,int StartIndex,bool Complete,
    IReadOnlyList<EquipmentItemWire> Items);
public readonly record struct EquipmentMoveWire(
    long MapEpoch,uint EquipmentSequence,long ItemUid);
public readonly record struct EquipmentTransferResultWire(
    long MapEpoch,uint EquipmentSequence,EquipmentTransferKind Kind,
    EquipmentTransferStatus Status,long ItemUid,
    int ItemDefinitionId,int SlotId,long SourceDropId);

public readonly record struct BankOpenWire(
    long MapEpoch,uint BankSequence,int NpcSpawnId);
public readonly record struct BankSnapshotWire(
    long MapEpoch,uint BankSequence,BankOpenStatus Status,int NpcSpawnId,
    BankBalanceScope Scope,long WalletMoney,long BankMoney);
public readonly record struct BankMoveWire(
    long MapEpoch,uint BankSequence,int NpcSpawnId,long Amount);
public readonly record struct BankTransactionResultWire(
    long MapEpoch,uint BankSequence,BankTransactionKind Kind,
    BankTransactionStatus Status,int NpcSpawnId,BankBalanceScope Scope,
    long Amount,long WalletMoney,long BankMoney);

public readonly record struct RepairOpenWire(
    long MapEpoch,uint RepairSequence,int NpcSpawnId);
public readonly record struct RepairPreviewItemWire(
    long ItemUid,int ItemDefinitionId,RepairItemLocation Location,
    int CurrentDurability,int MaxDurability,long Cost);
public sealed record RepairSnapshotWire(
    long MapEpoch,uint RepairSequence,RepairOpenStatus Status,int NpcSpawnId,
    long WalletMoney,int StartIndex,bool Complete,
    IReadOnlyList<RepairPreviewItemWire> Items);
public readonly record struct RepairItemWire(
    long MapEpoch,uint RepairSequence,int NpcSpawnId,long ItemUid);
public readonly record struct RepairResultWire(
    long MapEpoch,uint RepairSequence,RepairStatus Status,int NpcSpawnId,
    long ItemUid,int ItemDefinitionId,RepairItemLocation Location,
    int CurrentDurability,int MaxDurability,long Cost,long WalletMoney);

public readonly record struct TradeCommandWire(
    long MapEpoch,uint TradeSequence,TradeCommandAction Action,
    long TradeId,long TargetActorId,long ItemUid,long Money);
public readonly record struct TradeOfferItemWire(
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId);
public sealed record TradeSnapshotWire(
    long MapEpoch,long TradeId,uint Revision,TradeSessionWireState State,
    long PeerActorId,long MyMoney,long OtherMoney,
    bool MyLocked,bool OtherLocked,bool MyConfirmed,bool OtherConfirmed,
    IReadOnlyList<TradeOfferItemWire> MyItems,
    IReadOnlyList<TradeOfferItemWire> OtherItems);
public readonly record struct TradeTerminalWire(
    long MapEpoch,long TradeId,uint Revision,TradeTerminalStatus Status,
    long WalletMoney);

public readonly record struct PvpModeCommandWire(long MapEpoch,uint PvpSequence,PvpModeCommandAction Action);
public readonly record struct PvpModeResultWire(long MapEpoch,uint PvpSequence,PvpModeResultStatus Status,PvpMode Mode,bool MapAllowsPlayerPvp);
public readonly record struct PvpAttackWire(long MapEpoch,uint AttackSequence,long TargetActorId);
public readonly record struct PvpAttackResultWire(
    long MapEpoch,uint AttackSequence,PvpAttackStatus Status,long ServerTick,
    long TargetActorId,int Damage,bool Died);
public readonly record struct PvpDamageWire(
    long MapEpoch,uint DamageSequence,long ServerTick,
    long AttackerActorId,long AttackerConnectionEpoch,uint AttackerAttackSequence,
    int Damage,int CurrentHp,int MaxHp,bool Died);
public readonly record struct PlayerIdentityWire(
    long ReceiverMapEpoch,long ActorId,long ConnectionEpoch,long EntityMapEpoch,
    PvpMode Mode,bool RedMan,long ServerUnixMilliseconds,
    long PinkUntilUnixMilliseconds,long PvpUpdatedUnixMilliseconds,string Name);
public readonly record struct PvpCombatVisualWire(
    long ReceiverMapEpoch,long ServerTick,
    long AttackerActorId,long AttackerConnectionEpoch,uint AttackSequence,
    long VictimActorId,long VictimConnectionEpoch,uint DamageSequence,
    int Damage,bool Died,byte AttackerFacing,byte VictimFacing);
public readonly record struct PvpSkillWire(
    long MapEpoch,uint SkillSequence,int SkillId,long TargetActorId);
public readonly record struct PvpSkillResultWire(
    long MapEpoch,uint SkillSequence,SkillUseStatus Status,long ServerTick,
    int SkillId,long TargetActorId,int Damage,bool Died);
public readonly record struct PvpSkillVisualWire(
    long ReceiverMapEpoch,long ServerTick,
    long AttackerActorId,long AttackerConnectionEpoch,uint SkillSequence,
    int SkillId,long VictimActorId,long VictimConnectionEpoch,
    uint DamageSequence,int Damage,bool Died,
    byte AttackerFacing,byte VictimFacing);
public readonly record struct GmCapabilityWire(bool Authorized);
public readonly record struct GmPvpNameCommandWire(
    long MapEpoch,uint GmSequence,long TargetActorId,PvpNameState State);
public readonly record struct GmPvpNameResultWire(
    long MapEpoch,uint GmSequence,GmPvpNameResultStatus Status,
    long TargetActorId,PvpNameState State);
public readonly record struct PlayerShopCommandWire(
    long MapEpoch,uint ShopSequence,PlayerShopCommandAction Action,
    long SellerActorId,long ListingId,long ItemUid,long Price);
public readonly record struct PlayerShopResultWire(
    long MapEpoch,uint ShopSequence,PlayerShopCommandAction Action,
    PlayerShopResultStatus Status,long SellerActorId,long ListingId,
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId,
    int DurabilityRaw,long Price,long SellerWallet,long BuyerWallet);
public readonly record struct PlayerShopSoldWire(
    long ReceiverMapEpoch,long ListingId,long BuyerActorId,long ItemUid,
    long SellerWallet);
public readonly record struct ChatCommandWire(
    long MapEpoch,uint ChatSequence,ChatChannel Channel,byte BubbleType,string Message);
public readonly record struct ChatDeliveryWire(
    long ReceiverMapEpoch,long SenderActorId,long SenderConnectionEpoch,
    uint ChatSequence,ChatChannel Channel,byte BubbleType,
    string SenderName,string Message);

public readonly record struct QuestOpenWire(
    long MapEpoch,uint QuestSequence,int NpcSpawnId);
public readonly record struct QuestChooseWire(
    long MapEpoch,uint QuestSequence,int NpcSpawnId,int QuestId,int ChoiceId);
public readonly record struct QuestViewChoiceWire(int ChoiceId,string Label);
public sealed record QuestViewWire(
    long MapEpoch,uint QuestSequence,QuestViewWireStatus Status,int NpcSpawnId,
    int QuestId,int RuleRevision,QuestProgressState State,int Stage,
    int ProgressRevision,string Title,string Text,
    IReadOnlyList<QuestViewChoiceWire> Choices);

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
    public const int CharacterChooseFrameBytes = HeaderBytes + 8;
    public const int CharacterChooseResultFrameBytes = HeaderBytes + 37;
    public const int MaxCharacterCreateFrameBytes = HeaderBytes + 1 + 2 + 96;
    public const int MaxCharacterCreateResultFrameBytes = HeaderBytes + 1 + 8 + 1 + 2 + 96;
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
    public const int StorageItemBytes = 24;
    public const int StorageSnapshotMaxItemsPerFrame = 128;
    public const int StorageSnapshotMaxItems = 4095;
    public const int StorageTransferResultFrameBytes =
        HeaderBytes + 8 + 4 + 1 + 1 + 4 + 8 + 4 + 4 + 8;
    public const int EquipmentSnapshotHeaderBytes = HeaderBytes + 8 + 4 + 1 + 2;
    public const int EquipmentItemBytes = 24;
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
    public const int PvpModeCommandFrameBytes = HeaderBytes + 8 + 4 + 1;
    public const int PvpModeResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 1 + 1;
    public const int PvpAttackFrameBytes = HeaderBytes + 8 + 4 + 8;
    public const int PvpAttackResultFrameBytes = HeaderBytes + 8 + 4 + 1 + 8 + 8 + 4 + 1;
    public const int PvpDamageFrameBytes = HeaderBytes + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 4 + 4 + 1;
    public const int PlayerIdentityHeaderBytes =
        HeaderBytes + 8 + 8 + 8 + 8 + 1 + 1 + 8 + 8 + 8 + 2;
    public const int PlayerIdentityMaxNameBytes = 96;
    public const int PlayerIdentityMaximumFrameBytes =
        PlayerIdentityHeaderBytes + PlayerIdentityMaxNameBytes;
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
        HeaderBytes + 8 + 4 + 1 + 1 + 8 + 8 + 8 + 4 + 4 + 8 + 4 + 8 + 8 + 8;
    public const int PlayerShopSoldFrameBytes = HeaderBytes + 8 + 8 + 8 + 8 + 8;
    public const int ChatCommandHeaderBytes = HeaderBytes + 8 + 4 + 1 + 1 + 2;
    public const int ChatDeliveryHeaderBytes = HeaderBytes + 8 + 8 + 8 + 4 + 1 + 1 + 2 + 2;
    public const int ChatMaxMessageBytes = 256;
    public const int ChatMaxSenderNameBytes = PlayerIdentityMaxNameBytes;
    public const int ChatCommandMaximumFrameBytes = ChatCommandHeaderBytes + ChatMaxMessageBytes;
    public const int ChatDeliveryMaximumFrameBytes =
        ChatDeliveryHeaderBytes + ChatMaxSenderNameBytes + ChatMaxMessageBytes;
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
    public const int TradeOfferItemBytes = 24;
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

    public static byte[] EncodeSkillUse(SkillUseWire value)
    {
        if(value.MapEpoch<=0 || value.SkillSequence==0 ||
            value.SkillId is <1 or >100_000_000 || value.TargetEntityId<0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.SkillUse,SkillUseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.TargetEntityId);
        return frame;
    }

    public static bool TryDecodeSkillUse(
        ReadOnlySpan<byte> frame,out SkillUseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.SkillUse)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0 && value.SkillSequence>0 &&
            value.SkillId is >0 and <=100_000_000 && value.TargetEntityId>=0;
    }

    public static byte[] EncodeSkillUseResult(SkillUseResultWire value)
    {
        if(!ValidSkillUseResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.SkillUseResult,SkillUseResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(33,8),value.TargetEntityId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(45,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(49,4),value.MaxHp);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(53,8),value.TargetRevision);
        return frame;
    }

    public static bool TryDecodeSkillUseResult(
        ReadOnlySpan<byte> frame,out SkillUseResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var header) ||
            header.Opcode!=WireOpcode.SkillUseResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (SkillUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(33,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(45,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(49,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(53,8)));
        return ValidSkillUseResult(value);
    }

    public static byte[] EncodePlayerSkillVisual(PlayerSkillVisualWire value)
    {
        if(!ValidPlayerSkillVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerSkillVisual,PlayerSkillVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.TargetEntityId);
        frame[56]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(57,4),value.Damage);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(61,8),value.TargetRevision);
        frame[69]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerSkillVisual(
        ReadOnlySpan<byte> frame,out PlayerSkillVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerSkillVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            (SkillUseStatus)frame[56],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(57,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(61,8)),
            frame[69]);
        return ValidPlayerSkillVisual(value);
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

    public static byte[] EncodePlayerDamageVisual(PlayerDamageVisualWire value)
    {
        if(!ValidPlayerDamageVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerDamageVisual,PlayerDamageVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.AttackerMonsterEntityId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.AttackerMonsterRevision);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(60,4),value.Damage);
        frame[64]=value.Died?(byte)1:(byte)0;
        frame[65]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerDamageVisual(
        ReadOnlySpan<byte> frame,out PlayerDamageVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerDamageVisual)
            return false;
        byte died=frame[64];
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(60,4)),
            died==1,frame[65]);
        return died<=1 && ValidPlayerDamageVisual(value);
    }

    public static byte[] EncodePlayerRespawnVisual(PlayerRespawnVisualWire value)
    {
        if(!ValidPlayerRespawnVisual(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerRespawnVisual,PlayerRespawnVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.ConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.RespawnSequence);
        frame[44]=value.Facing;
        return frame;
    }

    public static bool TryDecodePlayerRespawnVisual(
        ReadOnlySpan<byte> frame,out PlayerRespawnVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h) || h.Opcode!=WireOpcode.PlayerRespawnVisual)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            frame[44]);
        return ValidPlayerRespawnVisual(value);
    }

    public static byte[] EncodePlayerCombatState(PlayerCombatStateWire value)
    {
        if (!ValidPlayerCombatState(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame = NewFrame(WireOpcode.PlayerCombatState, PlayerCombatStateFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8, 8), value.MapEpoch);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16, 4), value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20, 4), value.MaxHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24, 4), value.CurrentMp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28, 4), value.MaxMp);
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
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28, 4)));
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
               item.Quantity is <1 or >1_000_000 || item.SourceDropId<0 ||
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
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
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


    public static byte[] EncodeShopOpen(ShopOpenWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopOpen,ShopOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeShopOpen(ReadOnlySpan<byte> frame,out ShopOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeShopSnapshot(ShopSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.Money<0||value.Money>SqliteIdentityStore.MaximumMoney||
           value.Items is null||value.Items.Count>ShopSnapshotMaxItems||
           (byte)value.Status>(byte)ShopOpenStatus.PlayerDead)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status==ShopOpenStatus.Opened && value.Items.Count==0)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status!=ShopOpenStatus.Opened &&
           (value.Money!=0||value.Items.Count!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var ids=new HashSet<int>();
        foreach(var item in value.Items)
            if(item.ItemDefinitionId is <1 or >100_000_000 ||
               item.BuyPrice is <1 or >1_000_000_000_000L ||
               item.SellPrice<0||item.SellPrice>item.BuyPrice||
               !ids.Add(item.ItemDefinitionId))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopSnapshot,
            ShopSnapshotHeaderBytes+value.Items.Count*ShopItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.Money);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(33,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=ShopSnapshotHeaderBytes+i*ShopItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt32LittleEndian(
                frame.AsSpan(offset,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset+4,8),item.BuyPrice);
            BinaryPrimitives.WriteInt64LittleEndian(
                frame.AsSpan(offset+12,8),item.SellPrice);
        }
        return frame;
    }

    public static bool TryDecodeShopSnapshot(
        ReadOnlySpan<byte> frame,out ShopSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopSnapshot||
           frame.Length<ShopSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint seq=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        byte raw=frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        long money=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8));
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(33,2));
        if(epoch<=0||seq==0||npc<=0||raw>(byte)ShopOpenStatus.PlayerDead||
           money<0||money>SqliteIdentityStore.MaximumMoney||
           count>ShopSnapshotMaxItems||
           frame.Length!=ShopSnapshotHeaderBytes+count*ShopItemBytes)
            return false;
        var status=(ShopOpenStatus)raw;
        if(status==ShopOpenStatus.Opened && count==0)return false;
        if(status!=ShopOpenStatus.Opened && (money!=0||count!=0))return false;
        var items=new List<ShopItemWire>(count);
        var ids=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            int offset=ShopSnapshotHeaderBytes+i*ShopItemBytes;
            var item=new ShopItemWire(
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+4,8)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+12,8)));
            if(item.ItemDefinitionId is <1 or >100_000_000 ||
               item.BuyPrice is <1 or >1_000_000_000_000L||
               item.SellPrice<0||item.SellPrice>item.BuyPrice||
               !ids.Add(item.ItemDefinitionId))return false;
            items.Add(item);
        }
        value=new(epoch,seq,status,npc,money,items);
        return true;
    }

    public static byte[] EncodeShopBuy(ShopBuyWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopBuy,ShopBuyFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeShopBuy(ReadOnlySpan<byte> frame,out ShopBuyWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopBuy)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0&&
            value.ItemDefinitionId is >0 and <=100_000_000&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeShopSell(ShopSellWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||value.NpcSpawnId<=0||
           value.ItemUid<=0||value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopSell,ShopSellFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(32,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(36,4),value.Quantity);
        return frame;
    }

    public static bool TryDecodeShopSell(ReadOnlySpan<byte> frame,out ShopSellWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ShopSell)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(32,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(36,4)));
        return value.MapEpoch>0&&value.ShopSequence>0&&value.NpcSpawnId>0&&
            value.ItemUid>0&&value.ItemDefinitionId is >0 and <=100_000_000&&
            value.Quantity is >0 and <=1_000_000;
    }

    public static byte[] EncodeShopTransactionResult(ShopTransactionResultWire value)
    {
        if(!ValidShopTransactionResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ShopTransactionResult,
            ShopTransactionResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.Money);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(34,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(42,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(46,4),value.Quantity);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(50,4),value.RemainingQuantity);
        return frame;
    }

    public static bool TryDecodeShopTransactionResult(
        ReadOnlySpan<byte> frame,out ShopTransactionResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.ShopTransactionResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (ShopTransactionKind)frame[20],
            (ShopTransactionStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(34,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(42,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(46,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(50,4)));
        return ValidShopTransactionResult(value);
    }

    private static bool ValidShopTransactionResult(ShopTransactionResultWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||
           value.Kind is not (ShopTransactionKind.Buy or ShopTransactionKind.Sell)||
           (byte)value.Status>(byte)ShopTransactionStatus.NotSellable||
           value.NpcSpawnId<=0||value.Money<0||
           value.Money>SqliteIdentityStore.MaximumMoney||
           value.ItemUid<0||value.ItemDefinitionId is <1 or >100_000_000||
           value.Quantity is <1 or >1_000_000||
           value.RemainingQuantity is <0 or >1_000_000)
            return false;
        if(value.Status is ShopTransactionStatus.Applied or
            ShopTransactionStatus.AlreadyApplied)
            return value.ItemUid>0;
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

    public static byte[] EncodeStorageOpen(StorageOpenWire value)
    {
        if(value.MapEpoch<=0||value.StorageSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageOpen,StorageOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeStorageOpen(
        ReadOnlySpan<byte> frame,out StorageOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.StorageOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.StorageSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeStorageSnapshot(StorageSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.MapEpoch<=0||value.StorageSequence==0||value.NpcSpawnId<=0||
           value.StartIndex<0||value.StartIndex>StorageSnapshotMaxItems||
           value.Items is null||value.Items.Count>StorageSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>StorageSnapshotMaxItems||
           (byte)value.Status>(byte)StorageOpenStatus.PlayerDead)
            throw new ArgumentOutOfRangeException(nameof(value));
        if(value.Status!=StorageOpenStatus.Opened &&
           (value.StartIndex!=0||!value.Complete||value.Items.Count!=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var seen=new HashSet<long>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               !seen.Add(item.ItemUid))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageSnapshot,
            StorageSnapshotHeaderBytes+value.Items.Count*StorageItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25,4),value.StartIndex);
        frame[29]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(30,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=StorageSnapshotHeaderBytes+i*StorageItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
        }
        return frame;
    }

    public static bool TryDecodeStorageSnapshot(
        ReadOnlySpan<byte> frame,out StorageSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.StorageSnapshot||
           frame.Length<StorageSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint seq=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        byte raw=frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25,4));
        byte completeRaw=frame[29];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(30,2));
        if(epoch<=0||seq==0||npc<=0||raw>(byte)StorageOpenStatus.PlayerDead||
           start<0||start>StorageSnapshotMaxItems||completeRaw>1||
           count>StorageSnapshotMaxItemsPerFrame||
           start+count>StorageSnapshotMaxItems||
           frame.Length!=StorageSnapshotHeaderBytes+count*StorageItemBytes)
            return false;
        var status=(StorageOpenStatus)raw;
        bool complete=completeRaw==1;
        if(status!=StorageOpenStatus.Opened &&
           (start!=0||!complete||count!=0))return false;
        var items=new List<StorageItemWire>(count);
        var seen=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=StorageSnapshotHeaderBytes+i*StorageItemBytes;
            var item=new StorageItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)));
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||
               !seen.Add(item.ItemUid))return false;
            items.Add(item);
        }
        value=new(epoch,seq,status,npc,start,complete,items);
        return true;
    }

    private static byte[] EncodeStorageMove(
        WireOpcode opcode,StorageMoveWire value)
    {
        if(opcode is not (WireOpcode.StorageDeposit or WireOpcode.StorageWithdraw)||
           value.MapEpoch<=0||value.StorageSequence==0||
           value.NpcSpawnId<=0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,StorageMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        return frame;
    }

    public static byte[] EncodeStorageDeposit(StorageMoveWire value)=>
        EncodeStorageMove(WireOpcode.StorageDeposit,value);
    public static byte[] EncodeStorageWithdraw(StorageMoveWire value)=>
        EncodeStorageMove(WireOpcode.StorageWithdraw,value);

    private static bool TryDecodeStorageMove(
        ReadOnlySpan<byte> frame,WireOpcode opcode,out StorageMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.StorageSequence>0&&
            value.NpcSpawnId>0&&value.ItemUid>0;
    }

    public static bool TryDecodeStorageDeposit(
        ReadOnlySpan<byte> frame,out StorageMoveWire value)=>
        TryDecodeStorageMove(frame,WireOpcode.StorageDeposit,out value);
    public static bool TryDecodeStorageWithdraw(
        ReadOnlySpan<byte> frame,out StorageMoveWire value)=>
        TryDecodeStorageMove(frame,WireOpcode.StorageWithdraw,out value);

    public static byte[] EncodeStorageTransferResult(
        StorageTransferResultWire value)
    {
        if(!ValidStorageTransferResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.StorageTransferResult,
            StorageTransferResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.StorageSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.Quantity);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(42,8),value.SourceDropId);
        return frame;
    }

    public static bool TryDecodeStorageTransferResult(
        ReadOnlySpan<byte> frame,out StorageTransferResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||
           h.Opcode!=WireOpcode.StorageTransferResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (StorageTransferKind)frame[20],
            (StorageTransferStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(42,8)));
        return ValidStorageTransferResult(value);
    }

    private static bool ValidStorageTransferResult(StorageTransferResultWire value)
    {
        if(value.MapEpoch<=0||value.StorageSequence==0||
           value.Kind is not (StorageTransferKind.Deposit or StorageTransferKind.Withdraw)||
           (byte)value.Status>(byte)StorageTransferStatus.PlayerDead||
           value.NpcSpawnId<=0||value.ItemUid<=0||
           value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.Quantity<0||value.Quantity>1_000_000||value.SourceDropId<0)
            return false;
        if(value.Status is StorageTransferStatus.Applied or
            StorageTransferStatus.AlreadyApplied)
            return value.ItemDefinitionId>0&&value.Quantity>0;
        return value.ItemDefinitionId==0&&value.Quantity==0&&value.SourceDropId==0;
    }

    public static byte[] EncodeEquipmentSnapshot(EquipmentSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(value.CharacterId<=0||value.StartIndex<0||value.StartIndex>EquipmentSnapshotMaxItems||
           value.Items is null||value.Items.Count>EquipmentSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>EquipmentSnapshotMaxItems)
            throw new ArgumentOutOfRangeException(nameof(value));
        var uids=new HashSet<long>();
        var slots=new HashSet<int>();
        foreach(var item in value.Items)
            if(item.ItemUid<=0||item.SlotId is <1 or >255||
               item.ItemDefinitionId is <1 or >100_000_000||item.SourceDropId<0||
               !uids.Add(item.ItemUid)||!slots.Add(item.SlotId))
                throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EquipmentSnapshot,
            EquipmentSnapshotHeaderBytes+value.Items.Count*EquipmentItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.CharacterId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16,4),value.StartIndex);
        frame[20]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(21,2),checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=EquipmentSnapshotHeaderBytes+i*EquipmentItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.SlotId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
        }
        return frame;
    }

    public static bool TryDecodeEquipmentSnapshot(ReadOnlySpan<byte> frame,out EquipmentSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EquipmentSnapshot||
           frame.Length<EquipmentSnapshotHeaderBytes)return false;
        long characterId=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(16,4));
        byte completeRaw=frame[20];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(21,2));
        if(characterId<=0||start<0||start>EquipmentSnapshotMaxItems||completeRaw>1||
           count>EquipmentSnapshotMaxItemsPerFrame||start+count>EquipmentSnapshotMaxItems||
           frame.Length!=EquipmentSnapshotHeaderBytes+count*EquipmentItemBytes)return false;
        var items=new List<EquipmentItemWire>(count);
        var uids=new HashSet<long>();
        var slots=new HashSet<int>();
        for(int i=0;i<count;i++)
        {
            int offset=EquipmentSnapshotHeaderBytes+i*EquipmentItemBytes;
            var item=new EquipmentItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)));
            if(item.ItemUid<=0||item.SlotId is <1 or >255||
               item.ItemDefinitionId is <1 or >100_000_000||item.SourceDropId<0||
               !uids.Add(item.ItemUid)||!slots.Add(item.SlotId))return false;
            items.Add(item);
        }
        value=new(characterId,start,completeRaw==1,items);
        return true;
    }
    private static byte[] EncodeEquipmentMove(WireOpcode opcode,EquipmentMoveWire value)
    {
        if(opcode is not (WireOpcode.EquipmentEquip or WireOpcode.EquipmentUnequip)||
           value.MapEpoch<=0||value.EquipmentSequence==0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,EquipmentMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.EquipmentSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ItemUid);
        return frame;
    }

    public static byte[] EncodeEquipmentEquip(EquipmentMoveWire value)=>
        EncodeEquipmentMove(WireOpcode.EquipmentEquip,value);
    public static byte[] EncodeEquipmentUnequip(EquipmentMoveWire value)=>
        EncodeEquipmentMove(WireOpcode.EquipmentUnequip,value);

    private static bool TryDecodeEquipmentMove(ReadOnlySpan<byte> frame,WireOpcode opcode,out EquipmentMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.EquipmentSequence>0&&value.ItemUid>0;
    }

    public static bool TryDecodeEquipmentEquip(ReadOnlySpan<byte> frame,out EquipmentMoveWire value)=>
        TryDecodeEquipmentMove(frame,WireOpcode.EquipmentEquip,out value);
    public static bool TryDecodeEquipmentUnequip(ReadOnlySpan<byte> frame,out EquipmentMoveWire value)=>
        TryDecodeEquipmentMove(frame,WireOpcode.EquipmentUnequip,out value);

    public static byte[] EncodeEquipmentTransferResult(EquipmentTransferResultWire value)
    {
        if(!ValidEquipmentTransferResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.EquipmentTransferResult,EquipmentTransferResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.EquipmentSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(30,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.SlotId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(38,8),value.SourceDropId);
        return frame;
    }

    public static bool TryDecodeEquipmentTransferResult(ReadOnlySpan<byte> frame,out EquipmentTransferResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.EquipmentTransferResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (EquipmentTransferKind)frame[20],
            (EquipmentTransferStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(30,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(38,8)));
        return ValidEquipmentTransferResult(value);
    }

    private static bool ValidEquipmentTransferResult(EquipmentTransferResultWire value)
    {
        if(value.MapEpoch<=0||value.EquipmentSequence==0||
           value.Kind is not (EquipmentTransferKind.Equip or EquipmentTransferKind.Unequip)||
           (byte)value.Status>(byte)EquipmentTransferStatus.StaleGeneration||
           value.ItemUid<=0||value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.SlotId<0||value.SlotId>255||value.SourceDropId<0)return false;
        if(value.Status is EquipmentTransferStatus.Applied or EquipmentTransferStatus.AlreadyApplied)
            return value.ItemDefinitionId>0&&value.SlotId>0;
        return value.ItemDefinitionId==0&&value.SlotId==0&&value.SourceDropId==0;
    }
    public static byte[] EncodeBankOpen(BankOpenWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankOpen,BankOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeBankOpen(ReadOnlySpan<byte> frame,out BankOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankOpen)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.BankSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeBankSnapshot(BankSnapshotWire value)
    {
        if(!ValidBankSnapshot(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankSnapshot,BankSnapshotFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        frame[25]=(byte)value.Scope;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(26,8),value.WalletMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(34,8),value.BankMoney);
        return frame;
    }

    public static bool TryDecodeBankSnapshot(ReadOnlySpan<byte> frame,out BankSnapshotWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankSnapshot)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (BankOpenStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4)),
            (BankBalanceScope)frame[25],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(26,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(34,8)));
        return ValidBankSnapshot(value);
    }

    private static bool ValidBankSnapshot(BankSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)BankOpenStatus.PlayerDead||
           value.WalletMoney<0||value.BankMoney<0||
           value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.BankMoney>SqliteIdentityStore.MaximumMoney)return false;
        if(value.Status==BankOpenStatus.Opened)
            return value.Scope is BankBalanceScope.Character or BankBalanceScope.Account;
        return value.Scope==BankBalanceScope.None&&value.WalletMoney==0&&value.BankMoney==0;
    }

    private static byte[] EncodeBankMove(WireOpcode opcode,BankMoveWire value)
    {
        if(opcode is not (WireOpcode.BankDeposit or WireOpcode.BankWithdraw)||
           value.MapEpoch<=0||value.BankSequence==0||value.NpcSpawnId<=0||
           value.Amount<=0||value.Amount>SqliteIdentityStore.MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(opcode,BankMoveFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.Amount);
        return frame;
    }

    public static byte[] EncodeBankDeposit(BankMoveWire value)=>
        EncodeBankMove(WireOpcode.BankDeposit,value);
    public static byte[] EncodeBankWithdraw(BankMoveWire value)=>
        EncodeBankMove(WireOpcode.BankWithdraw,value);

    private static bool TryDecodeBankMove(
        ReadOnlySpan<byte> frame,WireOpcode opcode,out BankMoveWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=opcode)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.BankSequence>0&&value.NpcSpawnId>0&&
            value.Amount>0&&value.Amount<=SqliteIdentityStore.MaximumMoney;
    }

    public static bool TryDecodeBankDeposit(ReadOnlySpan<byte> frame,out BankMoveWire value)=>
        TryDecodeBankMove(frame,WireOpcode.BankDeposit,out value);
    public static bool TryDecodeBankWithdraw(ReadOnlySpan<byte> frame,out BankMoveWire value)=>
        TryDecodeBankMove(frame,WireOpcode.BankWithdraw,out value);

    public static byte[] EncodeBankTransactionResult(BankTransactionResultWire value)
    {
        if(!ValidBankTransactionResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.BankTransactionResult,BankTransactionResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.BankSequence);
        frame[20]=(byte)value.Kind;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(22,4),value.NpcSpawnId);
        frame[26]=(byte)value.Scope;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(27,8),value.Amount);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(35,8),value.WalletMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(43,8),value.BankMoney);
        return frame;
    }

    public static bool TryDecodeBankTransactionResult(
        ReadOnlySpan<byte> frame,out BankTransactionResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.BankTransactionResult)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (BankTransactionKind)frame[20],
            (BankTransactionStatus)frame[21],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(22,4)),
            (BankBalanceScope)frame[26],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(27,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(35,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(43,8)));
        return ValidBankTransactionResult(value);
    }

    private static bool ValidBankTransactionResult(BankTransactionResultWire value)
    {
        if(value.MapEpoch<=0||value.BankSequence==0||
           value.Kind is not (BankTransactionKind.Deposit or BankTransactionKind.Withdraw)||
           (byte)value.Status>(byte)BankTransactionStatus.PlayerDead||value.NpcSpawnId<=0||
           value.Amount<=0||value.Amount>SqliteIdentityStore.MaximumMoney||
           value.WalletMoney<0||value.BankMoney<0||
           value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.BankMoney>SqliteIdentityStore.MaximumMoney)return false;
        bool scoped=value.Scope is BankBalanceScope.Character or BankBalanceScope.Account;
        if(value.Status is BankTransactionStatus.Applied or
            BankTransactionStatus.AlreadyApplied or
            BankTransactionStatus.InsufficientWallet or
            BankTransactionStatus.InsufficientBank or
            BankTransactionStatus.WalletLimit or
            BankTransactionStatus.BankLimit or
            BankTransactionStatus.StaleSequence)
            return scoped;
        return value.Scope==BankBalanceScope.None&&
            value.WalletMoney==0&&value.BankMoney==0;
    }
    public static byte[] EncodeRepairOpen(RepairOpenWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairOpen,RepairOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeRepairOpen(
        ReadOnlySpan<byte> frame,out RepairOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.RepairSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeRepairSnapshot(RepairSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!ValidRepairSnapshot(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairSnapshot,
            RepairSnapshotHeaderBytes+value.Items.Count*RepairPreviewItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.WalletMoney);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.StartIndex);
        frame[37]=value.Complete?(byte)1:(byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),
            checked((ushort)value.Items.Count));
        for(int i=0;i<value.Items.Count;i++)
        {
            int offset=RepairSnapshotHeaderBytes+i*RepairPreviewItemBytes;
            var item=value.Items[i];
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            frame[offset+12]=(byte)item.Location;
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+13,4),item.CurrentDurability);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+17,4),item.MaxDurability);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+21,8),item.Cost);
        }
        return frame;
    }

    public static bool TryDecodeRepairSnapshot(
        ReadOnlySpan<byte> frame,out RepairSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairSnapshot||
           frame.Length<RepairSnapshotHeaderBytes)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(RepairOpenStatus)frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        long wallet=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8));
        int start=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4));
        byte completeRaw=frame[37];
        int count=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        if(epoch<=0||sequence==0||npc<=0||
           (byte)status>(byte)RepairOpenStatus.PlayerDead||
           wallet<0||wallet>SqliteIdentityStore.MaximumMoney||
           start<0||start>RepairSnapshotMaxItems||completeRaw>1||
           count>RepairSnapshotMaxItemsPerFrame||
           start+count>RepairSnapshotMaxItems||
           frame.Length!=RepairSnapshotHeaderBytes+count*RepairPreviewItemBytes)
            return false;
        var items=new List<RepairPreviewItemWire>(count);
        var uids=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            int offset=RepairSnapshotHeaderBytes+i*RepairPreviewItemBytes;
            var item=new RepairPreviewItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                (RepairItemLocation)frame[offset+12],
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+13,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+17,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+21,8)));
            if(!ValidRepairPreviewItem(item)||!uids.Add(item.ItemUid))
                return false;
            items.Add(item);
        }
        value=new(epoch,sequence,status,npc,wallet,start,completeRaw==1,items);
        return ValidRepairSnapshot(value);
    }

    private static bool ValidRepairSnapshot(RepairSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)RepairOpenStatus.PlayerDead||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney||
           value.StartIndex<0||value.StartIndex>RepairSnapshotMaxItems||
           value.Items is null||
           value.Items.Count>RepairSnapshotMaxItemsPerFrame||
           value.StartIndex+value.Items.Count>RepairSnapshotMaxItems)
            return false;
        if(value.Status!=RepairOpenStatus.Opened)
            return value.WalletMoney==0&&value.StartIndex==0&&value.Complete&&
                value.Items.Count==0;
        var uids=new HashSet<long>();
        return value.Items.All(item=>ValidRepairPreviewItem(item)&&uids.Add(item.ItemUid));
    }

    private static bool ValidRepairPreviewItem(RepairPreviewItemWire item)=>
        item.ItemUid>0&&
        item.ItemDefinitionId is >=1 and <=100_000_000&&
        item.Location is RepairItemLocation.Inventory or RepairItemLocation.Equipped&&
        item.MaxDurability>0&&item.CurrentDurability>=0&&
        item.CurrentDurability<=item.MaxDurability&&
        item.Cost>=0&&item.Cost<=SqliteIdentityStore.MaximumMoney;

    public static byte[] EncodeRepairItem(RepairItemWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||
           value.NpcSpawnId<=0||value.ItemUid<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairItem,RepairItemFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ItemUid);
        return frame;
    }

    public static bool TryDecodeRepairItem(
        ReadOnlySpan<byte> frame,out RepairItemWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairItem)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.RepairSequence>0&&
            value.NpcSpawnId>0&&value.ItemUid>0;
    }

    public static byte[] EncodeRepairResult(RepairResultWire value)
    {
        if(!ValidRepairResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.RepairResult,RepairResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.RepairSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(25,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(33,4),value.ItemDefinitionId);
        frame[37]=(byte)value.Location;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.CurrentDurability);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(42,4),value.MaxDurability);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(46,8),value.Cost);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(54,8),value.WalletMoney);
        return frame;
    }

    public static bool TryDecodeRepairResult(
        ReadOnlySpan<byte> frame,out RepairResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.RepairResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (RepairStatus)frame[20],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(25,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(33,4)),
            (RepairItemLocation)frame[37],
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(42,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(46,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(54,8)));
        return ValidRepairResult(value);
    }

    private static bool ValidRepairResult(RepairResultWire value)
    {
        if(value.MapEpoch<=0||value.RepairSequence==0||
           (byte)value.Status>(byte)RepairStatus.InvalidDurability||
           value.NpcSpawnId<=0||value.ItemUid<=0||
           value.ItemDefinitionId<0||value.ItemDefinitionId>100_000_000||
           value.CurrentDurability<0||value.MaxDurability<0||
           value.Cost<0||value.Cost>SqliteIdentityStore.MaximumMoney||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney)
            return false;
        bool knownItem=value.ItemDefinitionId>0&&
            value.Location is RepairItemLocation.Inventory or RepairItemLocation.Equipped;
        return value.Status switch
        {
            RepairStatus.Applied or RepairStatus.AlreadyApplied =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability==value.MaxDurability&&value.Cost>0,
            RepairStatus.AlreadyFull =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability==value.MaxDurability&&value.Cost==0,
            RepairStatus.InsufficientMoney =>
                knownItem&&value.MaxDurability>0&&
                value.CurrentDurability<=value.MaxDurability&&value.Cost>0,
            RepairStatus.UnknownItem =>
                knownItem&&value.CurrentDurability==0&&
                value.MaxDurability==0&&value.Cost==0,
            RepairStatus.InvalidDurability =>
                knownItem&&value.CurrentDurability==0&&
                value.MaxDurability>0&&value.Cost==0,
            _ =>
                value.ItemDefinitionId==0&&value.Location==RepairItemLocation.None&&
                value.CurrentDurability==0&&value.MaxDurability==0&&value.Cost==0
        };
    }

    public static byte[] EncodeTradeCommand(TradeCommandWire value)
    {
        if(!ValidTradeCommand(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.TradeCommand,TradeCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.TradeSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.TradeId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.Money);
        return frame;
    }

    public static bool TryDecodeTradeCommand(ReadOnlySpan<byte> frame,out TradeCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeCommand)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (TradeCommandAction)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)));
        return ValidTradeCommand(value);
    }

    private static bool ValidTradeCommand(TradeCommandWire value)
    {
        if(value.MapEpoch<=0||value.TradeSequence==0||
           value.Action is < TradeCommandAction.Request or > TradeCommandAction.Cancel||
           value.TradeId<0||value.TargetActorId<0||value.ItemUid<0||
           value.Money<0||value.Money>SqliteIdentityStore.MaximumMoney)return false;
        return value.Action switch
        {
            TradeCommandAction.Request => value.TradeId==0&&value.TargetActorId>0&&
                value.ItemUid==0&&value.Money==0,
            TradeCommandAction.Accept or TradeCommandAction.Reject =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0&&value.Money==0,
            TradeCommandAction.AddItem or TradeCommandAction.RemoveItem =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid>0&&value.Money==0,
            TradeCommandAction.SetMoney =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0,
            TradeCommandAction.Lock or TradeCommandAction.FinalConfirm or TradeCommandAction.Cancel =>
                value.TradeId>0&&value.TargetActorId==0&&value.ItemUid==0&&value.Money==0,
            _ => false
        };
    }

    public static byte[] EncodeTradeSnapshot(TradeSnapshotWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if(!ValidTradeSnapshot(value))throw new ArgumentOutOfRangeException(nameof(value));
        int count=value.MyItems.Count+value.OtherItems.Count;
        var frame=NewFrame(WireOpcode.TradeSnapshot,
            TradeSnapshotHeaderBytes+count*TradeOfferItemBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.TradeId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        frame[28]=(byte)value.State;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.PeerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.MyMoney);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.OtherMoney);
        frame[53]=value.MyLocked?(byte)1:(byte)0;
        frame[54]=value.OtherLocked?(byte)1:(byte)0;
        frame[55]=value.MyConfirmed?(byte)1:(byte)0;
        frame[56]=value.OtherConfirmed?(byte)1:(byte)0;
        frame[57]=checked((byte)value.MyItems.Count);
        frame[58]=checked((byte)value.OtherItems.Count);
        int offset=TradeSnapshotHeaderBytes;
        foreach(var item in value.MyItems.Concat(value.OtherItems))
        {
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset,8),item.ItemUid);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+8,4),item.ItemDefinitionId);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset+12,4),item.Quantity);
            BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(offset+16,8),item.SourceDropId);
            offset+=TradeOfferItemBytes;
        }
        return frame;
    }

    public static bool TryDecodeTradeSnapshot(ReadOnlySpan<byte> frame,out TradeSnapshotWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeSnapshot||
           frame.Length<TradeSnapshotHeaderBytes)return false;
        int myCount=frame[57],otherCount=frame[58];
        if(myCount>TradeMaxItemsPerSide||otherCount>TradeMaxItemsPerSide||
           frame.Length!=TradeSnapshotHeaderBytes+(myCount+otherCount)*TradeOfferItemBytes)
            return false;
        var my=new List<TradeOfferItemWire>(myCount);
        var other=new List<TradeOfferItemWire>(otherCount);
        int offset=TradeSnapshotHeaderBytes;
        for(int i=0;i<myCount+otherCount;i++)
        {
            var item=new TradeOfferItemWire(
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset,8)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+8,4)),
                BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset+12,4)),
                BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(offset+16,8)));
            (i<myCount?my:other).Add(item);
            offset+=TradeOfferItemBytes;
        }
        var decoded=new TradeSnapshotWire(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4)),
            (TradeSessionWireState)frame[28],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)),
            frame[53]==1,frame[54]==1,frame[55]==1,frame[56]==1,my,other);
        if(frame[53]>1||frame[54]>1||frame[55]>1||frame[56]>1||
           !ValidTradeSnapshot(decoded))return false;
        value=decoded;
        return true;
    }

    private static bool ValidTradeSnapshot(TradeSnapshotWire value)
    {
        if(value.MapEpoch<=0||value.TradeId<=0||value.Revision==0||value.PeerActorId<=0||
           value.State is < TradeSessionWireState.InviteReceived or > TradeSessionWireState.AwaitingFinalConfirm||
           value.MyMoney<0||value.OtherMoney<0||
           value.MyMoney>SqliteIdentityStore.MaximumMoney||value.OtherMoney>SqliteIdentityStore.MaximumMoney||
           value.MyItems is null||value.OtherItems is null||
           value.MyItems.Count>TradeMaxItemsPerSide||value.OtherItems.Count>TradeMaxItemsPerSide||
           (value.MyConfirmed&&!value.MyLocked)||(value.OtherConfirmed&&!value.OtherLocked))
            return false;
        var seen=new HashSet<long>();
        foreach(var item in value.MyItems.Concat(value.OtherItems))
            if(item.ItemUid<=0||item.ItemDefinitionId is <1 or >100_000_000||
               item.Quantity is <1 or >1_000_000||item.SourceDropId<0||!seen.Add(item.ItemUid))
                return false;
        bool invite=value.State is TradeSessionWireState.InviteReceived or TradeSessionWireState.InviteSent;
        if(invite)
            return value.MyMoney==0&&value.OtherMoney==0&&
                !value.MyLocked&&!value.OtherLocked&&!value.MyConfirmed&&!value.OtherConfirmed&&
                value.MyItems.Count==0&&value.OtherItems.Count==0;
        if(value.State==TradeSessionWireState.AwaitingFinalConfirm)
            return value.MyLocked&&value.OtherLocked;
        return true;
    }

    public static byte[] EncodeTradeTerminal(TradeTerminalWire value)
    {
        if(!ValidTradeTerminal(value))throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.TradeTerminal,TradeTerminalFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.TradeId);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(24,4),value.Revision);
        frame[28]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.WalletMoney);
        return frame;
    }

    public static bool TryDecodeTradeTerminal(ReadOnlySpan<byte> frame,out TradeTerminalWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.TradeTerminal)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(24,4)),
            (TradeTerminalStatus)frame[28],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)));
        return ValidTradeTerminal(value);
    }

    private static bool ValidTradeTerminal(TradeTerminalWire value)
    {
        if(value.MapEpoch<=0||value.TradeId<=0||value.Revision==0||
           value.Status is < TradeTerminalStatus.Success or > TradeTerminalStatus.StaleGeneration||
           value.WalletMoney<0||value.WalletMoney>SqliteIdentityStore.MaximumMoney)return false;
        return value.Status==TradeTerminalStatus.Success||value.WalletMoney==0;
    }
    public static byte[] EncodePvpModeCommand(PvpModeCommandWire value)
    {
        if(value.MapEpoch<=0||value.PvpSequence==0||(byte)value.Action>2)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpModeCommand,PvpModeCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PvpSequence);
        frame[20]=(byte)value.Action;
        return frame;
    }

    public static bool TryDecodePvpModeCommand(ReadOnlySpan<byte> frame,out PvpModeCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpModeCommand)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpModeCommandAction)frame[20]);
        return value.MapEpoch>0&&value.PvpSequence>0&&(byte)value.Action<=2;
    }

    public static byte[] EncodePvpModeResult(PvpModeResultWire value)
    {
        if(value.MapEpoch<=0||value.PvpSequence==0||(byte)value.Status>6||
           (byte)value.Mode>1)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpModeResult,PvpModeResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.PvpSequence);
        frame[20]=(byte)value.Status; frame[21]=(byte)value.Mode;
        frame[22]=value.MapAllowsPlayerPvp?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpModeResult(ReadOnlySpan<byte> frame,out PvpModeResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpModeResult)return false;
        byte allowed=frame[22];
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpModeResultStatus)frame[20],(PvpMode)frame[21],allowed==1);
        return value.MapEpoch>0&&value.PvpSequence>0&&allowed<=1&&
            (byte)value.Status<=6&&(byte)value.Mode<=1;
    }

    public static byte[] EncodePvpAttack(PvpAttackWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||value.TargetActorId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpAttack,PvpAttackFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodePvpAttack(ReadOnlySpan<byte> frame,out PvpAttackWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpAttack)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)));
        return value.MapEpoch>0&&value.AttackSequence>0&&value.TargetActorId>0;
    }

    public static byte[] EncodePvpAttackResult(PvpAttackResultWire value)
    {
        if(value.MapEpoch<=0||value.AttackSequence==0||(byte)value.Status>14||
           value.ServerTick<0||value.TargetActorId<=0||value.Damage<0||
           (value.Status==PvpAttackStatus.Applied?value.Damage<=0:value.Damage!=0||value.Died))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpAttackResult,PvpAttackResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.AttackSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.TargetActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(37,4),value.Damage);
        frame[41]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpAttackResult(ReadOnlySpan<byte> frame,out PvpAttackResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpAttackResult)return false;
        byte died=frame[41];
        if(died>1)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PvpAttackStatus)frame[20],BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(37,4)),died==1);
        return value.MapEpoch>0&&value.AttackSequence>0&&(byte)value.Status<=14&&
            value.ServerTick>=0&&value.TargetActorId>0&&value.Damage>=0&&
            (value.Status==PvpAttackStatus.Applied?value.Damage>0:value.Damage==0&&!value.Died);
    }

    public static byte[] EncodePvpDamage(PvpDamageWire value)
    {
        if(value.MapEpoch<=0||value.DamageSequence==0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.AttackerAttackSequence==0||value.Damage<=0||
           value.CurrentHp<0||value.MaxHp<=0||value.CurrentHp>value.MaxHp||
           (value.Died?value.CurrentHp!=0:value.CurrentHp<=0))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpDamage,PvpDamageFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.DamageSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(28,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(36,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(44,4),value.AttackerAttackSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(48,4),value.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(52,4),value.CurrentHp);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(56,4),value.MaxHp);
        frame[60]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpDamage(ReadOnlySpan<byte> frame,out PvpDamageWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpDamage)return false;
        byte died=frame[60];
        if(died>1)return false;
        value=new(BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(28,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(36,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(48,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(52,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(56,4)),died==1);
        return value.MapEpoch>0&&value.DamageSequence>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.AttackerAttackSequence>0&&value.Damage>0&&
            value.CurrentHp>=0&&value.MaxHp>0&&value.CurrentHp<=value.MaxHp&&
            (value.Died?value.CurrentHp==0:value.CurrentHp>0);
    }

    public static byte[] EncodePvpCombatVisual(PvpCombatVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.AttackSequence==0||value.VictimActorId<=0||
           value.VictimConnectionEpoch<=0||value.DamageSequence==0||
           value.Damage<=0||value.AttackerActorId==value.VictimActorId||
           value.AttackerFacing is <1 or >8||value.VictimFacing is <1 or >8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpCombatVisual,PvpCombatVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.AttackSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(44,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(52,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(60,4),value.DamageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(64,4),value.Damage);
        frame[68]=value.Died?(byte)1:(byte)0;
        frame[69]=value.AttackerFacing;
        frame[70]=value.VictimFacing;
        return frame;
    }

    public static bool TryDecodePvpCombatVisual(
        ReadOnlySpan<byte> frame,out PvpCombatVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpCombatVisual)
            return false;
        byte died=frame[68];
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(44,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(52,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(60,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(64,4)),
            died==1,frame[69],frame[70]);
        return died<=1&&value.ReceiverMapEpoch>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.AttackSequence>0&&value.VictimActorId>0&&
            value.VictimConnectionEpoch>0&&value.DamageSequence>0&&
            value.Damage>0&&value.AttackerActorId!=value.VictimActorId&&
            value.AttackerFacing is >=1 and <=8&&value.VictimFacing is >=1 and <=8;
    }

    public static byte[] EncodePvpSkill(PvpSkillWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||
           value.SkillId is <1 or >100_000_000||value.TargetActorId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkill,PvpSkillFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.TargetActorId);
        return frame;
    }

    public static bool TryDecodePvpSkill(ReadOnlySpan<byte> frame,out PvpSkillWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkill)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)));
        return value.MapEpoch>0&&value.SkillSequence>0&&
            value.SkillId is >0 and <=100_000_000&&value.TargetActorId>0;
    }

    public static byte[] EncodePvpSkillResult(PvpSkillResultWire value)
    {
        if(value.MapEpoch<=0||value.SkillSequence==0||
           (byte)value.Status>(byte)SkillUseStatus.Backpressured||
           value.ServerTick<0||value.SkillId is <1 or >100_000_000||
           value.TargetActorId<=0||value.Damage<0||
           (value.Status==SkillUseStatus.Applied
                ? value.Damage<=0
                : value.Damage!=0||value.Died))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkillResult,PvpSkillResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.SkillSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.ServerTick);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(33,8),value.TargetActorId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(41,4),value.Damage);
        frame[45]=value.Died?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodePvpSkillResult(
        ReadOnlySpan<byte> frame,out PvpSkillResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkillResult)return false;
        byte died=frame[45];
        if(died>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (SkillUseStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(33,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(41,4)),died==1);
        return value.MapEpoch>0&&value.SkillSequence>0&&
            (byte)value.Status<=(byte)SkillUseStatus.Backpressured&&
            value.ServerTick>=0&&value.SkillId is >0 and <=100_000_000&&
            value.TargetActorId>0&&value.Damage>=0&&
            (value.Status==SkillUseStatus.Applied
                ? value.Damage>0
                : value.Damage==0&&!value.Died);
    }

    public static byte[] EncodePvpSkillVisual(PvpSkillVisualWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ServerTick<0||
           value.AttackerActorId<=0||value.AttackerConnectionEpoch<=0||
           value.SkillSequence==0||value.SkillId is <1 or >100_000_000||
           value.VictimActorId<=0||value.VictimConnectionEpoch<=0||
           value.DamageSequence==0||value.Damage<=0||
           value.AttackerActorId==value.VictimActorId||
           value.AttackerFacing is <1 or >8||value.VictimFacing is <1 or >8)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PvpSkillVisual,PvpSkillVisualFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ServerTick);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.AttackerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.AttackerConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(40,4),value.SkillSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(44,4),value.SkillId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(48,8),value.VictimActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(56,8),value.VictimConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(64,4),value.DamageSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(68,4),value.Damage);
        frame[72]=value.Died?(byte)1:(byte)0;
        frame[73]=value.AttackerFacing;
        frame[74]=value.VictimFacing;
        return frame;
    }

    public static bool TryDecodePvpSkillVisual(
        ReadOnlySpan<byte> frame,out PvpSkillVisualWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PvpSkillVisual)return false;
        byte died=frame[72];
        if(died>1)return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(40,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(44,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(48,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(56,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(64,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(68,4)),
            died==1,frame[73],frame[74]);
        return value.ReceiverMapEpoch>0&&value.ServerTick>=0&&
            value.AttackerActorId>0&&value.AttackerConnectionEpoch>0&&
            value.SkillSequence>0&&value.SkillId is >0 and <=100_000_000&&
            value.VictimActorId>0&&value.VictimConnectionEpoch>0&&
            value.DamageSequence>0&&value.Damage>0&&
            value.AttackerActorId!=value.VictimActorId&&
            value.AttackerFacing is >=1 and <=8&&value.VictimFacing is >=1 and <=8;
    }

    public static byte[] EncodeGmCapability(GmCapabilityWire value)
    {
        var frame=NewFrame(WireOpcode.GmCapability,GmCapabilityFrameBytes);
        frame[8]=value.Authorized?(byte)1:(byte)0;
        return frame;
    }

    public static bool TryDecodeGmCapability(
        ReadOnlySpan<byte> frame,out GmCapabilityWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmCapability||
           frame[8]>1)
            return false;
        value=new(frame[8]==1);
        return true;
    }

    public static byte[] EncodeGmPvpNameCommand(GmPvpNameCommandWire value)
    {
        if(value.MapEpoch<=0||value.GmSequence==0||value.TargetActorId<=0||
           (byte)value.State>4)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GmPvpNameCommand,GmPvpNameCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.GmSequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(20,8),value.TargetActorId);
        frame[28]=(byte)value.State;
        return frame;
    }

    public static bool TryDecodeGmPvpNameCommand(
        ReadOnlySpan<byte> frame,out GmPvpNameCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmPvpNameCommand)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20,8)),
            (PvpNameState)frame[28]);
        return value.MapEpoch>0&&value.GmSequence>0&&
            value.TargetActorId>0&&(byte)value.State<=4;
    }

    public static byte[] EncodeGmPvpNameResult(GmPvpNameResultWire value)
    {
        if(value.MapEpoch<=0||value.GmSequence==0||
           (byte)value.Status>5||value.TargetActorId<=0||(byte)value.State>4)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.GmPvpNameResult,GmPvpNameResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.GmSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.TargetActorId);
        frame[29]=(byte)value.State;
        return frame;
    }

    public static bool TryDecodeGmPvpNameResult(
        ReadOnlySpan<byte> frame,out GmPvpNameResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.GmPvpNameResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (GmPvpNameResultStatus)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            (PvpNameState)frame[29]);
        return value.MapEpoch>0&&value.GmSequence>0&&
            (byte)value.Status<=5&&value.TargetActorId>0&&(byte)value.State<=4;
    }

    public static byte[] EncodePlayerShopCommand(PlayerShopCommandWire value)
    {
        if(!ValidPlayerShopCommand(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopCommand,PlayerShopCommandFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Action;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(21,8),value.SellerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(29,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(37,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(45,8),value.Price);
        return frame;
    }

    public static bool TryDecodePlayerShopCommand(
        ReadOnlySpan<byte> frame,out PlayerShopCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopCommand)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PlayerShopCommandAction)frame[20],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(21,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(29,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(37,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(45,8)));
        return ValidPlayerShopCommand(value);
    }

    public static byte[] EncodePlayerShopResult(PlayerShopResultWire value)
    {
        if(!ValidPlayerShopResult(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopResult,PlayerShopResultFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ShopSequence);
        frame[20]=(byte)value.Action;
        frame[21]=(byte)value.Status;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(22,8),value.SellerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(30,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(38,8),value.ItemUid);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(46,4),value.ItemDefinitionId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(50,4),value.Quantity);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(54,8),value.SourceDropId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(62,4),value.DurabilityRaw);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(66,8),value.Price);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(74,8),value.SellerWallet);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(82,8),value.BuyerWallet);
        return frame;
    }

    public static bool TryDecodePlayerShopResult(
        ReadOnlySpan<byte> frame,out PlayerShopResultWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopResult)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            (PlayerShopCommandAction)frame[20],
            (PlayerShopResultStatus)frame[21],
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(22,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(30,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(38,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(46,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(50,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(54,8)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(62,4)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(66,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(74,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(82,8)));
        return ValidPlayerShopResult(value);
    }

    private static bool ValidPlayerShopCommand(PlayerShopCommandWire value)
    {
        if(value.MapEpoch<=0||value.ShopSequence==0||
           value.Action is <PlayerShopCommandAction.Open or >PlayerShopCommandAction.Buy||
           value.SellerActorId<0||value.ListingId<0||value.ItemUid<0||
           value.Price<0||value.Price>SqliteIdentityStore.MaximumMoney)
            return false;
        return value.Action switch
        {
            PlayerShopCommandAction.Open =>
                value.SellerActorId==0&&value.ListingId==0&&
                value.ItemUid>0&&value.Price>0,
            PlayerShopCommandAction.Close =>
                value.SellerActorId==0&&value.ListingId>0&&
                value.ItemUid==0&&value.Price==0,
            PlayerShopCommandAction.Inspect =>
                value.SellerActorId>0&&value.ListingId==0&&
                value.ItemUid==0&&value.Price==0,
            PlayerShopCommandAction.Buy =>
                value.SellerActorId>0&&value.ListingId>0&&
                value.ItemUid==0&&value.Price==0,
            _=>false
        };
    }

    private static bool ValidPlayerShopResult(PlayerShopResultWire value)=>
        value.MapEpoch>0&&value.ShopSequence>0&&
        value.Action is >=PlayerShopCommandAction.Open and <=PlayerShopCommandAction.Buy&&
        (byte)value.Status<=15&&
        value.SellerActorId>=0&&value.ListingId>=0&&value.ItemUid>=0&&
        value.ItemDefinitionId is >=0 and <=100_000_000&&
        value.Quantity is >=0 and <=1_000_000&&value.SourceDropId>=0&&
        value.DurabilityRaw>=-1&&
        value.Price>=0&&value.Price<=SqliteIdentityStore.MaximumMoney&&
        value.SellerWallet>=0&&value.SellerWallet<=SqliteIdentityStore.MaximumMoney&&
        value.BuyerWallet>=0&&value.BuyerWallet<=SqliteIdentityStore.MaximumMoney;

    public static byte[] EncodePlayerShopSold(PlayerShopSoldWire value)
    {
        if(value.ReceiverMapEpoch<=0||value.ListingId<=0||
           value.BuyerActorId<=0||value.ItemUid<=0||
           value.SellerWallet<0||value.SellerWallet>SqliteIdentityStore.MaximumMoney)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.PlayerShopSold,PlayerShopSoldFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ListingId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.BuyerActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.ItemUid);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(40,8),value.SellerWallet);
        return frame;
    }

    public static bool TryDecodePlayerShopSold(
        ReadOnlySpan<byte> frame,out PlayerShopSoldWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerShopSold)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8)),
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(40,8)));
        return value.ReceiverMapEpoch>0&&value.ListingId>0&&
            value.BuyerActorId>0&&value.ItemUid>0&&
            value.SellerWallet>=0&&value.SellerWallet<=SqliteIdentityStore.MaximumMoney;
    }

    public static byte[] EncodeChatCommand(ChatCommandWire value)
    {
        byte[] message;
        try { message=StrictUtf8.GetBytes(value.Message??string.Empty); }
        catch(EncoderFallbackException) { throw new ArgumentOutOfRangeException(nameof(value)); }
        if(value.MapEpoch<=0||value.ChatSequence==0||(byte)value.Channel>4||
           value.BubbleType>5||message.Length is <1 or >ChatMaxMessageBytes||
           string.IsNullOrWhiteSpace(value.Message))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ChatCommand,ChatCommandHeaderBytes+message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.ChatSequence);
        frame[20]=(byte)value.Channel; frame[21]=value.BubbleType;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(22,2),(ushort)message.Length);
        message.AsSpan().CopyTo(frame.AsSpan(ChatCommandHeaderBytes));
        return frame;
    }

    public static bool TryDecodeChatCommand(ReadOnlySpan<byte> frame,out ChatCommandWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ChatCommand||
           frame.Length<ChatCommandHeaderBytes+1)return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var channel=(ChatChannel)frame[20]; byte bubble=frame[21];
        int length=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(22,2));
        if(epoch<=0||sequence==0||(byte)channel>4||bubble>5||
           length is <1 or >ChatMaxMessageBytes||frame.Length!=ChatCommandHeaderBytes+length)
            return false;
        try
        {
            string message=StrictUtf8.GetString(frame.Slice(ChatCommandHeaderBytes,length));
            if(string.IsNullOrWhiteSpace(message))return false;
            value=new(epoch,sequence,channel,bubble,message);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodeChatDelivery(ChatDeliveryWire value)
    {
        byte[] name,message;
        try
        {
            name=StrictUtf8.GetBytes(value.SenderName??string.Empty);
            message=StrictUtf8.GetBytes(value.Message??string.Empty);
        }
        catch(EncoderFallbackException){throw new ArgumentOutOfRangeException(nameof(value));}
        if(value.ReceiverMapEpoch<=0||value.SenderActorId<=0||
           value.SenderConnectionEpoch<=0||value.ChatSequence==0||
           (byte)value.Channel>4||value.BubbleType>5||
           name.Length is <1 or >ChatMaxSenderNameBytes||
           message.Length is <1 or >ChatMaxMessageBytes||
           string.IsNullOrWhiteSpace(value.SenderName)||string.IsNullOrWhiteSpace(value.Message))
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.ChatDelivery,
            ChatDeliveryHeaderBytes+name.Length+message.Length);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.SenderActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.SenderConnectionEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(32,4),value.ChatSequence);
        frame[36]=(byte)value.Channel; frame[37]=value.BubbleType;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(38,2),(ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(40,2),(ushort)message.Length);
        name.AsSpan().CopyTo(frame.AsSpan(ChatDeliveryHeaderBytes));
        message.AsSpan().CopyTo(frame.AsSpan(ChatDeliveryHeaderBytes+name.Length));
        return frame;
    }

    public static bool TryDecodeChatDelivery(ReadOnlySpan<byte> frame,out ChatDeliveryWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.ChatDelivery||
           frame.Length<ChatDeliveryHeaderBytes+2)return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(32,4));
        var channel=(ChatChannel)frame[36]; byte bubble=frame[37];
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(38,2));
        int messageLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(40,2));
        if(receiver<=0||actor<=0||connection<=0||sequence==0||
           (byte)channel>4||bubble>5||nameLength is <1 or >ChatMaxSenderNameBytes||
           messageLength is <1 or >ChatMaxMessageBytes||
           frame.Length!=ChatDeliveryHeaderBytes+nameLength+messageLength)return false;
        try
        {
            string name=StrictUtf8.GetString(frame.Slice(ChatDeliveryHeaderBytes,nameLength));
            string message=StrictUtf8.GetString(frame.Slice(ChatDeliveryHeaderBytes+nameLength,messageLength));
            if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(message))return false;
            value=new(receiver,actor,connection,sequence,channel,bubble,name,message);
            return true;
        }
        catch(DecoderFallbackException){return false;}
    }

    public static byte[] EncodePlayerIdentity(PlayerIdentityWire value)
    {
        byte[] name;
        try { name=StrictUtf8.GetBytes(value.Name??string.Empty); }
        catch(EncoderFallbackException)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if(value.ReceiverMapEpoch<=0||value.ActorId<=0||
           value.ConnectionEpoch<=0||value.EntityMapEpoch<=0||
           value.Mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           value.ServerUnixMilliseconds<=0||value.PinkUntilUnixMilliseconds<0||
           value.PvpUpdatedUnixMilliseconds<0||
           name.Length is <1 or >PlayerIdentityMaxNameBytes||
           string.IsNullOrWhiteSpace(value.Name))
            throw new ArgumentOutOfRangeException(nameof(value));
        int bytes=PlayerIdentityHeaderBytes+name.Length;
        var frame=NewFrame(WireOpcode.PlayerIdentity,bytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.ReceiverMapEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(16,8),value.ActorId);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(24,8),value.ConnectionEpoch);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(32,8),value.EntityMapEpoch);
        frame[40]=(byte)value.Mode;
        frame[41]=value.RedMan?(byte)1:(byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(42,8),value.ServerUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(50,8),value.PinkUntilUnixMilliseconds);
        BinaryPrimitives.WriteInt64LittleEndian(
            frame.AsSpan(58,8),value.PvpUpdatedUnixMilliseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(66,2),(ushort)name.Length);
        name.AsSpan().CopyTo(frame.AsSpan(PlayerIdentityHeaderBytes));
        return frame;
    }

    public static bool TryDecodePlayerIdentity(
        ReadOnlySpan<byte> frame,out PlayerIdentityWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.PlayerIdentity||
           frame.Length<PlayerIdentityHeaderBytes+1)
            return false;
        long receiver=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        long actor=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(16,8));
        long connection=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(24,8));
        long entityMap=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(32,8));
        var mode=(PvpMode)frame[40];
        byte red=frame[41];
        long serverUnix=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(42,8));
        long pinkUntil=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(50,8));
        long updated=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(58,8));
        int nameLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(66,2));
        if(receiver<=0||actor<=0||connection<=0||entityMap<=0||
           mode is not (PvpMode.ProtectedBlue or PvpMode.EnabledWhite)||
           red>1||serverUnix<=0||pinkUntil<0||updated<0||
           nameLength is <1 or >PlayerIdentityMaxNameBytes||
           frame.Length!=PlayerIdentityHeaderBytes+nameLength)
            return false;
        try
        {
            string name=StrictUtf8.GetString(
                frame.Slice(PlayerIdentityHeaderBytes,nameLength));
            if(string.IsNullOrWhiteSpace(name))return false;
            value=new(receiver,actor,connection,entityMap,mode,red==1,
                serverUnix,pinkUntil,updated,name);
            return true;
        }
        catch(DecoderFallbackException)
        {
            return false;
        }
    }

    public static byte[] EncodeQuestOpen(QuestOpenWire value)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.QuestOpen,QuestOpenFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        return frame;
    }

    public static bool TryDecodeQuestOpen(
        ReadOnlySpan<byte> frame,out QuestOpenWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestOpen)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)));
        return value.MapEpoch>0&&value.QuestSequence>0&&value.NpcSpawnId>0;
    }

    public static byte[] EncodeQuestChoose(QuestChooseWire value)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0||
           value.QuestId is <=0 or >100000000||
           value.ChoiceId is <=0 or >100000000)
            throw new ArgumentOutOfRangeException(nameof(value));
        var frame=NewFrame(WireOpcode.QuestChoose,QuestChooseFrameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(24,4),value.QuestId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(28,4),value.ChoiceId);
        return frame;
    }

    public static bool TryDecodeQuestChoose(
        ReadOnlySpan<byte> frame,out QuestChooseWire value)
    {
        value=default;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestChoose)
            return false;
        value=new(
            BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8)),
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(20,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(24,4)),
            BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(28,4)));
        return value.MapEpoch>0&&value.QuestSequence>0&&value.NpcSpawnId>0&&
            value.QuestId is >0 and <=100000000&&
            value.ChoiceId is >0 and <=100000000;
    }

    public static byte[] EncodeQuestView(QuestViewWire value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var encoded=EncodeQuestViewStrings(value);
        int frameBytes=checked(QuestViewHeaderBytes+
            encoded.Title.Length+encoded.Text.Length+
            encoded.Choices.Sum(x=>6+x.Label.Length));
        if(frameBytes>QuestViewMaximumFrameBytes||
           !ValidQuestView(value,encoded.Title,encoded.Text,encoded.Choices))
            throw new ArgumentOutOfRangeException(nameof(value));

        var frame=NewFrame(WireOpcode.QuestView,frameBytes);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8,8),value.MapEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(16,4),value.QuestSequence);
        frame[20]=(byte)value.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(21,4),value.NpcSpawnId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(25,4),value.QuestId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(29,4),value.RuleRevision);
        frame[33]=(byte)value.State;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(34,4),value.Stage);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(38,4),value.ProgressRevision);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(42,2),(ushort)encoded.Title.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(44,2),(ushort)encoded.Text.Length);
        frame[46]=(byte)encoded.Choices.Count;
        int offset=QuestViewHeaderBytes;
        encoded.Title.AsSpan().CopyTo(frame.AsSpan(offset)); offset+=encoded.Title.Length;
        encoded.Text.AsSpan().CopyTo(frame.AsSpan(offset)); offset+=encoded.Text.Length;
        foreach(var choice in encoded.Choices)
        {
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(offset,4),choice.Id);
            BinaryPrimitives.WriteUInt16LittleEndian(
                frame.AsSpan(offset+4,2),(ushort)choice.Label.Length);
            offset+=6;
            choice.Label.AsSpan().CopyTo(frame.AsSpan(offset));
            offset+=choice.Label.Length;
        }
        return frame;
    }

    public static bool TryDecodeQuestView(
        ReadOnlySpan<byte> frame,out QuestViewWire? value)
    {
        value=null;
        if(!TryReadHeader(frame,out var h)||h.Opcode!=WireOpcode.QuestView||
           frame.Length<QuestViewHeaderBytes)
            return false;
        long epoch=BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(8,8));
        uint sequence=BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(16,4));
        var status=(QuestViewWireStatus)frame[20];
        int npc=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(21,4));
        int quest=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(25,4));
        int rule=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(29,4));
        var state=(QuestProgressState)frame[33];
        int stage=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(34,4));
        int progress=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(38,4));
        int titleLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(42,2));
        int textLength=BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(44,2));
        int count=frame[46];
        if(titleLength>QuestViewMaxTitleBytes||textLength>QuestViewMaxTextBytes||
           count>QuestViewMaxChoices)
            return false;
        int offset=QuestViewHeaderBytes;
        if(offset+titleLength+textLength>frame.Length)return false;
        try
        {
            string title=StrictUtf8.GetString(frame.Slice(offset,titleLength));
            offset+=titleLength;
            string text=StrictUtf8.GetString(frame.Slice(offset,textLength));
            offset+=textLength;
            var choices=new List<QuestViewChoiceWire>(count);
            var seen=new HashSet<int>();
            for(int i=0;i<count;i++)
            {
                if(offset+6>frame.Length)return false;
                int id=BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(offset,4));
                int labelLength=BinaryPrimitives.ReadUInt16LittleEndian(
                    frame.Slice(offset+4,2));
                offset+=6;
                if(id<=0||labelLength<1||labelLength>QuestViewMaxLabelBytes||
                   offset+labelLength>frame.Length||!seen.Add(id))
                    return false;
                string label=StrictUtf8.GetString(frame.Slice(offset,labelLength));
                offset+=labelLength;
                choices.Add(new(id,label));
            }
            if(offset!=frame.Length)return false;
            var candidate=new QuestViewWire(
                epoch,sequence,status,npc,quest,rule,state,stage,progress,
                title,text,choices);
            var encoded=EncodeQuestViewStrings(candidate);
            if(!ValidQuestView(candidate,encoded.Title,encoded.Text,encoded.Choices))
                return false;
            value=candidate;
            return true;
        }
        catch(DecoderFallbackException)
        {
            return false;
        }
    }

    private static (
        byte[] Title,byte[] Text,
        IReadOnlyList<(int Id,byte[] Label)> Choices)
        EncodeQuestViewStrings(QuestViewWire value)
    {
        byte[] title=StrictUtf8.GetBytes(value.Title??string.Empty);
        byte[] text=StrictUtf8.GetBytes(value.Text??string.Empty);
        var choices=new List<(int Id,byte[] Label)>(value.Choices?.Count??0);
        if(value.Choices is not null)
            foreach(var choice in value.Choices)
                choices.Add((choice.ChoiceId,
                    StrictUtf8.GetBytes(choice.Label??string.Empty)));
        return(title,text,choices);
    }

    private static bool ValidQuestView(
        QuestViewWire value,byte[] title,byte[] text,
        IReadOnlyList<(int Id,byte[] Label)> choices)
    {
        if(value.MapEpoch<=0||value.QuestSequence==0||value.NpcSpawnId<=0||
           (byte)value.Status>(byte)QuestViewWireStatus.RuleRevisionMismatch)
            return false;
        bool hasView=value.Status is
            QuestViewWireStatus.Ready or QuestViewWireStatus.Applied or
            QuestViewWireStatus.AlreadyApplied or QuestViewWireStatus.InvalidChoice or
            QuestViewWireStatus.AlreadyCompleted;
        if(!hasView)
            return value.QuestId==0&&value.RuleRevision==0&&
                value.State==QuestProgressState.NotStarted&&value.Stage==0&&
                value.ProgressRevision==0&&title.Length==0&&text.Length==0&&
                choices.Count==0;
        if(value.QuestId is <=0 or >100000000||value.RuleRevision<=0||
           value.Stage is <0 or >1000000||value.ProgressRevision<0||
           (byte)value.State>(byte)QuestProgressState.Completed||
           title.Length is <1 or >QuestViewMaxTitleBytes||
           text.Length is <1 or >QuestViewMaxTextBytes||
           choices.Count>QuestViewMaxChoices)
            return false;
        if(value.State==QuestProgressState.NotStarted&&value.ProgressRevision!=0)
            return false;
        if(value.State!=QuestProgressState.NotStarted&&value.ProgressRevision<=0)
            return false;
        if(value.State==QuestProgressState.Completed&&choices.Count!=0)
            return false;
        var seen=new HashSet<int>();
        foreach(var choice in choices)
            if(choice.Id is <=0 or >100000000||
               choice.Label.Length is <1 or >QuestViewMaxLabelBytes||
               !seen.Add(choice.Id))
                return false;
        return true;
    }

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
            15 => WireOpcode.CharacterCreate, 16 => WireOpcode.CharacterCreateResult,
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
            WireOpcode.CharacterList => advertised is >= 9 and <= MaxCharacterListFrameBytes,
            WireOpcode.CharacterChoose => advertised == CharacterChooseFrameBytes,
            WireOpcode.CharacterChooseResult => advertised == CharacterChooseResultFrameBytes,
            WireOpcode.CharacterCreate => advertised is >= 12 and <= MaxCharacterCreateFrameBytes,
            WireOpcode.CharacterCreateResult => advertised is >= 20 and <= MaxCharacterCreateResultFrameBytes,
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
