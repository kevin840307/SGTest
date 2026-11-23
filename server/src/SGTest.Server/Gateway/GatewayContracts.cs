using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Text;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

// Public trusted command and result contracts shared by the authenticated
// gateway, domain handlers and reliable reply router. Protocol wire DTOs
// remain owned by WireProtocol; these types never decode user-supplied identity.
public sealed record GatewayConnectionOptions(
    int MaxBufferedInputBytes = WireProtocol.MaximumFrameBytes,
    int MaxReliableFrames = 256,
    int MaxReliableBytes = 256 * 1024);

public readonly record struct ConnectionBinding(
    long ActorId,
    long ConnectionEpoch,
    long MapEpoch)
{
    public static ConnectionBinding FromTrustedSnapshot(ActorSnapshot actor)
    {
        if (actor.ActorId <= 0 || actor.ConnectionEpoch <= 0 || actor.MapEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(actor),
                "Trusted actor binding requires positive identity and generations");
        return new(actor.ActorId, actor.ConnectionEpoch, actor.MapEpoch);
    }
}

public readonly record struct TrustedNpcInteraction(
    long ActorId, long ConnectionEpoch, long RequestMapEpoch,
    uint InteractionSequence, int NpcSpawnId);

public readonly record struct TrustedWorldResyncRequest(
    long ActorId, long ConnectionEpoch, long MapEpoch);

public readonly record struct TrustedCombatProfile(
    int Attack, int Defense, int Hit, int Dodge,
    double AttackRange, int AttackCooldownTicks);

public readonly record struct TrustedNormalAttack(
    long ActorId, long ConnectionEpoch, long RequestMapEpoch,
    uint AttackSequence, long TargetEntityId,
    TrustedCombatProfile? Profile);

public readonly record struct TrustedSkillUse(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint SkillSequence,int SkillId,long TargetEntityId,
    TrustedCombatProfile? Profile,
    SkillTargetKind? ExpectedTargetKind = null);

public readonly record struct TrustedPlayerRespawn(
    long ActorId, long ConnectionEpoch, long RequestMapEpoch,
    uint RespawnSequence);
public readonly record struct TrustedGroundPickup(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint PickupSequence,long DropId);
public readonly record struct TrustedRecoveryUse(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint UseSequence,long ItemUid);
public readonly record struct TrustedInventoryDiscard(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint DiscardSequence,long ItemUid,int Quantity);
public enum TrustedShopCommandKind : byte { Open=1, Buy=2, Sell=3 }
public readonly record struct TrustedShopCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint ShopSequence,int NpcSpawnId,TrustedShopCommandKind Kind,
    int ItemDefinitionId=0,long ItemUid=0,int Quantity=0);
public enum TrustedStorageCommandKind : byte { Open=1, Deposit=2, Withdraw=3 }
public readonly record struct TrustedStorageCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint StorageSequence,int NpcSpawnId,TrustedStorageCommandKind Kind,
    long ItemUid=0);
public enum TrustedEquipmentCommandKind : byte { Equip=1, Unequip=2 }
public readonly record struct TrustedEquipmentCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint EquipmentSequence,TrustedEquipmentCommandKind Kind,long ItemUid);
public enum TrustedBankCommandKind : byte { Open=1, Deposit=2, Withdraw=3 }
public readonly record struct TrustedBankCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint BankSequence,int NpcSpawnId,TrustedBankCommandKind Kind,long Amount=0);
public enum TrustedRepairCommandKind : byte { Open=1, Repair=2 }
public readonly record struct TrustedRepairCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint RepairSequence,int NpcSpawnId,TrustedRepairCommandKind Kind,long ItemUid=0);
public enum TrustedEnhancementCommandKind : byte { Open=1, Enhance=2 }
public readonly record struct TrustedEnhancementCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint EnhancementSequence,int NpcSpawnId,
    TrustedEnhancementCommandKind Kind,long ItemUid=0);
public readonly record struct TrustedTradeCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint TradeSequence,TradeCommandAction Action,long TradeId,
    long TargetActorId,long ItemUid,long Money);
public readonly record struct TrustedPvpModeCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint PvpSequence,PvpModeCommandAction Action);
public readonly record struct TrustedPvpAttack(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint AttackSequence,long TargetActorId,TrustedCombatProfile? Profile);
public readonly record struct TrustedPvpSkill(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint SkillSequence,int SkillId,long TargetActorId,
    TrustedCombatProfile? Profile);
public readonly record struct TrustedGmPvpNameCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint GmSequence,long TargetActorId,PvpNameState State,
    string? OperatorId);
public readonly record struct TrustedPlayerShopCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint ShopSequence,PlayerShopCommandAction Action,
    long SellerActorId,long ListingId,long ItemUid,long Price);
public readonly record struct TrustedChatCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint ChatSequence,ChatChannel Channel,byte BubbleType,string Message,
    long TargetActorId=0,string TargetName="");
public readonly record struct TrustedPartyCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint PartySequence,PartyCommandAction Action,long PartyId,long TargetActorId,
    string TargetName = "");
public readonly record struct TrustedClanCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint ClanSequence,ClanCommandAction Action,long ClanId,string Name = "",
    long TargetActorId = 0);
public readonly record struct TrustedPetCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint PetSequence,PetCommandAction Action,long PetUid);
public readonly record struct TrustedPetAttack(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint AttackSequence,long PetUid,long TargetEntityId);
public enum TrustedQuestCommandKind : byte { Open=1, Choose=2 }
public readonly record struct TrustedQuestCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint QuestSequence,int NpcSpawnId,TrustedQuestCommandKind Kind,
    int QuestId=0,int ChoiceId=0);

public readonly record struct NormalAttackOutcome(
    NormalAttackStatus Status, long ServerTick, long TargetEntityId,
    int Damage, int CurrentHp, int MaxHp, long TargetRevision,
    MonsterSnapshot? Monster = null);

public readonly record struct SkillHitOutcome(
    MonsterSnapshot Monster,int Damage);

public readonly record struct SkillUseOutcome(
    SkillUseStatus Status,long ServerTick,int SkillId,long TargetEntityId,
    int Damage,int CurrentHp,int MaxHp,long TargetRevision,
    MonsterSnapshot? Monster = null,
    IReadOnlyList<SkillHitOutcome>? Hits = null);

public enum GatewaySendStatus
{
    Enqueued,
    GenerationMismatch,
    Backpressured,
    Closed
}
