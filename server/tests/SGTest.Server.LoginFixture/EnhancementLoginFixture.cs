using SGTest.Server.Visibility;
using SGTest.Server.Npc;
using SGTest.Server.Map;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class EnhancementLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_enhancement_login_"+Guid.NewGuid().ToString("N"));
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
            long accountId=(await store.CreateAccountAsync(
                Program.Username,Program.Password)).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"EnhanceHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException(
                    "enhancement character create failed");
            SeedWallet(db,characterId,200);

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops=await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15,24,24,1001950001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                    0,5701,1,LootSourceTrust.SyntheticTest,
                    "ci:godot-tls-enhancement")
            ]).ConfigureAwait(false);
            var grant=await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId,19,1,1,15,24,24),
                drops.Single().DropId).ConfigureAwait(false);
            if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
                throw new InvalidOperationException(
                    "enhancement fixture inventory seed failed");
            long itemUid=grant.ItemUid;

            IReadOnlyDictionary<int,EquipmentRule> equipmentRules=
                new Dictionary<int,EquipmentRule>{[5701]=new(5701,4)};
            var equipped=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterId,301,1,1),
                itemUid,equipmentRules).ConfigureAwait(false);
            if(equipped.Status!=EquipmentTransferStatus.Applied)
                throw new InvalidOperationException(
                    "enhancement fixture equip seed failed");

            IReadOnlyDictionary<int,EquipmentEnhancementItemRule> itemRules=
                new Dictionary<int,EquipmentEnhancementItemRule>
                {
                    [5701]=new(
                        5701,
                        new Dictionary<int,EquipmentEnhancementStepRule>
                        {
                            [0]=new(0,1,100,1_000_000,true,
                                "ci:enhancement-5701-0-1")
                        },
                        "ci:enhancement-5701")
                };
            var enhancer=new NpcEnhancementCatalog.EnhancementPoint(
                15,99,24,24,"ci:enhancer-npc-99");
            var enhancers=new NpcEnhancementCatalog([enhancer]);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,
                enhancers:enhancers,enhancementItemRules:itemRules);
            using var certificate=Program.CreateCertificate();
            string certPath=Path.Combine(root,"fixture-cert.pem");
            Program.WriteCertificatePem(certPath,certificate);
            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(1);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"ENHANCEMENT_FIXTURE_READY port={port} cert={certPath} " +
                    $"username={Program.Username} character={characterId} " +
                    $"item_uid={itemUid} npc=99 level=0 target=1 success_ppm=1000000 " +
                    "cost=100 wallet=200");
                Console.Out.Flush();

                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended=await authorization.RunAuthenticatedSocketAsync(
                    client,certificate,router,
                    new TlsWireConnectionOptions(
                        AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);

                var preview=await store.ListEnhancementPreviewAsync(
                    characterId,
                    new TrustedEnhancementNpcRule(
                        enhancer.NpcSpawnId,enhancer.SourceReference),
                    itemRules).ConfigureAwait(false);
                int level=ReadEnhancementLevel(db,characterId,itemUid);
                if(preview.WalletMoney!=100||preview.Items.Count!=0||
                   level!=1||ended.CommandsAccepted!=3||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"enhancement E2E mismatch wallet={preview.WalletMoney} " +
                        $"preview={preview.Items.Count} level={level} " +
                        $"commands={ended.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"ENHANCEMENT_FIXTURE_PASS tls={ended.Protocol} " +
                    $"item_uid={itemUid} level=1 wallet=100 cost=100 " +
                    $"commands={ended.CommandsAccepted}");
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
            Console.Error.WriteLine("ENHANCEMENT_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }

    private static void SeedWallet(string db,long characterId,long money)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException(
                "enhancement fixture wallet seed failed");
    }

    private static int ReadEnhancementLevel(
        string db,long characterId,long itemUid)
    {
        using var connection=Open(db);
        using var command=connection.CreateCommand();
        command.CommandText=@"
SELECT enhancement_level FROM equipped_items
WHERE character_id=$actor AND item_uid=$uid;";
        command.Parameters.AddWithValue("$actor",characterId);
        command.Parameters.AddWithValue("$uid",itemUid);
        return Convert.ToInt32(command.ExecuteScalar());
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
