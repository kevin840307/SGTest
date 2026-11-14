#!/usr/bin/env python3
"""Audit legacy SGO client binaries for the ACT timing primitives.

This is a fail-closed byte-level verifier. It does not execute or modify the
client binary. Offsets are reported for evidence; copyrighted binary contents
are never copied into the repository.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path

ACTION_DEFAULTS = bytes.fromhex("c7431428000000c7431819000000")
ACTION_DECREMENT = bytes.fromhex("8b4e0c498bc1894e0c85c0")
ACTION_SET_SPEED = bytes.fromhex("85c974078b442404894118c20400")
DURATION_SCALE = bytes.fromhex("0faf461499f77e1889460c")
SYSTEM_FRAME_CORE = bytes.fromhex("b8e803000099f77d08")
SYSTEM_FRAME_PROLOGUE = bytes.fromhex("558bec51894dfc")
LOOP_PREFIX = bytes.fromhex("558bec83ec08894df88b45f88b4854894dfcff15")
LOOP_SUFFIX = bytes.fromhex(
    "8b55f88942548b45f88b48542b4dfc894dfc817dfce80300007609"
    "8b55f88b42508945fc8b4df88b51640355fc8b45f8895064"
    "8b4df88b55f88b41643b4250"
)


def find_all(blob: bytes, pattern: bytes) -> list[int]:
    result: list[int] = []
    start = 0
    while True:
        offset = blob.find(pattern, start)
        if offset < 0:
            return result
        result.append(offset)
        start = offset + 1


def find_loop_signatures(blob: bytes) -> list[int]:
    hits = []
    for offset in find_all(blob, LOOP_PREFIX):
        after_iat = offset + len(LOOP_PREFIX) + 4
        if blob[after_iat:after_iat + len(LOOP_SUFFIX)] == LOOP_SUFFIX:
            hits.append(offset)
    return hits


def parse_pe_sections(blob: bytes) -> list[dict]:
    if len(blob) < 0x40 or blob[:2] != b"MZ":
        raise ValueError("not a PE file")
    pe = struct.unpack_from("<I", blob, 0x3C)[0]
    if pe + 24 > len(blob) or blob[pe:pe + 4] != b"PE\0\0":
        raise ValueError("invalid PE header")
    count = struct.unpack_from("<H", blob, pe + 6)[0]
    optional_size = struct.unpack_from("<H", blob, pe + 20)[0]
    table = pe + 24 + optional_size
    sections = []
    for index in range(count):
        at = table + index * 40
        if at + 40 > len(blob):
            raise ValueError("truncated section table")
        name = blob[at:at + 8].split(b"\0", 1)[0].decode("ascii", errors="replace")
        virtual_size, rva, raw_size, raw = struct.unpack_from("<IIII", blob, at + 8)
        sections.append({
            "name": name, "virtual_size": virtual_size, "rva": rva,
            "raw_size": raw_size, "raw": raw,
        })
    return sections


def file_offset_to_rva(sections: list[dict], offset: int) -> int:
    for section in sections:
        start = section["raw"]
        end = start + section["raw_size"]
        if start <= offset < end:
            return section["rva"] + offset - start
    raise ValueError(f"file offset is outside mapped sections: {offset:#x}")


def find_rel32_calls_to(blob: bytes, sections: list[dict], target_rva: int) -> list[int]:
    calls = []
    for section in sections:
        if section["name"] != ".text":
            continue
        start = section["raw"]
        end = min(len(blob), start + section["raw_size"])
        for offset in range(start, max(start, end - 4)):
            if blob[offset] != 0xE8:
                continue
            rel = struct.unpack_from("<i", blob, offset + 1)[0]
            call_rva = section["rva"] + offset - start
            if call_rva + 5 + rel == target_rva:
                calls.append(offset)
    return calls


def has_push_50_before(blob: bytes, call_offset: int, window: int = 24) -> bool:
    before = blob[max(0, call_offset - window):call_offset]
    return b"\x6a\x32" in before or b"\x68\x32\x00\x00\x00" in before


def audit_binary(path: Path) -> dict:
    blob = path.read_bytes()
    sections = parse_pe_sections(blob)
    defaults = find_all(blob, ACTION_DEFAULTS)
    decrement = find_all(blob, ACTION_DECREMENT)
    set_speed = find_all(blob, ACTION_SET_SPEED)
    duration = find_all(blob, DURATION_SCALE)
    frame_core = find_all(blob, SYSTEM_FRAME_CORE)
    loops = find_loop_signatures(blob)

    system_start = None
    calls = []
    calls_with_50 = []
    if len(frame_core) == 1:
        core = frame_core[0]
        candidate = core - len(SYSTEM_FRAME_PROLOGUE)
        if candidate >= 0 and blob[candidate:core] == SYSTEM_FRAME_PROLOGUE:
            system_start = candidate
            target_rva = file_offset_to_rva(sections, candidate)
            calls = find_rel32_calls_to(blob, sections, target_rva)
            calls_with_50 = [offset for offset in calls if has_push_50_before(blob, offset)]

    verified = (
        len(defaults) == 1
        and len(decrement) == 1
        and len(set_speed) == 1
        and len(duration) == 2
        and len(frame_core) == 1
        and len(loops) == 1
        and system_start is not None
        and len(calls_with_50) >= 1
    )
    return {
        "file": path.name,
        "size": len(blob),
        "sha256": hashlib.sha256(blob).hexdigest(),
        "offsets": {
            "action_defaults_40_25": defaults,
            "action_update_decrement": decrement,
            "action_set_speed": set_speed,
            "duration_scale": duration,
            "system_set_frame_core": frame_core,
            "system_loop": loops,
            "system_set_frame_calls": calls,
            "system_set_frame_calls_with_push_50": calls_with_50,
        },
        "fixed_update_hz": 50 if verified else None,
        "fixed_update_ms": 20 if verified else None,
        "action_duration_numerator": 40 if verified else None,
        "default_action_speed": 25 if verified else None,
        "timing_primitives_verified": verified,
    }


def legacy_default_update_calls(raw_duration: int, speed: int = 25) -> int:
    if raw_duration < 0 or speed <= 0:
        raise ValueError("invalid duration or speed")
    return raw_duration * 40 // speed


def legacy_effective_update_ticks(raw_duration: int, speed: int = 25) -> int:
    """Actual fixed updates before a frame can advance; Update decrements first."""
    return max(1, legacy_default_update_calls(raw_duration, speed))


def legacy_reference_duration_ms(raw_duration: int, speed: int = 25,
                                 tick_ms: int = 20) -> int:
    return legacy_effective_update_ticks(raw_duration, speed) * tick_ms


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binaries", type=Path, nargs="+")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    rows = []
    for path in args.binaries:
        if not path.is_file():
            parser.error(f"missing binary: {path}")
        rows.append(audit_binary(path))
    document = {
        "version": 1,
        "binaries": rows,
        "all_timing_primitives_verified": all(row["timing_primitives_verified"] for row in rows),
        "reference_default_examples": {
            "raw_6_update_calls": legacy_default_update_calls(6),
            "raw_6_ms_at_50hz": legacy_reference_duration_ms(6),
            "raw_3_update_calls": legacy_default_update_calls(3),
            "raw_3_ms_at_50hz": legacy_reference_duration_ms(3),
            "raw_1_speed_50_counter": legacy_default_update_calls(1, 50),
            "raw_1_speed_50_effective_ticks": legacy_effective_update_ticks(1, 50),
            "raw_1_speed_50_ms": legacy_reference_duration_ms(1, 50),
        },
        "scope": (
            "Byte signatures verify timing primitives and SystemSetFrame(50). "
            "The reference Paradise.exe.lst call chain separately proves "
            "character/enemy animation Update consumes one action tick per fixed game Update."
        ),
    }
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(document, ensure_ascii=False, indent=2))
    return 0 if document["all_timing_primitives_verified"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
