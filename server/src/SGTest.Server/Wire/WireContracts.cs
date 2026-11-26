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

// Stable public protocol values and payload contracts. Keep opcode numbers unchanged.
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
    AccountRegisterRequest = 17,
    AccountRegisterResult = 18,
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
    ChatDelivery = 93,
    EnhancementOpen = 94,
    EnhancementSnapshot = 95,
    EnhancementItem = 96,
    EnhancementResult = 97,
    WhisperCommand = 98,
    WhisperResult = 99,
    PartyCommand = 100,
    PartyResult = 101,
    PartyInvite = 102,
    PartySnapshot = 103,
    SkillBookSnapshot = 104,
    CharacterManage = 105,
    CharacterManageResult = 106,
    ClanCommand = 107,
    ClanResult = 108,
    ClanSnapshot = 109,
    PlayerMovementIntent = 110,
    PetCommand = 111,
    PetResult = 112,
    PetSnapshot = 113,
    PetEntitySpawn = 114,
    PetEntityDespawn = 115,
    PetEntityDelta = 116,
    PetAttack = 117,
    PetAttackResult = 118,
    PetAttackVisual = 119,
    FriendCommand = 120,
    FriendResult = 121,
    FriendSnapshot = 122,
    MechaCommand = 123,
    MechaResult = 124,
    MechaSnapshot = 125,
    MechaPresentation = 126,
    PlayerShopSnapshot = 127,
    MechaEquipmentSnapshot = 128,
    PetSkill = 129,
    PetSkillResult = 130,
    PetSkillVisual = 131,
    PetEquipmentSnapshot = 132,
    PlayerProfileState = 133,
    PlayerCreationProfileState = 134,
    PlayerAttributeAllocate = 135,
    FriendInvite = 136,
    EmotionCommand = 137,
    EmotionVisual = 138,
    FriendMessageCommand = 139,
    HotkeyCommand = 140,
    HotkeySnapshot = 141
}

public enum ChatChannel : byte { Nearby = 0, World = 1, Warband = 2, Party = 3, Whisper = 4, Emotion = 5, FriendMessage = 6 }
public enum WhisperResultStatus : byte
{
    Delivered = 0,
    TargetUnavailable = 1,
    SelfTarget = 2
}
public enum ClanCommandAction : byte
{
    Create=1,Join=2,Leave=3,Refresh=4,SetAnnouncement=5,Kick=6,TransferLeadership=7
}
public enum ClanResultStatus : byte
{
    Success=0,AlreadyInClan=1,NotInClan=2,ClanNotFound=3,
    NameTaken=4,InvalidName=5,ClanFull=6,Conflict=7,
    NotLeader=8,TargetNotMember=9,CannotTargetSelf=10,InvalidAnnouncement=11
}
public enum FriendCommandAction : byte { Add=1, Remove=2, Refresh=3, Accept=4, Reject=5, Ignore=6, Restore=7, Rename=8 }
public enum FriendResultStatus : byte
{
    Success=0,TargetNotFound=1,CannotTargetSelf=2,
    AlreadyFriends=3,NotFriends=4,Conflict=5,
    TargetUnavailable=6,InviteMissing=7,TargetHasPendingInvite=8
}
public enum FriendInviteState : byte { Request=0, Accepted=1, Rejected=2, Cancelled=3 }
public enum HotkeyCommandAction : byte { Save=1, Remove=2, Refresh=3 }
public enum HotkeyDataType : byte { Item=0, Skill=1 }
public enum HotkeyResultStatus : byte { Applied=0, InvalidBinding=1 }
public enum PetCommandAction : byte { Summon=1, Recall=2, Refresh=3, Equip=4, Unequip=5 }
public enum PetResultStatus : byte
{
    Applied=0,AlreadyProcessed=1,PetNotOwned=2,
    AlreadyActive=3,NotActive=4,SequenceConflict=5,
    ItemNotOwned=6,InvalidEquipment=7,LevelTooLow=8,
    NotEquipped=9,RulesUnavailable=10
}
public enum MechaCommandAction : byte
{
    Mount=1,Dismount=2,Refresh=3,Equip=4,Unequip=5,ChargeBattery=6
}
public enum MechaResultStatus : byte
{
    Applied=0,AlreadyProcessed=1,MechaNotOwned=2,
    AlreadyMounted=3,NotMounted=4,SequenceConflict=5,
    ItemNotOwned=6,InvalidEquipment=7,LevelTooLow=8,
    MountedLocked=9,NotEquipped=10,CorePartCannotUnequip=11,
    CardSlotsFull=12,RulesUnavailable=13,
    BatteryIncompatible=14,BatteryAlreadyFull=15
}

public enum PartyCommandAction : byte
{
    Invite = 1, Accept = 2, Reject = 3, Leave = 4, Kick = 5, Promote = 6,
    Create = 7
}
public enum PartyResultStatus : byte
{
    Success = 0, TargetUnavailable = 1, CannotTargetSelf = 2,
    AlreadyInParty = 3, NotInParty = 4, NotLeader = 5, PartyFull = 6,
    InviteMissing = 7, InviteStale = 8, TargetNotMember = 9,
    TargetHasPendingInvite = 10
}

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
public enum AccountRegisterWireStatus : byte
{
    Created = 0,
    Unavailable = 1,
    UsernameTaken = 2,
    InvalidInput = 3,
    RateLimited = 4
}
public enum CharacterChooseWireStatus : byte
{
    Success = 0, NotOwned = 1, WorldRejected = 2,
    PasswordRequired = 3, InvalidPassword = 4, PasswordLocked = 5
}
public enum CharacterManageAction : byte { ChangePassword = 1, Delete = 2 }
public enum CharacterManageStatus : byte
{
    Success = 0, NotOwned = 1, InvalidPassword = 2, PasswordLocked = 3,
    InvalidInput = 4, Conflict = 5, HasHistory = 6
}
public enum CharacterCreateWireStatus : byte
{
    Success = 0,
    SlotOccupied = 1,
    NameTaken = 2,
    LimitReached = 3,
    InvalidName = 4,
    InvalidSlot = 5,
    Unavailable = 6,
    InvalidAppearance = 7,
    InvalidCreationSettings = 8
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

public enum EnhancementOpenStatus : byte
{
    Opened = 0,
    StaleGeneration = 1,
    UnknownEnhancer = 2,
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
    StaleSequence = 15,
    CapacityReached = 16
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
    int Damage,bool Died,byte Facing,NormalAttackStatus Status);
public readonly record struct PlayerRespawnVisualWire(
    long ReceiverMapEpoch,long ServerTick,long ActorId,
    long ConnectionEpoch,uint RespawnSequence,byte Facing);
public readonly record struct PlayerCombatStateWire(
    long MapEpoch, int CurrentHp, int MaxHp, int CurrentMp, int MaxMp,
    bool CombatEnabled = true);

[Flags]
public enum PlayerProfileStatVerification : byte
{
    None = 0,
    Attack = 1 << 0,
    Defense = 1 << 1,
    Hit = 1 << 2,
    Dodge = 1 << 3,
    All = Attack | Defense | Hit | Dodge
}

public readonly record struct PlayerProfileStateWire(
    long CharacterId, int Race, int Career, int Level, long Experience,
    int Strength, int Dexterity, int Constitution, int Wisdom,
    int Attack, int Defense, int Hit, int Dodge,
    int AvailableAttributePoints,
    PlayerProfileStatVerification VerifiedStats = PlayerProfileStatVerification.All,
    int ExperiencePercent = -1);
public readonly record struct PlayerAttributeAllocateWire(
    uint AttributeSequence, CharacterAttributeKind Attribute);
public readonly record struct PlayerCreationProfileStateWire(
    long CharacterId, int Strength, int Dexterity, int Constitution, int Wisdom);
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
public readonly record struct AccountRegisterRequestWire(string Username, string Password);
public readonly record struct AccountRegisterResultWire(AccountRegisterWireStatus Status);
public readonly record struct CharacterSummaryWire(long CharacterId, byte Slot, string Name,
    CharacterAppearance? Appearance = null);
public sealed record CharacterListWire(IReadOnlyList<CharacterSummaryWire> Characters,
    IReadOnlyList<CharacterAppearance>? CreationAppearances = null);
public readonly record struct CharacterCreationWire(
    byte Strength, byte Dexterity, byte Constitution, byte Wisdom, string CharacterPassword)
{
    public override string ToString() =>
        $"CharacterCreationWire {{ Strength = {Strength}, Dexterity = {Dexterity}, Constitution = {Constitution}, Wisdom = {Wisdom}, Password = [REDACTED] }}";
}
public readonly record struct CharacterCreateWire(
    byte Slot, string Name, CharacterAppearance? Appearance = null,
    CharacterCreationWire? Creation = null);
public readonly record struct CharacterCreateResultWire(
    CharacterCreateWireStatus Status, long CharacterId, byte Slot, string Name,
    CharacterAppearance? Appearance = null);
public readonly record struct CharacterChooseWire(long CharacterId, string? CharacterPassword = null)
{
    public override string ToString() => $"CharacterChooseWire {{ CharacterId = {CharacterId}, Password = [REDACTED] }}";
}
public readonly record struct CharacterManageWire(
    CharacterManageAction Action, long CharacterId, string? OldPassword, string? NewPassword)
{
    public override string ToString() => $"CharacterManageWire {{ Action = {Action}, CharacterId = {CharacterId}, Passwords = [REDACTED] }}";
}
public readonly record struct CharacterManageResultWire(
    CharacterManageAction Action, CharacterManageStatus Status, long CharacterId);
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
public readonly record struct PlayerMovementIntentWire(
    long ReceiverMapEpoch, long ServerTick, long ActorId,
    long ConnectionEpoch, long EntityMapEpoch, uint MovementActionSequence,
    float TargetX, float TargetY, bool IsMoving, byte Facing);
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
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId,
    int EnhancementLevel = 0,int DurabilityRaw = -1);
public sealed record InventorySnapshotWire(
    long CharacterId,int StartIndex,bool Complete,
    IReadOnlyList<InventoryItemWire> Items);
public readonly record struct SkillBookEntryWire(int SkillId,int Level);
public sealed record SkillBookSnapshotWire(
    long CharacterId,int StartIndex,bool Complete,
    IReadOnlyList<SkillBookEntryWire> Skills);
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
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId,
    int EnhancementLevel = 0,int DurabilityRaw = -1);
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
    long ItemUid,int SlotId,int ItemDefinitionId,long SourceDropId,
    int EnhancementLevel = 0,int DurabilityRaw = -1);
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

public readonly record struct EnhancementOpenWire(
    long MapEpoch,uint EnhancementSequence,int NpcSpawnId);
public readonly record struct EnhancementPreviewItemWire(
    long ItemUid,int ItemDefinitionId,EnhancementItemLocation Location,
    int CurrentLevel,int TargetLevel,int SuccessPpm,long MoneyCost,
    bool ConsumeMoneyOnFailure);
public sealed record EnhancementSnapshotWire(
    long MapEpoch,uint EnhancementSequence,EnhancementOpenStatus Status,
    int NpcSpawnId,long WalletMoney,int StartIndex,bool Complete,
    IReadOnlyList<EnhancementPreviewItemWire> Items);
public readonly record struct EnhancementItemWire(
    long MapEpoch,uint EnhancementSequence,int NpcSpawnId,long ItemUid);
public readonly record struct EnhancementResultWire(
    long MapEpoch,uint EnhancementSequence,EnhancementStatus Status,
    int NpcSpawnId,long ItemUid,int ItemDefinitionId,
    EnhancementItemLocation Location,int PreviousLevel,int TargetLevel,
    int CurrentLevel,bool Succeeded,long MoneyCost,long MoneyCharged,
    long WalletMoney,int RollPpm);

public readonly record struct TradeCommandWire(
    long MapEpoch,uint TradeSequence,TradeCommandAction Action,
    long TradeId,long TargetActorId,long ItemUid,long Money);
public readonly record struct TradeOfferItemWire(
    long ItemUid,int ItemDefinitionId,int Quantity,long SourceDropId,
    int EnhancementLevel = 0,int DurabilityRaw = -1);
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
    long PinkUntilUnixMilliseconds,long PvpUpdatedUnixMilliseconds,string Name,
    CharacterAppearance? Appearance = null);
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
    int DurabilityRaw,long Price,long SellerWallet,long BuyerWallet,
    int EnhancementLevel = 0);
public readonly record struct PlayerShopListingWire(
    long ListingId,long ItemUid,int ItemDefinitionId,int Quantity,
    long SourceDropId,int DurabilityRaw,long Price,int EnhancementLevel);
public sealed record PlayerShopSnapshotWire(
    long MapEpoch,uint ShopSequence,long SellerActorId,long BuyerWallet,
    IReadOnlyList<PlayerShopListingWire> Listings);
public readonly record struct PlayerShopSoldWire(
    long ReceiverMapEpoch,long ListingId,long BuyerActorId,long ItemUid,
    long SellerWallet);
public readonly record struct ChatCommandWire(
    long MapEpoch,uint ChatSequence,ChatChannel Channel,byte BubbleType,string Message);
public readonly record struct ChatDeliveryWire(
    long ReceiverMapEpoch,long SenderActorId,long SenderConnectionEpoch,
    uint ChatSequence,ChatChannel Channel,byte BubbleType,
    string SenderName,string Message);
public readonly record struct EmotionCommandWire(
    long MapEpoch,uint EmotionSequence,byte EmotionId);
public readonly record struct EmotionVisualWire(
    long ReceiverMapEpoch,long ActorId,long ConnectionEpoch,
    uint EmotionSequence,byte EmotionId);
public readonly record struct WhisperCommandWire(
    long MapEpoch,uint ChatSequence,long TargetActorId,byte BubbleType,
    string Message,string TargetName = "");
public readonly record struct FriendMessageCommandWire(
    long MapEpoch,uint ChatSequence,long TargetActorId,string Message);
public readonly record struct WhisperResultWire(
    long MapEpoch,uint ChatSequence,WhisperResultStatus Status,
    long TargetActorId,long TargetConnectionEpoch,string TargetName);
public readonly record struct PartyCommandWire(
    long MapEpoch,uint PartySequence,PartyCommandAction Action,
    long PartyId,long TargetActorId,string TargetName = "");
public readonly record struct PartyResultWire(
    long MapEpoch,uint PartySequence,PartyCommandAction Action,
    PartyResultStatus Status,long PartyId,long TargetActorId);
public readonly record struct PartyInviteWire(
    long ReceiverMapEpoch,long PartyId,uint Revision,long InviterActorId,
    long InviterConnectionEpoch,string InviterName);
public readonly record struct PartyMemberWire(
    long ActorId,long ConnectionEpoch,string Name);
public sealed record PartySnapshotWire(
    long ReceiverMapEpoch,long PartyId,uint Revision,long LeaderActorId,
    IReadOnlyList<PartyMemberWire> Members);

public readonly record struct ClanCommandWire(
    long MapEpoch,uint ClanSequence,ClanCommandAction Action,long ClanId,string Name="",
    long TargetActorId=0);
public readonly record struct ClanResultWire(
    long MapEpoch,uint ClanSequence,ClanCommandAction Action,ClanResultStatus Status,long ClanId);
public readonly record struct ClanMemberWire(long CharacterId,byte Role,string Name);
public sealed record ClanSnapshotWire(
    long ReceiverMapEpoch,long ClanId,uint Revision,long LeaderCharacterId,string Name,
    IReadOnlyList<ClanMemberWire> Members,string Announcement="");

public readonly record struct FriendCommandWire(
    long MapEpoch,uint FriendSequence,FriendCommandAction Action,
    long TargetActorId,string TargetName="");
public readonly record struct FriendResultWire(
    long MapEpoch,uint FriendSequence,FriendCommandAction Action,
    FriendResultStatus Status,long TargetActorId);
public readonly record struct FriendEntryWire(
    long CharacterId,string Name,bool IsOnline,bool IsIgnored=false);
public sealed record FriendSnapshotWire(
    long ReceiverMapEpoch,IReadOnlyList<FriendEntryWire> Friends);
public readonly record struct FriendInviteWire(
    long ReceiverMapEpoch,long ActorId,long ActorConnectionEpoch,
    FriendInviteState State,string Name);

public readonly record struct HotkeyCommandWire(
    long MapEpoch,uint HotkeySequence,HotkeyCommandAction Action,byte Slot,
    HotkeyDataType DataType,long DataId);
public readonly record struct HotkeyEntryWire(
    byte Slot,HotkeyDataType DataType,long DataId);
public sealed record HotkeySnapshotWire(
    long ReceiverMapEpoch,uint HotkeySequence,HotkeyResultStatus Status,
    IReadOnlyList<HotkeyEntryWire> Entries);

public readonly record struct PetCommandWire(
    long MapEpoch,uint PetSequence,PetCommandAction Action,long PetUid,long ItemUid=0);
public readonly record struct PetResultWire(
    long MapEpoch,uint PetSequence,PetCommandAction Action,PetResultStatus Status,
    long PetUid,long ActivePetUid,long ItemUid=0,int SlotId=0,long ReplacedItemUid=0);
public readonly record struct PetSnapshotEntryWire(
    long PetUid,int DefinitionId,int EvolutionId,int Level,long Experience);
public sealed record PetSnapshotWire(
    long ReceiverMapEpoch,long ActivePetUid,IReadOnlyList<PetSnapshotEntryWire> Pets);
public readonly record struct PetEquipmentEntryWire(
    long PetUid,long ItemUid,int SlotId,int ItemDefinitionId,long SourceDropId,
    int EnhancementLevel,int DurabilityRaw);
public sealed record PetEquipmentSnapshotWire(
    long ReceiverMapEpoch,IReadOnlyList<PetEquipmentEntryWire> Items);
public readonly record struct PetEntitySpawnWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,long PetUid,int DefinitionId,int EvolutionId,int Level,float X,float Y);
public readonly record struct PetEntityDeltaWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,long PetUid,float X,float Y);
public readonly record struct PetEntityDespawnWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,long PetUid);
public readonly record struct PetAttackWire(
    long MapEpoch,uint AttackSequence,long PetUid,long TargetEntityId);
public readonly record struct PetAttackResultWire(
    long MapEpoch,uint AttackSequence,NormalAttackStatus Status,long PetUid,
    long TargetEntityId,int Damage,int CurrentHp,int MaxHp,long TargetRevision,long ServerTick);
public readonly record struct PetAttackVisualWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,uint AttackSequence,long PetUid,long TargetEntityId,
    NormalAttackStatus Status,int Damage,long TargetRevision);
public readonly record struct PetSkillWire(
    long MapEpoch,uint SkillSequence,long PetUid,int SkillId,long TargetEntityId);
public readonly record struct PetSkillResultWire(
    long MapEpoch,uint SkillSequence,SkillUseStatus Status,long PetUid,int SkillId,
    long TargetEntityId,int Damage,int CurrentHp,int MaxHp,long TargetRevision,long ServerTick);
public readonly record struct PetSkillVisualWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,uint SkillSequence,long PetUid,int SkillId,long TargetEntityId,
    SkillUseStatus Status,int Damage,long TargetRevision);

public readonly record struct MechaCommandWire(
    long MapEpoch,uint MechaSequence,MechaCommandAction Action,long MechaUid,long ItemUid=0);
public readonly record struct MechaResultWire(
    long MapEpoch,uint MechaSequence,MechaCommandAction Action,MechaResultStatus Status,
    long MechaUid,long MountedMechaUid,long ItemUid=0,int SlotId=0,long ReplacedItemUid=0,
    int RemainingQuantity=-1);
public readonly record struct MechaSnapshotEntryWire(
    long MechaUid,int DefinitionId,int Level,long Experience,
    int BatteryCharge=0,int BatteryCapacity=0);
public sealed record MechaSnapshotWire(
    long ReceiverMapEpoch,long MountedMechaUid,IReadOnlyList<MechaSnapshotEntryWire> Mechas);
public readonly record struct MechaEquipmentEntryWire(
    long MechaUid,long ItemUid,int SlotId,int ItemDefinitionId,long SourceDropId,
    int EnhancementLevel,int DurabilityRaw);
public sealed record MechaEquipmentSnapshotWire(
    long ReceiverMapEpoch,IReadOnlyList<MechaEquipmentEntryWire> Items);
public readonly record struct MechaPresentationWire(
    long ReceiverMapEpoch,long ServerTick,long OwnerActorId,long OwnerConnectionEpoch,
    long OwnerMapEpoch,bool Mounted,long MechaUid,int DefinitionId,int Level);

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
