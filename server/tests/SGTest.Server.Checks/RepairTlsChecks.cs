using SGTest.Server.Npc;
using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class RepairTlsChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS repair preview settlement replay and relogin remain durable",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string identityRoot=Path.Combine(Path.GetTempPath(),
            "sgtest_repair_tls_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(identityRoot);
        string db=Path.Combine(identityRoot,"identity.db");
        try
        {
            string mapDir=Checks.Fixture(root,80,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,80);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(db);
            await store.InitializeAsync();
            long accountId=(await store.CreateAccountAsync(
                "repair_tls_user","Repair-Tls-Password-123!")).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"RepairTlsHero",new CharacterSpawn(80,8,8)))
                .Character?.CharacterId??throw new Exception("repair TLS character missing");
            SeedWallet(db,characterId,100);

            long generation=await store.AllocateLootWorldGenerationAsync();
            var drop=await store.CreateTrustedGroundDropAsync(new(
                80,8,8,9401,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,3701,1,LootSourceTrust.SyntheticTest,"repair-tls-seed"));
            var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId,1,1,1,80,8,8),drop.DropId);
            Checks.Require(grant.Status==PickupStatus.Granted&&grant.ItemUid>0,
                "repair TLS item seed failed");
            long itemUid=grant.ItemUid;

            IReadOnlyDictionary<int,EquipmentRule> equipmentRules=
                new Dictionary<int,EquipmentRule>{[3701]=new(3701,4)};
            var equip=await store.EquipInventoryItemAsync(
                new TrustedEquipmentContext(characterId,201,1,1),
                itemUid,equipmentRules);
            Checks.Require(equip.Status==EquipmentTransferStatus.Applied,
                "repair TLS seed equip failed");

            IReadOnlyDictionary<int,RepairItemRule> itemRules=
                new Dictionary<int,RepairItemRule>
                {
                    [3701]=new(3701,1000,100,"ci:repair-item-3701")
                };
            var wear=await store.ApplyTrustedEquippedWearAsync(
                characterId,itemUid,itemRules,1001);
            Checks.Require(wear.Applied&&wear.CurrentRaw==3999,
                "repair TLS seed wear failed");

            var repairs=new NpcRepairCatalog([
                new NpcRepairCatalog.RepairPoint(
                    80,88,8,8,10,"old:Repair.txt:OpenRepair(10)")
            ]);
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,
                repairs:repairs,repairItemRules:itemRules);
            var router=new WorldResultRouter();

            var first=await StartSession(authorization,router);
            await LoginAndChoose(first.Tls,characterId,
                "repair_tls_user","Repair-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(first.Tls),out var inventory)&&
                inventory is not null&&inventory.Complete&&inventory.Items.Count==0,
                "repair TLS equipped item leaked into inventory baseline");
            Checks.Require(WireProtocol.TryDecodeEquipmentSnapshot(
                await ReadFrame(first.Tls),out var equipment)&&
                equipment is not null&&equipment.Complete&&equipment.Items.Count==1&&
                equipment.Items[0].ItemUid==itemUid,
                "repair TLS equipment baseline mismatch");

            long epoch=_lastMapEpoch;
            var openFrame=WireProtocol.EncodeRepairOpen(new(epoch,1,88));
            await first.Tls.WriteAsync(openFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeRepairSnapshot(
                await ReadFrame(first.Tls),out var opened)&&opened is not null&&
                opened.Status==RepairOpenStatus.Opened&&opened.NpcSpawnId==88&&
                opened.WalletMoney==100&&opened.Complete&&opened.Items.Count==1&&
                opened.Items[0].ItemUid==itemUid&&
                opened.Items[0].Location==RepairItemLocation.Equipped&&
                opened.Items[0].CurrentDurability==80&&
                opened.Items[0].MaxDurability==100&&opened.Items[0].Cost==20,
                "repair TLS preview mismatch");

            var repairFrame=WireProtocol.EncodeRepairItem(new(epoch,2,88,itemUid));
            await first.Tls.WriteAsync(repairFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeRepairResult(
                await ReadFrame(first.Tls),out var repaired)&&
                repaired.Status==RepairStatus.Applied&&
                repaired.ItemUid==itemUid&&repaired.ItemDefinitionId==3701&&
                repaired.Location==RepairItemLocation.Equipped&&
                repaired.CurrentDurability==100&&repaired.MaxDurability==100&&
                repaired.Cost==20&&repaired.WalletMoney==80,
                "repair TLS settlement mismatch");

            await first.Tls.WriteAsync(repairFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeRepairResult(
                await ReadFrame(first.Tls),out var replay)&&
                replay.Status==RepairStatus.AlreadyApplied&&
                replay.ItemUid==itemUid&&replay.Cost==20&&
                replay.WalletMoney==80,
                "repair TLS replay mismatch");

            await first.Tls.DisposeAsync();
            first.Client.Dispose();
            var ended=await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended.Reason==TlsWireEndReason.CleanEof,
                "repair TLS first session did not close cleanly");

            var second=await StartSession(authorization,router);
            await LoginAndChoose(second.Tls,characterId,
                "repair_tls_user","Repair-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(second.Tls),out var inventory2)&&
                inventory2 is not null&&inventory2.Complete,
                "repair TLS relogin inventory baseline missing");
            Checks.Require(WireProtocol.TryDecodeEquipmentSnapshot(
                await ReadFrame(second.Tls),out var equipment2)&&
                equipment2 is not null&&equipment2.Complete&&
                equipment2.Items.Single().ItemUid==itemUid,
                "repair TLS relogin equipment baseline missing");

            var reopen=WireProtocol.EncodeRepairOpen(new(_lastMapEpoch,1,88));
            await second.Tls.WriteAsync(reopen);
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeRepairSnapshot(
                await ReadFrame(second.Tls),out var full)&&full is not null&&
                full.Status==RepairOpenStatus.Opened&&full.WalletMoney==80&&
                full.Items.Single().CurrentDurability==100&&
                full.Items.Single().MaxDurability==100&&
                full.Items.Single().Cost==0,
                "repair TLS durable relogin preview mismatch");

            await second.Tls.DisposeAsync();
            second.Client.Dispose();
            var ended2=await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(ended2.Reason==TlsWireEndReason.CleanEof,
                "repair TLS second session did not close cleanly");

            var durable=await store.ListRepairPreviewAsync(
                characterId,new TrustedRepairNpcRule(
                    88,10,"old:Repair.txt:OpenRepair(10)"),itemRules);
            Checks.Require(durable.WalletMoney==80&&
                durable.Items.Single().CurrentDurability==100&&
                durable.Items.Single().Cost==0,
                "repair durable final state mismatch");
            await world.StopAsync();
        }
        finally
        {
            try{Directory.Delete(identityRoot,true);}catch{}
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
        Checks.Require(command.ExecuteNonQuery()==1,"repair TLS wallet seed failed");
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
            "repair TLS auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(
            await ReadFrame(tls),out var list)&&
            list.Characters.Single().CharacterId==characterId,
            "repair TLS list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await ReadFrame(tls),out var choose)&&
            choose.Status==CharacterChooseWireStatus.Success,
            "repair TLS choose failed");
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
            "repair TLS invalid frame length");
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)
            await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
