"""Publish audited Old Server character EXP reward rules for selected maps."""

from __future__ import annotations

import hashlib
from pathlib import Path

from asset_converter.content.build_enemy_map_palette import read_enemy_dat
from asset_converter.content.build_maps import server_map_directory
from asset_converter.json_files import write_json
from asset_converter.source.old_enemy_rewards import read_old_enemy_reward_catalog

KIND = "verified_character_experience_reward_rules"
VERSION = 1


def compile_character_experience_rewards(
    server_dat: Path,
    map_ids: set[int],
    *,
    source_reference: str = (
        "Old SGOEnemyDistSys LoadNewCSVData/AddAttr + "
        "Enemy_Data.csv + EN_AttXX.csv"),
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
        raise ValueError("character EXP source reference is invalid")

    spawn_rows: dict[int, list[dict]] = {}
    required_enemy_ids: set[int] = set()
    map_audit = []
    for map_id in sorted(map_ids):
        enemy_dat = server_map_directory(root, map_id) / "Enemy.dat"
        spawns = read_enemy_dat(enemy_dat)
        if not spawns:
            raise ValueError(f"map {map_id} has no Enemy.dat spawns")
        spawn_rows[map_id] = spawns
        required_enemy_ids.update(row["enemy_id"] for row in spawns)
        payload = enemy_dat.read_bytes()
        map_audit.append({
            "map_id": map_id,
            "enemy_dat_sha256": hashlib.sha256(payload).hexdigest(),
            "enemy_dat_bytes": len(payload),
            "spawn_count": len(spawns),
            "unique_enemy_ids": len({row["enemy_id"] for row in spawns}),
        })

    rewards, source_audit = read_old_enemy_reward_catalog(
        root / "Enemy" / "Enemy_Data.csv",
        required_enemy_ids=required_enemy_ids)

    rules = []
    for map_id in sorted(map_ids):
        for enemy_id in sorted({row["enemy_id"] for row in spawn_rows[map_id]}):
            reward = rewards.get(enemy_id)
            if reward is None:
                continue
            rules.append({
                "map_id": map_id,
                "enemy_id": enemy_id,
                "enemy_level": reward["enemy_level"],
                "r_exp": reward["r_exp"],
            })

    if not rules:
        raise ValueError("no character EXP reward rules were resolved")
    return {
        "kind": KIND,
        "version": VERSION,
        "rules_verified": True,
        "gameplay_ready": True,
        "source_reference": source_label,
        "rules": rules,
        "audit": {
            "semantics": (
                "Enemy_Data.csv col[3] -> WORD[5] level/FindAttr key; "
                "col[4] -> BYTE[0x23] EN_Att selector; "
                "EN_AttXX col[7] -> CDEnemyAttr+0x22 -> "
                "CDEnemyWORDStr[12] RExp; 0xffff level key -> "
                "FindAttr miss -> AddAttr false -> no reward rule"),
            "enemy_data_sha256": source_audit["enemy_data_sha256"],
            "enemy_data_bytes": source_audit["enemy_data_bytes"],
            "attribute_tables": source_audit["attribute_tables"],
            "excluded_no_attribute_reward": (
                source_audit["excluded_no_attribute_reward"]),
            "maps": map_audit,
        },
        "status": "pass",
    }


def publish_character_experience_rewards(
    server_dat: Path,
    output: Path,
    map_ids: set[int],
    *,
    source_reference: str = (
        "Old SGOEnemyDistSys LoadNewCSVData/AddAttr + "
        "Enemy_Data.csv + EN_AttXX.csv"),
) -> dict:
    root = Path(server_dat).resolve()
    destination = Path(output).resolve()
    if destination == root or destination.is_relative_to(root):
        raise ValueError(
            "character EXP reward output must not overwrite original server Dat")
    result = compile_character_experience_rewards(
        root, map_ids, source_reference=source_reference)
    write_json(destination, result)
    return {**result, "output": str(destination)}
