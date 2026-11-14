#!/usr/bin/env python3
"""Audit core paper-doll character layers against ACT actions and facing mapping."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

if __package__:
    from .audit_character_layers import audit_table
    from .convert_assets import decode_spr
    from .inspect_act import parse_act
    from .legacy_direction import map_character_direction
else:
    from audit_character_layers import audit_table
    from convert_assets import decode_spr
    from inspect_act import parse_act
    from legacy_direction import map_character_direction


RACE_CONFIGS = {
    "A": {
        "act": "Alien.ACT",
        "folder": "Standard",
        "draw_order": "CharDrawOrder.dat",
        "parts": {
            "Body": "Body",
            "Head": "Head",
            "L-Arm": "L-Arm",
            "R-Arm": "R-Arm",
            "Leg": "Leg",
            "Hair": "Hair00",
        },
    },
    "B": {
        "act": "Boy.ACT",
        "folder": "Standard",
        "draw_order": "CharDrawOrder.dat",
        "parts": {
            "Body": "Body",
            "Head": "Head",
            "L-Arm": "L-Arm",
            "R-Arm": "R-Arm",
            "Leg": "Leg",
            "Hair": "Hair00",
        },
    },
    "G": {
        "act": "Girl.ACT",
        "folder": "Standard",
        "draw_order": "CharDrawOrder.dat",
        "parts": {
            "Body": "Body",
            "Head": "Head",
            "L-Arm": "L-Arm",
            "R-Arm": "R-Arm",
            "Leg": "Leg",
            "Hair": "Hair00",
        },
    },
    "R": {
        "act": "Robot.ACT",
        "folder": "Cloth",
        "draw_order": "R_CharDrawOrder.dat",
        "parts": {
            "Body": "Body00",
            "L-Arm": "L-Arm00",
            "R-Arm": "R-Arm00",
            "Leg": "Leg00",
            "Bonnet": "Bonnet00",
        },
    },
}


def read_action_names(path: Path) -> list[str]:
    names = [part.strip() for part in path.read_text(encoding="ascii").split(",")
             if part.strip()]
    if len(names) < 13:
        raise ValueError("CharActionName.dat has fewer than 13 primary actions")
    return names


def read_draw_order_keys(path: Path) -> set[str]:
    keys = set()
    for line_no, raw in enumerate(path.read_text(encoding="ascii").splitlines(), 1):
        if not raw.strip():
            continue
        if ":" not in raw:
            raise ValueError(f"invalid draw-order row {line_no}")
        key = raw.split(":", 1)[0].strip().upper()
        if key in keys:
            raise ValueError(f"duplicate draw-order key {key}")
        keys.add(key)
    return keys


def required_draw_order_keys(action_names: list[str]) -> set[str]:
    keys = set()
    for action_id, action_key in enumerate(action_names[:13]):
        # DrawOrder has physical keys 1/2/3 and, for actions whose source data
        # contains them, also 4/8. DirectionMapping is separately preserved.
        source_dirs = {map_character_direction(facing, action_id)[0]
                       for facing in range(1, 9)}
        keys.update(action_key.upper() + str(direction) for direction in source_dirs)
    return keys


def audit_race(player: Path, race: str, config: dict,
               action_names: list[str]) -> dict:
    race_dir = player / race
    source_dir = race_dir / config["folder"]
    act_path = race_dir / config["act"]
    draw_path = player / config["draw_order"]
    if not source_dir.is_dir() or not act_path.is_file() or not draw_path.is_file():
        raise ValueError(f"missing core race source: {race}")

    lookup = {p.name.casefold(): p for p in source_dir.glob("*.spr")}
    actions = {row["key"].upper(): row for row in parse_act(act_path.read_bytes())}
    draw_table = audit_table(draw_path)
    draw_rows = {row["key"].upper(): row for row in draw_table["rows"]}
    draw_keys = set(draw_rows)
    missing_draw = sorted(required_draw_order_keys(action_names) - draw_keys)
    issues = []
    sources = {}
    logical_rows = []

    for action_id, action_key in enumerate(action_names[:13]):
        action_key = action_key.upper()
        action = actions.get(action_key)
        if action is None:
            issues.append({"action_id": action_id, "action": action_key,
                           "reason": "missing_act"})
            continue

        for facing in range(1, 9):
            source_direction, flip_x = map_character_direction(facing, action_id)
            source_files = {}
            for logical_part, source_part in config["parts"].items():
                wanted = f"{race}-{source_part}_{action_key}{source_direction}.spr"
                path = lookup.get(wanted.casefold())
                if path is None:
                    issues.append({
                        "action": action_key,
                        "facing": facing,
                        "part": logical_part,
                        "source": wanted,
                        "reason": "missing_spr",
                    })
                    continue

                key = path.name
                if key not in sources:
                    raw = path.read_bytes()
                    sources[key] = {
                        "sha256": hashlib.sha256(raw).hexdigest(),
                        "frame_count": len(decode_spr(raw)),
                    }

                count = sources[key]["frame_count"]
                invalid = sorted({
                    frame["index"] for frame in action["frames"]
                    if frame["index"] >= count
                })
                if invalid:
                    issues.append({
                        "action": action_key,
                        "facing": facing,
                        "part": logical_part,
                        "source": key,
                        "reason": "frame_index_out_of_range",
                        "invalid_indices": invalid,
                        "frame_count": count,
                    })
                    continue
                source_files[logical_part] = key

            draw_key = action_key + str(source_direction)
            draw_row = draw_rows.get(draw_key)
            if draw_row is None:
                issues.append({
                    "action": action_key,
                    "facing": facing,
                    "source_direction": source_direction,
                    "reason": "missing_draw_order_row",
                    "draw_key": draw_key,
                })
                draw_slots = []
                raw_layers = []
            else:
                draw_slots = draw_row["draw_slots"]
                raw_layers = draw_row["raw_layers"]
            logical_rows.append({
                "action_id": action_id,
                "action": action_key,
                "facing": facing,
                "source_direction": source_direction,
                "flip_x": flip_x,
                "sources": source_files,
                "frames": action["frames"],
                "loops": action["loops"],
                "events": action["events"],
                "draw_key": draw_key,
                "draw_slots": draw_slots,
                "raw_layers": raw_layers,
            })

    return {
        "race": race,
        "act": config["act"],
        "source_folder": config["folder"],
        "draw_order": config["draw_order"],
        "act_sha256": hashlib.sha256(act_path.read_bytes()).hexdigest(),
        "parts": list(config["parts"]),
        "source_count": len(sources),
        "sources": sources,
        "logical_states": logical_rows,
        "missing_required_draw_order_keys": missing_draw,
        "issues": issues,
        "complete_core_mapping": not issues and not missing_draw,
        "draw_order_mapping_verified": True,
        "draw_order_slot_occupancy_verified_for_core": True,
        "visual_alignment_verified": False,
    }


def audit_player_core(root: Path) -> dict:
    player = root / "Player"
    if not player.is_dir():
        raise ValueError("source has no Player directory")

    names = read_action_names(player / "CharActionName.dat")
    races = [
        audit_race(player, race, config, names)
        for race, config in RACE_CONFIGS.items()
    ]
    return {
        "version": 2,
        "primary_action_names": names[:13],
        "char_action_name_count": len(names),
        "races": races,
        "complete": all(row["complete_core_mapping"] for row in races),
        "yellow_race_pending": True,
        "special_actions_za_zl_pending": True,
        "death_visual_pending": True,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path,
                        help="Private base+overlay working set")
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    result = audit_player_core(args.source.resolve())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )

    print(json.dumps({
        "complete": result["complete"],
        "actions": result["primary_action_names"],
        "races": [{
            "race": row["race"],
            "folder": row["source_folder"],
            "parts": row["parts"],
            "sources": row["source_count"],
            "states": len(row["logical_states"]),
            "issues": len(row["issues"]),
            "missing_draw_order": len(row["missing_required_draw_order_keys"]),
        } for row in result["races"]],
    }, ensure_ascii=False))
    return 0 if result["complete"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
