#!/usr/bin/env python3
"""Compile verified Enemy ACT/facing references into Godot atlas regions.

The catalog is visual-only and keeps timing as raw legacy update counts. Runtime
milliseconds, hit events, origin transforms and HK2-vs-reference parity remain
unverified. Original and derived media stay in the private atlas directory.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image


def compile_one(audit: dict, manifest_path: Path, atlas_root: Path) -> dict:
    if not audit["complete_source_mapping"]:
        raise ValueError("enemy source mapping has unresolved references")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("group_key") != f'Enemy/{audit["enemy"]}':
        raise ValueError("atlas group key does not match enemy")
    pages = [manifest_path.parent / name for name in manifest["pages"]]
    dimensions = []
    for page in pages:
        with Image.open(page) as image:
            dimensions.append(image.size)
    source_hashes = {Path(row["path"]).name: row["sha256"] for row in manifest["sources"]}
    for name, source in audit["sources"].items():
        if source_hashes.get(name) != source["sha256"]:
            raise ValueError(f"source hash differs: {name}")
    regions = {}
    for record in manifest["frames"]:
        key = (Path(record["source"]).name, record["source_frame"])
        if key in regions:
            raise ValueError(f"duplicate atlas source frame: {key}")
        page_index = record["page"]
        if page_index >= 0:
            if page_index >= len(pages):
                raise ValueError("atlas page index out of range")
            x, y, w, h = record["rect"]
            page_w, page_h = dimensions[page_index]
            if min(x, y, w, h) < 0 or x + w > page_w or y + h > page_h:
                raise ValueError("atlas region outside image")
        regions[key] = record
    actions = []
    for action in audit["actions"]:
        directions = []
        for direction in action["directions"]:
            source = direction["source"]
            frames = []
            for step in action["frames"]:
                key = (source, step["index"])
                if key not in regions:
                    raise ValueError(f"atlas missing source frame: {key}")
                region = regions[key]
                frames.append({"page": region["page"], "rect": region["rect"],
                               "raw_origin": region["raw_origin"], "crop": region["crop"],
                               "size": region["size"], "source_frame": step["index"],
                               "raw_time": step["raw_time"],
                               "legacy_default_update_calls": step["legacy_default_update_calls"]})
            directions.append({"facing": direction["facing"],
                               "source_direction": direction["source_direction"],
                               "flip_x": direction["flip_x"], "frames": frames})
        if len(directions) != 8:
            raise ValueError("incomplete logical facing set")
        actions.append({"key": action["key"], "action_id": action["action_id"],
                        "loops": action["loops"], "events": action["events"],
                        "special_init_path": action["special_init_path"],
                        "directions": directions})
    return {"enemy": audit["enemy"], "act_sha256": audit["act_sha256"],
            "pages": [page.relative_to(atlas_root).as_posix() for page in pages],
            "actions": actions, "time_unit": None, "visual_alignment_verified": False}


def compile_catalog(audit_report: dict, atlas_root: Path) -> dict:
    by_enemy = {row["enemy"]: row for row in audit_report["groups"]}
    compiled = []
    quarantined = []
    for manifest_path in sorted((atlas_root / "Enemy").glob("*/group.atlas.json")):
        name = manifest_path.parent.name
        audit = by_enemy.get(name)
        if audit is None:
            quarantined.append({"enemy": name, "reason": "no_act_audit"})
            continue
        try:
            compiled.append(compile_one(audit, manifest_path, atlas_root))
        except (OSError, ValueError, KeyError, IndexError) as exc:
            quarantined.append({"enemy": name, "reason": str(exc)})
    return {"version": 1, "page_paths_relative_to": "atlas_root",
            "animation_timing_verified": False,
            "compiled_groups": len(compiled), "quarantined_groups": len(quarantined),
            "groups": compiled, "quarantined": quarantined}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("audit", type=Path, help="Private enemy-animation-audit.json")
    parser.add_argument("atlas_root", type=Path, help="Private atlas candidate root")
    parser.add_argument("output", type=Path, help="Godot catalog JSON, within atlas root")
    args = parser.parse_args()
    atlas_root, output = args.atlas_root.resolve(), args.output.resolve()
    if not atlas_root.is_dir() or not (output == atlas_root or atlas_root in output.parents):
        parser.error("catalog output must be inside the private atlas root")
    audit = json.loads(args.audit.read_text(encoding="utf-8"))
    result = compile_catalog(audit, atlas_root)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    print(json.dumps({"compiled_groups": result["compiled_groups"],
                      "quarantined_groups": result["quarantined_groups"],
                      "quarantined": result["quarantined"][:10]}, ensure_ascii=False, indent=2))
    return 1 if result["quarantined"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
