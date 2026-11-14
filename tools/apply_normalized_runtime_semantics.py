#!/usr/bin/env python3
"""Apply normalized v2 animation semantics to private runtime atlas manifests.

PNG pages/rectangles stay unchanged. This only adds logical facing/timing data
so physical SPR availability can never override the audited original mapping.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from animation_definition import compile_definition
from runtime_animation_semantics import apply_overlay


def recipes(source: Path, runtime_assets: Path) -> list[dict]:
    population = json.loads((runtime_assets / "maps/13/population.json").read_text(encoding="utf-8"))
    result = []
    for row in population["definitions"]:
        key = Path(row["atlas"]).with_suffix("").as_posix()
        result.append({
            "entity": key, "kind": "monster", "act": source / f"{key}.act",
            "sources": list(source.glob(key + "_*.spr")),
        })
    for part in ["body", "leg", "head", "hair00", "l-arm", "r-arm"]:
        key = f"player/b/standard/b-{part}"
        result.append({
            "entity": key, "kind": "character_part", "act": source / "player/b/boy.act",
            "sources": list(source.glob(key + "_*.spr")),
            "character_actions": source / "player/charactionname.dat",
            "draw_order_path": source / "player/chardraworder.dat",
        })
    key = "player/b/standard/b-death"
    result.append({
        "entity": key, "kind": "single_view", "act": source / "player/b/b-death.act",
        "sources": list(source.glob(key + "_*.spr")), "act_bindings": {"ND": "Effect"},
    })
    return result


def compile_recipe(recipe: dict, source: Path) -> dict:
    parameters = {key: value for key, value in recipe.items()
                  if key not in ("entity", "act")}
    return compile_definition(recipe["entity"], root=source, act_path=recipe["act"], **parameters)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(".cache/client"))
    parser.add_argument("--runtime-assets", type=Path, default=Path("client/assets/generated"))
    args = parser.parse_args()
    source = args.source.resolve()
    runtime = args.runtime_assets.resolve()
    if not source.is_dir() or not runtime.is_dir():
        parser.error("source and runtime asset roots must exist")
    if runtime == source or runtime.is_relative_to(source):
        parser.error("runtime output must stay outside read-only original source")

    records = []
    for recipe in recipes(source, runtime):
        definition = compile_recipe(recipe, source)
        manifest_path = runtime / (recipe["entity"] + ".json")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        patched = apply_overlay(definition, manifest)
        temp = manifest_path.with_name(manifest_path.name + ".tmp")
        temp.write_text(json.dumps(patched, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
        temp.replace(manifest_path)
        records.append({
            "entity": recipe["entity"],
            "logical_views": len(patched["logical_views"]),
            "semantic_sha256": patched["semantic_sha256"],
        })
    print(json.dumps({
        "patched": len(records),
        "logical_views": sum(row["logical_views"] for row in records),
        "entities": records,
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
