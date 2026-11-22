using SGTest.Server.Visibility;
using SGTest.Server.Skill;
using SGTest.Server.Pvp;
using SGTest.Server.Npc;
using SGTest.Server.Monster;
using SGTest.Server.Combat;
using SGTest.Server.Wire;
using SGTest.Server.World;
using SGTest.Server.Identity;

namespace SGTest.Server.Gateway;

/// <summary>
/// One authenticated GatewayConnection's reliable AOI encoding and exact
/// per-viewer actor-visibility bookkeeping. Shares the same generation gate
/// and bounded FIFO with all other responses; no additional AOI owner.
/// </summary>
public sealed partial class GatewayConnection
{
    /// <summary>
    /// Reliable AOI event (WorldReady/Spawn/Delta/Despawn), already filtered by
    /// trusted WorldAoi. A future-map or stale-map AOI event must never leak
    /// across the current connection/map generation. Shares receipt FIFO.
    /// </summary>
    public GatewaySendStatus QueueAoi(AoiEvent evt)
    {
        lock (_bindingGate)
        {
            if (_closed) return GatewaySendStatus.Closed;
            var bound = _binding;
            if (evt.ReceiverMapEpoch != bound.MapEpoch || evt.Tick < 0)
                return GatewaySendStatus.GenerationMismatch;
            byte[] frame;
            switch (evt.Kind)
            {
                case AoiEventKind.WorldReady when evt.Entity is { } ready &&
                     ready.ActorId == bound.ActorId &&
                     ready.ConnectionEpoch == bound.ConnectionEpoch &&
                     ready.MapEpoch == bound.MapEpoch:
                    frame = WireProtocol.EncodeWorldReady(new(
                        evt.ReceiverMapEpoch, evt.Tick, ready.ActorId,
                        ready.MapId, (float)ready.X, (float)ready.Y, ready.Facing));
                    break;

                case AoiEventKind.SelfState when evt.Entity is { } self &&
                     self.ActorId == bound.ActorId &&
                     self.ConnectionEpoch == bound.ConnectionEpoch &&
                     self.MapEpoch == bound.MapEpoch:
                    frame = WireProtocol.EncodeEntityState(WireOpcode.SelfState,
                        new(evt.ReceiverMapEpoch, evt.Tick, self.ActorId,
                            self.ConnectionEpoch, self.MapEpoch, self.MapId,
                            self.EntityRevision, (float)self.X, (float)self.Y,
                            self.IsMoving, self.MovementActionSequence, self.Facing));
                    break;

                case AoiEventKind.Spawn or AoiEventKind.Delta
                    when evt.Entity is { } entity && entity.ActorId != bound.ActorId:
                    frame = WireProtocol.EncodeEntityState(
                        evt.Kind == AoiEventKind.Spawn
                            ? WireOpcode.EntitySpawn : WireOpcode.EntityDelta,
                        new(evt.ReceiverMapEpoch, evt.Tick, entity.ActorId,
                            entity.ConnectionEpoch, entity.MapEpoch, entity.MapId,
                            entity.EntityRevision, (float)entity.X, (float)entity.Y,
                            entity.IsMoving, entity.MovementActionSequence, entity.Facing));
                    break;

                case AoiEventKind.Despawn
                    when evt.EntityActorId > 0 && evt.EntityConnectionEpoch > 0 &&
                         evt.EntityMapEpoch > 0:
                    frame = WireProtocol.EncodeEntityDespawn(new(
                        evt.ReceiverMapEpoch, evt.Tick, evt.EntityActorId,
                        evt.EntityConnectionEpoch));
                    break;

                case AoiEventKind.PlayerMovementIntent
                    when evt.Entity is { } movement && movement.ActorId != bound.ActorId:
                    frame = WireProtocol.EncodePlayerMovementIntent(new(
                        evt.ReceiverMapEpoch, evt.Tick, movement.ActorId,
                        movement.ConnectionEpoch, movement.MapEpoch,
                        movement.MovementActionSequence,
                        (float)movement.MovementTargetX, (float)movement.MovementTargetY,
                        movement.IsMoving, movement.Facing));
                    break;

                case AoiEventKind.MonsterSpawn or AoiEventKind.MonsterDelta
                    when evt.Monster is { } monster &&
                         monster.EntityId != bound.ActorId:
                    frame = WireProtocol.EncodeMonsterState(
                        evt.Kind == AoiEventKind.MonsterSpawn
                            ? WireOpcode.MonsterSpawn : WireOpcode.MonsterDelta,
                        new(evt.ReceiverMapEpoch, evt.Tick, monster.EntityId,
                            monster.EnemyId, monster.SourceIndex, monster.MapId,
                            (float)monster.X, (float)monster.Y, monster.EntityRevision,
                            monster.CurrentHp, monster.MaxHp, monster.State,
                            monster.Facing, monster.TargetActorId));
                    break;

                case AoiEventKind.MonsterDespawn
                    when evt.MonsterEntityId > 0 && evt.MonsterRevision > 0:
                    frame = WireProtocol.EncodeMonsterDespawn(new(
                        evt.ReceiverMapEpoch, evt.Tick, evt.MonsterEntityId,
                        evt.MonsterRevision));
                    break;

                case AoiEventKind.GroundDropSpawn when evt.GroundDrop is { } drop:
                    frame=WireProtocol.EncodeGroundDropSpawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,drop.DropId,
                        drop.MapId,(float)drop.X,(float)drop.Y,
                        drop.ItemDefinitionId,drop.Quantity));
                    break;
                case AoiEventKind.GroundDropDespawn when evt.GroundDropId>0:
                    frame=WireProtocol.EncodeGroundDropDespawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,evt.GroundDropId));
                    break;
                case AoiEventKind.PetSpawn when evt.Pet is { } pet:
                    frame=WireProtocol.EncodePetEntitySpawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,pet.OwnerActorId,
                        pet.OwnerConnectionEpoch,pet.OwnerMapEpoch,pet.PetUid,
                        pet.DefinitionId,pet.EvolutionId,pet.Level,(float)pet.X,(float)pet.Y));
                    break;
                case AoiEventKind.PetDelta when evt.Pet is { } pet:
                    frame=WireProtocol.EncodePetEntityDelta(new(
                        evt.ReceiverMapEpoch,evt.Tick,pet.OwnerActorId,
                        pet.OwnerConnectionEpoch,pet.OwnerMapEpoch,pet.PetUid,
                        (float)pet.X,(float)pet.Y));
                    break;
                case AoiEventKind.PetDespawn when evt.Pet is { } pet:
                    frame=WireProtocol.EncodePetEntityDespawn(new(
                        evt.ReceiverMapEpoch,evt.Tick,pet.OwnerActorId,
                        pet.OwnerConnectionEpoch,pet.OwnerMapEpoch,pet.PetUid));
                    break;
                case AoiEventKind.PetAttackVisual when evt.PetAttack is { } petAttack:
                    frame=WireProtocol.EncodePetAttackVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,petAttack.OwnerActorId,
                        petAttack.OwnerConnectionEpoch,petAttack.OwnerMapEpoch,
                        petAttack.AttackSequence,petAttack.PetUid,
                        petAttack.TargetEntityId,petAttack.Status,
                        petAttack.Damage,petAttack.TargetRevision));
                    break;
                case AoiEventKind.PlayerAttackVisual when evt.PlayerAttack is { } attack:
                    frame=WireProtocol.EncodePlayerAttackVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        attack.AttackerActorId,attack.AttackerConnectionEpoch,
                        attack.AttackSequence,attack.TargetEntityId,
                        attack.Status,attack.Damage,attack.TargetRevision,
                        attack.Facing));
                    break;
                case AoiEventKind.PlayerSkillVisual when evt.PlayerSkill is { } skill:
                    frame=WireProtocol.EncodePlayerSkillVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        skill.AttackerActorId,skill.AttackerConnectionEpoch,
                        skill.SkillSequence,skill.SkillId,skill.TargetEntityId,
                        skill.Status,skill.Damage,skill.TargetRevision,
                        skill.Facing));
                    break;
                case AoiEventKind.PlayerDamageVisual when evt.PlayerDamage is { } damage:
                    frame=WireProtocol.EncodePlayerDamageVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        damage.VictimActorId,damage.VictimConnectionEpoch,
                        damage.DamageSequence,damage.AttackerMonsterEntityId,
                        damage.AttackerMonsterRevision,damage.Damage,
                        damage.Died,damage.Facing));
                    break;
                case AoiEventKind.PlayerRespawnVisual when evt.PlayerRespawn is { } respawn:
                    frame=WireProtocol.EncodePlayerRespawnVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        respawn.ActorId,respawn.ConnectionEpoch,
                        respawn.RespawnSequence,respawn.Facing));
                    break;
                case AoiEventKind.PlayerIdentity when evt.PlayerIdentity is { } identity:
                    frame=WireProtocol.EncodePlayerIdentity(new(
                        evt.ReceiverMapEpoch,identity.ActorId,
                        identity.ConnectionEpoch,identity.EntityMapEpoch,
                        identity.Mode,identity.RedMan,
                        identity.ServerUnixMilliseconds,
                        identity.PinkUntilUnixMilliseconds,
                        identity.PvpUpdatedUnixMilliseconds,
                        identity.DisplayName,identity.Appearance));
                    break;
                case AoiEventKind.PvpCombatVisual when evt.PvpCombat is { } pvp:
                    frame=WireProtocol.EncodePvpCombatVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        pvp.AttackerActorId,pvp.AttackerConnectionEpoch,
                        pvp.AttackSequence,pvp.VictimActorId,
                        pvp.VictimConnectionEpoch,pvp.DamageSequence,
                        pvp.Damage,pvp.Died,pvp.AttackerFacing,pvp.VictimFacing));
                    break;
                case AoiEventKind.PvpSkillVisual when evt.PvpSkill is { } skill:
                    frame=WireProtocol.EncodePvpSkillVisual(new(
                        evt.ReceiverMapEpoch,evt.Tick,
                        skill.AttackerActorId,skill.AttackerConnectionEpoch,
                        skill.SkillSequence,skill.SkillId,skill.VictimActorId,
                        skill.VictimConnectionEpoch,skill.DamageSequence,
                        skill.Damage,skill.Died,
                        skill.AttackerFacing,skill.VictimFacing));
                    break;
                default:
                    return GatewaySendStatus.GenerationMismatch;
            }
            if (!_reliable.TryEnqueue(frame))
                return GatewaySendStatus.Backpressured;
            if (evt.Kind == AoiEventKind.WorldReady)
            {
                _worldReadySentMapEpoch = bound.MapEpoch;
                _worldReadySentTick = evt.Tick;
                _worldReadyAckedMapEpoch = 0;
                _worldReadyAckedTick = -1;
                _resyncPending = false;
                _visibleActors.Clear();
            }
            else if(evt.Kind==AoiEventKind.Spawn&&evt.Entity is { } spawned)
                _visibleActors[spawned.ActorId]=
                    ConnectionBinding.FromTrustedSnapshot(spawned);
            else if(evt.Kind==AoiEventKind.Despawn&&
                    _visibleActors.TryGetValue(evt.EntityActorId,out var seen)&&
                    seen==new ConnectionBinding(
                        evt.EntityActorId,evt.EntityConnectionEpoch,evt.EntityMapEpoch))
                _visibleActors.Remove(evt.EntityActorId);
            return GatewaySendStatus.Enqueued;
        }
    }
}
