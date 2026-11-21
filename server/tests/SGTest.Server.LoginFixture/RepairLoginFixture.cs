using SGTest.Server.Visibility;
using SGTest.Server.Npc;
using SGTest.Server.Map;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class RepairLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_repair_login_"+Guid.NewGuid().ToString("N"));
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
                accountId,0,"RepairHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("repair character create failed");
            SeedWallet(db,characterId,100);

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops=await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15,24,24,1001900001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                    0,3701,1,LootSourceTrust.SyntheticTest,
                    "ci:godot-tls-repair")
            ]).ConfigureAwait(false);
            var grant=await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId,19,1,1,15,24,24),
                drops.Single().DropId).ConfigureAwait(false);
            if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0)
                throw new InvalidOperationException("repair fixture inventory seed failed");
            long itemUid=grant.ItemUid;

            IReadOnlyDictionary<int,EquipmentRule> equipmentRules=
                new Dictionary<int,EquipmentRule>{[3701]=new(3701,4)};
            var equipped=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterId,301,1,1),
                itemUid,equipmentRules).ConfigureAwait(false);
            if(equipped.Status!=EquipmentTransferStatus.Applied)
                throw new InvalidOperationException("repair fixture equip seed failed");

            IReadOnlyDictionary<int,RepairItemRule> repairItems=
                new Dictionary<int,RepairItemRule>
                {
                    [3701]=new(3701,1000,100,"ci:repair-item-3701")
                };
            var wear=await store.ApplyTrustedEquippedWearAsync(
                characterId,itemUid,repairItems,1001).ConfigureAwait(false);
            if(!wear.Applied||wear.CurrentRaw!=3999)
                throw new InvalidOperationException("repair fixture wear seed failed");

            var repairNpc=new NpcRepairCatalog.RepairPoint(
                15,88,24,24,10,"old:Repair.txt:OpenRepair(10)");
            var repairs=new NpcRepairCatalog([repairNpc]);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,
                repairs:repairs,repairItemRules:repairItems);
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
                    $"REPAIR_FIXTURE_READY port={port} cert={certPath} username={Program.Username} " +
                    $"character={characterId} item_uid={itemUid} npc=88 current=80 max=100 cost=20 wallet=100");
                Console.Out.Flush();

                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended=await authorization.RunAuthenticatedSocketAsync(
                    client,certificate,router,
                    new TlsWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);

                var preview=await store.ListRepairPreviewAsync(
                    characterId,
                    new TrustedRepairNpcRule(
                        repairNpc.NpcSpawnId,repairNpc.RepairRate,repairNpc.SourceReference),
                    repairItems).ConfigureAwait(false);
                var equipment=await store.ListEquippedItemsAsync(characterId)
                    .ConfigureAwait(false);
                if(preview.WalletMoney!=80||preview.Items.Count!=1||
                   preview.Items[0].ItemUid!=itemUid||
                   preview.Items[0].Location!=RepairItemLocation.Equipped||
                   preview.Items[0].CurrentDurability!=100||
                   preview.Items[0].MaxDurability!=100||
                   preview.Items[0].Cost!=0||
                   equipment.Count!=1||equipment[0].ItemUid!=itemUid||
                   ended.CommandsAccepted!=3||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"repair E2E mismatch wallet={preview.WalletMoney} items={preview.Items.Count} " +
                        $"current={(preview.Items.Count==0?-1:preview.Items[0].CurrentDurability)} " +
                        $"cost={(preview.Items.Count==0?-1:preview.Items[0].Cost)} " +
                        $"equipment={equipment.Count} commands={ended.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"REPAIR_FIXTURE_PASS tls={ended.Protocol} item_uid={itemUid} " +
                    $"current=100 max=100 wallet=80 cost=0 equipment_preserved=true " +
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
                await routing.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
        }
        catch(Exception error)
        {
            Console.Error.WriteLine("REPAIR_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
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
        command.CommandText=@"
INSERT INTO character_wallets(character_id,money) VALUES($id,$money)
ON CONFLICT(character_id) DO UPDATE SET money=excluded.money;";
        command.Parameters.AddWithValue("$id",characterId);
        command.Parameters.AddWithValue("$money",money);
        if(command.ExecuteNonQuery()!=1)
            throw new InvalidOperationException("repair fixture wallet seed failed");
    }
}
