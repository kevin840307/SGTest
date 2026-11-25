using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Monster;
using SGTest.Server.Pet;
using SGTest.Server.Mecha;
using SGTest.Server.Map;
using SGTest.Server.Combat;
using SGTest.Server.Gateway;
using SGTest.Server.World;
using SGTest.Server.Identity;

internal static class WorldAoiChecks
{
    private static void Require(bool condition, string message) => Checks.Require(condition, message);
    private static ActorSnapshot Actor(long id, int map, double x, double y,
        long revision = 1, long connection = 1, long mapEpoch = 1) =>
        new(id, map, connection, mapEpoch, 0, 0, x, y, revision, false);
    private static ActorLifecycleEvent Join(ActorSnapshot actor) =>
        new(ActorLifecycleKind.Joined, actor);
    private static ActorLifecycleEvent Leave(ActorSnapshot actor) =>
        new(ActorLifecycleKind.Left, actor);

    private static void Life(WorldAoi aoi, long tick, params ActorLifecycleEvent[] changes) =>
        aoi.Apply(new WorldChangedBatch(tick, Array.Empty<ActorSnapshot>(), changes));

    private static void Move(WorldAoi aoi, long tick, ActorSnapshot actor) =>
        aoi.Apply(new WorldChangedBatch(tick, new[] { actor }));

    private static AoiEvent[] Drain(WorldAoi aoi, ActorSnapshot observer)
    {
        var output = new List<AoiEvent>();
        while (aoi.TryDequeue(observer.ActorId, observer.ConnectionEpoch,
               observer.MapEpoch, out var item))
            output.Add(item);
        return output.ToArray();
    }

    public static void Verify(string root)
    {
        Checks.Run("trusted World registration/disconnect/transfer emits ordered lifecycle even without Move",
            () => TrustedLifecycle(root));
        Checks.Run("AOI WorldReady, Spawn/Delta/Despawn, hysteresis and map isolation",
            Interest);
        Checks.Run("AOI coalesces remote movement while preserving contiguous wire revisions and boundaries",
            RemoteMovementCheckpointRate);
        Checks.Run("same AOI tracks monster Spawn/Delta/Despawn hysteresis and resync baseline",
            MonsterInterest);
        Checks.Run("pet AOI follows owner visibility and one-tick authoritative trail with safe transfer generation",
            PetInterest);
        Checks.Run("mounted Mecha presentation follows player AOI and heals transfer generation",
            MechaInterest);
        Checks.Run("pet attack and skill visuals reach visible remote peers without duplicating owner result",
            PetAttackVisualInterest);
        Checks.Run("player attack visual reaches only viewers seeing attacker and target",
            PlayerAttackVisualInterest);
        Checks.Run("player skill visual reaches only viewers seeing attacker and target",
            PlayerSkillVisualInterest);
        Checks.Run("durable player damage and respawn visuals reach victim and visible peers only",
            PlayerDamageVisualInterest);
        Checks.Run("same map-bucket AOI tracks durable ground drop enter, exit, resync and pickup removal",
            GroundDropInterest);
        Checks.Run("ground drop committed batches fail atomically on later invalid UID and AOI backpressure",
            GroundDropBatchAtomicAdmission);
        Checks.Run("20 observers and 500 ground drops share one bounded AOI with zero idle resends",
            GroundDropDensity);
        Checks.Run("AOI self state is changed-only, generation-safe and retained on bounded overflow", OwnState);
        Checks.Run("AOI transfer clears old map; reconnect resets generation and ignores stale dequeue",
            TransfersAndReconnects);
        Checks.Run("AOI same-generation resync rebuilds WorldReady SelfState and visible Spawn baseline",
            ResyncBaseline);
        Checks.Run("AOI fails closed on stale revision, malformed actor and bounded reliable events",
            InvalidAndBounded);
        Checks.Run("30Hz Host publishes idle join and leave to AOI through WorldResultRouter",
            () => RealHostLifecycle(root).GetAwaiter().GetResult());
    }

    private static void PetInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var owner=Actor(71,15,24,24,connection:3,mapEpoch:4);
        var peer=Actor(72,15,48,24,connection:4,mapEpoch:5);
        var far=Actor(73,15,1200,24,connection:5,mapEpoch:6);
        Life(aoi,1,Join(owner),Join(peer),Join(far));
        Drain(aoi,owner);Drain(aoi,peer);Drain(aoi,far);

        aoi.ApplyPetState(new(owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            9001,5001,2,4,true));
        var ownerSpawn=Drain(aoi,owner);
        var peerSpawn=Drain(aoi,peer);
        Require(ownerSpawn is [var own]&&own.Kind==AoiEventKind.PetSpawn&&
            own.Pet?.PetUid==9001&&own.Pet?.OwnerActorId==owner.ActorId&&
            own.Pet?.EvolutionId==2&&own.Pet?.X==owner.X&&own.Pet?.Y==owner.Y&&
            peerSpawn is [var seen]&&seen.Pet==own.Pet&&Drain(aoi,far).Length==0,
            "pet spawn was not scoped to owner visibility/authoritative position");

        var movedOwner=owner with {X=40,EntityRevision=2};
        Move(aoi,2,movedOwner);
        var ownerMoveEvents=Drain(aoi,movedOwner);
        var peerMoveEvents=Drain(aoi,peer);
        Require(
            !ownerMoveEvents.Any(x=>x.Kind is AoiEventKind.PetDelta or AoiEventKind.PetSpawn or AoiEventKind.PetDespawn)&&
            !peerMoveEvents.Any(x=>x.Kind is AoiEventKind.PetDelta or AoiEventKind.PetSpawn or AoiEventKind.PetDespawn),
            "pet still mirrored the owner's same-tick position instead of trailing an accepted waypoint");

        aoi.Apply(new WorldChangedBatch(3,Array.Empty<ActorSnapshot>()));
        var ownerFollowEvents=Drain(aoi,movedOwner);
        var peerFollowEvents=Drain(aoi,peer);
        Require(
            ownerFollowEvents.Any(x=>x.Kind==AoiEventKind.PetDelta&&
                x.Pet?.PetUid==9001&&x.Pet?.X==40&&x.Pet?.Y==24)&&
            peerFollowEvents.Any(x=>x.Kind==AoiEventKind.PetDelta&&
                x.Pet?.PetUid==9001&&x.Pet?.X==40&&x.Pet?.Y==24)&&
            !ownerFollowEvents.Any(x=>x.Kind is AoiEventKind.PetSpawn or AoiEventKind.PetDespawn)&&
            !peerFollowEvents.Any(x=>x.Kind is AoiEventKind.PetSpawn or AoiEventKind.PetDespawn)&&
            Drain(aoi,far).Length==0,
            "pet did not advance one World tick later along the authoritative owner trail");
        owner=movedOwner;

        var movedPeer=peer with {X=1000,EntityRevision=2};
        Move(aoi,4,movedPeer);
        var peerExit=Drain(aoi,movedPeer);
        Require(peerExit.Any(x=>x.Kind==AoiEventKind.PetDespawn&&
                x.Pet?.PetUid==9001)&&
            Drain(aoi,owner).Any(x=>x.Kind==AoiEventKind.Despawn&&
                x.EntityActorId==peer.ActorId),
            "pet did not leave AOI when observer lost owner visibility");

        aoi.ApplyPetState(new(owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            0,0,0,0,false));
        Require(Drain(aoi,owner) is [var recall]&&
            recall.Kind==AoiEventKind.PetDespawn&&recall.Pet?.PetUid==9001&&
            aoi.PetCount==0,
            "pet recall did not reliably despawn/remove active presence");

        aoi.ApplyPetState(new(owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            9002,5002,7,7,true));
        Require(Drain(aoi,owner).Single() is {Kind:AoiEventKind.PetSpawn,Pet.EvolutionId:7},
            "replacement active pet did not spawn with evolution identity for owner");
        Life(aoi,5,Leave(owner));
        Drain(aoi,owner);
        var transferred=owner with {MapId=13,MapEpoch=owner.MapEpoch+1,EntityRevision=1};
        Life(aoi,6,Join(transferred));
        var transferEvents=Drain(aoi,transferred);
        Require(transferEvents.Any(x=>x.Kind==AoiEventKind.PetSpawn&&
                x.Pet?.PetUid==9002&&x.Pet?.OwnerMapEpoch==transferred.MapEpoch&&
                x.Pet?.X==transferred.X&&x.Pet?.Y==transferred.Y),
            "active pet did not heal owner generation/spawn position after transfer");
    }

    private static void MechaInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var owner=Actor(77,15,24,24,connection:11,mapEpoch:7);
        var peer=Actor(78,15,48,24,connection:12,mapEpoch:8);
        var far=Actor(79,15,1200,24,connection:13,mapEpoch:9);
        Life(aoi,1,Join(owner),Join(peer),Join(far));
        Drain(aoi,owner);Drain(aoi,peer);Drain(aoi,far);

        aoi.ApplyMechaState(new MechaWorldStateUpdate(
            owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            true,9201,7002,7));
        var ownerMount=Drain(aoi,owner);
        var peerMount=Drain(aoi,peer);
        Require(ownerMount is [var own]&&own.Kind==AoiEventKind.MechaState&&
            own.Mecha is {Mounted:true,MechaUid:9201,DefinitionId:7002,Level:7}&&
            peerMount is [var seen]&&seen.Mecha==own.Mecha&&Drain(aoi,far).Length==0,
            "mounted Mecha presentation was not scoped to player visibility");

        var movedPeer=peer with {X=600,EntityRevision=2};
        Move(aoi,2,movedPeer);
        var peerExit=Drain(aoi,movedPeer);
        Require(peerExit.Any(x=>x.Kind==AoiEventKind.MechaState&&
                x.Mecha is {Mounted:false,MechaUid:0})&&
            Drain(aoi,far).Length==0,
            "Mecha presentation did not clear when observer lost owner visibility");

        var returnedPeer=movedPeer with {X=48,EntityRevision=3};
        Move(aoi,3,returnedPeer);
        var peerReturn=Drain(aoi,returnedPeer);
        Require(peerReturn.Any(x=>x.Kind==AoiEventKind.MechaState&&
                x.Mecha is {Mounted:true,MechaUid:9201}),
            "Mecha presentation did not rebuild after owner re-entered AOI");

        aoi.ApplyMechaState(new MechaWorldStateUpdate(
            owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            true,9202,7003,9));
        Require(Drain(aoi,owner).Single(x=>x.Kind==AoiEventKind.MechaState) is {Mecha.MechaUid:9202}&&
            Drain(aoi,returnedPeer).Single(x=>x.Kind==AoiEventKind.MechaState) is {Mecha.MechaUid:9202},
            "atomic mounted Mecha replacement did not reach visible players");

        aoi.ApplyMechaState(new MechaWorldStateUpdate(
            owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            false,0,0,0));
        Require(Drain(aoi,owner).Single() is {Kind:AoiEventKind.MechaState,Mecha.Mounted:false}&&
            Drain(aoi,returnedPeer).Single() is {Kind:AoiEventKind.MechaState,Mecha.Mounted:false},
            "dismount did not reliably clear visible Mecha presentation");

        aoi.ApplyMechaState(new MechaWorldStateUpdate(
            owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            true,9203,7004,10));
        Drain(aoi,owner);Drain(aoi,returnedPeer);
        Life(aoi,4,Leave(owner));
        var peerLeave=Drain(aoi,returnedPeer);
        Require(peerLeave.Any(x=>x.Kind==AoiEventKind.MechaState&&
                x.Mecha is {Mounted:false})&&
            peerLeave.Any(x=>x.Kind==AoiEventKind.Despawn&&x.EntityActorId==owner.ActorId),
            "old-map Mecha presentation survived owner leave");

        var transferred=owner with {MapId=13,MapEpoch=owner.MapEpoch+1,EntityRevision=1};
        Life(aoi,5,Join(transferred));
        var transferEvents=Drain(aoi,transferred);
        Require(transferEvents.Any(x=>x.Kind==AoiEventKind.MechaState&&
                x.Mecha is {Mounted:true,MechaUid:9203}&&
                x.Mecha?.OwnerMapEpoch==transferred.MapEpoch),
            "mounted Mecha presentation did not heal owner map generation after transfer");
    }

    private static void PetAttackVisualInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var owner=Actor(74,15,24,24,connection:7,mapEpoch:4);
        var peer=Actor(75,15,48,24,connection:8,mapEpoch:5);
        var far=Actor(76,15,1200,24,connection:9,mapEpoch:6);
        var monster=new MonsterSnapshot(
            1001500001,1,0,15,40,24,40,24,100,100,
            MonsterAiState.Idle,0,1,2);
        aoi.Apply(new WorldChangedBatch(1,Array.Empty<ActorSnapshot>(),
            [Join(owner),Join(peer),Join(far)]),[monster]);
        Drain(aoi,owner);Drain(aoi,peer);Drain(aoi,far);
        aoi.ApplyPetState(new(owner.ActorId,owner.ConnectionEpoch,owner.MapEpoch,
            9101,5002,7,7,true));
        Drain(aoi,owner);Drain(aoi,peer);Drain(aoi,far);

        var request=new TrustedPetAttack(owner.ActorId,owner.ConnectionEpoch,
            owner.MapEpoch,3,9101,monster.EntityId);
        var changed=monster with {CurrentHp=75,EntityRevision=2};
        var outcome=new NormalAttackOutcome(
            NormalAttackStatus.Applied,1,monster.EntityId,25,75,100,2,changed);
        Require(aoi.EnqueuePetAttackVisual(request,outcome)==1,
            "pet attack visual did not target exactly one visible remote peer");
        Require(Drain(aoi,owner).Length==0,
            "pet owner received duplicate observer attack visual");
        var peerEvents=Drain(aoi,peer);
        Require(peerEvents is [var seen]&&seen.Kind==AoiEventKind.PetAttackVisual&&
            seen.PetAttack?.OwnerActorId==owner.ActorId&&
            seen.PetAttack?.OwnerConnectionEpoch==owner.ConnectionEpoch&&
            seen.PetAttack?.OwnerMapEpoch==owner.MapEpoch&&
            seen.PetAttack?.PetUid==9101&&seen.PetAttack?.AttackSequence==3&&
            seen.PetAttack?.Damage==25&&Drain(aoi,far).Length==0,
            "pet attack visual AOI scope/generation payload mismatch");

        var miss=new NormalAttackOutcome(
            NormalAttackStatus.Miss,1,monster.EntityId,0,75,100,2,changed);
        Require(aoi.EnqueuePetAttackVisual(request with {AttackSequence=4},miss)==1&&
            Drain(aoi,peer).Single().PetAttack?.Status==NormalAttackStatus.Miss&&
            Drain(aoi,owner).Length==0,
            "pet MISS visual was lost or duplicated to owner");

        var skillRequest=new TrustedPetSkill(owner.ActorId,owner.ConnectionEpoch,
            owner.MapEpoch,5,9101,9001,monster.EntityId);
        var skillOutcome=new SkillUseOutcome(
            SkillUseStatus.Applied,1,9001,monster.EntityId,30,70,100,2,changed);
        Require(aoi.EnqueuePetSkillVisual(skillRequest,skillOutcome)==1,
            "pet skill visual did not target exactly one visible remote peer");
        Require(Drain(aoi,owner).Length==0,
            "pet owner received duplicate observer skill visual");
        var peerSkill=Drain(aoi,peer).Single();
        Require(peerSkill.Kind==AoiEventKind.PetSkillVisual&&
            peerSkill.PetSkill?.OwnerActorId==owner.ActorId&&
            peerSkill.PetSkill?.OwnerConnectionEpoch==owner.ConnectionEpoch&&
            peerSkill.PetSkill?.OwnerMapEpoch==owner.MapEpoch&&
            peerSkill.PetSkill?.PetUid==9101&&
            peerSkill.PetSkill?.SkillId==9001&&
            peerSkill.PetSkill?.SkillSequence==5&&
            peerSkill.PetSkill?.Damage==30&&Drain(aoi,far).Length==0,
            "pet skill visual AOI scope/generation payload mismatch");
    }

    private static void PlayerAttackVisualInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var attacker=Actor(81,15,24,24,connection:3);
        var observer=Actor(82,15,48,24,connection:4);
        var far=Actor(83,15,1200,24,connection:5);
        var monster=new MonsterSnapshot(
            1001500001,1,0,15,40,24,40,24,100,100,
            MonsterAiState.Idle,0,1,2);
        aoi.Apply(new WorldChangedBatch(1,Array.Empty<ActorSnapshot>(),
            [Join(attacker),Join(observer),Join(far)]),[monster]);
        Drain(aoi,attacker);Drain(aoi,observer);Drain(aoi,far);
        var request=new TrustedNormalAttack(
            attacker.ActorId,attacker.ConnectionEpoch,attacker.MapEpoch,
            7,monster.EntityId,null);
        var changed=monster with {CurrentHp=70,EntityRevision=2};
        var outcome=new NormalAttackOutcome(
            NormalAttackStatus.Applied,1,monster.EntityId,30,70,100,2,changed);
        Require(aoi.EnqueuePlayerAttackVisual(request,outcome)==2,
            "attack visual fanout included invisible observer or omitted visible owner/peer");
        var ownerEvents=Drain(aoi,attacker);
        var peerEvents=Drain(aoi,observer);
        Require(ownerEvents is [var own] &&
            own.Kind==AoiEventKind.PlayerAttackVisual &&
            own.PlayerAttack?.AttackSequence==7 &&
            own.PlayerAttack?.Facing==attacker.Facing &&
            peerEvents is [var peer] &&
            peer.PlayerAttack==own.PlayerAttack &&
            Drain(aoi,far).Length==0,
            "attack visual AOI scope/payload mismatch");

        aoi.ApplyMonsters(1,[changed]);
        Require(Drain(aoi,attacker).Single().Kind==AoiEventKind.MonsterDelta &&
            Drain(aoi,observer).Single().Kind==AoiEventKind.MonsterDelta,
            "authoritative monster HP delta lost after visual event");
        var miss=new NormalAttackOutcome(
            NormalAttackStatus.Miss,1,monster.EntityId,0,70,100,2,changed);
        Require(aoi.EnqueuePlayerAttackVisual(
                request with {AttackSequence=8},miss)==2 &&
            Drain(aoi,attacker).Single().PlayerAttack?.Status==NormalAttackStatus.Miss,
            "MISS presentation event not admitted");
    }

    private static void PlayerSkillVisualInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var attacker=Actor(91,15,24,24,connection:3);
        var observer=Actor(92,15,48,24,connection:4);
        var far=Actor(93,15,1200,24,connection:5);
        var monster=new MonsterSnapshot(
            1001500001,1,0,15,40,24,40,24,100,100,
            MonsterAiState.Idle,0,1,2);
        var secondMonster=new MonsterSnapshot(
            1001500002,1,1,15,64,24,64,24,100,100,
            MonsterAiState.Idle,0,1,2);
        aoi.Apply(new WorldChangedBatch(1,Array.Empty<ActorSnapshot>(),
            [Join(attacker),Join(observer),Join(far)]),[monster,secondMonster]);
        Drain(aoi,attacker);Drain(aoi,observer);Drain(aoi,far);
        var request=new TrustedSkillUse(
            attacker.ActorId,attacker.ConnectionEpoch,attacker.MapEpoch,
            3,99001,monster.EntityId,null);
        var changed=monster with {CurrentHp=60,EntityRevision=2};
        var outcome=new SkillUseOutcome(
            SkillUseStatus.Applied,1,99001,monster.EntityId,
            40,60,100,2,changed);
        Require(aoi.EnqueuePlayerSkillVisual(request,outcome)==2,
            "skill visual fanout included invisible observer or omitted owner/peer");
        var owner=Drain(aoi,attacker);
        var peer=Drain(aoi,observer);
        Require(owner is [var own] &&
            own.Kind==AoiEventKind.PlayerSkillVisual &&
            own.PlayerSkill?.SkillId==99001 &&
            own.PlayerSkill?.Damage==40 &&
            own.PlayerSkill?.Facing==attacker.Facing &&
            peer is [var seen] && seen.PlayerSkill==own.PlayerSkill &&
            Drain(aoi,far).Length==0,
            "skill visual AOI scope/payload mismatch");

        var areaRequest=request with {SkillSequence=4,TargetEntityId=0};
        var changedSecond=secondMonster with {CurrentHp=75,EntityRevision=2};
        var areaOutcome=new SkillUseOutcome(
            SkillUseStatus.Applied,1,99001,0,0,0,0,0,null,
            [new(changed,40),new(changedSecond,25)]);
        Require(aoi.EnqueuePlayerSkillVisual(areaRequest,areaOutcome)==4,
            "area skill did not fan out two target hits to owner and peer");
        owner=Drain(aoi,attacker);
        peer=Drain(aoi,observer);
        Require(owner.Length==2 && peer.Length==2 &&
            owner.Select(x=>x.PlayerSkill?.TargetEntityId).SequenceEqual(
                new long?[]{monster.EntityId,secondMonster.EntityId}) &&
            owner.Select(x=>x.PlayerSkill?.Damage).SequenceEqual(
                new int?[]{40,25}) &&
            peer.Select(x=>x.PlayerSkill).SequenceEqual(
                owner.Select(x=>x.PlayerSkill)) &&
            Drain(aoi,far).Length==0,
            "area skill per-target visual payload/order mismatch");

        // A server-authoritative single-target MISS carries zero damage.
        // Fan-out must preserve MISS rather than throwing and killing the
        // AOI pump (or silently advertising an Applied hit).
        var missMonster=monster with { EntityRevision=3, CurrentHp=100 };
        var missOutcome=new SkillUseOutcome(
            SkillUseStatus.Miss,1,99001,monster.EntityId,
            0,100,100,3,missMonster);
        Require(aoi.EnqueuePlayerSkillVisual(
                request with {SkillSequence=5},missOutcome)==2,
            "single-target skill MISS rejected by AOI");
        owner=Drain(aoi,attacker);
        peer=Drain(aoi,observer);
        Require(owner is [var missOwner] &&
            missOwner.Kind==AoiEventKind.PlayerSkillVisual &&
            missOwner.PlayerSkill?.Status==SkillUseStatus.Miss &&
            missOwner.PlayerSkill?.Damage==0 &&
            missOwner.PlayerSkill?.TargetEntityId==monster.EntityId &&
            peer is [var missPeer] &&
            missPeer.PlayerSkill==missOwner.PlayerSkill &&
            Drain(aoi,far).Length==0,
            "skill MISS not faithfully scoped/fanned out as zero damage");

        bool rejectedInvalidMiss=false;
        try
        {
            aoi.EnqueuePlayerSkillVisual(
                request with {SkillSequence=6},
                missOutcome with {Damage=1});
        }
        catch(InvalidOperationException){rejectedInvalidMiss=true;}
        Require(rejectedInvalidMiss&&Drain(aoi,attacker).Length==0,
            "malformed nonzero-damage skill MISS was admitted");
    }

    private static void PlayerDamageVisualInterest()
    {
        var aoi=new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200,EnterHalfHeight:150,ExitMargin:60,BucketSize:64));
        var victim=Actor(91,15,24,24,connection:7);
        var observer=Actor(92,15,48,24,connection:8);
        var far=Actor(93,15,1200,24,connection:9);
        aoi.Apply(new WorldChangedBatch(1,Array.Empty<ActorSnapshot>(),
            [Join(victim),Join(observer),Join(far)]));
        Drain(aoi,victim);Drain(aoi,observer);Drain(aoi,far);

        var damage=new PlayerDamageSettlement(
            victim.ActorId,victim.ConnectionEpoch,victim.MapEpoch,
            1,1,1001500001,3,18,82,100,false);
        Require(aoi.EnqueuePlayerDamageVisual(damage)==2,
            "damage visual fanout included invisible observer or omitted victim/peer");
        var owner=Drain(aoi,victim);
        var peer=Drain(aoi,observer);
        Require(owner is [var own] &&
            own.Kind==AoiEventKind.PlayerDamageVisual &&
            own.PlayerDamage?.VictimActorId==victim.ActorId &&
            own.PlayerDamage?.DamageSequence==1 &&
            own.PlayerDamage?.Damage==18 &&
            own.PlayerDamage?.Status==NormalAttackStatus.Applied &&
            own.PlayerDamage?.Facing==victim.Facing &&
            peer is [var remote] &&
            remote.PlayerDamage==own.PlayerDamage &&
            Drain(aoi,far).Length==0,
            "damage visual AOI scope/payload mismatch");

        var miss=damage with {
            DamageSequence=0,VisualSequence=2,ServerTick=1,
            AttackerMonsterRevision=4,Damage=0,CurrentHp=82,Died=false,
            Status=NormalAttackStatus.Miss
        };
        Require(aoi.EnqueuePlayerDamageVisual(miss)==2 &&
            Drain(aoi,victim).Single().PlayerDamage is {
                DamageSequence:2,Damage:0,Status:NormalAttackStatus.Miss } &&
            Drain(aoi,observer).Single().PlayerDamage is {
                DamageSequence:2,Damage:0,Status:NormalAttackStatus.Miss } &&
            Drain(aoi,far).Length==0,
            "MISS visual did not fan out without HP mutation");

        var lethal=damage with {
            DamageSequence=2,VisualSequence=3,ServerTick=1,
            AttackerMonsterRevision=5,Damage=82,CurrentHp=0,Died=true
        };
        Require(aoi.EnqueuePlayerDamageVisual(lethal)==2 &&
            Drain(aoi,victim).Single().PlayerDamage?.Died==true &&
            Drain(aoi,observer).Single().PlayerDamage is { Died:true, Damage:82 },
            "lethal visual did not reach victim and peer");

        Require(aoi.EnqueuePlayerRespawnVisual(victim,1)==2,
            "respawn visual fanout omitted visible generation");
        owner=Drain(aoi,victim);
        peer=Drain(aoi,observer);
        Require(owner is [var ownRespawn] &&
            ownRespawn.Kind==AoiEventKind.PlayerRespawnVisual &&
            ownRespawn.PlayerRespawn?.ActorId==victim.ActorId &&
            ownRespawn.PlayerRespawn?.RespawnSequence==1 &&
            peer is [var peerRespawn] &&
            peerRespawn.PlayerRespawn==ownRespawn.PlayerRespawn &&
            Drain(aoi,far).Length==0,
            "respawn visual AOI scope/payload mismatch");

        var staleGeneration=damage with { ConnectionEpoch=victim.ConnectionEpoch+1 };
        Require(aoi.EnqueuePlayerDamageVisual(staleGeneration)==0 &&
            Drain(aoi,victim).Length==0 && Drain(aoi,observer).Length==0,
            "stale victim generation emitted damage visual");
    }

    private static void GroundDropInterest()
    {
        var aoi=new WorldAoi();
        GroundDropRecord D(long id,int map,double x,double y)=>new(
            id,map,x,y,1001300001,4294967301,0,2701,2,
            LootSourceTrust.Verified,"ci:trusted-source",null);
        var map15=D(501,15,48,24);
        var map13=D(502,13,64,24);
        aoi.SeedGroundDrops([map15,map13]);
        Require(aoi.GroundDropCount==2,"startup drops not indexed");
        var a=Actor(101,15,24,24);
        var b=Actor(102,13,24,24);
        Life(aoi,1,Join(a),Join(b));
        var joined=Drain(aoi,a);
        Require(joined.Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==1 &&
            joined.Any(x=>x.GroundDrop?.DropId==501) &&
            !joined.Any(x=>x.GroundDrop?.DropId==502) &&
            Drain(aoi,b).Single(x=>x.Kind==AoiEventKind.GroundDropSpawn)
                .GroundDrop?.DropId==502,
            "ground drop bootstrap leaked cross-map or lost WorldReady baseline");

        var newDrop=D(503,15,56,24);
        aoi.ApplyGroundDrops(1,[newDrop]);
        Require(Drain(aoi,a).Single().GroundDrop?.DropId==503 &&
            Drain(aoi,b).Length==0,
            "committed drop notification was not AOI scoped");
        aoi.ApplyGroundDrops(1,[newDrop]);
        Require(Drain(aoi,a).Length==0,"same committed drop re-emitted");
        var moved=a with {X=1000,EntityRevision=2,IsMoving=true,MovementActionSequence=1};
        Move(aoi,2,moved);
        var outOfRange=Drain(aoi,moved);
        Require(outOfRange.Count(x=>x.Kind==AoiEventKind.GroundDropDespawn)==2,
            "leaving visibility retained out-of-range ground drops");
        var back=moved with {X=24,EntityRevision=3,MovementActionSequence=2};
        Move(aoi,3,back);
        Require(Drain(aoi,back).Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==2,
            "re-entering visibility failed to restore ground drops");

        aoi.RemoveGroundDrop(3,501);
        Require(Drain(aoi,back).Single(x=>x.Kind==AoiEventKind.GroundDropDespawn)
            .GroundDropId==501 && aoi.GroundDropCount==2,
            "committed pickup did not remove ground UID");
        Require(aoi.TryResync(new(back.ActorId,back.ConnectionEpoch,back.MapEpoch)),
            "ground baseline resync rejected valid viewer");
        var rebuilt=Drain(aoi,back);
        Require(rebuilt.Any(x=>x.Kind==AoiEventKind.WorldReady) &&
            rebuilt.Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==1 &&
            rebuilt.Single(x=>x.Kind==AoiEventKind.GroundDropSpawn)
                .GroundDrop?.DropId==503,
            "resync reintroduced picked UID or omitted active UID");
        Life(aoi,4,Leave(back));
        var newMap=back with {MapId=13,MapEpoch=2,EntityRevision=1};
        Life(aoi,5,Join(newMap));
        var changedMap=Drain(aoi,newMap);
        Require(changedMap.Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==1 &&
            changedMap.Single(x=>x.Kind==AoiEventKind.GroundDropSpawn)
                .GroundDrop?.DropId==502,
            "map transfer retained wrong map ground UID");
        bool late=false;
        try{aoi.SeedGroundDrops([D(504,15,56,24)]);}
        catch(InvalidOperationException){late=true;}
        Require(late,"late bootstrap mutated live AOI");
    }

    private static void GroundDropBatchAtomicAdmission()
    {
        GroundDropRecord D(long uid) => new(
            uid,15,40+(uid-901)*8,24,1001500001,4294967301,0,2701,1,
            LootSourceTrust.Verified,"ci:atomic-drop-batch",null);
        var first=D(901);
        var second=D(902);
        var third=D(903);
        var viewer=Actor(601,15,24,24);

        // Failures on the last item cannot publish the first item.
        var aoi=new WorldAoi(new WorldAoiOptions(
            MaxEntities:3,MaxVisiblePerObserver:2,MaxPendingEventsPerObserver:3));
        Life(aoi,1,Join(viewer));
        Drain(aoi,viewer);
        void Refused(Action action, string reason)
        {
            bool blocked=false;
            try { action(); }
            catch(InvalidOperationException) { blocked=true; }
            catch(AoiBackpressureException) { blocked=true; }
            Require(blocked && aoi.GroundDropCount==0 &&
                    aoi.PendingEvents(viewer.ActorId)==0,
                reason);
        }
        Refused(() => aoi.ApplyGroundDrops(1,[first,second,third]),
            "global entity cap partially published a committed drop batch");
        Refused(() => aoi.ApplyGroundDrops(1,[first,second with {Quantity=0}]),
            "invalid late record partially published a committed drop batch");
        Refused(() => aoi.ApplyGroundDrops(1,[first,first with {Quantity=2}]),
            "conflicting UID inside one batch partially published a drop");
        aoi.ApplyGroundDrops(1,[first,second]);
        var emitted=Drain(aoi,viewer);
        Require(aoi.GroundDropCount==2 &&
                emitted.Length==2 &&
                emitted.Select(e=>e.GroundDrop?.DropId)
                    .SequenceEqual(new long?[]{901,902}),
            "valid two-drop batch did not publish both UIDs in order");
        aoi.ApplyGroundDrops(1,[first,second]);
        Require(Drain(aoi,viewer).Length==0,
            "identical committed batch rebroadcast immutable UIDs");

        var visibleCap=new WorldAoi(new WorldAoiOptions(
            MaxVisiblePerObserver:1));
        Life(visibleCap,1,Join(viewer));
        Drain(visibleCap,viewer);
        bool visibleRejected=false;
        try { visibleCap.ApplyGroundDrops(1,[first,second]); }
        catch(AoiBackpressureException) { visibleRejected=true; }
        Require(visibleRejected && visibleCap.GroundDropCount==0 &&
                visibleCap.PendingEvents(viewer.ActorId)==0,
            "observer visible cap partially published a committed drop batch");

        var fifoCap=new WorldAoi(new WorldAoiOptions(
            MaxPendingEventsPerObserver:1));
        Life(fifoCap,1,Join(viewer));
        Drain(fifoCap,viewer);
        bool pendingRejected=false;
        try { fifoCap.ApplyGroundDrops(1,[first,second]); }
        catch(AoiBackpressureException) { pendingRejected=true; }
        Require(pendingRejected && fifoCap.GroundDropCount==0 &&
                fifoCap.PendingEvents(viewer.ActorId)==0,
            "observer reliable FIFO cap partially published a committed drop batch");

        var bootstrap=new WorldAoi(new WorldAoiOptions(MaxEntities:1));
        bool bootstrapRejected=false;
        try { bootstrap.SeedGroundDrops([first,second]); }
        catch(AoiBackpressureException) { bootstrapRejected=true; }
        Require(bootstrapRejected && bootstrap.GroundDropCount==0,
            "invalid startup batch left a partial persistent ground-drop index");
        bootstrap.SeedGroundDrops([first]);
        Require(bootstrap.GroundDropCount==1,
            "valid startup drop rejected after prior fail-closed batch");
    }

    private static void GroundDropDensity()
    {
        var aoi=new WorldAoi();
        var records=Enumerable.Range(0,500).Select(index=>
            new GroundDropRecord(1000L+index,13,
                40+(index%25)*16,40+(index/25)*16,
                1001300001,4294967301,index%32,2701,1,
                LootSourceTrust.Verified,"ci:500-ground",null)).ToArray();
        aoi.SeedGroundDrops(records);
        var viewers=Enumerable.Range(0,20).Select(i=>Actor(
            10000+i,13,24,24)).ToArray();
        Life(aoi,1,viewers.Select(Join).ToArray());
        foreach(var viewer in viewers)
        {
            var events=Drain(aoi,viewer);
            Require(events.Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==500 &&
                events.Count(x=>x.Kind==AoiEventKind.WorldReady)==1 &&
                events.Count(x=>x.Kind==AoiEventKind.GroundDropDespawn)==0,
                "500-ground initial AOI fanout missed/duplicated items");
        }
        Require(aoi.TrackedEntityCount==520 && aoi.GroundDropCount==500,
            "500-ground shared AOI entity count mismatch");
        aoi.Apply(new WorldChangedBatch(2,Array.Empty<ActorSnapshot>()));
        Require(viewers.All(x=>aoi.PendingEvents(x.ActorId)==0),
            "idle tick needlessly rebroadcast immutable ground objects");
        aoi.RemoveGroundDrop(2,records[0].DropId);
        foreach(var viewer in viewers)
        {
            var events=Drain(aoi,viewer);
            Require(events.Length==1 &&
                events[0].Kind==AoiEventKind.GroundDropDespawn &&
                events[0].GroundDropId==records[0].DropId,
                "single committed pickup did not fanout exactly once to all observers");
        }
        aoi.Apply(new WorldChangedBatch(3,Array.Empty<ActorSnapshot>()));
        Require(viewers.All(x=>aoi.PendingEvents(x.ActorId)==0),
            "idle AOI resent old ground removal");
        Require(aoi.TryResync(new(viewers[0].ActorId,
            viewers[0].ConnectionEpoch,viewers[0].MapEpoch)),
            "500-ground re-sync rejected");
        var baseline=Drain(aoi,viewers[0]);
        Require(baseline.Count(x=>x.Kind==AoiEventKind.GroundDropSpawn)==499 &&
            baseline.Count(x=>x.Kind==AoiEventKind.WorldReady)==1,
            "resync failed to rebuild all 499 surviving ground UIDs");
    }

    private static void OwnState()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(MaxPendingEventsPerObserver: 1));
        var a = Actor(71, 15, 24, 8);
        Life(aoi, 1, Join(a));
        Drain(aoi, a);
        Move(aoi, 2, a); // replayed revision creates no extra owner traffic
        Require(aoi.PendingEvents(a.ActorId) == 0, "idle self generated traffic");
        var moving = a with { X = 28, EntityRevision = 2, IsMoving = true, MovementActionSequence = 1 };
        Move(aoi, 3, moving);
        Require(aoi.TryPeek(a.ActorId, a.ConnectionEpoch, a.MapEpoch, out var retained) &&
                retained.Kind == AoiEventKind.SelfState && retained.Entity == moving,
                "own changed revision not retained");
        bool overflow = false;
        try { Move(aoi, 4, moving with { X = 32, EntityRevision = 3 }); }
        catch (AoiBackpressureException) { overflow = true; }
        Require(overflow && aoi.TryPeek(a.ActorId, a.ConnectionEpoch, a.MapEpoch, out var after) &&
                after == retained, "self overflow silently discarded reliable event");
        Require(!aoi.TryConfirmQueued(a.ActorId, a.ConnectionEpoch + 1, a.MapEpoch),
                "wrong connection consumed own event");
    }

    private static void TrustedLifecycle(string root)
    {
        string dirA = Checks.Fixture(root, 101, 8, 8, (_, _) => 0);
        string dirB = Checks.Fixture(root, 102, 8, 8, (_, _) => 0);
        var world = new ServerWorld(new[] { BlockingGrid.Load(dirA, 101),
                                            BlockingGrid.Load(dirB, 102) });
        Require(world.TryTrustedRegister(11, 101, 24, 24, out var a), "first register");
        Require(world.TryTrustedRegister(22, 101, 40, 24, out var b), "second register");
        var first = world.Tick();
        Require(first.Changed.Count == 0 && first.Receipts.Count == 0 &&
                first.Lifecycle is { Count: 2 } &&
                first.Lifecycle[0].Kind == ActorLifecycleKind.Joined &&
                first.Lifecycle[0].Actor.ActorId == 11 &&
                first.Lifecycle[1].Actor.ActorId == 22,
                "idle joins not retained or wrong order");

        Require(world.TryTrustedDisconnect(11, a.ConnectionEpoch, a.MapEpoch),
                "trusted player leave");
        Require(world.TryTrustedTransfer(22, b.ConnectionEpoch, b.MapEpoch, 102, 24, 40,
                out var moved), "trusted transfer");
        var second = world.Tick();
        Require(second.Lifecycle is { Count: 3 } &&
                second.Lifecycle[0].Kind == ActorLifecycleKind.Left &&
                second.Lifecycle[0].Actor.ActorId == 11 &&
                second.Lifecycle[1].Kind == ActorLifecycleKind.Left &&
                second.Lifecycle[1].Actor.MapId == 101 &&
                second.Lifecycle[2].Kind == ActorLifecycleKind.Joined &&
                second.Lifecycle[2].Actor.MapId == 102 &&
                moved.MapEpoch > b.MapEpoch, "disconnect and transfer ordering");
        Require(!world.TryTrustedDisconnect(22, b.ConnectionEpoch, b.MapEpoch),
                "stale map disconnect unexpectedly succeeded");
        Require(world.Tick().Lifecycle is { Count: 0 },
                "lifecycle emitted repeatedly across idle ticks");
    }

    private static void Interest()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(1, 15, 100, 100);
        var b = Actor(2, 15, 140, 100);
        var c = Actor(3, 15, 1200, 100);
        var otherMap = Actor(4, 13, 140, 100);
        Life(aoi, 1, Join(a));
        Require(Drain(aoi, a) is [var first] &&
                first.Kind == AoiEventKind.WorldReady && first.ReceiverMapEpoch == 1,
                "WorldReady must precede visible spawns");

        Life(aoi, 2, Join(b));
        var aEvents = Drain(aoi, a);
        var bEvents = Drain(aoi, b);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Spawn &&
                aEvents[0].Entity?.ActorId == 2, "A did not see B spawn");
        Require(bEvents.Length == 2 && bEvents[0].Kind == AoiEventKind.WorldReady &&
                bEvents[1].Kind == AoiEventKind.Spawn &&
                bEvents[1].Entity?.ActorId == 1, "new player WorldReady ordering");

        Life(aoi, 3, Join(c));
        Life(aoi, 4, Join(otherMap));
        Require(Drain(aoi, a).Length == 0 && Drain(aoi, b).Length == 0 &&
                Drain(aoi, c).Length == 1 && Drain(aoi, otherMap).Length == 1,
                "far or different map player leaked into AOI");

        var bEdge = b with { X = 350, EntityRevision = 2 };
        Move(aoi, 5, bEdge); // diff=250, stays visible through exit hysteresis=260
        aEvents = Drain(aoi, a);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Delta &&
                aEvents[0].Entity?.EntityRevision == 2,
                "hysteresis should retain and delta visible B");
        Require(Drain(aoi, bEdge) is [var own] && own.Kind == AoiEventKind.SelfState &&
                own.Entity == bEdge, "moving observer did not receive its own authoritative state");

        var bOutside = b with { X = 370, EntityRevision = 3 };
        Move(aoi, 6, bOutside); // diff=270 exits
        aEvents = Drain(aoi, a);
        bEvents = Drain(aoi, bOutside);
        Require(aEvents.Length == 1 && aEvents[0].Kind == AoiEventKind.Despawn &&
                aEvents[0].EntityActorId == 2 &&
                bEvents.Length == 2 && bEvents[0].Kind == AoiEventKind.SelfState &&
                bEvents[1].Kind == AoiEventKind.Despawn && bEvents[1].EntityActorId == 1,
                "crossing hysteresis exit should despawn in both directions");

        var bNear = b with { X = 250, EntityRevision = 4 };
        Move(aoi, 7, bNear);
        Require(Drain(aoi, a) is [var aSpawn] &&
                aSpawn.Kind == AoiEventKind.Spawn && aSpawn.Entity?.ActorId == 2 &&
                Drain(aoi, bNear) is [{ Kind: AoiEventKind.SelfState }, var bSpawn] &&
                bSpawn.Kind == AoiEventKind.Spawn && bSpawn.Entity?.ActorId == 1,
                "re-entering view should respawn, not delta without an entity");

        Require(aoi.EntityCount == 4 && aoi.ObserverCount == 4 &&
                aoi.LastTick == 7, "AOI world counters");
    }

    private static void RemoteMovementCheckpointRate()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:640, EnterHalfHeight:400, ExitMargin:96, BucketSize:64,
            RemotePlayerDeltaIntervalTicks:3, RemotePlayerDeltaDistance:24));
        var observer = Actor(41,15,100,100);
        var remote = Actor(42,15,140,100);
        Life(aoi,1,Join(observer),Join(remote));
        Drain(aoi,observer);
        Drain(aoi,remote);

        var wireRevisions = new List<long>();
        var movementIntents = new List<AoiEvent>();
        var moving = remote;
        for(int index=0; index<30; index++)
        {
            moving = moving with
            {
                X=148+index*8,
                EntityRevision=2+index,
                IsMoving=true,
                MovementActionSequence=1,
                MovementTargetX=900,
                MovementTargetY=100
            };
            Move(aoi,2+index,moving);
            var observerEvents=Drain(aoi,observer);
            wireRevisions.AddRange(observerEvents
                .Where(x=>x.Kind==AoiEventKind.Delta)
                .Select(x=>x.Entity?.EntityRevision ?? 0));
            movementIntents.AddRange(observerEvents
                .Where(x=>x.Kind==AoiEventKind.PlayerMovementIntent));
            var own=Drain(aoi,moving);
            Require(own is [var self] && self.Kind==AoiEventKind.SelfState &&
                    self.Entity?.EntityRevision==moving.EntityRevision,
                "remote coalescing changed owner authoritative SelfState cadence/revision");
        }
        Require(wireRevisions.SequenceEqual(Enumerable.Range(2,10).Select(x=>(long)x)),
            "30 source movement revisions did not coalesce to 10 contiguous remote wire revisions");
        Require(movementIntents is [var startIntent] &&
                startIntent.Entity is { } startMovement &&
                startMovement.MovementActionSequence==1 &&
                startMovement.MovementTargetX==900 &&
                startMovement.MovementTargetY==100 &&
                startMovement.IsMoving,
            "movement start intent was missing, duplicated or lost its authoritative target");

        // Deterministic A/B on the identical source trace. Interval=1 is the
        // previous every-World-revision behavior; the production default is 3.
        var baseline = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:640, EnterHalfHeight:400, ExitMargin:96, BucketSize:64,
            RemotePlayerDeltaIntervalTicks:1, RemotePlayerDeltaDistance:24));
        var baselineObserver=Actor(51,15,100,100);
        var baselineRemote=Actor(52,15,140,100);
        Life(baseline,1,Join(baselineObserver),Join(baselineRemote));
        Drain(baseline,baselineObserver); Drain(baseline,baselineRemote);
        int baselineDeltas=0;
        for(int index=0; index<30; index++)
        {
            baselineRemote=baselineRemote with
            {
                X=148+index*8,
                EntityRevision=2+index,
                IsMoving=true,
                MovementActionSequence=1
            };
            Move(baseline,2+index,baselineRemote);
            baselineDeltas+=Drain(baseline,baselineObserver)
                .Count(x=>x.Kind==AoiEventKind.Delta);
            Drain(baseline,baselineRemote);
        }
        Require(baselineDeltas==30 && wireRevisions.Count==10,
            "remote movement A/B gate did not prove 30-to-10 checkpoint reduction");
        Console.WriteLine(
            "SERVER_REMOTE_MOVE_CHECKPOINT_AB before_deltas=30 after_deltas=10 reduction_pct=66.7");

        moving=moving with {X=moving.X+4,EntityRevision=32,Facing=7};
        Move(aoi,32,moving);
        Require(Drain(aoi,observer) is [var turn] &&
                turn.Kind==AoiEventKind.Delta && turn.Entity?.EntityRevision==12 &&
                turn.Entity?.Facing==7,
            "facing boundary was delayed or broke contiguous wire revision");
        Drain(aoi,moving);

        moving=moving with
        {
            X=moving.X+4,
            EntityRevision=33,
            MovementActionSequence=2,
            MovementTargetX=1000,
            MovementTargetY=180
        };
        Move(aoi,33,moving);
        Require(Drain(aoi,observer) is [var retarget] &&
                retarget.Kind==AoiEventKind.PlayerMovementIntent &&
                retarget.Entity is { } retargeted &&
                retargeted.MovementActionSequence==2 &&
                retargeted.MovementTargetX==1000 &&
                retargeted.MovementTargetY==180,
            "retarget did not emit exactly one intent without an unnecessary Delta");
        Drain(aoi,moving);

        var stopped=moving with
        {
            X=moving.X+4,EntityRevision=34,IsMoving=false,
            MovementTargetX=moving.X+4,MovementTargetY=moving.Y
        };
        Move(aoi,34,stopped);
        Require(Drain(aoi,observer) is [var stop, var stopIntent] &&
                stop.Kind==AoiEventKind.Delta && stop.Entity?.EntityRevision==13 &&
                stop.Entity?.MovementActionSequence==2 && stop.Entity?.IsMoving==false &&
                stopIntent.Kind==AoiEventKind.PlayerMovementIntent &&
                stopIntent.Entity is { } stoppedIntent &&
                stoppedIntent.MovementActionSequence==2 && !stoppedIntent.IsMoving &&
                stoppedIntent.MovementTargetX==stopped.X &&
                stoppedIntent.MovementTargetY==stopped.Y,
            "stop boundary did not flush contiguous checkpoint plus stop intent");
        Drain(aoi,stopped);

        var corrected=stopped with {X=stopped.X+2,EntityRevision=35};
        Move(aoi,35,corrected);
        Require(Drain(aoi,observer) is [var correction] &&
                correction.Kind==AoiEventKind.Delta &&
                correction.Entity?.EntityRevision==14 &&
                correction.Entity?.X==corrected.X,
            "idle authoritative correction was incorrectly coalesced");
        Drain(aoi,corrected);

        Require(aoi.TryResync(new(observer.ActorId,observer.ConnectionEpoch,observer.MapEpoch)),
            "checkpoint observer resync rejected");
        var rebuilt=Drain(aoi,observer);
        Require(rebuilt.Any(x=>x.Kind==AoiEventKind.Spawn &&
                    x.Entity?.ActorId==remote.ActorId &&
                    x.Entity?.EntityRevision==corrected.EntityRevision),
            "resync did not restore latest source revision baseline");
    }

    private static MonsterSnapshot Monster(long id, int map, double x, double y,
        long revision = 1, int hp = 100, MonsterAiState state = MonsterAiState.Idle) =>
        new(id, 1, 0, map, x, y, 140, 100, hp, 100, state, 0, revision, 1);

    private static void MonsterInterest()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth:200, EnterHalfHeight:150, ExitMargin:60, BucketSize:64));
        var viewer = Actor(51,15,100,100);
        Life(aoi,1,Join(viewer));
        Require(Drain(aoi,viewer) is [{ Kind:AoiEventKind.WorldReady }],
            "monster fixture Ready");

        var monster = Monster(1001500001,15,140,100);
        aoi.ApplyMonsters(1,[monster]);
        Require(Drain(aoi,viewer) is [var spawn] &&
                spawn.Kind == AoiEventKind.MonsterSpawn &&
                spawn.Monster?.EntityId == monster.EntityId,
                "near monster did not Spawn through shared AOI");

        aoi.Apply(new WorldChangedBatch(2,Array.Empty<ActorSnapshot>()));
        var edge = monster with { X=350, EntityRevision=2, State=MonsterAiState.Chase,
                                  TargetActorId=viewer.ActorId };
        aoi.ApplyMonsters(2,[edge]);
        Require(Drain(aoi,viewer) is [var delta] &&
                delta.Kind == AoiEventKind.MonsterDelta &&
                delta.Monster?.EntityRevision == 2,
                "monster exit hysteresis did not retain Delta");

        aoi.Apply(new WorldChangedBatch(3,Array.Empty<ActorSnapshot>()));
        var outside = edge with { X=370, EntityRevision=3 };
        aoi.ApplyMonsters(3,[outside]);
        Require(Drain(aoi,viewer) is [var gone] &&
                gone.Kind == AoiEventKind.MonsterDespawn &&
                gone.MonsterEntityId == monster.EntityId &&
                gone.MonsterRevision == 3,
                "monster crossing shared AOI exit did not Despawn");

        aoi.Apply(new WorldChangedBatch(4,Array.Empty<ActorSnapshot>()));
        var near = outside with { X=250, EntityRevision=4, CurrentHp=80 };
        aoi.ApplyMonsters(4,[near]);
        Require(Drain(aoi,viewer) is [var reenter] &&
                reenter.Kind == AoiEventKind.MonsterSpawn &&
                reenter.Monster?.EntityRevision == 4,
                "monster re-entry did not rebuild Spawn baseline");

        Require(aoi.TryResync(new TrustedWorldResyncRequest(
                    viewer.ActorId,viewer.ConnectionEpoch,viewer.MapEpoch)),
                "monster AOI resync rejected");
        var rebuilt=Drain(aoi,viewer);
        Require(rebuilt.Length==3 &&
                rebuilt[0].Kind==AoiEventKind.WorldReady &&
                rebuilt[1].Kind==AoiEventKind.SelfState &&
                rebuilt[2].Kind==AoiEventKind.MonsterSpawn &&
                rebuilt[2].Monster?.EntityRevision==4,
                "monster missing from same AOI resync baseline");
        Require(aoi.MonsterCount==1 && aoi.TrackedEntityCount==2,
                "shared AOI monster counters");
    }

    private static void TransfersAndReconnects()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(11, 15, 100, 100);
        var b = Actor(22, 15, 120, 100);
        var c = Actor(33, 13, 100, 100);
        Life(aoi, 1, Join(a), Join(b), Join(c));
        Drain(aoi, a); Drain(aoi, b); Drain(aoi, c);

        var transferred = b with { MapId = 13, MapEpoch = 2, EntityRevision = 2,
                                   X = 120, Y = 100 };
        Life(aoi, 2, Leave(b), Join(transferred));
        Require(Drain(aoi, a) is [var gone] &&
                gone.Kind == AoiEventKind.Despawn &&
                gone.EntityActorId == 22 &&
                gone.EntityConnectionEpoch == 1, "old map did not despawn transfer");
        Require(Drain(aoi, c) is [var entered] &&
                entered.Kind == AoiEventKind.Spawn &&
                entered.Entity?.ActorId == 22, "new map observer missed transfer");
        var bEvents = Drain(aoi, transferred);
        Require(bEvents.Length == 2 &&
                bEvents[0].Kind == AoiEventKind.WorldReady &&
                bEvents[0].ReceiverMapEpoch == 2 &&
                bEvents[1].Kind == AoiEventKind.Spawn &&
                bEvents[1].Entity?.ActorId == 33, "transfer requires new WorldReady then snapshot");
        Require(!aoi.TryDequeue(22, 1, 1, out _), "old MapEpoch dequeued new map events");

        Life(aoi, 3, Leave(transferred));
        Require(Drain(aoi, c) is [var removed] &&
                removed.Kind == AoiEventKind.Despawn &&
                removed.EntityActorId == 22, "disconnect ghost actor");

        var reconnected = b with { ConnectionEpoch = 2, MapEpoch = 3,
                                   EntityRevision = 1 };
        Life(aoi, 4, Join(reconnected));
        Require(!aoi.TryDequeue(22, 1, 1, out _) &&
                Drain(aoi, reconnected).Any(e => e.Kind == AoiEventKind.WorldReady),
                "reconnect must not reuse old queue");
        Require(Drain(aoi, a) is [var reSpawn] &&
                reSpawn.Kind == AoiEventKind.Spawn &&
                reSpawn.Entity?.ConnectionEpoch == 2,
                "reconnect did not create new entity generation");
    }

    private static void ResyncBaseline()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 150, ExitMargin: 60, BucketSize: 64));
        var a = Actor(81, 15, 100, 100);
        var b = Actor(82, 15, 120, 100);
        Life(aoi, 1, Join(a), Join(b));
        Drain(aoi,a); Drain(aoi,b);
        var moved = b with { X = 136, EntityRevision = 2, IsMoving = true,
                             MovementActionSequence = 1,
                             MovementTargetX = 320, MovementTargetY = 100 };
        Move(aoi,2,moved);
        Require(aoi.PendingEvents(a.ActorId) == 2,
            "resync fixture missing stale pending delta plus movement intent");
        Require(aoi.TryResync(new TrustedWorldResyncRequest(
                    a.ActorId,a.ConnectionEpoch,a.MapEpoch)),
                "current observer resync rejected");
        var rebuilt = Drain(aoi,a);
        Require(rebuilt.Length == 4 &&
                rebuilt[0].Kind == AoiEventKind.WorldReady &&
                rebuilt[1].Kind == AoiEventKind.SelfState &&
                rebuilt[1].Entity?.ActorId == a.ActorId &&
                rebuilt[2].Kind == AoiEventKind.Spawn &&
                rebuilt[2].Entity?.ActorId == b.ActorId &&
                rebuilt[2].Entity?.EntityRevision == 2 &&
                rebuilt[3].Kind == AoiEventKind.PlayerMovementIntent &&
                rebuilt[3].Entity?.MovementActionSequence == 1 &&
                rebuilt[3].Entity?.MovementTargetX == 320,
                "resync baseline order/content wrong");
        Require(!aoi.TryResync(new TrustedWorldResyncRequest(
                    a.ActorId,a.ConnectionEpoch+1,a.MapEpoch)),
                "stale connection resync accepted");
    }

    private static void InvalidAndBounded()
    {
        var aoi = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 200, BucketSize: 64));
        var a = Actor(1, 15, 100, 100);
        Life(aoi, 1, Join(a));
        Drain(aoi, a);
        var newer = a with { EntityRevision = 3, X = 110 };
        Move(aoi, 2, newer);
        Move(aoi, 3, newer); // identical revision is idempotent
        bool stale = false, wrongEpoch = false, wrongTick = false;
        try { Move(aoi, 4, a with { EntityRevision = 2 }); }
        catch (InvalidOperationException) { stale = true; }
        try { Move(aoi, 5, newer with { MapEpoch = 99, EntityRevision = 4 }); }
        catch (InvalidOperationException) { wrongEpoch = true; }
        try { Move(aoi, 5, newer); }
        catch (InvalidOperationException) { wrongTick = true; }
        Require(stale && wrongEpoch && wrongTick, "AOI accepted stale/future state");

        var bounded = new WorldAoi(new WorldAoiOptions(
            EnterHalfWidth: 200, EnterHalfHeight: 200, BucketSize: 64,
            MaxPendingEventsPerObserver: 1));
        Life(bounded, 1, Join(a));
        bool overflow = false;
        try { Life(bounded, 2, Join(Actor(2, 15, 110, 100))); }
        catch (AoiBackpressureException) { overflow = true; }
        Require(overflow && bounded.PendingEvents(1) == 1 &&
                Drain(bounded, a) is [var ready] &&
                ready.Kind == AoiEventKind.WorldReady,
                "full AOI event queue dropped WorldReady or silently accepted overflow");
    }

    private static async Task RealHostLifecycle(string root)
    {
        string mapDir = Checks.Fixture(root, 103, 8, 8, (_, _) => 0);
        await using var host = new ServerWorldHost([BlockingGrid.Load(mapDir, 103)]);
        await host.StartAsync();
        var a = await host.TrustedRegisterAsync(71, 103, 24, 24)
            ?? throw new Exception("host trusted register");
        var b = await host.TrustedRegisterAsync(72, 103, 40, 24)
            ?? throw new Exception("host second register");
        var aoi = new WorldAoi();
        var router = new WorldResultRouter();
        var pump = router.RunAsync(host.Results);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (aoi.EntityCount < 2)
            aoi.Apply(await router.ChangedBatches.ReadAsync(timeout.Token));
        Require(Drain(aoi, a).Any(e => e.Kind == AoiEventKind.WorldReady) &&
                Drain(aoi, b).Any(e => e.Kind == AoiEventKind.Spawn),
                "idle WorldHost join did not reach AOI without a Move");
        Require(await host.TrustedDisconnectAsync(72, b.ConnectionEpoch, b.MapEpoch),
                "trusted live disconnect");
        while (aoi.EntityCount != 1)
            aoi.Apply(await router.ChangedBatches.ReadAsync(timeout.Token));
        Require(Drain(aoi, a) is [var gone] &&
                gone.Kind == AoiEventKind.Despawn && gone.EntityActorId == 72,
                "idle WorldHost disconnect did not despawn other player");
        await host.StopAsync();
        await pump.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
