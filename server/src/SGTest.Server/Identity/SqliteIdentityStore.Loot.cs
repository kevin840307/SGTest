using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public enum LootSourceTrust : byte { Verified = 1, SyntheticTest = 2 }

/// <summary>Only trusted server code may supply this, never client gameplay bytes.
/// Monster death revision must be the authoritative, monotonic revision of the kill.</summary>
public readonly record struct TrustedDropSpawn(
    int MapId, double X, double Y, long MonsterEntityId, long DeathRevision,
    int DropSlot, int ItemDefinitionId, int Quantity,
    LootSourceTrust Trust, string SourceReference);

public sealed record GroundDropRecord(
    long DropId, int MapId, double X, double Y, long MonsterEntityId,
    long DeathRevision, int DropSlot, int ItemDefinitionId, int Quantity,
    LootSourceTrust Trust, string SourceReference, long? PickedByCharacterId);

/// <summary>To be built by the authenticated World/Gateway owner from trusted
/// actor position and generation. The raw client must never set this context.</summary>
public readonly record struct TrustedPickupContext(
    long CharacterId, long ConnectionEpoch, long MapEpoch, uint ActionSequence,
    int MapId, double X, double Y);

public enum PickupStatus : byte
{
    Granted, AlreadyGranted, Missing, AlreadyTaken, WrongMap, TooFar,
    StaleSequence, UnknownCharacter, StaleGeneration, PlayerDead
}

public sealed record PickupResult(PickupStatus Status, long DropId,
    long ItemUid = 0, int ItemDefinitionId = 0, int Quantity = 0);

public sealed record InventoryItem(long ItemUid, long CharacterId,
    int ItemDefinitionId, int Quantity, long SourceDropId);

/// <summary>
/// Durable loot primitives owned by the existing identity SQLite DB, not a
/// second datastore. The DB transaction atomically claims one ground UID and
/// creates exactly one inventory UID. No DB call is allowed on the World tick.
/// </summary>
public sealed partial class SqliteIdentityStore
{
    /// <summary>Reserve once per new authoritative Server boot, outside World tick.
    /// Reboot or parallel processes never reuse committed generation IDs.</summary>
    public async Task<long> AllocateLootWorldGenerationAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "INSERT INTO loot_world_generations(started_unix_ms) VALUES($now);";
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Failed to reserve durable World generation");
            long generation = ScalarLong(connection, "SELECT last_insert_rowid();", tx);
            if (generation is < 1 or > 0x3fffffff)
                throw new InvalidOperationException("Loot world generation exhausted");
            tx.Commit();
            return generation;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Compose a durable death ID from a database-allocated boot
    /// generation plus the MonsterRuntime's per-boot monotonically increasing revision.</summary>
    public static long ComposeLootDeathRevision(long worldGeneration, long monsterRevision)
    {
        if (worldGeneration is < 1 or > 0x3fffffff ||
            monsterRevision is < 1 or > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(monsterRevision));
        return (worldGeneration << 32) | monsterRevision;
    }

    /// <summary>Combines a durable SQLite boot generation and trusted World
    /// connection epoch. Client request sequences may reset safely on restart.</summary>
    public static long ComposePickupConnectionEpoch(
        long worldGeneration,long connectionEpoch)
    {
        if (worldGeneration is < 1 or > 0x3fffffff ||
            connectionEpoch is < 1 or > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(connectionEpoch));
        return (worldGeneration << 32) | connectionEpoch;
    }

    // Provisional server-side pickup radius; the original game's rule is not verified.
    public const double PickupRange = 96.0;

    public async Task<GroundDropRecord> CreateTrustedGroundDropAsync(
        TrustedDropSpawn drop, CancellationToken cancellationToken = default)
    {
        var rows = await CreateTrustedGroundDropsAsync([drop], cancellationToken)
            .ConfigureAwait(false);
        return rows[0];
    }

    /// <summary>
    /// One monster death can create multiple independent drop UIDs. All slots
    /// are inserted/validated in one SQLite transaction: a duplicate conflicting
    /// slot rejects the ENTIRE batch, including previously unseen slots.
    /// Server trust & provenance remain mandatory; never call on the World tick.
    /// </summary>
    public async Task<IReadOnlyList<GroundDropRecord>> CreateTrustedGroundDropsAsync(
        IReadOnlyList<TrustedDropSpawn> drops,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(drops);
        if (drops.Count is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(drops));
        var first = drops[0];
        var slots = new HashSet<int>();
        foreach (var drop in drops)
        {
            ValidateDrop(drop);
            if (drop.MapId != first.MapId ||
                drop.MonsterEntityId != first.MonsterEntityId ||
                drop.DeathRevision != first.DeathRevision ||
                !slots.Add(drop.DropSlot))
                throw new ArgumentException(
                    "One atomic drop batch must identify one death and unique slots",
                    nameof(drops));
        }

        return await Task.Run<IReadOnlyList<GroundDropRecord>>(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            long generation = first.DeathRevision >> 32;
            if (ScalarLong(connection,
                "SELECT COUNT(*) FROM loot_world_generations WHERE generation_id=$id;", tx,
                ("$id", generation)) != 1)
                throw new InvalidOperationException(
                    "Drop batch references unknown trusted World generation");
            var result = new List<GroundDropRecord>(drops.Count);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var drop in drops)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = tx;
                    command.CommandText = @"
INSERT INTO ground_drops(map_id,x,y,monster_entity_id,death_revision,
    drop_slot,item_definition_id,quantity,source_trust,source_reference,created_unix_ms)
VALUES($map,$x,$y,$monster,$revision,$slot,$item,$quantity,$trust,$source,$now)
ON CONFLICT(map_id,monster_entity_id,death_revision,drop_slot) DO NOTHING;";
                    command.Parameters.AddWithValue("$map", drop.MapId);
                    command.Parameters.AddWithValue("$x", drop.X);
                    command.Parameters.AddWithValue("$y", drop.Y);
                    command.Parameters.AddWithValue("$monster", drop.MonsterEntityId);
                    command.Parameters.AddWithValue("$revision", drop.DeathRevision);
                    command.Parameters.AddWithValue("$slot", drop.DropSlot);
                    command.Parameters.AddWithValue("$item", drop.ItemDefinitionId);
                    command.Parameters.AddWithValue("$quantity", drop.Quantity);
                    command.Parameters.AddWithValue("$trust", (int)drop.Trust);
                    command.Parameters.AddWithValue("$source", drop.SourceReference);
                    command.Parameters.AddWithValue("$now", now);
                    command.ExecuteNonQuery();
                }
                long id = ScalarLong(connection, @"
SELECT drop_id FROM ground_drops
 WHERE map_id=$map AND monster_entity_id=$monster
   AND death_revision=$revision AND drop_slot=$slot;", tx,
                    ("$map", drop.MapId), ("$monster", drop.MonsterEntityId),
                    ("$revision", drop.DeathRevision), ("$slot", drop.DropSlot));
                var saved = ReadDrop(connection, tx, id)
                    ?? throw new InvalidOperationException(
                        "Trusted drop batch insertion vanished");
                if (saved.X != drop.X || saved.Y != drop.Y ||
                    saved.ItemDefinitionId != drop.ItemDefinitionId ||
                    saved.Quantity != drop.Quantity || saved.Trust != drop.Trust ||
                    saved.SourceReference != drop.SourceReference)
                    throw new InvalidOperationException(
                        "Conflicting trusted death slot must not alter existing loot");
                result.Add(saved);
            }
            tx.Commit();
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GroundDropRecord>> ListActiveGroundDropsAsync(
        int mapId, int limit = 1000, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (mapId is < 1 or > 65535 || limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(mapId));
        return await Task.Run<IReadOnlyList<GroundDropRecord>>(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT drop_id,map_id,x,y,monster_entity_id,
death_revision,drop_slot,item_definition_id,quantity,source_trust,source_reference,
picked_by_character_id FROM ground_drops
WHERE map_id=$map AND picked_by_character_id IS NULL ORDER BY drop_id LIMIT $limit;";
            command.Parameters.AddWithValue("$map", mapId);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<GroundDropRecord>();
            while (reader.Read()) result.Add(ReadDrop(reader));
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PickupResult> ClaimGroundDropAsync(
        TrustedPickupContext actor, long dropId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (actor.CharacterId <= 0 || actor.ConnectionEpoch <= 0 ||
            actor.MapEpoch <= 0 || actor.ActionSequence == 0 ||
            actor.MapId is < 1 or > 65535 || !double.IsFinite(actor.X) ||
            !double.IsFinite(actor.Y) || actor.X < 0 || actor.Y < 0 || dropId <= 0)
            throw new ArgumentOutOfRangeException(nameof(actor));
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            if (ScalarLong(connection,
                "SELECT COUNT(*) FROM characters WHERE character_id=$id;", tx,
                ("$id", actor.CharacterId)) != 1)
                return new PickupResult(PickupStatus.UnknownCharacter, dropId);

            // Any previously committed action with the same authenticated
            // generation+sequence is immutable, including attempts on other UIDs.
            long last = ScalarLong(connection, @"
SELECT COALESCE(MAX(pickup_sequence),0) FROM ground_drops
WHERE picked_by_character_id=$actor AND pickup_connection_epoch=$conn
  AND pickup_map_epoch=$epoch;", tx,
                ("$actor", actor.CharacterId), ("$conn", actor.ConnectionEpoch),
                ("$epoch", actor.MapEpoch));
            if (last >= actor.ActionSequence)
            {
                using var old = connection.CreateCommand();
                old.Transaction = tx;
                old.CommandText = @"
SELECT d.drop_id, i.item_uid, d.item_definition_id, d.quantity
FROM ground_drops d JOIN item_instances i ON i.source_drop_id=d.drop_id
WHERE d.picked_by_character_id=$actor AND d.pickup_connection_epoch=$conn
  AND d.pickup_map_epoch=$epoch AND d.pickup_sequence=$seq;";
                old.Parameters.AddWithValue("$actor", actor.CharacterId);
                old.Parameters.AddWithValue("$conn", actor.ConnectionEpoch);
                old.Parameters.AddWithValue("$epoch", actor.MapEpoch);
                old.Parameters.AddWithValue("$seq", (long)actor.ActionSequence);
                using var reader = old.ExecuteReader();
                if (reader.Read())
                {
                    long originalId = reader.GetInt64(0);
                    if (originalId == dropId)
                        return new PickupResult(PickupStatus.AlreadyGranted, dropId,
                            reader.GetInt64(1), reader.GetInt32(2), reader.GetInt32(3));
                }
                return new PickupResult(PickupStatus.StaleSequence, dropId);
            }

            var drop = ReadDrop(connection, tx, dropId);
            if (drop is null) return new PickupResult(PickupStatus.Missing, dropId);
            if (drop.PickedByCharacterId is not null)
                return new PickupResult(PickupStatus.AlreadyTaken, dropId);
            if (drop.MapId != actor.MapId)
                return new PickupResult(PickupStatus.WrongMap, dropId);
            double dx = drop.X - actor.X, dy = drop.Y - actor.Y;
            if (dx * dx + dy * dy > PickupRange * PickupRange)
                return new PickupResult(PickupStatus.TooFar, dropId);
            using (var update = connection.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = @"
UPDATE ground_drops SET picked_by_character_id=$actor,
 pickup_connection_epoch=$conn,pickup_map_epoch=$epoch,pickup_sequence=$seq
WHERE drop_id=$drop AND picked_by_character_id IS NULL;";
                update.Parameters.AddWithValue("$actor", actor.CharacterId);
                update.Parameters.AddWithValue("$conn", actor.ConnectionEpoch);
                update.Parameters.AddWithValue("$epoch", actor.MapEpoch);
                update.Parameters.AddWithValue("$seq", (long)actor.ActionSequence);
                update.Parameters.AddWithValue("$drop", dropId);
                if (update.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Ground drop claim lost atomic ownership");
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"
INSERT INTO item_instances(
    character_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
VALUES($actor,$item,$quantity,$drop,1,$source,$now);";
            insert.Parameters.AddWithValue("$actor", actor.CharacterId);
            insert.Parameters.AddWithValue("$item", drop.ItemDefinitionId);
            insert.Parameters.AddWithValue("$quantity", drop.Quantity);
            insert.Parameters.AddWithValue("$drop", dropId);
            insert.Parameters.AddWithValue("$source", "drop:" + dropId);
            insert.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Inventory grant failed");
            long itemUid = ScalarLong(connection, "SELECT last_insert_rowid();", tx);
            tx.Commit();
            return new PickupResult(PickupStatus.Granted, dropId, itemUid,
                drop.ItemDefinitionId, drop.Quantity);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InventoryItem>> ListInventoryItemsAsync(
        long characterId, int limit = 1000, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (characterId <= 0 || limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(characterId));
        return await Task.Run<IReadOnlyList<InventoryItem>>(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT item_uid,character_id,item_definition_id,
quantity,COALESCE(source_drop_id,0) FROM item_instances
WHERE character_id=$id ORDER BY item_uid LIMIT $limit;";
            command.Parameters.AddWithValue("$id", characterId);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<InventoryItem>();
            while (reader.Read())
                result.Add(new(reader.GetInt64(0),reader.GetInt64(1),
                    reader.GetInt32(2),reader.GetInt32(3),reader.GetInt64(4)));
            return result;
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDrop(TrustedDropSpawn drop)
    {
        if (drop.MapId is < 1 or > 65535 || !double.IsFinite(drop.X) ||
            !double.IsFinite(drop.Y) || drop.X < 0 || drop.Y < 0 ||
            drop.MonsterEntityId <= 0 || drop.DeathRevision < (1L << 32) ||
            drop.DropSlot is < 0 or > 31 ||
            drop.ItemDefinitionId is < 1 or > 100_000_000 ||
            drop.Quantity is < 1 or > 1_000_000 ||
            drop.Trust is not (LootSourceTrust.Verified or LootSourceTrust.SyntheticTest) ||
            string.IsNullOrWhiteSpace(drop.SourceReference) ||
            drop.SourceReference.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(drop));
    }

    private static GroundDropRecord? ReadDrop(
        SqliteConnection connection, SqliteTransaction transaction, long dropId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"SELECT drop_id,map_id,x,y,monster_entity_id,
death_revision,drop_slot,item_definition_id,quantity,source_trust,source_reference,
picked_by_character_id FROM ground_drops WHERE drop_id=$id;";
        command.Parameters.AddWithValue("$id", dropId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadDrop(reader) : null;
    }

    private static GroundDropRecord ReadDrop(SqliteDataReader reader) =>
        new(reader.GetInt64(0),reader.GetInt32(1),reader.GetDouble(2),
            reader.GetDouble(3),reader.GetInt64(4),reader.GetInt64(5),
            reader.GetInt32(6),reader.GetInt32(7),reader.GetInt32(8),
            (LootSourceTrust)reader.GetInt32(9),reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetInt64(11));
}
