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

            var mechas=new MechaCoordinator(store);
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
                    $"character={characterId} mecha_a={mechaA} mecha_b={mechaB}");
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
                if(mountedAfter?.MechaUid!=mechaA||
                   mountedAfter.DefinitionId!=7001||
                   mountedAfter.Level!=4||
                   mountedAfter.Experience!=88||
                   !mountedAfter.PartsBlob.SequenceEqual(new byte[]{1,2,3})||
                   firstEnded.CommandsAccepted!=2||
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
                   firstMecha.Experience!=88||
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
                    $"mount_persisted=true reconnect_restored=true dismount_persisted=true " +
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
