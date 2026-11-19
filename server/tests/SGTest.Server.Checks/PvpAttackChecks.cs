using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class PvpAttackChecks
{
    public static void Verify(string root)
    {
        Checks.Run("PvP attack compact wire and reliable receipts",Wire);
        Checks.Run("PvP geometry enforces NonPK range and Blocking",
            ()=>Geometry(root).GetAwaiter().GetResult());
        Checks.Run("PvP damage shares combat settlement and crime state",
            ()=>Settlement(root).GetAwaiter().GetResult());
    }

    private static void Wire()
    {
        var attack=new PvpAttackWire(3,7,99);
        var bytes=WireProtocol.EncodePvpAttack(attack);
        Checks.Require(bytes.Length==WireProtocol.PvpAttackFrameBytes&&
            WireProtocol.TryDecodePvpAttack(bytes,out var decoded)&&decoded==attack,
            "PvP attack wire mismatch");
        var result=new PvpAttackResultWire(3,7,PvpAttackStatus.Applied,44,99,28,false);
        Checks.Require(WireProtocol.TryDecodePvpAttackResult(
            WireProtocol.EncodePvpAttackResult(result),out var parsed)&&parsed==result,
            "PvP attacker result wire mismatch");
        var damage=new PvpDamageWire(3,2,44,88,5,7,28,72,100,false);
        Checks.Require(WireProtocol.TryDecodePvpDamage(
            WireProtocol.EncodePvpDamage(damage),out var pd)&&pd==damage,
            "PvP victim damage wire mismatch");

        var actor=new ActorSnapshot(88,13,5,3,0,0,8,8,1,false);
        var gateway=new GatewayConnection(actor);
        var trusted=new TrustedPvpAttack(88,5,3,7,99,null);
        Checks.Require(gateway.QueuePvpAttackResult(trusted,result)==GatewaySendStatus.Enqueued&&
            gateway.TryDequeueReliable(out var q)&&WireProtocol.TryDecodePvpAttackResult(q,out var qr)&&qr==result,
            "reliable PvP attacker receipt missing");
    }

    private static async Task Geometry(string root)
    {
        string clear=Checks.Fixture(root,13,8,8,(_,_)=>0);
        await using(var world=new ServerWorldHost([BlockingGrid.Load(clear,13)]))
        {
            await world.StartAsync();
            var a=await world.TrustedRegisterAsync(701,13,8,8)??throw new Exception();
            var b=await world.TrustedRegisterAsync(702,13,72,8)??throw new Exception();
            Checks.Require((await world.TrustedGetPvpAttackGeometryAsync(
                a.ActorId,a.ConnectionEpoch,a.MapEpoch,b.ActorId,48)).Status==
                PvpAttackGeometryStatus.Ready,"clear in-range PvP rejected");
            Checks.Require((await world.TrustedGetPvpAttackGeometryAsync(
                a.ActorId,a.ConnectionEpoch,a.MapEpoch,b.ActorId,8)).Status==
                PvpAttackGeometryStatus.TooFar,"PvP range ignored");
            await world.StopAsync();
        }
        string blocked=Checks.Fixture(root,13,8,8,(x,y)=>(byte)(x==2&&y==0?7:0));
        await using(var world=new ServerWorldHost([BlockingGrid.Load(blocked,13)]))
        {
            await world.StartAsync();
            var a=await world.TrustedRegisterAsync(703,13,8,8)??throw new Exception();
            var b=await world.TrustedRegisterAsync(704,13,72,8)??throw new Exception();
            Checks.Require((await world.TrustedGetPvpAttackGeometryAsync(
                a.ActorId,a.ConnectionEpoch,a.MapEpoch,b.ActorId,48)).Status==
                PvpAttackGeometryStatus.Blocked,"PvP Blocking line ignored");
            await world.StopAsync();
        }
        string safe=Checks.Fixture(root,15,8,8,(_,_)=>0);
        await using(var world=new ServerWorldHost([BlockingGrid.Load(safe,15)]))
        {
            await world.StartAsync();
            var a=await world.TrustedRegisterAsync(801,15,8,8)??throw new Exception();
            var b=await world.TrustedRegisterAsync(802,15,24,8)??throw new Exception();
            Checks.Require((await world.TrustedGetPvpAttackGeometryAsync(
                a.ActorId,a.ConnectionEpoch,a.MapEpoch,b.ActorId,48)).Status==
                PvpAttackGeometryStatus.NonPvpMap,"Map15 admitted PvP");
            await world.StopAsync();
        }
    }

    private static async Task Settlement(string root)
    {
        string dir=Path.Combine(root,"pvp_attack_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        using var store=new SqliteIdentityStore(Path.Combine(dir,"identity.db"));
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync("pvp_attack","Pvp-Attack-Password-123!")).AccountId;
        long aid=(await store.CreateCharacterAsync(account,0,"PvpAttacker",new(13,8,8))).Character!.CharacterId;
        long tid=(await store.CreateCharacterAsync(account,1,"PvpTarget",new(13,40,8))).Character!.CharacterId;
        var ap=Profile(30,0); var tp=Profile(10,10);
        await store.SetCharacterCombatProfileAsync(aid,ap);
        await store.SetCharacterCombatProfileAsync(tid,tp);
        await store.SetCharacterPvpModeAsync(aid,PvpMode.EnabledWhite);
        await store.SetCharacterPvpModeAsync(tid,PvpMode.EnabledWhite);
        string map=Checks.Fixture(root,13,8,8,(_,_)=>0);
        await using var world=new ServerWorldHost([BlockingGrid.Load(map,13)]);
        await world.StartAsync();
        var a=await world.TrustedRegisterAsync(aid,13,8,8)??throw new Exception();
        var t=await world.TrustedRegisterAsync(tid,13,40,8)??throw new Exception();
        var victimGateway=new GatewayConnection(t);
        var baseTime=DateTimeOffset.FromUnixTimeMilliseconds(2_100_000_000_000L);
        int step=0;
        var combat=new PlayerCombatSettlement(store,new(Capacity:8),victimGateway.QueuePlayerDamage,
            committedLifecycle:async result=>{
                var now=baseTime.AddSeconds(step++);
                if(result.Died)
                {
                    await store.MarkPvpWhiteKillAsync(result.AttackerActorId,result.VictimWasNormalWhite,now);
                    Checks.Require(await world.TrustedSetCombatDeadAsync(
                        result.ActorId,result.ConnectionEpoch,result.MapEpoch,true) is not null,
                        "lethal PvP failed to stop target");
                }
                else await store.MarkPvpHostileAttackAsync(
                    result.AttackerActorId,result.VictimWasNormalWhite,now);
            });
        Checks.Require(combat.TryAttach(a,ap)&&combat.TryAttach(t,tp)&&
            combat.TryResume(a)&&combat.TryResume(t),"combat attach");
        using var stop=new CancellationTokenSource();
        var worker=combat.RunAsync(stop.Token);
        var gate=new PvpAttackSequenceGate();
        var request=new TrustedPvpAttack(a.ActorId,a.ConnectionEpoch,a.MapEpoch,1,t.ActorId,
            new TrustedCombatProfile(30,0,10,3,48,15));
        Checks.Require(gate.Inspect(request)==PvpAttackSequenceDecision.New,"sequence");
        Checks.Require(combat.TryPostPvpHit(t,a,1,30,100,true,out var c1)==
            CombatPostStatus.Accepted&&c1 is not null,"first PvP post");
        var first=await c1!.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Record(request,new(a.MapEpoch,1,PvpAttackStatus.Applied,100,t.ActorId,first.Damage,false),true);
        Checks.Require(first.Damage==28&&first.CurrentHp==72&&!gate.CanApplyAt(114,15)&&
            gate.CanApplyAt(115,15),"damage/cooldown");
        Checks.Require(victimGateway.TryDequeueReliable(out var f1)&&
            WireProtocol.TryDecodePvpDamage(f1,out var vd1)&&vd1.Damage==28&&vd1.CurrentHp==72,
            "victim first receipt");
        Checks.Require(PvpCrimePolicy.NameState(
            await store.GetCharacterPvpStateAsync(aid),baseTime)==PvpNameState.Pink,
            "normal-white hit not pink");

        var request2=request with { AttackSequence=2 };
        Checks.Require(combat.TryPostPvpHit(t,a,2,1000,115,true,out var c2)==
            CombatPostStatus.Accepted&&c2 is not null,"lethal PvP post");
        var second=await c2!.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Record(request2,new(a.MapEpoch,2,PvpAttackStatus.Applied,115,t.ActorId,second.Damage,true),true);
        Checks.Require(second.Died&&second.Damage==72&&second.CurrentHp==0,"lethal clamp");
        Checks.Require(victimGateway.TryDequeueReliable(out var f2)&&
            WireProtocol.TryDecodePvpDamage(f2,out var vd2)&&vd2.Died&&vd2.DamageSequence==2,
            "victim lethal receipt");
        Checks.Require(PvpCrimePolicy.NameState(
            await store.GetCharacterPvpStateAsync(aid),baseTime.AddSeconds(1))==PvpNameState.Red,
            "white kill not red");
        Checks.Require(await world.TrustedGetActorAsync(tid) is { CombatDead:true },
            "target World not dead");
        stop.Cancel();
        try{await worker;}catch(OperationCanceledException){}
        await world.StopAsync();
    }

    private static CharacterCombatProfileData Profile(int attack,int defense)=>
        new(CombatProfileTrust.SyntheticTest,"ci:pvp-attack",0,0,5,5,5,5,1,
            100,100,attack,defense,10,3,48,15);
}
