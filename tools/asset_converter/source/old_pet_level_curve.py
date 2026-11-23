"""Audit Old Server Player_Up_Lv.csv into exact Pet total-EXP thresholds."""

from __future__ import annotations

import hashlib
from pathlib import Path

MAX_LEVEL = 100
MAX_ULONG = 0xFFFFFFFF
MAX_SOURCE_BYTES = 1_048_576


def parse_old_pet_level_curve(text: str) -> list[int]:
    raw: list[int] = []
    saw_end = False
    for line_number, source_line in enumerate(text.splitlines(), 1):
        line = source_line.strip()
        if not line:
            continue
        if line.casefold() == "[end]":
            saw_end = True
            break
        if not line.isascii() or not line.isdecimal():
            raise ValueError(f"invalid Player_Up_Lv.csv row {line_number}")
        value = int(line, 10)
        if value <= 0 or value > MAX_ULONG:
            raise ValueError(f"unsafe Player_Up_Lv.csv row {line_number}")
        raw.append(value)

    if not saw_end:
        raise ValueError("Player_Up_Lv.csv missing [end] terminator")
    if len(raw) < MAX_LEVEL:
        raise ValueError("Player_Up_Lv.csv has fewer than 100 numeric rows")

    # Old CSGOPetSys::ReadLevelUpTable consumes only the first 100 rows and,
    # from row index 1 onward, adds the prior cumulative dword. This is the
    # total-EXP threshold used by CSGOPet::AddExp before it increments level.
    cumulative: list[int] = []
    total = 0
    for index, value in enumerate(raw[:MAX_LEVEL], 1):
        total += value
        if total > MAX_ULONG:
            raise ValueError(
                f"Player_Up_Lv.csv cumulative threshold overflows ulong at level {index}")
        cumulative.append(total)

    if any(right <= left for left, right in zip(cumulative, cumulative[1:])):
        raise ValueError("Player_Up_Lv.csv cumulative thresholds are not increasing")
    return cumulative


def read_old_pet_level_curve(path: Path) -> tuple[list[int], dict]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError("Player_Up_Lv.csv source is missing or symbolic")
    size = source.stat().st_size
    if size < 2 or size > MAX_SOURCE_BYTES:
        raise ValueError("Player_Up_Lv.csv source size is invalid")
    payload = source.read_bytes()
    try:
        text = payload.decode("ascii")
    except UnicodeDecodeError as exc:
        raise ValueError("Player_Up_Lv.csv must be ASCII") from exc
    thresholds = parse_old_pet_level_curve(text)
    return thresholds, {
        "sha256": hashlib.sha256(payload).hexdigest(),
        "bytes": len(payload),
        "thresholds": len(thresholds),
    }
