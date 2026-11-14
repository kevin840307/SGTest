#!/usr/bin/env python3
"""Fail-closed structural audit of clean HK2 Item/item.itd records.

Record lengths come from CSGOCore::InitItemLib in the reference disassembly.
Only structure is exported until each field offset is independently verified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from collections import Counter
from pathlib import Path

if __package__:
    from .convert_assets import unwrap_cde
else:
    from convert_assets import unwrap_cde


# Byte counts after each one-byte type tag, not C++ object allocation sizes.
PAYLOAD_SIZES = {0: 1093, 1: 1200, 2: 1151, 3: 1149, 4: 1151, 6: 329}
# Offsets include the one-byte type tag. Reference InitItemLib writes +0x0A
# (one-based picture number) and +0x0C (definition ID) in each derived record.
PICTURE_OFFSETS = {0: 10, 1: 13, 2: 6, 3: 6, 4: 6, 6: 6}


def scan_records(data: bytes) -> list[tuple[int, int]]:
    cursor = 0
    rows = []
    while cursor < len(data):
        tag = data[cursor]
        size = PAYLOAD_SIZES.get(tag)
        if size is None:
            raise ValueError(f"unknown item record tag {tag} at offset {cursor}")
        if cursor + 1 + size > len(data):
            raise ValueError(f"truncated item record at offset {cursor}")
        rows.append((cursor, tag))
        cursor += 1 + size
    return rows


def extract_definitions(data: bytes) -> list[dict]:
    rows = []
    for offset, tag in scan_records(data):
        picture, definition_id = struct.unpack_from("<HH", data, offset + PICTURE_OFFSETS[tag])
        rows.append({"offset": offset, "type_tag": tag, "legacy_definition_id": definition_id,
                     "picture_number_one_based": picture})
    return rows


def audit(source: Path, ground_frame_counts: tuple[int, int]) -> dict:
    raw = source.read_bytes()
    plain = unwrap_cde(raw)
    rows = extract_definitions(plain)
    first_count, second_count = ground_frame_counts
    ids = Counter(row["legacy_definition_id"] for row in rows)
    direct = 0
    second_candidate = 0
    outside = 0
    for row in rows:
        picture = row["picture_number_one_based"]
        if 1 <= picture <= first_count:
            row["source_candidate"] = "Interface/ground.spr"
            row["source_frame_candidate"] = picture - 1
            direct += 1
        elif first_count < picture <= first_count + second_count:
            row["source_candidate"] = "Interface/ground1.spr"
            row["source_frame_candidate"] = picture - first_count - 1
            second_candidate += 1
        else:
            row["source_candidate"] = None
            row["source_frame_candidate"] = None
            outside += 1
    return {"source": "Item/item.itd", "source_sha256": hashlib.sha256(raw).hexdigest(),
            "plain_bytes": len(plain), "records": len(rows),
            "types": dict(sorted(Counter(row["type_tag"] for row in rows).items())),
            "unique_definition_ids": len(ids),
            "duplicate_definition_ids": sorted(key for key, count in ids.items() if count > 1),
            "ground_frame_counts": list(ground_frame_counts),
            "first_ground_references": direct, "second_ground_candidates": second_candidate,
            "outside_ground_references": outside,
            "max_picture_number": max(row["picture_number_one_based"] for row in rows),
            "second_ground_runtime_selection_verified": False,
            "definitions": rows}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root, output = args.client.resolve(), args.output.resolve()
    source = root / "Item" / "item.itd"
    if not source.is_file() or output == root or root in output.parents:
        parser.error("Item/item.itd must exist and output must be outside the clean client")
    from_ground = root / "Interface" / "ground.spr"
    second_ground = root / "Interface" / "ground1.spr"
    if not from_ground.is_file() or not second_ground.is_file():
        parser.error("both ground.spr and ground1.spr must exist for this audit")
    if __package__:
        from .convert_assets import decode_spr
    else:
        from convert_assets import decode_spr
    ground_frame_counts = (len(decode_spr(from_ground.read_bytes())),
                           len(decode_spr(second_ground.read_bytes())))
    report = audit(source, ground_frame_counts)
    report["ground_source_sha256"] = {
        "Interface/ground.spr": hashlib.sha256(from_ground.read_bytes()).hexdigest(),
        "Interface/ground1.spr": hashlib.sha256(second_ground.read_bytes()).hexdigest(),
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "definitions"},
                     ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
