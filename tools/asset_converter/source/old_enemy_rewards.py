"""Resolve audited fields from the Old Server Enemy_Data/EN_Att join."""

from __future__ import annotations

import csv
import hashlib
from pathlib import Path

MAX_SOURCE_BYTES = 16_777_216
MAX_ENEMY_ID = 10_000_000
MAX_LEVEL = 255
MAX_WORD = 0xFFFF
MAX_DWORD = 0x7FFFFFFF

# Zero-based Enemy_Data columns, shared by the parser and evidence exporter.
ENEMY_BEHAVIOR_COLUMNS = {
    'size': 9, 'attack_length': 10, 'visibility_area': 13,
    'ai_type_code': 14, 'ai_attack_level': 15, 'reborn_time_ms': 19,
}


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
    """Return level-key -> RExp from the ninth EN_Att field.

    Old CDEnemyDistSys::LoadEnAttData reads nine integer columns into
    CDEnemyAttr. Column 8 (zero-based) becomes +0x24, and AddAttr copies that
    field into internal WORD property index 16 (RExp). Column 7 becomes +0x22
    and internal WORD property index 12 (DR), which combat hit checks consume.
    """
    return _parse_attribute_column(
        text, table_id, 8, "RExp", 0, maximum=MAX_WORD)


def _parse_attribute_column(
    text: str, table_id: int, column: int, label: str, minimum: int,
    *, maximum: int = MAX_DWORD,
) -> dict[int, int]:
    if table_id < 1 or table_id > 255:
        raise ValueError("invalid EN_Att table id")
    values: dict[int, int] = {}
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
        value = _parse_int(
            row[column], label=f"EN_Att {label} row {row_number}",
            minimum=minimum, maximum=maximum)
        if level_key in values:
            if values[level_key] != value:
                raise ValueError(
                    f"ambiguous EN_Att{table_id:02d}.csv {label} for level "
                    f"key {level_key}")
            # Old FindAttr ultimately uses CRT bsearch, whose duplicate-key
            # selection is unspecified. The projected field must agree.
            continue
        values[level_key] = value
    if not values:
        raise ValueError(f"EN_Att{table_id:02d}.csv has no rows")
    return values


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


def parse_enemy_behavior_evidence(text: str, *, required_enemy_ids: set[int]) -> dict[int, dict]:
    """Read only confirmed definition fields, not an executable AI profile.

    LoadNewCSVData writes column 9 to WORD[22] (Size, 0x1007259D),
    10 to WORD[13] (AttLen, 0x100725C0), 13 to WORD[19] (VisArea,
    0x10072629), the first byte of column 14 to WORD[14] (AIType,
    0x1007264C..0x1007266F), and 15 to WORD[15] (AIAttLv, 0x10072678).
    0x10072722..0x10072748 multiplies column 19 by 1000 before writing
    DWORD[3] (RebornTime). Property names: CDEnemyWORDStr/CDEnemyDWORDStr
    at 0x100ADF24/0x100ADF78. These are source parameters, not executable
    SGTest AI rules or a proof of runtime scheduling.
    """
    result: dict[int, dict] = {}
    for row in csv.reader(text.splitlines()):
        if not row or all(not value.strip() for value in row):
            continue
        enemy_id = _parse_int(row[0], label="EnemyId", minimum=1,
                              maximum=MAX_ENEMY_ID)
        if enemy_id not in required_enemy_ids:
            continue
        if enemy_id in result:
            raise ValueError(f"duplicate Enemy_Data.csv EnemyId {enemy_id}")
        fields = {}
        for name, column in ENEMY_BEHAVIOR_COLUMNS.items():
            if len(row) <= column or not row[column].strip():
                continue
            value = row[column]
            if name == 'ai_type_code':
                # The old loader consumes the first byte. Refuse ambiguous
                # tokens rather than silently truncating or recoding them.
                if len(value) != 1 or not value.isascii() or not value.isprintable():
                    raise ValueError(f"invalid EnemyId {enemy_id} {name}")
                fields[name] = value
                continue
            multiplier = 1000 if name == 'reborn_time_ms' else 1
            maximum = MAX_DWORD // multiplier if multiplier != 1 else MAX_WORD
            fields[name] = _parse_int(
                value, label=f"EnemyId {enemy_id} {name}",
                minimum=0, maximum=maximum) * multiplier
        result[enemy_id] = fields
    if required_enemy_ids.difference(result):
        raise ValueError("missing Enemy_Data.csv behavior evidence definition")
    return result


def read_old_enemy_reward_catalog(
    enemy_data_csv: Path,
    *,
    required_enemy_ids: set[int],
) -> tuple[dict[int, dict], dict]:
    """Resolve only requested enemies, failing closed on any missing mapping."""
    return _read_old_enemy_attribute_catalog(
        enemy_data_csv, required_enemy_ids, 8, "RExp", 0, "r_exp",
        "excluded_no_attribute_reward", maximum=MAX_WORD)


def read_old_enemy_hp_catalog(
    enemy_data_csv: Path, *, required_enemy_ids: set[int],
) -> tuple[dict[int, dict], dict]:
    """EN_Att[1] -> CDEnemyAttr+0x0C -> WORD[4] -> initial HP WORD[6].

    Old LoadEnAttData 0x10071307, AddAttr 0x10078451, and
    CDEnemyMgr::CloneChild 0x10081552–0x10081570 establish the chain.
    """
    return _read_old_enemy_attribute_catalog(
        enemy_data_csv, required_enemy_ids, 1, "HPMAX", 1, "max_hp",
        "excluded_no_attribute_hp")


def read_old_enemy_dr_catalog(
    enemy_data_csv: Path, *, required_enemy_ids: set[int],
) -> tuple[dict[int, dict], dict]:
    """Resolve the Old enemy DR word consumed by GetEnemyDefendAttrib.

    LoadEnAttData column 7 -> CDEnemyAttr+0x22; AddAttr maps that value to
    internal WORD property index 12 (DR), and GetEnemyDefendAttrib copies
    property 12 to sTarget+0x1C for IsHit_PerHit.
    """
    return _read_old_enemy_attribute_catalog(
        enemy_data_csv, required_enemy_ids, 7, "DR", 0, "dodge_rate",
        "excluded_no_attribute_dr", maximum=MAX_WORD)


def read_old_enemy_combat_catalog(
    enemy_data_csv: Path, *, required_enemy_ids: set[int],
) -> tuple[dict[int, dict], dict]:
    """Resolve the full audited nine-column EN_Att combat row in one pass."""
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
    behavior_by_enemy = parse_enemy_behavior_evidence(
        catalog_text, required_enemy_ids=required_enemy_ids)

    selectors = sorted({
        definitions[enemy_id][1]
        for enemy_id in required_enemy_ids
        if definitions[enemy_id][0] != 0xFFFF
    })
    tables: dict[int, dict[int, dict]] = {}
    table_audit = []
    for selector in selectors:
        table_path = enemy_path.parent / f"EN_Att{selector:02d}.csv"
        table_payload = _read_bytes(table_path)
        try:
            table_text = table_payload.decode("ascii")
        except UnicodeDecodeError as exc:
            raise ValueError(
                f"EN_Att{selector:02d}.csv must be ASCII") from exc
        rows: dict[int, dict[str, int | None]] = {}
        for row_number, row in enumerate(csv.reader(table_text.splitlines()), 1):
            if not row or all(not value.strip() for value in row):
                continue
            if len(row) != 9:
                raise ValueError(
                    f"EN_Att{selector:02d}.csv row {row_number} must contain 9 columns")
            level_key = _parse_int(
                row[0], label=f"EN_Att level key row {row_number}",
                minimum=1, maximum=MAX_LEVEL)
            values = {
                "max_hp": _parse_int(row[1], label="HPMAX", minimum=1,
                                     maximum=MAX_DWORD),
                "attack_power": _parse_int(row[2], label="AP", minimum=0,
                                            maximum=MAX_DWORD),
                "defense_power": _parse_int(row[3], label="DP", minimum=0,
                                             maximum=MAX_DWORD),
                "magic_attack_power": _parse_int(row[4], label="MAP", minimum=0,
                                                  maximum=MAX_DWORD),
                "magic_defense_power": _parse_int(row[5], label="MDP", minimum=0,
                                                   maximum=MAX_DWORD),
                "hit_rate": _parse_int(row[6], label="HR", minimum=0,
                                       maximum=MAX_WORD),
                "dodge_rate": _parse_int(row[7], label="DR", minimum=0,
                                         maximum=MAX_WORD),
                "r_exp": _parse_int(row[8], label="RExp", minimum=0,
                                    maximum=MAX_WORD),
            }
            if level_key in rows:
                current = rows[level_key]
                for field, value in values.items():
                    if current[field] is not None and current[field] != value:
                        current[field] = None
                continue
            rows[level_key] = values
        if not rows:
            raise ValueError(f"EN_Att{selector:02d}.csv has no rows")
        tables[selector] = rows
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
            excluded.append({
                "enemy_id": enemy_id,
                "level_key": level_key,
                "attribute_table": selector,
                "reason": "old_add_attr_find_attr_miss",
            })
            continue
        if level_key > MAX_LEVEL or level_key not in tables[selector]:
            raise ValueError(
                f"EnemyId {enemy_id} level {level_key} missing from "
                f"EN_Att{selector:02d}.csv")
        row = tables[selector][level_key]
        stable = {field: value for field, value in row.items()
                  if value is not None}
        ambiguous = sorted(field for field, value in row.items()
                           if value is None)
        resolved[enemy_id] = {
            "enemy_id": enemy_id,
            "enemy_level": level_key,
            "attribute_table": selector,
            **stable,
        }
        if behavior_by_enemy[enemy_id]:
            resolved[enemy_id]["definition_behavior"] = behavior_by_enemy[enemy_id]
        if ambiguous:
            resolved[enemy_id]["ambiguous_fields"] = ambiguous

    return resolved, {
        "enemy_data_sha256": hashlib.sha256(payload).hexdigest(),
        "enemy_data_bytes": len(payload),
        "attribute_tables": table_audit,
        "excluded_no_attributes": excluded,
    }


def _read_old_enemy_attribute_catalog(
    enemy_data_csv: Path, required_enemy_ids: set[int], column: int,
    label: str, minimum: int, value_key: str, exclusion_key: str,
    *, maximum: int = MAX_DWORD,
) -> tuple[dict[int, dict], dict]:
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
        tables[selector] = _parse_attribute_column(
            table_text, selector, column, label, minimum, maximum=maximum)
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
            # the audited field is populated. Preserve that miss explicitly.
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
            value_key: table[level_key],
        }

    return resolved, {
        "enemy_data_sha256": hashlib.sha256(payload).hexdigest(),
        "enemy_data_bytes": len(payload),
        "attribute_tables": table_audit,
        exclusion_key: excluded,
    }
