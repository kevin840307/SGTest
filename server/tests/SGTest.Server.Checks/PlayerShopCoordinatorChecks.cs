using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PlayerShopCoordinatorChecks
{
    public static void Verify(string root)
    {
        Checks.Run("player shop coordinator opens inspects buys and notifies seller",
            ()=>Lifecycle(root).GetAwaiter().GetResult());
        Checks.Run("player shop sequence exact replay cannot change command payload",
            Sequence);
        Checks.Run("player shop buy loses to trusted seller MapEpoch transfer without settlement",
            ()=>PortalRace(root).GetAwaiter().GetResult());
    }

    private static void Sequence()
    {
        var gate=new PlayerShopSequenceGate();
        var first=new TrustedPlayerShopCommand(
            11,5,3,1,PlayerShopCommandAction.Inspect,77,0,0,0);
        Checks.Require(gate.Inspect(first)==PlayerShopSequenceDecision.New,
            "first player-shop command rejected");
        var receipt=new PlayerShopResultWire(
            3,1,PlayerShopCommandAction.Inspect,PlayerShopResultStatus.NoShop,
            77,0,0,0,0,0,0,0,500);
        gate.Record(first,receipt);
        Checks.Require(gate.Inspect(first)==PlayerShopSequenceDecision.ExactReplay&&
            gate.LastReceipt==receipt,
            "exact player-shop replay not cached");
        Checks.Require(gate.Inspect(first with{SellerActorId=78})==
                PlayerShopSequenceDecision.Stale&&
            gate.Inspect(first with{Action=PlayerShopCommandAction.Buy,ListingId=9})==
                PlayerShopSequenceDecision.Stale,
            "same player-shop sequence changed semantic payload");
        Checks.Require(gate.Inspect(first with{
                RequestMapEpoch=4,ShopSequence=1,SellerActorId=78})==
                PlayerShopSequenceDecision.New,
            "new trusted map generation did not reset shop sequence namespace");
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(root,"player_shop_coord_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        string mapDir=Checks.Fixture(root,98,6,6,(_,_)=>0);
        await using var world=new ServerWorldHost([BlockingGrid.Load(mapDir,98)]);
        await world.StartAsync();
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long sellerAccount=(await store.CreateAccountAsync(
                "shop_coord_s","Shop-Coord-S-Password-123!")).AccountId;
            long buyerAccount=(await store.CreateAccountAsync(
                "shop_coord_b","Shop-Coord-B-Password-123!")).AccountId;
            long seller=(await store.CreateCharacterAsync(
                sellerAccount,0,"ShopCoordSeller",new(98,24,24)))
                .Character!.CharacterId;
            long buyer=(await store.CreateCharacterAsync(
                buyerAccount,0,"ShopCoordBuyer",new(98,28,24)))
                .Character!.CharacterId;
            SeedWallet(db,seller,100);
            SeedWallet(db,buyer,1000);
            long generation=await store.AllocateLootWorldGenerationAsync();
            var item=await SeedItem(store,generation,seller,6101,2,98,24,24);
            var rules=new Dictionary<int,TradeItemRule>
            { [6101]=new(6101,true,"synthetic:player-shop-coord") };

            var sellerActor=await world.TrustedRegisterAsync(seller,98,24,24)
                ??throw new Exception("seller world register");
            var buyerActor=await world.TrustedRegisterAsync(buyer,98,28,24)
                ??throw new Exception("buyer world register");
            var sellerGateway=new GatewayConnection(sellerActor);
            var buyerGateway=new GatewayConnection(buyerActor);
            var router=new WorldResultRouter();
            Checks.Require(router.TryAttach(sellerGateway)&&router.TryAttach(buyerGateway),
                "player-shop gateways attach failed");
            var trades=new PlayerTradeCoordinator(store,world,rules);
            using var sellerGate=new SemaphoreSlim(1,1);
            using var buyerGate=new SemaphoreSlim(1,1);
            Checks.Require(trades.TryRegisterGameplayGate(
                    sellerGateway.Binding,sellerGate)&&
                trades.TryRegisterGameplayGate(buyerGateway.Binding,buyerGate),
                "player-shop gameplay gates registration failed");
            var shops=new PlayerShopCoordinator(store,world,rules,trades);
            await shops.AttachAsync(sellerGateway.Binding);
            await shops.AttachAsync(buyerGateway.Binding);

            var open=new TrustedPlayerShopCommand(
                seller,sellerActor.ConnectionEpoch,sellerActor.MapEpoch,1,
                PlayerShopCommandAction.Open,0,0,item.ItemUid,300);
            var opened=await shops.HandleNewAsync(open,router);
            var openedWire=DrainResult(sellerGateway);
            Checks.Require(opened.Status==PlayerShopResultStatus.Applied&&
                openedWire==opened&&opened.ListingId>0&&
                opened.ItemUid==item.ItemUid&&opened.Quantity==2&&
                opened.SourceDropId==item.DropId&&opened.Price==300,
                "player-shop open receipt mismatch");
            long listingId=opened.ListingId;

            var inspectHidden=new TrustedPlayerShopCommand(
                buyer,buyerActor.ConnectionEpoch,buyerActor.MapEpoch,1,
                PlayerShopCommandAction.Inspect,seller,0,0,0);
            var hidden=await shops.HandleNewAsync(inspectHidden,router);
            Checks.Require(hidden.Status==PlayerShopResultStatus.NotVisible&&
                DrainResult(buyerGateway)==hidden,
                "non-AOI seller could be inspected");

            Checks.Require(buyerGateway.QueueAoi(new(
                    AoiEventKind.Spawn,10,buyerActor.MapEpoch,sellerActor))==
                    GatewaySendStatus.Enqueued,
                "player-shop seller AOI Spawn failed");
            DrainAny(buyerGateway,WireOpcode.EntitySpawn);
            var inspect=inspectHidden with{ShopSequence=2};
            var inspected=await shops.HandleNewAsync(inspect,router);
            Checks.Require(inspected.Status==PlayerShopResultStatus.Applied&&
                inspected.ListingId==listingId&&inspected.ItemUid==item.ItemUid&&
                inspected.BuyerWallet==1000&&DrainResult(buyerGateway)==inspected,
                "visible player-shop inspect mismatch");

            var buy=new TrustedPlayerShopCommand(
                buyer,buyerActor.ConnectionEpoch,buyerActor.MapEpoch,3,
                PlayerShopCommandAction.Buy,seller,listingId,0,0);
            var bought=await shops.HandleNewAsync(buy,router);
            Checks.Require(bought.Status==PlayerShopResultStatus.Applied&&
                bought.BuyerWallet==700&&bought.SellerWallet==400&&
                DrainResult(buyerGateway)==bought,
                "player-shop buy requester receipt mismatch");
            Checks.Require(sellerGateway.TryDequeueReliable(out var soldFrame)&&
                WireProtocol.TryDecodePlayerShopSold(soldFrame,out var sold)&&
                sold.ListingId==listingId&&sold.BuyerActorId==buyer&&
                sold.ItemUid==item.ItemUid&&sold.SellerWallet==400,
                "player-shop seller durable sale notice mismatch");
            Checks.Require(await store.GetCharacterMoneyAsync(seller)==400&&
                await store.GetCharacterMoneyAsync(buyer)==700&&
                (await store.ListInventoryItemsAsync(buyer))
                    .Single(x=>x.ItemUid==item.ItemUid).SourceDropId==item.DropId,
                "player-shop coordinator durable settlement mismatch");

            // New semantic sequence against the same sold listing converges on
            // durable AlreadyApplied and may re-notify seller idempotently.
            var replay=buy with{ShopSequence=4};
            var replayed=await shops.HandleNewAsync(replay,router);
            Checks.Require(replayed.Status==PlayerShopResultStatus.NoShop&&
                DrainResult(buyerGateway)==replayed,
                "sold online listing remained active for a new buy sequence");
            Checks.Require(!buyerGateway.TryDequeueReliable(out _),
                "unexpected duplicate buyer frame after sold listing");

            await shops.CloseForBindingAsync(sellerGateway.Binding,router);
            trades.UnregisterGameplayGate(sellerGateway.Binding,sellerGate);
            trades.UnregisterGameplayGate(buyerGateway.Binding,buyerGate);
            router.TryDetach(sellerGateway);
            router.TryDetach(buyerGateway);
        }
        finally
        {
            await world.StopAsync();
            Directory.Delete(dir,true);
        }
    }

    private static async Task PortalRace(string root)
    {
        string dir=Path.Combine(root,"player_shop_portal_race_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        string mapDir=Checks.Fixture(root,99,6,6,(_,_)=>0);
        await using var world=new ServerWorldHost([BlockingGrid.Load(mapDir,99)]);
        await world.StartAsync();
        try
        {
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "shop_portal","Shop-Portal-Password-123!")).AccountId;
            long seller=(await store.CreateCharacterAsync(
                account,0,"PortalSeller",new(99,24,24))).Character!.CharacterId;
            long buyer=(await store.CreateCharacterAsync(
                account,1,"PortalBuyer",new(99,28,24))).Character!.CharacterId;
            SeedWallet(db,seller,50);
            SeedWallet(db,buyer,500);
            long generation=await store.AllocateLootWorldGenerationAsync();
            var item=await SeedItem(store,generation,seller,6201,1,99,24,24);
            var rules=new Dictionary<int,TradeItemRule>
            { [6201]=new(6201,true,"synthetic:player-shop-portal") };
            var sellerActor=await world.TrustedRegisterAsync(seller,99,24,24)
                ??throw new Exception("portal seller register");
            var buyerActor=await world.TrustedRegisterAsync(buyer,99,28,24)
                ??throw new Exception("portal buyer register");
            var sellerGateway=new GatewayConnection(sellerActor);
            var buyerGateway=new GatewayConnection(buyerActor);
            var router=new WorldResultRouter();
            Checks.Require(router.TryAttach(sellerGateway)&&router.TryAttach(buyerGateway),
                "portal race gateway attach");
            var trades=new PlayerTradeCoordinator(store,world,rules);
            using var sellerGate=new SemaphoreSlim(1,1);
            using var buyerGate=new SemaphoreSlim(1,1);
            Checks.Require(trades.TryRegisterGameplayGate(sellerGateway.Binding,sellerGate)&&
                trades.TryRegisterGameplayGate(buyerGateway.Binding,buyerGate),
                "portal race gate register");
            var shops=new PlayerShopCoordinator(store,world,rules,trades);
            var open=new TrustedPlayerShopCommand(
                seller,sellerActor.ConnectionEpoch,sellerActor.MapEpoch,1,
                PlayerShopCommandAction.Open,0,0,item.ItemUid,200);
            var opened=await shops.HandleNewAsync(open,router);
            DrainResult(sellerGateway);
            Checks.Require(buyerGateway.QueueAoi(new(
                    AoiEventKind.Spawn,20,buyerActor.MapEpoch,sellerActor))==
                    GatewaySendStatus.Enqueued,"portal race seller Spawn");
            DrainAny(buyerGateway,WireOpcode.EntitySpawn);

            // Model the formal portal path: portal owns seller gameplayGate while
            // World/Gateway/gate generation advance. Buy has already captured
            // the old seller binding and waits for this same gate.
            await sellerGate.WaitAsync();
            try
            {
                var buy=new TrustedPlayerShopCommand(
                    buyer,buyerActor.ConnectionEpoch,buyerActor.MapEpoch,1,
                    PlayerShopCommandAction.Buy,seller,opened.ListingId,0,0);
                var pending=shops.HandleNewAsync(buy,router);
                await Task.Delay(20);
                Checks.Require(!pending.IsCompleted,
                    "player-shop buy did not wait behind seller portal gate");
                var moved=await world.TrustedTransferAsync(
                    seller,sellerActor.ConnectionEpoch,sellerActor.MapEpoch,99,40,24)
                    ??throw new Exception("portal race transfer");
                Checks.Require(trades.TryAdvanceGameplayGate(
                        sellerGateway.Binding,moved,sellerGate)&&
                    router.TryAdvanceTrustedMap(sellerGateway,moved),
                    "portal race trusted generations did not advance");
                sellerGate.Release();
                var result=await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Checks.Require(result.Status==PlayerShopResultStatus.StaleGeneration&&
                    DrainResult(buyerGateway)==result,
                    "seller portal did not reject stale player-shop buy");
            }
            finally
            {
                if(sellerGate.CurrentCount==0)sellerGate.Release();
            }
            Checks.Require(await store.GetCharacterMoneyAsync(seller)==50&&
                await store.GetCharacterMoneyAsync(buyer)==500&&
                (await store.ListInventoryItemsAsync(seller))
                    .Any(x=>x.ItemUid==item.ItemUid),
                "stale player-shop portal race changed money or UID");
        }
        finally
        {
            await world.StopAsync();
            Directory.Delete(dir,true);
        }
    }

    private static PlayerShopResultWire DrainResult(GatewayConnection gateway)
    {
        Checks.Require(gateway.TryDequeueReliable(out var frame)&&
            WireProtocol.TryDecodePlayerShopResult(frame,out var result),
            "missing player-shop result");
        return result;
    }

    private static void DrainAny(GatewayConnection gateway,WireOpcode opcode)
    {
        Checks.Require(gateway.TryDequeueReliable(out var frame)&&
            WireProtocol.TryReadHeader(frame,out var header)&&header.Opcode==opcode,
            "unexpected reliable fixture frame");
    }

    private static async Task<(long ItemUid,long DropId)> SeedItem(
        SqliteIdentityStore store,long generation,long characterId,
        int definition,int quantity,int mapId,int x,int y)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            mapId,x,y,970001,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "synthetic:player-shop-coordinator"));
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,700,1,1,mapId,x,y),drop.DropId);
        Checks.Require(grant.Status==PickupStatus.Granted,
            "player-shop coordinator seed pickup failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false}.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        Checks.Require(command.ExecuteNonQuery()==1,"player-shop wallet seed");
    }
}
