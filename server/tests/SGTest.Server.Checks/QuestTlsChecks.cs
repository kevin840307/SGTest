using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class QuestTlsChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS quest open choose replay completion and relogin stay durable",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string identityRoot=Path.Combine(Path.GetTempPath(),
            "sgtest_quest_tls_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(identityRoot);
        string db=Path.Combine(identityRoot,"identity.db");
        try
        {
            string mapDir=Checks.Fixture(root,82,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,82);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long accountId=(await store.CreateAccountAsync(
                "quest_tls_user","Quest-Tls-Password-123!")).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"QuestTlsHero",new CharacterSpawn(82,8,8)))
                .Character?.CharacterId??throw new Exception("quest TLS character missing");
            long generation=await store.AllocateLootWorldGenerationAsync();

            var rule=QuestChecks.Rule(1,"ci:quest:v1");
            var quests=new NpcQuestCatalog([
                new NpcQuestCatalog.QuestPoint(82,88,8,8,rule)
            ]);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,quests:quests);
            var router=new WorldResultRouter();

            var first=await StartSession(authorization,router);
            await LoginAndChoose(first.Tls,characterId,
                "quest_tls_user","Quest-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(first.Tls),out var inventory)&&
                inventory is not null&&inventory.Complete&&inventory.Items.Count==0,
                "quest TLS initial inventory baseline mismatch");

            long epoch=_lastMapEpoch;
            await first.Tls.WriteAsync(
                WireProtocol.EncodeQuestOpen(new(epoch,1,88)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeQuestView(
                await ReadFrame(first.Tls),out var opened)&&opened is not null&&
                opened.Status==QuestViewWireStatus.Ready&&
                opened.QuestId==91001&&opened.RuleRevision==1&&
                opened.State==QuestProgressState.NotStarted&&opened.Stage==0&&
                opened.ProgressRevision==0&&opened.Choices.Count==2&&
                opened.Choices.Select(x=>x.ChoiceId).SequenceEqual([1,3]),
                "quest TLS open view mismatch");

            var choose1=WireProtocol.EncodeQuestChoose(
                new(epoch,2,88,91001,1));
            await first.Tls.WriteAsync(choose1);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeQuestView(
                await ReadFrame(first.Tls),out var active)&&active is not null&&
                active.Status==QuestViewWireStatus.Applied&&
                active.State==QuestProgressState.Active&&active.Stage==1&&
                active.ProgressRevision==1&&
                active.Choices.Select(x=>x.ChoiceId).SequenceEqual([2]),
                "quest TLS first choice mismatch");

            await first.Tls.WriteAsync(choose1);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeQuestView(
                await ReadFrame(first.Tls),out var replay)&&replay is not null&&
                replay.Status==QuestViewWireStatus.AlreadyApplied&&
                replay.State==QuestProgressState.Active&&replay.Stage==1&&
                replay.ProgressRevision==1,
                "quest TLS exact replay mismatch");

            await first.Tls.WriteAsync(
                WireProtocol.EncodeQuestChoose(new(epoch,3,88,91001,2)));
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeQuestView(
                await ReadFrame(first.Tls),out var completed)&&completed is not null&&
                completed.Status==QuestViewWireStatus.Applied&&
                completed.State==QuestProgressState.Completed&&completed.Stage==2&&
                completed.ProgressRevision==2&&completed.Choices.Count==0,
                "quest TLS completion mismatch");

            await first.Tls.DisposeAsync();
            first.Client.Dispose();
            var ended=await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended.Reason==TlsWireEndReason.CleanEof&&
                ended.CommandsAccepted==4,
                "quest TLS first session command lifecycle mismatch");

            var second=await StartSession(authorization,router);
            await LoginAndChoose(second.Tls,characterId,
                "quest_tls_user","Quest-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(second.Tls),out var inventory2)&&
                inventory2 is not null&&inventory2.Complete,
                "quest TLS relogin inventory baseline missing");

            await second.Tls.WriteAsync(
                WireProtocol.EncodeQuestOpen(new(_lastMapEpoch,1,88)));
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeQuestView(
                await ReadFrame(second.Tls),out var durable)&&durable is not null&&
                durable.Status==QuestViewWireStatus.Ready&&
                durable.State==QuestProgressState.Completed&&durable.Stage==2&&
                durable.ProgressRevision==2&&durable.Choices.Count==0,
                "quest TLS durable relogin progress mismatch");

            await second.Tls.DisposeAsync();
            second.Client.Dispose();
            var ended2=await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended2.Reason==TlsWireEndReason.CleanEof&&
                ended2.CommandsAccepted==1,
                "quest TLS second session lifecycle mismatch");

            var final=await store.GetQuestViewAsync(characterId,rule);
            Checks.Require(final.Status==QuestViewStatus.Ready&&
                final.View is { State:QuestProgressState.Completed,Stage:2,
                    ProgressRevision:2 },
                "quest TLS final SQLite progress mismatch");
            await world.StopAsync();
        }
        finally
        {
            try{Directory.Delete(identityRoot,true);}catch{}
        }
    }

    private static async Task<(SslStream Tls,Task<TlsWireConnectionResult> ServerTask,TcpClient Client)>
        StartSession(IdentityTlsAuthorization authorization,WorldResultRouter router)
    {
        var certificate=TlsSocketChecks.CreateCertificate();
        var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start(1);
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted=listener.AcceptTcpClientAsync();
        var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback,port);
        var serverClient=await accepted;
        var serverTask=authorization.RunAuthenticatedSocketAsync(
            serverClient,certificate,router).ContinueWith(t=>
            {
                certificate.Dispose();
                listener.Stop();
                return t.GetAwaiter().GetResult();
            },TaskScheduler.Default);
        var tls=TlsSocketChecks.CreateClientTls(client);
        await TlsSocketChecks.AuthenticateClient(tls);
        return(tls,serverTask,client);
    }

    private static async Task LoginAndChoose(
        SslStream tls,long characterId,string username,string password)
    {
        await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(username,password)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeAuthResult(
            await ReadFrame(tls),out var auth)&&auth.Status==AuthWireStatus.Success,
            "quest TLS auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(
            await ReadFrame(tls),out var list)&&
            list.Characters.Single().CharacterId==characterId,
            "quest TLS list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await ReadFrame(tls),out var choose)&&
            choose.Status==CharacterChooseWireStatus.Success,
            "quest TLS choose failed");
        _lastMapEpoch=choose.MapEpoch;
    }

    private static long _lastMapEpoch;

    private static async Task<byte[]> ReadFrame(SslStream tls)
    {
        byte[] header=new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0,4));
        Checks.Require(length>=WireProtocol.HeaderBytes&&
            length<=WireProtocol.MaximumFrameBytes,
            "quest TLS invalid frame length");
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
