using SGTest.Server.Trade;
using SGTest.Server.Visibility;
using System.Collections.Concurrent;
using System.Text;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

namespace SGTest.Server.PlayerShop;

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
    private readonly ConcurrentDictionary<long,LiveShop> _liveShops = new();

    private sealed record LiveShop(ConnectionBinding Binding,uint Revision,string Name);

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
        // Shops are explicitly online-only. A process/socket death can leave
        // durable active rows; a new authenticated session closes the complete
        // stale stall instead of silently reviving any old listing.
        await _store.CloseAllPlayerShopListingsAsync(binding.ActorId)
            .ConfigureAwait(false);
        _liveShops.TryRemove(binding.ActorId,out _);
    }

    public async Task CloseForBindingAsync(
        ConnectionBinding binding,WorldResultRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if(!router.IsCurrentBinding(binding))return;
        await _store.CloseAllPlayerShopListingsAsync(binding.ActorId)
            .ConfigureAwait(false);
        if(_liveShops.TryGetValue(binding.ActorId,out var live)&&
           live.Binding==binding&&
           _liveShops.TryRemove(binding.ActorId,out var removed))
        {
            uint revision=removed.Revision==uint.MaxValue?uint.MaxValue:removed.Revision+1;
            if(!router.TryPostPlayerShopWorldState(
                PlayerShopWorldStateUpdate.Closed(binding,revision)))
                throw new AoiBackpressureException(
                    "Player-shop close presentation queue full");
        }
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
            if(current.CombatDead&&command.Action is not (PlayerShopCommandAction.Close or PlayerShopCommandAction.CloseAll))
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.PlayerDead));

            switch(command.Action)
            {
                case PlayerShopCommandAction.Open:
                {
                    if(!_liveShops.TryGetValue(command.ActorId,out var liveShop)||
                       liveShop.Binding!=binding)
                        return Queue(command,router,Result(
                            command,PlayerShopResultStatus.NoShop,
                            sellerActorId:command.ActorId));
                    var created=await _store.CreatePlayerShopListingAsync(
                        command.ActorId,command.ItemUid,command.Quantity,
                        command.Price,_itemRules)
                        .ConfigureAwait(false);
                    var status=created.Status switch
                    {
                        PlayerShopCreateStatus.Created=>PlayerShopResultStatus.Applied,
                        PlayerShopCreateStatus.AlreadyOpen=>PlayerShopResultStatus.AlreadyOpen,
                        PlayerShopCreateStatus.NotOwned=>PlayerShopResultStatus.NotOwned,
                        PlayerShopCreateStatus.NotTradeable=>PlayerShopResultStatus.NotTradeable,
                        PlayerShopCreateStatus.CapacityReached=>PlayerShopResultStatus.CapacityReached,
                        PlayerShopCreateStatus.InvalidQuantity=>PlayerShopResultStatus.InvalidQuantity,
                        _=>throw new InvalidOperationException("Unknown player-shop create result")
                    };
                    long wallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    var result=Queue(command,router,Result(
                        command,status,created.Listing,command.ActorId,wallet,wallet));
                    if(status==PlayerShopResultStatus.Applied)
                        await QueueInventorySnapshotAsync(command,router,wallet)
                            .ConfigureAwait(false);
                    return result;
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
                    var result=Queue(command,router,Result(
                        command,status,null,command.ActorId,wallet,wallet));
                    if(status is PlayerShopResultStatus.Applied or
                                 PlayerShopResultStatus.AlreadyApplied)
                        await QueueInventorySnapshotAsync(command,router,wallet)
                            .ConfigureAwait(false);
                    return result;
                }
                case PlayerShopCommandAction.SetName:
                {
                    string name=(command.Name??string.Empty).Trim();
                    int bytes=Encoding.UTF8.GetByteCount(name);
                    if(string.IsNullOrWhiteSpace(name)||
                       name.EnumerateRunes().Count() is <1 or >20||
                       bytes is <1 or >WireProtocol.PlayerShopMaximumNameBytes)
                        return Queue(command,router,Result(
                            command,PlayerShopResultStatus.InvalidName,
                            sellerActorId:command.ActorId,
                            sellerWallet:await _store.GetCharacterMoneyAsync(command.ActorId)
                                .ConfigureAwait(false),
                            buyerWallet:await _store.GetCharacterMoneyAsync(command.ActorId)
                                .ConfigureAwait(false)));

                    uint revision=1;
                    if(_liveShops.TryGetValue(command.ActorId,out var prior)&&
                       prior.Binding==binding)
                    {
                        if(prior.Revision==uint.MaxValue)
                            return Queue(command,router,Result(
                                command,PlayerShopResultStatus.StaleSequence,
                                sellerActorId:command.ActorId));
                        revision=prior.Revision+1;
                    }
                    var update=PlayerShopWorldStateUpdate.Open(binding,revision,name);
                    if(!router.TryPostPlayerShopWorldState(update))
                        throw new AoiBackpressureException(
                            "Player-shop presentation queue full");
                    _liveShops[command.ActorId]=new(binding,revision,name);
                    long wallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    return Queue(command,router,Result(
                        command,PlayerShopResultStatus.Applied,null,
                        command.ActorId,wallet,wallet));
                }
                case PlayerShopCommandAction.CloseAll:
                {
                    int closed=await _store.CloseAllPlayerShopListingsAsync(
                        command.ActorId).ConfigureAwait(false);
                    bool hadLive=_liveShops.TryGetValue(command.ActorId,out var live)&&
                        live.Binding==binding;
                    if(hadLive&&live is not null)
                    {
                        _liveShops.TryRemove(command.ActorId,out _);
                        uint revision=live.Revision==uint.MaxValue
                            ?uint.MaxValue
                            :live.Revision+1;
                        if(!router.TryPostPlayerShopWorldState(
                            PlayerShopWorldStateUpdate.Closed(binding,revision)))
                        {
                            router.CloseCurrentBinding(binding);
                            throw new AoiBackpressureException(
                                "Player-shop close presentation queue full");
                        }
                    }
                    long wallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    var result=Queue(command,router,Result(
                        command,(closed>0||hadLive)
                            ?PlayerShopResultStatus.Applied
                            :PlayerShopResultStatus.AlreadyApplied,
                        null,command.ActorId,wallet,wallet));
                    await QueueInventorySnapshotAsync(command,router,wallet)
                        .ConfigureAwait(false);
                    return result;
                }
                case PlayerShopCommandAction.Inspect:
                {
                    // MapEpoch is scoped to each actor's own map/session
                    // lifecycle. AOI visibility already validates the seller's
                    // exact generation; peers on the same MapId need not have
                    // numerically equal epochs.
                    if(command.SellerActorId==command.ActorId||
                       !router.TryGetCurrentBinding(command.SellerActorId,out var seller)||
                       !router.IsVisibleActor(binding,seller))
                        return Queue(command,router,Result(
                            command,PlayerShopResultStatus.NotVisible,
                            sellerActorId:command.SellerActorId));
                    if(!_liveShops.TryGetValue(command.SellerActorId,out var sellerShop)||
                       sellerShop.Binding!=seller)
                        return Queue(command,router,Result(
                            command,PlayerShopResultStatus.NoShop,
                            sellerActorId:command.SellerActorId));
                    var listings=await _store.GetActivePlayerShopListingsAsync(
                        command.SellerActorId).ConfigureAwait(false);
                    long buyerWallet=await _store.GetCharacterMoneyAsync(command.ActorId)
                        .ConfigureAwait(false);
                    if(listings.Count>0)
                    {
                        var snapshot=new PlayerShopSnapshotWire(
                            command.RequestMapEpoch,command.ShopSequence,
                            command.SellerActorId,buyerWallet,
                            listings.Select(ToWireListing).ToArray());
                        var snapshotStatus=router.QueuePlayerShopSnapshot(command,snapshot);
                        if(snapshotStatus!=GatewaySendStatus.Enqueued)
                            throw new IOException(
                                "Reliable player-shop snapshot could not be queued: "+snapshotStatus);
                    }
                    var first=listings.Count==0?null:listings[0];
                    return Queue(command,router,Result(
                        command,first is null
                            ?PlayerShopResultStatus.NoShop
                            :PlayerShopResultStatus.Applied,
                        first,command.SellerActorId,0,buyerWallet));
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
        if(!router.TryGetCurrentBinding(command.SellerActorId,out var sellerBinding)||
           !router.IsVisibleActor(buyerBinding,sellerBinding))
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.NotVisible,
                sellerActorId:command.SellerActorId));
        if(!_liveShops.TryGetValue(command.SellerActorId,out var sellerShop)||
           sellerShop.Binding!=sellerBinding)
            return Queue(command,router,Result(
                command,PlayerShopResultStatus.NoShop,
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
               !router.IsVisibleActor(buyerBinding,sellerBinding)||
               (_combat is not null&&
                (!_combat.TryGetSuspendedCombatState(b,out int buyerHp,out _)||
                 buyerHp<=0||
                 !_combat.TryGetSuspendedCombatState(s,out int sellerHp,out _)||
                 sellerHp<=0)))
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.StaleGeneration,
                    sellerActorId:command.SellerActorId));

            var active=await _store.GetPlayerShopListingAsync(command.ListingId)
                .ConfigureAwait(false);
            if(active is null||active.SellerCharacterId!=sellerBinding.ActorId)
                return Queue(command,router,Result(
                    command,PlayerShopResultStatus.NoShop,
                    sellerActorId:command.SellerActorId));
            if(active.State!=PlayerShopListingState.Active)
                return Queue(command,router,Result(
                    command,active.State switch
                    {
                        PlayerShopListingState.Sold=>PlayerShopResultStatus.Sold,
                        PlayerShopListingState.Closed=>PlayerShopResultStatus.Closed,
                        _=>PlayerShopResultStatus.StaleListing
                    },active,command.SellerActorId));

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

            // Selling the last active listing closes the live stall presentation.
            // The durable sale is already committed and both reliable receipts are
            // queued; removing the empty sign prevents peers from browsing a ghost
            // shop. The seller gameplay gate is still held, so a concurrent reopen
            // cannot race this check.
            if(status is PlayerShopResultStatus.Applied or
                         PlayerShopResultStatus.AlreadyApplied)
            {
                var remaining=await _store.GetActivePlayerShopListingsAsync(
                    sellerBinding.ActorId).ConfigureAwait(false);
                if(remaining.Count==0&&
                   _liveShops.TryGetValue(sellerBinding.ActorId,out var live)&&
                   live.Binding==sellerBinding&&
                   _liveShops.TryRemove(
                       new KeyValuePair<long,LiveShop>(sellerBinding.ActorId,live)))
                {
                    uint revision=live.Revision==uint.MaxValue
                        ?uint.MaxValue
                        :live.Revision+1;
                    if(!router.TryPostPlayerShopWorldState(
                        PlayerShopWorldStateUpdate.Closed(
                            sellerBinding,revision)))
                    {
                        router.CloseCurrentBinding(sellerBinding);
                        throw new AoiBackpressureException(
                            "Sold-out player-shop close presentation queue full");
                    }
                }
            }
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

    private static PlayerShopListingWire ToWireListing(PlayerShopListing listing)=>
        new(listing.ListingId,listing.ItemUid,listing.ItemDefinitionId,
            listing.Quantity,listing.SourceDropId,listing.DurabilityRaw??-1,
            listing.Price,listing.EnhancementLevel);

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
            listing?.Price??0,sellerWallet,buyerWallet,
            listing?.EnhancementLevel??0);

    private async Task QueueInventorySnapshotAsync(
        TrustedPlayerShopCommand command,WorldResultRouter router,long wallet)
    {
        var inventory=await _store.ListInventoryItemsAsync(
            command.ActorId,WireProtocol.InventorySnapshotMaxItems+1)
            .ConfigureAwait(false);
        if(inventory.Count>WireProtocol.InventorySnapshotMaxItems)
            throw new InvalidOperationException(
                "Character inventory exceeds bounded V1 baseline capacity");
        var items=inventory.Select(item=>new InventoryItemWire(
            item.ItemUid,item.ItemDefinitionId,item.Quantity,item.SourceDropId,
            item.EnhancementLevel,item.DurabilityRaw??-1)).ToArray();
        var queued=router.QueuePlayerShopInventorySnapshot(command,items,wallet);
        if(queued!=GatewaySendStatus.Enqueued)
        {
            if(router.TryGetCurrentBinding(command.ActorId,out var binding))
                router.CloseCurrentBinding(binding);
            throw new IOException(
                "Reliable player-shop inventory snapshot could not be queued: "+queued);
        }
    }

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
