"""Compile verified Old Server monster drop rules for selected maps."""

from __future__ import annotations

import hashlib
from pathlib import Path

from asset_converter.content.build_enemy_map_palette import read_enemy_dat
from asset_converter.content.build_maps import server_map_directory
from asset_converter.json_files import write_json
from asset_converter.source.old_enemy_drops import read_old_enemy_drop_catalog
from asset_converter.source.old_enemy_rewards import read_old_enemy_combat_catalog

KIND = "verified_monster_drop_rules"
VERSION = 2


def compile_monster_drop_rules(
    server_dat: Path,
    map_ids: set[int],
    *,
    source_reference: str = (
        "Old SGOEnemyDistSys::LoadEnemyData/CheckDropItem + "
        "Dat/Enemy/Enemy_Data.csv + enemy.cfg"
    ),
) -> dict:
    root = Path(server_dat).resolve()
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Old Server Dat root is missing or symbolic")
    if not map_ids or len(map_ids) > 4096:
        raise ValueError("at least one bounded map id is required")
    if any(map_id < 1 or map_id > 65535 for map_id in map_ids):
        raise ValueError("map id outside bounds")
    source_label = source_reference.strip()
    if not source_label or len(source_label) > 256:
        raise ValueError("monster drop source reference is invalid")

    required_enemy_ids: set[int] = set()
    map_enemy_ids: dict[int, set[int]] = {}
    map_audit = []
    for map_id in sorted(map_ids):
        enemy_dat = server_map_directory(root, map_id) / "Enemy.dat"
        spawns = read_enemy_dat(enemy_dat)
        if not spawns:
            raise ValueError(f"map {map_id} has no Enemy.dat spawns")
        ids = {row["enemy_id"] for row in spawns}
        map_enemy_ids[map_id] = ids
        required_enemy_ids.update(ids)
        payload = enemy_dat.read_bytes()
        map_audit.append({
            "map_id": map_id,
            "enemy_dat_sha256": hashlib.sha256(payload).hexdigest(),
            "enemy_dat_bytes": len(payload),
            "spawn_count": len(spawns),
            "unique_enemy_ids": len(ids),
        })

    combat, combat_audit = read_old_enemy_combat_catalog(
        root / "Enemy" / "Enemy_Data.csv",
        required_enemy_ids=required_enemy_ids,
    )
    drop_rows, settings, drop_audit = read_old_enemy_drop_catalog(
        root / "Enemy" / "Enemy_Data.csv",
        root.parent / "enemy.cfg",
        required_enemy_ids=required_enemy_ids,
    )

    rules = []
    for map_id in sorted(map_ids):
        for enemy_id in sorted(map_enemy_ids[map_id]):
            stats = combat.get(enemy_id)
            if stats is None:
                continue
            for source_rule in drop_rows[enemy_id]:
                rules.append({
                    "map_id": map_id,
                    "enemy_id": enemy_id,
                    "slot": source_rule["slot"],
                    "item_definition_id": source_rule["item_definition_id"],
                    "quantity": 1,
                    "enemy_level": stats["enemy_level"],
                    "roll_threshold": source_rule["roll_threshold"],
                    "source_pair_index": source_rule["source_pair_index"],
                    "check_luck_source": source_rule["check_luck_source"],
                })

    if not rules:
        raise ValueError("no source-verifiable monster drop rules were resolved")

    return {
        "kind": KIND,
        "version": VERSION,
        "rules_verified": True,
        "gameplay_ready": True,
        "source_reference": source_label,
        "level_delta_divisor": settings["level_delta_divisor"],
        "rules": rules,
        "audit": {
            "semantics": (
                "LoadEnemyData reads Enemy_Data.csv drop counts at columns 72/73, "
                "then item/rate pairs from column 74; first-count entries set "
                "sDropItem flag +2 and rate*0xffffffff becomes threshold +4. "
                "CheckDropItem applies the verified luck cap, then divides by "
                "floor(max(playerLevel-enemyLevel,0)/LvDelta)+1 and drops only "
                "when its four-rand 32-bit roll is below threshold. Luck-sensitive "
                "entries below the verified cap are excluded fail-closed."
            ),
            "check_luck_cap_threshold": settings["check_luck_cap_threshold"],
            "max_check_luck_rate_units": settings["max_check_luck_rate_units"],
            "enemy_data_sha256": drop_audit["enemy_data_sha256"],
            "enemy_data_bytes": drop_audit["enemy_data_bytes"],
            "enemy_cfg_sha256": drop_audit["enemy_cfg_sha256"],
            "enemy_cfg_bytes": drop_audit["enemy_cfg_bytes"],
            "attribute_tables": combat_audit["attribute_tables"],
            "excluded_no_attributes": combat_audit["excluded_no_attributes"],
            "excluded_rules": drop_audit["excluded_rules"],
            "maps": map_audit,
        },
        "status": "pass",
    }


def publish_monster_drop_rules(
    server_dat: Path,
    output: Path,
    map_ids: set[int],
    *,
    source_reference: str = (
        "Old SGOEnemyDistSys::LoadEnemyData/CheckDropItem + "
        "Dat/Enemy/Enemy_Data.csv + enemy.cfg"
    ),
) -> dict:
    root = Path(server_dat).resolve()
    destination = Path(output).resolve()
    if destination == root or destination.is_relative_to(root):
        raise ValueError("monster drop output must not overwrite original server Dat")
    result = compile_monster_drop_rules(
        root, map_ids, source_reference=source_reference)
    write_json(destination, result)
    return {**result, "output": str(destination)}
