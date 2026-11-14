#!/usr/bin/env python3
"""Build deterministic per-map Enemy visual palettes from original server data.

This is an offline content-planning tool. It reads Enemy.dat spawn records and
Enemy_Data.csv sprite references without importing gameplay/server code.
"""
from __future__ import annotations

import argparse
import csv
import json
import struct
from collections import Counter
from pathlib import Path

TAIL_SIZE = 207


def read_enemy_dat(path: Path) -> list[dict]:
    data = path.read_bytes()
    if len(data) < 4:
        raise ValueError(f"short Enemy.dat: {path}")
    count = struct.unpack_from("<i", data, 0)[0]
    if count < 0 or count > 1_000_000:
        raise ValueError(f"invalid Enemy.dat count {count}: {path}")
    offset = 4
    rows = []
    for index in range(count):
        if offset + 4 > len(data):
            raise ValueError(f"Enemy.dat record {index} missing flags: {path}")
        flags = struct.unpack_from("<i", data, offset)[0]
        offset += 4
        end = data.find(b"\0", offset, min(len(data), offset + 32))
        if end < 0:
            raise ValueError(f"Enemy.dat record {index} missing id terminator: {path}")
        try:
            enemy_id = int(data[offset:end].decode("ascii"))
        except (UnicodeDecodeError, ValueError) as exc:
            raise ValueError(f"Enemy.dat record {index} invalid enemy id: {path}") from exc
        offset = end + 1
        if offset + 8 + TAIL_SIZE > len(data):
            raise ValueError(f"Enemy.dat record {index} ended early: {path}")
        x, y = struct.unpack_from("<ii", data, offset)
        offset += 8
        tail = data[offset:offset + TAIL_SIZE]
        offset += TAIL_SIZE
        rows.append({"source_index": index, "flags": flags, "enemy_id": enemy_id,
                     "x": x, "y": y, "tail_size": len(tail)})
    if offset != len(data):
        raise ValueError(f"Enemy.dat has {len(data) - offset} trailing bytes: {path}")
    return rows


def read_enemy_catalog(path: Path) -> dict[int, dict]:
    items = {}
    with path.open("r", encoding="cp950", errors="replace", newline="") as stream:
        for row_number, row in enumerate(csv.reader(stream), 1):
            if len(row) < 7:
                continue
            try:
                enemy_id = int(row[0])
            except ValueError:
                continue
            sprite = row[6].strip()
            if not sprite:
                continue
            items[enemy_id] = {
                "enemy_id": enemy_id,
                "name": row[2].strip() if len(row) > 2 else "",
                "level": int(row[3]) if len(row) > 3 and row[3].strip().lstrip("-").isdigit() else None,
                "sprite": sprite.upper(),
                "source_row": row_number,
            }
    return items


def build_map_palette(map_id: int, enemy_dat: Path, catalog: dict[int, dict]) -> dict:
    spawns = read_enemy_dat(enemy_dat)
    ids = Counter(row["enemy_id"] for row in spawns)
    visuals = Counter()
    unresolved = []
    definitions = []
    for enemy_id, count in sorted(ids.items()):
        definition = catalog.get(enemy_id)
        if definition is None:
            unresolved.append({"enemy_id": enemy_id, "spawn_count": count})
            continue
        sprite = definition["sprite"]
        visuals[sprite] += count
        definitions.append({
            "enemy_id": enemy_id,
            "sprite": sprite,
            "spawn_count": count,
            "level": definition["level"],
            "name": definition["name"],
        })
    visual_rows = [{"sprite": sprite, "spawn_count": count}
                   for sprite, count in sorted(visuals.items())]
    return {
        "map_id": map_id,
        "spawn_count": len(spawns),
        "enemy_definition_count": len(ids),
        "visual_kind_count": len(visuals),
        "visuals": visual_rows,
        "definitions": definitions,
        "unresolved": unresolved,
        "complete": not unresolved,
    }


def discover_maps(map_root: Path, only: set[int] | None = None) -> list[tuple[int, Path]]:
    rows = []
    for folder in map_root.iterdir():
        if not folder.is_dir() or not folder.name.casefold().startswith("map"):
            continue
        suffix = folder.name[3:]
        if not suffix.isdigit():
            continue
        map_id = int(suffix)
        if only is not None and map_id not in only:
            continue
        enemy = folder / "Enemy.dat"
        if enemy.is_file():
            rows.append((map_id, enemy))
    return sorted(rows)


def build_palettes(data_root: Path, only: set[int] | None = None) -> dict:
    catalog_path = data_root / "Enemy" / "Enemy_Data.csv"
    map_root = data_root / "Map"
    if not catalog_path.is_file() or not map_root.is_dir():
        raise ValueError("expected Enemy/Enemy_Data.csv and Map/MapXX/Enemy.dat")
    catalog = read_enemy_catalog(catalog_path)
    maps = [build_map_palette(map_id, path, catalog)
            for map_id, path in discover_maps(map_root, only)]
    visual_maps: dict[str, list[int]] = {}
    for row in maps:
        for visual in row["visuals"]:
            visual_maps.setdefault(visual["sprite"], []).append(row["map_id"])
    return {
        "version": 1,
        "maps": maps,
        "map_count": len(maps),
        "total_spawns": sum(row["spawn_count"] for row in maps),
        "unique_visuals": len(visual_maps),
        "visual_to_maps": {key: value for key, value in sorted(visual_maps.items())},
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("data_root", type=Path,
                        help="Original server Dat root containing Enemy and Map")
    parser.add_argument("output", type=Path)
    parser.add_argument("--map", dest="maps", action="append", type=int,
                        help="Only include this map id; repeatable")
    args = parser.parse_args()
    result = build_palettes(args.data_root.resolve(), set(args.maps) if args.maps else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    summary = [{
        "map": row["map_id"], "spawns": row["spawn_count"],
        "visuals": row["visual_kind_count"], "complete": row["complete"],
    } for row in result["maps"]]
    print(json.dumps({"map_count": result["map_count"],
                      "unique_visuals": result["unique_visuals"],
                      "maps": summary}, ensure_ascii=False))
    return 1 if any(not row["complete"] for row in result["maps"]) else 0


if __name__ == "__main__":
    raise SystemExit(main())
