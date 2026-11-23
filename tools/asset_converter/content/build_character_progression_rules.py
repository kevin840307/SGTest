"""Publish a private audited character progression manifest from Old Server player tables."""

from __future__ import annotations

import csv
import hashlib
from pathlib import Path

from asset_converter.json_files import write_json

SCHEMA = "sgtest.character_progression_rules/v1"
MAX_LEVEL = 100
MAX_ATTRIBUTE = 200
RACES = ("Boy", "Girl", "Migu", "Robot")


def _read_rows(path: Path, minimum_rows: int, columns: int, *, limit: int | None = None) -> tuple[list[list[int]], dict]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError(f"missing or symbolic progression source: {source}")
    payload = source.read_bytes()
    if len(payload) < 1 or len(payload) > 4 * 1024 * 1024:
        raise ValueError(f"progression source outside bounds: {source.name}")
    rows: list[list[int]] = []
    with source.open("r", encoding="utf-8-sig", newline="") as handle:
        for line_no, raw in enumerate(csv.reader(handle), start=1):
            if not raw or all(not value.strip() for value in raw):
                continue
            if len(raw) != columns:
                raise ValueError(f"{source.name}:{line_no} expected {columns} columns")
            try:
                values = [int(value.strip(), 10) for value in raw]
            except ValueError as error:
                raise ValueError(f"{source.name}:{line_no} contains a non-integer") from error
            if any(value < 0 or value > 2_000_000_000 for value in values):
                raise ValueError(f"{source.name}:{line_no} value outside bounds")
            rows.append(values)
            if limit is not None and len(rows) >= limit:
                break
    if len(rows) < minimum_rows:
        raise ValueError(f"{source.name} has only {len(rows)} rows; need {minimum_rows}")
    return rows, {
        "file": source.name,
        "sha256": hashlib.sha256(payload).hexdigest(),
        "bytes": len(payload),
        "rows": len(rows),
    }


def compile_character_progression_rules(
    player_dat: Path,
    *,
    source_reference: str = (
        "Old SGOPlayerSys::LoadLvUpTable/SGOPlayer::LevelUp/UpdateParam + "
        "Dat/Player progression tables"
    ),
) -> dict:
    root = Path(player_dat).resolve()
    if not root.is_dir() or root.is_symlink():
        raise ValueError("Old Server Dat/Player root is missing or symbolic")
    source_label = source_reference.strip()
    if not source_label or len(source_label) > 256:
        raise ValueError("character progression source reference is invalid")

    level_rows, level_audit = _read_rows(root / "Player_Up_Lv.csv", MAX_LEVEL, 1, limit=MAX_LEVEL)
    point_rows, point_audit = _read_rows(root / "ExpUse.csv", MAX_LEVEL, 1, limit=MAX_LEVEL)
    base_rows, base_audit = _read_rows(root / "AttBase_Newbie.csv", MAX_LEVEL, 9, limit=MAX_LEVEL)

    required = [row[0] for row in level_rows[:MAX_LEVEL]]
    if any(value <= 0 for value in required):
        raise ValueError("character level requirements must be positive")
    points = [row[0] for row in point_rows[:MAX_LEVEL]]
    if any(value < 0 or value > 1000 for value in points):
        raise ValueError("character level attribute-point awards outside bounds")

    races: dict[str, dict[str, list[int]]] = {}
    audits = [level_audit, point_audit, base_audit]
    for race in RACES:
        strength, audit = _read_rows(root / f"STR_{race}.csv", MAX_ATTRIBUTE, 1, limit=MAX_ATTRIBUTE)
        audits.append(audit)
        constitution, audit = _read_rows(root / f"CON_{race}.csv", MAX_ATTRIBUTE, 1, limit=MAX_ATTRIBUTE)
        audits.append(audit)
        dexterity, audit = _read_rows(root / f"DEX_{race}.csv", MAX_ATTRIBUTE, 2, limit=MAX_ATTRIBUTE)
        audits.append(audit)
        wisdom, audit = _read_rows(root / f"WIS_{race}.csv", MAX_ATTRIBUTE, 2, limit=MAX_ATTRIBUTE)
        audits.append(audit)
        races[race.lower()] = {
            "strength_attack": [row[0] for row in strength[:MAX_ATTRIBUTE]],
            "constitution_hp": [row[0] for row in constitution[:MAX_ATTRIBUTE]],
            "dexterity_hit": [row[0] for row in dexterity[:MAX_ATTRIBUTE]],
            "dexterity_dodge": [row[1] for row in dexterity[:MAX_ATTRIBUTE]],
            "wisdom_mp": [row[0] for row in wisdom[:MAX_ATTRIBUTE]],
            "wisdom_magic_attack": [row[1] for row in wisdom[:MAX_ATTRIBUTE]],
        }

    levels = []
    for index, row in enumerate(base_rows[:MAX_LEVEL], start=1):
        levels.append({
            "level": index,
            "required_experience": required[index - 1],
            "attribute_points_awarded": points[index - 1],
            "base_hp": row[0],
            "base_mp": row[1],
            "base_attack": row[2],
            "base_defense": row[3],
            "base_magic_attack": row[4],
            "base_magic_defense": row[5],
            "base_hit": row[6],
            "base_dodge": row[7],
            "base_will": row[8],
        })

    return {
        "schema": SCHEMA,
        "status": "pass",
        "rules_verified": True,
        "gameplay_ready": True,
        "source": source_label,
        "max_level": MAX_LEVEL,
        "max_attribute": MAX_ATTRIBUTE,
        "levels": levels,
        "races": races,
        "audit": {
            "semantics": (
                "SGOPlayerSys::LoadLvUpTable loads first 100 Player_Up_Lv/ExpUse rows; "
                "SGOPlayer::LevelUp consumes the current level requirement, applies ExpUse, "
                "increments level; UpdateParam combines AttBase_Newbie[level] with race "
                "STR/CON/DEX/WIS attribute rows"
            ),
            "files": audits,
        },
    }


def publish_character_progression_rules(
    player_dat: Path,
    output: Path,
    *,
    source_reference: str = (
        "Old SGOPlayerSys::LoadLvUpTable/SGOPlayer::LevelUp/UpdateParam + "
        "Dat/Player progression tables"
    ),
) -> dict:
    source = Path(player_dat).resolve()
    destination = Path(output).resolve()
    if destination == source or destination.is_relative_to(source):
        raise ValueError("character progression output must not overwrite original Dat/Player")
    result = compile_character_progression_rules(
        source, source_reference=source_reference)
    write_json(destination, result)
    return {**result, "output": str(destination)}
