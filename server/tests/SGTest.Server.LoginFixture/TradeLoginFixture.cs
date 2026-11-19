using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class TradeLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_trade_login_"+Guid.NewGuid().ToString("N"));
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
            long accountA=(await store.CreateAccountAsync(
                "trade_one",Program.Password)).AccountId;
            long accountB=(await store.CreateAccountAsync(
                "trade_two",Program.Password)).AccountId;
            long charA=(await store.CreateCharacterAsync(
                accountA,0,"TradeTlsOne",new CharacterSpawn(15,8,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("trade A missing");
            long charB=(await store.CreateCharacterAsync(
                accountB,0,"TradeTlsTwo",new CharacterSpawn(15,24,8)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("trade B missing");
            SeedWallet(db,charA,1000);
            SeedWallet(db,charB,500);

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var itemA=await SeedItem(store,generation,charA,4103,2,1,8,8)
                .ConfigureAwait(false);
            var itemB=await SeedItem(store,generation,charB,4104,1,2,24,8)
                .ConfigureAwait(false);
            var rules=new Dictionary<int,TradeItemRule>
            {
                [4103]=new(4103,true,"ci:godot-trade-4103"),
                [4104]=new(4104,true,"ci:godot-trade-4104")
            };
            var coordinator=new PlayerTradeCoordinator(store,world,rules);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,trades:coordinator);
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
                    $"TRADE_FIXTURE_READY port={port} cert={certPath} " +
                    $"actor_a={charA} actor_b={charB} item_a={itemA.ItemUid} item_b={itemB.ItemUid}");
                Console.Out.Flush();

                using var first=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var firstTask=authorization.RunAuthenticatedSocketAsync(
                    first,certificate,router,
                    new TlsWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000));
                using var second=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var secondTask=authorization.RunAuthenticatedSocketAsync(
                    second,certificate,router,
                    new TlsWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000));
                var ended=await Task.WhenAll(firstTask,secondTask)
                    .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                var inventoryA=await store.ListInventoryItemsAsync(charA)
                    .ConfigureAwait(false);
                var inventoryB=await store.ListInventoryItemsAsync(charB)
                    .ConfigureAwait(false);
                long walletA=await store.GetCharacterMoneyAsync(charA)
                    .ConfigureAwait(false);
                long walletB=await store.GetCharacterMoneyAsync(charB)
                    .ConfigureAwait(false);
                if(inventoryA.Count!=1||inventoryA[0].ItemUid!=itemB.ItemUid||
                   inventoryA[0].ItemDefinitionId!=4104||inventoryA[0].Quantity!=1||
                   inventoryA[0].SourceDropId!=itemB.DropId||
                   inventoryB.Count!=1||inventoryB[0].ItemUid!=itemA.ItemUid||
                   inventoryB[0].ItemDefinitionId!=4103||inventoryB[0].Quantity!=2||
                   inventoryB[0].SourceDropId!=itemA.DropId||
                   walletA!=850||walletB!=650||
                   ended.Any(x=>x.CommandsAccepted!=5)||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"trade E2E mismatch walletA={walletA} walletB={walletB} " +
                        $"invA={inventoryA.Count} invB={inventoryB.Count} " +
                        $"cmdA={ended[0].CommandsAccepted} cmdB={ended[1].CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"TRADE_FIXTURE_PASS actor_a={charA} actor_b={charB} " +
                    $"wallet_a={walletA} wallet_b={walletB} " +
                    $"item_a_owner={charB} item_b_owner={charA} commands=5/5");
                Console.Out.Flush();
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                await pumping.ConfigureAwait(false);
                await world.StopAsync().ConfigureAwait(false);
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("TRADE_FIXTURE_FAIL "+error);
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
            15,x,y,880000+slot,
            SqliteIdentityStore.ComposeLootDeathRevision(generation,slot),
            0,definition,quantity,LootSourceTrust.SyntheticTest,
            "ci:godot-trade-seed-"+slot)).ConfigureAwait(false);
        var grant=await store.ClaimGroundDropAsync(
            new TrustedPickupContext(characterId,100+slot,1,1,15,x,y),
            drop.DropId).ConfigureAwait(false);
        if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
            throw new InvalidOperationException("trade seed pickup failed");
        return(grant.ItemUid,drop.DropId);
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource=db,Mode=SqliteOpenMode.ReadWrite,Pooling=false
            }.ToString());
        connection.Open();
        using var command=connection.CreateCommand();
        command.CommandText=
            "UPDATE character_wallets SET money=$money WHERE character_id=$id;";
        command.Parameters.AddWithValue("$money",money);
        command.Parameters.AddWithValue("$id",characterId);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("trade fixture wallet seed failed");
    }
}
