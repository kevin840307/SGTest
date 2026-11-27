using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

/// <summary>Persists verified character combat profiles and authoritative HP/MP/WP state.</summary>
public sealed partial class SqliteIdentityStore
{
    public async Task<CharacterCombatProfile?> GetCharacterCombatProfileAsync(
        long characterId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection = Open();
            return ReadCombatProfile(connection, characterId);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Trusted-server provisioning only. No network codec maps client fields into
    /// this method. Normal character creation intentionally leaves this row absent
    /// until source-verified combat identity/rules are available.
    /// </summary>
    public async Task SetCharacterCombatProfileAsync(
        long characterId,
        CharacterCombatProfileData profile,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        ValidateCombatProfile(profile);
        await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            if (ScalarLong(connection,
                    "SELECT COUNT(*) FROM characters WHERE character_id=$id;", tx,
                    ("$id", characterId)) != 1)
                throw new KeyNotFoundException("Unknown character");
            if (ScalarLong(connection,
                    "SELECT COUNT(*) FROM character_appearances WHERE character_id=$id AND race<>$race;",
                    tx, ("$id", characterId), ("$race", profile.Race)) != 0)
                throw new InvalidOperationException("Combat race differs from persisted character appearance");
            if (profile.Level == 1 && ScalarLong(connection,@"
SELECT COUNT(*) FROM character_creation_settings s
WHERE s.character_id=$id AND NOT EXISTS(
    SELECT 1 FROM character_combat_profiles p WHERE p.character_id=s.character_id)
AND (s.strength<>$str OR s.dexterity<>$dex OR s.constitution<>$con OR s.wisdom<>$wis);",tx,
                ("$id",characterId),("$str",profile.Strength),("$dex",profile.Dexterity),
                ("$con",profile.Constitution),("$wis",profile.Wisdom)) != 0)
                throw new InvalidOperationException("Initial combat attributes differ from character creation choices");
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = @"
INSERT INTO character_combat_profiles(
    character_id,trust,source_ref,race,career,strength,dexterity,constitution,wisdom,
    level,current_hp,max_hp,current_mp,max_mp,current_wp,max_wp,attack,defense,hit,dodge,attack_range,
    attack_cooldown_ticks,available_attribute_points,updated_unix_ms)
VALUES($id,$trust,$source,$race,$career,$str,$dex,$con,$wis,$level,$hp,$maxhp,
       $mp,$maxmp,$wp,$maxwp,$attack,$defense,$hit,$dodge,$range,$cooldown,$points,$updated)
ON CONFLICT(character_id) DO UPDATE SET
    trust=excluded.trust,source_ref=excluded.source_ref,race=excluded.race,
    career=excluded.career,strength=excluded.strength,dexterity=excluded.dexterity,
    constitution=excluded.constitution,wisdom=excluded.wisdom,level=excluded.level,
    current_hp=excluded.current_hp,max_hp=excluded.max_hp,
    current_mp=excluded.current_mp,max_mp=excluded.max_mp,
    current_wp=excluded.current_wp,max_wp=excluded.max_wp,
    attack=excluded.attack,defense=excluded.defense,hit=excluded.hit,dodge=excluded.dodge,
    attack_range=excluded.attack_range,
    attack_cooldown_ticks=excluded.attack_cooldown_ticks,
    available_attribute_points=excluded.available_attribute_points,
    updated_unix_ms=excluded.updated_unix_ms;";
            BindCombatProfile(command, characterId, profile,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            tx.Commit();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomic HP settlement primitive for the future authoritative combat owner.
    /// The caller supplies only a server-computed damage value; missing profiles
    /// fail closed instead of inventing HP for ordinary identity-only characters.
    /// </summary>
    public async Task<CharacterCombatProfile?> ApplyCharacterDamageAsync(
        long characterId, int damage, CancellationToken cancellationToken = default)
        => (await ApplyCharacterDamageSettlementAsync(
            characterId, damage, cancellationToken).ConfigureAwait(false))?.Profile;

    public async Task<CharacterDamageSettlement?> ApplyCharacterDamageSettlementAsync(
        long characterId, int damage, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0 || damage <= 0 || damage > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(damage));
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            var before = ReadCombatProfile(connection, characterId, tx);
            if (before is null)
            {
                tx.Rollback();
                return null;
            }
            int hpBefore = before.Data.CurrentHp;
            if (hpBefore > 0)
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = @"
UPDATE character_combat_profiles
   SET current_hp=MAX(0,current_hp-$damage),
       updated_unix_ms=$updated
 WHERE character_id=$id;";
                command.Parameters.AddWithValue("$damage", damage);
                command.Parameters.AddWithValue("$updated",
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$id", characterId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "Combat profile disappeared during atomic damage settlement");
            }
            var after = ReadCombatProfile(connection, characterId, tx)
                ?? throw new InvalidOperationException(
                    "Combat profile disappeared after atomic damage settlement");
            tx.Commit();
            int applied = hpBefore - after.Data.CurrentHp;
            return new CharacterDamageSettlement(
                after, applied, hpBefore > 0 && after.Data.CurrentHp == 0);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Trusted MP settlement primitive. Only the Server chooses the cost.
    /// Missing/dead/insufficient profiles fail closed without mutation.
    /// </summary>
    public async Task<CharacterMpConsumeResult> ConsumeCharacterMpAsync(
        long characterId, int cost,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0 || cost is < 1 or > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(cost));
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            var before = ReadCombatProfile(connection, characterId, tx);
            if (before is null)
            {
                tx.Rollback();
                return new CharacterMpConsumeResult(CharacterMpConsumeStatus.MissingProfile, null, 0);
            }
            if (before.Data.CurrentHp <= 0)
            {
                tx.Rollback();
                return new CharacterMpConsumeResult(CharacterMpConsumeStatus.Dead, before, 0);
            }
            if (before.Data.CurrentMp < cost)
            {
                tx.Rollback();
                return new CharacterMpConsumeResult(CharacterMpConsumeStatus.InsufficientMp, before, 0);
            }
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = @"
UPDATE character_combat_profiles
   SET current_mp=current_mp-$cost,
       updated_unix_ms=$updated
 WHERE character_id=$id AND current_hp>0 AND current_mp>=$cost;";
            command.Parameters.AddWithValue("$cost", cost);
            command.Parameters.AddWithValue("$updated",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$id", characterId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "Combat profile diverged during atomic MP settlement");
            var after = ReadCombatProfile(connection, characterId, tx)
                ?? throw new InvalidOperationException(
                    "Combat profile disappeared after atomic MP settlement");
            if (before.Data.CurrentMp - after.Data.CurrentMp != cost ||
                after.Data.MaxMp != before.Data.MaxMp)
                throw new InvalidOperationException(
                    "Atomic MP settlement changed an unexpected resource value");
            tx.Commit();
            return new CharacterMpConsumeResult(CharacterMpConsumeStatus.Consumed, after, cost);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Trusted combat respawn primitive. Only a dead persisted profile may
    /// transition from HP=0 to its existing MaxHP. No client field can choose
    /// the restored HP value; missing/already-alive profiles fail closed.
    /// </summary>
    public async Task<CharacterCombatProfile?> RestoreCharacterFullHpAsync(
        long characterId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            var before = ReadCombatProfile(connection, characterId, tx);
            if (before is null || before.Data.CurrentHp != 0)
            {
                tx.Rollback();
                return null;
            }
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = @"
UPDATE character_combat_profiles
   SET current_hp=max_hp,
       updated_unix_ms=$updated
 WHERE character_id=$id AND current_hp=0;";
            command.Parameters.AddWithValue("$updated",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$id", characterId);
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "Dead combat profile disappeared during atomic respawn");
            var after = ReadCombatProfile(connection, characterId, tx)
                ?? throw new InvalidOperationException(
                    "Combat profile disappeared after atomic respawn");
            if (after.Data.CurrentHp != after.Data.MaxHp || after.Data.CurrentHp <= 0)
                throw new InvalidOperationException(
                    "Atomic respawn did not restore authoritative MaxHP");
            tx.Commit();
            return after;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateCombatProfile(CharacterCombatProfileData profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool profileOnly = profile.Trust == CombatProfileTrust.ProfileOnlyVerified;
        bool combatEnabled = profile.Trust is CombatProfileTrust.Verified or CombatProfileTrust.SyntheticTest;
        if ((!profileOnly && !combatEnabled) ||
            string.IsNullOrWhiteSpace(profile.SourceReference) ||
            profile.SourceReference.Length > 256 ||
            profile.SourceReference.Any(char.IsControl) ||
            profile.Race is < 0 or > 255 ||
            profile.Career is < 0 or > 65535 ||
            profile.Strength is < 0 or > 1_000_000 ||
            profile.Dexterity is < 0 or > 1_000_000 ||
            profile.Constitution is < 0 or > 1_000_000 ||
            profile.Wisdom is < 0 or > 1_000_000 ||
            profile.Level is < 1 or > 255 ||
            profile.CurrentHp < 0 || profile.MaxHp < 1 ||
            profile.CurrentHp > profile.MaxHp || profile.MaxHp > 2_000_000_000 ||
            profile.CurrentMp < 0 || profile.MaxMp < 0 ||
            profile.CurrentMp > profile.MaxMp || profile.MaxMp > 2_000_000_000 ||
            profile.CurrentWp < 0 || profile.MaxWp < 0 ||
            profile.CurrentWp > profile.MaxWp || profile.MaxWp > 2_000_000_000 ||
            profile.Attack is < 0 or > 100_000_000 ||
            profile.Defense is < 0 or > 100_000_000 ||
            profile.Hit is < 0 or > 100_000_000 ||
            profile.Dodge is < 0 or > 100_000_000 ||
            !double.IsFinite(profile.AttackRange) ||
            profile.AttackRange is < 0 or > 4096 ||
            profile.AttackCooldownTicks is < 0 or > 3600 ||
            profile.AvailableAttributePoints is < 0 or > 1_000_000 ||
            (profileOnly && (profile.AttackRange != 0 || profile.AttackCooldownTicks != 0)) ||
            (combatEnabled && (profile.AttackRange <= 0 || profile.AttackCooldownTicks < 1)))
            throw new ArgumentOutOfRangeException(nameof(profile));
    }

    private static void InsertInitialCombatProfile(
        SqliteConnection connection,
        SqliteTransaction tx,
        long characterId,
        CharacterCombatProfileData profile,
        long updated)
    {
        ValidateCombatProfile(profile);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"INSERT INTO character_combat_profiles(
    character_id,trust,source_ref,race,career,strength,dexterity,constitution,wisdom,
    level,current_hp,max_hp,current_mp,max_mp,current_wp,max_wp,attack,defense,hit,dodge,attack_range,
    attack_cooldown_ticks,available_attribute_points,updated_unix_ms)
VALUES($id,$trust,$source,$race,$career,$str,$dex,$con,$wis,$level,$hp,$maxhp,
       $mp,$maxmp,$wp,$maxwp,$attack,$defense,$hit,$dodge,$range,$cooldown,$points,$updated);";
        BindCombatProfile(command, characterId, profile, updated);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Initial combat profile creation failed");
    }

    private static void BindCombatProfile(SqliteCommand command, long characterId,
        CharacterCombatProfileData profile, long updated)
    {
        command.Parameters.AddWithValue("$id", characterId);
        command.Parameters.AddWithValue("$trust", (int)profile.Trust);
        command.Parameters.AddWithValue("$source", profile.SourceReference);
        command.Parameters.AddWithValue("$race", profile.Race);
        command.Parameters.AddWithValue("$career", profile.Career);
        command.Parameters.AddWithValue("$str", profile.Strength);
        command.Parameters.AddWithValue("$dex", profile.Dexterity);
        command.Parameters.AddWithValue("$con", profile.Constitution);
        command.Parameters.AddWithValue("$wis", profile.Wisdom);
        command.Parameters.AddWithValue("$level", profile.Level);
        command.Parameters.AddWithValue("$hp", profile.CurrentHp);
        command.Parameters.AddWithValue("$maxhp", profile.MaxHp);
        command.Parameters.AddWithValue("$mp", profile.CurrentMp);
        command.Parameters.AddWithValue("$maxmp", profile.MaxMp);
        command.Parameters.AddWithValue("$wp", profile.CurrentWp);
        command.Parameters.AddWithValue("$maxwp", profile.MaxWp);
        command.Parameters.AddWithValue("$attack", profile.Attack);
        command.Parameters.AddWithValue("$defense", profile.Defense);
        command.Parameters.AddWithValue("$hit", profile.Hit);
        command.Parameters.AddWithValue("$dodge", profile.Dodge);
        command.Parameters.AddWithValue("$range", profile.AttackRange);
        command.Parameters.AddWithValue("$cooldown", profile.AttackCooldownTicks);
        command.Parameters.AddWithValue("$points", profile.AvailableAttributePoints);
        command.Parameters.AddWithValue("$updated", updated);
    }

    private static CharacterCombatProfile? ReadCombatProfile(
        SqliteConnection connection, long characterId, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
SELECT character_id,trust,source_ref,race,career,strength,dexterity,constitution,
       wisdom,level,current_hp,max_hp,current_mp,max_mp,current_wp,max_wp,attack,defense,hit,dodge,attack_range,
       attack_cooldown_ticks,available_attribute_points,updated_unix_ms
  FROM character_combat_profiles
 WHERE character_id=$id
 LIMIT 1;";
        command.Parameters.AddWithValue("$id", characterId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var data = new CharacterCombatProfileData(
            (CombatProfileTrust)reader.GetInt32(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            reader.GetInt32(11),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetInt32(18),
            reader.GetInt32(19),
            reader.GetDouble(20),
            reader.GetInt32(21),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt32(22),
            reader.GetInt32(14),
            reader.GetInt32(15));
        return new CharacterCombatProfile(
            reader.GetInt64(0), data, reader.GetInt64(23));
    }

}
