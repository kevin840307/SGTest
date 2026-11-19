using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.Gateway;

/// <summary>
/// Online-only player-shop coordinator. Listing/item/wallet mutation is durable
/// SQLite state; AOI visibility and authenticated generations remain ephemeral.
/// Purchases reuse the exact gameplay semaphores already registered for player
/// trade, so portal/release and combat cannot overtake a two-party commit.
/// </summary>
public sealed class PlayerShopCoordinator
{
    private static readonly TimeSpan GateTimeout=TimeSpan.FromSeconds(10);
    private readonly SqliteIdentityStore _store;
    private readonly ServerWorldHost _world;
    private readonly IReadOnlyDictionary<int,TradeItemRule> _itemRules;
    private readonly PlayerCombatSettlement? _combat;
    private readonly PlayerTradeCoordinator _gates;

    public PlayerShopCoordinator(
        SqliteIdentityStore store,ServerWorldHost world,
        IReadOnlyDictionary<int,TradeItemRule> itemRules,
        PlayerTradeCoordinator gameplayGates,
        PlayerCombatSettlement? combat=null)
    {
        _store=store??throw new ArgumentNullException(nameof(store));
        _world=world??throw new ArgumentNullException(nameof(world));
        _itemRules=itemRules??throw new ArgumentNullException(nameof(itemRules));
        _gates=gameplayGates??throw new ArgumentNullException(nameof(gameplayGates));
        _combat=combat;
    }

    public async Task AttachAsync(ConnectionBinding binding)
    {
        // Shops are explicitly online-only in this first slice. A process/socket
        // death can leave an active durable row; a new authenticated session
        // closes that stale row instead of silently reviving an old stall.
        var active=await _store.GetActivePlayerShopListingAsync(binding.ActorId)
            .ConfigureAwait(false);
        if(active is not null)
            await _store.ClosePlayerShopListingAsync(
                binding.ActorId,active.ListingId).ConfigureAwait(false);
    }

    public async Task CloseForBindingAsync(
        ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.IsCurrentBinding(binding))return;
        var active=await _store.GetActivePlayerShopListingAsync(binding.ActorId)
            .ConfigureAwait(false);
        if(active is not null)
            await _store.ClosePlayerShopListingAsync(
                binding.ActorId,active.ListingId).ConfigureAwait(false);
    }

    public async Task<PlayerShopResultWire> HandleNewAsync(
        TrustedPlayerShopCommand command,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        return command.Action==PlayerShopCommandAction.Buy
            ?await BuyAsync(command,router).ConfigureAwait(false)
            :await OnePartyAsync(command,router).ConfigureAwait(false);
    }

    private async Task<PlayerShopResultWire> OnePartyAsync(
        TrustedPlayerShopCommand command,WorldResultRouter router)
    {
        var binding=new ConnectionBinding(
            command.ActorId,command.ConnectionEpoch,command.RequestMapEpoch);
        if(!_gates.TryResolveGameplayGate(binding,out var gate)||gate is null)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.StaleGeneration));

        bool held=await gate.WaitAsync(GateTimeout).ConfigureAwait(false);
        if(!held)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.StaleGeneration));
        try
        {
            var actor=await _world.TrustedGetActorAsync(command.ActorId)
                .ConfigureAwait(false);
            if(actor is not { } current||
               ConnectionBinding.FromTrustedSnapshot(current)!=binding||
               !router.IsCurrentBinding(binding))
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleGeneration));
            if(current.CombatDead&&command.Action!=PlayerShopCommandAction.Close)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.PlayerDead));

            switch(command.Action)
            {
                case PlayerShopCommandAction.Open:
                {
                    var created=await _store.CreatePlayerShopListingAsync(
                        command.ActorId,command.ItemUid,command.Price,_itemRules)
                        .ConfigureAwait(false);
                    var status=created.Status switch
                    {
                        PlayerShopCreateStatus.Created=>PlayerShopResultStatus.Applied,
                        PlayerShopCreateStatus.AlreadyOpen=>PlayerShopResultStatus.AlreadyOpen,
                        PlayerShopCreateStatus.NotOwned=>PlayerShopResultStatus.NotOwned,
                        PlayerShopCreateStatus.NotTradeable=>PlayerShopResultStatus.NotTradeable,
                        _=>throw new InvalidOperationException("Unknown player-shop create result")
                    };
                    long wallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    return Queue(command,router,Result(
                        command,status,created.Listing,command.ActorId,wallet,wallet));
                }
                case PlayerShopCommandAction.Close:
                {
                    var closed=await _store.ClosePlayerShopListingAsync(
                        command.ActorId,command.ListingId).ConfigureAwait(false);
                    var status=closed.Status switch
                    {
                        PlayerShopCloseStatus.Closed=>PlayerShopResultStatus.Applied,
                        PlayerShopCloseStatus.AlreadyClosed=>PlayerShopResultStatus.AlreadyApplied,
                        PlayerShopCloseStatus.NotFound=>PlayerShopResultStatus.NoShop,
                        _=>throw new InvalidOperationException("Unknown player-shop close result")
                    };
                    long wallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    return Queue(command,router,Result(
                        command,status,null,command.ActorId,wallet,wallet));
                }
                case PlayerShopCommandAction.Inspect:
                {
                    if(command.SellerActorId==command.ActorId||
                       !router.IsVisibleActor(binding,command.SellerActorId)||
                       !router.TryGetCurrentBinding(command.SellerActorId,out var seller)||
                       seller.MapEpoch!=binding.MapEpoch)
                        return Queue(command,router,Result(
                            command,PlayerShopResultStatus.NotVisible,
                            sellerActorId:command.SellerActorId));
                    var listing=await _store.GetActivePlayerShopListingAsync(
                        command.SellerActorId).ConfigureAwait(false);
                    long buyerWallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    return Queue(command,router,Result(
                        command,listing is null
                            ?PlayerShopResultStatus.NoShop
                            :PlayerShopResultStatus.Applied,
                        listing,command.SellerActorId,0,buyerWallet));
                }
                default:
                    throw new InvalidOperationException(
                        "Buy must use the two-party player-shop path");
            }
        }
        finally{gate.Release();}
    }

    private async Task<PlayerShopResultWire> BuyAsync(
        TrustedPlayerShopCommand command,WorldResultRouter router)
    {
        var buyerBinding=new ConnectionBinding(
            command.ActorId,command.ConnectionEpoch,command.RequestMapEpoch);
        if(command.SellerActorId==command.ActorId)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.SelfPurchase,
                sellerActorId:command.SellerActorId));
        if(!router.IsVisibleActor(buyerBinding,command.SellerActorId)||
           !router.TryGetCurrentBinding(command.SellerActorId,out var sellerBinding)||
           sellerBinding.MapEpoch!=buyerBinding.MapEpoch)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.NotVisible,
                sellerActorId:command.SellerActorId));
        if(!_gates.TryResolveGameplayGates(
                buyerBinding,sellerBinding,out var buyerGate,out var sellerGate)||
           buyerGate is null||sellerGate is null)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.StaleGeneration,
                sellerActorId:command.SellerActorId));

        SemaphoreSlim first=buyerGate,second=sellerGate;
        if(buyerBinding.ActorId>sellerBinding.ActorId)
            (first,second)=(second,first);
        bool heldFirst=false,heldSecond=false;
        bool buyerSuspended=false,sellerSuspended=false;
        try
        {
            heldFirst=await first.WaitAsync(GateTimeout).ConfigureAwait(false);
            if(!heldFirst)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleGeneration,
                    sellerActorId:command.SellerActorId));
            heldSecond=await second.WaitAsync(GateTimeout).ConfigureAwait(false);
            if(!heldSecond)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleGeneration,
                    sellerActorId:command.SellerActorId));

            if(_combat is not null)
            {
                await _combat.SuspendAndDrainAsync(
                    buyerBinding.ActorId,buyerBinding.ConnectionEpoch,buyerBinding.MapEpoch)
                    .ConfigureAwait(false);
                buyerSuspended=true;
                await _combat.SuspendAndDrainAsync(
                    sellerBinding.ActorId,sellerBinding.ConnectionEpoch,sellerBinding.MapEpoch)
                    .ConfigureAwait(false);
                sellerSuspended=true;
            }

            var buyer=await _world.TrustedGetActorAsync(buyerBinding.ActorId)
                .ConfigureAwait(false);
            var seller=await _world.TrustedGetActorAsync(sellerBinding.ActorId)
                .ConfigureAwait(false);
            if(buyer is not { } b||seller is not { } s||
               ConnectionBinding.FromTrustedSnapshot(b)!=buyerBinding||
               ConnectionBinding.FromTrustedSnapshot(s)!=sellerBinding||
               b.MapId!=s.MapId||b.CombatDead||s.CombatDead||
               !router.IsCurrentBinding(buyerBinding)||
               !router.IsCurrentBinding(sellerBinding)||
               !router.IsVisibleActor(buyerBinding,sellerBinding.ActorId)||
               (_combat is not null&&
                (!_combat.TryGetSuspendedCombatState(b,out int buyerHp,out _)||
                 buyerHp<=0||
                 !_combat.TryGetSuspendedCombatState(s,out int sellerHp,out _)||
                 sellerHp<=0)))
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleGeneration,
                    sellerActorId:command.SellerActorId));

            var active=await _store.GetActivePlayerShopListingAsync(
                sellerBinding.ActorId).ConfigureAwait(false);
            if(active is null)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.NoShop,
                    sellerActorId:command.SellerActorId));
            if(active.ListingId!=command.ListingId)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleListing,active,
                    command.SellerActorId));

            var bought=await _store.BuyPlayerShopListingAsync(
                command.ActorId,command.ListingId,_itemRules).ConfigureAwait(false);
            var status=bought.Status switch
            {
                PlayerShopBuyStatus.Applied=>PlayerShopResultStatus.Applied,
                PlayerShopBuyStatus.AlreadyApplied=>PlayerShopResultStatus.AlreadyApplied,
                PlayerShopBuyStatus.Sold=>PlayerShopResultStatus.Sold,
                PlayerShopBuyStatus.Closed=>PlayerShopResultStatus.Closed,
                PlayerShopBuyStatus.StaleListing=>PlayerShopResultStatus.StaleListing,
                PlayerShopBuyStatus.SelfPurchase=>PlayerShopResultStatus.SelfPurchase,
                PlayerShopBuyStatus.InsufficientMoney=>PlayerShopResultStatus.InsufficientMoney,
                PlayerShopBuyStatus.WalletLimit=>PlayerShopResultStatus.WalletLimit,
                PlayerShopBuyStatus.NotTradeable=>PlayerShopResultStatus.NotTradeable,
                PlayerShopBuyStatus.NotFound=>PlayerShopResultStatus.NoShop,
                _=>throw new InvalidOperationException("Unknown player-shop buy result")
            };
            var receipt=Result(
                command,status,bought.Listing,command.SellerActorId,
                bought.Listing?.SellerWalletAfter??0,
                bought.Listing?.BuyerWalletAfter??
                    await _store.GetCharacterMoneyAsync(command.ActorId).ConfigureAwait(false));
            var requesterStatus=router.QueuePlayerShopResult(command,receipt);
            bool requesterQueued=requesterStatus==GatewaySendStatus.Enqueued;

            bool sellerQueued=true;
            if((status is PlayerShopResultStatus.Applied or
                          PlayerShopResultStatus.AlreadyApplied)&&
               bought.Listing is { } sold)
            {
                sellerQueued=router.QueuePlayerShopSold(
                    sellerBinding,new(
                        sellerBinding.MapEpoch,sold.ListingId,command.ActorId,
                        sold.ItemUid,sold.SellerWalletAfter))==
                    GatewaySendStatus.Enqueued;
            }
            if(!requesterQueued)router.CloseCurrentBinding(buyerBinding);
            if(!sellerQueued)router.CloseCurrentBinding(sellerBinding);
            if(!requesterQueued||!sellerQueued)
                throw new IOException(
                    "Committed player-shop sale could not reach both reliable queues");
            return receipt;
        }
        finally
        {
            bool resumed=true;
            try
            {
                if(_combat is not null)
                {
                    if(sellerSuspended)
                        resumed=_combat.TryResumeGeneration(
                            sellerBinding.ActorId,sellerBinding.ConnectionEpoch,
                            sellerBinding.MapEpoch)&&resumed;
                    if(buyerSuspended)
                        resumed=_combat.TryResumeGeneration(
                            buyerBinding.ActorId,buyerBinding.ConnectionEpoch,
                            buyerBinding.MapEpoch)&&resumed;
                }
            }
            finally
            {
                if(heldSecond)second.Release();
                if(heldFirst)first.Release();
            }
            if(!resumed)
                throw new PlayerCombatSettlementException(
                    "Player-shop participant combat generation could not resume");
        }
    }

    private static PlayerShopResultWire Result(
        TrustedPlayerShopCommand command,PlayerShopResultStatus status,
        PlayerShopListing? listing=null,long sellerActorId=0,
        long sellerWallet=0,long buyerWallet=0)=>
        new(
            command.RequestMapEpoch,command.ShopSequence,command.Action,status,
            sellerActorId!=0?sellerActorId:listing?.SellerCharacterId??0,
            listing?.ListingId??command.ListingId,
            listing?.ItemUid??0,listing?.ItemDefinitionId??0,
            listing?.Quantity??0,listing?.SourceDropId??0,
            listing?.DurabilityRaw??-1,
            listing?.Price??0,sellerWallet,buyerWallet);

    private static PlayerShopResultWire Queue(
        TrustedPlayerShopCommand command,WorldResultRouter router,
        PlayerShopResultWire result)
    {
        var queued=router.QueuePlayerShopResult(command,result);
        if(queued!=GatewaySendStatus.Enqueued)
            throw new IOException(
                "Reliable player-shop result could not be queued: "+queued);
        return result;
    }
}
