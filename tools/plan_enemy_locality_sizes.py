#!/usr/bin/env python3
"""Adjust measured Enemy atlas sizes to preserve each original SPR on one page.

The baseline size plan remains performance/memory driven. This pass only grows a
group when at least one original SPR would be split at the baseline side.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path

if __package__:
    from .build_atlas_groups import select_groups
    from .convert_assets import decode_spr, pack
else:
    from build_atlas_groups import select_groups
    from convert_assets import decode_spr, pack

SIDES = (512, 1024, 2048, 4096)


def select_locality_side(base_side: int, max_pages_by_side: dict[int, int]) -> int:
    if base_side not in SIDES:
        raise ValueError("unsupported baseline side")
    for side in SIDES:
        if side < base_side:
            continue
        if max_pages_by_side.get(side) == 1:
            return side
    raise ValueError("no supported atlas side keeps each source SPR on one page")


def max_pages_for_sources(paths: list[Path], side: int) -> int:
    maximum = 0
    for path in paths:
        frames = decode_spr(path.read_bytes())
        pages, _ = pack(frames, side)
        maximum = max(maximum, len(pages))
    return maximum


def make_plan(root: Path, baseline: dict, group_keys: set[str] | None = None) -> dict:
    if not root.is_dir():
        raise ValueError("clean client directory does not exist")
    if "groups" not in baseline or not baseline["groups"]:
        raise ValueError("baseline plan has no groups")
    selected = select_groups(root, category="Enemy", keys={k.casefold() for k in group_keys}
                             if group_keys is not None else None)
    baseline_groups = baseline["groups"]
    expected = set(group_keys or baseline_groups.keys())
    if set(key.as_posix() for key in selected) != expected:
        raise ValueError("source groups do not match requested baseline groups")

    groups = {}
    grown = []
    for key, paths in selected.items():
        name = key.as_posix()
        if name not in baseline_groups:
            raise ValueError(f"missing baseline group: {name}")
        base_side = int(baseline_groups[name]["side"])
        page_counts = {}
        for side in SIDES:
            if side < base_side:
                continue
            page_counts[side] = max_pages_for_sources(paths, side)
            if page_counts[side] == 1:
                break
        side = select_locality_side(base_side, page_counts)
        row = copy.deepcopy(baseline_groups[name])
        row["baseline_side"] = base_side
        row["side"] = side
        row["max_source_pages_by_side"] = {str(k): v for k, v in page_counts.items()}
        row["locality_reason"] = "baseline_already_source_local" if side == base_side else "grown_for_source_locality"
        groups[name] = row
        if side != base_side:
            grown.append({"group": name, "from": base_side, "to": side})

    return {
        "version": 1,
        "policy": "measured_baseline_plus_source_spr_locality_v1",
        "source_locality_required": True,
        "runtime_validated": False,
        "grown_groups": grown,
        "groups": groups,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("baseline_plan", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--group-list", type=Path)
    args = parser.parse_args()

    baseline = json.loads(args.baseline_plan.read_text(encoding="utf-8"))
    keys = None
    if args.group_list:
        keys = {line.strip() for line in args.group_list.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")}
    result = make_plan(args.client.resolve(), baseline, keys)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "groups": len(result["groups"]),
        "grown_groups": result["grown_groups"],
        "policy": result["policy"],
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
