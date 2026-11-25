"""Build private source-backed targetable monster runtime documents.

This deliberately does not invent AI movement/attack rules. It publishes only
identity/visuals/spawns plus Old Server level, HPMAX and RebornTime, allowing
players/skills to target, damage, kill and observe respawn while unverified AI
remains disabled.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

from asset_converter.json_files import write_json
from asset_converter.source.old_enemy_rewards import read_old_enemy_combat_catalog

KIND = "verified_monster_target_runtime"
VERSION = 5
TICK_RATE = 30


def compile_target_runtime(
    visual_population: Path,
    enemy_data_csv: Path,
) -> dict:
    visual_path = Path(visual_population).resolve()
    if not visual_path.is_file() or visual_path.is_symlink():
        raise ValueError("visual population is missing or symbolic")
    document = json.loads(visual_path.read_text(encoding="utf-8"))
    if (
        document.get("version") != 3
        or document.get("kind") != "visual_spawn_content"
        or document.get("stats_verified") is not False
        or document.get("gameplay_ready") is not False
    ):
        raise ValueError("input must be the fail-closed v3 visual population")

    map_id = int(document.get("map_id", 0))
    raw_definitions = document.get("definitions")
    spawns = document.get("spawns")
    if map_id <= 0 or not isinstance(raw_definitions, list) or not raw_definitions:
        raise ValueError("visual population has invalid definitions")
    if not isinstance(spawns, list):
        raise ValueError("visual population has invalid spawns")

    definitions_by_id: dict[int, dict] = {}
    for definition in raw_definitions:
        enemy_id = int(definition.get("id", 0))
        sprite = str(definition.get("sprite", "")).strip().upper()
        atlas = str(definition.get("atlas", "")).strip().replace("\\", "/")
        if (
            enemy_id <= 0
            or enemy_id in definitions_by_id
            or not sprite
            or not atlas
            or definition.get("stats") is not None
            or definition.get("stats_verified") is not False
        ):
            raise ValueError(f"invalid visual definition {enemy_id}")
        definitions_by_id[enemy_id] = {"id": enemy_id, "sprite": sprite, "atlas": atlas}

    required_ids = set(definitions_by_id)
    combat, audit = read_old_enemy_combat_catalog(
        Path(enemy_data_csv), required_enemy_ids=required_ids
    )
    if set(combat) != required_ids:
        missing = sorted(required_ids.difference(combat))
        raise ValueError(f"Old combat source did not resolve every visual EnemyId: {missing}")

    definitions = []
    for enemy_id in sorted(required_ids):
        source = combat[enemy_id]
        behavior = source.get("definition_behavior", {})
        reborn_ms = int(behavior.get("reborn_time_ms", 0))
        if reborn_ms <= 0:
            raise ValueError(f"EnemyId {enemy_id} has no verified RebornTime")
        level = int(source["enemy_level"])
        max_hp = int(source["max_hp"])
        respawn_ticks = math.ceil(reborn_ms * TICK_RATE / 1000)
        base = definitions_by_id[enemy_id]
        definitions.append({
            **base,
            "stats": {
                "level": level,
                "max_hp": max_hp,
                "respawn_ticks": respawn_ticks,
            },
            "source_evidence": {
                "attribute_table": int(source["attribute_table"]),
                "reborn_time_ms": reborn_ms,
            },
        })

    source_indices: set[int] = set()
    runtime_spawns = []
    for spawn in spawns:
        source_index = int(spawn.get("source_index", -1))
        definition = int(spawn.get("definition", 0))
        point = spawn.get("p")
        if (
            source_index < 0
            or source_index in source_indices
            or definition not in required_ids
            or not isinstance(point, list)
            or len(point) != 2
        ):
            raise ValueError(f"invalid visual spawn {source_index}")
        source_indices.add(source_index)
        runtime_spawns.append({
            "definition": definition,
            "p": [float(point[0]), float(point[1])],
            "source_index": source_index,
            "source_flags": int(spawn.get("source_flags", 0)),
        })

    return {
        "version": VERSION,
        "kind": KIND,
        "map_id": map_id,
        "stats_verified": True,
        "target_combat_ready": True,
        "ai_ready": False,
        "definitions": definitions,
        "spawns": runtime_spawns,
        "audit": {
            "semantics": (
                "Old Enemy_Data/EN_Att source owns enemy level/HPMAX; "
                "Enemy_Data RebornTime is converted ms->30Hz ticks. "
                "AI attack/movement/range/cadence remain disabled and zero."
            ),
            "enemy_data_sha256": audit["enemy_data_sha256"],
            "enemy_data_bytes": audit["enemy_data_bytes"],
            "attribute_tables": audit["attribute_tables"],
        },
    }


def publish_target_runtime(
    visual_population: Path,
    enemy_data_csv: Path,
    output: Path,
) -> dict:
    result = compile_target_runtime(visual_population, enemy_data_csv)
    destination = Path(output).resolve()
    original_root = Path(enemy_data_csv).resolve().parent
    if destination == original_root or destination.is_relative_to(original_root):
        raise ValueError("runtime output must not overwrite original Old Server data")
    write_json(destination, result)
    return {**result, "output": str(destination)}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--visual-population", required=True, type=Path)
    parser.add_argument("--enemy-data", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = publish_target_runtime(
        args.visual_population, args.enemy_data, args.output
    )
    print(
        f"TARGET_MONSTER_RUNTIME_PASS map={result['map_id']} "
        f"definitions={len(result['definitions'])} spawns={len(result['spawns'])} "
        f"ai_ready={str(result['ai_ready']).lower()} output={result['output']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
