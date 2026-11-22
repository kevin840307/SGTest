using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.Pet;
using SGTest.Server.Transport;
using SGTest.Server.Visibility;
using SGTest.Server.World;

internal static class PetLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_pet_login_"+Guid.NewGuid().ToString("N"));
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
                accountId,0,"PetHero",new CharacterSpawn(15,24,24)))
                .Character?.CharacterId
                ??throw new InvalidOperationException("pet character create failed");
            long petA=await store.ProvisionPetAsync(
                characterId,5001,level:4,experience:88,evolutionId:2)
                .ConfigureAwait(false);
            long petB=await store.ProvisionPetAsync(
                characterId,5002,level:7,experience:1234,evolutionId:7)
                .ConfigureAwait(false);

            var pets=new PetCoordinator(store);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentitySessionAuthorization(
                store,world,pets:pets);
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
                    $"PET_FIXTURE_READY port={port} username={Program.Username} " +
                    $"character={characterId} pet_a={petA} pet_b={petB}");
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

                var afterSummon=await store.GetPetsAsync(characterId)
                    .ConfigureAwait(false);
                var activeAfterSummon=afterSummon.SingleOrDefault(x=>x.IsActive);
                if(activeAfterSummon?.PetUid!=petA||
                   activeAfterSummon.DefinitionId!=5001||
                   activeAfterSummon.EvolutionId!=2||
                   activeAfterSummon.Level!=4||
                   activeAfterSummon.Experience!=88||
                   firstEnded.CommandsAccepted!=2||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"pet summon/reconnect baseline mismatch active={activeAfterSummon?.PetUid??0} " +
                        $"commands={firstEnded.CommandsAccepted} sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"PET_FIXTURE_SUMMON_PERSISTED pet_uid={petA} commands={firstEnded.CommandsAccepted}");
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

                var final=await store.GetPetsAsync(characterId).ConfigureAwait(false);
                var firstPet=final.Single(x=>x.PetUid==petA);
                var secondPet=final.Single(x=>x.PetUid==petB);
                if(final.Count!=2||final.Any(x=>x.IsActive)||
                   firstPet.DefinitionId!=5001||firstPet.EvolutionId!=2||
                   firstPet.Level!=4||firstPet.Experience!=88||
                   secondPet.DefinitionId!=5002||secondPet.EvolutionId!=7||
                   secondPet.Level!=7||secondPet.Experience!=1234||
                   secondEnded.CommandsAccepted!=2||
                   router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"pet recall E2E mismatch pets={final.Count} " +
                        $"active={final.Count(x=>x.IsActive)} commands={secondEnded.CommandsAccepted} " +
                        $"sessions={router.Statistics.ActiveSessions}");

                Console.WriteLine(
                    $"PET_FIXTURE_PASS aes=true pet_a={petA} pet_b={petB} " +
                    $"summon_persisted=true reconnect_restored=true recall_persisted=true " +
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
            Console.Error.WriteLine("PET_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }
}
