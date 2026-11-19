using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

public sealed record IdentityStoreOptions(int MaxCharactersPerAccount = 8, int BusyTimeoutSeconds = 5);
public readonly record struct CharacterSpawn(int MapId, double X, double Y);
public readonly record struct CharacterPositionUpdate(long CharacterId, CharacterSpawn Spawn);
public sealed record CharacterRecord(long CharacterId, long AccountId, int Slot, string Name,
    int MapId, double X, double Y, long CreatedUnixMilliseconds);

public enum CombatProfileTrust : byte
{
    Verified = 1,
    SyntheticTest = 2
}

public sealed record CharacterCombatProfileData(
    CombatProfileTrust Trust,
    string SourceReference,
    int Race,
    int Career,
    int Strength,
    int Dexterity,
    int Constitution,
    int Wisdom,
    int Level,
    int CurrentHp,
    int MaxHp,
    int Attack,
    int Defense,
    int Hit,
    int Dodge,
    double AttackRange,
    int AttackCooldownTicks,
    int CurrentMp = 0,
    int MaxMp = 0);

public sealed record CharacterCombatProfile(
    long CharacterId,
    CharacterCombatProfileData Data,
    long UpdatedUnixMilliseconds);

public sealed record CharacterDamageSettlement(
    CharacterCombatProfile Profile,
    int DamageApplied,
    bool Died);

public enum CharacterMpConsumeStatus : byte
{
    Consumed = 0,
    MissingProfile = 1,
    Dead = 2,
    InsufficientMp = 3
}

public sealed record CharacterMpConsumeResult(
    CharacterMpConsumeStatus Status,
    CharacterCombatProfile? Profile,
    int MpConsumed);

public sealed record AccountRecord(long AccountId, string Username, bool Disabled,
    IReadOnlyList<CharacterRecord> Characters);

public enum AccountCreateStatus { Created, DuplicateUsername }
public sealed record AccountCreateResult(AccountCreateStatus Status, long AccountId = 0);
public enum AuthenticationStatus { Success, InvalidCredentials, Disabled }
public sealed record AuthenticationResult(AuthenticationStatus Status, AccountRecord? Account = null);
public enum CharacterCreateStatus { Created, UnknownAccount, SlotOccupied, NameTaken, LimitReached }
public sealed record CharacterCreateResult(CharacterCreateStatus Status, CharacterRecord? Character = null);

/// <summary>
/// SQLite identity persistence. This service is intentionally outside World/Tick;
/// callers must finish Account/Auth/CharacterChoose before TrustedRegisterAsync.
/// No network listener or client-controlled ActorId is exposed here.
/// </summary>
public sealed partial class SqliteIdentityStore : IDisposable
{
    public const int SchemaVersion = 14;
    private readonly string _connectionString;
    private readonly IdentityStoreOptions _options;
    private readonly PasswordHasher _passwords;
    private readonly bool _ownsPasswords;
    private readonly string _dummyHash;
    private bool _disposed;

    public SqliteIdentityStore(string databasePath, PasswordHasher? passwords = null,
        IdentityStoreOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Database path required", nameof(databasePath));
        _options = options ?? new IdentityStoreOptions();
        if (_options.MaxCharactersPerAccount is < 1 or > 32 || _options.BusyTimeoutSeconds is < 1 or > 30)
            throw new ArgumentOutOfRangeException(nameof(options));
        _ownsPasswords = passwords is null;
        _passwords = passwords ?? new PasswordHasher();
        _dummyHash = _passwords.CreateDeterministicDummyHash();
        string full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            // Identity traffic is not a World/Tick hot path. Disabling pooling keeps
            // DB file ownership deterministic across shutdown/restart and tests.
            Pooling = false,
            DefaultTimeout = _options.BusyTimeoutSeconds
        };
        _connectionString = builder.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.Run(() =>
        {
            using var connection = Open();
            Execute(connection, "PRAGMA journal_mode=WAL;");
            Execute(connection, "PRAGMA synchronous=NORMAL;");
            Execute(connection, $"PRAGMA busy_timeout={_options.BusyTimeoutSeconds * 1000};");
            using var tx = connection.BeginTransaction();
            Execute(connection, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);", tx);
            long count = ScalarLong(connection, "SELECT COUNT(*) FROM schema_version;", tx);
            long schemaVersion;
            if (count == 0)
            {
                Execute(connection, $"INSERT INTO schema_version(version) VALUES ({SchemaVersion});", tx);
                schemaVersion = SchemaVersion;
            }
            else
            {
                if (count != 1)
                    throw new InvalidDataException("Unsupported identity schema version");
                schemaVersion = ScalarLong(connection,
                    "SELECT version FROM schema_version LIMIT 1;", tx);
                if (schemaVersion is < 1 or > SchemaVersion)
                    throw new InvalidDataException("Unsupported identity schema version");
            }

            Execute(connection, @"
CREATE TABLE IF NOT EXISTS accounts (
    account_id INTEGER PRIMARY KEY AUTOINCREMENT,
    username_key TEXT NOT NULL UNIQUE,
    username_display TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    disabled INTEGER NOT NULL DEFAULT 0 CHECK(disabled IN (0,1)),
    created_unix_ms INTEGER NOT NULL
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS characters (
    character_id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL REFERENCES accounts(account_id) ON DELETE CASCADE,
    slot INTEGER NOT NULL CHECK(slot BETWEEN 0 AND 31),
    name TEXT NOT NULL,
    name_key TEXT NOT NULL UNIQUE,
    map_id INTEGER NOT NULL CHECK(map_id BETWEEN 1 AND 65535),
    x REAL NOT NULL,
    y REAL NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    updated_unix_ms INTEGER NOT NULL,
    UNIQUE(account_id, slot)
);", tx);
            Execute(connection, "CREATE INDEX IF NOT EXISTS ix_characters_account ON characters(account_id, slot);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS character_combat_profiles (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    trust INTEGER NOT NULL CHECK(trust IN (1,2)),
    source_ref TEXT NOT NULL,
    race INTEGER NOT NULL CHECK(race BETWEEN 0 AND 255),
    career INTEGER NOT NULL CHECK(career BETWEEN 0 AND 65535),
    strength INTEGER NOT NULL CHECK(strength BETWEEN 0 AND 1000000),
    dexterity INTEGER NOT NULL CHECK(dexterity BETWEEN 0 AND 1000000),
    constitution INTEGER NOT NULL CHECK(constitution BETWEEN 0 AND 1000000),
    wisdom INTEGER NOT NULL CHECK(wisdom BETWEEN 0 AND 1000000),
    level INTEGER NOT NULL CHECK(level BETWEEN 1 AND 255),
    current_hp INTEGER NOT NULL CHECK(current_hp BETWEEN 0 AND 2000000000),
    max_hp INTEGER NOT NULL CHECK(max_hp BETWEEN 1 AND 2000000000),
    current_mp INTEGER NOT NULL DEFAULT 0 CHECK(current_mp BETWEEN 0 AND 2000000000),
    max_mp INTEGER NOT NULL DEFAULT 0 CHECK(max_mp BETWEEN 0 AND 2000000000),
    attack INTEGER NOT NULL CHECK(attack BETWEEN 0 AND 100000000),
    defense INTEGER NOT NULL CHECK(defense BETWEEN 0 AND 100000000),
    hit INTEGER NOT NULL CHECK(hit BETWEEN 0 AND 100000000),
    dodge INTEGER NOT NULL CHECK(dodge BETWEEN 0 AND 100000000),
    attack_range REAL NOT NULL CHECK(attack_range > 0 AND attack_range <= 4096),
    attack_cooldown_ticks INTEGER NOT NULL CHECK(attack_cooldown_ticks BETWEEN 1 AND 3600),
    updated_unix_ms INTEGER NOT NULL,
    CHECK(current_hp <= max_hp),
    CHECK(current_mp <= max_mp)
);", tx);

            // Reserve a durable generation at each new authoritative server boot.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS loot_world_generations (
    generation_id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_unix_ms INTEGER NOT NULL
);", tx);

            // v3: authoritative ground-drop identity and one-time inventory grants.
            // The unique kill/slot key makes a trusted death retry idempotent.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS ground_drops (
    drop_id INTEGER PRIMARY KEY AUTOINCREMENT,
    map_id INTEGER NOT NULL CHECK(map_id BETWEEN 1 AND 65535),
    x REAL NOT NULL CHECK(x >= 0),
    y REAL NOT NULL CHECK(y >= 0),
    monster_entity_id INTEGER NOT NULL CHECK(monster_entity_id > 0),
    death_revision INTEGER NOT NULL CHECK(death_revision > 0),
    drop_slot INTEGER NOT NULL CHECK(drop_slot BETWEEN 0 AND 31),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_trust INTEGER NOT NULL CHECK(source_trust IN (1,2)),
    source_reference TEXT NOT NULL,
    picked_by_character_id INTEGER REFERENCES characters(character_id),
    pickup_connection_epoch INTEGER,
    pickup_map_epoch INTEGER,
    pickup_sequence INTEGER,
    created_unix_ms INTEGER NOT NULL,
    UNIQUE(map_id, monster_entity_id, death_revision, drop_slot),
    CHECK(
      (picked_by_character_id IS NULL AND pickup_connection_epoch IS NULL
       AND pickup_map_epoch IS NULL AND pickup_sequence IS NULL)
      OR
      (picked_by_character_id IS NOT NULL
       AND pickup_connection_epoch IS NOT NULL AND pickup_connection_epoch > 0
       AND pickup_map_epoch IS NOT NULL AND pickup_map_epoch > 0
       AND pickup_sequence IS NOT NULL AND pickup_sequence BETWEEN 1 AND 4294967295)
    )
);", tx);
            Execute(connection, @"
CREATE UNIQUE INDEX IF NOT EXISTS ux_ground_drops_pickup_action
ON ground_drops(picked_by_character_id, pickup_connection_epoch, pickup_map_epoch, pickup_sequence)
WHERE picked_by_character_id IS NOT NULL;", tx);
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_ground_drops_visible
ON ground_drops(map_id, drop_id) WHERE picked_by_character_id IS NULL;", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS item_instances (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER UNIQUE REFERENCES ground_drops(drop_id),
    durability_raw INTEGER CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647),
    source_kind INTEGER NOT NULL DEFAULT 1 CHECK(source_kind IN (1,2)),
    source_reference TEXT NOT NULL DEFAULT '',
    created_unix_ms INTEGER NOT NULL,
    CHECK(
      (source_kind=1 AND source_drop_id IS NOT NULL)
      OR (source_kind=2 AND source_drop_id IS NULL)
    )
);", tx);
            // Schema v4: replay receipts survive consumption of the last
            // quantity, while any inventory decrement and combat HP restore
            // share one SQLite transaction.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS recovery_use_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    remaining_quantity INTEGER NOT NULL CHECK(remaining_quantity BETWEEN 0 AND 1000000),
    previous_hp INTEGER NOT NULL CHECK(previous_hp > 0),
    current_hp INTEGER NOT NULL CHECK(current_hp > 0),
    max_hp INTEGER NOT NULL CHECK(max_hp >= current_hp),
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            // Schema v5: durable inventory discard receipts remain after the
            // final stack quantity is deleted, so transport retry cannot
            // destroy another quantity or apply the same action twice.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS inventory_discard_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    discarded_quantity INTEGER NOT NULL CHECK(discarded_quantity BETWEEN 1 AND 1000000),
    remaining_quantity INTEGER NOT NULL CHECK(remaining_quantity BETWEEN 0 AND 1000000),
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            // Schema v6: MP joins the trusted combat profile. Existing v1-v5
            // profiles migrate to 0/0 so MP skills fail closed rather than
            // inventing a resource pool.
            if (schemaVersion < 6)
            {
                // A v1 database reaches this point after the current CREATE TABLE
                // statement has already materialized both MP columns. A real v5
                // database has the old table and needs ALTER. Inspect the actual
                // schema instead of assuming version alone describes table shape.
                if (!ColumnExists(connection, "character_combat_profiles", "current_mp", tx))
                    Execute(connection, @"
ALTER TABLE character_combat_profiles
ADD COLUMN current_mp INTEGER NOT NULL DEFAULT 0
CHECK(current_mp BETWEEN 0 AND 2000000000);", tx);
                if (!ColumnExists(connection, "character_combat_profiles", "max_mp", tx))
                    Execute(connection, @"
ALTER TABLE character_combat_profiles
ADD COLUMN max_mp INTEGER NOT NULL DEFAULT 0
CHECK(max_mp BETWEEN 0 AND 2000000000);", tx);
            }
            // Schema v7: durable successful skill-MP settlements. A replay of
            // one authenticated skill generation/sequence returns the original
            // receipt and must never spend MP twice after retry/reconnect.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS skill_mp_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    skill_sequence INTEGER NOT NULL CHECK(skill_sequence BETWEEN 1 AND 4294967295),
    skill_id INTEGER NOT NULL CHECK(skill_id BETWEEN 1 AND 100000000),
    mp_cost INTEGER NOT NULL CHECK(mp_cost BETWEEN 1 AND 100000000),
    previous_mp INTEGER NOT NULL CHECK(previous_mp >= 0),
    current_mp INTEGER NOT NULL CHECK(current_mp >= 0),
    max_mp INTEGER NOT NULL CHECK(max_mp >= current_mp),
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,skill_sequence),
    CHECK(previous_mp - current_mp = mp_cost)
);", tx);
            // Schema v8: one durable inventory table now supports both
            // monster-drop grants and direct shop purchases. Older rows retain
            // their exact item_uid and source_drop_id; shop rows have a null
            // source_drop_id plus explicit trusted provenance.
            if (schemaVersion < 8)
            {
                Execute(connection, "ALTER TABLE item_instances RENAME TO item_instances_v7;", tx);
                Execute(connection, @"
CREATE TABLE item_instances (
    item_uid INTEGER PRIMARY KEY AUTOINCREMENT,
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER UNIQUE REFERENCES ground_drops(drop_id),
    durability_raw INTEGER CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647),
    source_kind INTEGER NOT NULL CHECK(source_kind IN (1,2)),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    CHECK(
      (source_kind=1 AND source_drop_id IS NOT NULL)
      OR (source_kind=2 AND source_drop_id IS NULL)
    )
);", tx);
                Execute(connection, @"
INSERT INTO item_instances(
    item_uid,character_id,item_definition_id,quantity,source_drop_id,
    source_kind,source_reference,created_unix_ms)
SELECT item_uid,character_id,item_definition_id,quantity,source_drop_id,
       1,'drop:' || source_drop_id,created_unix_ms
FROM item_instances_v7;", tx);
                Execute(connection, "DROP TABLE item_instances_v7;", tx);
            }
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_item_instances_owner
ON item_instances(character_id, item_uid);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS character_wallets (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    money INTEGER NOT NULL DEFAULT 0 CHECK(money BETWEEN 0 AND 9000000000000000)
);", tx);
            Execute(connection, @"
INSERT OR IGNORE INTO character_wallets(character_id,money)
SELECT character_id,0 FROM characters;", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS shop_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    action_kind INTEGER NOT NULL CHECK(action_kind IN (1,2)),
    npc_spawn_id INTEGER NOT NULL CHECK(npc_spawn_id > 0),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    unit_price INTEGER NOT NULL CHECK(unit_price BETWEEN 1 AND 1000000000000),
    remaining_quantity INTEGER NOT NULL CHECK(remaining_quantity BETWEEN 0 AND 1000000),
    money_after INTEGER NOT NULL CHECK(money_after BETWEEN 0 AND 9000000000000000),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);

            // Schema v9: personal storage moves whole item stacks without
            // minting a second UID. Inventory and storage are separate physical
            // tables for now, guarded by cross-location triggers so one UID (or
            // one drop provenance row) cannot exist in both locations.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS storage_items (
    item_uid INTEGER PRIMARY KEY,
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER UNIQUE REFERENCES ground_drops(drop_id),
    durability_raw INTEGER CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647),
    source_kind INTEGER NOT NULL CHECK(source_kind IN (1,2)),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    CHECK(
      (source_kind=1 AND source_drop_id IS NOT NULL)
      OR (source_kind=2 AND source_drop_id IS NULL)
    )
);", tx);
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_storage_items_owner
ON storage_items(character_id,item_uid);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS storage_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    action_kind INTEGER NOT NULL CHECK(action_kind IN (1,2)),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            if (schemaVersion == 9 &&
                !ColumnExists(connection, "storage_actions", "source_drop_id", tx))
                Execute(connection, @"
ALTER TABLE storage_actions ADD COLUMN source_drop_id INTEGER;", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_storage_no_inventory_overlap
BEFORE INSERT ON storage_items
WHEN EXISTS(SELECT 1 FROM item_instances WHERE item_uid=NEW.item_uid)
  OR (NEW.source_drop_id IS NOT NULL AND
      EXISTS(SELECT 1 FROM item_instances WHERE source_drop_id=NEW.source_drop_id))
BEGIN
    SELECT RAISE(ABORT,'item already exists in inventory');
END;", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_inventory_no_storage_overlap
BEFORE INSERT ON item_instances
WHEN EXISTS(SELECT 1 FROM storage_items WHERE item_uid=NEW.item_uid)
  OR (NEW.source_drop_id IS NOT NULL AND
      EXISTS(SELECT 1 FROM storage_items WHERE source_drop_id=NEW.source_drop_id))
BEGIN
    SELECT RAISE(ABORT,'item already exists in storage');
END;", tx);

            // Schema v11: equipment is another physical Item UID location.
            // V1 intentionally stores only quantity=1 items. Slot semantics are
            // supplied by a trusted verified item rule; the client never writes
            // slot or stats directly.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS equipped_items (
    item_uid INTEGER PRIMARY KEY,
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    slot_id INTEGER NOT NULL CHECK(slot_id BETWEEN 1 AND 255),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity = 1),
    source_drop_id INTEGER UNIQUE REFERENCES ground_drops(drop_id),
    durability_raw INTEGER CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647),
    source_kind INTEGER NOT NULL CHECK(source_kind IN (1,2)),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    UNIQUE(character_id,slot_id),
    CHECK(
      (source_kind=1 AND source_drop_id IS NOT NULL)
      OR (source_kind=2 AND source_drop_id IS NULL)
    )
);", tx);
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_equipped_items_owner
ON equipped_items(character_id,item_uid);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS equipment_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    action_kind INTEGER NOT NULL CHECK(action_kind IN (1,2)),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    slot_id INTEGER NOT NULL CHECK(slot_id BETWEEN 1 AND 255),
    source_drop_id INTEGER,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_equipped_no_inventory_storage_overlap
BEFORE INSERT ON equipped_items
WHEN EXISTS(SELECT 1 FROM item_instances WHERE item_uid=NEW.item_uid)
  OR EXISTS(SELECT 1 FROM storage_items WHERE item_uid=NEW.item_uid)
  OR (NEW.source_drop_id IS NOT NULL AND
      (EXISTS(SELECT 1 FROM item_instances WHERE source_drop_id=NEW.source_drop_id)
       OR EXISTS(SELECT 1 FROM storage_items WHERE source_drop_id=NEW.source_drop_id)))
BEGIN
    SELECT RAISE(ABORT,'item already exists outside equipment');
END;", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_inventory_no_equipped_overlap
BEFORE INSERT ON item_instances
WHEN EXISTS(SELECT 1 FROM equipped_items WHERE item_uid=NEW.item_uid)
  OR (NEW.source_drop_id IS NOT NULL AND
      EXISTS(SELECT 1 FROM equipped_items WHERE source_drop_id=NEW.source_drop_id))
BEGIN
    SELECT RAISE(ABORT,'item already exists equipped');
END;", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_storage_no_equipped_overlap
BEFORE INSERT ON storage_items
WHEN EXISTS(SELECT 1 FROM equipped_items WHERE item_uid=NEW.item_uid)
  OR (NEW.source_drop_id IS NOT NULL AND
      EXISTS(SELECT 1 FROM equipped_items WHERE source_drop_id=NEW.source_drop_id))
BEGIN
    SELECT RAISE(ABORT,'item already exists equipped');
END;", tx);

            // Schema v12: personal-bank balances support an explicitly verified
            // character or account scope. The client never chooses that scope.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS character_bank_balances (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    money INTEGER NOT NULL DEFAULT 0 CHECK(money BETWEEN 0 AND 9000000000000000)
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS account_bank_balances (
    account_id INTEGER PRIMARY KEY REFERENCES accounts(account_id) ON DELETE CASCADE,
    money INTEGER NOT NULL DEFAULT 0 CHECK(money BETWEEN 0 AND 9000000000000000)
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS bank_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    action_kind INTEGER NOT NULL CHECK(action_kind IN (1,2)),
    npc_spawn_id INTEGER NOT NULL CHECK(npc_spawn_id > 0),
    balance_scope INTEGER NOT NULL CHECK(balance_scope IN (1,2)),
    amount INTEGER NOT NULL CHECK(amount BETWEEN 1 AND 9000000000000000),
    wallet_after INTEGER NOT NULL CHECK(wallet_after BETWEEN 0 AND 9000000000000000),
    bank_after INTEGER NOT NULL CHECK(bank_after BETWEEN 0 AND 9000000000000000),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            // Schema v13: Old Server stores repairable equipment durability in
            // raw 1/50 display-point ticks. Durability follows the physical Item UID
            // across inventory/storage/equipment; NULL means no verified durability
            // rule has initialized that item yet.
            if (schemaVersion < 13)
            {
                foreach (string table in new[] { "item_instances", "storage_items", "equipped_items" })
                    if (!ColumnExists(connection, table, "durability_raw", tx))
                        Execute(connection,
                            $"ALTER TABLE {table} ADD COLUMN durability_raw INTEGER " +
                            "CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647);",
                            tx);
            }
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS repair_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    npc_spawn_id INTEGER NOT NULL CHECK(npc_spawn_id > 0),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    repair_rate INTEGER NOT NULL CHECK(repair_rate BETWEEN 1 AND 255),
    base_price INTEGER NOT NULL CHECK(base_price BETWEEN 1 AND 1000000000000),
    max_durability INTEGER NOT NULL CHECK(max_durability BETWEEN 1 AND 42949672),
    raw_before INTEGER NOT NULL CHECK(raw_before BETWEEN 0 AND 2147483647),
    raw_after INTEGER NOT NULL CHECK(raw_after BETWEEN 1 AND 2147483647),
    cost INTEGER NOT NULL CHECK(cost BETWEEN 1 AND 9000000000000000),
    money_after INTEGER NOT NULL CHECK(money_after BETWEEN 0 AND 9000000000000000),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);

            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_combat_mp_insert
BEFORE INSERT ON character_combat_profiles
WHEN NEW.current_mp > NEW.max_mp
BEGIN
    SELECT RAISE(ABORT,'current_mp exceeds max_mp');
END;", tx);
            Execute(connection, @"
CREATE TRIGGER IF NOT EXISTS trg_combat_mp_update
BEFORE UPDATE OF current_mp,max_mp ON character_combat_profiles
WHEN NEW.current_mp > NEW.max_mp
BEGIN
    SELECT RAISE(ABORT,'current_mp exceeds max_mp');
END;", tx);
            if (schemaVersion < SchemaVersion)
                Execute(connection,
                    $"UPDATE schema_version SET version={SchemaVersion};", tx);
            tx.Commit();
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountCreateResult> CreateAccountAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var identity = NormalizeUsername(username);
        PasswordHasher.ValidateNewPassword(password);
        string passwordHash = await _passwords.HashAsync(password, cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO accounts(username_key,username_display,password_hash,created_unix_ms)
VALUES($key,$display,$hash,$created); SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$key", identity.Key);
            command.Parameters.AddWithValue("$display", identity.Display);
            command.Parameters.AddWithValue("$hash", passwordHash);
            command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            try
            {
                long id = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
                return new AccountCreateResult(AccountCreateStatus.Created, id);
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19)
            {
                return new AccountCreateResult(AccountCreateStatus.DuplicateUsername);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        string? key = TryNormalizeUsername(username, out var normalized) ? normalized.Key : null;
        AccountRow? row = await Task.Run(() => key is null ? null : ReadAccount(key), cancellationToken)
            .ConfigureAwait(false);
        string suppliedPassword = password ?? string.Empty;
        string hash = row?.PasswordHash ?? _dummyHash;
        bool verified = await _passwords.VerifyAsync(suppliedPassword, hash, cancellationToken)
            .ConfigureAwait(false);
        if (row is null || !verified)
            return new AuthenticationResult(AuthenticationStatus.InvalidCredentials);
        if (row.Disabled)
            return new AuthenticationResult(AuthenticationStatus.Disabled);

        if (_passwords.NeedsRehash(row.PasswordHash))
        {
            string upgraded = await _passwords.HashAsync(suppliedPassword, cancellationToken).ConfigureAwait(false);
            await Task.Run(() => UpdatePasswordHash(row.AccountId, row.PasswordHash, upgraded), cancellationToken)
                .ConfigureAwait(false);
        }
        var characters = await Task.Run(() => ReadCharacters(row.AccountId), cancellationToken)
            .ConfigureAwait(false);
        return new AuthenticationResult(AuthenticationStatus.Success,
            new AccountRecord(row.AccountId, row.UsernameDisplay, false, characters));
    }

    public async Task<CharacterCreateResult> CreateCharacterAsync(long accountId, int slot,
        string name, CharacterSpawn spawn, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        if (slot < 0 || slot >= _options.MaxCharactersPerAccount)
            throw new ArgumentOutOfRangeException(nameof(slot));
        var characterName = NormalizeCharacterName(name);
        ValidateSpawn(spawn);
        return await Task.Run(() => CreateCharacter(accountId, slot, characterName, spawn), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CharacterRecord>> ListCharactersAsync(long accountId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        return await Task.Run(() => ReadCharacters(accountId), cancellationToken).ConfigureAwait(false);
    }

    public Task UpdateCharacterSpawnAsync(long characterId, CharacterSpawn spawn,
        CancellationToken cancellationToken = default) =>
        UpdateCharacterSpawnsAsync([new CharacterPositionUpdate(characterId, spawn)], cancellationToken);

    public async Task UpdateCharacterSpawnsAsync(
        IReadOnlyList<CharacterPositionUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(updates));
        var seen = new HashSet<long>();
        foreach (var update in updates)
        {
            if (update.CharacterId <= 0 || !seen.Add(update.CharacterId))
                throw new ArgumentException("Character position batch has invalid/duplicate ID", nameof(updates));
            ValidateSpawn(update.Spawn);
        }

        await Task.Run(() =>
        {
            using var connection = Open();
            using var tx = connection.BeginTransaction();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var update in updates)
            {
                using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = @"UPDATE characters
SET map_id=$map,x=$x,y=$y,updated_unix_ms=$updated
WHERE character_id=$id;";
                command.Parameters.AddWithValue("$map", update.Spawn.MapId);
                command.Parameters.AddWithValue("$x", update.Spawn.X);
                command.Parameters.AddWithValue("$y", update.Spawn.Y);
                command.Parameters.AddWithValue("$updated", now);
                command.Parameters.AddWithValue("$id", update.CharacterId);
                if (command.ExecuteNonQuery() != 1)
                    throw new KeyNotFoundException("Unknown character");
            }
            tx.Commit();
        }, cancellationToken).ConfigureAwait(false);
    }

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
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = @"
INSERT INTO character_combat_profiles(
    character_id,trust,source_ref,race,career,strength,dexterity,constitution,wisdom,
    level,current_hp,max_hp,current_mp,max_mp,attack,defense,hit,dodge,attack_range,
    attack_cooldown_ticks,updated_unix_ms)
VALUES($id,$trust,$source,$race,$career,$str,$dex,$con,$wis,$level,$hp,$maxhp,
       $mp,$maxmp,$attack,$defense,$hit,$dodge,$range,$cooldown,$updated)
ON CONFLICT(character_id) DO UPDATE SET
    trust=excluded.trust,source_ref=excluded.source_ref,race=excluded.race,
    career=excluded.career,strength=excluded.strength,dexterity=excluded.dexterity,
    constitution=excluded.constitution,wisdom=excluded.wisdom,level=excluded.level,
    current_hp=excluded.current_hp,max_hp=excluded.max_hp,
    current_mp=excluded.current_mp,max_mp=excluded.max_mp,
    attack=excluded.attack,defense=excluded.defense,hit=excluded.hit,dodge=excluded.dodge,
    attack_range=excluded.attack_range,
    attack_cooldown_ticks=excluded.attack_cooldown_ticks,
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
        if (profile.Trust is not (CombatProfileTrust.Verified or CombatProfileTrust.SyntheticTest) ||
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
            profile.Attack is < 0 or > 100_000_000 ||
            profile.Defense is < 0 or > 100_000_000 ||
            profile.Hit is < 0 or > 100_000_000 ||
            profile.Dodge is < 0 or > 100_000_000 ||
            !double.IsFinite(profile.AttackRange) ||
            profile.AttackRange is <= 0 or > 4096 ||
            profile.AttackCooldownTicks is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(profile));
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
        command.Parameters.AddWithValue("$attack", profile.Attack);
        command.Parameters.AddWithValue("$defense", profile.Defense);
        command.Parameters.AddWithValue("$hit", profile.Hit);
        command.Parameters.AddWithValue("$dodge", profile.Dodge);
        command.Parameters.AddWithValue("$range", profile.AttackRange);
        command.Parameters.AddWithValue("$cooldown", profile.AttackCooldownTicks);
        command.Parameters.AddWithValue("$updated", updated);
    }

    private static CharacterCombatProfile? ReadCombatProfile(
        SqliteConnection connection, long characterId, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"
SELECT character_id,trust,source_ref,race,career,strength,dexterity,constitution,
       wisdom,level,current_hp,max_hp,current_mp,max_mp,attack,defense,hit,dodge,attack_range,
       attack_cooldown_ticks,updated_unix_ms
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
            reader.GetInt32(14),
            reader.GetInt32(15),
            reader.GetInt32(16),
            reader.GetInt32(17),
            reader.GetDouble(18),
            reader.GetInt32(19),
            reader.GetInt32(12),
            reader.GetInt32(13));
        return new CharacterCombatProfile(
            reader.GetInt64(0), data, reader.GetInt64(20));
    }

    public async Task SetDisabledAsync(long accountId, bool disabled,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (accountId <= 0) throw new ArgumentOutOfRangeException(nameof(accountId));
        await Task.Run(() =>
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE accounts SET disabled=$disabled WHERE account_id=$id;";
            command.Parameters.AddWithValue("$disabled", disabled ? 1 : 0);
            command.Parameters.AddWithValue("$id", accountId);
            if (command.ExecuteNonQuery() != 1) throw new KeyNotFoundException("Unknown account");
        }, cancellationToken).ConfigureAwait(false);
    }

    private CharacterCreateResult CreateCharacter(long accountId, int slot,
        (string Display, string Key) name, CharacterSpawn spawn)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        if (ScalarLong(connection, "SELECT COUNT(*) FROM accounts WHERE account_id=$id;", tx,
            ("$id", accountId)) != 1)
            return new CharacterCreateResult(CharacterCreateStatus.UnknownAccount);
        if (ScalarLong(connection, "SELECT COUNT(*) FROM characters WHERE account_id=$id;", tx,
            ("$id", accountId)) >= _options.MaxCharactersPerAccount)
            return new CharacterCreateResult(CharacterCreateStatus.LimitReached);

        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = @"INSERT INTO characters(account_id,slot,name,name_key,map_id,x,y,created_unix_ms,updated_unix_ms)
VALUES($account,$slot,$name,$key,$map,$x,$y,$created,$updated); SELECT last_insert_rowid();";
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        command.Parameters.AddWithValue("$account", accountId);
        command.Parameters.AddWithValue("$slot", slot);
        command.Parameters.AddWithValue("$name", name.Display);
        command.Parameters.AddWithValue("$key", name.Key);
        command.Parameters.AddWithValue("$map", spawn.MapId);
        command.Parameters.AddWithValue("$x", spawn.X);
        command.Parameters.AddWithValue("$y", spawn.Y);
        command.Parameters.AddWithValue("$created", now);
        command.Parameters.AddWithValue("$updated", now);
        try
        {
            long characterId = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            using (var wallet = connection.CreateCommand())
            {
                wallet.Transaction = tx;
                wallet.CommandText = @"INSERT INTO character_wallets(character_id,money)
VALUES($character,0);";
                wallet.Parameters.AddWithValue("$character", characterId);
                if (wallet.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Character wallet creation failed");
            }
            tx.Commit();
            return new CharacterCreateResult(CharacterCreateStatus.Created,
                new CharacterRecord(characterId, accountId, slot, name.Display,
                    spawn.MapId, spawn.X, spawn.Y, now));
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            bool slotTaken = ScalarLong(connection,
                "SELECT COUNT(*) FROM characters WHERE account_id=$id AND slot=$slot;", tx,
                ("$id", accountId), ("$slot", slot)) > 0;
            tx.Rollback();
            return new CharacterCreateResult(slotTaken
                ? CharacterCreateStatus.SlotOccupied : CharacterCreateStatus.NameTaken);
        }
    }

    private AccountRow? ReadAccount(string usernameKey)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT account_id,username_display,password_hash,disabled
FROM accounts WHERE username_key=$key LIMIT 1;";
        command.Parameters.AddWithValue("$key", usernameKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new AccountRow(reader.GetInt64(0), reader.GetString(1),
            reader.GetString(2), reader.GetInt64(3) != 0) : null;
    }

    private IReadOnlyList<CharacterRecord> ReadCharacters(long accountId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT character_id,account_id,slot,name,map_id,x,y,created_unix_ms
FROM characters WHERE account_id=$account ORDER BY slot;";
        command.Parameters.AddWithValue("$account", accountId);
        using var reader = command.ExecuteReader();
        var rows = new List<CharacterRecord>();
        while (reader.Read())
            rows.Add(new CharacterRecord(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt32(4), reader.GetDouble(5), reader.GetDouble(6),
                reader.GetInt64(7)));
        return rows;
    }

    private void UpdatePasswordHash(long accountId, string oldHash, string newHash)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE accounts SET password_hash=$new
WHERE account_id=$id AND password_hash=$old;";
        command.Parameters.AddWithValue("$new", newHash);
        command.Parameters.AddWithValue("$old", oldHash);
        command.Parameters.AddWithValue("$id", accountId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql,
        SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool ColumnExists(
        SqliteConnection connection, string table, string column,
        SqliteTransaction? transaction = null)
    {
        // This helper is intentionally limited to the trusted internal schema
        // identifiers supplied by migrations; no client/user value is interpolated.
        if (table is not ("character_combat_profiles" or
                "item_instances" or "storage_items" or "equipped_items") ||
            string.IsNullOrWhiteSpace(column))
            throw new ArgumentOutOfRangeException(nameof(table));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static long ScalarLong(SqliteConnection connection, string sql,
        SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.AddWithValue(p.Name, p.Value);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static (string Display, string Key) NormalizeUsername(string username)
    {
        if (!TryNormalizeUsername(username, out var result))
            throw new ArgumentException("Username must be 3..32 ASCII letters, digits or underscore", nameof(username));
        return result;
    }

    private static bool TryNormalizeUsername(string? username, out (string Display, string Key) result)
    {
        result = default;
        if (username is null || username.Length is < 3 or > 32) return false;
        foreach (char c in username)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                return false;
        result = (username, username.ToLowerInvariant());
        return true;
    }

    private static (string Display, string Key) NormalizeCharacterName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string normalized = name.Normalize(NormalizationForm.FormC).Trim();
        if (normalized.Length is < 1 or > 24 || normalized.Any(char.IsControl))
            throw new ArgumentException("Character name must be 1..24 non-control characters", nameof(name));
        return (normalized, normalized);
    }

    private static void ValidateSpawn(CharacterSpawn spawn)
    {
        if (spawn.MapId is < 1 or > 65535 || !double.IsFinite(spawn.X) || !double.IsFinite(spawn.Y) ||
            spawn.X < 0 || spawn.Y < 0)
            throw new ArgumentOutOfRangeException(nameof(spawn), "Spawn must be a finite server-authorized location");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    private sealed record AccountRow(long AccountId, string UsernameDisplay, string PasswordHash, bool Disabled);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsPasswords) _passwords.Dispose();
    }
}
