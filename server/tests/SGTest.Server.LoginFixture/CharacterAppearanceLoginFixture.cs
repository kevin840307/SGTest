using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.Map;
using SGTest.Server.World;
using SGTest.Server.Visibility;

internal static class CharacterAppearanceLoginFixture
{
    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),"sgtest_appearance_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid=BlockingGrid.Load(Program.CreateMap(root,15),15);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(root,"identity.db"));
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(Program.Username,Program.Password)).AccountId;
            var chosen=new CharacterAppearance(1,3,7,2,1);
            var rules=new CharacterAppearanceRules("synthetic/godot-aes-appearance/v2",[
                new(0,0,0,0,0),chosen]);
            var authorization=new IdentitySessionAuthorization(store,world,
                characterCreation:new(new CharacterSpawn(15,8,8),rules));
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            using var stop=new CancellationTokenSource();
            var routing=router.RunAsync(world.Results);
            var pumping=pump.RunAsync(stop.Token);
            var settings = AesCbcSettings.FromEnvironment();
                                    var listener=new TcpListener(IPAddress.Loopback,0);
            listener.Start(1);
            try
            {
                Console.WriteLine($"APPEARANCE_FIXTURE_READY port={((IPEndPoint)listener.LocalEndpoint).Port} username={Program.Username}");
                Console.Out.Flush();
                for(int pass=0;pass<2;pass++)
                {
                    using var client=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    await authorization.RunAuthenticatedAesSocketAsync(client,settings,router,
                        new AesWireConnectionOptions(AuthorizationTimeoutMilliseconds:15000))
                        .WaitAsync(TimeSpan.FromSeconds(25));
                    var row=(await store.ListCharactersAsync(account)).Single();
                    if(row.Appearance!=new CharacterAppearanceState(chosen,rules.SourceReference) ||
                       !row.RequiresCharacterPassword ||
                       row.Creation?.Attributes!=new CharacterCreationAttributes(3,2,2,1))
                        throw new InvalidOperationException(
                            "Godot creation/relogin did not preserve appearance, attributes and secondary password");
                }
                if(router.Statistics.ActiveSessions!=0 || world.Statistics.ActiveActors!=0)
                    throw new InvalidOperationException("appearance fixture leaked session or actor");
                Console.WriteLine("APPEARANCE_FIXTURE_PASS created=1 relogin=true selected_world=true durable_appearance=true active=0");
                return 0;
            }
            finally
            {
                listener.Stop();
                stop.Cancel();
                try { await pumping; } catch(OperationCanceledException) { }
                await world.StopAsync();
                await routing;
            }
        }
        finally { Directory.Delete(root,true); }
    }
}
