using Microsoft.Data.Sqlite;
using SGTest.Server.Gateway;
using SGTest.Server.Hotkey;
using SGTest.Server.Identity;
using SGTest.Server.Pvp;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class HotkeyChecks
{
    public static void Verify()
    {
        Checks.Run("hotkey wire roundtrips 12-slot item/skill snapshot",Wire);
        Checks.Run("hotkey authority validates inventory UID and survives relog",
            ()=>AuthorityAndPersistence().GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var save=new HotkeyCommandWire(
            7,1,HotkeyCommandAction.Save,11,HotkeyDataType.Item,1234);
        var bytes=WireProtocol.EncodeHotkeyCommand(save);
        Checks.Require(
            bytes.Length==WireProtocol.HotkeyCommandFrameBytes&&
            WireProtocol.TryDecodeHotkeyCommand(bytes,out var decoded)&&decoded==save,
            "hotkey save wire roundtrip failed");

        var refresh=new HotkeyCommandWire(
            7,2,HotkeyCommandAction.Refresh,0,HotkeyDataType.Item,0);
        Checks.Require(
            WireProtocol.TryDecodeHotkeyCommand(
                WireProtocol.EncodeHotkeyCommand(refresh),out decoded)&&decoded==refresh,
            "hotkey refresh wire roundtrip failed");

        var snapshot=new HotkeySnapshotWire(
            7,2,HotkeyResultStatus.Applied,
            [
                new HotkeyEntryWire(0,HotkeyDataType.Item,1234),
                new HotkeyEntryWire(11,HotkeyDataType.Skill,99001)
            ]);
        Checks.Require(
            WireProtocol.TryDecodeHotkeySnapshot(
                WireProtocol.EncodeHotkeySnapshot(snapshot),out var parsed)&&
            parsed is not null&&parsed.ReceiverMapEpoch==7&&
            parsed.HotkeySequence==2&&parsed.Status==HotkeyResultStatus.Applied&&
            parsed.Entries.Count==2&&parsed.Entries[0].Slot==0&&
            parsed.Entries[1].Slot==11&&parsed.Entries[1].DataId==99001,
            "hotkey snapshot roundtrip failed");

        var malformed=WireProtocol.EncodeHotkeyCommand(save);
        malformed[21]=12;
        Checks.Require(!WireProtocol.TryDecodeHotkeyCommand(malformed,out _),
            "hotkey wire accepted slot 12");
    }

    private static async Task AuthorityAndPersistence()
    {
        string root=Path.Combine(
            Path.GetTempPath(),"sgtest_hotkey_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string db=Path.Combine(root,"identity.db");
        try
        {
            long actorId;
            long itemUid;
            using(var store=new SqliteIdentityStore(db))
            {
                await store.InitializeAsync();
                long account=(await store.CreateAccountAsync(
                    "hotkey_actor","Hotkey-Account-Secret-2026!")).AccountId;
                actorId=(await store.CreateCharacterAsync(
                    account,0,"HotkeyActor",new(15,100,100))).Character!.CharacterId;
                await store.SetCharacterSkillLevelAsync(
                    actorId,28101,3,"hotkey-check");

                using(var connection=new SqliteConnection($"Data Source={db}"))
                {
                    connection.Open();
                    using var command=connection.CreateCommand();
                    command.CommandText=@"INSERT INTO item_instances(
character_id,item_definition_id,quantity,source_drop_id,durability_raw,
enhancement_level,source_kind,source_reference,created_unix_ms)
VALUES($actor,1001,2,NULL,NULL,0,2,'hotkey-check',$now);
SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("$actor",actorId);
                    command.Parameters.AddWithValue(
                        "$now",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    itemUid=Convert.ToInt64(command.ExecuteScalar());
                }

                var router=new WorldResultRouter();
                var gateway=Gateway(Actor(actorId,11,3),"HotkeyActor");
                Checks.Require(router.TryAttach(gateway),
                    "hotkey gateway attach failed");
                var coordinator=new HotkeyCoordinator(store);

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,1,HotkeyCommandAction.Save,11,HotkeyDataType.Item,itemUid),
                    coordinator,router);
                var saved=DrainSnapshot(gateway);
                Checks.Require(
                    saved.Status==HotkeyResultStatus.Applied&&
                    saved.Entries.Count==1&&saved.Entries[0].Slot==11&&
                    saved.Entries[0].DataType==HotkeyDataType.Item&&
                    saved.Entries[0].DataId==itemUid,
                    "server did not persist valid inventory hotkey");

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,2,HotkeyCommandAction.Save,10,HotkeyDataType.Item,itemUid+999999),
                    coordinator,router);
                var rejected=DrainSnapshot(gateway);
                Checks.Require(
                    rejected.Status==HotkeyResultStatus.InvalidBinding&&
                    rejected.Entries.Count==1&&rejected.Entries[0].Slot==11,
                    "server accepted forged inventory UID or lost valid binding");

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,3,HotkeyCommandAction.Remove,11,HotkeyDataType.Item,0),
                    coordinator,router);
                var removed=DrainSnapshot(gateway);
                Checks.Require(
                    removed.Status==HotkeyResultStatus.Applied&&removed.Entries.Count==0,
                    "server did not authoritatively clear source hotkey slot");

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,4,HotkeyCommandAction.Save,10,HotkeyDataType.Item,itemUid),
                    coordinator,router);
                var moved=DrainSnapshot(gateway);
                Checks.Require(
                    moved.Status==HotkeyResultStatus.Applied&&moved.Entries.Count==1&&
                    moved.Entries[0].Slot==10&&moved.Entries[0].DataId==itemUid,
                    "server did not persist moved hotkey binding");

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,5,HotkeyCommandAction.Save,9,HotkeyDataType.Skill,28101),
                    coordinator,router);
                var skillSaved=DrainSnapshot(gateway);
                Checks.Require(
                    skillSaved.Status==HotkeyResultStatus.Applied&&
                    skillSaved.Entries.Count==2&&
                    skillSaved.Entries.Any(x=>x.Slot==9&&
                        x.DataType==HotkeyDataType.Skill&&x.DataId==28101),
                    "server did not persist learned skill hotkey");

                FeedHotkey(gateway,new HotkeyCommandWire(
                    3,6,HotkeyCommandAction.Save,8,HotkeyDataType.Skill,28102),
                    coordinator,router);
                var skillRejected=DrainSnapshot(gateway);
                Checks.Require(
                    skillRejected.Status==HotkeyResultStatus.InvalidBinding&&
                    skillRejected.Entries.Count==2&&
                    !skillRejected.Entries.Any(x=>x.Slot==8),
                    "server accepted unlearned skill hotkey or lost durable bindings");
            }

            using(var reopened=new SqliteIdentityStore(db))
            {
                await reopened.InitializeAsync();
                var durable=await reopened.ListCharacterHotkeysAsync(actorId);
                Checks.Require(
                    durable.Count==2&&
                    durable.Any(x=>x.Slot==10&&
                        x.DataType==(int)HotkeyDataType.Item&&x.DataId==itemUid)&&
                    durable.Any(x=>x.Slot==9&&
                        x.DataType==(int)HotkeyDataType.Skill&&x.DataId==28101),
                    "item/skill hotkey bindings did not survive SQLite reopen");

                var router=new WorldResultRouter();
                var relog=Gateway(Actor(actorId,12,4),"HotkeyActor");
                Checks.Require(router.TryAttach(relog),
                    "hotkey relog gateway attach failed");
                var coordinator=new HotkeyCoordinator(reopened);
                FeedHotkey(relog,new HotkeyCommandWire(
                    4,1,HotkeyCommandAction.Refresh,0,HotkeyDataType.Item,0),
                    coordinator,router);
                var refreshed=DrainSnapshot(relog);
                Checks.Require(
                    refreshed.Status==HotkeyResultStatus.Applied&&
                    refreshed.Entries.Count==2&&
                    refreshed.Entries.Any(x=>x.Slot==10&&
                        x.DataType==HotkeyDataType.Item&&x.DataId==itemUid)&&
                    refreshed.Entries.Any(x=>x.Slot==9&&
                        x.DataType==HotkeyDataType.Skill&&x.DataId==28101),
                    "relog refresh did not return durable item/skill hotkeys");
            }
        }
        finally
        {
            try{Directory.Delete(root,true);}catch{}
        }
    }

    private static void FeedHotkey(
        GatewayConnection gateway,HotkeyCommandWire command,
        HotkeyCoordinator coordinator,WorldResultRouter router)
    {
        var status=gateway.FeedClientBytes(
            WireProtocol.EncodeHotkeyCommand(command),_=>true,out int accepted,
            postHotkey:value=>
            {
                coordinator.HandleAsync(value,router).GetAwaiter().GetResult();
                return true;
            });
        Checks.Require(status==WireFeedStatus.Accepted&&accepted==1,
            "hotkey encoded client frame was not admitted through Gateway");
    }

    private static GatewayConnection Gateway(ActorSnapshot actor,string name)
    {
        var pvp=new CharacterPvpState(
            actor.ActorId,PvpMode.ProtectedBlue,false,0,0,0);
        return new GatewayConnection(actor,displayName:name,pvpState:pvp);
    }

    private static ActorSnapshot Actor(long id,long connection,long mapEpoch)=>
        new(id,15,connection,mapEpoch,0,0,8,8,1,false,1,false);

    private static HotkeySnapshotWire DrainSnapshot(GatewayConnection gateway)
    {
        if(!gateway.TryDequeueReliable(out var frame)||
           !WireProtocol.TryDecodeHotkeySnapshot(frame,out var value)||
           value is null)
            throw new Exception("hotkey snapshot missing");
        return value;
    }
}
