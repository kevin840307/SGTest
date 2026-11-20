using System.Text;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

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
    long TargetActorId=0);
public readonly record struct TrustedPartyCommand(
    long ActorId,long ConnectionEpoch,long RequestMapEpoch,
    uint PartySequence,PartyCommandAction Action,long PartyId,long TargetActorId);
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

/// <summary>
/// Transport-independent state for one authenticated connection.
/// Authentication/TLS/socket lifetime live above this class; raw client bytes can
/// never choose ActorId or ConnectionEpoch. Reconnect creates a NEW instance.
/// </summary>
public sealed class GatewayConnection
{
    private sealed class ReliableFrameQueue
    {
        private readonly object _gate = new();
        private readonly Queue<byte[]> _frames = new();
        private readonly int _maxFrames;
        private readonly int _maxBytes;
        private readonly SemaphoreSlim _available = new(0);
        private int _bytes;

        public ReliableFrameQueue(int maxFrames, int maxBytes)
        {
            _maxFrames = maxFrames;
            _maxBytes = maxBytes;
        }

        public int Count { get { lock (_gate) return _frames.Count; } }
        public int Bytes { get { lock (_gate) return _bytes; } }

        public bool TryEnqueue(byte[] frame)
        {
            lock (_gate)
            {
                if (_frames.Count >= _maxFrames || frame.Length > _maxBytes - _bytes)
                    return false;
                _frames.Enqueue(frame);
                _bytes += frame.Length;
            }
            _available.Release();
            return true;
        }

        public bool TryDequeue(out byte[] frame)
        {
            if (!_available.Wait(0))
            {
                frame = Array.Empty<byte>();
                return false;
            }
            lock (_gate)
            {
                if (!_frames.TryDequeue(out frame!))
                    throw new InvalidOperationException("Reliable queue signal/state diverged");
                _bytes -= frame.Length;
                return true;
            }
        }

        public async ValueTask<byte[]?> DequeueAsync(CancellationToken cancellationToken)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_frames.TryDequeue(out var frame))
                    return null; // Close may clear after the signal was acquired.
                _bytes -= frame.Length;
                return frame;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _frames.Clear();
                _bytes = 0;
                while (_available.Wait(0)) { }
            }
        }
    }

    private readonly object _bindingGate = new();
    private readonly object _receiveGate = new();
    private readonly WireStreamParser _parser;
    private readonly ReliableFrameQueue _reliable;
    private ConnectionBinding _binding;
    private bool _closed;
    private long _worldReadySentMapEpoch;
    private long _worldReadySentTick = -1;
    private long _worldReadyAckedMapEpoch;
    private long _worldReadyAckedTick = -1;
    private bool _resyncPending;
    private readonly TrustedCombatProfile? _combatProfile;
    private uint _lastChatSequence;
    private uint _lastPartySequence;
    private readonly string? _displayName;
    private CharacterPvpState? _pvpState;
    private readonly string? _gmOperatorId;
    private readonly Dictionary<long,long> _visibleActors = new();

    public GatewayConnection(ActorSnapshot trustedActor,
                             GatewayConnectionOptions? options = null,
                             TrustedCombatProfile? combatProfile = null,
                             string? displayName = null,
                             CharacterPvpState? pvpState = null,
                             string? gmOperatorId = null)
    {
        var configured = options ?? new GatewayConnectionOptions();
        if (configured.MaxBufferedInputBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxBufferedInputBytes > WireProtocol.MaximumFrameBytes * 4 ||
            configured.MaxReliableFrames is < 1 or > 4096 ||
            configured.MaxReliableBytes < WireProtocol.MaximumFrameBytes ||
            configured.MaxReliableBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid bounded connection limits");

        _binding = ConnectionBinding.FromTrustedSnapshot(trustedActor);
        if (combatProfile is { } profile &&
            (profile.Attack < 0 || profile.Defense < 0 || profile.Hit < 0 ||
             profile.Dodge < 0 || !double.IsFinite(profile.AttackRange) ||
             profile.AttackRange <= 0 || profile.AttackRange > 500 ||
             profile.AttackCooldownTicks is < 1 or > 3600))
            throw new ArgumentOutOfRangeException(nameof(combatProfile));
        _combatProfile = combatProfile;
        if((displayName is null)!=(pvpState is null)||
           (pvpState is not null&&pvpState.CharacterId!=trustedActor.ActorId))
            throw new ArgumentException(
                "Display name and PvP state must match the trusted actor");
        if(displayName is not null)
        {
            int nameBytes=Encoding.UTF8.GetByteCount(displayName);
            if(string.IsNullOrWhiteSpace(displayName)||
               nameBytes is <1 or >WireProtocol.PlayerIdentityMaxNameBytes)
                throw new ArgumentOutOfRangeException(nameof(displayName));
        }
        _displayName=displayName;
        _pvpState=pvpState;
        if(gmOperatorId is not null&&
           (string.IsNullOrWhiteSpace(gmOperatorId)||gmOperatorId.Length>64||
            gmOperatorId.Any(char.IsControl)))
            throw new ArgumentOutOfRangeException(nameof(gmOperatorId));
        _gmOperatorId=gmOperatorId;
        _parser = new WireStreamParser(configured.MaxBufferedInputBytes);
        _reliable = new ReliableFrameQueue(configured.MaxReliableFrames, configured.MaxReliableBytes);
    }

    public ConnectionBinding Binding
    {
        get { lock (_bindingGate) return _binding; }
    }

    public bool TryGetPlayerIdentity(out PlayerIdentityPresentation identity)
    {
        lock(_bindingGate)
        {
            if(_closed||_displayName is null||_pvpState is null)
            {
                identity=default;
                return false;
            }
            identity=new(
                _binding.ActorId,_binding.ConnectionEpoch,_binding.MapEpoch,
                _displayName,_pvpState.Mode,_pvpState.RedMan,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                _pvpState.PinkUntilUnixMilliseconds,
                _pvpState.UpdatedUnixMilliseconds);
            return true;
        }
    }

    public bool TryUpdatePlayerPvpState(CharacterPvpState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock(_bindingGate)
        {
            if(_closed||_displayName is null||
               state.CharacterId!=_binding.ActorId)
                return false;
            _pvpState=state;
            return true;
        }
    }

    public bool IsClosed
    {
        get { lock (_bindingGate) return _closed; }
    }

    public int BufferedInputBytes => _parser.BufferedBytes;
    public int ReliableFrames => _reliable.Count;
    public int ReliableBytes => _reliable.Bytes;
    public bool GmAuthorized => _gmOperatorId is not null;
    public bool IsVisibleActor(long actorId)
    {
        lock(_bindingGate)
            return !_closed&&actorId>0&&actorId!=_binding.ActorId&&
                _visibleActors.ContainsKey(actorId);
    }
    public bool WorldReadyAcknowledged
    {
        get
        {
            lock (_bindingGate)
                return !_resyncPending &&
                    _worldReadySentMapEpoch == _binding.MapEpoch &&
                    _worldReadyAckedMapEpoch == _binding.MapEpoch &&
                    _worldReadyAckedTick == _worldReadySentTick;
        }
    }

    /// <summary>
    /// Same physical/authenticated connection may advance MapEpoch after a trusted
    /// transfer. A new ConnectionEpoch MUST create a new GatewayConnection.
    /// </summary>
    public bool TryUpdateTrustedBinding(ActorSnapshot trustedActor)
    {
        ConnectionBinding next;
        try { next = ConnectionBinding.FromTrustedSnapshot(trustedActor); }
        catch (ArgumentOutOfRangeException) { return false; }

        lock (_bindingGate)
        {
            if (_closed ||
                next.ActorId != _binding.ActorId ||
                next.ConnectionEpoch != _binding.ConnectionEpoch ||
                next.MapEpoch < _binding.MapEpoch)
                return false;
            if (next.MapEpoch > _binding.MapEpoch)
            {
                _worldReadySentMapEpoch = 0;
                _worldReadySentTick = -1;
                _worldReadyAckedMapEpoch = 0;
                _worldReadyAckedTick = -1;
                _resyncPending = false;
                _visibleActors.Clear();
            }
            _binding = next;
            return true;
        }
    }

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
                                              Func<TrustedPartyCommand, bool>? postParty = null)
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
                    postPickup, postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty),
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
                                              Func<TrustedPartyCommand, bool>? postParty = null)
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
                    postPickup, postRecovery, postDiscard, postSkill, postShop, postStorage, postEquipment, postBank, postRepair, postEnhancement, postTrade, postQuest, postPvp, postPvpAttack, postPvpSkill, postGmPvpName, postPlayerShop, postChat, postParty),
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
        Func<TrustedPartyCommand, bool>? postParty)
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
                whisper.BubbleType,whisper.Message,whisper.TargetActorId);
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
                party.PartySequence,party.Action,party.PartyId,party.TargetActorId);
            if(!postParty(request))
                return WireFrameDisposition.Backpressured;
            _lastPartySequence=party.PartySequence;
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
                        item.EnhancementLevel))
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
            if(request.Channel!=ChatChannel.Whisper||
               request.ActorId!=bound.ActorId||
               request.ConnectionEpoch!=bound.ConnectionEpoch||
               request.RequestMapEpoch!=bound.MapEpoch||
               request.ChatSequence!=result.ChatSequence||
               request.RequestMapEpoch!=result.MapEpoch||
               request.TargetActorId!=result.TargetActorId)
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

    /// <summary>
    /// Reliable AOI event (WorldReady/Spawn/Delta/Despawn), already filtered by
    /// trusted WorldAoi. A future-map or stale-map AOI event must never leak
    /// across the current connection/map generation. Shares receipt FIFO.
    /// </summary>
    public GatewaySendStatus QueueAoi(AoiEvent evt)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (evt.ReceiverMapEpoch != bound.MapEpoch || evt.Tick < 0)
                return GatewaySendStatus.GenerationMismatch;
            byte[] frame;
            switch (evt.Kind)
            {
                case AoiEventKind.WorldReady when evt.Entity is { } ready &&
                     ready.ActorId == bound.ActorId &&
                     ready.ConnectionEpoch == bound.ConnectionEpoch &&
                     ready.MapEpoch == bound.MapEpoch:
                    frame = WireProtocol.EncodeWorldReady(new(
                        evt.ReceiverMapEpoch, evt.Tick, ready.ActorId,
                        ready.MapId, (float)ready.X, (float)ready.Y, ready.Facing));
                    break;

                case AoiEventKind.SelfState when evt.Entity is { } self &&
                     self.ActorId == bound.ActorId &&
                     self.ConnectionEpoch == bound.ConnectionEpoch &&
                     self.MapEpoch == bound.MapEpoch:
                    frame = WireProtocol.EncodeEntityState(WireOpcode.SelfState,
                        new(evt.ReceiverMapEpoch, evt.Tick, self.ActorId,
                            self.ConnectionEpoch, self.MapEpoch, self.MapId,
                            self.EntityRevision, (float)self.X, (float)self.Y,
                            self.IsMoving, self.MovementActionSequence, self.Facing));
                    break;

                case AoiEventKind.Spawn or AoiEventKind.Delta
                    when evt.Entity is { } entity && entity.ActorId != bound.ActorId:
                    frame = WireProtocol.EncodeEntityState(
                        evt.Kind == AoiEventKind.Spawn
                            ? WireOpcode.EntitySpawn : WireOpcode.EntityDelta,
                        new(evt.ReceiverMapEpoch, evt.Tick, entity.ActorId,
                            entity.ConnectionEpoch, entity.MapEpoch, entity.MapId,
                            entity.EntityRevision, (float)entity.X, (float)entity.Y,
                            entity.IsMoving, entity.MovementActionSequence, entity.Facing));
                    break;

                case AoiEventKind.Despawn
                    when evt.EntityActorId > 0 && evt.EntityConnectionEpoch > 0:
                    frame = WireProtocol.EncodeEntityDespawn(new(
                        evt.ReceiverMapEpoch, evt.Tick, evt.EntityActorId,
                        evt.EntityConnectionEpoch));
                    break;

                case AoiEventKind.MonsterSpawn or AoiEventKind.MonsterDelta
                    when evt.Monster is { } monster &&
                         monster.EntityId != bound.ActorId:
                    frame = WireProtocol.EncodeMonsterState(
                        evt.Kind == AoiEventKind.MonsterSpawn
                            ? WireOpcode.MonsterSpawn : WireOpcode.MonsterDelta,
                        new(evt.ReceiverMapEpoch, evt.Tick, monster.EntityId,
                            monster.EnemyId, monster.SourceIndex, monster.MapId,
                            (float)monster.X, (float)monster.Y, monster.EntityRevision,
                            monster.CurrentHp, monster.MaxHp, monster.State,
                            monster.Facing, monster.TargetActorId));
                    break;

                case AoiEventKind.MonsterDespawn
                    when evt.MonsterEntityId > 0 && evt.MonsterRevision > 0:
                    frame = WireProtocol.EncodeMonsterDespawn(new(
                        evt.ReceiverMapEpoch, evt.Tick, evt.MonsterEntityId,
                        evt.MonsterRevision));
                    break;

                case AoiEventKind.GroundDropSpawn when evt.GroundDrop is { } drop:
                    frame=WireProtocol.EncodeGroundDropSpawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,drop.DropId,
                        drop.MapId,(float)drop.X,(float)drop.Y,
                        drop.ItemDefinitionId,drop.Quantity));
                    break;
                case AoiEventKind.GroundDropDespawn when evt.GroundDropId>0:
                    frame=WireProtocol.EncodeGroundDropDespawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,evt.GroundDropId));
                    break;
                case AoiEventKind.PlayerAttackVisual when evt.PlayerAttack is { } attack:
                    frame=WireProtocol.EncodePlayerAttackVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        attack.AttackerActorId,attack.AttackerConnectionEpoch,
                        attack.AttackSequence,attack.TargetEntityId,
                        attack.Status,attack.Damage,attack.TargetRevision,
                        attack.Facing));
                    break;
                case AoiEventKind.PlayerSkillVisual when evt.PlayerSkill is { } skill:
                    frame=WireProtocol.EncodePlayerSkillVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        skill.AttackerActorId,skill.AttackerConnectionEpoch,
                        skill.SkillSequence,skill.SkillId,skill.TargetEntityId,
                        skill.Status,skill.Damage,skill.TargetRevision,
                        skill.Facing));
                    break;
                case AoiEventKind.PlayerDamageVisual when evt.PlayerDamage is { } damage:
                    frame=WireProtocol.EncodePlayerDamageVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        damage.VictimActorId,damage.VictimConnectionEpoch,
                        damage.DamageSequence,damage.AttackerMonsterEntityId,
                        damage.AttackerMonsterRevision,damage.Damage,
                        damage.Died,damage.Facing));
                    break;
                case AoiEventKind.PlayerRespawnVisual when evt.PlayerRespawn is { } respawn:
                    frame=WireProtocol.EncodePlayerRespawnVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        respawn.ActorId,respawn.ConnectionEpoch,
                        respawn.RespawnSequence,respawn.Facing));
                    break;
                case AoiEventKind.PlayerIdentity when evt.PlayerIdentity is { } identity:
                    frame=WireProtocol.EncodePlayerIdentity(new(
                        evt.ReceiverMapEpoch,identity.ActorId,
                        identity.ConnectionEpoch,identity.EntityMapEpoch,
                        identity.Mode,identity.RedMan,
                        identity.ServerUnixMilliseconds,
                        identity.PinkUntilUnixMilliseconds,
                        identity.PvpUpdatedUnixMilliseconds,
                        identity.DisplayName));
                    break;
                case AoiEventKind.PvpCombatVisual when evt.PvpCombat is { } pvp:
                    frame=WireProtocol.EncodePvpCombatVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        pvp.AttackerActorId,pvp.AttackerConnectionEpoch,
                        pvp.AttackSequence,pvp.VictimActorId,
                        pvp.VictimConnectionEpoch,pvp.DamageSequence,
                        pvp.Damage,pvp.Died,pvp.AttackerFacing,pvp.VictimFacing));
                    break;
                case AoiEventKind.PvpSkillVisual when evt.PvpSkill is { } skill:
                    frame=WireProtocol.EncodePvpSkillVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        skill.AttackerActorId,skill.AttackerConnectionEpoch,
                        skill.SkillSequence,skill.SkillId,skill.VictimActorId,
                        skill.VictimConnectionEpoch,skill.DamageSequence,
                        skill.Damage,skill.Died,
                        skill.AttackerFacing,skill.VictimFacing));
                    break;
                default:
                    return GatewaySendStatus.GenerationMismatch;
            }
            if (!_reliable.TryEnqueue(frame))
                return GatewaySendStatus.Backpressured;
            if (evt.Kind == AoiEventKind.WorldReady)
            {
                _worldReadySentMapEpoch = bound.MapEpoch;
                _worldReadySentTick = evt.Tick;
                _worldReadyAckedMapEpoch = 0;
                _worldReadyAckedTick = -1;
                _resyncPending = false;
                _visibleActors.Clear();
            }
            else if(evt.Kind==AoiEventKind.Spawn&&evt.Entity is { } spawned)
                _visibleActors[spawned.ActorId]=spawned.ConnectionEpoch;
            else if(evt.Kind==AoiEventKind.Despawn&&
                    _visibleActors.TryGetValue(evt.EntityActorId,out var seen)&&
                    seen==evt.EntityConnectionEpoch)
                _visibleActors.Remove(evt.EntityActorId);
            return GatewaySendStatus.Enqueued;
        }
    }

    public bool TryDequeueReliable(out byte[] frame)
    {
        if (IsClosed)
        {
            frame = Array.Empty<byte>();
            return false;
        }
        return _reliable.TryDequeue(out frame);
    }

    public async ValueTask<byte[]?> DequeueReliableAsync(CancellationToken cancellationToken)
    {
        if (IsClosed) return null;
        return await _reliable.DequeueAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Close()
    {
        lock (_bindingGate)
        {
            if (_closed) return;
            _closed = true;
            _visibleActors.Clear();
        }
        _reliable.Clear();
    }
}
