using SGTest.Server.Visibility;
using SGTest.Server.Trade;
using SGTest.Server.PlayerShop;
using SGTest.Server.Map;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class PlayerShopLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_player_shop_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            var grid=BlockingGrid.Load(Program.CreateMap(root,15),15);
            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync().ConfigureAwait(false);

            long sellerAccount=(await store.CreateAccountAsync(
                "pshop_one",Program.Password)).AccountId;
            long buyerAccount=(await store.CreateAccountAsync(
                "pshop_two",Program.Password)).AccountId;
            long seller=(await store.CreateCharacterAsync(
                sellerAccount,0,"PlayerShopSeller",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("player-shop seller missing");
            long buyer=(await store.CreateCharacterAsync(
                buyerAccount,0,"PlayerShopBuyer",new CharacterSpawn(15,24,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("player-shop buyer missing");
            SeedWallet(db,seller,100);
            SeedWallet(db,buyer,1000);

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var seeded=await SeedItem(
                store,generation,seller,6401,2,1,8,8).ConfigureAwait(false);
            SetDurability(db,seeded.ItemUid,80);
            SetEnhancementLevel(db,seeded.ItemUid,6);
            var rules=new Dictionary<int,TradeItemRule>
            {
                [6401]=new(6401,true,"ci:godot-player-shop-6401")
            };
            var trades=new PlayerTradeCoordinator(store,world,rules);
            var shops=new PlayerShopCoordinator(store,world,rules,trades);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,
                trades:trades,playerShops:shops);

            using var certificate=Program.CreateCertificate();
            string certPath=Path.Combine(root,"fixture-cert.pem");
            Program.WriteCertificatePem(certPath,certificate);
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(2);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"PLAYER_SHOP_FIXTURE_READY port={port} cert={certPath} " +
                    $"seller={seller} buyer={buyer} item_uid={seeded.ItemUid} " +
                    $"drop_id={seeded.DropId} enhancement=6");
                Console.Out.Flush();

                using var sellerSocket=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var sellerTask=authorization.RunAuthenticatedSocketAsync(
                    sellerSocket,certificate,router,
                    new TlsWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));
                _=sellerTask.ContinueWith(task=>
                {
                    if(task.IsCompletedSuccessfully)
                    {
                        var result=task.Result;
                        Console.WriteLine(
                            $"PLAYER_SHOP_FIXTURE_SELLER_END reason={result.Reason} " +
                            $"commands={result.CommandsAccepted} reliable={result.ReliableFramesSent} " +
                            $"fault={result.Fault??"-"}");
                    }
                    else
                    {
                        Console.WriteLine(
                            "PLAYER_SHOP_FIXTURE_SELLER_END faulted="+
                            task.Exception?.GetBaseException().Message);
                    }
                    Console.Out.Flush();
                },TaskScheduler.Default);

                using var buyerSocket=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var buyerTask=authorization.RunAuthenticatedSocketAsync(
                    buyerSocket,certificate,router,
                    new TlsWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000));

                var ended=await Task.WhenAll(sellerTask,buyerTask)
                    .WaitAsync(TimeSpan.FromSeconds(35)).ConfigureAwait(false);

                long sellerWallet=await store.GetCharacterMoneyAsync(seller)
                    .ConfigureAwait(false);
                long buyerWallet=await store.GetCharacterMoneyAsync(buyer)
                    .ConfigureAwait(false);
                var sellerInventory=await store.ListInventoryItemsAsync(seller)
                    .ConfigureAwait(false);
                var buyerInventory=await store.ListInventoryItemsAsync(buyer)
                    .ConfigureAwait(false);
                var active=await store.GetActivePlayerShopListingAsync(seller)
                    .ConfigureAwait(false);
                var raw=ReadItem(db,seeded.ItemUid);

                if(sellerWallet!=400||buyerWallet!=700||
                   sellerInventory.Any(x=>x.ItemUid==seeded.ItemUid)||
                   buyerInventory.Count(x=>x.ItemUid==seeded.ItemUid)!=1||
                   buyerInventory.Single(x=>x.ItemUid==seeded.ItemUid)
                       .ItemDefinitionId!=6401||
                   buyerInventory.Single(x=>x.ItemUid==seeded.ItemUid)
                       .Quantity!=2||
                   buyerInventory.Single(x=>x.ItemUid==seeded.ItemUid)
                       .SourceDropId!=seeded.DropId||
                   buyerInventory.Single(x=>x.ItemUid==seeded.ItemUid)
                       .EnhancementLevel!=6||
                   raw.Owner!=buyer||raw.DurabilityRaw!=80||
                   raw.EnhancementLevel!=6||
                   active is not null||
                   ended[0].CommandsAccepted!=2||
                   ended[1].CommandsAccepted!=3||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"player-shop E2E mismatch seller_wallet={sellerWallet} " +
                        $"buyer_wallet={buyerWallet} seller_inv={sellerInventory.Count} " +
                        $"buyer_inv={buyerInventory.Count} owner={raw.Owner} " +
                        $"durability={raw.DurabilityRaw} enhancement={raw.EnhancementLevel} " +
                        $"commands={ended[0].CommandsAccepted}/{ended[1].CommandsAccepted} " +
                        $"active_shop={active is not null} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"PLAYER_SHOP_FIXTURE_PASS seller={seller} buyer={buyer} " +
                    $"item_uid={seeded.ItemUid} drop_id={seeded.DropId} " +
                    $"quantity=2 durability=80 enhancement=6 seller_wallet={sellerWallet} " +
                    $"buyer_wallet={buyerWallet} commands=2/3");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("PLAYER_SHOP_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }

    private static async Task<(long ItemUid,long DropId)> SeedItem(
        SqliteIdentityStore store,long generation,long characterId,
        int definition,int quantity,int slot,double x,double y)
    {
        var drop=await store.CreateTrustedGroundDropAsync(new(
            15,x,y,930000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "ci:godot-player-shop-seed-"+slot)).ConfigureAwait(false);
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,200+slot,1,1,15,x,y),
            drop.DropId).ConfigureAwait(false);
        if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
            throw new InvalidOperationException("player-shop seed pickup failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("player-shop wallet seed failed");
    }

    private static void SetDurability(string db,long itemUid,int durability)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE item_instances SET durability_raw=$value WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$value",durability);
        command.Parameters.AddWithValue("$uid",itemUid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("player-shop durability seed failed");
    }

    private static void SetEnhancementLevel(string db,long itemUid,int level)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE item_instances SET enhancement_level=$value WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$value",level);
        command.Parameters.AddWithValue("$uid",itemUid);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException(
                "player-shop enhancement seed failed");
    }

    private static (long Owner,int? DurabilityRaw,int EnhancementLevel) ReadItem(string db,long itemUid)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=
            "SELECT character_id,durability_raw,enhancement_level FROM item_instances WHERE item_uid=$uid;";
        command.Parameters.AddWithValue("$uid",itemUid);
        using var reader=command.ExecuteReader();
        if(!reader.Read())
            throw new InvalidOperationException("player-shop item disappeared");
        return(reader.GetInt64(0),
            reader.IsDBNull(1)?null:reader.GetInt32(1),reader.GetInt32(2));
    }

    private static SqliteConnection Open(string db)
    {
        var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        return connection;
    }
}
