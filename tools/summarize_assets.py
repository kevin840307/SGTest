"""Summarize the production per-asset runtime asset root and reject legacy bundles."""
from __future__ import annotations

import argparse
import json
from collections import Counter
from pathlib import Path

ALLOWED_TOP_LEVEL = {
    "catalog", "maps", "Enemy", "npcs", "players", "equipment",
    "pets", "mecha", "items", "effects", "ui", "audio", "data", ".gdignore",
}


def _load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def summarize(assets: Path) -> dict:
    assets = assets.resolve()
    if not assets.is_dir():
        raise ValueError(f"asset root missing: {assets}")

    files = [path for path in assets.rglob("*") if path.is_file()]
    top: dict[str, dict[str, int]] = {}
    for path in files:
        relative = path.relative_to(assets)
        key = relative.parts[0]
        row = top.setdefault(key, {"files": 0, "bytes": 0})
        row["files"] += 1
        row["bytes"] += path.stat().st_size

    root_catalog_path = assets / "catalog/assets.json"
    root_catalog = _load(root_catalog_path) if root_catalog_path.is_file() else {}
    categories = {}
    catalog_errors = []
    ownership = Counter()
    total_catalog_assets = 0

    if root_catalog.get("schema") != "sgtest.asset_catalog_root/v1":
        catalog_errors.append("missing or unsupported catalog/assets.json")
    for category, row in sorted(root_catalog.get("categories", {}).items()):
        index_path = assets / str(row.get("index", ""))
        if not index_path.is_file():
            catalog_errors.append(f"missing category index: {category}")
            continue
        index = _load(index_path)
        entries = index.get("assets", {})
        if index.get("schema") != "sgtest.asset_catalog/v1" or not isinstance(entries, dict):
            catalog_errors.append(f"invalid category index: {category}")
            continue
        declared = int(row.get("assets", -1))
        if declared != len(entries):
            catalog_errors.append(
                f"category count mismatch: {category} declared={declared} actual={len(entries)}"
            )
        for asset_id, asset in entries.items():
            file = str(asset.get("file", ""))
            if file.startswith("bundles/"):
                catalog_errors.append(f"legacy bundle reference: {asset_id}")
            ownership[str(asset.get("ownership", "unspecified"))] += 1
        categories[category] = {
            "assets": len(entries),
            "index": index_path.relative_to(assets).as_posix(),
        }
        total_catalog_assets += len(entries)

    maps = []
    cross_asset_bundle = False
    for world_path in sorted((assets / "maps").glob("*/world.json")):
        world = _load(world_path)
        if world.get("sprite_bundle"):
            cross_asset_bundle = True
        object_assets = {
            str(row.get("atlas", ""))
            for row in world.get("objects", [])
            if row.get("atlas")
        }
        population_path = world_path.with_name("population.json")
        monster_assets = set()
        if population_path.is_file():
            population = _load(population_path)
            for definition in population.get("definitions", []):
                atlas = str(definition.get("atlas", ""))
                if atlas:
                    monster_assets.add(atlas)
                    if not atlas.startswith("Enemy/"):
                        cross_asset_bundle = True
        maps.append({
            "map_id": int(world["map_id"]),
            "objects": len(world.get("objects", [])),
            "visual_assets": len(object_assets),
            "monster_assets": len(monster_assets),
            "terrain_bytes": world_path.with_name("terrain.png").stat().st_size,
            "collision_bytes": world_path.with_name("blocking.dat").stat().st_size,
            "tiles_bytes": world_path.with_name("tiles.bin").stat().st_size,
        })

    unexpected = sorted(name for name in top if name not in ALLOWED_TOP_LEVEL)
    production_clean = (
        not unexpected
        and not catalog_errors
        and not cross_asset_bundle
        and bool(categories)
        and bool(maps)
    )
    return {
        "version": 3,
        "asset_root": assets.as_posix(),
        "files": len(files),
        "disk_bytes": sum(path.stat().st_size for path in files),
        "top_level": dict(sorted(top.items())),
        "catalog_assets": total_catalog_assets,
        "categories": categories,
        "ownership": dict(sorted(ownership.items())),
        "maps": maps,
        "cross_asset_bundle": cross_asset_bundle,
        "catalog_errors": catalog_errors,
        "unexpected_top_level": unexpected,
        "production_clean": production_clean,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=Path("client/assets"))
    parser.add_argument("--output", type=Path, default=Path("reports/asset_inventory.json"))
    args = parser.parse_args()
    report = summarize(args.assets)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["production_clean"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
