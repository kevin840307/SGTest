"""Strict, presentation-only HK skill.dat icon-frame evidence.

Old Paradise.exe.lst proves CHotKeyItf::OnLoadHotKey reads SKILLDATA+7 and
renders that frame from Interface\\SKILL_ICON.spr. InitSkillLib maps
SKILLDATA+7 to CSV field 14 (zero-based index 13). This module exports only
that presentation mapping; it never grants skills or defines gameplay rules.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import json
from pathlib import Path

from .runtime_spr import unwrap_cde

MAX_BYTES = 4 * 1024 * 1024
MAX_ROWS = 10000
MAX_SKILLS = 4096
SKILL_ICON_FRAMES = 114


def parse_skill_icon_frames_plain(plain: bytes) -> dict[int, int]:
    if not 1 <= len(plain) <= MAX_BYTES:
        raise ValueError("HK skill.dat length outside bounded source range")
    try:
        text = plain.decode("big5hkscs", errors="strict")
        rows = csv.reader(io.StringIO(text, newline=""), strict=True)
        icons: dict[int, int] = {}
        count = 0
        for count, row in enumerate(rows, 1):
            if count > MAX_ROWS or len(row) != 20:
                raise ValueError(f"HK skill.dat invalid field count at row {count}")
            raw_id = row[0]
            raw_frame = row[13]
            if not raw_id.isascii() or not raw_id.isdecimal() or raw_id.startswith("0"):
                raise ValueError(f"HK skill.dat invalid SkillID at row {count}")
            if not raw_frame.isascii() or not raw_frame.isdecimal():
                raise ValueError(f"HK skill.dat invalid icon frame at row {count}")
            skill_id = int(raw_id)
            frame = int(raw_frame)
            if not 1 <= skill_id <= 65535:
                raise ValueError(f"HK skill.dat SkillID out of range at row {count}")
            if not 0 <= frame < SKILL_ICON_FRAMES:
                raise ValueError(f"HK skill.dat icon frame out of range at row {count}")
            prior = icons.setdefault(skill_id, frame)
            if prior != frame:
                raise ValueError(f"HK skill.dat conflicting icon frame for {skill_id}")
        if count == 0 or len(icons) > MAX_SKILLS:
            raise ValueError("HK skill.dat empty or too many skills")
        return icons
    except (UnicodeDecodeError, csv.Error) as exc:
        raise ValueError("HK skill.dat invalid Big5-HKSCS CSV") from exc


def export_private_skill_icons(source: Path, destination: Path) -> tuple[int, int]:
    blob = source.read_bytes()
    if not 1 <= len(blob) <= MAX_BYTES:
        raise ValueError("HK skill.dat source size invalid")
    if not blob.startswith(b"C.D.E."):
        raise ValueError("HK skill.dat must be original C.D.E. source")
    icons = parse_skill_icon_frames_plain(unwrap_cde(blob))
    manifest = {
        "kind": "hk_skill_icons",
        "version": 1,
        "source_sha256": hashlib.sha256(blob).hexdigest(),
        "spr_frame_count": SKILL_ICON_FRAMES,
        "entries": {str(skill_id): frame for skill_id, frame in sorted(icons.items())},
    }
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(
        json.dumps(manifest, ensure_ascii=False, separators=(",", ":"), sort_keys=True),
        encoding="utf-8",
    )
    return len(icons), len(blob)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    count, size = export_private_skill_icons(args.source, args.output)
    print(f"HK_SKILL_ICONS_PRIVATE_EXPORT_PASS skills={count} original_bytes={size}")


if __name__ == "__main__":
    main()
