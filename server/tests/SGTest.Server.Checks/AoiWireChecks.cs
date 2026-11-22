using SGTest.Server.Visibility;
using SGTest.Server.Monster;
using System.Buffers.Binary;
using SGTest.Server.Gateway;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Identity;

internal static class AoiWireChecks
{
    private static void Require(bool ok, string name) => Checks.Require(ok, name);
    private const string Ready = "2D0000000100140001000000000000002A000000000000000B000000000000000F0000000000C0410000004101";
    private const string Spawn = "4A0000000100150001000000000000002B000000000000001600000000000000020000000000000001000000000000000F0000000700000000000000000040420000C041010400000001";
    private const string Delta = "4A0000000100170001000000000000002C000000000000001600000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000001";
    private const string Self = "4A0000000100180001000000000000002C000000000000000B00000000000000020000000000000001000000000000000F0000000800000000000000000060420000C041000500000006";
    private const string Despawn = "280000000100160001000000000000002D0000000000000016000000000000000200000000000000";
    private const string MovementIntent = "3E00000001006E0001000000000000002D00000000000000160000000000000002000000000000000100000000000000050000000000A043000034430106";
    private const string ReadyAck = "180000000100190001000000000000002A00000000000000";
    private const string Resync = "1000000001001A000100000000000000";
    private const string MonsterSpawn = "4E00000001001B000100000000000000320000000000000021A0AE3B0000000001000000000000000D0000000000F0420000A04202000000000000005A0000006400000002060B00000000000000";
    private const string MonsterDelta = "4E00000001001C000100000000000000330000000000000021A0AE3B0000000001000000000000000D0000000000F8420000A0420300000000000000500000006400000003060B00000000000000";
    private const string MonsterDespawn = "2800000001001D000100000000000000340000000000000021A0AE3B000000000300000000000000";
    private const string GroundSpawn = "340000000100200001000000000000003700000000000000F5010000000000000F000000000040420000C0418D0A000002000000";
    private const string GroundDespawn = "200000000100210001000000000000003800000000000000F501000000000000";
    private const string PlayerAttack = "4200000001002900010000000000000032000000000000000B0000000000000002000000000000000700000021A0AE3B00000000001E000000030000000000000006";
    private const string PlayerDamageVisual = "4200000001002A0001000000000000003C00000000000000160000000000000003000000000000000400000021A0AE3B000000000700000000000000120000000006";
    private const string PlayerRespawnVisual = "2D00000001002B0001000000000000003D00000000000000160000000000000003000000000000000200000006";

    public static void Verify()
    {
        Checks.Run("AOI compact wire WorldReady/Spawn/Delta/Despawn fixed cross-Godot golden",
            CodecRoundtrip);
        Checks.Run("AOI wire rejects malformed float, generations, sizes and boolean",
            CodecReject);
        Checks.Run("Gateway AOI reliable FIFO enforces viewer MapEpoch, identity and backpressure",
            GatewayAoi);
    }

    private static void CodecRoundtrip()
    {
        byte[] ready = WireProtocol.EncodeWorldReady(new(1, 42, 11, 15, 24, 8));
        Require(Convert.ToHexString(ready) == Ready &&
                WireProtocol.TryDecodeWorldReady(ready, out var own) &&
                own.ActorId == 11 && own.X == 24 && own.ServerTick == 42,
                "WorldReady binary fixture");
        var e = new EntityStateWire(1, 43, 22, 2, 1, 15, 7,
            48, 24, true, 4);
        byte[] spawn = WireProtocol.EncodeEntityState(WireOpcode.EntitySpawn, e);
        Require(Convert.ToHexString(spawn) == Spawn &&
                WireProtocol.TryDecodeEntityState(spawn, out var kind, out var parsed) &&
                kind == WireOpcode.EntitySpawn && parsed == e,
                "Spawn binary fixture");
        e = e with { ServerTick = 44, Revision = 8, X = 56,
                     IsMoving = false, MovementActionSequence = 5 };
        byte[] delta = WireProtocol.EncodeEntityState(WireOpcode.EntityDelta, e);
        Require(Convert.ToHexString(delta) == Delta &&
                WireProtocol.TryDecodeEntityState(delta, out kind, out parsed) &&
                kind == WireOpcode.EntityDelta && parsed == e,
                "Delta binary fixture");
        var self = e with { ActorId = 11, Facing = 6 };
        byte[] ownBytes = WireProtocol.EncodeEntityState(WireOpcode.SelfState, self);
        Require(Convert.ToHexString(ownBytes) == Self &&
                WireProtocol.TryDecodeEntityState(ownBytes, out kind, out parsed) &&
                kind == WireOpcode.SelfState && parsed == self, "SelfState binary fixture");
        var movementIntent = new PlayerMovementIntentWire(
            1,45,22,2,1,5,320,180,true,6);
        byte[] movementIntentBytes = WireProtocol.EncodePlayerMovementIntent(movementIntent);
        Require(Convert.ToHexString(movementIntentBytes) == MovementIntent &&
                WireProtocol.TryDecodePlayerMovementIntent(
                    movementIntentBytes,out var parsedMovementIntent) &&
                parsedMovementIntent == movementIntent,
            "PlayerMovementIntent C#/Godot golden binary");
        byte[] ack = WireProtocol.EncodeWorldReadyAck(new(1, 42));
        Require(Convert.ToHexString(ack) == ReadyAck &&
                WireProtocol.TryDecodeWorldReadyAck(ack, out var ackValue) &&
                ackValue.MapEpoch == 1 && ackValue.ServerTick == 42,
                "WorldReady ACK binary fixture");
        byte[] resync = WireProtocol.EncodeWorldResyncRequest(new(1));
        Require(Convert.ToHexString(resync) == Resync &&
                WireProtocol.TryDecodeWorldResyncRequest(resync, out var resyncValue) &&
                resyncValue.MapEpoch == 1, "WorldResync binary fixture");
        var monster = new MonsterStateWire(1,50,1001300001,1,0,13,120,80,2,
            90,100,MonsterAiState.Chase,6,11);
        byte[] monsterSpawn = WireProtocol.EncodeMonsterState(WireOpcode.MonsterSpawn,monster);
        Require(Convert.ToHexString(monsterSpawn) == MonsterSpawn &&
                WireProtocol.TryDecodeMonsterState(monsterSpawn,out var monsterKind,out var monsterValue) &&
                monsterKind == WireOpcode.MonsterSpawn && monsterValue == monster,
                "MonsterSpawn binary fixture");
        monster = monster with { ServerTick=51, X=124, Revision=3, CurrentHp=80,
                                 State=MonsterAiState.Attack };
        byte[] monsterDelta = WireProtocol.EncodeMonsterState(WireOpcode.MonsterDelta,monster);
        Require(Convert.ToHexString(monsterDelta) == MonsterDelta &&
                WireProtocol.TryDecodeMonsterState(monsterDelta,out monsterKind,out monsterValue) &&
                monsterKind == WireOpcode.MonsterDelta && monsterValue == monster,
                "MonsterDelta binary fixture");
        byte[] monsterDespawn = WireProtocol.EncodeMonsterDespawn(
            new(1,52,1001300001,3));
        Require(Convert.ToHexString(monsterDespawn) == MonsterDespawn &&
                WireProtocol.TryDecodeMonsterDespawn(monsterDespawn,out var monsterGone) &&
                monsterGone.EntityId == 1001300001 && monsterGone.Revision == 3,
                "MonsterDespawn binary fixture");
        var ground=new GroundDropWire(1,55,501,15,48,24,2701,2);
        var groundSpawn=WireProtocol.EncodeGroundDropSpawn(ground);
        Require(Convert.ToHexString(groundSpawn)==GroundSpawn &&
            WireProtocol.TryDecodeGroundDropSpawn(groundSpawn,out var groundValue) &&
            groundValue==ground,"GroundDropSpawn C#/Godot golden binary");
        var groundDespawn=WireProtocol.EncodeGroundDropDespawn(new(1,56,501));
        Require(Convert.ToHexString(groundDespawn)==GroundDespawn &&
            WireProtocol.TryDecodeGroundDropDespawn(groundDespawn,out var goneDrop) &&
            goneDrop.DropId==501,"GroundDropDespawn C#/Godot golden binary");
        var malformedGround=(byte[])groundSpawn.Clone();
        malformedGround[38]=0xC0;malformedGround[39]=0x7F;
        Require(!WireProtocol.TryDecodeGroundDropSpawn(malformedGround,out _),
            "nonfinite ground drop coordinate accepted");
        var playerAttack=new PlayerAttackVisualWire(
            1,50,11,2,7,1001300001,NormalAttackStatus.Applied,30,3,6);
        byte[] playerAttackBytes=WireProtocol.EncodePlayerAttackVisual(playerAttack);
        Require(Convert.ToHexString(playerAttackBytes)==PlayerAttack &&
            WireProtocol.TryDecodePlayerAttackVisual(playerAttackBytes,out var parsedAttack) &&
            parsedAttack==playerAttack,
            "PlayerAttackVisual C#/Godot golden binary");
        var playerDamageVisual=new PlayerDamageVisualWire(
            1,60,22,3,4,1001300001,7,18,false,6);
        byte[] playerDamageVisualBytes=WireProtocol.EncodePlayerDamageVisual(playerDamageVisual);
        Require(Convert.ToHexString(playerDamageVisualBytes)==PlayerDamageVisual &&
            WireProtocol.TryDecodePlayerDamageVisual(
                playerDamageVisualBytes,out var parsedDamageVisual) &&
            parsedDamageVisual==playerDamageVisual,
            "PlayerDamageVisual C#/Godot golden binary");
        var playerRespawnVisual=new PlayerRespawnVisualWire(1,61,22,3,2,6);
        byte[] playerRespawnVisualBytes=
            WireProtocol.EncodePlayerRespawnVisual(playerRespawnVisual);
        Require(Convert.ToHexString(playerRespawnVisualBytes)==PlayerRespawnVisual &&
            WireProtocol.TryDecodePlayerRespawnVisual(
                playerRespawnVisualBytes,out var parsedRespawnVisual) &&
            parsedRespawnVisual==playerRespawnVisual,
            "PlayerRespawnVisual C#/Godot golden binary");
        byte[] despawn = WireProtocol.EncodeEntityDespawn(new(1, 45, 22, 2));
        Require(Convert.ToHexString(despawn) == Despawn &&
                WireProtocol.TryDecodeEntityDespawn(despawn, out var left) &&
                left.ActorId == 22 && left.ConnectionEpoch == 2 &&
                left.ServerTick == 45, "Despawn binary fixture");
    }

    private static void CodecReject()
    {
        var bad = Convert.FromHexString(Spawn);
        bad[68] = 2;
        Require(!WireProtocol.TryDecodeEntityState(bad, out _, out _),
                "invalid moving bit accepted");
        foreach (var invalid in new byte[] {0,9,255})
        {
            var badSelf = Convert.FromHexString(Self); badSelf[73] = invalid;
            Require(!WireProtocol.TryDecodeEntityState(badSelf,out _,out _), "invalid entity facing accepted");
            var badReady = Convert.FromHexString(Ready); badReady[44] = invalid;
            Require(!WireProtocol.TryDecodeWorldReady(badReady,out _), "invalid Ready facing accepted");
        }
        bad = Convert.FromHexString(Ready);
        bad[0] = 43;
        Require(!WireProtocol.TryDecodeWorldReady(bad, out _),
                "invalid Ready frame length accepted");
        bad = Convert.FromHexString(Delta);
        bad[60] = 0; bad[61] = 0; bad[62] = 0xC0; bad[63] = 0x7F; // quiet NaN float
        Require(!WireProtocol.TryDecodeEntityState(bad, out _, out _),
                "NaN coordinate accepted");
        bad = Convert.FromHexString(MovementIntent);
        bad[60] = 2;
        Require(!WireProtocol.TryDecodePlayerMovementIntent(bad, out _),
                "invalid movement intent moving bit accepted");
        bad = Convert.FromHexString(MovementIntent);
        bad[61] = 9;
        Require(!WireProtocol.TryDecodePlayerMovementIntent(bad, out _),
                "invalid movement intent facing accepted");
        bad = Convert.FromHexString(MovementIntent);
        BinaryPrimitives.WriteUInt32LittleEndian(bad.AsSpan(52,4),0x7FC00000);
        Require(!WireProtocol.TryDecodePlayerMovementIntent(bad, out _),
                "NaN movement intent target accepted");
        bad = Convert.FromHexString(MonsterSpawn);
        bad[68] = 5;
        Require(!WireProtocol.TryDecodeMonsterState(bad,out _,out _),
                "invalid monster AI state accepted");
        bad = Convert.FromHexString(MonsterDelta);
        bad[69] = 9;
        Require(!WireProtocol.TryDecodeMonsterState(bad,out _,out _),
                "invalid monster facing accepted");
        bad = Convert.FromHexString(MonsterDelta);
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(60,4),101);
        Require(!WireProtocol.TryDecodeMonsterState(bad,out _,out _),
                "monster HP above MaxHP accepted");
        bad=Convert.FromHexString(PlayerAttack);
        bad[52]=(byte)NormalAttackStatus.OutOfRange;
        Require(!WireProtocol.TryDecodePlayerAttackVisual(bad,out _),
            "non-visual attack status accepted as presentation event");
        bad=Convert.FromHexString(PlayerAttack);
        bad[65]=9;
        Require(!WireProtocol.TryDecodePlayerAttackVisual(bad,out _),
            "invalid attack visual facing accepted");
        bad=Convert.FromHexString(PlayerDamageVisual);
        bad[64]=2;
        Require(!WireProtocol.TryDecodePlayerDamageVisual(bad,out _),
            "invalid player damage visual death flag accepted");
        bad=Convert.FromHexString(PlayerDamageVisual);
        bad[65]=9;
        Require(!WireProtocol.TryDecodePlayerDamageVisual(bad,out _),
            "invalid player damage visual facing accepted");
        bad=Convert.FromHexString(PlayerRespawnVisual);
        bad[44]=0;
        Require(!WireProtocol.TryDecodePlayerRespawnVisual(bad,out _),
            "invalid player respawn visual facing accepted");
        bad = Convert.FromHexString(Despawn);
        Array.Clear(bad, 8, 8);
        Require(!WireProtocol.TryDecodeEntityDespawn(bad, out _),
                "zero receiver epoch accepted");
    }

    private static void GatewayAoi()
    {
        var owner = new ActorSnapshot(11, 15, 1, 1, 0, 0, 24, 8, 1, false);
        var other = new ActorSnapshot(22, 15, 2, 1, 0, 4, 48, 24, 7, true);
        var gateway = new GatewayConnection(owner, new GatewayConnectionOptions(
            MaxReliableFrames: 3, MaxReliableBytes: WireProtocol.MaximumFrameBytes));
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 42, 1, owner)) ==
                GatewaySendStatus.Enqueued && !gateway.WorldReadyAcknowledged,
                "own Ready enqueue");
        Require(gateway.QueueAoi(new(AoiEventKind.Spawn, 43, 1, other)) ==
                GatewaySendStatus.Enqueued, "other Spawn enqueue");
        Require(gateway.QueueAoi(new(
                AoiEventKind.Despawn,45,1,null,22,2,EntityMapEpoch:1)) ==
                GatewaySendStatus.Enqueued, "other Despawn enqueue");
        Require(gateway.QueueAoi(new(AoiEventKind.Delta, 46, 1, other)) ==
                GatewaySendStatus.Backpressured, "bounded AOI queue silently overflowed");
        Require(gateway.TryDequeueReliable(out var first) &&
                WireProtocol.TryDecodeWorldReady(first, out _), "reliable FIFO ready");
        Require(gateway.FeedClientBytes(
                WireProtocol.EncodeWorldReadyAck(new(1,42)), _ => true, out var controlCount) ==
                WireFeedStatus.Accepted && controlCount == 0 && gateway.WorldReadyAcknowledged,
                "WorldReady ACK did not open gameplay gate");
        Require(gateway.TryDequeueReliable(out var second) &&
                WireProtocol.TryDecodeEntityState(second, out var op, out _) &&
                op == WireOpcode.EntitySpawn, "reliable FIFO spawn");
        Require(gateway.TryDequeueReliable(out var third) &&
                WireProtocol.TryDecodeEntityDespawn(third, out _),
                "reliable FIFO despawn");

        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1, other)) ==
                GatewaySendStatus.GenerationMismatch, "other actor impersonated self state");
        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1,
                owner with { ConnectionEpoch = 2 })) == GatewaySendStatus.GenerationMismatch,
                "wrong connection impersonated self state");
        Require(gateway.QueueAoi(new(AoiEventKind.SelfState, 46, 1, owner)) ==
                GatewaySendStatus.Enqueued, "own state enqueue");
        Require(gateway.TryDequeueReliable(out var ownFrame) &&
                WireProtocol.TryDecodeEntityState(ownFrame, out var ownKind, out var own) &&
                ownKind == WireOpcode.SelfState && own.ActorId == owner.ActorId,
                "own state reliable wire");

        var monster = new MonsterSnapshot(
            1001500001,1,0,15,120,80,120,80,90,100,
            MonsterAiState.Chase,owner.ActorId,2,6);
        Require(gateway.QueueAoi(new(AoiEventKind.MonsterSpawn,47,1,
                    Monster:monster)) == GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out var monsterFrame) &&
                WireProtocol.TryDecodeMonsterState(monsterFrame,out var monsterKind,out var monsterWire) &&
                monsterKind == WireOpcode.MonsterSpawn &&
                monsterWire.EntityId == monster.EntityId &&
                monsterWire.TargetActorId == owner.ActorId,
                "monster Spawn did not use shared reliable Gateway FIFO");
        monster = monster with { X=124, CurrentHp=80, EntityRevision=3,
                                 State=MonsterAiState.Attack };
        Require(gateway.QueueAoi(new(AoiEventKind.MonsterDelta,48,1,
                    Monster:monster)) == GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out monsterFrame) &&
                WireProtocol.TryDecodeMonsterState(monsterFrame,out monsterKind,out monsterWire) &&
                monsterKind == WireOpcode.MonsterDelta &&
                monsterWire.Revision == 3 && monsterWire.CurrentHp == 80,
                "monster Delta did not use shared reliable Gateway FIFO");
        Require(gateway.QueueAoi(new(AoiEventKind.MonsterDespawn,49,1,
                    MonsterEntityId:monster.EntityId,MonsterRevision:monster.EntityRevision)) ==
                GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out monsterFrame) &&
                WireProtocol.TryDecodeMonsterDespawn(monsterFrame,out var monsterGone) &&
                monsterGone.EntityId == monster.EntityId &&
                monsterGone.Revision == monster.EntityRevision,
                "monster Despawn did not use shared reliable Gateway FIFO");
        var visual=new PlayerAttackVisual(
            owner.ActorId,owner.ConnectionEpoch,7,monster.EntityId,
            NormalAttackStatus.Applied,30,monster.EntityRevision,6);
        Require(gateway.QueueAoi(new(AoiEventKind.PlayerAttackVisual,50,1,
                    PlayerAttack:visual))==GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out var visualFrame) &&
                WireProtocol.TryDecodePlayerAttackVisual(visualFrame,out var visualWire) &&
                visualWire.AttackerActorId==owner.ActorId &&
                visualWire.AttackSequence==7 && visualWire.Damage==30 &&
                visualWire.Facing==6,
                "player attack visual did not use shared reliable Gateway FIFO");
        var damageVisual=new PlayerDamageVisual(
            owner.ActorId,owner.ConnectionEpoch,4,monster.EntityId,
            monster.EntityRevision,18,false,6);
        Require(gateway.QueueAoi(new(AoiEventKind.PlayerDamageVisual,51,1,
                    PlayerDamage:damageVisual))==GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out visualFrame) &&
                WireProtocol.TryDecodePlayerDamageVisual(
                    visualFrame,out var damageVisualWire) &&
                damageVisualWire.VictimActorId==owner.ActorId &&
                damageVisualWire.DamageSequence==4 &&
                damageVisualWire.Damage==18 && !damageVisualWire.Died &&
                damageVisualWire.Facing==6,
                "player damage visual did not use shared reliable Gateway FIFO");
        var respawnVisual=new PlayerRespawnVisual(
            owner.ActorId,owner.ConnectionEpoch,2,6);
        Require(gateway.QueueAoi(new(AoiEventKind.PlayerRespawnVisual,52,1,
                    PlayerRespawn:respawnVisual))==GatewaySendStatus.Enqueued &&
                gateway.TryDequeueReliable(out visualFrame) &&
                WireProtocol.TryDecodePlayerRespawnVisual(
                    visualFrame,out var respawnVisualWire) &&
                respawnVisualWire.ActorId==owner.ActorId &&
                respawnVisualWire.RespawnSequence==2 &&
                respawnVisualWire.Facing==6,
                "player respawn visual did not use shared reliable Gateway FIFO");

        TrustedWorldResyncRequest requested = default;
        Require(gateway.FeedClientBytes(
                WireProtocol.EncodeWorldResyncRequest(new(1)), _ => true, out var resyncCount,
                null, request => { requested = request; return true; }) ==
                WireFeedStatus.Accepted && resyncCount == 0 &&
                requested == new TrustedWorldResyncRequest(11,1,1) &&
                !gateway.WorldReadyAcknowledged,
                "same-map resync request did not close ACK gate");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 50, 2, owner)) ==
                GatewaySendStatus.GenerationMismatch, "future map sent without transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 50, 1, other)) ==
                GatewaySendStatus.GenerationMismatch, "other actor impersonated Ready");
        Require(gateway.TryUpdateTrustedBinding(owner with { MapId = 13, MapEpoch = 2 }),
                "trusted MapEpoch transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 51, 1, owner)) ==
                GatewaySendStatus.GenerationMismatch, "old map AOI leaked after transfer");
        Require(gateway.QueueAoi(new(AoiEventKind.WorldReady, 52, 2,
                owner with { MapId = 13, MapEpoch = 2 })) ==
                GatewaySendStatus.Enqueued && !gateway.WorldReadyAcknowledged,
                "new map Ready rejected");
    }
}
