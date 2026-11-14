#!/usr/bin/env python3
"""Data-driven monster add/update workflow for SGTest.

One recipe validates original SPR/ACT, normalized 8-facing semantics, runtime
stats and spawn collision. Normal mode updates population v2, rebuilds the map
lifecycle bundle, creates a private preview, and mirrors population to server
content. Adding a monster never creates a new production atlas category.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import shutil
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageOps

from animation_definition import compile_definition, validate_definition
from asset_pipeline import build_map_bundle
from build_population import DEFAULT_STATS, validate_stats
from runtime_spr import decode_spr

ACTIONS = ("NS", "NW", "FA", "NH", "ND")
SPRITE_CHARS = set("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    tmp.replace(path)


def validate_recipe(recipe: dict) -> dict:
    if recipe.get("version") != 1:
        raise ValueError("monster recipe version must be 1")
    map_id = recipe.get("map_id")
    definition = recipe.get("definition")
    spawns = recipe.get("spawns")
    if not isinstance(map_id, int) or map_id <= 0:
        raise ValueError("map_id must be a positive integer")
    if not isinstance(definition, dict):
        raise ValueError("definition object is required")
    definition_id = definition.get("id")
    sprite = str(definition.get("sprite", "")).upper()
    if not isinstance(definition_id, int) or not 0 < definition_id <= 0xFFFFFFFF:
        raise ValueError("definition.id must be uint32 and non-zero")
    if not sprite or not sprite.isascii() or any(ch not in SPRITE_CHARS for ch in sprite):
        raise ValueError("definition.sprite must be non-empty ASCII alphanumeric")
    stats = validate_stats(definition.get("stats"))
    if not isinstance(spawns, list) or not spawns:
        raise ValueError("at least one spawn is required")
    normalized_spawns = []
    for index, spawn in enumerate(spawns):
        if not isinstance(spawn, dict):
            raise ValueError(f"spawn {index} must be an object")
        point = spawn.get("p")
        if (not isinstance(point, list) or len(point) != 2 or
                any(not isinstance(value, (int, float)) or isinstance(value, bool)
                    or not math.isfinite(float(value)) for value in point)):
            raise ValueError(f"spawn {index} p must contain two finite numbers")
        flags = spawn.get("source_flags", 0)
        if not isinstance(flags, int) or flags < 0 or flags > 0x7FFFFFFF:
            raise ValueError(f"spawn {index} source_flags must be a non-negative int32")
        normalized_spawns.append({"p": [float(point[0]), float(point[1])], "source_flags": flags})
    replace = recipe.get("replace", False)
    if not isinstance(replace, bool):
        raise ValueError("replace must be boolean when provided")
    return {
        "version": 1,
        "map_id": map_id,
        "definition": {"id": definition_id, "sprite": sprite, "stats": stats},
        "spawns": normalized_spawns,
        "replace": replace,
    }


def compile_monster_definition(source_root: Path, sprite: str) -> dict:
    entity = f"enemy/{sprite.lower()}"
    sources = sorted(source_root.glob(entity + "_*.spr"),
                     key=lambda path: path.as_posix().casefold())
    act = source_root / (entity + ".act")
    if not sources:
        raise ValueError(f"no source SPR found for {sprite}: {source_root / 'enemy'}")
    if not act.is_file():
        raise ValueError(f"missing ACT for {sprite}: {act}")
    definition = compile_definition(
        entity, sources, source_root, "monster", act,
        requested_behaviors=list(ACTIONS),
    )
    validate_definition(definition)
    if set(definition["behaviors"]) != set(ACTIONS):
        raise ValueError(f"{sprite}: expected exactly actions {ACTIONS}")
    if sum(len(row["facings"]) for row in definition["behaviors"].values()) != 40:
        raise ValueError(f"{sprite}: expected 40 logical action/facing views")
    return definition


def collision_grid(path: Path) -> tuple[int, int, bytes]:
    raw = path.read_bytes()
    if len(raw) < 8:
        raise ValueError(f"short collision grid: {path}")
    width, height = struct.unpack_from("<ii", raw)
    if width <= 0 or height <= 0 or width * height > 16_000_000 or len(raw) != 8 + width * height:
        raise ValueError(f"invalid collision grid: {path}")
    return width, height, raw[8:]


def cell_open(grid: tuple[int, int, bytes], x: int, y: int) -> bool:
    width, height, cells = grid
    return 0 <= x < width and 0 <= y < height and (cells[y * width + x] & 7) == 0


def walkable(grid: tuple[int, int, bytes], point: list[float]) -> bool:
    return cell_open(grid, math.floor(point[0] / 16), math.floor(point[1] / 16))


def nearest_walkable(grid: tuple[int, int, bytes], point: list[float]) -> list[float] | None:
    width, height, _ = grid
    ox, oy = math.floor(point[0] / 16), math.floor(point[1] / 16)
    if cell_open(grid, ox, oy):
        return point
    for ring in range(1, 65):
        for y in range(-ring, ring + 1):
            for x in range(-ring, ring + 1):
                if abs(x) != ring and abs(y) != ring:
                    continue
                cx, cy = ox + x, oy + y
                if 0 <= cx < width and 0 <= cy < height and cell_open(grid, cx, cy):
                    return [(cx + .5) * 16, (cy + .5) * 16]
    return None


def validate_spawns(spawns: list[dict], blocking: Path, snap: bool = False) -> list[dict]:
    grid = collision_grid(blocking)
    result = []
    for index, spawn in enumerate(spawns):
        requested = list(spawn["p"])
        actual = requested
        if not walkable(grid, requested):
            suggestion = nearest_walkable(grid, requested)
            if not snap:
                raise ValueError(
                    f"spawn {index} is blocked/outside map at {requested}; "
                    f"nearest walkable={suggestion}"
                )
            if suggestion is None:
                raise ValueError(f"spawn {index} has no walkable cell within 64 collision cells")
            actual = suggestion
        result.append({
            **spawn,
            "p": [float(actual[0]), float(actual[1])],
            "requested_p": [float(requested[0]), float(requested[1])],
            "snapped": actual != requested,
        })
    return result


def normalize_population(document: dict) -> dict:
    version = document.get("version")
    if version not in (1, 2):
        raise ValueError("existing population must be version 1 or 2")
    result = json.loads(json.dumps(document))
    definitions = []
    seen = set()
    for row in result.get("definitions", []):
        definition_id = row.get("id")
        if not isinstance(definition_id, int) or definition_id <= 0 or definition_id in seen:
            raise ValueError("existing population has invalid/duplicate definition id")
        seen.add(definition_id)
        row["stats"] = validate_stats(row.get("stats"))
        definitions.append(row)
    result["version"] = 2
    result["definitions"] = definitions
    return result


def merge_population(population: dict, recipe: dict, spawns: list[dict],
                     replace: bool = False) -> dict:
    result = normalize_population(population)
    definition = recipe["definition"]
    definition_id = definition["id"]
    existing = [row for row in result["definitions"] if int(row["id"]) == definition_id]
    if existing and not replace:
        raise ValueError(f"definition id {definition_id} already exists; use --replace explicitly")
    if existing:
        result["definitions"] = [row for row in result["definitions"]
                                 if int(row["id"]) != definition_id]
        result["spawns"] = [row for row in result["spawns"]
                            if int(row["definition"]) != definition_id]
    result["definitions"].append({
        "id": definition_id,
        "sprite": definition["sprite"],
        "atlas": f"bundles/map/{recipe['map_id']}/sprites.json",
        "stats": definition["stats"],
    })
    result["definitions"].sort(key=lambda row: int(row["id"]))
    first_custom = -1 - sum(1 for row in result["spawns"]
                            if int(row.get("source_index", 0)) < 0)
    for index, spawn in enumerate(spawns):
        result["spawns"].append({
            "definition": definition_id,
            "p": [spawn["p"][0], spawn["p"][1]],
            "source_index": first_custom - index,
            "source_flags": spawn["source_flags"],
        })
    result["policy"] = "population v2: explicit visual identity, runtime stats and validated spawn positions"
    return result


def _frame_cache(source_root: Path, definition: dict) -> dict[str, list]:
    result = {}
    for clip in {view["clip"] for behavior in definition["behaviors"].values()
                 for view in behavior["facings"].values()}:
        relative = definition["clips"][clip]["source"]
        result[clip] = decode_spr((source_root / relative).read_bytes())
    return result


def render_preview(source_root: Path, definition: dict, destination: Path) -> dict:
    frames = _frame_cache(source_root, definition)
    selected = []
    left = right = top = bottom = 0
    for action in ACTIONS:
        behavior = definition["behaviors"][action]
        frame_index = int(behavior["timeline"][0]["frame_index"])
        for facing in range(1, 9):
            view = behavior["facings"][str(facing)]
            frame = frames[view["clip"]][frame_index]
            width, height = frame.image.size
            origin_x = frame.origin_x if not view["flip_h"] else width - frame.origin_x
            left = max(left, origin_x)
            right = max(right, width - origin_x)
            top = max(top, frame.origin_y)
            bottom = max(bottom, height - frame.origin_y)
            selected.append((action, facing, view, frame, origin_x))
    cell_w = max(96, left + right + 24)
    cell_h = max(96, top + bottom + 38)
    canvas = Image.new("RGBA", (cell_w * 8, cell_h * len(ACTIONS)), (24, 26, 30, 255))
    draw = ImageDraw.Draw(canvas)
    for index, (action, facing, view, frame, origin_x) in enumerate(selected):
        row, column = divmod(index, 8)
        cx = column * cell_w + cell_w // 2
        cy = row * cell_h + 22 + top
        image = ImageOps.mirror(frame.image) if view["flip_h"] else frame.image
        canvas.alpha_composite(image, (int(cx - origin_x), int(cy - frame.origin_y)))
        draw.text((column * cell_w + 4, row * cell_h + 3),
                  f"{action}{facing} -> {view['clip'].rsplit('_', 1)[-1]}"
                  + (" flip" if view["flip_h"] else ""), fill=(240, 240, 240, 255))
        draw.line((cx - 4, cy, cx + 4, cy), fill=(255, 196, 64, 255), width=1)
        draw.line((cx, cy - 4, cx, cy + 4), fill=(255, 196, 64, 255), width=1)
    destination.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(destination, compress_level=3)
    return {
        "path": str(destination),
        "size": list(canvas.size),
        "views": len(selected),
        "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
    }


def ensure_runtime_bundle(source_root: Path, assets: Path,
                          population: dict, map_id: int) -> dict:
    """Rebuild the one production sprite bundle owned by this map lifetime."""
    row = build_map_bundle(source_root, assets, population, map_id)
    return {
        "bundle_id": row["bundle_id"],
        "manifest": row["manifest"],
        "pages": row["pages"],
        "rgba_bytes": row["rgba_bytes"],
        "side": row["side"],
        "entities": row["entities"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe", type=Path)
    parser.add_argument("--source", type=Path, default=Path(".cache/client"))
    parser.add_argument("--assets", type=Path, default=Path("client/assets"))
    parser.add_argument("--server-content", type=Path, default=Path("server/content"))
    parser.add_argument("--report-root", type=Path, default=Path("reports/monster_workflow"))
    parser.add_argument("--replace", action="store_true",
                        help="replace existing definition id and all of its spawns")
    parser.add_argument("--snap-spawns", action="store_true",
                        help="snap blocked spawns to nearest open collision cell and report it")
    parser.add_argument("--validate-only", action="store_true",
                        help="validate source/stats/spawns and create preview; do not mutate runtime assets/population")
    args = parser.parse_args()

    recipe_path = args.recipe.resolve()
    source_root = args.source.resolve()
    assets = args.assets.resolve()
    server_content = args.server_content.resolve()
    report_root = args.report_root.resolve()
    recipe_raw = recipe_path.read_bytes()
    recipe = validate_recipe(json.loads(recipe_raw))
    sprite = recipe["definition"]["sprite"]
    definition = compile_monster_definition(source_root, sprite)

    map_root = assets / "maps" / str(recipe["map_id"])
    population_path = map_root / "population.json"
    blocking = map_root / "blocking.bin"
    if not population_path.is_file() or not blocking.is_file():
        raise ValueError(f"map {recipe['map_id']} runtime population/blocking is missing")
    spawns = validate_spawns(recipe["spawns"], blocking, args.snap_spawns)

    entity_report = report_root / sprite.lower()
    entity_report.mkdir(parents=True, exist_ok=True)
    definition_path = entity_report / "definition.json"
    write_json(definition_path, definition)
    preview = render_preview(source_root, definition, entity_report / "preview.png")

    base_population = json.loads(population_path.read_text(encoding="utf-8"))
    candidate_population = merge_population(
        base_population, recipe, spawns, replace=args.replace or recipe["replace"]
    )
    recipe_hash = hashlib.sha256(recipe_raw).hexdigest()
    candidate_population.setdefault("sources", {})[
        f"monster_recipe:{recipe_path.name}"
    ] = recipe_hash

    report = {
        "version": 1,
        "validate_only": args.validate_only,
        "map_id": recipe["map_id"],
        "definition": recipe["definition"],
        "normalized_definition_sha256": definition["content_sha256"],
        "source_files": len(definition["source_sha256"]),
        "behaviors": len(definition["behaviors"]),
        "logical_views": sum(len(row["facings"]) for row in definition["behaviors"].values()),
        "spawns": spawns,
        "preview": preview,
        "population_after": {
            "definitions": len(candidate_population["definitions"]),
            "spawns": len(candidate_population["spawns"]),
        },
        "recipe_sha256": recipe_hash,
    }

    if not args.validate_only:
        bundle = ensure_runtime_bundle(source_root, assets, candidate_population, recipe["map_id"])
        report["bundle"] = bundle
        write_json(population_path, candidate_population)
        destination = server_content / "maps" / str(recipe["map_id"]) / "population.json"
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(population_path, destination)
        if hashlib.sha256(population_path.read_bytes()).digest() != hashlib.sha256(destination.read_bytes()).digest():
            raise ValueError("server population copy hash mismatch")

    write_json(entity_report / "report.json", report)
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
