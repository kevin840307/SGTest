#!/usr/bin/env python3
"""Fail-closed structural audit of clean HK2 Item/item.itd records.

Record lengths come from CSGOCore::InitItemLib in the reference disassembly.
Only structure is exported until each field offset is independently verified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
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
# HK2 Client.bin 0x004F4ECC..0x004F4F54 divides the zero-based
# picture index by 500. Page 0 is ground.spr; page N>0 is groundN.spr.
GROUND_PAGE_SIZE = 500


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


def resolve_ground_picture(picture_number_one_based: int,
                           ground_frame_counts: tuple[int, ...]) -> tuple[str | None, int | None]:
    """Apply the HK2 Client.bin ground%d.spr page formula and validate bounds."""
    if picture_number_one_based <= 0:
        return None, None
    index = picture_number_one_based - 1
    page = index // GROUND_PAGE_SIZE
    frame = index % GROUND_PAGE_SIZE
    if page >= len(ground_frame_counts) or frame >= ground_frame_counts[page]:
        return None, None
    source = "Interface/ground.spr" if page == 0 else f"Interface/ground{page}.spr"
    return source, frame


def classify_duplicate_ground_visuals(rows: list[dict]) -> tuple[list[dict], list[dict]]:
    """Return duplicate IDs with conflicting versus equivalent ground visuals."""
    definitions_by_id = {}
    for row in rows:
        definitions_by_id.setdefault(row["legacy_definition_id"], []).append(row)
    conflicts = []
    equivalent = []
    for definition_id, definitions in definitions_by_id.items():
        if len(definitions) <= 1:
            continue
        visuals = {(row.get("source"), row.get("source_frame")) for row in definitions}
        entry = {
            "legacy_definition_id": definition_id,
            "visuals": sorted(
                [{"source": source_name, "source_frame": frame}
                 for source_name, frame in visuals],
                key=lambda row: (row["source"] or "",
                                 -1 if row["source_frame"] is None else row["source_frame"]),
            ),
            "record_offsets": [row["offset"] for row in definitions],
        }
        (conflicts if len(visuals) > 1 else equivalent).append(entry)
    return conflicts, equivalent


def audit(source: Path, ground_frame_counts: tuple[int, ...]) -> dict:
    raw = source.read_bytes()
    plain = unwrap_cde(raw)
    rows = extract_definitions(plain)
    ids = Counter(row["legacy_definition_id"] for row in rows)
    by_source = Counter()
    outside = 0
    for row in rows:
        source_name, frame = resolve_ground_picture(
            row["picture_number_one_based"], ground_frame_counts)
        row["source"] = source_name
        row["source_frame"] = frame
        if source_name is None:
            outside += 1
        else:
            by_source[source_name] += 1
    duplicate_visual_conflicts, duplicate_visual_equivalent = classify_duplicate_ground_visuals(rows)
    return {"source": "Item/item.itd", "source_sha256": hashlib.sha256(raw).hexdigest(),
            "plain_bytes": len(plain), "records": len(rows),
            "types": dict(sorted(Counter(row["type_tag"] for row in rows).items())),
            "unique_definition_ids": len(ids),
            "duplicate_definition_ids": sorted(key for key, count in ids.items() if count > 1),
            "duplicate_ground_visual_conflicts": duplicate_visual_conflicts,
            "duplicate_ground_visual_equivalent": duplicate_visual_equivalent,
            "duplicate_lookup_precedence_verified": False,
            "ground_page_size": GROUND_PAGE_SIZE,
            "ground_frame_counts": list(ground_frame_counts),
            "ground_references_by_source": dict(sorted(by_source.items())),
            "outside_ground_references": outside,
            "max_picture_number": max(row["picture_number_one_based"] for row in rows),
            "ground_runtime_selection_verified": True,
            "hk2_client_rule_va": "0x004F4ECC-0x004F4F54",
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
    interface = root / "Interface"
    ground_paths = {}
    base_ground = interface / "ground.spr"
    if not base_ground.is_file():
        parser.error("Interface/ground.spr must exist for this audit")
    ground_paths[0] = base_ground
    for path in interface.glob("ground*.spr"):
        match = re.fullmatch(r"ground([1-9]\d*)\.spr", path.name, re.IGNORECASE)
        if match:
            ground_paths[int(match.group(1))] = path
    max_page = max(ground_paths)
    if set(ground_paths) != set(range(max_page + 1)):
        parser.error("ground sprite pages must be contiguous from ground.spr")
    if __package__:
        from .convert_assets import decode_spr
    else:
        from convert_assets import decode_spr
    ground_frame_counts = tuple(
        len(decode_spr(ground_paths[index].read_bytes())) for index in range(max_page + 1))
    report = audit(source, ground_frame_counts)
    report["ground_source_sha256"] = {
        ("Interface/ground.spr" if index == 0 else f"Interface/ground{index}.spr"):
            hashlib.sha256(path.read_bytes()).hexdigest()
        for index, path in sorted(ground_paths.items())
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "definitions"},
                     ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
