#!/usr/bin/env python3
"""Compile per-map Enemy preload palettes against a verified atlas catalog.

This joins authoritative content usage (Enemy.dat/Enemy_Data.csv) to visual
asset groups. It does not load every world asset at runtime; each map lists only
the groups/pages its known spawns can require.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


def compile_palettes(map_report: dict, enemy_catalog: dict, atlas_root: Path) -> dict:
    if enemy_catalog.get("quarantined_groups", 0):
        raise ValueError("enemy catalog contains quarantined groups")
    groups = {str(row["enemy"]).upper(): row for row in enemy_catalog.get("groups", [])}
    if not groups:
        raise ValueError("enemy catalog has no groups")

    page_cache: dict[str, tuple[int, int]] = {}
    maps = []
    unresolved = []
    for source_map in map_report.get("maps", []):
        preload_groups = []
        page_paths = set()
        rgba_bytes = 0
        for visual in source_map.get("visuals", []):
            sprite = str(visual["sprite"]).upper()
            group = groups.get(sprite)
            if group is None:
                unresolved.append({"map_id": source_map["map_id"], "sprite": sprite})
                continue
            pages = []
            group_rgba = 0
            for page in group["pages"]:
                page_path = str(page).replace("\\", "/")
                image_path = atlas_root / Path(page_path)
                if not image_path.is_file():
                    raise ValueError(f"missing atlas page: {page_path}")
                if page_path not in page_cache:
                    with Image.open(image_path) as image:
                        page_cache[page_path] = image.size
                width, height = page_cache[page_path]
                bytes_ = width * height * 4
                group_rgba += bytes_
                pages.append(page_path)
                page_paths.add(page_path)
            rgba_bytes += group_rgba
            preload_groups.append({
                "visual_id": sprite,
                "group_key": f"Enemy/{sprite}",
                "spawn_count": int(visual["spawn_count"]),
                "page_count": len(pages),
                "rgba_area_bytes": group_rgba,
                "pages": pages,
            })
        maps.append({
            "map_id": int(source_map["map_id"]),
            "spawn_count": int(source_map["spawn_count"]),
            "visual_kind_count": len(preload_groups),
            "preload_group_count": len(preload_groups),
            "atlas_page_count": len(page_paths),
            "estimated_rgba_area_bytes": rgba_bytes,
            "preload_groups": preload_groups,
            "complete": len(preload_groups) == int(source_map["visual_kind_count"]),
        })

    return {
        "version": 1,
        "policy": "map_enemy_visual_palette_v1",
        "atlas_pages_relative_to": "atlas_root",
        "map_count": len(maps),
        "maps": maps,
        "unresolved": unresolved,
        "complete": not unresolved and all(row["complete"] for row in maps),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("map_palette", type=Path)
    parser.add_argument("enemy_catalog", type=Path)
    parser.add_argument("atlas_root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    map_report = json.loads(args.map_palette.read_text(encoding="utf-8"))
    enemy_catalog = json.loads(args.enemy_catalog.read_text(encoding="utf-8"))
    atlas_root = args.atlas_root.resolve()
    if not atlas_root.is_dir():
        parser.error("atlas root does not exist")
    result = compile_palettes(map_report, enemy_catalog, atlas_root)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    summary = [{
        "map": row["map_id"],
        "spawns": row["spawn_count"],
        "visuals": row["visual_kind_count"],
        "pages": row["atlas_page_count"],
        "rgba_mb": round(row["estimated_rgba_area_bytes"] / 1024 / 1024, 3),
    } for row in result["maps"]]
    print(json.dumps({"complete": result["complete"], "maps": summary},
                     ensure_ascii=False))
    return 0 if result["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
