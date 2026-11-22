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

/// <summary>
/// Trusted inbound V1 command routing and WorldReady/resync ACK gate for the
/// ONE GatewayConnection. Parser, binding and locks remain on the parent
/// connection; this partial has no second socket, queue or mutable world.
/// </summary>
public sealed partial class GatewayConnection
{
    /// <summary>
    /// Single receive-loop entry point. The callback must be nonblocking.
    /// Backpressure retains the complete frame in the parser; caller should stop
    /// socket reads and call ResumeClientIngress after World ingress has capacity.
    /// </summary>
    public WireFeedStatus FeedClientBytes(ReadOnlySpan<byte> bytes,
                                          Func<MoveTargetCommand, bool> postMove,
                                          out int commandsAccepted,
                                          Func<TrustedNpcInteraction, bool>? postNpc = null,
                                          Func<TrustedWorldResyncRequest, bool>? postResync = null,
                                          Func<TrustedNormalAttack, bool>? postAttack = null,
                                          Func<TrustedPlayerRespawn, bool>? postRespawn = null,
                                              Func<TrustedGroundPickup, bool>? postPickup = null,
                                              Func<TrustedRecoveryUse, bool>? postRecovery = null,
                                              Func<TrustedInventoryDiscard, bool>? postDiscard = null,
                                              Func<TrustedSkillUse, bool>? postSkill = null,
                                              Func<TrustedShopCommand, bool>? postShop = null,
                                              Func<TrustedStorageCommand, bool>? postStorage = null,
                                              Func<TrustedEquipmentCommand, bool>? postEquipment = null,
                                              Func<TrustedBankCommand, bool>? postBank = null,
                                              Func<TrustedRepairCommand, bool>? postRepair = null,
                                              Func<TrustedEnhancementCommand, bool>? postEnhancement = null,
                                              Func<TrustedTradeCommand, bool>? postTrade = null,
                                              Func<TrustedQuestCommand, bool>? postQuest = null,
                                              Func<TrustedPvpModeCommand, bool>? postPvp = null,
                                              Func<TrustedPvpAttack, bool>? postPvpAttack = null,
                                              Func<TrustedPvpSkill, bool>? postPvpSkill = null,
                                              Func<TrustedGmPvpNameCommand, bool>? postGmPvpName = null,
                                              Func<TrustedPlayerShopCommand, bool>? postPlayerShop = null,
                                              Func<TrustedChatCommand, bool>? postChat = null,
                                              Func<TrustedPartyCommand, bool>? postParty = null,
                                              Func<TrustedClanCommand, bool>? postClan = null)
    {
        ArgumentNullException.ThrowIfNull(postMove);
        lock (_receiveGate)
        {
            if (IsClosed)
            {
                commandsAccepted = 0;
                return WireFeedStatus.Closed;
            }
            return _parser.Feed(bytes,
                frame => HandleClientFrame(
                    frame, postMove, postNpc, postResync, postAttack, postRespawn,
                    postPickup, postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty, postClan),
                out commandsAccepted);
        }
    }

    public WireFeedStatus ResumeClientIngress(Func<MoveTargetCommand, bool> postMove,
                                              out int commandsAccepted,
                                              Func<TrustedNpcInteraction, bool>? postNpc = null,
                                              Func<TrustedWorldResyncRequest, bool>? postResync = null,
                                              Func<TrustedNormalAttack, bool>? postAttack = null,
                                              Func<TrustedPlayerRespawn, bool>? postRespawn = null,
                                              Func<TrustedGroundPickup, bool>? postPickup = null,
                                              Func<TrustedRecoveryUse, bool>? postRecovery = null,
                                              Func<TrustedInventoryDiscard, bool>? postDiscard = null,
                                              Func<TrustedSkillUse, bool>? postSkill = null,
                                              Func<TrustedShopCommand, bool>? postShop = null,
                                              Func<TrustedStorageCommand, bool>? postStorage = null,
                                              Func<TrustedEquipmentCommand, bool>? postEquipment = null,
                                              Func<TrustedBankCommand, bool>? postBank = null,
                                              Func<TrustedRepairCommand, bool>? postRepair = null,
                                              Func<TrustedEnhancementCommand, bool>? postEnhancement = null,
                                              Func<TrustedTradeCommand, bool>? postTrade = null,
                                              Func<TrustedQuestCommand, bool>? postQuest = null,
                                              Func<TrustedPvpModeCommand, bool>? postPvp = null,
                                              Func<TrustedPvpAttack, bool>? postPvpAttack = null,
                                              Func<TrustedPvpSkill, bool>? postPvpSkill = null,
                                              Func<TrustedGmPvpNameCommand, bool>? postGmPvpName = null,
                                              Func<TrustedPlayerShopCommand, bool>? postPlayerShop = null,
                                              Func<TrustedChatCommand, bool>? postChat = null,
                                              Func<TrustedPartyCommand, bool>? postParty = null,
                                              Func<TrustedClanCommand, bool>? postClan = null)
    {
        ArgumentNullException.ThrowIfNull(postMove);
        lock (_receiveGate)
        {
            if (IsClosed)
            {
                commandsAccepted = 0;
                return WireFeedStatus.Closed;
            }
            return _parser.Resume(
                frame => HandleClientFrame(
                    frame, postMove, postNpc, postResync, postAttack, postRespawn,
                    postPickup, postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty, postClan),
                out commandsAccepted);
        }
    }

    private WireFrameDisposition HandleClientFrame(
        ReadOnlySpan<byte> frame,
        Func<MoveTargetCommand, bool> postMove,
        Func<TrustedNpcInteraction, bool>? postNpc,
        Func<TrustedWorldResyncRequest, bool>? postResync,
        Func<TrustedNormalAttack, bool>? postAttack,
        Func<TrustedPlayerRespawn, bool>? postRespawn,
        Func<TrustedGroundPickup, bool>? postPickup,
        Func<TrustedRecoveryUse, bool>? postRecovery,
        Func<TrustedInventoryDiscard, bool>? postDiscard,
        Func<TrustedSkillUse, bool>? postSkill,
        Func<TrustedShopCommand, bool>? postShop,
        Func<TrustedStorageCommand, bool>? postStorage,
        Func<TrustedEquipmentCommand, bool>? postEquipment,
        Func<TrustedBankCommand, bool>? postBank,
        Func<TrustedRepairCommand, bool>? postRepair,
        Func<TrustedEnhancementCommand, bool>? postEnhancement,
        Func<TrustedTradeCommand, bool>? postTrade,
        Func<TrustedQuestCommand, bool>? postQuest,
        Func<TrustedPvpModeCommand, bool>? postPvp,
        Func<TrustedPvpAttack, bool>? postPvpAttack,
        Func<TrustedPvpSkill, bool>? postPvpSkill,
        Func<TrustedGmPvpNameCommand, bool>? postGmPvpName,
        Func<TrustedPlayerShopCommand, bool>? postPlayerShop,
        Func<TrustedChatCommand, bool>? postChat,
        Func<TrustedPartyCommand, bool>? postParty,
        Func<TrustedClanCommand, bool>? postClan)
    {
        if (!WireProtocol.TryReadHeader(frame, out var header))
            return WireFrameDisposition.ProtocolViolation;

        var bound = Binding;
        if (header.Opcode == WireOpcode.WorldReadyAck &&
            WireProtocol.TryDecodeWorldReadyAck(frame, out var ack))
            return TryAcknowledgeWorldReady(bound, ack)
                ? WireFrameDisposition.AcceptedControl
                : WireFrameDisposition.ProtocolViolation;

        if (header.Opcode == WireOpcode.WorldResyncRequest &&
            WireProtocol.TryDecodeWorldResyncRequest(frame, out var resync) &&
            postResync is not null)
        {
            if (!TryArmResync(bound, resync))
                return WireFrameDisposition.ProtocolViolation;
            var request = new TrustedWorldResyncRequest(
                bound.ActorId, bound.ConnectionEpoch, resync.MapEpoch);
            if (!postResync(request))
            {
                CancelArmedResync(bound);
                return WireFrameDisposition.Backpressured;
            }
            return WireFrameDisposition.AcceptedControl;
        }


        if(postShop is not null && header.Opcode==WireOpcode.ShopOpen &&
            WireProtocol.TryDecodeShopOpen(frame,out var shopOpen))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postShop(new TrustedShopCommand(
                bound.ActorId,bound.ConnectionEpoch,shopOpen.MapEpoch,
                shopOpen.ShopSequence,shopOpen.NpcSpawnId,
                TrustedShopCommandKind.Open))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }
        if(postShop is not null && header.Opcode==WireOpcode.ShopBuy &&
            WireProtocol.TryDecodeShopBuy(frame,out var shopBuy))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postShop(new TrustedShopCommand(
                bound.ActorId,bound.ConnectionEpoch,shopBuy.MapEpoch,
                shopBuy.ShopSequence,shopBuy.NpcSpawnId,
                TrustedShopCommandKind.Buy,shopBuy.ItemDefinitionId,0,
                shopBuy.Quantity))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }
        if(postShop is not null && header.Opcode==WireOpcode.ShopSell &&
            WireProtocol.TryDecodeShopSell(frame,out var shopSell))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postShop(new TrustedShopCommand(
                bound.ActorId,bound.ConnectionEpoch,shopSell.MapEpoch,
                shopSell.ShopSequence,shopSell.NpcSpawnId,
                TrustedShopCommandKind.Sell,shopSell.ItemDefinitionId,
                shopSell.ItemUid,shopSell.Quantity))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if(postStorage is not null && header.Opcode==WireOpcode.StorageOpen &&
            WireProtocol.TryDecodeStorageOpen(frame,out var storageOpen))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postStorage(new TrustedStorageCommand(
                bound.ActorId,bound.ConnectionEpoch,storageOpen.MapEpoch,
                storageOpen.StorageSequence,storageOpen.NpcSpawnId,
                TrustedStorageCommandKind.Open))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postStorage is not null && header.Opcode==WireOpcode.StorageDeposit &&
            WireProtocol.TryDecodeStorageDeposit(frame,out var storageDeposit))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postStorage(new TrustedStorageCommand(
                bound.ActorId,bound.ConnectionEpoch,storageDeposit.MapEpoch,
                storageDeposit.StorageSequence,storageDeposit.NpcSpawnId,
                TrustedStorageCommandKind.Deposit,storageDeposit.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postStorage is not null && header.Opcode==WireOpcode.StorageWithdraw &&
            WireProtocol.TryDecodeStorageWithdraw(frame,out var storageWithdraw))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postStorage(new TrustedStorageCommand(
                bound.ActorId,bound.ConnectionEpoch,storageWithdraw.MapEpoch,
                storageWithdraw.StorageSequence,storageWithdraw.NpcSpawnId,
                TrustedStorageCommandKind.Withdraw,storageWithdraw.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(postEquipment is not null && header.Opcode==WireOpcode.EquipmentEquip &&
            WireProtocol.TryDecodeEquipmentEquip(frame,out var equipmentEquip))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postEquipment(new TrustedEquipmentCommand(
                bound.ActorId,bound.ConnectionEpoch,equipmentEquip.MapEpoch,
                equipmentEquip.EquipmentSequence,TrustedEquipmentCommandKind.Equip,
                equipmentEquip.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postEquipment is not null && header.Opcode==WireOpcode.EquipmentUnequip &&
            WireProtocol.TryDecodeEquipmentUnequip(frame,out var equipmentUnequip))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postEquipment(new TrustedEquipmentCommand(
                bound.ActorId,bound.ConnectionEpoch,equipmentUnequip.MapEpoch,
                equipmentUnequip.EquipmentSequence,TrustedEquipmentCommandKind.Unequip,
                equipmentUnequip.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(postBank is not null && header.Opcode==WireOpcode.BankOpen &&
            WireProtocol.TryDecodeBankOpen(frame,out var bankOpen))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postBank(new TrustedBankCommand(
                bound.ActorId,bound.ConnectionEpoch,bankOpen.MapEpoch,
                bankOpen.BankSequence,bankOpen.NpcSpawnId,TrustedBankCommandKind.Open))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postBank is not null && header.Opcode==WireOpcode.BankDeposit &&
            WireProtocol.TryDecodeBankDeposit(frame,out var bankDeposit))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postBank(new TrustedBankCommand(
                bound.ActorId,bound.ConnectionEpoch,bankDeposit.MapEpoch,
                bankDeposit.BankSequence,bankDeposit.NpcSpawnId,
                TrustedBankCommandKind.Deposit,bankDeposit.Amount))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postBank is not null && header.Opcode==WireOpcode.BankWithdraw &&
            WireProtocol.TryDecodeBankWithdraw(frame,out var bankWithdraw))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postBank(new TrustedBankCommand(
                bound.ActorId,bound.ConnectionEpoch,bankWithdraw.MapEpoch,
                bankWithdraw.BankSequence,bankWithdraw.NpcSpawnId,
                TrustedBankCommandKind.Withdraw,bankWithdraw.Amount))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(postRepair is not null && header.Opcode==WireOpcode.RepairOpen &&
            WireProtocol.TryDecodeRepairOpen(frame,out var repairOpen))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postRepair(new TrustedRepairCommand(
                bound.ActorId,bound.ConnectionEpoch,repairOpen.MapEpoch,
                repairOpen.RepairSequence,repairOpen.NpcSpawnId,
                TrustedRepairCommandKind.Open))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postRepair is not null && header.Opcode==WireOpcode.RepairItem &&
            WireProtocol.TryDecodeRepairItem(frame,out var repairItem))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postRepair(new TrustedRepairCommand(
                bound.ActorId,bound.ConnectionEpoch,repairItem.MapEpoch,
                repairItem.RepairSequence,repairItem.NpcSpawnId,
                TrustedRepairCommandKind.Repair,repairItem.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(postEnhancement is not null &&
           header.Opcode==WireOpcode.EnhancementOpen &&
           WireProtocol.TryDecodeEnhancementOpen(frame,out var enhancementOpen))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postEnhancement(new TrustedEnhancementCommand(
                bound.ActorId,bound.ConnectionEpoch,enhancementOpen.MapEpoch,
                enhancementOpen.EnhancementSequence,enhancementOpen.NpcSpawnId,
                TrustedEnhancementCommandKind.Open))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(postEnhancement is not null &&
           header.Opcode==WireOpcode.EnhancementItem &&
           WireProtocol.TryDecodeEnhancementItem(frame,out var enhancementItem))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postEnhancement(new TrustedEnhancementCommand(
                bound.ActorId,bound.ConnectionEpoch,enhancementItem.MapEpoch,
                enhancementItem.EnhancementSequence,enhancementItem.NpcSpawnId,
                TrustedEnhancementCommandKind.Enhance,enhancementItem.ItemUid))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.RecoveryUse && postRecovery is not null &&
            WireProtocol.TryDecodeRecoveryUse(frame,out var recovery))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postRecovery(new TrustedRecoveryUse(
                bound.ActorId,bound.ConnectionEpoch,recovery.MapEpoch,
                recovery.UseSequence,recovery.ItemUid))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.InventoryDiscard && postDiscard is not null &&
            WireProtocol.TryDecodeInventoryDiscard(frame,out var discard))
        {
            if(!GameplayReady(bound))return WireFrameDisposition.ProtocolViolation;
            return postDiscard(new TrustedInventoryDiscard(
                bound.ActorId,bound.ConnectionEpoch,discard.MapEpoch,
                discard.DiscardSequence,discard.ItemUid,discard.Quantity))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.GroundPickup && postPickup is not null &&
            WireProtocol.TryDecodeGroundPickup(frame,out var pickup))
        {
            if (!GameplayReady(bound)) return WireFrameDisposition.ProtocolViolation;
            return postPickup(new TrustedGroundPickup(
                bound.ActorId,bound.ConnectionEpoch,pickup.MapEpoch,
                pickup.PickupSequence,pickup.DropId))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.PlayerRespawn && postRespawn is not null &&
            WireProtocol.TryDecodePlayerRespawn(frame, out var respawn))
        {
            if (!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            var request = new TrustedPlayerRespawn(
                bound.ActorId, bound.ConnectionEpoch, respawn.MapEpoch,
                respawn.RespawnSequence);
            return postRespawn(request)
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.SkillUse && postSkill is not null &&
            WireProtocol.TryDecodeSkillUse(frame,out var skill))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postSkill(new TrustedSkillUse(
                bound.ActorId,bound.ConnectionEpoch,skill.MapEpoch,
                skill.SkillSequence,skill.SkillId,skill.TargetEntityId,
                _combatProfile))
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.QuestOpen && postQuest is not null &&
            WireProtocol.TryDecodeQuestOpen(frame,out var questOpen))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postQuest(new TrustedQuestCommand(
                bound.ActorId,bound.ConnectionEpoch,questOpen.MapEpoch,
                questOpen.QuestSequence,questOpen.NpcSpawnId,
                TrustedQuestCommandKind.Open))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(header.Opcode==WireOpcode.QuestChoose && postQuest is not null &&
            WireProtocol.TryDecodeQuestChoose(frame,out var questChoose))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postQuest(new TrustedQuestCommand(
                bound.ActorId,bound.ConnectionEpoch,questChoose.MapEpoch,
                questChoose.QuestSequence,questChoose.NpcSpawnId,
                TrustedQuestCommandKind.Choose,
                questChoose.QuestId,questChoose.ChoiceId))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }
        if(header.Opcode==WireOpcode.PvpModeCommand && postPvp is not null &&
            WireProtocol.TryDecodePvpModeCommand(frame,out var pvp))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postPvp(new TrustedPvpModeCommand(
                bound.ActorId,bound.ConnectionEpoch,pvp.MapEpoch,
                pvp.PvpSequence,pvp.Action))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.PlayerShopCommand &&
            postPlayerShop is not null &&
            WireProtocol.TryDecodePlayerShopCommand(frame,out var playerShop))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postPlayerShop(new TrustedPlayerShopCommand(
                bound.ActorId,bound.ConnectionEpoch,playerShop.MapEpoch,
                playerShop.ShopSequence,playerShop.Action,
                playerShop.SellerActorId,playerShop.ListingId,
                playerShop.ItemUid,playerShop.Price))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.ChatCommand &&
            postChat is not null &&
            WireProtocol.TryDecodeChatCommand(frame,out var chat))
        {
            if(!GameplayReady(bound)||chat.MapEpoch!=bound.MapEpoch||
               chat.Channel is not (ChatChannel.Nearby or ChatChannel.Party)||
               chat.ChatSequence<=_lastChatSequence)
                return WireFrameDisposition.ProtocolViolation;
            var request=new TrustedChatCommand(
                bound.ActorId,bound.ConnectionEpoch,chat.MapEpoch,
                chat.ChatSequence,chat.Channel,chat.BubbleType,chat.Message);
            if(!postChat(request))
                return WireFrameDisposition.Backpressured;
            _lastChatSequence=chat.ChatSequence;
            return WireFrameDisposition.Accepted;
        }

        if(header.Opcode==WireOpcode.WhisperCommand &&
            postChat is not null &&
            WireProtocol.TryDecodeWhisperCommand(frame,out var whisper))
        {
            if(!GameplayReady(bound)||whisper.MapEpoch!=bound.MapEpoch||
               whisper.ChatSequence<=_lastChatSequence)
                return WireFrameDisposition.ProtocolViolation;
            var request=new TrustedChatCommand(
                bound.ActorId,bound.ConnectionEpoch,whisper.MapEpoch,
                whisper.ChatSequence,ChatChannel.Whisper,
                whisper.BubbleType,whisper.Message,whisper.TargetActorId,
                whisper.TargetName);
            if(!postChat(request))
                return WireFrameDisposition.Backpressured;
            _lastChatSequence=whisper.ChatSequence;
            return WireFrameDisposition.Accepted;
        }

        if(header.Opcode==WireOpcode.PartyCommand &&
            postParty is not null &&
            WireProtocol.TryDecodePartyCommand(frame,out var party))
        {
            if(!GameplayReady(bound)||party.MapEpoch!=bound.MapEpoch||
               party.PartySequence<=_lastPartySequence)
                return WireFrameDisposition.ProtocolViolation;
            var request=new TrustedPartyCommand(
                bound.ActorId,bound.ConnectionEpoch,party.MapEpoch,
                party.PartySequence,party.Action,party.PartyId,party.TargetActorId,
                party.TargetName);
            if(!postParty(request))
                return WireFrameDisposition.Backpressured;
            _lastPartySequence=party.PartySequence;
            return WireFrameDisposition.Accepted;
        }

        if(header.Opcode==WireOpcode.ClanCommand &&
            postClan is not null &&
            WireProtocol.TryDecodeClanCommand(frame,out var clan))
        {
            if(!GameplayReady(bound)||clan.MapEpoch!=bound.MapEpoch||
               clan.ClanSequence<=_lastClanSequence)
                return WireFrameDisposition.ProtocolViolation;
            var request=new TrustedClanCommand(
                bound.ActorId,bound.ConnectionEpoch,clan.MapEpoch,
                clan.ClanSequence,clan.Action,clan.ClanId,clan.Name);
            if(!postClan(request))
                return WireFrameDisposition.Backpressured;
            _lastClanSequence=clan.ClanSequence;
            return WireFrameDisposition.Accepted;
        }

        if(header.Opcode==WireOpcode.GmPvpNameCommand &&
            postGmPvpName is not null &&
            WireProtocol.TryDecodeGmPvpNameCommand(frame,out var gmPvp))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postGmPvpName(new TrustedGmPvpNameCommand(
                bound.ActorId,bound.ConnectionEpoch,gmPvp.MapEpoch,
                gmPvp.GmSequence,gmPvp.TargetActorId,gmPvp.State,
                _gmOperatorId))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.PvpSkill && postPvpSkill is not null &&
            WireProtocol.TryDecodePvpSkill(frame,out var pvpSkill))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postPvpSkill(new TrustedPvpSkill(
                bound.ActorId,bound.ConnectionEpoch,pvpSkill.MapEpoch,
                pvpSkill.SkillSequence,pvpSkill.SkillId,pvpSkill.TargetActorId,
                _combatProfile))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.PvpAttack && postPvpAttack is not null &&
            WireProtocol.TryDecodePvpAttack(frame,out var pvpAttack))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postPvpAttack(new TrustedPvpAttack(
                bound.ActorId,bound.ConnectionEpoch,pvpAttack.MapEpoch,
                pvpAttack.AttackSequence,pvpAttack.TargetActorId,_combatProfile))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if(header.Opcode==WireOpcode.TradeCommand && postTrade is not null &&
            WireProtocol.TryDecodeTradeCommand(frame,out var trade))
        {
            if(!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            return postTrade(new TrustedTradeCommand(
                bound.ActorId,bound.ConnectionEpoch,trade.MapEpoch,
                trade.TradeSequence,trade.Action,trade.TradeId,
                trade.TargetActorId,trade.ItemUid,trade.Money))
                ? WireFrameDisposition.Accepted:WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.NormalAttack && postAttack is not null &&
            WireProtocol.TryDecodeNormalAttack(frame, out var attack))
        {
            if (!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            var request = new TrustedNormalAttack(
                bound.ActorId, bound.ConnectionEpoch, attack.MapEpoch,
                attack.AttackSequence, attack.TargetEntityId, _combatProfile);
            return postAttack(request)
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }

        if (header.Opcode == WireOpcode.MoveTarget &&
            WireProtocol.TryDecodeMoveTarget(frame, out var move))
        {
            if (!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            var command = new MoveTargetCommand(
                bound.ActorId, bound.ConnectionEpoch, move.MapEpoch,
                move.MovementSequence, move.TargetX, move.TargetY);
            return postMove(command)
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }
        if (header.Opcode == WireOpcode.NpcInteract && postNpc is not null &&
            WireProtocol.TryDecodeNpcInteract(frame, out var npc))
        {
            if (!GameplayReady(bound))
                return WireFrameDisposition.ProtocolViolation;
            var request = new TrustedNpcInteraction(
                bound.ActorId, bound.ConnectionEpoch, npc.MapEpoch,
                npc.InteractionSequence, npc.NpcSpawnId);
            return postNpc(request)
                ? WireFrameDisposition.Accepted : WireFrameDisposition.Backpressured;
        }
        return WireFrameDisposition.ProtocolViolation;
    }

    private bool GameplayReady(ConnectionBinding expected)
    {
        lock (_bindingGate)
        {
            if (_closed || _binding != expected || _resyncPending) return false;
            if (_worldReadySentMapEpoch != expected.MapEpoch) return true;
            return _worldReadyAckedMapEpoch == expected.MapEpoch &&
                   _worldReadyAckedTick == _worldReadySentTick;
        }
    }

    private bool TryAcknowledgeWorldReady(ConnectionBinding expected, WorldReadyAckWire ack)
    {
        lock (_bindingGate)
        {
            if (_closed || _binding != expected || _resyncPending ||
                ack.MapEpoch != expected.MapEpoch ||
                _worldReadySentMapEpoch != expected.MapEpoch ||
                ack.ServerTick != _worldReadySentTick)
                return false;
            _worldReadyAckedMapEpoch = ack.MapEpoch;
            _worldReadyAckedTick = ack.ServerTick;
            return true;
        }
    }

    private bool TryArmResync(ConnectionBinding expected, WorldResyncRequestWire request)
    {
        lock (_bindingGate)
        {
            if (_closed || _binding != expected || _resyncPending ||
                request.MapEpoch != expected.MapEpoch ||
                _worldReadySentMapEpoch != expected.MapEpoch ||
                _worldReadyAckedMapEpoch != expected.MapEpoch ||
                _worldReadyAckedTick != _worldReadySentTick)
                return false;
            // Close the gameplay/ACK gate BEFORE publishing the request to the
            // AOI owner. Otherwise the pump could rebuild WorldReady first and
            // this receive thread could overwrite that completion with pending=true.
            _resyncPending = true;
            _worldReadyAckedMapEpoch = 0;
            _worldReadyAckedTick = -1;
            return true;
        }
    }

    private void CancelArmedResync(ConnectionBinding expected)
    {
        lock (_bindingGate)
        {
            if (_closed || _binding != expected || !_resyncPending)
                return;
            _resyncPending = false;
            // The bounded request was NOT accepted, so no replacement baseline
            // can exist. Restore the exact ACK that was valid before arming.
            _worldReadyAckedMapEpoch = _worldReadySentMapEpoch;
            _worldReadyAckedTick = _worldReadySentTick;
        }
    }
}
