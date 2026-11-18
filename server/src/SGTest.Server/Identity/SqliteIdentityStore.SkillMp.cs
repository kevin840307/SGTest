namespace SGTest.Server.Identity;

/// <summary>
/// Trusted authenticated skill-generation identity. Raw clients never supply
/// CharacterId or ConnectionGeneration; the Server composes those from the
/// current Gateway/World binding and durable server boot generation.
/// </summary>
public readonly record struct TrustedSkillMpContext(
    long CharacterId,long ConnectionGeneration,long MapEpoch,
    uint SkillSequence,int SkillId);

public enum SkillMpConsumeStatus : byte
{
    Consumed=0,
    AlreadyConsumed=1,
    MissingProfile=2,
    Dead=3,
    InsufficientMp=4,
    StaleSequence=5
}

public sealed record SkillMpConsumeResult(
    SkillMpConsumeStatus Status,int SkillId,
    int MpCost=0,int PreviousMp=0,int CurrentMp=0,int MaxMp=0);

public sealed partial class SqliteIdentityStore
{
    /// <summary>
    /// Idempotent durable MP debit for one authenticated skill sequence.
    /// Successful debit and replay receipt share one SQLite transaction.
    /// Reusing the same sequence with another SkillId/cost fails closed.
    /// Non-mutating failures are not persisted and may be retried only if the
    /// higher-level combat generation still accepts that sequence.
    /// </summary>
    public async Task<SkillMpConsumeResult> ConsumeSkillMpAsync(
        TrustedSkillMpContext actor,int cost,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        if(actor.CharacterId<=0 || actor.ConnectionGeneration<=0 ||
            actor.MapEpoch<=0 || actor.SkillSequence==0 ||
            actor.SkillId is <1 or >100_000_000 ||
            cost is <1 or >100_000_000)
            throw new ArgumentOutOfRangeException(nameof(actor));

        return await Task.Run(() =>
        {
            using var conn=Open();
            using var tx=conn.BeginTransaction();

            using(var action=conn.CreateCommand())
            {
                action.Transaction=tx;
                action.CommandText=@"SELECT skill_id,mp_cost,previous_mp,current_mp,max_mp
FROM skill_mp_actions
WHERE character_id=$actor AND connection_generation=$conn
AND map_epoch=$epoch AND skill_sequence=$seq;";
                action.Parameters.AddWithValue("$actor",actor.CharacterId);
                action.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                action.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                action.Parameters.AddWithValue("$seq",(long)actor.SkillSequence);
                using var reader=action.ExecuteReader();
                if(reader.Read())
                {
                    int previousSkill=reader.GetInt32(0);
                    int previousCost=reader.GetInt32(1);
                    return previousSkill==actor.SkillId && previousCost==cost
                        ? new SkillMpConsumeResult(
                            SkillMpConsumeStatus.AlreadyConsumed,
                            previousSkill,previousCost,
                            reader.GetInt32(2),reader.GetInt32(3),reader.GetInt32(4))
                        : new SkillMpConsumeResult(
                            SkillMpConsumeStatus.StaleSequence,actor.SkillId);
                }
            }

            long last=ScalarLong(conn,@"SELECT COALESCE(MAX(skill_sequence),0)
FROM skill_mp_actions
WHERE character_id=$actor AND connection_generation=$conn AND map_epoch=$epoch;",
                tx,("$actor",actor.CharacterId),
                ("$conn",actor.ConnectionGeneration),("$epoch",actor.MapEpoch));
            if(last>=actor.SkillSequence)
                return new(SkillMpConsumeStatus.StaleSequence,actor.SkillId);

            var profile=ReadCombatProfile(conn,actor.CharacterId,tx);
            if(profile is null)
                return new(SkillMpConsumeStatus.MissingProfile,actor.SkillId);
            int current=profile.Data.CurrentMp;
            int max=profile.Data.MaxMp;
            if(profile.Data.CurrentHp<=0)
                return new(SkillMpConsumeStatus.Dead,actor.SkillId,
                    cost, current,current,max);
            if(current<cost)
                return new(SkillMpConsumeStatus.InsufficientMp,actor.SkillId,
                    cost,current,current,max);

            int after=current-cost;
            using(var update=conn.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=@"UPDATE character_combat_profiles
SET current_mp=$after,updated_unix_ms=$now
WHERE character_id=$actor AND current_hp>0
AND current_mp=$before AND max_mp=$max;";
                update.Parameters.AddWithValue("$after",after);
                update.Parameters.AddWithValue("$now",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                update.Parameters.AddWithValue("$actor",actor.CharacterId);
                update.Parameters.AddWithValue("$before",current);
                update.Parameters.AddWithValue("$max",max);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Combat MP raced during durable skill settlement");
            }

            using(var save=conn.CreateCommand())
            {
                save.Transaction=tx;
                save.CommandText=@"INSERT INTO skill_mp_actions(
character_id,connection_generation,map_epoch,skill_sequence,skill_id,
mp_cost,previous_mp,current_mp,max_mp,created_unix_ms)
VALUES($actor,$conn,$epoch,$seq,$skill,$cost,$before,$after,$max,$now);";
                save.Parameters.AddWithValue("$actor",actor.CharacterId);
                save.Parameters.AddWithValue("$conn",actor.ConnectionGeneration);
                save.Parameters.AddWithValue("$epoch",actor.MapEpoch);
                save.Parameters.AddWithValue("$seq",(long)actor.SkillSequence);
                save.Parameters.AddWithValue("$skill",actor.SkillId);
                save.Parameters.AddWithValue("$cost",cost);
                save.Parameters.AddWithValue("$before",current);
                save.Parameters.AddWithValue("$after",after);
                save.Parameters.AddWithValue("$max",max);
                save.Parameters.AddWithValue("$now",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if(save.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Skill MP replay receipt was not persisted");
            }

            tx.Commit();
            return new(SkillMpConsumeStatus.Consumed,actor.SkillId,
                cost,current,after,max);
        },cancellationToken).ConfigureAwait(false);
    }
}
