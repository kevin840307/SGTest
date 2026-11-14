#!/usr/bin/env python3
"""Unified lifecycle-bundle builder for SGTest runtime sprite assets.

Public runtime grouping is by ownership/lifetime, never by direction, action,
source file size, or historical experiment name. Normalized animation v2 owns
semantic truth; this module only chooses page placement.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from animation_definition import compile_definition, validate_definition
from build_atlas_groups import pack_source_blocks
from runtime_animation_semantics import logical_views
from runtime_spr import decode_spr

SIDES = (512, 1024, 2048, 4096)
MONSTER_ACTIONS = ("NS", "NW", "FA", "NH", "ND")


def _write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def compile_monster(source: Path, sprite: str) -> dict:
    sprite = sprite.upper()
    entity = f"enemy/{sprite.lower()}"
    sources = sorted(source.glob(entity + "_*.spr"), key=lambda p: p.as_posix().casefold())
    act = source / (entity + ".act")
    if not sources or not act.is_file():
        raise ValueError(f"{sprite}: missing source SPR/ACT")
    definition = compile_definition(
        entity, sources, source, "monster", act,
        requested_behaviors=list(MONSTER_ACTIONS),
    )
    validate_definition(definition)
    return definition


def compile_player_base(source: Path) -> list[dict]:
    names = source / "player/charactionname.dat"
    draw = source / "player/chardraworder.dat"
    act = source / "player/b/boy.act"
    result = []
    for part in ("body", "leg", "head", "hair00", "l-arm", "r-arm"):
        entity = f"player/b/standard/b-{part}"
        sources = sorted(source.glob(entity + "_*.spr"), key=lambda p: p.as_posix().casefold())
        definition = compile_definition(
            entity, sources, source, "character_part", act, names, draw
        )
        validate_definition(definition)
        result.append(definition)
    entity = "player/b/standard/b-death"
    sources = sorted(source.glob(entity + "_*.spr"), key=lambda p: p.as_posix().casefold())
    death = compile_definition(
        entity, sources, source, "single_view",
        source / "player/b/b-death.act", act_bindings={"ND": "Effect"},
    )
    validate_definition(death)
    result.append(death)
    return result


def _clip_sources(definition: dict) -> dict[str, str]:
    used = {view["clip"] for behavior in definition["behaviors"].values()
            for view in behavior["facings"].values()}
    return {clip: definition["clips"][clip]["source"] for clip in sorted(used)}


def _inspection_index(definition: dict) -> dict[str, dict[str, str]]:
    """Keep per-entity action/facing lookup independent from physical packing."""
    prefixes = {view["clip"].rsplit("_", 1)[0]
                for behavior in definition["behaviors"].values()
                for view in behavior["facings"].values()}
    if len(prefixes) != 1:
        raise ValueError("normalized definition has multiple clip prefixes")
    prefix = next(iter(prefixes))
    return {
        behavior_code: {
            str(facing): f"{prefix}_{behavior_code}{facing}"
            for facing in sorted(behavior["facings"], key=int)
        }
        for behavior_code, behavior in sorted(definition["behaviors"].items())
    }


def collect_bundle(definitions: list[dict], source_root: Path,
                   raw_sources: list[Path] | None = None) -> dict:
    """Merge normalized entities plus optional untimed raw SPR clips.

    Raw clips are for map decoration or other assets without ACT semantics. They
    keep original frame order/pivot and use EntityAtlas' deterministic untimed
    playback; they do not create a new runtime asset type.
    """
    raw_sources = raw_sources or []
    if not definitions and not raw_sources:
        raise ValueError("bundle needs at least one normalized definition or raw SPR")
    frames = []
    ranges = []
    clips: dict[str, list[int]] = {}
    source_to_indices: dict[str, list[int]] = {}
    sources = []
    merged_views = {}
    entities = []
    raw_clips = []

    for definition in definitions:
        validate_definition(definition)
        if int(definition["version"]) != 2:
            raise ValueError("bundle requires sgtest.animation/v2")
        entity_views = logical_views(definition)
        overlap = set(merged_views).intersection(entity_views)
        if overlap:
            raise ValueError(f"duplicate logical clip(s): {sorted(overlap)[:3]}")
        merged_views.update(entity_views)

        physical = _clip_sources(definition)
        for clip, relative in physical.items():
            normalized_ids = definition["clips"][clip]["frames"]
            if relative not in source_to_indices:
                path = source_root / relative
                raw = path.read_bytes()
                decoded = decode_spr(raw)
                if len(decoded) != len(normalized_ids):
                    raise ValueError(f"{clip}: source frame count mismatch")
                start = len(frames)
                indices = []
                for source_index, (frame, normalized_id) in enumerate(zip(decoded, normalized_ids)):
                    metadata = definition["frames"][normalized_id]
                    if metadata["source"] != relative or int(metadata["source_index"]) != source_index:
                        raise ValueError(f"{clip}: normalized source mapping mismatch")
                    if [frame.origin_x, frame.origin_y] != metadata["pivot"]:
                        raise ValueError(f"{clip}: normalized pivot mismatch")
                    bounds = frame.image.getbbox()
                    pixels = frame.image.crop(bounds) if bounds else None
                    digest = hashlib.sha256(pixels.tobytes()).hexdigest() if pixels else None
                    if digest != metadata["pixel_sha256"]:
                        raise ValueError(f"{clip}: normalized pixel hash mismatch")
                    frames.append(frame)
                    indices.append(len(frames) - 1)
                source_to_indices[relative] = indices
                ranges.append((start, len(decoded)))
                sources.append({
                    "path": relative,
                    "sha256": hashlib.sha256(raw).hexdigest(),
                    "frames": len(decoded),
                })
            indices = source_to_indices[relative]
            if len(indices) != len(normalized_ids):
                raise ValueError(f"{clip}: shared source frame count mismatch")
            clips[clip] = indices

        entities.append({
            "entity": definition["entity"],
            "definition_sha256": definition["content_sha256"],
            "logical_views": len(entity_views),
            "inspect": _inspection_index(definition),
            "physical_clips": sorted(physical),
        })

    for path in sorted(raw_sources, key=lambda value: value.as_posix().casefold()):
        path = path.resolve()
        if not path.is_relative_to(source_root.resolve()):
            raise ValueError(f"raw source outside source root: {path}")
        relative = path.relative_to(source_root.resolve()).as_posix()
        clip = path.stem.upper()
        if clip in clips:
            raise ValueError(f"duplicate physical clip name in bundle: {clip}")
        raw = path.read_bytes()
        decoded = decode_spr(raw)
        start = len(frames)
        indices = []
        for frame in decoded:
            frames.append(frame)
            indices.append(len(frames) - 1)
        ranges.append((start, len(decoded)))
        clips[clip] = indices
        source_to_indices[relative] = indices
        sources.append({
            "path": relative,
            "sha256": hashlib.sha256(raw).hexdigest(),
            "frames": len(decoded),
        })
        raw_clips.append({"clip": clip, "source": relative, "frames": len(decoded)})

    return {
        "frames": frames,
        "ranges": ranges,
        "clips": clips,
        "logical_views": merged_views,
        "sources": sources,
        "entities": entities,
        "raw_clips": raw_clips,
    }


def measure_bundle(collected: dict) -> list[dict]:
    rows = []
    for side in SIDES:
        try:
            pages, _ = pack_source_blocks(collected["frames"], collected["ranges"], side)
        except ValueError as exc:
            rows.append({"side": side, "valid": False, "reason": str(exc)})
            continue
        rgba = sum(page.width * page.height * 4 for page in pages)
        rows.append({
            "side": side,
            "valid": True,
            "pages": len(pages),
            "rgba_bytes": rgba,
            "page_sizes": [list(page.size) for page in pages],
        })
    return rows


def choose_candidate(candidates: list[dict], overhead: float = 0.05,
                     same_page_memory_tolerance: float = 0.02,
                     auto_max_side: int = 2048) -> dict:
    """Pick by measured memory/page cost; never expose size as a runtime type.

    Auto mode first stays within the measured <=2048 pool when it is valid,
    then stays within a small overhead of that pool's minimum decoded area and
    minimizes page count. 4096 remains measured/researchable and is an automatic
    fallback only when smaller sides cannot hold an atomic source block.
    """
    valid = [row for row in candidates if row.get("valid")]
    if not valid:
        raise ValueError("no atlas side can hold the bundle")
    if overhead < 0 or same_page_memory_tolerance < 0 or auto_max_side < 1:
        raise ValueError("candidate policy values must be valid")
    # 4096 remains measured and can be forced for research, but current real
    # lifecycle/render A/B shows no frame/load gain over 2048 and a much larger
    # transient decode workspace. Auto mode therefore uses <=2048 whenever it
    # can hold every atomic source block; 4096 is a correctness fallback.
    preferred = [row for row in valid if int(row["side"]) <= auto_max_side]
    pool = preferred or valid
    minimum = min(row["rgba_bytes"] for row in pool)
    allowed = [row for row in pool if row["rgba_bytes"] <= minimum * (1.0 + overhead)]
    fewest_pages = min(row["pages"] for row in allowed)
    page_candidates = [row for row in allowed if row["pages"] == fewest_pages]
    page_minimum = min(row["rgba_bytes"] for row in page_candidates)
    near_equal = [row for row in page_candidates
                  if row["rgba_bytes"] <= page_minimum * (1.0 + same_page_memory_tolerance)]
    return min(near_equal, key=lambda row: (row["side"], row["rgba_bytes"]))


def build_bundle(definitions: list[dict], source_root: Path, destination: Path,
                 bundle_id: str, scope: str, side: int | None = None,
                 overhead: float = 0.05,
                 raw_sources: list[Path] | None = None) -> dict:
    if scope not in ("map", "session", "on-demand"):
        raise ValueError("scope must be map/session/on-demand")
    collected = collect_bundle(definitions, source_root, raw_sources)
    candidates = measure_bundle(collected)
    chosen = next((row for row in candidates if row.get("valid") and row["side"] == side), None) if side else None
    if side is not None and chosen is None:
        raise ValueError(f"requested side {side} cannot hold bundle")
    chosen = chosen or choose_candidate(candidates, overhead)
    pages, records = pack_source_blocks(collected["frames"], collected["ranges"], int(chosen["side"]))

    manifest_frames = []
    for record in records:
        crop = record["crop"]
        manifest_frames.append({
            "page": int(record["page"]),
            "rect": list(record["rect"]),
            "size": list(record["size"]),
            "origin": list(record["raw_origin"]),
            "trim": [int(crop[0]), int(crop[1])],
        })

    destination.parent.mkdir(parents=True, exist_ok=True)
    page_names = []
    for index, page in enumerate(pages):
        name = f"{destination.stem}_{index}.png"
        target = destination.parent / name
        temporary = target.with_name(target.name + ".tmp")
        page.save(temporary, format="PNG", compress_level=3)
        temporary.replace(target)
        page_names.append(name)

    manifest = {
        "version": 9,
        "bundle_version": 1,
        "bundle_id": bundle_id,
        "bundle_scope": scope,
        "packing_policy": "lifecycle_bundle_clip_atomic_v1",
        "semantic_source": "sgtest.animation/v2",
        "normalized_schema_version": 2,
        "atlas_side": int(chosen["side"]),
        "pages": page_names,
        "page_sizes": [list(page.size) for page in pages],
        "frames": manifest_frames,
        "clips": collected["clips"],
        "logical_views": collected["logical_views"],
        "aliases": {},
        "act": {},
        "sources": collected["sources"],
        "entities": collected["entities"],
        "raw_clips": collected["raw_clips"],
        "candidate_measurements": candidates,
    }
    manifest["semantic_sha256"] = hashlib.sha256(
        json.dumps({
            "entities": [(row["entity"], row["definition_sha256"]) for row in collected["entities"]],
            "logical_views": collected["logical_views"],
            "raw_clips": collected["raw_clips"],
            "sources": [(row["path"], row["sha256"]) for row in collected["sources"]],
        }, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    _write_json(destination, manifest)
    return {
        "bundle_id": bundle_id,
        "scope": scope,
        "manifest": destination.as_posix(),
        "side": int(chosen["side"]),
        "pages": len(pages),
        "rgba_bytes": sum(page.width * page.height * 4 for page in pages),
        "png_bytes": sum((destination.parent / name).stat().st_size for name in page_names),
        "entities": [row["entity"] for row in collected["entities"]],
        "raw_clips": [row["clip"] for row in collected["raw_clips"]],
        "candidate_measurements": candidates,
    }


def map_definitions(source: Path, population: dict) -> list[dict]:
    return [compile_monster(source, str(row["sprite"])) for row in population["definitions"]]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(".cache/client"))
    parser.add_argument("--assets", type=Path, default=Path("client/assets"))
    parser.add_argument("--map", type=int, default=13)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--scope", choices=("map", "session"), required=True)
    parser.add_argument("--side", type=int, choices=SIDES)
    parser.add_argument("--memory-overhead", type=float, default=0.05)
    args = parser.parse_args()

    source = args.source.resolve()
    assets = args.assets.resolve()
    raw_sources: list[Path] = []
    if args.scope == "map":
        population = json.loads((assets / f"maps/{args.map}/population.json").read_text(encoding="utf-8"))
        definitions = map_definitions(source, population)
        world_path = assets / f"maps/{args.map}/world.json"
        if world_path.is_file():
            world = json.loads(world_path.read_text(encoding="utf-8"))
            raw_sources = sorted({source / str(row["source"]) for row in world.get("objects", [])},
                                 key=lambda value: value.as_posix().casefold())
        output = args.output or (assets / f"bundles/map/{args.map}/sprites.json")
        bundle_id = f"map:{args.map}:sprites"
    else:
        definitions = compile_player_base(source)
        output = args.output or (assets / "bundles/session/player-b-base.json")
        bundle_id = "session:player:b:base"

    result = build_bundle(
        definitions, source, output.resolve(), bundle_id, args.scope,
        side=args.side, overhead=args.memory_overhead, raw_sources=raw_sources,
    )
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
