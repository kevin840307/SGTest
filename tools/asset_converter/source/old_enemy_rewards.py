"""Audit Old Server Enemy_Data/EN_Att tables into exact monster EXP facts."""

from __future__ import annotations

import csv
import hashlib
from pathlib import Path

MAX_SOURCE_BYTES = 16_777_216
MAX_ENEMY_ID = 10_000_000
MAX_LEVEL = 255
MAX_REWARD = 0xFFFF


def _read_bytes(path: Path) -> bytes:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError(f"missing or symbolic Old Server source: {source}")
    size = source.stat().st_size
    if size < 1 or size > MAX_SOURCE_BYTES:
        raise ValueError(f"Old Server source size outside bounds: {source}")
    return source.read_bytes()


def _parse_int(value: str, *, label: str, minimum: int, maximum: int) -> int:
    text = value.strip()
    if not text or not text.isascii():
        raise ValueError(f"invalid {label}")
    try:
        number = int(text, 10)
    except ValueError as exc:
        raise ValueError(f"invalid {label}") from exc
    if number < minimum or number > maximum:
        raise ValueError(f"unsafe {label}: {number}")
    return number


def parse_enemy_attribute_table(text: str, *, table_id: int) -> dict[int, int]:
    """Return level-key -> RExp.

    Old CDEnemyDistSys::LoadEnAttData reads nine integer columns into
    CDEnemyAttr. Column 7 (zero-based) becomes +0x22, and AddAttr copies that
    field into CDEnemyWORDStr[12] (RExp).
    """
    if table_id < 1 or table_id > 255:
        raise ValueError("invalid EN_Att table id")
    rewards: dict[int, int] = {}
    reader = csv.reader(text.splitlines())
    for row_number, row in enumerate(reader, 1):
        if not row or all(not value.strip() for value in row):
            continue
        if len(row) != 9:
            raise ValueError(
                f"EN_Att{table_id:02d}.csv row {row_number} must contain 9 columns")
        level_key = _parse_int(
            row[0], label=f"EN_Att level key row {row_number}",
            minimum=1, maximum=MAX_LEVEL)
        reward = _parse_int(
            row[7], label=f"EN_Att RExp row {row_number}",
            minimum=0, maximum=MAX_REWARD)
        if level_key in rewards:
            if rewards[level_key] != reward:
                raise ValueError(
                    f"ambiguous EN_Att{table_id:02d}.csv RExp for level "
                    f"key {level_key}")
            # Old FindAttr ultimately uses CRT bsearch, whose duplicate-key
            # selection is unspecified. A duplicate is safe for this EXP-only
            # projection only when every candidate has the same RExp.
            continue
        rewards[level_key] = reward
    if not rewards:
        raise ValueError(f"EN_Att{table_id:02d}.csv has no rows")
    return rewards


def parse_enemy_catalog(text: str) -> dict[int, tuple[int, int]]:
    """Return EnemyId -> (level key, EN_Att selector).

    Old CDEnemyDistSys::LoadNewCSVData writes Enemy_Data.csv column 3 to
    WORD[5], the key used by AddAttr::FindAttr, and column 4 to BYTE[0x23],
    the FindAttrTbl selector. The same key is the source level used by the
    existing SGTest visual evidence catalog.
    """
    definitions: dict[int, tuple[int, int]] = {}
    reader = csv.reader(text.splitlines())
    for row_number, row in enumerate(reader, 1):
        if not row or all(not value.strip() for value in row):
            continue
        if len(row) < 5:
            raise ValueError(f"Enemy_Data.csv row {row_number} is too short")
        enemy_id = _parse_int(
            row[0], label=f"EnemyId row {row_number}",
            minimum=1, maximum=MAX_ENEMY_ID)
        level_key = _parse_int(
            row[3], label=f"enemy level key row {row_number}",
            minimum=1, maximum=0xFFFF)
        selector = _parse_int(
            row[4], label=f"enemy attr selector row {row_number}",
            minimum=1, maximum=255)
        if enemy_id in definitions:
            raise ValueError(f"duplicate Enemy_Data.csv EnemyId {enemy_id}")
        definitions[enemy_id] = (level_key, selector)
    if not definitions:
        raise ValueError("Enemy_Data.csv has no enemy rows")
    return definitions


def read_old_enemy_reward_catalog(
    enemy_data_csv: Path,
    *,
    required_enemy_ids: set[int],
) -> tuple[dict[int, dict], dict]:
    """Resolve only requested enemies, failing closed on any missing mapping."""
    if not required_enemy_ids:
        raise ValueError("at least one required EnemyId is needed")
    if any(enemy_id < 1 or enemy_id > MAX_ENEMY_ID
           for enemy_id in required_enemy_ids):
        raise ValueError("required EnemyId outside bounds")

    enemy_path = Path(enemy_data_csv).resolve()
    payload = _read_bytes(enemy_path)
    try:
        catalog_text = payload.decode("cp950")
    except UnicodeDecodeError as exc:
        raise ValueError("Enemy_Data.csv is not valid cp950") from exc
    definitions = parse_enemy_catalog(catalog_text)

    missing = sorted(required_enemy_ids.difference(definitions))
    if missing:
        raise ValueError(f"Enemy_Data.csv missing required EnemyIds: {missing}")

    selectors = sorted({
        definitions[enemy_id][1]
        for enemy_id in required_enemy_ids
        if definitions[enemy_id][0] != 0xFFFF
    })
    tables: dict[int, dict[int, int]] = {}
    table_audit = []
    for selector in selectors:
        table_path = enemy_path.parent / f"EN_Att{selector:02d}.csv"
        table_payload = _read_bytes(table_path)
        try:
            table_text = table_payload.decode("ascii")
        except UnicodeDecodeError as exc:
            raise ValueError(
                f"EN_Att{selector:02d}.csv must be ASCII") from exc
        tables[selector] = parse_enemy_attribute_table(
            table_text, table_id=selector)
        table_audit.append({
            "table": selector,
            "sha256": hashlib.sha256(table_payload).hexdigest(),
            "bytes": len(table_payload),
        })

    resolved: dict[int, dict] = {}
    excluded = []
    for enemy_id in sorted(required_enemy_ids):
        level_key, selector = definitions[enemy_id]
        if level_key == 0xFFFF:
            # Old AddAttr passes this key to FindAttr. EN_Att tables have no
            # 0xffff entry, so FindAttr fails and AddAttr returns false before
            # RExp is populated. Preserve that no-reward result explicitly.
            excluded.append({
                "enemy_id": enemy_id,
                "level_key": level_key,
                "attribute_table": selector,
                "reason": "old_add_attr_find_attr_miss",
            })
            continue
        if level_key > MAX_LEVEL:
            raise ValueError(
                f"EnemyId {enemy_id} uses unsupported level key {level_key}")
        table = tables[selector]
        if level_key not in table:
            raise ValueError(
                f"EnemyId {enemy_id} level {level_key} missing from "
                f"EN_Att{selector:02d}.csv")
        resolved[enemy_id] = {
            "enemy_id": enemy_id,
            "enemy_level": level_key,
            "attribute_table": selector,
            "r_exp": table[level_key],
        }

    return resolved, {
        "enemy_data_sha256": hashlib.sha256(payload).hexdigest(),
        "enemy_data_bytes": len(payload),
        "attribute_tables": table_audit,
        "excluded_no_attribute_reward": excluded,
    }
