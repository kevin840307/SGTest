"""Strict, presentation-only HK skill.dat name/description evidence.

This module belongs to the existing asset_converter source pipeline. Its output
is a private local client manifest, NEVER authoritative combat rules, levels,
skill effects, icon frame IDs, cooldowns or proficiency.
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


def parse_skill_labels_plain(plain: bytes) -> dict[int, tuple[str, str]]:
    if not 1 <= len(plain) <= MAX_BYTES:
        raise ValueError("HK skill.dat length outside bounded source range")
    try:
        text = plain.decode("big5hkscs", errors="strict")
        rows = csv.reader(io.StringIO(text, newline=""), strict=True)
        labels: dict[int, tuple[str, str]] = {}
        count = 0
        for count, row in enumerate(rows, 1):
            if count > MAX_ROWS or len(row) != 20:
                raise ValueError(f"HK skill.dat invalid field count at row {count}")
            raw_id, name, description = row[:3]
            if not raw_id.isascii() or not raw_id.isdecimal() or raw_id.startswith("0"):
                raise ValueError(f"HK skill.dat invalid SkillID at row {count}")
            skill_id = int(raw_id)
            if not 1 <= skill_id <= 65535:
                raise ValueError(f"HK skill.dat SkillID out of range at row {count}")
            name, description = name.strip(), description.strip()
            if not 1 <= len(name) <= 32 or len(description) > 512:
                raise ValueError(f"HK skill.dat invalid display text at row {count}")
            pair = (name, description)
            prior = labels.setdefault(skill_id, pair)
            if prior != pair:
                raise ValueError(f"HK skill.dat conflicting name/description for {skill_id}")
        if count == 0 or len(labels) > MAX_SKILLS:
            raise ValueError("HK skill.dat empty or too many skills")
        return labels
    except (UnicodeDecodeError, csv.Error) as exc:
        raise ValueError("HK skill.dat invalid Big5-HKSCS CSV") from exc


def export_private_skill_labels(source: Path, destination: Path) -> tuple[int, int]:
    blob = source.read_bytes()
    if not 1 <= len(blob) <= MAX_BYTES:
        raise ValueError("HK skill.dat source size invalid")
    if not blob.startswith(b"C.D.E."):
        raise ValueError("HK skill.dat must be original C.D.E. source")
    labels = parse_skill_labels_plain(unwrap_cde(blob))
    manifest = {
        "kind": "hk_skill_labels",
        "version": 1,
        "source_sha256": hashlib.sha256(blob).hexdigest(),
        "entries": {
            str(skill_id): {"name": name, "description": description}
            for skill_id, (name, description) in sorted(labels.items())
        },
    }
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(
        json.dumps(manifest, ensure_ascii=False, separators=(",", ":"), sort_keys=True),
        encoding="utf-8",
    )
    return len(labels), len(blob)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    count, size = export_private_skill_labels(args.source, args.output)
    print(f"HK_SKILL_LABELS_PRIVATE_EXPORT_PASS skills={count} original_bytes={size}")


if __name__ == "__main__":
    main()
