using SGTest.Server.Visibility;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PlayerRespawnChecks
{
    private static void Require(bool condition,string message) =>
        Checks.Require(condition,message);

    public static void Verify(string root)
    {
        Checks.Run("player respawn wire carries only map generation and sequence", Wire);
        Checks.Run("dead combat profile restores MaxHP atomically once",
            () => Store(root).GetAwaiter().GetResult());
        Checks.Run("combat respawn stays untargetable until durable restore is resumed",
            () => Settlement(root).GetAwaiter().GetResult());
        Checks.Run("gateway injects authenticated identity into respawn request", Gateway);
    }

    private static void Wire()
    {
        var request=new PlayerRespawnWire(9,7);
        byte[] frame=WireProtocol.EncodePlayerRespawn(request);
        Require(frame.Length==20 &&
                WireProtocol.TryDecodePlayerRespawn(frame,out var parsed) &&
                parsed==request,
            "respawn request roundtrip/size");

        var result=new PlayerRespawnResultWire(
            9,7,PlayerRespawnStatus.Accepted,100,100);
        byte[] response=WireProtocol.EncodePlayerRespawnResult(result);
        Require(response.Length==29 &&
                WireProtocol.TryDecodePlayerRespawnResult(response,out var decoded) &&
                decoded==result,
            "respawn result roundtrip/size");

        bool rejected=false;
        try
        {
            WireProtocol.EncodePlayerRespawnResult(
                result with { CurrentHp=50 });
        }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        Require(rejected,"accepted respawn allowed client-visible partial HP baseline");
    }

    private static async Task Store(string root)
    {
        string db=Path.Combine(root,
            "respawn-store-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "respawn_store","Respawn-Store-Password-123!")).AccountId;
        long character=(await store.CreateCharacterAsync(
            account,0,"RespawnStoreHero",new CharacterSpawn(15,24,24)))
            .Character!.CharacterId;
        Require(await store.RestoreCharacterFullHpAsync(character) is null,
            "missing combat profile invented HP");

        var profile=Profile(hp:0);
        await store.SetCharacterCombatProfileAsync(character,profile);
        var restored=await store.RestoreCharacterFullHpAsync(character);
        Require(restored?.Data.CurrentHp==100 &&
                restored.Data.MaxHp==100,
            "dead profile did not restore MaxHP");
        Require(await store.RestoreCharacterFullHpAsync(character) is null,
            "already-alive profile respawned twice");
    }

    private static async Task Settlement(string root)
    {
        string db=Path.Combine(root,
            "respawn-settlement-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "respawn_settle","Respawn-Settle-Password-123!")).AccountId;
        long character=(await store.CreateCharacterAsync(
            account,0,"RespawnSettleHero",new CharacterSpawn(15,24,24)))
            .Character!.CharacterId;
        var profile=Profile(hp:0);
        await store.SetCharacterCombatProfileAsync(character,profile);

        var actor=new ActorSnapshot(
            character,15,1,1,0,0,24,24,1,false);
        var combat=new PlayerCombatSettlement(store,new(Capacity:4));
        Require(combat.TryAttach(actor,profile),"dead combat attach");
        var outcome=await combat.RespawnFullAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,1);
        Require(outcome.Status==PlayerRespawnStatus.Accepted &&
                outcome.CurrentHp==100 && outcome.MaxHp==100 &&
                combat.CurrentHp(actor)==0,
            "respawn became targetable before reliable lifecycle could finish");
        Require(combat.TryResume(actor) && combat.CurrentHp(actor)==100,
            "respawn did not reopen authoritative targetability");

        var replay=await combat.RespawnFullAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,1);
        Require(replay.Status==PlayerRespawnStatus.ReplayedSequence,
            "respawn sequence replay not rejected");
        var alive=await combat.RespawnFullAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,2);
        Require(alive.Status==PlayerRespawnStatus.NotDead &&
                alive.CurrentHp==100,
            "already-alive respawn did not fail closed");
    }

    private static void Gateway()
    {
        var actor=new ActorSnapshot(
            91,15,3,4,0,0,24,24,1,false);
        var gateway=new GatewayConnection(actor);
        Require(gateway.QueueAoi(
                    new(AoiEventKind.WorldReady,11,actor.MapEpoch,actor))==
                GatewaySendStatus.Enqueued &&
                gateway.FeedClientBytes(
                    WireProtocol.EncodeWorldReadyAck(
                        new(actor.MapEpoch,11)),
                    _=>true,out _)==WireFeedStatus.Accepted,
            "respawn gateway Ready ACK fixture");

        TrustedPlayerRespawn captured=default;
        byte[] request=WireProtocol.EncodePlayerRespawn(
            new(actor.MapEpoch,7));
        Require(gateway.FeedClientBytes(
                    request,_=>true,out var accepted,
                    postRespawn:value=>{captured=value;return true;})==
                WireFeedStatus.Accepted &&
                accepted==1 &&
                captured.ActorId==actor.ActorId &&
                captured.ConnectionEpoch==actor.ConnectionEpoch &&
                captured.RequestMapEpoch==actor.MapEpoch &&
                captured.RespawnSequence==7,
            "respawn gateway did not inject authenticated identity");
    }

    private static CharacterCombatProfileData Profile(int hp) =>
        new(CombatProfileTrust.SyntheticTest,
            "ci:respawn-authority",
            Race:0,Career:0,Strength:5,Dexterity:5,
            Constitution:5,Wisdom:5,Level:1,
            CurrentHp:hp,MaxHp:100,Attack:20,Defense:10,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15);
}
