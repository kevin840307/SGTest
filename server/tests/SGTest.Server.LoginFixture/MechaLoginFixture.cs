using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Mecha;
using SGTest.Server.Transport;
using SGTest.Server.Visibility;
using SGTest.Server.World;

internal static class MechaLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_mecha_login_"+Guid.NewGuid().ToString("N"));
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
                accountId,0,"MechaHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("mecha character create failed");
            long mechaA=await store.ProvisionMechaAsync(
                characterId,7001,level:4,experience:88,new byte[]{1,2,3})
                .ConfigureAwait(false);
            long mechaB=await store.ProvisionMechaAsync(
                characterId,7002,level:7,experience:1234)
                .ConfigureAwait(false);

            string batteryManifest=Path.Combine(root,"verified-mecha-battery.json");
            File.WriteAllText(batteryManifest,System.Text.Json.JsonSerializer.Serialize(new
            {
                kind="verified_mecha_battery_rules",version=1,
                rules_verified=true,gameplay_ready=true,
                source_reference="ci:synthetic-battery-rule-not-hk",
                rules=new[]{new{mecha_definition_id=7001,battery_item_definition_id=7701,
                    capacity=100,charge_amount=40,allow_while_mounted=false}}
            }));
            var batteryRules=MechaBatteryCatalog.LoadVerified(batteryManifest);
            long generation=await store.AllocateLootWorldGenerationAsync().ConfigureAwait(false);
            var batteryDrop=await store.CreateTrustedGroundDropAsync(new(
                15,24,24,9701,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,7701,1,LootSourceTrust.SyntheticTest,"mecha-battery-e2e"))
                .ConfigureAwait(false);
            var batteryGrant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId,SqliteIdentityStore.ComposePickupConnectionEpoch(generation,1),
                1,1,15,24,24),batteryDrop.DropId).ConfigureAwait(false);
            if(batteryGrant.Status!=PickupStatus.Granted||batteryGrant.ItemUid<=0)
                throw new InvalidOperationException("mecha battery fixture seed failed");
            long batteryUid=batteryGrant.ItemUid;

            var mechas=new MechaCoordinator(store,null,batteryRules);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentitySessionAuthorization(
                store,world,mechas:mechas);
            var settings=AesCbcSettings.FromEnvironment();

            var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(2);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            try
            {
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                Console.WriteLine(
                    $"MECHA_FIXTURE_READY port={port} username={Program.Username} " +
                    $"character={characterId} mecha_a={mechaA} mecha_b={mechaB} battery_uid={batteryUid}");
                Console.Out.Flush();

                AesWireConnectionResult firstEnded;
                using(var first=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
                {
                    firstEnded=await authorization.RunAuthenticatedAesSocketAsync(
                        first,settings,router,
                        new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                        .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                }

                var afterMount=await store.GetMechasAsync(characterId)
                    .ConfigureAwait(false);
                var mountedAfter=afterMount.SingleOrDefault(x=>x.IsMounted);
                var inventoryAfterFirst=await store.ListInventoryItemsAsync(characterId)
                    .ConfigureAwait(false);
                if(mountedAfter?.MechaUid!=mechaA||
                   mountedAfter.DefinitionId!=7001||
                   mountedAfter.Level!=4||
                   mountedAfter.Experience!=88||mountedAfter.BatteryCharge!=40||
                   !mountedAfter.PartsBlob.SequenceEqual(new byte[]{1,2,3})||
                   inventoryAfterFirst.Any(x=>x.ItemUid==batteryUid)||
                   firstEnded.CommandsAccepted!=3||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"mecha mount/reconnect baseline mismatch mounted={mountedAfter?.MechaUid??0} " +
                        $"commands={firstEnded.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"MECHA_FIXTURE_MOUNT_PERSISTED mecha_uid={mechaA} commands={firstEnded.CommandsAccepted}");
                Console.Out.Flush();

                AesWireConnectionResult secondEnded;
                using(var second=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
                {
                    secondEnded=await authorization.RunAuthenticatedAesSocketAsync(
                        second,settings,router,
                        new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                        .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                }

                var final=await store.GetMechasAsync(characterId).ConfigureAwait(false);
                var firstMecha=final.Single(x=>x.MechaUid==mechaA);
                var secondMecha=final.Single(x=>x.MechaUid==mechaB);
                if(final.Count!=2||final.Any(x=>x.IsMounted)||
                   firstMecha.DefinitionId!=7001||firstMecha.Level!=4||
                   firstMecha.Experience!=88||firstMecha.BatteryCharge!=40||
                   !firstMecha.PartsBlob.SequenceEqual(new byte[]{1,2,3})||
                   secondMecha.DefinitionId!=7002||secondMecha.Level!=7||
                   secondMecha.Experience!=1234||
                   secondEnded.CommandsAccepted!=2||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"mecha dismount E2E mismatch mechas={final.Count} " +
                        $"mounted={final.Count(x=>x.IsMounted)} commands={secondEnded.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"MECHA_FIXTURE_PASS aes=true mecha_a={mechaA} mecha_b={mechaB} " +
                    $"battery_charge=40 inventory_consumed=true mount_persisted=true " +
                    $"reconnect_restored=true dismount_persisted=true " +
                    $"commands={firstEnded.CommandsAccepted}/{secondEnded.CommandsAccepted}");
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
            Console.Error.WriteLine("MECHA_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }
}
