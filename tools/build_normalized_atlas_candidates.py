#!/usr/bin/env python3
"""Build Map13 atlas candidates from sgtest.animation/v2 definitions.

Animation semantics come only from normalized definitions. Packing is allowed
to change page/rect placement, never logical facing, ACT timing, pivot or
paper-doll metadata. Each physical source clip (one original SPR) is atomic.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path

if __package__:
    from .animation_definition import validate_definition
    from .build_atlas_groups import pack_source_blocks
    from .runtime_spr import decode_spr
else:
    from animation_definition import validate_definition
    from build_atlas_groups import pack_source_blocks
    from runtime_spr import decode_spr

SIDES = (512, 1024, 2048, 4096)


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temp.replace(path)


def semantic_view(definition: dict) -> dict:
    """Compile logical requested clips without trusting physical filename presence."""
    validate_definition(definition)
    canonical = set()
    aliases = {}
    act = {}
    prefixes = set()
    for behavior_code, behavior in sorted(definition["behaviors"].items()):
        for view in behavior["facings"].values():
            canonical.add(view["clip"])
            prefixes.add(view["clip"].rsplit("_", 1)[0])
        action = {
            "timeline": [[step["frame_index"], step["raw_time"]] for step in behavior["timeline"]],
            "loop": bool(behavior["loop"]),
            "events": behavior.get("events", []),
            "normalized_behavior": behavior_code,
        }
        key = behavior["act_key"]
        if key in act and act[key] != action:
            raise ValueError(f"ACT key maps to multiple normalized timelines: {key}")
        act[key] = action
    if len(prefixes) != 1:
        raise ValueError("definition contains multiple clip prefixes")
    prefix = next(iter(prefixes))
    for behavior_code, behavior in sorted(definition["behaviors"].items()):
        for facing, view in sorted(behavior["facings"].items(), key=lambda item: int(item[0])):
            requested = f"{prefix}_{behavior_code}{facing}"
            source = view["clip"]
            flip = bool(view["flip_h"])
            if requested == source and flip:
                raise ValueError("normalized facing requires self-alias flip unsupported by runtime manifest")
            if requested != source or flip:
                aliases[requested] = {"clip": source, "flip_h": flip}
    return {
        "canonical_clips": sorted(canonical),
        "aliases": aliases,
        "act": act,
        "prefix": prefix,
    }


def classify_tiers(population: dict) -> dict[str, dict]:
    """Classify co-occurring Map13 visuals by cumulative spawn coverage."""
    definitions = {int(row["id"]): Path(row["atlas"]).with_suffix("").as_posix()
                   for row in population["definitions"]}
    counts = Counter(int(row["definition"]) for row in population["spawns"])
    rows = sorted(((definitions[key], counts[key]) for key in definitions),
                  key=lambda row: (-row[1], row[0]))
    total = sum(count for _, count in rows)
    cumulative = 0
    result = {}
    for entity, count in rows:
        before = cumulative / total if total else 0.0
        cumulative += count
        # Include the item that crosses the boundary in the lower-latency tier.
        tier = "hot" if before < 0.75 else ("warm" if before < 0.95 else "cold")
        result[entity] = {
            "spawn_count": count,
            "tier": tier,
            "cumulative_fraction": cumulative / total if total else 0.0,
        }
    return result


def choose_candidate(candidates: list[dict], policy: str, tier: str = "warm",
                     preferred_side: int | None = None) -> dict:
    valid = [row for row in candidates if row.get("valid")]
    if not valid:
        raise ValueError("no valid atlas candidate")
    if preferred_side is not None:
        for row in sorted(valid, key=lambda row: row["side"]):
            if row["side"] >= preferred_side:
                return row
        return valid[-1]
    if policy == "memory":
        return min(valid, key=lambda row: (row["rgba_bytes"], row["pages"], row["side"]))
    if policy == "pages":
        return min(valid, key=lambda row: (row["pages"], row["rgba_bytes"], row["side"]))
    if policy != "balanced":
        raise ValueError(f"unknown policy: {policy}")
    minimum = min(row["rgba_bytes"] for row in valid)
    overhead = {"hot": 0.08, "warm": 0.03, "cold": 0.0}[tier]
    allowed = [row for row in valid if row["rgba_bytes"] <= minimum * (1.0 + overhead)]
    return min(allowed, key=lambda row: (row["pages"], row["rgba_bytes"], row["side"]))


def load_physical_clips(definition: dict, source_root: Path, semantic: dict):
    frames = []
    ranges = []
    clip_indices = {}
    sources = []
    for clip_name in semantic["canonical_clips"]:
        clip = definition["clips"][clip_name]
        source_relative = clip["source"]
        path = source_root / source_relative
        decoded = decode_spr(path.read_bytes())
        normalized_ids = clip["frames"]
        if len(decoded) != len(normalized_ids):
            raise ValueError(f"{clip_name}: decoded frame count differs from normalized definition")
        start = len(frames)
        indices = []
        for ordinal, (frame, normalized_id) in enumerate(zip(decoded, normalized_ids)):
            metadata = definition["frames"][normalized_id]
            if metadata["source"] != source_relative or int(metadata["source_index"]) != ordinal:
                raise ValueError(f"{clip_name}: normalized source frame mismatch")
            bounds = frame.image.getbbox()
            pixels = frame.image.crop(bounds) if bounds else None
            digest = hashlib.sha256(pixels.tobytes()).hexdigest() if pixels else None
            if digest != metadata["pixel_sha256"]:
                raise ValueError(f"{clip_name}: normalized pixel hash mismatch")
            if [frame.origin_x, frame.origin_y] != metadata["pivot"]:
                raise ValueError(f"{clip_name}: normalized pivot mismatch")
            frames.append(frame)
            indices.append(len(frames) - 1)
        ranges.append((start, len(decoded)))
        clip_indices[clip_name] = indices
        sources.append(source_relative)
    return frames, ranges, clip_indices, sources


def measure_entity(definition: dict, source_root: Path) -> tuple[dict, dict]:
    semantic = semantic_view(definition)
    frames, ranges, _, _ = load_physical_clips(definition, source_root, semantic)
    candidates = []
    for side in SIDES:
        try:
            pages, _ = pack_source_blocks(frames, ranges, side)
        except ValueError as exc:
            candidates.append({"side": side, "valid": False, "reason": str(exc)})
            continue
        candidates.append({
            "side": side,
            "valid": True,
            "pages": len(pages),
            "rgba_bytes": sum(page.width * page.height * 4 for page in pages),
            "page_sizes": [list(page.size) for page in pages],
        })
    return semantic, {"candidates": candidates}


def validate_runtime_semantics(definition: dict, manifest: dict) -> None:
    """Simulate current EntityAtlas direct-clip/alias resolution for all facings."""
    if int(manifest.get("normalized_schema_version", 0)) != 2:
        raise ValueError("runtime manifest is not bound to normalized schema v2")
    prefixes = {view["clip"].rsplit("_", 1)[0]
                for behavior in definition["behaviors"].values()
                for view in behavior["facings"].values()}
    if len(prefixes) != 1:
        raise ValueError("normalized definition has multiple prefixes")
    prefix = next(iter(prefixes))
    checked = 0
    for behavior_code, behavior in definition["behaviors"].items():
        expected_timeline = [[step["frame_index"], step["raw_time"]]
                             for step in behavior["timeline"]]
        runtime_action = manifest["act"].get(behavior["act_key"])
        if runtime_action is None or runtime_action["timeline"] != expected_timeline:
            raise ValueError(f"{behavior_code}: runtime ACT timeline differs from normalized definition")
        if bool(runtime_action["loop"]) != bool(behavior["loop"]):
            raise ValueError(f"{behavior_code}: runtime loop differs from normalized definition")
        for facing, expected in behavior["facings"].items():
            requested = f"{prefix}_{behavior_code}{facing}"
            if requested in manifest["clips"]:
                resolved = requested
                flip = False
            else:
                alias = manifest["aliases"].get(requested)
                if alias is None:
                    raise ValueError(f"{requested}: no runtime logical resolution")
                resolved = alias["clip"]
                flip = bool(alias["flip_h"])
            if resolved != expected["clip"] or flip != bool(expected["flip_h"]):
                raise ValueError(
                    f"{requested}: runtime resolves {(resolved, flip)} but normalized expects "
                    f"{(expected['clip'], bool(expected['flip_h']))}"
                )
            checked += 1
    if checked != len(definition["behaviors"]) * 8:
        raise ValueError("runtime semantic validation did not cover all logical facings")


def build_entity(definition: dict, source_root: Path, output_root: Path, side: int,
                 tier: dict, policy: str, measurements: list[dict]) -> dict:
    semantic = semantic_view(definition)
    frames, ranges, clip_indices, sources = load_physical_clips(definition, source_root, semantic)
    pages, records = pack_source_blocks(frames, ranges, side)

    manifest_frames = []
    for record in records:
        crop = record["crop"]
        manifest_frames.append({
            "page": int(record["page"]),
            "rect": list(record["rect"]),
            "size": list(record["size"]),
            "origin": list(record["raw_origin"]),
            "trim": [crop[0], crop[1]],
        })

    entity = definition["entity"]
    target = output_root / entity
    target.parent.mkdir(parents=True, exist_ok=True)
    page_names = []
    for index, page in enumerate(pages):
        name = f"{target.name}_{index}.png"
        temp = target.parent / (name + ".tmp")
        page.save(temp, format="PNG", compress_level=3)
        temp.replace(target.parent / name)
        page_names.append(name)

    document = {
        "version": 9,
        "entity": entity,
        "layout": "normalized_clip_atomic",
        "shape": "atomic_maxrects",
        "arrangement": "normalized-physical-clip-blocks",
        "pages": page_names,
        "page_sizes": [list(page.size) for page in pages],
        "atlas_side": side,
        "frames": manifest_frames,
        "clips": clip_indices,
        "aliases": semantic["aliases"],
        "act": semantic["act"],
        "sources": sources,
        "normalized_schema_version": int(definition["version"]),
        "normalized_definition_sha256": definition["content_sha256"],
        "semantic_source": "sgtest.animation/v2",
        "map_tier": tier["tier"],
        "map_spawn_count": tier["spawn_count"],
        "candidate_policy": policy,
        "candidate_measurements": measurements,
    }
    fingerprint_value = {key: value for key, value in document.items() if key not in ("pages",)}
    document["sha256"] = hashlib.sha256(
        json.dumps(fingerprint_value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    validate_runtime_semantics(definition, document)
    write_json(target.with_suffix(".json"), document)
    return {
        "entity": entity,
        "side": side,
        "pages": len(pages),
        "rgba_bytes": sum(page.width * page.height * 4 for page in pages),
        "png_bytes": sum((target.parent / name).stat().st_size for name in page_names),
        "tier": tier["tier"],
        "spawn_count": tier["spawn_count"],
        "definition_sha256": definition["content_sha256"],
    }


def build_candidate(label: str, policy: str, definitions: dict[str, dict],
                    measurements: dict[str, list[dict]], tiers: dict[str, dict],
                    source_root: Path, output_root: Path, preferred_side: int | None = None) -> dict:
    root = output_root / label
    rows = []
    for entity in sorted(definitions):
        choice = choose_candidate(measurements[entity], policy, tiers[entity]["tier"], preferred_side)
        rows.append(build_entity(definitions[entity], source_root, root, int(choice["side"]),
                                 tiers[entity], label, measurements[entity]))
    report = {
        "version": 1,
        "candidate": label,
        "semantic_source": "sgtest.animation/v2",
        "policy": policy,
        "preferred_side": preferred_side,
        "groups": rows,
        "pages": sum(row["pages"] for row in rows),
        "rgba_bytes": sum(row["rgba_bytes"] for row in rows),
        "png_bytes": sum(row["png_bytes"] for row in rows),
        "tier_counts": dict(Counter(row["tier"] for row in rows)),
    }
    write_json(root / "candidate.json", report)
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=Path(".cache/client"))
    parser.add_argument("--definitions", type=Path, default=Path("reports/normalized"))
    parser.add_argument("--population", type=Path,
                        default=Path("client/assets/generated/maps/13/population.json"))
    parser.add_argument("--output", type=Path, default=Path("reports/normalized_atlas_candidates"))
    args = parser.parse_args()
    source_root = args.source.resolve()
    output_root = args.output.resolve()
    definitions_root = args.definitions.resolve()
    if not source_root.is_dir() or not definitions_root.is_dir():
        parser.error("private source and normalized definition roots must exist")

    population = json.loads(args.population.read_text(encoding="utf-8"))
    tiers = classify_tiers(population)
    definitions = {}
    measurements = {}
    for entity in sorted(tiers):
        path = definitions_root / (entity + ".json")
        definition = json.loads(path.read_text(encoding="utf-8"))
        validate_definition(definition)
        if definition["entity"] != entity or int(definition["version"]) != 2:
            raise ValueError(f"normalized definition mismatch: {entity}")
        definitions[entity] = definition
        _, measured = measure_entity(definition, source_root)
        measurements[entity] = measured["candidates"]

    labels = [
        ("normalized_512", "preferred", 512),
        ("normalized_1024", "preferred", 1024),
        ("normalized_2048", "preferred", 2048),
        ("normalized_4096", "preferred", 4096),
        ("normalized_memory", "memory", None),
        ("normalized_balanced", "balanced", None),
        ("normalized_pages", "pages", None),
    ]
    reports = []
    for label, policy, preferred in labels:
        reports.append(build_candidate(label, policy, definitions, measurements, tiers,
                                       source_root, output_root, preferred))
        print(json.dumps({
            "candidate": label,
            "pages": reports[-1]["pages"],
            "rgba_bytes": reports[-1]["rgba_bytes"],
            "tier_counts": reports[-1]["tier_counts"],
        }, ensure_ascii=False), flush=True)

    index = {
        "version": 1,
        "population": args.population.as_posix(),
        "semantic_source": "sgtest.animation/v2",
        "tier_policy": "cumulative spawn coverage: hot<75%, warm<95%, cold remainder",
        "balanced_memory_overhead": {"hot": 0.08, "warm": 0.03, "cold": 0.0},
        "entities": {entity: {"tier": tiers[entity], "candidates": measurements[entity]}
                     for entity in sorted(tiers)},
        "candidates": reports,
    }
    write_json(output_root / "candidate_index.json", index)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
