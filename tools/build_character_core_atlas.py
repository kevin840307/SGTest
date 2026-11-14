#!/usr/bin/env python3
"""Build character-core atlas candidates with paper-doll state locality.

A state bundle is one physical action + source direction and includes all core
paper-doll layer SPRs needed by that state. State-local packing guarantees the
bundle stays on one final atlas page, while dense packing is retained for A/B.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image

if __package__:
    from .build_atlas_groups import pack_atomic_rectangles
    from .convert_assets import decode_spr, pack
else:
    from build_atlas_groups import pack_atomic_rectangles
    from convert_assets import decode_spr, pack


CORE_SLOT_PARTS = {
    "A": {0: "Head", 1: "Hair", 6: "L-Arm", 8: "R-Arm", 10: "Body", 13: "Leg"},
    "B": {0: "Head", 1: "Hair", 6: "L-Arm", 8: "R-Arm", 10: "Body", 13: "Leg"},
    "G": {0: "Head", 1: "Hair", 6: "L-Arm", 8: "R-Arm", 10: "Body", 13: "Leg"},
    "R": {2: "Bonnet", 6: "L-Arm", 8: "R-Arm", 10: "Body", 13: "Leg"},
}


def apply_core_draw_order(race: str, states: list[dict]) -> None:
    slot_parts = CORE_SLOT_PARTS[race]
    for state in states:
        parts = [slot_parts[slot] for slot in state["draw_slots"]
                 if slot in slot_parts and slot_parts[slot] in state["sources"]]
        if len(parts) != len(set(parts)):
            raise ValueError(f"duplicate occupied core draw part: {race} {state['draw_key']}")
        if set(parts) != set(state["sources"]):
            raise ValueError(
                f"draw order does not cover core sources: {race} {state['draw_key']} "
                f"{sorted(state['sources'])} vs {parts}"
            )
        state["draw_parts"] = parts


def collect_physical_states(race_audit: dict) -> list[dict]:
    states = {}
    for logical in race_audit["logical_states"]:
        key = (int(logical["action_id"]), int(logical["source_direction"]))
        current = states.get(key)
        row = {
            "action_id": int(logical["action_id"]),
            "action": str(logical["action"]),
            "source_direction": int(logical["source_direction"]),
            "sources": dict(logical["sources"]),
            "frames": logical["frames"],
            "loops": logical["loops"],
            "events": logical["events"],
            "draw_key": logical["draw_key"],
            "draw_slots": logical["draw_slots"],
            "raw_layers": logical["raw_layers"],
        }
        if current is None:
            states[key] = row
        elif (current["sources"] != row["sources"]
              or current["action"] != row["action"]
              or current["frames"] != row["frames"]
              or current["loops"] != row["loops"]
              or current["events"] != row["events"]
              or current["draw_key"] != row["draw_key"]
              or current["draw_slots"] != row["draw_slots"]
              or current["raw_layers"] != row["raw_layers"]):
            raise ValueError(f"inconsistent physical state mapping: {key}")
    return [states[key] for key in sorted(states)]


def _source_lookup(folder: Path) -> dict[str, Path]:
    return {path.name.casefold(): path for path in folder.glob("*.spr")}


def _load_source_frames(folder: Path, state_rows: list[dict]) -> tuple[dict[str, list], dict[str, str]]:
    lookup = _source_lookup(folder)
    decoded = {}
    hashes = {}
    for state in state_rows:
        for source in state["sources"].values():
            key = source.casefold()
            if key in decoded:
                continue
            path = lookup.get(key)
            if path is None:
                raise ValueError(f"missing source SPR: {source}")
            raw = path.read_bytes()
            decoded[key] = decode_spr(raw)
            hashes[key] = hashlib.sha256(raw).hexdigest()
    return decoded, hashes


def _dense_pack(decoded: dict[str, list], side: int):
    frames = []
    identities = []
    for source_key in sorted(decoded):
        for frame_index, frame in enumerate(decoded[source_key]):
            frames.append(frame)
            identities.append((source_key, frame_index))
    pages, records = pack(frames, side)
    return pages, records, identities


def _state_local_pack(decoded: dict[str, list], states: list[dict], side: int):
    blocks = []
    state_local_records = {}
    for state_index, state in enumerate(states):
        frames = []
        identities = []
        for part, source in sorted(state["sources"].items()):
            source_key = source.casefold()
            for frame_index, frame in enumerate(decoded[source_key]):
                frames.append(frame)
                identities.append((source_key, frame_index))
        local_pages, local_records = pack(frames, side)
        if len(local_pages) != 1:
            raise ValueError(
                f"state bundle {state['action']}{state['source_direction']} "
                f"needs {len(local_pages)} pages at side {side}"
            )
        blocks.append((state_index, local_pages[0]))
        state_local_records[state_index] = list(zip(identities, local_records))

    placements, bounds = pack_atomic_rectangles(
        [(state_index, image.width, image.height) for state_index, image in blocks], side
    )
    pages = [Image.new("RGBA", (width, height)) for width, height in bounds]
    final_records = {}
    state_pages = {}
    for state_index, image in blocks:
        page_index, x, y = placements[state_index]
        pages[page_index].paste(image, (x, y))
        state_pages[state_index] = page_index
        for identity, local in state_local_records[state_index]:
            record = dict(local)
            if record["page"] >= 0:
                rx, ry, rw, rh = record["rect"]
                record["page"] = page_index
                record["rect"] = [rx + x, ry + y, rw, rh]
            if identity in final_records:
                raise ValueError(f"source frame appears in multiple states: {identity}")
            final_records[identity] = record
    identities = sorted(final_records)
    records = [final_records[key] for key in identities]
    return pages, records, identities, state_pages


def build_race(root: Path, race_audit: dict, output: Path, side: int,
               policy: str) -> dict:
    race = race_audit["race"]
    folder = root / "Player" / race / race_audit["source_folder"]
    states = collect_physical_states(race_audit)
    apply_core_draw_order(race, states)
    decoded, hashes = _load_source_frames(folder, states)

    if policy == "dense":
        pages, records, identities = _dense_pack(decoded, side)
        state_pages = {}
    elif policy == "state_local":
        pages, records, identities, state_pages = _state_local_pack(
            decoded, states, side
        )
    else:
        raise ValueError(f"unknown policy: {policy}")

    region_by_identity = {}
    for identity, record in zip(identities, records):
        region_by_identity[identity] = record

    target = output / race
    target.mkdir(parents=True, exist_ok=True)
    page_names = []
    for index, page in enumerate(pages):
        name = f"page_{index}.png"
        temporary = target / (name + ".tmp")
        page.save(temporary, format="PNG", optimize=True)
        temporary.replace(target / name)
        page_names.append(name)

    frame_rows = []
    for (source_key, frame_index), record in region_by_identity.items():
        row = dict(record)
        row["source"] = source_key
        row["source_frame"] = frame_index
        row["source_sha256"] = hashes[source_key]
        frame_rows.append(row)

    physical_rows = []
    for state_index, state in enumerate(states):
        page_set = set()
        for source in state["sources"].values():
            source_key = source.casefold()
            for frame_index in range(len(decoded[source_key])):
                record = region_by_identity[(source_key, frame_index)]
                if record["page"] >= 0:
                    page_set.add(record["page"])
        physical_rows.append({
            **state,
            "atlas_pages": sorted(page_set),
            "single_page": len(page_set) <= 1,
            "state_page": state_pages.get(state_index),
        })

    physical_index = {
        (row["action_id"], row["source_direction"]): index
        for index, row in enumerate(physical_rows)
    }
    logical_rows = []
    for logical in race_audit["logical_states"]:
        key = (int(logical["action_id"]), int(logical["source_direction"]))
        logical_rows.append({
            "action_id": int(logical["action_id"]),
            "action": logical["action"],
            "facing": int(logical["facing"]),
            "source_direction": int(logical["source_direction"]),
            "flip_x": bool(logical["flip_x"]),
            "physical_state": physical_index[key],
        })

    document = {
        "version": 1,
        "race": race,
        "policy": policy,
        "atlas_side": side,
        "pages": page_names,
        "parts": race_audit["parts"],
        "core_slot_parts": {str(slot): part for slot, part in CORE_SLOT_PARTS[race].items()},
        "draw_order_mapping_verified": True,
        "core_slot_occupancy_verified": True,
        "frames": frame_rows,
        "physical_states": physical_rows,
        "logical_states": logical_rows,
        "visual_alignment_verified": False,
        "draw_order_runtime_verified": False,
        "animation_timing_verified": False,
    }
    (target / "character.atlas.json").write_text(
        json.dumps(document, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )

    page_area = sum(page.width * page.height for page in pages)
    return {
        "race": race,
        "policy": policy,
        "sources": len(decoded),
        "frames": len(records),
        "physical_states": len(physical_rows),
        "logical_states": len(logical_rows),
        "pages": len(page_names),
        "single_page_states": sum(row["single_page"] for row in physical_rows),
        "rgba_area_bytes": page_area * 4,
        "png_bytes": sum((target / name).stat().st_size for name in page_names),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("audit", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--atlas-side", type=int, default=1024,
                        choices=(512, 1024, 2048, 4096))
    parser.add_argument("--policy", choices=("dense", "state_local"),
                        default="state_local")
    args = parser.parse_args()

    audit = json.loads(args.audit.read_text(encoding="utf-8"))
    if not audit.get("complete"):
        parser.error("character core audit is not complete")
    rows = []
    for race in audit["races"]:
        rows.append(build_race(
            args.source.resolve(), race, args.output.resolve(),
            args.atlas_side, args.policy
        ))
    report = {
        "version": 1,
        "policy": args.policy,
        "atlas_side": args.atlas_side,
        "races": rows,
        "failed_locality_states": sum(
            row["physical_states"] - row["single_page_states"] for row in rows
        ),
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "character_report.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report["failed_locality_states"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
