using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.Identity;
using SGTest.Server.Wire;
using SGTest.Server.World;

internal static class CombatDeathGateChecks
{
    private static void Require(bool condition, string message) =>
        Checks.Require(condition, message);

    public static void Verify(string root)
    {
        Checks.Run("combat death stops authoritative route and rejects Move/NPC without consuming sequence",
            () => WorldDeathGate(root));
        Checks.Run("lethal commit stops World owner before reliable PlayerDamage delivery",
            () => CommitStopBeforeDelivery(root).GetAwaiter().GetResult());
        Checks.Run("combat death wire statuses roundtrip without changing frame sizes",
            WireStatuses);
    }

    private static void WorldDeathGate(string root)
    {
        string aDir=Checks.Fixture(root,220,12,12,(_,_)=>0);
        string bDir=Checks.Fixture(root,221,12,12,(_,_)=>0);
        var world=new ServerWorld([
            BlockingGrid.Load(aDir,220),
            BlockingGrid.Load(bDir,221)
        ]);
        Require(world.TryTrustedRegister(700,220,24,24,out var actor),
            "death gate actor register");
        Require(world.TryPostMove(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,1,136,24)),
            "death gate first move enqueue");
        var first=world.Tick();
        Require(first.Receipts.Count==1 &&
                first.Receipts[0].Status==MoveReceiptStatus.Accepted,
            "death gate first move receipt");
        var moving=world.GetActor(actor.ActorId)!.Value;
        Require(moving.IsMoving && moving.X>24,
            "death gate fixture never entered movement");

        Require(world.TryTrustedSetCombatDead(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,true,out var dead) &&
                !dead.IsMoving,
            "trusted lethal stop did not clear route");
        var stopTick=world.Tick();
        Require(stopTick.Changed.Any(x=>
                x.ActorId==actor.ActorId &&
                x.EntityRevision==dead.EntityRevision &&
                !x.IsMoving),
            "death stop was not published through normal changed stream");
        double stoppedX=world.GetActor(actor.ActorId)!.Value.X;

        Require(world.TryPostMove(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,2,136,24)),
            "dead malicious move enqueue");
        var rejected=world.Tick();
        Require(rejected.Receipts.Count==1 &&
                rejected.Receipts[0].Status==MoveReceiptStatus.CombatDead &&
                Math.Abs(world.GetActor(actor.ActorId)!.Value.X-stoppedX)<0.001,
            "dead actor moved or did not receive CombatDead receipt");

        var portals=new PortalCatalog([
            new PortalCatalog.Portal(
                220,1,stoppedX,24,221,24,24)
        ]);
        var npc=world.TryTrustedNpcInteract(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,
            1,1,portals);
        Require(npc.Status==NpcPortalStatus.CombatDead &&
                npc.TransferredActor is null &&
                world.GetActor(actor.ActorId)!.Value.MapId==220,
            "dead actor used NPC transfer");

        Require(world.TryTrustedSetCombatDead(
                actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,false,out var revived),
            "trusted revive gate clear");
        world.Tick();
        // CombatDead rejection deliberately did not consume movement sequence=2.
        Require(world.TryPostMove(new(
            revived.ActorId,revived.ConnectionEpoch,revived.MapEpoch,2,136,24)),
            "revived move enqueue");
        var resumed=world.Tick();
        Require(resumed.Receipts.Count==1 &&
                resumed.Receipts[0].Status==MoveReceiptStatus.Accepted,
            "dead rejected sequence poisoned revived movement timeline");
    }

    private static async Task CommitStopBeforeDelivery(string root)
    {
        string mapDir=Checks.Fixture(root,222,12,12,(_,_)=>0);
        var grid=BlockingGrid.Load(mapDir,222);
        await using var world=new ServerWorldHost(
            [grid],options:new WorldHostOptions(PublishIdleTicks:true));
        await world.StartAsync();

        string db=Path.Combine(root,
            "death-delivery-"+Guid.NewGuid().ToString("N")+".db");
        using var store=new SqliteIdentityStore(db);
        await store.InitializeAsync();
        long account=(await store.CreateAccountAsync(
            "death_delivery","Death-Delivery-Password-123!")).AccountId;
        long character=(await store.CreateCharacterAsync(
            account,0,"DeathDelivery",
            new CharacterSpawn(222,24,24))).Character!.CharacterId;
        var profile=new CharacterCombatProfileData(
            CombatProfileTrust.SyntheticTest,"ci:death-gate",
            Race:0,Career:0,Strength:5,Dexterity:5,Constitution:5,Wisdom:5,
            Level:1,CurrentHp:18,MaxHp:100,Attack:20,Defense:10,
            Hit:10,Dodge:3,AttackRange:48,AttackCooldownTicks:15);
        await store.SetCharacterCombatProfileAsync(character,profile);

        var actor=await world.TrustedRegisterAsync(character,222,24,24)
            ?? throw new Exception("death delivery world register");
        var gateway=new GatewayConnection(actor);
        bool worldStoppedBeforeDelivery=false;
        var settlement=new PlayerCombatSettlement(
            store,new(Capacity:8),
            delivery: result =>
            {
                if (!worldStoppedBeforeDelivery)
                    throw new PlayerCombatSettlementException(
                        "lethal result reached delivery before World stop");
                return gateway.QueuePlayerDamage(result);
            },
            committedLifecycle: async result =>
            {
                if (!result.Died) return;
                var stopped=await world.TrustedSetCombatDeadAsync(
                    result.ActorId,result.ConnectionEpoch,result.MapEpoch,true)
                    .ConfigureAwait(false);
                if (stopped is null || stopped.Value.IsMoving)
                    throw new PlayerCombatSettlementException(
                        "lethal commit failed to stop authoritative World actor");
                worldStoppedBeforeDelivery=true;
            });
        Require(settlement.TryAttach(actor,profile) &&
                settlement.TryResume(actor),
            "death delivery settlement attach");

        Require(world.TryPostMove(new(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch,1,136,24)),
            "death delivery route enqueue");
        using var routeTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        ActorSnapshot current=actor;
        while(!current.IsMoving)
        {
            routeTimeout.Token.ThrowIfCancellationRequested();
            current=await world.TrustedGetActorAsync(actor.ActorId)
                ?? throw new Exception("death delivery actor disappeared");
            if(!current.IsMoving) await Task.Delay(5,routeTimeout.Token);
        }

        var hit=new MonsterWorldEvent(
            MonsterWorldEventKind.Attack,
            Math.Max(1,world.Statistics.Ticks),
            new MonsterSnapshot(
                1_002_220_001,1,0,222,40,24,40,24,
                100,100,MonsterAiState.Attack,actor.ActorId,2,2),
            actor.ActorId,20);
        Require(settlement.TryPostMonsterHit(current,hit)==CombatPostStatus.Accepted,
            "death delivery lethal hit enqueue");

        using var stop=new CancellationTokenSource();
        var worker=settlement.RunAsync(stop.Token);
        await settlement.SuspendAndDrainAsync(
            actor.ActorId,actor.ConnectionEpoch,actor.MapEpoch)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Require(worldStoppedBeforeDelivery,
            "death delivery callback order");
        var after=await world.TrustedGetActorAsync(actor.ActorId)
            ?? throw new Exception("death delivery actor missing after lethal commit");
        Require(!after.IsMoving,
            "authoritative route remained active after lethal commit");
        Require(gateway.TryDequeueReliable(out var frame) &&
                WireProtocol.TryDecodePlayerDamage(frame,out var damage) &&
                damage.Died && damage.CurrentHp==0 &&
                damage.Damage==18 && damage.DamageSequence==1,
            "lethal reliable damage missing after World stop");

        var persisted=await store.GetCharacterCombatProfileAsync(actor.ActorId);
        Require(persisted?.Data.CurrentHp==0,
            "lethal HP was not durable");

        stop.Cancel();
        await worker.WaitAsync(TimeSpan.FromSeconds(2));
        await world.StopAsync();
    }

    private static void WireStatuses()
    {
        var move=new MoveReceiptWire(
            3,2,MoveReceiptStatus.CombatDead,42);
        var moveBytes=WireProtocol.EncodeMoveReceipt(move);
        Require(moveBytes.Length==WireProtocol.MoveReceiptFrameBytes &&
                WireProtocol.TryDecodeMoveReceipt(moveBytes,out var decodedMove) &&
                decodedMove==move,
            "CombatDead move receipt wire");

        var npc=new NpcInteractResultWire(
            3,2,NpcPortalStatus.CombatDead,3);
        var npcBytes=WireProtocol.EncodeNpcInteractResult(npc);
        Require(npcBytes.Length==WireProtocol.NpcInteractResultFrameBytes &&
                WireProtocol.TryDecodeNpcInteractResult(npcBytes,out var decodedNpc) &&
                decodedNpc==npc,
            "CombatDead NPC result wire");
    }
}
