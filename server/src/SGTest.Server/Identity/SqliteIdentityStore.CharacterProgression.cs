using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum CharacterAttributeAllocationStatus : byte
{
    Applied = 0,
    CharacterNotFound = 1,
    ProfileNotVerified = 2,
    NoAvailablePoints = 3,
    AttributeAtCap = 4
}

public sealed record CharacterAttributeAllocationOutcome(
    CharacterAttributeAllocationStatus Status,
    CharacterCombatProfile? Profile);

public sealed record CharacterProgressionGrantOutcome(
    CharacterExperienceGrantStatus Status,
    long Amount,
    long TotalExperience,
    long CurrentExperience,
    bool Leveled,
    CharacterCombatProfile? Profile);

public sealed partial class SqliteIdentityStore
{
    public async Task<CharacterProgressionGrantOutcome> ApplyCharacterProgressionExperienceAsync(
        long characterId,long amount,string grantKey,CharacterProgressionRules rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rules);
        string key=(grantKey??"").Trim();
        if(characterId<=0||amount<0||amount>CharacterMaximumExperience||
           key.Length is <1 or >128||key.Any(char.IsControl))
            throw new ArgumentOutOfRangeException();

        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();

            long? replayAmount=null;
            long? replayExperience=null;
            using(var replay=connection.CreateCommand())
            {
                replay.Transaction=tx;
                replay.CommandText=@"
SELECT amount,resulting_experience
FROM character_experience_grants
WHERE character_id=$character AND grant_key=$key;";
                replay.Parameters.AddWithValue("$character",characterId);
                replay.Parameters.AddWithValue("$key",key);
                using var reader=replay.ExecuteReader();
                if(reader.Read())
                {
                    replayAmount=reader.GetInt64(0);
                    replayExperience=reader.GetInt64(1);
                }
            }
            if(replayAmount.HasValue)
            {
                var replayProfile=ReadCombatProfile(connection,characterId,tx);
                var replayStatus=replayAmount.Value==amount
                    ?CharacterExperienceGrantStatus.AlreadyProcessed
                    :CharacterExperienceGrantStatus.GrantConflict;
                long durable=ReadExperience(connection,tx,characterId);
                long current=replayProfile is null
                    ?durable
                    :rules.CurrentExperience(replayProfile.Data.Level,durable);
                tx.Commit();
                return new CharacterProgressionGrantOutcome(
                    replayStatus,replayAmount.Value,replayExperience!.Value,current,
                    false,replayProfile);
            }

            if(ScalarLong(connection,
                "SELECT COUNT(*) FROM characters WHERE character_id=$character;",tx,
                ("$character",characterId))!=1)
            {
                tx.Rollback();
                return new CharacterProgressionGrantOutcome(
                    CharacterExperienceGrantStatus.CharacterNotFound,amount,0,0,false,null);
            }

            var before=ReadCombatProfile(connection,characterId,tx)
                ?? throw new InvalidOperationException(
                    "Character progression requires a persisted combat/profile owner");
            if(before.Data.Trust!=CombatProfileTrust.ProfileOnlyVerified &&
               before.Data.Trust!=CombatProfileTrust.Verified)
                throw new InvalidOperationException(
                    "Character progression refuses unverified profile state");
            if(before.Data.Race is <0 or >3)
                throw new InvalidOperationException(
                    "Character progression has no verified rules for this race");

            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            EnsureExperience(connection,tx,characterId,now);
            long currentTotal=ReadExperience(connection,tx,characterId);
            long nextTotal;
            try { nextTotal=checked(currentTotal+amount); }
            catch(OverflowException error)
            {
                throw new InvalidOperationException("Character experience overflow",error);
            }
            if(nextTotal>CharacterMaximumExperience)
                throw new InvalidOperationException(
                    "Character experience exceeds durable cap");

            int oldLevel=before.Data.Level;
            int newLevel=rules.AdvanceLevel(oldLevel,nextTotal);
            int newPoints=checked(before.Data.AvailableAttributePoints+
                rules.AwardedPointsForAdvance(oldLevel,newLevel));
            var afterData=rules.BuildProfile(
                before.Data,newLevel,
                before.Data.Strength,before.Data.Dexterity,
                before.Data.Constitution,before.Data.Wisdom,
                newPoints,refillVitals:newLevel>oldLevel);
            UpdateProgressionProfile(connection,tx,characterId,afterData,now);

            using(var update=connection.CreateCommand())
            {
                update.Transaction=tx;
                update.CommandText=@"
UPDATE character_experience_state
SET experience=$experience,updated_unix_ms=$now
WHERE character_id=$character;";
                update.Parameters.AddWithValue("$experience",nextTotal);
                update.Parameters.AddWithValue("$now",now);
                update.Parameters.AddWithValue("$character",characterId);
                if(update.ExecuteNonQuery()!=1)
                    throw new InvalidOperationException(
                        "Character experience state disappeared during progression grant");
            }

            using(var audit=connection.CreateCommand())
            {
                audit.Transaction=tx;
                audit.CommandText=@"
INSERT INTO character_experience_grants(
    character_id,grant_key,amount,result_status,resulting_experience,created_unix_ms)
VALUES($character,$key,$amount,$status,$experience,$created);";
                audit.Parameters.AddWithValue("$character",characterId);
                audit.Parameters.AddWithValue("$key",key);
                audit.Parameters.AddWithValue("$amount",amount);
                audit.Parameters.AddWithValue(
                    "$status",(int)CharacterExperienceGrantStatus.Applied);
                audit.Parameters.AddWithValue("$experience",nextTotal);
                audit.Parameters.AddWithValue("$created",now);
                audit.ExecuteNonQuery();
            }

            var after=ReadCombatProfile(connection,characterId,tx)
                ?? throw new InvalidOperationException(
                    "Character profile disappeared during progression grant");
            long visibleExperience=rules.CurrentExperience(newLevel,nextTotal);
            tx.Commit();
            return new CharacterProgressionGrantOutcome(
                CharacterExperienceGrantStatus.Applied,amount,nextTotal,visibleExperience,
                newLevel>oldLevel,after);
        },cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterAttributeAllocationOutcome> AllocateCharacterAttributeAsync(
        long characterId,CharacterAttributeKind attribute,CharacterProgressionRules rules,
        CancellationToken cancellationToken=default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rules);
        if(characterId<=0)throw new ArgumentOutOfRangeException(nameof(characterId));
        if(!Enum.IsDefined(attribute))throw new ArgumentOutOfRangeException(nameof(attribute));

        return await Task.Run(() =>
        {
            using var connection=Open();
            using var tx=connection.BeginTransaction();
            var before=ReadCombatProfile(connection,characterId,tx);
            if(before is null)
            {
                tx.Rollback();
                return new CharacterAttributeAllocationOutcome(
                    CharacterAttributeAllocationStatus.CharacterNotFound,null);
            }
            if(before.Data.Trust!=CombatProfileTrust.ProfileOnlyVerified &&
               before.Data.Trust!=CombatProfileTrust.Verified)
            {
                tx.Rollback();
                return new CharacterAttributeAllocationOutcome(
                    CharacterAttributeAllocationStatus.ProfileNotVerified,before);
            }
            if(before.Data.Race is <0 or >3)
            {
                tx.Rollback();
                return new CharacterAttributeAllocationOutcome(
                    CharacterAttributeAllocationStatus.ProfileNotVerified,before);
            }
            if(before.Data.AvailableAttributePoints<=0)
            {
                tx.Rollback();
                return new CharacterAttributeAllocationOutcome(
                    CharacterAttributeAllocationStatus.NoAvailablePoints,before);
            }

            int strength=before.Data.Strength;
            int dexterity=before.Data.Dexterity;
            int constitution=before.Data.Constitution;
            int wisdom=before.Data.Wisdom;
            int current=attribute switch
            {
                CharacterAttributeKind.Strength=>strength,
                CharacterAttributeKind.Dexterity=>dexterity,
                CharacterAttributeKind.Constitution=>constitution,
                CharacterAttributeKind.Wisdom=>wisdom,
                _=>throw new InvalidOperationException()
            };
            if(current>=rules.MaximumAllocatableAttribute)
            {
                tx.Rollback();
                return new CharacterAttributeAllocationOutcome(
                    CharacterAttributeAllocationStatus.AttributeAtCap,before);
            }
            switch(attribute)
            {
                case CharacterAttributeKind.Strength: strength++; break;
                case CharacterAttributeKind.Dexterity: dexterity++; break;
                case CharacterAttributeKind.Constitution: constitution++; break;
                case CharacterAttributeKind.Wisdom: wisdom++; break;
            }

            int remaining=before.Data.AvailableAttributePoints-1;
            var afterData=rules.BuildProfile(
                before.Data,before.Data.Level,
                strength,dexterity,constitution,wisdom,remaining,
                refillVitals:false);
            long now=_timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            UpdateProgressionProfile(connection,tx,characterId,afterData,now);
            var after=ReadCombatProfile(connection,characterId,tx)
                ?? throw new InvalidOperationException(
                    "Character profile disappeared during attribute allocation");
            tx.Commit();
            return new CharacterAttributeAllocationOutcome(
                CharacterAttributeAllocationStatus.Applied,after);
        },cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureExperience(
        SqliteConnection connection,SqliteTransaction tx,long characterId,long now)
    {
        using var ensure=connection.CreateCommand();
        ensure.Transaction=tx;
        ensure.CommandText=@"
INSERT INTO character_experience_state(character_id,experience,updated_unix_ms)
VALUES($character,0,$now)
ON CONFLICT(character_id) DO NOTHING;";
        ensure.Parameters.AddWithValue("$character",characterId);
        ensure.Parameters.AddWithValue("$now",now);
        ensure.ExecuteNonQuery();
    }

    private static long ReadExperience(
        SqliteConnection connection,SqliteTransaction tx,long characterId)
    {
        using var read=connection.CreateCommand();
        read.Transaction=tx;
        read.CommandText=@"
SELECT experience FROM character_experience_state
WHERE character_id=$character;";
        read.Parameters.AddWithValue("$character",characterId);
        object? value=read.ExecuteScalar();
        return value is null or DBNull?0:Convert.ToInt64(value);
    }

    private static void UpdateProgressionProfile(
        SqliteConnection connection,SqliteTransaction tx,long characterId,
        CharacterCombatProfileData profile,long now)
    {
        ValidateCombatProfile(profile);
        using var update=connection.CreateCommand();
        update.Transaction=tx;
        update.CommandText=@"
UPDATE character_combat_profiles
SET trust=$trust,source_ref=$source,
    strength=$str,dexterity=$dex,constitution=$con,wisdom=$wis,
    level=$level,current_hp=$hp,max_hp=$maxhp,current_mp=$mp,max_mp=$maxmp,
    attack=$attack,defense=$defense,hit=$hit,dodge=$dodge,
    attack_range=$range,attack_cooldown_ticks=$cooldown,
    available_attribute_points=$points,updated_unix_ms=$updated
WHERE character_id=$id;";
        BindCombatProfile(update,characterId,profile,now);
        if(update.ExecuteNonQuery()!=1)
            throw new InvalidOperationException(
                "Character profile disappeared during progression update");
    }
}
