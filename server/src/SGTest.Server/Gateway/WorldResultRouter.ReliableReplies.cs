using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using System.Threading.Channels;
using SGTest.Server.World;
using SGTest.Server.Identity;
using SGTest.Server.Wire;

namespace SGTest.Server.Gateway;

/// <summary>
/// Generation-safe result delegation to the original authenticated
/// GatewayConnection. No secondary sender, queue or socket is created.
/// </summary>
public sealed partial class WorldResultRouter
{
    public GatewaySendStatus QueuePlayerRespawnResult(
        TrustedPlayerRespawn request, PlayerRespawnOutcome outcome)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerRespawnResult(request, outcome);
        }
    }

    public GatewaySendStatus QueueAoi(ConnectionBinding viewer, AoiEvent evt)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(viewer.ActorId, out var connection) ||
                connection.Binding != viewer)
                return GatewaySendStatus.GenerationMismatch;
            return connection.QueueAoi(evt);
        }
    }

    public GatewaySendStatus QueuePlayerDamage(PlayerDamageSettlement result)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(result.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerDamage(result);
        }
    }

    public GatewaySendStatus QueuePvpAttackResult(
        TrustedPvpAttack request,PvpAttackResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpAttackResult(request,result);
        }
    }

    public GatewaySendStatus QueuePvpSkillResult(
        TrustedPvpSkill request,PvpSkillResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpSkillResult(request,result);
        }
    }

    public GatewaySendStatus QueueGmPvpNameResult(
        TrustedGmPvpNameCommand request,GmPvpNameResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueGmPvpNameResult(request,result);
        }
    }

    public GatewaySendStatus QueueChatDelivery(
        ConnectionBinding recipient,ChatDeliveryWire delivery)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueChatDelivery(recipient,delivery);
        }
    }

    public GatewaySendStatus QueueEmotionVisual(
        ConnectionBinding recipient,EmotionVisualWire visual)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueEmotionVisual(recipient,visual);
        }
    }

    public GatewaySendStatus QueueWhisperResult(
        TrustedChatCommand request,WhisperResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueWhisperResult(request,result);
        }
    }

    public GatewaySendStatus QueuePartyResult(
        TrustedPartyCommand request,PartyResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePartyResult(request,result);
        }
    }

    public GatewaySendStatus QueuePartyInvite(
        ConnectionBinding recipient,PartyInviteWire invite)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueuePartyInvite(recipient,invite);
        }
    }

    public GatewaySendStatus QueuePartySnapshot(
        ConnectionBinding recipient,PartySnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueuePartySnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueueClanResult(
        TrustedClanCommand request,ClanResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueClanResult(request,result);
        }
    }

    public GatewaySendStatus QueueClanSnapshot(
        ConnectionBinding recipient,ClanSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueClanSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueueFriendResult(
        TrustedFriendCommand request,FriendResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueFriendResult(request,result);
        }
    }

    public GatewaySendStatus QueueFriendSnapshot(
        ConnectionBinding recipient,FriendSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueFriendSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueueFriendInvite(
        ConnectionBinding recipient,FriendInviteWire invite)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueFriendInvite(recipient,invite);
        }
    }

    public GatewaySendStatus QueueHotkeySnapshot(
        ConnectionBinding recipient,HotkeySnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueHotkeySnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueuePetResult(
        TrustedPetCommand request,PetResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePetResult(request,result);
        }
    }

    public GatewaySendStatus QueuePetSnapshot(
        ConnectionBinding recipient,PetSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueuePetSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueuePetEquipmentSnapshot(
        ConnectionBinding recipient,PetEquipmentSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueuePetEquipmentSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueueMechaResult(
        TrustedMechaCommand request,MechaResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueMechaResult(request,result);
        }
    }

    public GatewaySendStatus QueueMechaSnapshot(
        ConnectionBinding recipient,MechaSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueMechaSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueueMechaEquipmentSnapshot(
        ConnectionBinding recipient,MechaEquipmentSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(recipient.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=recipient)
                return GatewaySendStatus.Closed;
            return connection.QueueMechaEquipmentSnapshot(recipient,snapshot);
        }
    }

    public GatewaySendStatus QueuePlayerShopResult(
        TrustedPlayerShopCommand request,PlayerShopResultWire result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerShopResult(request,result);
        }
    }

    public GatewaySendStatus QueuePlayerShopSnapshot(
        TrustedPlayerShopCommand request,PlayerShopSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerShopSnapshot(request,snapshot);
        }
    }

    public GatewaySendStatus QueuePlayerShopSold(
        ConnectionBinding seller,PlayerShopSoldWire sold)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(seller.ActorId,out var connection)||
               connection.IsClosed||connection.Binding!=seller)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerShopSold(seller,sold);
        }
    }

    public GatewaySendStatus QueueShopSnapshot(
        TrustedShopCommand request,ShopOpenStatus status,long money,
        IReadOnlyList<NpcShopCatalog.Item> items)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueShopSnapshot(request,status,money,items);
        }
    }

    public GatewaySendStatus QueueShopTransactionResult(
        TrustedShopCommand request,ShopTransactionResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueShopTransactionResult(request,result);
        }
    }

    public GatewaySendStatus QueueStorageSnapshot(
        TrustedStorageCommand request,StorageOpenStatus status,
        IReadOnlyList<StoredItem> items)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueStorageSnapshot(request,status,items);
        }
    }

    public GatewaySendStatus QueueStorageTransferResult(
        TrustedStorageCommand request,StorageTransferResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueStorageTransferResult(request,result);
        }
    }

    public GatewaySendStatus QueueEquipmentTransferResult(
        TrustedEquipmentCommand request,EquipmentTransferResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueEquipmentTransferResult(request,result);
        }
    }

    public GatewaySendStatus QueueBankSnapshot(
        TrustedBankCommand request,BankOpenStatus status,BankBalanceSnapshot? snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueBankSnapshot(request,status,snapshot);
        }
    }

    public GatewaySendStatus QueueBankTransactionResult(
        TrustedBankCommand request,BankTransactionResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueBankTransactionResult(request,result);
        }
    }

    public GatewaySendStatus QueuePvpModeResult(
        TrustedPvpModeCommand request,PvpModeResultStatus status,
        PvpMode mode,bool mapAllowsPlayerPvp)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePvpModeResult(
                request,status,mode,mapAllowsPlayerPvp);
        }
    }

    public GatewaySendStatus QueueQuestView(
        TrustedQuestCommand request,QuestViewWireStatus status,QuestView? view)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueQuestView(request,status,view);
        }
    }

    public GatewaySendStatus QueueRepairSnapshot(
        TrustedRepairCommand request,RepairOpenStatus status,RepairPreview? preview)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRepairSnapshot(request,status,preview);
        }
    }

    public GatewaySendStatus QueueRepairResult(
        TrustedRepairCommand request,RepairResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRepairResult(request,result);
        }
    }

    public GatewaySendStatus QueueEnhancementSnapshot(
        TrustedEnhancementCommand request,EnhancementOpenStatus status,
        EnhancementPreview? preview)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueEnhancementSnapshot(request,status,preview);
        }
    }

    public GatewaySendStatus QueueEnhancementResult(
        TrustedEnhancementCommand request,EnhancementResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueEnhancementResult(request,result);
        }
    }

    public GatewaySendStatus QueueTradeSnapshot(
        ConnectionBinding expected,TradeSnapshotWire snapshot)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(expected.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueTradeSnapshot(expected,snapshot);
        }
    }

    public GatewaySendStatus QueueTradeTerminal(
        ConnectionBinding expected,TradeTerminalWire terminal)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(expected.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueTradeTerminal(expected,terminal);
        }
    }

    public GatewaySendStatus QueueRecoveryUseResult(
        TrustedRecoveryUse request,RecoveryUseResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueRecoveryUseResult(request,result);
        }
    }

    public GatewaySendStatus QueueInventoryDiscardResult(
        TrustedInventoryDiscard request,InventoryDiscardResult result)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueInventoryDiscardResult(request,result);
        }
    }

    public GatewaySendStatus QueueGroundPickupResult(
        TrustedGroundPickup request,PickupResult result)
    {
        lock(_gate)
        {
            if (!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueGroundPickupResult(request,result);
        }
    }

    public GatewaySendStatus QueuePlayerCombatState(
        ConnectionBinding generation,int currentHp,int maxHp,
        int currentMp,int maxMp,int currentWp,int maxWp)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(generation.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePlayerCombatState(
                generation,currentHp,maxHp,currentMp,maxMp,currentWp,maxWp);
        }
    }

    public GatewaySendStatus QueueSkillUseResult(
        TrustedSkillUse request,SkillUseOutcome outcome)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueSkillUseResult(request,outcome);
        }
    }

    public GatewaySendStatus QueueNormalAttackResult(
        TrustedNormalAttack request, NormalAttackOutcome outcome)
    {
        lock (_gate)
        {
            if (!_connections.TryGetValue(request.ActorId, out var connection) ||
                connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueueNormalAttackResult(request, outcome);
        }
    }

    public GatewaySendStatus QueuePetAttackResult(
        TrustedPetAttack request,NormalAttackOutcome outcome)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePetAttackResult(request,outcome);
        }
    }

    public GatewaySendStatus QueuePetSkillResult(
        TrustedPetSkill request,SkillUseOutcome outcome)
    {
        lock(_gate)
        {
            if(!_connections.TryGetValue(request.ActorId,out var connection)||
               connection.IsClosed)
                return GatewaySendStatus.Closed;
            return connection.QueuePetSkillResult(request,outcome);
        }
    }
}
