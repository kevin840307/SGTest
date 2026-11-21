using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

/// <summary>
/// Fully Server-owned resolved Heal request. Raw clients never supply
/// SkillLevel, MP cost, heal amount, CharacterId or connection generation.
/// The caller must derive those from the authenticated binding, SkillBook and
/// verified original-source rule before crossing this durability boundary.
/// </summary>
public readonly record struct TrustedSkillHealContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,
    uint SkillSequence,int SkillId,int SkillLevel,long TargetCharacterId,
    int ExpectedCasterHp,int ExpectedTargetHp,int MpCost,int HealAmount);

public enum SkillHealStatus : byte
{
    Applied=0,
    AlreadyApplied=1,
    SkillNotLearned=2,
    MissingProfile=3,
    CasterDead=4,
    TargetDead=5,
    InsufficientMp=6,
    FullHp=7,
    StaleSequence=8,
    CombatDiverged=9
}

public sealed record SkillHealResult(
    SkillHealStatus Status,int SkillId,int SkillLevel,long TargetCharacterId,
    int MpCost=0,int HealRequested=0,int HealApplied=0,
    int PreviousMp=0,int CurrentMp=0,int MaxMp=0,
    int TargetPreviousHp=0,int TargetCurrentHp=0,int TargetMaxHp=0);

public sealed partial class SqliteIdentityStore
{
    /// <summary>
    /// Atomically debit caster MP and increase one living target's HP. The
    /// learned SkillID/level is re-read inside this same SQLite transaction.
    /// A committed Heal also owns the existing skill_mp_actions sequence, so a
    /// replay or another skill kind cannot spend the same authenticated
    /// SkillSequence twice.
    /// </summary>
    public async Task<SkillHealResult> SettleSkillHealAsync(
        TrustedSkillHealContext actor,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(actor.CharacterId<=0||actor.ConnectionGeneration<=0||
           actor.MapEpoch<=0||actor.SkillSequence==0||
           actor.SkillId is <1 or >65535||actor.SkillLevel is <1 or >255||
           actor.TargetCharacterId<=0||actor.ExpectedCasterHp<=0||
           actor.ExpectedTargetHp<=0||actor.MpCost is <1 or >100_000_000||
           actor.HealAmount is <1 or >100_000_000)
            throw new ArgumentOutOfRangeException(nameof(actor));

        return await Task.Run<SkillHealResult>(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"SELECT skill_id,skill_level,target_character_id,
mp_cost,heal_requested,heal_applied,caster_previous_mp,caster_current_mp,
caster_max_mp,target_previous_hp,target_current_hp,target_max_hp
FROM skill_heal_actions
WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch AND skill_sequence=$seq;";
                replay.Parameters.AddWithValue("$actor",actor.CharacterId);
                replay.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                replay.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                replay.Parameters.AddWithValue("$seq",(long)actor.SkillSequence);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    int skill=reader.GetInt32(0),level=reader.GetInt32(1);
                    long replayTarget=reader.GetInt64(2);
                    int cost=reader.GetInt32(3),requested=reader.GetInt32(4);
                    if(skill!=actor.SkillId||level!=actor.SkillLevel||
                       replayTarget!=actor.TargetCharacterId||cost!=actor.MpCost||
                       requested!=actor.HealAmount)
                        return new(SkillHealStatus.StaleSequence,
                            actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);
                    return new(SkillHealStatus.AlreadyApplied,skill,level,replayTarget,
                        cost,requested,reader.GetInt32(5),
                        reader.GetInt32(6),reader.GetInt32(7),reader.GetInt32(8),
                        reader.GetInt32(9),reader.GetInt32(10),reader.GetInt32(11));
                }
            }

            // A damage/PvP skill may already own this shared durable sequence.
            if(ScalarLong(connection,@"SELECT COUNT(*) FROM skill_mp_actions
WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch AND skill_sequence=$seq;",tx,
                ("$actor",actor.CharacterId),("$conn",actor.ConnectionGeneration),
                ("$epoch",actor.MapEpoch),("$seq",(long)actor.SkillSequence))!=0)
                return new(SkillHealStatus.StaleSequence,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);

            long last=ScalarLong(connection,@"SELECT COALESCE(MAX(skill_sequence),0)
FROM skill_mp_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",tx,
                ("$actor",actor.CharacterId),("$conn",actor.ConnectionGeneration),
                ("$epoch",actor.MapEpoch));
            if(last>=actor.SkillSequence)
                return new(SkillHealStatus.StaleSequence,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);

            long learned=ScalarLong(connection,@"SELECT COALESCE(MAX(level),0)
FROM character_skills
WHERE character_id=$actor AND skill_id=$skill;",tx,
                ("$actor",actor.CharacterId),("$skill",actor.SkillId));
            if(learned!=actor.SkillLevel)
                return new(SkillHealStatus.SkillNotLearned,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);

            var caster=ReadCombatProfile(connection,actor.CharacterId,tx);
            var target=actor.TargetCharacterId==actor.CharacterId
                ? caster:ReadCombatProfile(connection,actor.TargetCharacterId,tx);
            if(caster is null||target is null)
                return new(SkillHealStatus.MissingProfile,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);
            if(caster.Data.CurrentHp<=0)
                return new(SkillHealStatus.CasterDead,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                    actor.MpCost,actor.HealAmount);
            if(target.Data.CurrentHp<=0)
                return new(SkillHealStatus.TargetDead,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                    actor.MpCost,actor.HealAmount);
            if(caster.Data.CurrentHp!=actor.ExpectedCasterHp||
               target.Data.CurrentHp!=actor.ExpectedTargetHp)
                return new(SkillHealStatus.CombatDiverged,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                    actor.MpCost,actor.HealAmount);
            if(target.Data.CurrentHp>=target.Data.MaxHp)
                return new(SkillHealStatus.FullHp,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                    actor.MpCost,actor.HealAmount,
                    0,caster.Data.CurrentMp,caster.Data.CurrentMp,caster.Data.MaxMp,
                    target.Data.CurrentHp,target.Data.CurrentHp,target.Data.MaxHp);
            if(caster.Data.CurrentMp<actor.MpCost)
                return new(SkillHealStatus.InsufficientMp,
                    actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                    actor.MpCost,actor.HealAmount,
                    0,caster.Data.CurrentMp,caster.Data.CurrentMp,caster.Data.MaxMp,
                    target.Data.CurrentHp,target.Data.CurrentHp,target.Data.MaxHp);

            int mpAfter=caster.Data.CurrentMp-actor.MpCost;
            int applied=Math.Min(actor.HealAmount,
                target.Data.MaxHp-target.Data.CurrentHp);
            int hpAfter=target.Data.CurrentHp+applied;
            long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if(actor.TargetCharacterId==actor.CharacterId)
            {
                using var update=connection.CreateCommand();
                update.Transaction=tx;
                update.CommandText=@"UPDATE character_combat_profiles
SET current_mp=$mpAfter,current_hp=$hpAfter,updated_unix_ms=$now
WHERE character_id=$actor AND current_mp=$mpBefore AND current_hp=$hpBefore
AND max_mp=$maxMp AND max_hp=$maxHp;";
                update.Parameters.AddWithValue("$mpAfter",mpAfter);
                update.Parameters.AddWithValue("$hpAfter",hpAfter);
                update.Parameters.AddWithValue("$now",now);
                update.Parameters.AddWithValue("$actor",actor.CharacterId);
                update.Parameters.AddWithValue("$mpBefore",caster.Data.CurrentMp);
                update.Parameters.AddWithValue("$hpBefore",caster.Data.CurrentHp);
                update.Parameters.AddWithValue("$maxMp",caster.Data.MaxMp);
                update.Parameters.AddWithValue("$maxHp",caster.Data.MaxHp);
                if(update.ExecuteNonQuery()!=1)
                    return new(SkillHealStatus.CombatDiverged,
                        actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);
            }
            else
            {
                using(var spend=connection.CreateCommand())
                {
                    spend.Transaction=tx;
                    spend.CommandText=@"UPDATE character_combat_profiles
SET current_mp=$after,updated_unix_ms=$now
WHERE character_id=$actor AND current_hp=$hp
AND current_mp=$before AND max_mp=$max;";
                    spend.Parameters.AddWithValue("$after",mpAfter);
                    spend.Parameters.AddWithValue("$now",now);
                    spend.Parameters.AddWithValue("$actor",actor.CharacterId);
                    spend.Parameters.AddWithValue("$hp",caster.Data.CurrentHp);
                    spend.Parameters.AddWithValue("$before",caster.Data.CurrentMp);
                    spend.Parameters.AddWithValue("$max",caster.Data.MaxMp);
                    if(spend.ExecuteNonQuery()!=1)
                        return new(SkillHealStatus.CombatDiverged,
                            actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);
                }
                using(var heal=connection.CreateCommand())
                {
                    heal.Transaction=tx;
                    heal.CommandText=@"UPDATE character_combat_profiles
SET current_hp=$after,updated_unix_ms=$now
WHERE character_id=$target AND current_hp=$before AND max_hp=$max;";
                    heal.Parameters.AddWithValue("$after",hpAfter);
                    heal.Parameters.AddWithValue("$now",now);
                    heal.Parameters.AddWithValue("$target",actor.TargetCharacterId);
                    heal.Parameters.AddWithValue("$before",target.Data.CurrentHp);
                    heal.Parameters.AddWithValue("$max",target.Data.MaxHp);
                    if(heal.ExecuteNonQuery()!=1)
                        return new(SkillHealStatus.CombatDiverged,
                            actor.SkillId,actor.SkillLevel,actor.TargetCharacterId);
                }
            }

            using(var mpAction=connection.CreateCommand())
            {
                mpAction.Transaction=tx;
                mpAction.CommandText=@"INSERT INTO skill_mp_actions(
character_id,connection_generation,map_epoch,skill_sequence,skill_id,mp_cost,
previous_mp,current_mp,max_mp,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$skill,$cost,$before,$after,$max,$now);";
                mpAction.Parameters.AddWithValue("$actor",actor.CharacterId);
                mpAction.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                mpAction.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                mpAction.Parameters.AddWithValue("$seq",(long)actor.SkillSequence);
                mpAction.Parameters.AddWithValue("$skill",actor.SkillId);
                mpAction.Parameters.AddWithValue("$cost",actor.MpCost);
                mpAction.Parameters.AddWithValue("$before",caster.Data.CurrentMp);
                mpAction.Parameters.AddWithValue("$after",mpAfter);
                mpAction.Parameters.AddWithValue("$max",caster.Data.MaxMp);
                mpAction.Parameters.AddWithValue("$now",now);
                if(mpAction.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Skill Heal MP receipt insert failed");
            }
            using(var detail=connection.CreateCommand())
            {
                detail.Transaction=tx;
                detail.CommandText=@"INSERT INTO skill_heal_actions(
character_id,connection_generation,map_epoch,skill_sequence,skill_id,skill_level,
target_character_id,mp_cost,heal_requested,heal_applied,
caster_previous_mp,caster_current_mp,caster_max_mp,
target_previous_hp,target_current_hp,target_max_hp,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$skill,$level,$target,$cost,$requested,$applied,
$mpBefore,$mpAfter,$maxMp,$hpBefore,$hpAfter,$maxHp,$now);";
                detail.Parameters.AddWithValue("$actor",actor.CharacterId);
                detail.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                detail.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                detail.Parameters.AddWithValue("$seq",(long)actor.SkillSequence);
                detail.Parameters.AddWithValue("$skill",actor.SkillId);
                detail.Parameters.AddWithValue("$level",actor.SkillLevel);
                detail.Parameters.AddWithValue("$target",actor.TargetCharacterId);
                detail.Parameters.AddWithValue("$cost",actor.MpCost);
                detail.Parameters.AddWithValue("$requested",actor.HealAmount);
                detail.Parameters.AddWithValue("$applied",applied);
                detail.Parameters.AddWithValue("$mpBefore",caster.Data.CurrentMp);
                detail.Parameters.AddWithValue("$mpAfter",mpAfter);
                detail.Parameters.AddWithValue("$maxMp",caster.Data.MaxMp);
                detail.Parameters.AddWithValue("$hpBefore",target.Data.CurrentHp);
                detail.Parameters.AddWithValue("$hpAfter",hpAfter);
                detail.Parameters.AddWithValue("$maxHp",target.Data.MaxHp);
                detail.Parameters.AddWithValue("$now",now);
                if(detail.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException("Skill Heal receipt insert failed");
            }
            tx.Commit();
            return new(SkillHealStatus.Applied,
                actor.SkillId,actor.SkillLevel,actor.TargetCharacterId,
                actor.MpCost,actor.HealAmount,applied,
                caster.Data.CurrentMp,mpAfter,caster.Data.MaxMp,
                target.Data.CurrentHp,hpAfter,target.Data.MaxHp);
        },cancellationToken).ConfigureAwait(false);
    }
}
