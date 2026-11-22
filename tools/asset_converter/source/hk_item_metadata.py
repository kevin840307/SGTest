"""Parse HK SGO item_extra.csv and grading.csv presentation evidence.

The original client loads both files beside Item/item.itd. This module extracts
only evidence that is safe for presentation. Gameplay flags from item_extra are
validated and retained in the parser result, but are not promoted to SGTest
runtime authority.
"""
from __future__ import annotations

import csv
from dataclasses import dataclass
import hashlib
import io
from pathlib import Path

from asset_converter.source.runtime_spr import unwrap_cde

MAX_FILE = 4 * 1024 * 1024
MAX_ROWS = 20000
ITEM_KINDS = frozenset(("EQUIP", "ATTACK", "BOOSTER", "RECOVERY"))
GRADES = frozenset(("S", "A", "B", "C", "D"))


@dataclass(frozen=True)
class HKItemExtra:
    hk_item_id: int
    name: str
    kind: str
    flags: tuple[bool, bool, bool, bool]
    grade: str


@dataclass(frozen=True)
class HKGrade:
    grade: str
    raw_rgba: tuple[int, int, int, int]
    effective_rgba: tuple[int, int, int, int]


def _plain(blob: bytes, label: str) -> bytes:
    if not 1 <= len(blob) <= MAX_FILE:
        raise ValueError(f"HK {label} source size is invalid")
    plain = unwrap_cde(blob) if blob.startswith(b"C.D.E.") else blob
    if not 1 <= len(plain) <= MAX_FILE:
        raise ValueError(f"HK {label} plain payload size is invalid")
    return plain


def _cp950_rows(plain: bytes, label: str) -> list[list[str]]:
    try:
        text = plain.decode("cp950")
    except UnicodeDecodeError as exc:
        raise ValueError(f"HK {label} is not valid CP950") from exc
    try:
        rows = list(csv.reader(io.StringIO(text, newline="")))
    except csv.Error as exc:
        raise ValueError(f"HK {label} CSV is malformed") from exc
    if not rows or len(rows) > MAX_ROWS or any(not row for row in rows):
        raise ValueError(f"HK {label} row count is invalid")
    return rows


def parse_hk_item_extra_plain(plain: bytes) -> list[HKItemExtra]:
    rows = _cp950_rows(plain, "item_extra.csv")
    result: list[HKItemExtra] = []
    seen: set[int] = set()
    for line_no, row in enumerate(rows, 1):
        if len(row) != 8:
            raise ValueError(f"HK item_extra.csv row {line_no} must have 8 fields")
        raw_id, name, kind, *tail = row
        if not raw_id.isdigit():
            raise ValueError(f"HK item_extra.csv row {line_no} has invalid item ID")
        item_id = int(raw_id)
        if not 1 <= item_id <= 65535 or item_id in seen:
            raise ValueError(f"HK item_extra.csv row {line_no} has invalid/duplicate item ID")
        seen.add(item_id)
        name = name.strip()
        kind = kind.strip().upper()
        if not name or len(name) > 128 or kind not in ITEM_KINDS:
            raise ValueError(f"HK item_extra.csv row {line_no} has invalid name/kind")
        raw_flags = tail[:4]
        grade = tail[4].strip().upper()
        if any(value not in ("0", "1") for value in raw_flags) or grade not in GRADES:
            raise ValueError(f"HK item_extra.csv row {line_no} has invalid flags/grade")
        result.append(HKItemExtra(
            item_id, name, kind, tuple(value == "1" for value in raw_flags), grade))
    return result


def parse_hk_grading_plain(plain: bytes) -> list[HKGrade]:
    rows = _cp950_rows(plain, "grading.csv")
    result: list[HKGrade] = []
    seen: set[str] = set()
    for line_no, row in enumerate(rows, 1):
        if len(row) != 5:
            raise ValueError(f"HK grading.csv row {line_no} must have 5 fields")
        grade = row[0].strip().upper()
        if grade not in GRADES or grade in seen:
            raise ValueError(f"HK grading.csv row {line_no} has invalid/duplicate grade")
        seen.add(grade)
        channels: list[int] = []
        for raw in row[1:]:
            if not raw.isdigit() or not 0 <= int(raw) <= 255:
                raise ValueError(f"HK grading.csv row {line_no} has invalid RGBA")
            channels.append(int(raw))
        raw_rgba = tuple(channels)
        # CONFIRMED in SGOClient.exe.lst sub_406FF1: every zero channel is
        # replaced with 1 before the seven-byte grade/color record is stored.
        effective_rgba = tuple(value if value != 0 else 1 for value in channels)
        result.append(HKGrade(grade, raw_rgba, effective_rgba))
    if seen != GRADES:
        raise ValueError("HK grading.csv does not define exactly S/A/B/C/D")
    return result


def _read(path: Path, label: str) -> tuple[bytes, bytes]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError(f"HK {label} source is missing or symbolic")
    blob = source.read_bytes()
    return blob, _plain(blob, label)


def read_hk_item_extra(path: Path) -> tuple[list[HKItemExtra], dict]:
    blob, plain = _read(path, "item_extra.csv")
    rows = parse_hk_item_extra_plain(plain)
    return rows, {
        "row_count": len(rows),
        "source_sha256": hashlib.sha256(blob).hexdigest(),
        "plain_sha256": hashlib.sha256(plain).hexdigest(),
        "plain_bytes": len(plain),
        "id_uniqueness": "confirmed",
        "format": "8_fields_cp950",
    }


def read_hk_grading(path: Path) -> tuple[list[HKGrade], dict]:
    blob, plain = _read(path, "grading.csv")
    rows = parse_hk_grading_plain(plain)
    return rows, {
        "row_count": len(rows),
        "source_sha256": hashlib.sha256(blob).hexdigest(),
        "plain_sha256": hashlib.sha256(plain).hexdigest(),
        "plain_bytes": len(plain),
        "grades": [row.grade for row in rows],
        "zero_channels_promoted_to_one": sum(
            raw == 0 for row in rows for raw in row.raw_rgba),
        "format": "grade_plus_rgba",
    }
