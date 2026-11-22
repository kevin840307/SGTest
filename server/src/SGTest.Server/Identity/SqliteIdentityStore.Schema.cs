using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SGTest.Server.Identity;

/// <summary>Owns the sequential SQLite schema initialization and migration transaction.</summary>
public sealed partial class SqliteIdentityStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.Run(() =>
        {
            using var connection = Open();
            Execute(connection, "PRAGMA journal_mode=WAL;");
            Execute(connection, "PRAGMA synchronous=NORMAL;");
            Execute(connection, $"PRAGMA busy_timeout={_options.BusyTimeoutSeconds * 1000};");
            int? existingSchemaVersion = TryReadExistingSchemaVersion(connection);
            if (existingSchemaVersion is int version && version < SchemaVersion)
                CreateMigrationBackup(version);
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
CREATE TABLE IF NOT EXISTS character_appearances (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    race INTEGER NOT NULL CHECK(race BETWEEN 0 AND 4),
    hair_index INTEGER NOT NULL CHECK(hair_index BETWEEN 0 AND 65535),
    hair_color INTEGER NOT NULL CHECK(hair_color BETWEEN 0 AND 7),
    source_ref TEXT NOT NULL CHECK(length(source_ref) BETWEEN 1 AND 384)
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS character_creation_settings (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    strength INTEGER NOT NULL CHECK(strength BETWEEN 1 AND 5),
    dexterity INTEGER NOT NULL CHECK(dexterity BETWEEN 1 AND 5),
    constitution INTEGER NOT NULL CHECK(constitution BETWEEN 1 AND 5),
    wisdom INTEGER NOT NULL CHECK(wisdom BETWEEN 1 AND 5),
    source_ref TEXT NOT NULL CHECK(length(source_ref) BETWEEN 1 AND 384),
    password_hash TEXT NOT NULL CHECK(length(password_hash) BETWEEN 1 AND 512),
    failed_password_attempts INTEGER NOT NULL DEFAULT 0 CHECK(failed_password_attempts BETWEEN 0 AND 5),
    password_locked_until_ms INTEGER NOT NULL DEFAULT 0 CHECK(password_locked_until_ms >= 0),
    CHECK(strength+dexterity+constitution+wisdom=8)
);", tx);
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
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
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
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
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
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
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
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
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

            // Schema v14: pending trade negotiation is ephemeral. Only the
            // final two-party ownership/money settlement is durable and replayable.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS trade_commits (
    trade_id INTEGER PRIMARY KEY CHECK(trade_id > 0),
    first_character_id INTEGER NOT NULL REFERENCES characters(character_id),
    second_character_id INTEGER NOT NULL REFERENCES characters(character_id),
    first_money_offer INTEGER NOT NULL CHECK(first_money_offer BETWEEN 0 AND 9000000000000000),
    second_money_offer INTEGER NOT NULL CHECK(second_money_offer BETWEEN 0 AND 9000000000000000),
    first_wallet_after INTEGER NOT NULL CHECK(first_wallet_after BETWEEN 0 AND 9000000000000000),
    second_wallet_after INTEGER NOT NULL CHECK(second_wallet_after BETWEEN 0 AND 9000000000000000),
    created_unix_ms INTEGER NOT NULL,
    CHECK(first_character_id <> second_character_id)
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS trade_commit_items (
    trade_id INTEGER NOT NULL REFERENCES trade_commits(trade_id) ON DELETE CASCADE,
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    from_character_id INTEGER NOT NULL REFERENCES characters(character_id),
    to_character_id INTEGER NOT NULL REFERENCES characters(character_id),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER,
    durability_raw INTEGER CHECK(durability_raw IS NULL OR durability_raw BETWEEN 0 AND 2147483647),
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
    PRIMARY KEY(trade_id,item_uid),
    CHECK(from_character_id <> to_character_id)
);", tx);
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_trade_commit_items_from
ON trade_commit_items(trade_id,from_character_id,item_uid);", tx);
            // Schema v15: verified Quest V1 persists only the stage/state
            // chosen through an admitted rule. No row means NotStarted.
            // rule_revision prevents an edited manifest from silently reinterpreting
            // an existing character's progress.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS quest_progress (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    quest_id INTEGER NOT NULL CHECK(quest_id BETWEEN 1 AND 100000000),
    rule_revision INTEGER NOT NULL CHECK(rule_revision BETWEEN 1 AND 2147483647),
    stage INTEGER NOT NULL CHECK(stage BETWEEN 0 AND 1000000),
    state INTEGER NOT NULL CHECK(state IN (1,2)),
    progress_revision INTEGER NOT NULL CHECK(progress_revision BETWEEN 1 AND 2147483647),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    updated_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,quest_id)
);", tx);
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS quest_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    npc_spawn_id INTEGER NOT NULL CHECK(npc_spawn_id > 0),
    quest_id INTEGER NOT NULL CHECK(quest_id BETWEEN 1 AND 100000000),
    rule_revision INTEGER NOT NULL CHECK(rule_revision BETWEEN 1 AND 2147483647),
    choice_id INTEGER NOT NULL CHECK(choice_id BETWEEN 1 AND 100000000),
    state_before INTEGER NOT NULL CHECK(state_before BETWEEN 0 AND 2),
    stage_before INTEGER NOT NULL CHECK(stage_before BETWEEN 0 AND 1000000),
    state_after INTEGER NOT NULL CHECK(state_after IN (1,2)),
    stage_after INTEGER NOT NULL CHECK(stage_after BETWEEN 0 AND 1000000),
    progress_revision_after INTEGER NOT NULL CHECK(progress_revision_after BETWEEN 1 AND 2147483647),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);", tx);
            Execute(connection, @"
CREATE INDEX IF NOT EXISTS ix_quest_progress_character
ON quest_progress(character_id,quest_id);", tx);
            // Schema v16: persistent player PvP opt-in mode.
            // Schema v17: source-recovered red/pink crime state. The normal
            // player's blue/white preference remains a separate field.
            Execute(connection, @"
CREATE TABLE IF NOT EXISTS character_pvp_modes (
    character_id INTEGER PRIMARY KEY REFERENCES characters(character_id) ON DELETE CASCADE,
    mode INTEGER NOT NULL DEFAULT 0 CHECK(mode IN (0,1)),
    red_man INTEGER NOT NULL DEFAULT 0 CHECK(red_man IN (0,1)),
    red_man_kill_left INTEGER NOT NULL DEFAULT 0 CHECK(red_man_kill_left BETWEEN 0 AND 2147483647),
    pink_until_unix_ms INTEGER NOT NULL DEFAULT 0 CHECK(pink_until_unix_ms >= 0),
    updated_unix_ms INTEGER NOT NULL
);", tx);
            if(schemaVersion < 17)
            {
                if(!ColumnExists(connection,"character_pvp_modes","red_man",tx))
                    Execute(connection,@"ALTER TABLE character_pvp_modes
ADD COLUMN red_man INTEGER NOT NULL DEFAULT 0 CHECK(red_man IN (0,1));",tx);
                if(!ColumnExists(connection,"character_pvp_modes","red_man_kill_left",tx))
                    Execute(connection,@"ALTER TABLE character_pvp_modes
ADD COLUMN red_man_kill_left INTEGER NOT NULL DEFAULT 0
CHECK(red_man_kill_left BETWEEN 0 AND 2147483647);",tx);
                if(!ColumnExists(connection,"character_pvp_modes","pink_until_unix_ms",tx))
                    Execute(connection,@"ALTER TABLE character_pvp_modes
ADD COLUMN pink_until_unix_ms INTEGER NOT NULL DEFAULT 0
CHECK(pink_until_unix_ms >= 0);",tx);
            }
            // Schema v18: local operator PvP-name mutations are durable,
            // idempotent and auditable. No game-socket opcode writes this table.
            Execute(connection,@"
CREATE TABLE IF NOT EXISTS pvp_name_admin_events (
    event_id TEXT PRIMARY KEY,
    operator_id TEXT NOT NULL CHECK(length(operator_id) BETWEEN 1 AND 64),
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE RESTRICT,
    requested_state INTEGER NOT NULL CHECK(requested_state BETWEEN 0 AND 4),
    reason TEXT NOT NULL CHECK(length(reason) BETWEEN 1 AND 256),
    result_mode INTEGER NOT NULL CHECK(result_mode IN (0,1)),
    result_red_man INTEGER NOT NULL CHECK(result_red_man IN (0,1)),
    result_red_man_kill_left INTEGER NOT NULL CHECK(result_red_man_kill_left BETWEEN 0 AND 2147483647),
    result_pink_until_unix_ms INTEGER NOT NULL CHECK(result_pink_until_unix_ms >= 0),
    result_updated_unix_ms INTEGER NOT NULL,
    created_unix_ms INTEGER NOT NULL
);",tx);
            Execute(connection,@"CREATE INDEX IF NOT EXISTS ix_pvp_name_admin_character
ON pvp_name_admin_events(character_id,created_unix_ms);",tx);
            // Schema v19: durable single-listing player-shop settlement core.
            // Listing presence is gameplay/session-owned; SQLite owns item/wallet
            // settlement and exact sold-listing replay.
            Execute(connection,@"
CREATE TABLE IF NOT EXISTS player_shop_listings (
    listing_id INTEGER PRIMARY KEY AUTOINCREMENT,
    seller_character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE RESTRICT,
    item_uid INTEGER NOT NULL,
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    quantity INTEGER NOT NULL CHECK(quantity BETWEEN 1 AND 1000000),
    source_drop_id INTEGER NULL,
    durability_raw INTEGER NULL,
    enhancement_level INTEGER NOT NULL DEFAULT 0 CHECK(enhancement_level BETWEEN 0 AND 255),
    price INTEGER NOT NULL CHECK(price BETWEEN 1 AND 9000000000000000),
    state INTEGER NOT NULL CHECK(state BETWEEN 0 AND 3),
    buyer_character_id INTEGER NULL REFERENCES characters(character_id) ON DELETE RESTRICT,
    seller_wallet_after INTEGER NULL,
    buyer_wallet_after INTEGER NULL,
    created_unix_ms INTEGER NOT NULL,
    updated_unix_ms INTEGER NOT NULL
);",tx);
            Execute(connection,@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_player_shop_active_seller
ON player_shop_listings(seller_character_id) WHERE state=0;",tx);
            Execute(connection,@"
CREATE UNIQUE INDEX IF NOT EXISTS ux_player_shop_active_item
ON player_shop_listings(item_uid) WHERE state=0;",tx);
            Execute(connection,@"
CREATE INDEX IF NOT EXISTS ix_player_shop_seller_history
ON player_shop_listings(seller_character_id,listing_id);",tx);
            // Schema v20: +N is ItemInstance state, never a separate
            // ItemDefinition. This migration only establishes durable metadata;
            // it does not claim an original max level, cost, material or success
            // formula. Production mutation stays disabled until verified rules exist.
            if(schemaVersion<20)
            {
                foreach(string table in new[]{"item_instances","storage_items","equipped_items"})
                    if(!ColumnExists(connection,table,"enhancement_level",tx))
                        Execute(connection,$@"ALTER TABLE {table}
ADD COLUMN enhancement_level INTEGER NOT NULL DEFAULT 0
CHECK(enhancement_level BETWEEN 0 AND 255);",tx);
            }
            // Schema v21: trade/player-shop durable receipts must freeze +N
            // together with UID/definition/provenance. Otherwise an exact trade
            // replay or a player-shop listing could describe a different item
            // state from the one actually committed.
            if(schemaVersion<21)
            {
                if(!ColumnExists(connection,"trade_commit_items","enhancement_level",tx))
                    Execute(connection,@"ALTER TABLE trade_commit_items
ADD COLUMN enhancement_level INTEGER NOT NULL DEFAULT 0
CHECK(enhancement_level BETWEEN 0 AND 255);",tx);
                if(!ColumnExists(connection,"player_shop_listings","enhancement_level",tx))
                    Execute(connection,@"ALTER TABLE player_shop_listings
ADD COLUMN enhancement_level INTEGER NOT NULL DEFAULT 0
CHECK(enhancement_level BETWEEN 0 AND 255);",tx);
            }
            // Schema v22: original Server-owned learned skill state. Old
            // CSkill/CDSkillInfo evidence stores SkillID + Lv on the player;
            // clients never choose the level used by gameplay formulas.
            Execute(connection,@"
CREATE TABLE IF NOT EXISTS character_skills (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    skill_id INTEGER NOT NULL CHECK(skill_id BETWEEN 1 AND 65535),
    level INTEGER NOT NULL CHECK(level BETWEEN 1 AND 255),
    source_reference TEXT NOT NULL CHECK(length(source_reference) BETWEEN 1 AND 256),
    updated_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,skill_id)
);",tx);
            Execute(connection,@"
CREATE INDEX IF NOT EXISTS ix_character_skills_character
ON character_skills(character_id,skill_id);",tx);
            // Schema v23: durable single-target/self Heal settlement detail.
            // The same authenticated skill sequence is first recorded in the
            // existing skill_mp_actions table so damage/support effects share
            // one durable per-generation sequence namespace. This detail row
            // freezes the Server-owned learned level, target and applied HP.
            Execute(connection,@"
CREATE TABLE IF NOT EXISTS skill_heal_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id),
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    skill_sequence INTEGER NOT NULL CHECK(skill_sequence BETWEEN 1 AND 4294967295),
    skill_id INTEGER NOT NULL CHECK(skill_id BETWEEN 1 AND 100000000),
    skill_level INTEGER NOT NULL CHECK(skill_level BETWEEN 1 AND 255),
    target_character_id INTEGER NOT NULL REFERENCES characters(character_id),
    mp_cost INTEGER NOT NULL CHECK(mp_cost BETWEEN 1 AND 100000000),
    heal_requested INTEGER NOT NULL CHECK(heal_requested BETWEEN 1 AND 100000000),
    heal_applied INTEGER NOT NULL CHECK(heal_applied BETWEEN 1 AND heal_requested),
    caster_previous_mp INTEGER NOT NULL CHECK(caster_previous_mp >= 0),
    caster_current_mp INTEGER NOT NULL CHECK(caster_current_mp >= 0),
    caster_max_mp INTEGER NOT NULL CHECK(caster_max_mp >= caster_current_mp),
    target_previous_hp INTEGER NOT NULL CHECK(target_previous_hp > 0),
    target_current_hp INTEGER NOT NULL CHECK(target_current_hp > target_previous_hp),
    target_max_hp INTEGER NOT NULL CHECK(target_max_hp >= target_current_hp),
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,skill_sequence),
    FOREIGN KEY(character_id,connection_generation,map_epoch,skill_sequence)
      REFERENCES skill_mp_actions(
        character_id,connection_generation,map_epoch,skill_sequence),
    CHECK(caster_previous_mp - caster_current_mp = mp_cost),
    CHECK(target_current_hp - target_previous_hp = heal_applied)
);",tx);

            Execute(connection,@"
CREATE TABLE IF NOT EXISTS enhancement_actions (
    character_id INTEGER NOT NULL REFERENCES characters(character_id) ON DELETE CASCADE,
    connection_generation INTEGER NOT NULL CHECK(connection_generation > 0),
    map_epoch INTEGER NOT NULL CHECK(map_epoch > 0),
    action_sequence INTEGER NOT NULL CHECK(action_sequence BETWEEN 1 AND 4294967295),
    npc_spawn_id INTEGER NOT NULL CHECK(npc_spawn_id > 0),
    item_uid INTEGER NOT NULL CHECK(item_uid > 0),
    item_definition_id INTEGER NOT NULL CHECK(item_definition_id BETWEEN 1 AND 100000000),
    item_location INTEGER NOT NULL CHECK(item_location IN (1,2)),
    level_before INTEGER NOT NULL CHECK(level_before BETWEEN 0 AND 255),
    target_level INTEGER NOT NULL CHECK(target_level BETWEEN 1 AND 255),
    level_after INTEGER NOT NULL CHECK(level_after BETWEEN 0 AND 255),
    success_ppm INTEGER NOT NULL CHECK(success_ppm BETWEEN 0 AND 1000000),
    roll_ppm INTEGER NOT NULL CHECK(roll_ppm BETWEEN 0 AND 999999),
    succeeded INTEGER NOT NULL CHECK(succeeded IN (0,1)),
    money_cost INTEGER NOT NULL CHECK(money_cost BETWEEN 0 AND 9000000000000000),
    money_charged INTEGER NOT NULL CHECK(money_charged BETWEEN 0 AND 9000000000000000),
    wallet_after INTEGER NOT NULL CHECK(wallet_after BETWEEN 0 AND 9000000000000000),
    source_reference TEXT NOT NULL,
    created_unix_ms INTEGER NOT NULL,
    PRIMARY KEY(character_id,connection_generation,map_epoch,action_sequence)
);",tx);
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

}
