#!/usr/bin/env python3
"""Join original Enemy ACT keys, SPR suffixes and action-specific facing maps.

This validates source availability, frame indices and verified default timing.
Pass --timing-audit from audit_action_timing_binary.py to promote default
milliseconds; dynamic SetActionSpeed remains a runtime override.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

if __package__:
    from .convert_assets import decode_spr
    from .inspect_act import parse_act
    from .legacy_direction import map_enemy_direction
else:
    from convert_assets import decode_spr
    from inspect_act import parse_act
    from legacy_direction import map_enemy_direction


ACTION_SUFFIX = {"S": "NS", "W": "NW", "D": "ND", "A": "FA", "H": "NH"}
ACTION_ID = {key: index for index, key in enumerate(ACTION_SUFFIX)}


def audit_group(enemy_dir: Path, act: Path, timing_verified: bool = False) -> dict:
    raw_act = act.read_bytes()
    actions = parse_act(raw_act)
    file_lookup = {path.name.casefold(): path for path in enemy_dir.iterdir() if path.is_file()}
    sources: dict[str, dict] = {}
    issues: list[dict] = []
    rows = []
    for action in actions:
        key = action["key"]
        if key not in ACTION_SUFFIX:
            issues.append({"action": key, "reason": "unknown_action_key"})
            continue
        directions = []
        for facing in range(1, 9):
            source_direction, flipped = map_enemy_direction(facing, ACTION_ID[key])
            name = f"{act.stem}_{ACTION_SUFFIX[key]}{source_direction}.spr"
            path = file_lookup.get(name.casefold())
            if path is None:
                issues.append({"action": key, "facing": facing, "source": name,
                               "reason": "missing_spr"})
                continue
            actual_name = path.name
            if actual_name not in sources:
                raw = path.read_bytes()
                sources[actual_name] = {"sha256": hashlib.sha256(raw).hexdigest(),
                                        "frame_count": len(decode_spr(raw))}
            count = sources[actual_name]["frame_count"]
            invalid = sorted({frame["index"] for frame in action["frames"]
                              if frame["index"] >= count})
            if invalid:
                issues.append({"action": key, "facing": facing, "source": name,
                               "reason": "frame_index_out_of_range", "invalid_indices": invalid,
                               "source_frame_count": count})
                continue
            directions.append({"facing": facing, "source": actual_name,
                               "source_direction": source_direction, "flip_x": flipped})
        rows.append({"key": key, "action_id": ACTION_ID[key], "loops": action["loops"],
                     "frames": action["frames"], "events": action["events"],
                     "directions": directions,
                     "special_init_path": key == "H"})
    return {"enemy": act.stem.upper(), "act_sha256": hashlib.sha256(raw_act).hexdigest(),
            "sources": sources, "actions": rows, "issues": issues,
            "complete_source_mapping": not issues,
            "default_timing_verified": bool(timing_verified),
            "runtime_speed_can_override": True, "visual_alignment_verified": False}


def audit_all(root: Path, timing_verified: bool = False) -> dict:
    enemy = root / "Enemy"
    if not enemy.is_dir():
        raise ValueError("clean client has no Enemy directory")
    rows = [audit_group(enemy, act, timing_verified) for act in sorted(enemy.glob("*.ACT"),
             key=lambda p: p.name.casefold())]
    return {"version": 2, "source_root": str(root), "groups": rows,
            "default_timing_verified": bool(timing_verified),
            "act_groups": len(rows),
            "source_complete_groups": sum(row["complete_source_mapping"] for row in rows),
            "issue_groups": sum(bool(row["issues"]) for row in rows),
            "issue_count": sum(len(row["issues"]) for row in rows)}


def complete_group_keys(report: dict) -> list[str]:
    """Deterministic atlas keys for groups with all audited ACT references valid."""
    return sorted((f'Enemy/{row["enemy"]}' for row in report["groups"]
                   if row["complete_source_mapping"]), key=str.casefold)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path, help="Private report path outside the clean client")
    parser.add_argument("--complete-group-list", type=Path,
                        help="Optional private UTF-8 group list for atlas conversion")
    parser.add_argument("--timing-audit", type=Path,
                        help="JSON from audit_action_timing_binary.py; required to mark default ms verified")
    args = parser.parse_args()
    root, output = args.client.resolve(), args.output.resolve()
    if not root.is_dir() or output == root or root in output.parents:
        parser.error("source must exist and output must be outside the clean client")
    group_list = args.complete_group_list.resolve() if args.complete_group_list else None
    if group_list is not None and (group_list == root or root in group_list.parents):
        parser.error("group list must be outside the clean client")
    timing_verified = False
    if args.timing_audit:
        timing = json.loads(args.timing_audit.read_text(encoding="utf-8"))
        timing_verified = bool(timing.get("all_timing_primitives_verified"))
        if not timing_verified:
            parser.error("timing audit did not verify all supplied binaries")
    report = audit_all(root, timing_verified)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if group_list is not None:
        group_list.parent.mkdir(parents=True, exist_ok=True)
        group_list.write_text("".join(key + "\n" for key in complete_group_keys(report)),
                              encoding="utf-8")
    print(json.dumps({"act_groups": report["act_groups"],
                      "source_complete_groups": report["source_complete_groups"],
                      "issue_groups": report["issue_groups"],
                      "issue_count": report["issue_count"],
                      "issue_examples": [{"enemy": row["enemy"], "issues": row["issues"][:3]}
                                         for row in report["groups"] if row["issues"]][:5]},
                     ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
