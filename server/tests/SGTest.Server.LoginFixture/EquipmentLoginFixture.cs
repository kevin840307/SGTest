using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class EquipmentLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_equipment_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid=BlockingGrid.Load(Program.CreateMap(root,15),15);
            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long accountId=(await store.CreateAccountAsync(
                Program.Username,Program.Password)).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"EquipmentHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("equipment character create failed");

            long generation=await store.AllocateLootWorldGenerationAsync()
                .ConfigureAwait(false);
            var drops=await store.CreateTrustedGroundDropsAsync([
                new TrustedDropSpawn(
                    15,24,24,1001700001,
                    SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                    0,3701,1,LootSourceTrust.SyntheticTest,
                    "ci:godot-tls-equipment")
            ]).ConfigureAwait(false);
            var grant=await store.ClaimGroundDropAsync(
                new TrustedPickupContext(characterId,19,1,1,15,24,24),
                drops.Single().DropId).ConfigureAwait(false);
            if(grant.Status!=PickupStatus.Granted||grant.ItemUid<=0||grant.Quantity!=1)
                throw new InvalidOperationException(
                    "equipment fixture could not seed durable inventory");

            IReadOnlyDictionary<int,EquipmentRule> rules=
                new Dictionary<int,EquipmentRule>{[3701]=new(3701,4)};
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,equipmentRules:rules);
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
                    $"EQUIPMENT_FIXTURE_READY port={port} cert={certPath} username={Program.Username} " +
                    $"character={characterId} item_uid={grant.ItemUid} slot=4");
                Console.Out.Flush();

                using var client=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                var ended=await authorization.RunAuthenticatedSocketAsync(
                    client,certificate,router,
                    new TlsWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                    .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);

                var inventory=await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                var equipment=await store.ListEquippedItemsAsync(characterId)
                    .ConfigureAwait(false);
                if(inventory.Count!=1||
                   inventory[0].ItemUid!=grant.ItemUid||
                   inventory[0].ItemDefinitionId!=3701||
                   inventory[0].Quantity!=1||
                   inventory[0].SourceDropId!=drops.Single().DropId||
                   equipment.Count!=0||ended.CommandsAccepted!=3||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"equipment E2E mismatch inventory={inventory.Count} equipment={equipment.Count} " +
                        $"commands={ended.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"EQUIPMENT_FIXTURE_PASS tls={ended.Protocol} item_uid={grant.ItemUid} " +
                    $"quantity=1 source_drop={drops.Single().DropId} equipment=0 " +
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
            Console.Error.WriteLine("EQUIPMENT_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }
}
