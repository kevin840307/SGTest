using SGTest.Server.Map;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class EquipmentTlsChecks
{
    public static void Verify(string root)
    {
        Checks.Run("TLS equipment UID equip replay relogin and unequip remain durable",
            () => Lifecycle(root).GetAwaiter().GetResult());
    }

    private static async Task Lifecycle(string root)
    {
        string identityRoot=Path.Combine(Path.GetTempPath(),
            "sgtest_equipment_tls_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(identityRoot);
        try
        {
            string mapDir=Checks.Fixture(root,78,5,5,(_,_)=>0);
            var grid=BlockingGrid.Load(mapDir,78);
            await using var world=new ServerWorldHost([grid]);
            await world.StartAsync();
            using var store=new SqliteIdentityStore(Path.Combine(identityRoot,"identity.db"));
            await store.InitializeAsync();
            long accountId=(await store.CreateAccountAsync(
                "equipment_tls_user","Equipment-Tls-Password-123!")).AccountId;
            long characterId=(await store.CreateCharacterAsync(
                accountId,0,"EquipmentTlsHero",new CharacterSpawn(78,8,8)))
                .Character?.CharacterId??throw new Exception("equipment TLS character missing");

            long generation=await store.AllocateLootWorldGenerationAsync();
            var drop=await store.CreateTrustedGroundDropAsync(new(
                78,8,8,9201,
                SqliteIdentityStore.ComposeLootDeathRevision(generation,1),
                0,3701,1,LootSourceTrust.SyntheticTest,"equipment-tls-seed"));
            var grant=await store.ClaimGroundDropAsync(new TrustedPickupContext(
                characterId,1,1,1,78,8,8),drop.DropId);
            Checks.Require(grant.Status==PickupStatus.Granted&&
                grant.ItemUid>0&&grant.Quantity==1,"equipment TLS seed failed");
            long itemUid=grant.ItemUid;
            IReadOnlyDictionary<int,EquipmentRule> rules=
                new Dictionary<int,EquipmentRule>{[3701]=new(3701,4)};
            var authorization=new IdentityTlsAuthorization(
                store,world,pickupWorldGeneration:generation,equipmentRules:rules);
            var router=new WorldResultRouter();

            var first=await StartSession(authorization,router);
            await LoginAndChoose(first.Tls,characterId,"equipment_tls_user",
                "Equipment-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(first.Tls),out var inventory)&&inventory is not null&&
                inventory.Complete&&inventory.Items.Count==1&&
                inventory.Items[0].ItemUid==itemUid&&inventory.Items[0].SourceDropId==drop.DropId,
                "equipment TLS initial inventory baseline mismatch");
            Checks.Require(WireProtocol.TryDecodeEquipmentSnapshot(
                await ReadFrame(first.Tls),out var emptyEquipment)&&emptyEquipment is not null&&
                emptyEquipment.Complete&&emptyEquipment.Items.Count==0,
                "equipment TLS empty equipment baseline mismatch");
            long firstEpoch=_lastMapEpoch;

            var equipFrame=WireProtocol.EncodeEquipmentEquip(new(firstEpoch,1,itemUid));
            await first.Tls.WriteAsync(equipFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEquipmentTransferResult(
                await ReadFrame(first.Tls),out var equipped)&&
                equipped.Status==EquipmentTransferStatus.Applied&&
                equipped.Kind==EquipmentTransferKind.Equip&&
                equipped.ItemUid==itemUid&&equipped.ItemDefinitionId==3701&&
                equipped.SlotId==4&&equipped.SourceDropId==drop.DropId,
                "equipment TLS equip receipt mismatch");
            await first.Tls.WriteAsync(equipFrame);
            await first.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEquipmentTransferResult(
                await ReadFrame(first.Tls),out var replay)&&
                replay.Status==EquipmentTransferStatus.AlreadyApplied&&
                replay.ItemUid==itemUid&&replay.SlotId==4,
                "equipment TLS equip replay mismatch");
            Checks.Require((await store.ListInventoryItemsAsync(characterId)).Count==0&&
                (await store.ListEquippedItemsAsync(characterId)).Single().ItemUid==itemUid,
                "equipment durable equipped location mismatch");

            await first.Tls.DisposeAsync();
            first.Client.Dispose();
            var firstEnded=await first.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(firstEnded.Reason==TlsWireEndReason.CleanEof,
                "equipment TLS first session did not close cleanly");

            var second=await StartSession(authorization,router);
            await LoginAndChoose(second.Tls,characterId,"equipment_tls_user",
                "Equipment-Tls-Password-123!");
            Checks.Require(WireProtocol.TryDecodeInventorySnapshot(
                await ReadFrame(second.Tls),out var emptyInventory)&&emptyInventory is not null&&
                emptyInventory.Complete&&emptyInventory.Items.Count==0,
                "equipped UID leaked into relogin inventory");
            Checks.Require(WireProtocol.TryDecodeEquipmentSnapshot(
                await ReadFrame(second.Tls),out var equipment)&&equipment is not null&&
                equipment.Complete&&equipment.Items.Count==1&&
                equipment.Items[0].ItemUid==itemUid&&equipment.Items[0].SlotId==4&&
                equipment.Items[0].ItemDefinitionId==3701&&
                equipment.Items[0].SourceDropId==drop.DropId,
                "equipment UID/provenance did not survive relogin");

            var unequipFrame=WireProtocol.EncodeEquipmentUnequip(
                new(_lastMapEpoch,1,itemUid));
            await second.Tls.WriteAsync(unequipFrame);
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEquipmentTransferResult(
                await ReadFrame(second.Tls),out var unequipped)&&
                unequipped.Status==EquipmentTransferStatus.Applied&&
                unequipped.Kind==EquipmentTransferKind.Unequip&&
                unequipped.ItemUid==itemUid&&unequipped.SlotId==4,
                "equipment TLS unequip receipt mismatch");
            await second.Tls.WriteAsync(unequipFrame);
            await second.Tls.FlushAsync();
            Checks.Require(WireProtocol.TryDecodeEquipmentTransferResult(
                await ReadFrame(second.Tls),out var unequipReplay)&&
                unequipReplay.Status==EquipmentTransferStatus.AlreadyApplied&&
                unequipReplay.ItemUid==itemUid,
                "equipment TLS unequip replay mismatch");

            await second.Tls.DisposeAsync();
            second.Client.Dispose();
            var secondEnded=await second.ServerTask.WaitAsync(TimeSpan.FromSeconds(3));
            Checks.Require(secondEnded.Reason==TlsWireEndReason.CleanEof,
                "equipment TLS second session did not close cleanly");
            var finalInventory=await store.ListInventoryItemsAsync(characterId);
            Checks.Require(finalInventory.Count==1&&finalInventory[0].ItemUid==itemUid&&
                finalInventory[0].ItemDefinitionId==3701&&finalInventory[0].Quantity==1&&
                finalInventory[0].SourceDropId==drop.DropId&&
                (await store.ListEquippedItemsAsync(characterId)).Count==0,
                "equipment final UID/provenance mismatch");
            await world.StopAsync();
        }
        finally
        {
            try{Directory.Delete(identityRoot,true);}catch{}
        }
    }

    private static async Task<(SslStream Tls,Task<TlsWireConnectionResult> ServerTask,TcpClient Client,long MapEpoch)>
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
        return(tls,serverTask,client,0);
    }

    private static async Task LoginAndChoose(
        SslStream tls,long characterId,string username,string password)
    {
        await tls.WriteAsync(WireProtocol.EncodeAuthRequest(new(username,password)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeAuthResult(await ReadFrame(tls),out var auth)&&
            auth.Status==AuthWireStatus.Success,"equipment TLS auth failed");
        Checks.Require(WireProtocol.TryDecodeCharacterList(await ReadFrame(tls),out var list)&&
            list.Characters.Single().CharacterId==characterId,"equipment TLS list mismatch");
        await tls.WriteAsync(WireProtocol.EncodeCharacterChoose(new(characterId)));
        await tls.FlushAsync();
        Checks.Require(WireProtocol.TryDecodeCharacterChooseResult(
            await ReadFrame(tls),out var choose)&&choose.Status==CharacterChooseWireStatus.Success,
            "equipment TLS choose failed");
        _lastMapEpoch=choose.MapEpoch;
    }

    private static long _lastMapEpoch;

    private static async Task<byte[]> ReadFrame(SslStream tls)
    {
        byte[] header=new byte[WireProtocol.HeaderBytes];
        await tls.ReadExactlyAsync(header);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0,4));
        Checks.Require(length>=WireProtocol.HeaderBytes&&length<=WireProtocol.MaximumFrameBytes,
            "equipment TLS invalid frame length");
        byte[] frame=new byte[length];
        header.CopyTo(frame,0);
        if(length>header.Length)await tls.ReadExactlyAsync(frame.AsMemory(header.Length));
        return frame;
    }
}
