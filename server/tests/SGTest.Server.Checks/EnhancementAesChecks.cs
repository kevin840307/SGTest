using SGTest.Server.Npc;
using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Transport;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class EnhancementAesChecks
{
    public static void Verify(string root)
    {
        Checks.Run("AES enhancement preview settlement and replay are authoritative",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string dir=Path.Combine(Path.GetTempPath(),
            "sgtest_enhance_tls_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string db=Path.Combine(dir,"identity.db");
        try
        {
            string mapDir=Checks.Fixture(root,80,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,80);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long account=(await store.CreateAccountAsync(
                "enhance_tls_user","Enhance-Aes-Password-123!")).AccountId;
            long actor=(await store.CreateCharacterAsync(
                account,0,"EnhanceAesHero",new CharacterSpawn(80,8,8)))
                .Character?.CharacterId
                ??throw new Exception("enhancement AES character missing");
            SeedWallet(db,actor,200);

            long generation=await store.AllocateLootWorldGenerationAsync();
            var drop=await store.CreateTrustedGroundDropAsync(new(
                80,8,8,9501,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,5701,1,LootSourceTrust.SyntheticTest,"enhance-tls-seed"));
            var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                actor,1,1,1,80,8,8),drop.DropId);
            Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
                "enhancement AES item seed failed");
            long uid=grant.ItemUid;

            IReadOnlyDictionary<int,EquipmentEnhancementItemRule> rules=
                new Dictionary<int,EquipmentEnhancementItemRule>
                {
                    [5701]=new(
                        5701,
                        new Dictionary<int,EquipmentEnhancementStepRule>
                        {
                            [0]=new(0,1,100,1_000_000,true,
                                "ci:enhance-5701-0-1")
                        },
                        "ci:enhance-5701")
                };
            var enhancers=new NpcEnhancementCatalog([
                new NpcEnhancementCatalog.EnhancementPoint(
                    80,99,8,8,"ci:enhancer-npc")
            ]);
            var authorization=new IdentitySessionAuthorization(
                store,world,pickupWorldGeneration:generation,
                enhancers:enhancers,enhancementItemRules:rules);
            var router=new WorldResultRouter();

            var session=await StartSession(authorization,router);
            await LoginAndChoose(session.Aes,actor,
                "enhance_tls_user","Enhance-Aes-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(session.Aes),out var inventory)&&
                inventory is not null&&inventory.Complete&&
                inventory.Items.Single().ItemUid==uid,
                "enhancement AES inventory baseline mismatch");

            long epoch=_lastMapEpoch;
            await session.Aes.WriteAsync(
                WireProtocol.EncodeEnhancementOpen(new(epoch,1,99)));
            await session.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEnhancementSnapshot(
                await ReadFrame(session.Aes),out var opened)&&opened is not null&&
                opened.Status==EnhancementOpenStatus.Opened&&
                opened.NpcSpawnId==99&&opened.WalletMoney==200&&
                opened.Complete&&opened.Items.Count==1&&
                opened.Items[0].ItemUid==uid&&
                opened.Items[0].Location==EnhancementItemLocation.Inventory&&
                opened.Items[0].CurrentLevel==0&&
                opened.Items[0].TargetLevel==1&&
                opened.Items[0].SuccessPpm==1_000_000&&
                opened.Items[0].MoneyCost==100,
                "enhancement AES preview mismatch");

            var enhance=WireProtocol.EncodeEnhancementItem(new(epoch,2,99,uid));
            await session.Aes.WriteAsync(enhance);
            await session.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEnhancementResult(
                await ReadFrame(session.Aes),out var applied)&&
                applied.Status==EnhancementStatus.Applied&&applied.Succeeded&&
                applied.ItemUid==uid&&applied.ItemDefinitionId==5701&&
                applied.PreviousLevel==0&&applied.TargetLevel==1&&
                applied.CurrentLevel==1&&applied.MoneyCost==100&&
                applied.MoneyCharged==100&&applied.WalletMoney==100,
                "enhancement AES settlement mismatch");

            await session.Aes.WriteAsync(enhance);
            await session.Aes.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEnhancementResult(
                await ReadFrame(session.Aes),out var replay)&&
                replay.Status==EnhancementStatus.AlreadyApplied&&
                replay.Succeeded&&replay.CurrentLevel==1&&
                replay.WalletMoney==100,
                "enhancement AES replay mismatch");

            await session.Aes.DisposeAsync();
            session.Client.Dispose();
            var ended=await session.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended.Reason==AesWireEndReason.CleanEof,
                "enhancement AES session did not close cleanly");

            var durable=await store.ListEnhancementPreviewAsync(
                actor,new TrustedEnhancementNpcRule(99,"ci:enhancer-npc"),rules);
            Checks.Require(durable.WalletMoney==100&&durable.Items.Count==0,
                "enhancement durable final state mismatch");

            var relogin=await StartSession(authorization,router);
            await LoginAndChoose(relogin.Aes,actor,
                "enhance_tls_user","Enhance-Aes-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(relogin.Aes),out var reloginInventory)&&
                reloginInventory is not null&&reloginInventory.Complete&&
                reloginInventory.Items.Count==1&&
                reloginInventory.Items[0].ItemUid==uid&&
                reloginInventory.Items[0].EnhancementLevel==1,
                "relogin inventory baseline lost durable enhancement level");
            await relogin.Aes.DisposeAsync();
            relogin.Client.Dispose();
            var reloginEnded=await relogin.ServerTask.WaitAsync(
                TimeSpan.FromSeconds(3));
            Checks.Require(reloginEnded.Reason==AesWireEndReason.CleanEof,
                "enhancement relogin AES session did not close cleanly");

            await world.StopAsync();
        }
        finally
        {
            try{Directory.Delete(dir,true);}catch{}
        }
    }

    private static void SeedWallet(string db,long actor,long money)
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
        command.Parameters.AddWithValue("$id",actor);
        command.Parameters.AddWithValue("$money",money);
        Checks.Require(command.ExecuteNonQuery()==1,
            "enhancement AES wallet seed failed");
    }

    private static async Task<(AesCbcFrameStream Aes,Task<AesWireConnectionResult> ServerTask,TcpClient Client)>
        StartSession(IdentitySessionAuthorization authorization,WorldResultRouter router)
    {
        var settings=AesSocketChecks.CreateSettings();
        var listener=new TcpListener(IPAddress.Loopback,0);
        listener.Start(1);
        int port=((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted=listener.AcceptTcpClientAsync();
        var client=new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback,port);
        var serverClient=await accepted;
        var serverTask=authorization.RunAuthenticatedAesSocketAsync(
            serverClient,settings,router).ContinueWith(t=>
            {

                listener.Stop();
                return t.GetAwaiter().GetResult();
            },TaskScheduler.Default);
        var tls=AesSocketChecks.CreateClientStream(client);

        return(tls,serverTask,client);
    }

    private static async Task LoginAndChoose(
        AesCbcFrameStream tls,long actor,string username,string password)
    {
        await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(username,password)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeAuthResult(
            await ReadFrame(tls),out var auth)&&auth.Status==AuthWireStatus.Success,
            "enhancement AES auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(
            await ReadFrame(tls),out var list)&&
            list.Characters.Single().CharacterId==actor,
            "enhancement AES list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(actor)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await ReadFrame(tls),out var choose)&&
            choose.Status==CharacterChooseWireStatus.Success,
            "enhancement AES choose failed");
        _lastMapEpoch=choose.MapEpoch;
    }

    private static long _lastMapEpoch;

    private static async Task<byte[]> ReadFrame(AesCbcFrameStream tls)
    {
        byte[] header=new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0,4));
        Checks.Require(length>=WireProtocol.HeaderBytes&&
            length<=WireProtocol.MaximumFrameBytes,
            "enhancement AES invalid frame length");
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
