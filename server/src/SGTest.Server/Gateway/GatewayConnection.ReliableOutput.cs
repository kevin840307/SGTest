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
/// Reliable V1 result and AOI encoding for the authenticated connection.
/// All output shares GatewayConnection's existing _bindingGate, one bounded
/// reliable FIFO, generation checks, and WorldReady/visibility bookkeeping.
/// No second queue, connection, socket, or status owner is introduced.
/// </summary>
public sealed partial class GatewayConnection
{
    /// <summary>
    /// A portal result is reliable and bound to the same physical session.
    /// Successful World transfer must already have advanced the trusted Gateway
    /// MapEpoch; rejection can reference a stale old request and never mutates
    /// the binding. Only the server issues result MapEpoch and status.
    /// </summary>
    public GatewaySendStatus QueueNpcResult(
        TrustedNpcInteraction request, NpcPortalOutcome outcome)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (request.ActorId != bound.ActorId ||
                request.ConnectionEpoch != bound.ConnectionEpoch ||
                request.RequestMapEpoch <= 0 ||
                request.RequestMapEpoch > bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            long next = request.RequestMapEpoch;
            if (outcome.Status == NpcPortalStatus.Accepted)
            {
                if (outcome.TransferredActor is not { } after ||
                    after.ActorId != bound.ActorId ||
                    after.ConnectionEpoch != bound.ConnectionEpoch ||
                    after.MapEpoch != bound.MapEpoch ||
                    after.MapEpoch <= request.RequestMapEpoch)
                    return GatewaySendStatus.GenerationMismatch;
                next = after.MapEpoch;
            }
            else if (outcome.TransferredActor is not null)
                return GatewaySendStatus.GenerationMismatch;

            var frame = WireProtocol.EncodeNpcInteractResult(
                new NpcInteractResultWire(request.RequestMapEpoch,
                    request.InteractionSequence, outcome.Status, next));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    /// <summary>
    /// Reliable authoritative receipt queue. Never DropOldest.
    /// A receipt from another Actor/Connection or a future MapEpoch is not routable
    /// on this connection. Old-map receipts on the SAME connection are allowed so
    /// the client can discard them using the receipt MapEpoch.
    /// </summary>
    public GatewaySendStatus QueuePlayerDamage(PlayerDamageSettlement result)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (result.ActorId != bound.ActorId ||
                result.ConnectionEpoch != bound.ConnectionEpoch ||
                result.MapEpoch != bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            var frame = result.SourceKind!=PlayerDamageSourceKind.Monster
                ? WireProtocol.EncodePvpDamage(new(
                    result.MapEpoch,result.DamageSequence,result.ServerTick,
                    result.AttackerActorId,result.AttackerConnectionEpoch,
                    result.SourceKind==PlayerDamageSourceKind.PlayerSkill
                        ? result.AttackerSkillSequence
                        : result.AttackerAttackSequence,
                    result.Damage,result.CurrentHp,result.MaxHp,result.Died))
                : WireProtocol.EncodePlayerDamage(new(
                    result.MapEpoch,result.DamageSequence,result.ServerTick,
                    result.AttackerMonsterEntityId,result.AttackerMonsterRevision,
                    result.Damage,result.CurrentHp,result.MaxHp,result.Died));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePlayerRespawnResult(
        TrustedPlayerRespawn request, PlayerRespawnOutcome outcome)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (request.ActorId != bound.ActorId ||
                request.ConnectionEpoch != bound.ConnectionEpoch ||
                request.RequestMapEpoch != bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            var frame = WireProtocol.EncodePlayerRespawnResult(new(
                request.RequestMapEpoch, request.RespawnSequence,
                outcome.Status, outcome.CurrentHp, outcome.MaxHp));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }


    public GatewaySendStatus QueueShopSnapshot(
        TrustedShopCommand request,ShopOpenStatus status,long money,
        IReadOnlyList<NpcShopCatalog.Item> items)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedShopCommandKind.Open)
                return GatewaySendStatus.GenerationMismatch;
            var wireItems=status==ShopOpenStatus.Opened
                ? items.OrderBy(item=>item.ItemDefinitionId)
                    .Select(item=>new ShopItemWire(
                        item.ItemDefinitionId,item.BuyPrice,item.SellPrice)).ToArray()
                : Array.Empty<ShopItemWire>();
            var frame=WireProtocol.EncodeShopSnapshot(new(
                request.RequestMapEpoch,request.ShopSequence,status,
                request.NpcSpawnId,status==ShopOpenStatus.Opened?money:0,wireItems));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueShopTransactionResult(
        TrustedShopCommand request,ShopTransactionResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.NpcSpawnId!=result.NpcSpawnId||
               request.ItemDefinitionId!=result.ItemDefinitionId||
               request.Quantity!=result.Quantity||
               (request.Kind==TrustedShopCommandKind.Sell&&
                request.ItemUid!=result.ItemUid)||
               request.Kind==TrustedShopCommandKind.Open)
                return GatewaySendStatus.GenerationMismatch;
            var expectedKind=request.Kind==TrustedShopCommandKind.Buy
                ? ShopTransactionKind.Buy:ShopTransactionKind.Sell;
            if(result.Kind!=expectedKind)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeShopTransactionResult(new(
                request.RequestMapEpoch,request.ShopSequence,result.Kind,
                result.Status,result.NpcSpawnId,result.Money,result.ItemUid,
                result.ItemDefinitionId,result.Quantity,result.RemainingQuantity));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueStorageSnapshot(
        TrustedStorageCommand request,StorageOpenStatus status,
        IReadOnlyList<StoredItem> items)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedStorageCommandKind.Open||
               items is null||
               (status!=StorageOpenStatus.Opened&&items.Count!=0)||
               items.Count>WireProtocol.StorageSnapshotMaxItems)
                return GatewaySendStatus.GenerationMismatch;
            var ordered=status==StorageOpenStatus.Opened
                ? items.OrderBy(item=>item.ItemUid).ToArray()
                : Array.Empty<StoredItem>();
            if(ordered.Length==0)
            {
                var frame=WireProtocol.EncodeStorageSnapshot(new(
                    request.RequestMapEpoch,request.StorageSequence,status,
                    request.NpcSpawnId,0,true,Array.Empty<StorageItemWire>()));
                return _reliable.TryEnqueue(frame)
                    ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
            }
            for(int start=0;start<ordered.Length;
                start+=WireProtocol.StorageSnapshotMaxItemsPerFrame)
            {
                var chunk=ordered.Skip(start)
                    .Take(WireProtocol.StorageSnapshotMaxItemsPerFrame)
                    .Select(item=>new StorageItemWire(
                        item.ItemUid,item.ItemDefinitionId,item.Quantity,item.SourceDropId,
                        item.EnhancementLevel,item.DurabilityRaw??-1))
                    .ToArray();
                bool complete=start+chunk.Length==ordered.Length;
                var frame=WireProtocol.EncodeStorageSnapshot(new(
                    request.RequestMapEpoch,request.StorageSequence,status,
                    request.NpcSpawnId,start,complete,chunk));
                if(!_reliable.TryEnqueue(frame))
                    return GatewaySendStatus.Backpressured;
            }
            return GatewaySendStatus.Enqueued;
        }
    }

    public GatewaySendStatus QueueStorageTransferResult(
        TrustedStorageCommand request,StorageTransferResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind==TrustedStorageCommandKind.Open||
               request.ItemUid!=result.ItemUid)
                return GatewaySendStatus.GenerationMismatch;
            var expected=request.Kind==TrustedStorageCommandKind.Deposit
                ? StorageTransferKind.Deposit:StorageTransferKind.Withdraw;
            if(result.Kind!=expected)return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeStorageTransferResult(new(
                request.RequestMapEpoch,request.StorageSequence,result.Kind,
                result.Status,request.NpcSpawnId,result.ItemUid,
                result.ItemDefinitionId,result.Quantity,result.SourceDropId));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueEquipmentTransferResult(
        TrustedEquipmentCommand request,EquipmentTransferResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.ItemUid!=result.ItemUid)
                return GatewaySendStatus.GenerationMismatch;
            var expected=request.Kind==TrustedEquipmentCommandKind.Equip
                ? EquipmentTransferKind.Equip:EquipmentTransferKind.Unequip;
            if(result.Kind!=expected)return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeEquipmentTransferResult(new(
                request.RequestMapEpoch,request.EquipmentSequence,result.Kind,
                result.Status,result.ItemUid,result.ItemDefinitionId,
                result.SlotId,result.SourceDropId));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueBankSnapshot(
        TrustedBankCommand request,BankOpenStatus status,BankBalanceSnapshot? snapshot)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedBankCommandKind.Open)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeBankSnapshot(new(
                request.RequestMapEpoch,request.BankSequence,status,request.NpcSpawnId,
                snapshot?.Scope??BankBalanceScope.None,
                snapshot?.WalletMoney??0,snapshot?.BankMoney??0));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueBankTransactionResult(
        TrustedBankCommand request,BankTransactionResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind==TrustedBankCommandKind.Open||
               request.Amount!=result.Amount)
                return GatewaySendStatus.GenerationMismatch;
            var expected=request.Kind==TrustedBankCommandKind.Deposit
                ? BankTransactionKind.Deposit:BankTransactionKind.Withdraw;
            if(result.Kind!=expected)return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeBankTransactionResult(new(
                request.RequestMapEpoch,request.BankSequence,result.Kind,result.Status,
                request.NpcSpawnId,result.Scope,result.Amount,
                result.WalletMoney,result.BankMoney));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePvpModeResult(
        TrustedPvpModeCommand request,PvpModeResultStatus status,
        PvpMode mode,bool mapAllowsPlayerPvp)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodePvpModeResult(new(
                request.RequestMapEpoch,request.PvpSequence,status,
                mode,mapAllowsPlayerPvp));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePvpAttackResult(TrustedPvpAttack request,PvpAttackResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||result.MapEpoch!=request.RequestMapEpoch||
               result.AttackSequence!=request.AttackSequence||result.TargetActorId!=request.TargetActorId)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePvpAttackResult(result))
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePvpSkillResult(
        TrustedPvpSkill request,PvpSkillResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               result.MapEpoch!=request.RequestMapEpoch||
               result.SkillSequence!=request.SkillSequence||
               result.SkillId!=request.SkillId||
               result.TargetActorId!=request.TargetActorId)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePvpSkillResult(result))
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueGmPvpNameResult(
        TrustedGmPvpNameCommand request,GmPvpNameResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               result.MapEpoch!=request.RequestMapEpoch||
               result.GmSequence!=request.GmSequence||
               result.TargetActorId!=request.TargetActorId||
               result.State!=request.State)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodeGmPvpNameResult(result))
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePlayerShopResult(
        TrustedPlayerShopCommand request,PlayerShopResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               result.MapEpoch!=request.RequestMapEpoch||
               result.ShopSequence!=request.ShopSequence||
               result.Action!=request.Action)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePlayerShopResult(result))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePlayerShopSold(
        ConnectionBinding expected,PlayerShopSoldWire sold)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||sold.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePlayerShopSold(sold))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueChatDelivery(
        ConnectionBinding expected,ChatDeliveryWire delivery)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||delivery.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodeChatDelivery(delivery))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueWhisperResult(
        TrustedChatCommand request,WhisperResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            bool nameTarget=!string.IsNullOrEmpty(request.TargetName);
            bool validSelector=request.TargetActorId>=0&&
                ((request.TargetActorId>0)!=nameTarget);
            bool targetMatches=nameTarget
                ? result.TargetActorId>=0
                : request.TargetActorId==result.TargetActorId;
            if(request.Channel!=ChatChannel.Whisper||
               request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.ChatSequence!=result.ChatSequence||
               request.RequestMapEpoch!=result.MapEpoch||
               !validSelector||!targetMatches)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodeWhisperResult(result))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePartyResult(
        TrustedPartyCommand request,PartyResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.PartySequence!=result.PartySequence||
               request.RequestMapEpoch!=result.MapEpoch||
               request.Action!=result.Action)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePartyResult(result))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePartyInvite(
        ConnectionBinding expected,PartyInviteWire invite)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||invite.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePartyInvite(invite))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePartySnapshot(
        ConnectionBinding expected,PartySnapshotWire snapshot)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||snapshot.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePartySnapshot(snapshot))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueClanResult(
        TrustedClanCommand request,ClanResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.ClanSequence!=result.ClanSequence||
               request.RequestMapEpoch!=result.MapEpoch||
               request.Action!=result.Action)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodeClanResult(result))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueClanSnapshot(
        ConnectionBinding expected,ClanSnapshotWire snapshot)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||snapshot.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodeClanSnapshot(snapshot))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePetResult(
        TrustedPetCommand request,PetResultWire result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.PetSequence!=result.PetSequence||
               request.RequestMapEpoch!=result.MapEpoch||
               request.Action!=result.Action||
               request.PetUid!=result.PetUid)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePetResult(result))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePetSnapshot(
        ConnectionBinding expected,PetSnapshotWire snapshot)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||snapshot.ReceiverMapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            return _reliable.TryEnqueue(WireProtocol.EncodePetSnapshot(snapshot))
                ?GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueQuestView(
        TrustedQuestCommand request,QuestViewWireStatus status,QuestView? view)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            if(view is not null&&request.Kind==TrustedQuestCommandKind.Choose&&
               request.QuestId!=view.QuestId)
                return GatewaySendStatus.GenerationMismatch;
            var choices=view?.Choices.Select(
                x=>new QuestViewChoiceWire(x.ChoiceId,x.Label)).ToArray()
                ??Array.Empty<QuestViewChoiceWire>();
            var frame=WireProtocol.EncodeQuestView(new(
                request.RequestMapEpoch,request.QuestSequence,status,
                request.NpcSpawnId,view?.QuestId??0,view?.RuleRevision??0,
                view?.State??QuestProgressState.NotStarted,view?.Stage??0,
                view?.ProgressRevision??0,view?.Title??string.Empty,
                view?.Text??string.Empty,choices));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueRepairSnapshot(
        TrustedRepairCommand request,RepairOpenStatus status,RepairPreview? preview)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedRepairCommandKind.Open)
                return GatewaySendStatus.GenerationMismatch;
            var items=preview?.Items??Array.Empty<RepairPreviewItem>();
            const int page=WireProtocol.RepairSnapshotMaxItemsPerFrame;
            if(items.Count==0)
            {
                var empty=WireProtocol.EncodeRepairSnapshot(new(
                    request.RequestMapEpoch,request.RepairSequence,status,
                    request.NpcSpawnId,preview?.WalletMoney??0,0,true,
                    Array.Empty<RepairPreviewItemWire>()));
                return _reliable.TryEnqueue(empty)
                    ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
            }
            for(int start=0;start<items.Count;start+=page)
            {
                int count=Math.Min(page,items.Count-start);
                var rows=new RepairPreviewItemWire[count];
                for(int i=0;i<count;i++)
                {
                    var item=items[start+i];
                    rows[i]=new(item.ItemUid,item.ItemDefinitionId,item.Location,
                        item.CurrentDurability,item.MaxDurability,item.Cost);
                }
                var frame=WireProtocol.EncodeRepairSnapshot(new(
                    request.RequestMapEpoch,request.RepairSequence,status,
                    request.NpcSpawnId,preview!.WalletMoney,start,
                    start+count==items.Count,rows));
                if(!_reliable.TryEnqueue(frame))
                    return GatewaySendStatus.Backpressured;
            }
            return GatewaySendStatus.Enqueued;
        }
    }

    public GatewaySendStatus QueueRepairResult(
        TrustedRepairCommand request,RepairResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedRepairCommandKind.Repair||
               request.ItemUid!=result.ItemUid||
               request.NpcSpawnId!=result.NpcSpawnId)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeRepairResult(new(
                request.RequestMapEpoch,request.RepairSequence,result.Status,
                result.NpcSpawnId,result.ItemUid,result.ItemDefinitionId,
                result.Location,result.CurrentDurability,result.MaxDurability,
                result.Cost,result.WalletMoney));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueEnhancementSnapshot(
        TrustedEnhancementCommand request,EnhancementOpenStatus status,
        EnhancementPreview? preview)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedEnhancementCommandKind.Open)
                return GatewaySendStatus.GenerationMismatch;
            var items=preview?.Items??Array.Empty<EnhancementPreviewItem>();
            const int page=WireProtocol.EnhancementSnapshotMaxItemsPerFrame;
            if(items.Count==0)
            {
                var empty=WireProtocol.EncodeEnhancementSnapshot(new(
                    request.RequestMapEpoch,request.EnhancementSequence,status,
                    request.NpcSpawnId,preview?.WalletMoney??0,0,true,
                    Array.Empty<EnhancementPreviewItemWire>()));
                return _reliable.TryEnqueue(empty)
                    ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
            }
            for(int start=0;start<items.Count;start+=page)
            {
                int count=Math.Min(page,items.Count-start);
                var rows=new EnhancementPreviewItemWire[count];
                for(int i=0;i<count;i++)
                {
                    var item=items[start+i];
                    rows[i]=new(item.ItemUid,item.ItemDefinitionId,item.Location,
                        item.CurrentLevel,item.TargetLevel,item.SuccessPpm,
                        item.MoneyCost,item.ConsumeMoneyOnFailure);
                }
                var frame=WireProtocol.EncodeEnhancementSnapshot(new(
                    request.RequestMapEpoch,request.EnhancementSequence,status,
                    request.NpcSpawnId,preview!.WalletMoney,start,
                    start+count==items.Count,rows));
                if(!_reliable.TryEnqueue(frame))
                    return GatewaySendStatus.Backpressured;
            }
            return GatewaySendStatus.Enqueued;
        }
    }

    public GatewaySendStatus QueueEnhancementResult(
        TrustedEnhancementCommand request,EnhancementResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.Kind!=TrustedEnhancementCommandKind.Enhance||
               request.ItemUid!=result.ItemUid||
               request.NpcSpawnId!=result.NpcSpawnId)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeEnhancementResult(new(
                request.RequestMapEpoch,request.EnhancementSequence,result.Status,
                result.NpcSpawnId,result.ItemUid,result.ItemDefinitionId,
                result.Location,result.PreviousLevel,result.TargetLevel,
                result.CurrentLevel,result.Succeeded,result.MoneyCost,
                result.MoneyCharged,result.WalletMoney,result.RollPpm));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueTradeSnapshot(
        ConnectionBinding expected,TradeSnapshotWire snapshot)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||snapshot.MapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeTradeSnapshot(snapshot);
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueTradeTerminal(
        ConnectionBinding expected,TradeTerminalWire terminal)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            if(_binding!=expected||terminal.MapEpoch!=expected.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeTradeTerminal(terminal);
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued:GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueRecoveryUseResult(
        TrustedRecoveryUse request,RecoveryUseResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId ||
                request.ConnectionEpoch!=bound.ConnectionEpoch ||
                request.RequestMapEpoch<=0 ||
                request.RequestMapEpoch>bound.MapEpoch ||
                request.ItemUid!=result.ItemUid)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeRecoveryUseResult(new(
                request.RequestMapEpoch,request.UseSequence,result.Status,
                result.ItemUid,result.ItemDefinitionId,result.RemainingQuantity,
                result.CurrentHp,result.MaxHp));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueInventoryDiscardResult(
        TrustedInventoryDiscard request,InventoryDiscardResult result)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId ||
                request.ConnectionEpoch!=bound.ConnectionEpoch ||
                request.RequestMapEpoch<=0 ||
                request.RequestMapEpoch>bound.MapEpoch ||
                request.ItemUid!=result.ItemUid)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeInventoryDiscardResult(new(
                request.RequestMapEpoch,request.DiscardSequence,result.Status,
                result.ItemUid,result.ItemDefinitionId,result.DiscardedQuantity,
                result.RemainingQuantity));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueGroundPickupResult(
        TrustedGroundPickup request,PickupResult result)
    {
        lock(_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound=_binding;
            if (request.ActorId!=bound.ActorId ||
                request.ConnectionEpoch!=bound.ConnectionEpoch ||
                request.RequestMapEpoch<=0 || request.RequestMapEpoch>bound.MapEpoch ||
                request.DropId!=result.DropId)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeGroundPickupResult(new(
                request.RequestMapEpoch,request.PickupSequence,result.Status,
                result.DropId,result.ItemUid,result.ItemDefinitionId,result.Quantity));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueuePlayerCombatState(
        ConnectionBinding generation,int currentHp,int maxHp,
        int currentMp,int maxMp)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(generation!=bound)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodePlayerCombatState(new(
                bound.MapEpoch,currentHp,maxHp,currentMp,maxMp));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueNormalAttackResult(
        TrustedNormalAttack request, NormalAttackOutcome outcome)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (request.ActorId != bound.ActorId ||
                request.ConnectionEpoch != bound.ConnectionEpoch ||
                request.RequestMapEpoch <= 0 ||
                request.RequestMapEpoch > bound.MapEpoch ||
                outcome.TargetEntityId != request.TargetEntityId)
                return GatewaySendStatus.GenerationMismatch;
            var frame = WireProtocol.EncodeNormalAttackResult(new(
                request.RequestMapEpoch, request.AttackSequence, outcome.Status,
                outcome.ServerTick, outcome.TargetEntityId, outcome.Damage,
                outcome.CurrentHp, outcome.MaxHp, outcome.TargetRevision));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueSkillUseResult(
        TrustedSkillUse request,SkillUseOutcome outcome)
    {
        lock(_bindingGate)
        {
            if(_closed)return GatewaySendStatus.Closed;
            var bound=_binding;
            if(request.ActorId!=bound.ActorId ||
                request.ConnectionEpoch!=bound.ConnectionEpoch ||
                request.RequestMapEpoch<=0 ||
                request.RequestMapEpoch>bound.MapEpoch ||
                outcome.SkillId!=request.SkillId ||
                outcome.TargetEntityId!=request.TargetEntityId)
                return GatewaySendStatus.GenerationMismatch;
            var frame=WireProtocol.EncodeSkillUseResult(new(
                request.RequestMapEpoch,request.SkillSequence,outcome.Status,
                outcome.ServerTick,outcome.SkillId,outcome.TargetEntityId,
                outcome.Damage,outcome.CurrentHp,outcome.MaxHp,
                outcome.TargetRevision));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued : GatewaySendStatus.Backpressured;
        }
    }

    public GatewaySendStatus QueueReceipt(MoveReceipt receipt)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (receipt.ActorId != bound.ActorId ||
                receipt.ConnectionEpoch != bound.ConnectionEpoch ||
                receipt.MapEpoch <= 0 ||
                receipt.MapEpoch > bound.MapEpoch)
                return GatewaySendStatus.GenerationMismatch;

            var frame = WireProtocol.EncodeMoveReceipt(new MoveReceiptWire(
                receipt.MapEpoch,
                receipt.MovementSequence,
                receipt.Status,
                receipt.Tick));
            return _reliable.TryEnqueue(frame)
                ? GatewaySendStatus.Enqueued
                : GatewaySendStatus.Backpressured;
        }
    }


}
