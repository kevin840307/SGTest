using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;
using SGTest.Server.Wire;

namespace SGTest.Server.Npc;

/// <summary>
/// NPC commerce and personal storage settlement for a trusted game connection.
/// Reuses the existing authenticated gameplay gate and authoritative DB owner.
/// </summary>
internal sealed class NpcEconomyHandler
{
    private readonly SqliteIdentityStore _identity;
    private readonly ServerWorldHost _world;
    private readonly PlayerCombatSettlement? _combat;
    private readonly long _pickupWorldGeneration;
    private readonly NpcShopCatalog? _shops;
    private readonly NpcStorageCatalog? _storages;
    private readonly NpcBankCatalog? _banks;
    private readonly WorldResultRouter _router;
    private readonly SemaphoreSlim _gameplayGate;

    internal NpcEconomyHandler(
        SqliteIdentityStore identity, ServerWorldHost world,
        PlayerCombatSettlement? combat, long pickupWorldGeneration,
        NpcShopCatalog? shops, NpcStorageCatalog? storages,
        NpcBankCatalog? banks, WorldResultRouter router, SemaphoreSlim gameplayGate)
    {
        _identity = identity;
        _world = world;
        _combat = combat;
        _pickupWorldGeneration = pickupWorldGeneration;
        _shops = shops;
        _storages = storages;
        _banks = banks;
        _router = router;
        _gameplayGate = gameplayGate;
    }

    internal async Task HandleShopAsync(TrustedShopCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                if(request.Kind==TrustedShopCommandKind.Open)
                {
                    var stale=_router.QueueShopSnapshot(
                        request,ShopOpenStatus.StaleGeneration,0,
                        Array.Empty<NpcShopCatalog.Item>());
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale shop snapshot could not be queued: "+stale);
                }
                else
                {
                    var kind=request.Kind==TrustedShopCommandKind.Buy
                        ? ShopTransactionKind.Buy:ShopTransactionKind.Sell;
                    var staleResult=new ShopTransactionResult(
                        ShopTransactionStatus.StaleGeneration,kind,
                        request.NpcSpawnId,Math.Max(0,request.ItemUid),
                        request.ItemDefinitionId,request.Quantity,0,0);
                    var stale=_router.QueueShopTransactionResult(request,staleResult);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale shop result could not be queued: "+stale);
                }
                return;
            }

            bool dead=_combat?.IsIncapacitated(actor) ?? actor.CombatDead;
            NpcShopCatalog.Shop? shop=null;
            ShopAccessStatus access;
            if(dead)access=ShopAccessStatus.PlayerDead;
            else if(_shops is null)
                access=ShopAccessStatus.UnknownShop;
            else
            {
                access=_shops.Access(actor,request.NpcSpawnId);
                if(access==ShopAccessStatus.Allowed)
                    _shops.TryGet(actor.MapId,request.NpcSpawnId,out shop!);
            }

            if(request.Kind==TrustedShopCommandKind.Open)
            {
                ShopOpenStatus status=access switch
                {
                    ShopAccessStatus.Allowed=>ShopOpenStatus.Opened,
                    ShopAccessStatus.UnknownShop=>ShopOpenStatus.UnknownShop,
                    ShopAccessStatus.TooFar=>ShopOpenStatus.TooFar,
                    ShopAccessStatus.PlayerDead=>ShopOpenStatus.PlayerDead,
                    _=>ShopOpenStatus.StaleGeneration
                };
                long money=status==ShopOpenStatus.Opened
                    ? await _identity.GetCharacterMoneyAsync(actor.ActorId)
                        .ConfigureAwait(false):0;
                var items=status==ShopOpenStatus.Opened
                    ? shop!.Items.Values.ToArray()
                    : Array.Empty<NpcShopCatalog.Item>();
                var sent=_router.QueueShopSnapshot(request,status,money,items);
                if(sent!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable shop snapshot could not be queued: "+sent);
                return;
            }

            int itemDefinitionId=request.ItemDefinitionId;
            TrustedShopPrice price;
            if(shop is not null&&_shops is not null)
                price=_shops.Price(shop,itemDefinitionId);
            else
                price=new TrustedShopPrice(
                    request.NpcSpawnId,itemDefinitionId,0,0,
                    "shop-unavailable");

            long durableConnection=_pickupWorldGeneration==0
                ? actor.ConnectionEpoch
                : SqliteIdentityStore.ComposePickupConnectionEpoch(
                    _pickupWorldGeneration,actor.ConnectionEpoch);
            var context=new TrustedShopContext(
                actor.ActorId,durableConnection,actor.MapEpoch,
                request.ShopSequence);
            ShopTransactionResult result=request.Kind switch
            {
                TrustedShopCommandKind.Buy=>
                    await _identity.BuyFromShopAsync(
                        context,access,price,request.Quantity)
                        .ConfigureAwait(false),
                TrustedShopCommandKind.Sell=>
                    await _identity.SellToShopAsync(
                        context,access,price,request.ItemUid,
                        request.Quantity).ConfigureAwait(false),
                _=>throw new InvalidOperationException(
                    "Unhandled trusted shop command")
            };
            var queued=_router.QueueShopTransactionResult(request,result);
            if(queued!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable shop transaction result could not be queued: "+queued);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleStorageAsync(TrustedStorageCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                if(request.Kind==TrustedStorageCommandKind.Open)
                {
                    var stale=_router.QueueStorageSnapshot(
                        request,StorageOpenStatus.StaleGeneration,
                        Array.Empty<StoredItem>());
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale storage snapshot could not be queued: "+stale);
                }
                else
                {
                    var kind=request.Kind==TrustedStorageCommandKind.Deposit
                        ? StorageTransferKind.Deposit:StorageTransferKind.Withdraw;
                    var staleResult=new StorageTransferResult(
                        StorageTransferStatus.StaleGeneration,kind,
                        request.ItemUid,0,0,0);
                    var stale=_router.QueueStorageTransferResult(request,staleResult);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale storage result could not be queued: "+stale);
                }
                return;
            }

            bool incapacitated = _combat?.IsIncapacitated(actor) ?? actor.CombatDead;
            StorageAccessStatus access=incapacitated
                ? StorageAccessStatus.PlayerDead
                : _storages is null
                    ? StorageAccessStatus.UnknownStorage
                    : _storages.Access(actor,request.NpcSpawnId);

            if(request.Kind==TrustedStorageCommandKind.Open)
            {
                StorageOpenStatus status=access switch
                {
                    StorageAccessStatus.Allowed=>StorageOpenStatus.Opened,
                    StorageAccessStatus.UnknownStorage=>StorageOpenStatus.UnknownStorage,
                    StorageAccessStatus.TooFar=>StorageOpenStatus.TooFar,
                    StorageAccessStatus.PlayerDead=>StorageOpenStatus.PlayerDead,
                    _=>StorageOpenStatus.StaleGeneration
                };
                IReadOnlyList<StoredItem> items=Array.Empty<StoredItem>();
                if(status==StorageOpenStatus.Opened)
                {
                    items=await _identity.ListStorageItemsAsync(
                        actor.ActorId,WireProtocol.StorageSnapshotMaxItems+1)
                        .ConfigureAwait(false);
                    if(items.Count>WireProtocol.StorageSnapshotMaxItems)
                        throw new InvalidOperationException(
                            "Storage exceeds bounded V1 snapshot capacity");
                }
                var queued=_router.QueueStorageSnapshot(request,status,items);
                if(queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable storage snapshot could not be queued: "+queued);
                return;
            }

            var transferKind=request.Kind==TrustedStorageCommandKind.Deposit
                ? StorageTransferKind.Deposit:StorageTransferKind.Withdraw;
            StorageTransferResult result;
            if(access!=StorageAccessStatus.Allowed)
            {
                var rejected=access switch
                {
                    StorageAccessStatus.UnknownStorage=>StorageTransferStatus.UnknownStorage,
                    StorageAccessStatus.TooFar=>StorageTransferStatus.TooFar,
                    StorageAccessStatus.PlayerDead=>StorageTransferStatus.PlayerDead,
                    _=>StorageTransferStatus.StaleGeneration
                };
                result=new(rejected,transferKind,request.ItemUid,0,0,0);
            }
            else
            {
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                var context=new TrustedStorageContext(
                    actor.ActorId,durableConnection,actor.MapEpoch,
                    request.StorageSequence);
                result=request.Kind switch
                {
                    TrustedStorageCommandKind.Deposit=>
                        await _identity.DepositInventoryItemAsync(
                            context,request.ItemUid).ConfigureAwait(false),
                    TrustedStorageCommandKind.Withdraw=>
                        await _identity.WithdrawStorageItemAsync(
                            context,request.ItemUid).ConfigureAwait(false),
                    _=>throw new InvalidOperationException(
                        "Unhandled trusted storage command")
                };
            }
            var sent=_router.QueueStorageTransferResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable storage transfer result could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }

    internal async Task HandleBankAsync(TrustedBankCommand request)
    {
        var binding=new ConnectionBinding(
            request.ActorId,request.ConnectionEpoch,request.RequestMapEpoch);
        await _gameplayGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if(!_router.IsCurrentBinding(binding))return;
            var snapshot=await _world.TrustedGetActorAsync(
                request.ActorId).ConfigureAwait(false);
            if(snapshot is not { } actor||
               actor.ActorId!=request.ActorId||
               actor.ConnectionEpoch!=request.ConnectionEpoch||
               actor.MapEpoch!=request.RequestMapEpoch)
            {
                if(request.Kind==TrustedBankCommandKind.Open)
                {
                    var stale=_router.QueueBankSnapshot(
                        request,BankOpenStatus.StaleGeneration,null);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale bank snapshot could not be queued: "+stale);
                }
                else
                {
                    var kind=request.Kind==TrustedBankCommandKind.Deposit
                        ? BankTransactionKind.Deposit:BankTransactionKind.Withdraw;
                    var staleResult=new BankTransactionResult(
                        BankTransactionStatus.StaleGeneration,kind,
                        request.NpcSpawnId,BankBalanceScope.None,
                        request.Amount,0,0);
                    var stale=_router.QueueBankTransactionResult(request,staleResult);
                    if(stale!=GatewaySendStatus.Enqueued)
                        throw new IOException(
                            "Reliable stale bank result could not be queued: "+stale);
                }
                return;
            }

            bool dead=_combat?.IsIncapacitated(actor) ?? actor.CombatDead;
            BankNpcAccessStatus access=dead
                ? BankNpcAccessStatus.PlayerDead
                : _banks is null
                    ? BankNpcAccessStatus.UnknownBank
                    : _banks.Access(actor,request.NpcSpawnId);

            if(request.Kind==TrustedBankCommandKind.Open)
            {
                BankOpenStatus status=access switch
                {
                    BankNpcAccessStatus.Allowed=>BankOpenStatus.Opened,
                    BankNpcAccessStatus.UnknownBank=>BankOpenStatus.UnknownBank,
                    BankNpcAccessStatus.TooFar=>BankOpenStatus.TooFar,
                    BankNpcAccessStatus.PlayerDead=>BankOpenStatus.PlayerDead,
                    _=>BankOpenStatus.StaleGeneration
                };
                BankBalanceSnapshot? balance=null;
                if(status==BankOpenStatus.Opened)
                {
                    if(_banks is null||
                       !_banks.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                        throw new InvalidOperationException("Bank rule vanished after access check");
                    var scope=point.Scope==BankScopePolicy.Account
                        ? BankBalanceScope.Account:BankBalanceScope.Character;
                    balance=await _identity.GetBankBalanceAsync(
                        actor.ActorId,scope).ConfigureAwait(false);
                }
                var queued=_router.QueueBankSnapshot(request,status,balance);
                if(queued!=GatewaySendStatus.Enqueued)
                    throw new IOException(
                        "Reliable bank snapshot could not be queued: "+queued);
                return;
            }

            var transferKind=request.Kind==TrustedBankCommandKind.Deposit
                ? BankTransactionKind.Deposit:BankTransactionKind.Withdraw;
            BankTransactionResult result;
            if(access!=BankNpcAccessStatus.Allowed)
            {
                var rejected=access switch
                {
                    BankNpcAccessStatus.UnknownBank=>BankTransactionStatus.UnknownBank,
                    BankNpcAccessStatus.TooFar=>BankTransactionStatus.TooFar,
                    BankNpcAccessStatus.PlayerDead=>BankTransactionStatus.PlayerDead,
                    _=>BankTransactionStatus.StaleGeneration
                };
                result=new(rejected,transferKind,request.NpcSpawnId,
                    BankBalanceScope.None,request.Amount,0,0);
            }
            else
            {
                if(_banks is null||
                   !_banks.TryGet(actor.MapId,request.NpcSpawnId,out var point))
                    throw new InvalidOperationException("Bank rule vanished after access check");
                var scope=point.Scope==BankScopePolicy.Account
                    ? BankBalanceScope.Account:BankBalanceScope.Character;
                long durableConnection=_pickupWorldGeneration==0
                    ? actor.ConnectionEpoch
                    : SqliteIdentityStore.ComposePickupConnectionEpoch(
                        _pickupWorldGeneration,actor.ConnectionEpoch);
                var context=new TrustedBankContext(
                    actor.ActorId,durableConnection,actor.MapEpoch,
                    request.BankSequence);
                var rule=new TrustedBankRule(
                    point.NpcSpawnId,scope,point.SourceReference);
                result=request.Kind switch
                {
                    TrustedBankCommandKind.Deposit=>
                        await _identity.DepositBankAsync(
                            context,BankAccessStatus.Allowed,rule,
                            request.Amount).ConfigureAwait(false),
                    TrustedBankCommandKind.Withdraw=>
                        await _identity.WithdrawBankAsync(
                            context,BankAccessStatus.Allowed,rule,
                            request.Amount).ConfigureAwait(false),
                    _=>throw new InvalidOperationException(
                        "Unhandled trusted bank command")
                };
            }
            var sent=_router.QueueBankTransactionResult(request,result);
            if(sent!=GatewaySendStatus.Enqueued)
                throw new IOException(
                    "Reliable bank transaction result could not be queued: "+sent);
        }
        finally { _gameplayGate.Release(); }
    }
}
