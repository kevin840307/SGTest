"""Summarize the production runtime asset root and flag accidental bulk outputs."""
from __future__ import annotations

import argparse
import json
from pathlib import Path


ALLOWED_TOP_LEVEL = {"bundles", "maps", ".gdignore"}


def summarize(assets: Path) -> dict:
    assets = assets.resolve()
    if not assets.is_dir():
        raise ValueError(f"asset root missing: {assets}")
    files = [path for path in assets.rglob("*") if path.is_file()]
    top = {}
    for path in files:
        relative = path.relative_to(assets)
        key = relative.parts[0]
        row = top.setdefault(key, {"files": 0, "bytes": 0})
        row["files"] += 1
        row["bytes"] += path.stat().st_size

    index_path = assets / "bundles/index.json"
    index = json.loads(index_path.read_text(encoding="utf-8")) if index_path.is_file() else {}
    bundles = []
    for row in index.get("bundles", []):
        manifest_path = assets / str(row["manifest"])
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        bundles.append({
            "id": row["id"],
            "scope": row["scope"],
            "manifest": row["manifest"],
            "side": int(manifest.get("atlas_side", 0)),
            "pages": len(manifest.get("pages", [])),
            "rgba_bytes": sum(int(w) * int(h) * 4 for w, h in manifest.get("page_sizes", [])),
            "normalized_entities": len(manifest.get("entities", [])),
            "raw_clips": len(manifest.get("raw_clips", [])),
            "sources": len(manifest.get("sources", [])),
        })

    maps = []
    for world_path in sorted((assets / "maps").glob("*/world.json")):
        world = json.loads(world_path.read_text(encoding="utf-8"))
        maps.append({
            "map_id": int(world["map_id"]),
            "objects": len(world.get("objects", [])),
            "object_clips": len({str(row.get("clip", "")) for row in world.get("objects", [])}),
            "sprite_bundle": world.get("sprite_bundle"),
            "terrain_bytes": world_path.with_name("terrain.png").stat().st_size,
            "collision_bytes": world_path.with_name("blocking.bin").stat().st_size,
            "tiles_bytes": world_path.with_name("tiles.bin").stat().st_size,
        })

    unexpected = sorted(name for name in top if name not in ALLOWED_TOP_LEVEL)
    return {
        "version": 2,
        "asset_root": assets.as_posix(),
        "files": len(files),
        "disk_bytes": sum(path.stat().st_size for path in files),
        "top_level": dict(sorted(top.items())),
        "bundles": bundles,
        "maps": maps,
        "unexpected_top_level": unexpected,
        "production_clean": not unexpected and bool(bundles) and bool(maps),
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
