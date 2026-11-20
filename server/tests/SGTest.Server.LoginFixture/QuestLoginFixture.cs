using System.Net;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.World;

internal static class QuestLoginFixture
{
    private const int QuestId=91001;
    private const int NpcSpawnId=88;

    internal static async Task<int> RunAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "sgtest_quest_login_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var grid=BlockingGrid.Load(Program.CreateMap(root,15),15);
            await using var world=new ServerWorldHost(
                [grid],options:new WorldHostOptions(PublishIdleTicks:true));
            await world.StartAsync().ConfigureAwait(false);
            using var store=new SqliteIdentityStore(
                Path.Combine(root,"identity.db"));
            await store.InitializeAsync().ConfigureAwait(false);
            long account=(await store.CreateAccountAsync(
                Program.Username,Program.Password).ConfigureAwait(false)).AccountId;
            long character=(await store.CreateCharacterAsync(
                account,0,"QuestHero",new CharacterSpawn(15,24,24))
                .ConfigureAwait(false)).Character?.CharacterId
                ??throw new InvalidOperationException("quest character create failed");

            var rule=CreateRule();
            var quests=new NpcQuestCatalog([
                new NpcQuestCatalog.QuestPoint(
                    15,NpcSpawnId,24,24,rule)
            ]);
            var router=new WorldResultRouter();
            var pump=new WorldAoiPump(router);
            var authorization=new IdentityTlsAuthorization(
                store,world,quests:quests);

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
                    $"QUEST_FIXTURE_READY port={port} cert={certPath} " +
                    $"username={Program.Username} character={character} " +
                    $"npc={NpcSpawnId} quest={QuestId}");
                Console.Out.Flush();

                using(var first=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
                {
                    var ended=await authorization.RunAuthenticatedSocketAsync(
                        first,certificate,router,
                        new TlsWireConnectionOptions(
                            AuthorizationTimeoutMilliseconds:15000))
                        .WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                    if(ended.CommandsAccepted!=4)
                        throw new InvalidOperationException(
                            $"quest mutate commands={ended.CommandsAccepted}");
                    var completed=await store.GetQuestViewAsync(character,rule)
                        .ConfigureAwait(false);
                    if(completed.Status!=QuestViewStatus.Ready||
                       completed.View is not { } view||
                       view.State!=QuestProgressState.Completed||
                       view.Stage!=2||view.ProgressRevision!=2||
                       view.Choices.Count!=0)
                        throw new InvalidOperationException(
                            "quest mutate session did not persist completion");
                    Console.WriteLine(
                        $"QUEST_FIXTURE_MUTATE_PASS commands={ended.CommandsAccepted} " +
                        $"stage={view.Stage} revision={view.ProgressRevision}");
                    Console.Out.Flush();
                }

                using(var second=await listener.AcceptTcpClientAsync()
                    .WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false))
                {
                    var ended=await authorization.RunAuthenticatedSocketAsync(
                        second,certificate,router,
                        new TlsWireConnectionOptions(
                            AuthorizationTimeoutMilliseconds:15000))
                        .WaitAsync(TimeSpan.FromSeconds(25)).ConfigureAwait(false);
                    if(ended.CommandsAccepted!=2)
                        throw new InvalidOperationException(
                            $"quest relogin commands={ended.CommandsAccepted}");
                    var persisted=await store.GetQuestViewAsync(character,rule)
                        .ConfigureAwait(false);
                    if(persisted.Status!=QuestViewStatus.Ready||
                       persisted.View is not { } view||
                       view.State!=QuestProgressState.Completed||
                       view.Stage!=2||view.ProgressRevision!=2||
                       view.Choices.Count!=0)
                        throw new InvalidOperationException(
                            "quest completion changed after relogin");
                    Console.WriteLine(
                        $"QUEST_FIXTURE_RELOGIN_PASS commands={ended.CommandsAccepted} " +
                        $"stage={view.Stage} revision={view.ProgressRevision}");
                    Console.Out.Flush();
                }

                if(router.Statistics.ActiveSessions!=0)
                    throw new InvalidOperationException(
                        $"quest sessions leaked={router.Statistics.ActiveSessions}");
                Console.WriteLine(
                    $"QUEST_FIXTURE_PASS quest={QuestId} npc={NpcSpawnId} " +
                    "durable_completed=true relogin=true active=0");
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
            Console.Error.WriteLine("QUEST_FIXTURE_FAIL "+error);
            return 1;
        }
        finally
        {
            try{Directory.Delete(root,recursive:true);}catch{}
        }
    }

    private static TrustedQuestRule CreateRule()
    {
        IReadOnlyDictionary<int,TrustedQuestStageRule> stages=
            new Dictionary<int,TrustedQuestStageRule>
            {
                [0]=new(
                    0,"這是一個合成的 verified Quest 測試。",
                    new Dictionary<int,TrustedQuestChoiceRule>
                    {
                        [1]=new(1,"接受",1,QuestProgressState.Active),
                        [3]=new(
                            3,"另一個合法起始選項",1,
                            QuestProgressState.Active)
                    }),
                [1]=new(
                    1,"是否完成這個已驗證步驟？",
                    new Dictionary<int,TrustedQuestChoiceRule>
                    {
                        [2]=new(2,"完成",2,QuestProgressState.Completed)
                    }),
                [2]=new(
                    2,"任務已完成。",
                    new Dictionary<int,TrustedQuestChoiceRule>())
            };
        return new TrustedQuestRule(
            QuestId,1,"Verified Quest Fixture",0,stages,
            "ci:godot-quest:v1");
    }
}
