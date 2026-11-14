#!/usr/bin/env python3
"""Choose per-enemy atomic atlas size from measured in-memory packing.

Each original SPR must fit as one atomic block. Candidate pages are measured
from decoded HK2 frames without writing PNGs. The default policy first finds
minimum RGBA area, then permits a small memory overhead to reduce page count.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

if __package__:
    from .build_atlas_groups import pack_source_blocks, select_groups
    from .convert_assets import decode_spr
else:
    from build_atlas_groups import pack_source_blocks, select_groups
    from convert_assets import decode_spr

SIDES = (512, 1024, 2048, 4096)


def choose_candidate(candidates: list[dict], memory_overhead: float = 0.05) -> dict:
    if memory_overhead < 0:
        raise ValueError("memory overhead must be nonnegative")
    valid = [row for row in candidates if row.get("valid")]
    if not valid:
        raise ValueError("no atomic candidate fits")
    minimum = min(valid, key=lambda row: (row["rgba_area_bytes"], row["pages"], row["side"]))
    limit = minimum["rgba_area_bytes"] * (1.0 + memory_overhead)
    within = [row for row in valid if row["rgba_area_bytes"] <= limit]
    return min(within, key=lambda row: (row["pages"], row["rgba_area_bytes"], row["side"]))


def measure_group(paths: list[Path]) -> list[dict]:
    frames = []
    source_ranges = []
    for path in paths:
        decoded = decode_spr(path.read_bytes())
        source_ranges.append((len(frames), len(decoded)))
        frames.extend(decoded)
    rows = []
    for side in SIDES:
        try:
            pages, _records = pack_source_blocks(frames, source_ranges, side)
        except ValueError as exc:
            rows.append({"side": side, "valid": False, "reason": str(exc)})
            continue
        area = sum(page.width * page.height for page in pages)
        rows.append({
            "side": side,
            "valid": True,
            "pages": len(pages),
            "rgba_area_bytes": area * 4,
            "page_pixels": area,
        })
    return rows


def make_plan(root: Path, keys: set[str], memory_overhead: float) -> dict:
    groups = select_groups(root, category="Enemy", keys={key.casefold() for key in keys})
    if {key.as_posix() for key in groups} != keys:
        raise ValueError("source groups do not match group list")
    result = {}
    counts = {}
    total_rgba = 0
    total_pages = 0
    for key, paths in groups.items():
        candidates = measure_group(paths)
        chosen = choose_candidate(candidates, memory_overhead)
        name = key.as_posix()
        result[name] = {
            "side": int(chosen["side"]),
            "pages": int(chosen["pages"]),
            "rgba_area_bytes": int(chosen["rgba_area_bytes"]),
            "reason": "atomic_measured_balanced",
            "candidates": candidates,
        }
        counts[str(chosen["side"])] = counts.get(str(chosen["side"]), 0) + 1
        total_rgba += int(chosen["rgba_area_bytes"])
        total_pages += int(chosen["pages"])
    return {
        "version": 1,
        "policy": "atomic_measured_rgba_then_pages_v1",
        "memory_overhead_limit": memory_overhead,
        "source_spr_atomic": True,
        "runtime_validated": False,
        "side_counts": counts,
        "planned_pages": total_pages,
        "planned_rgba_area_bytes": total_rgba,
        "groups": result,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("group_list", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--memory-overhead", type=float, default=0.05)
    args = parser.parse_args()
    root = args.client.resolve()
    if not root.is_dir():
        parser.error("clean client directory does not exist")
    keys = {line.strip() for line in args.group_list.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")}
    result = make_plan(root, keys, args.memory_overhead)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "groups": len(result["groups"]),
        "side_counts": result["side_counts"],
        "planned_pages": result["planned_pages"],
        "planned_rgba_area_bytes": result["planned_rgba_area_bytes"],
        "policy": result["policy"],
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
