"""Parse source-backed Old Server monster drop rules conservatively."""

from __future__ import annotations

import csv
import hashlib
import re
from decimal import Decimal, InvalidOperation
from pathlib import Path

UINT32_MAX = 0xFFFFFFFF
OLD_RATE_UNIT = 429496  # floor(UINT32_MAX / 10000), matches CheckDropItem.
DROP_COUNT_A_COLUMN = 72
DROP_COUNT_B_COLUMN = 73
DROP_PAIR_START_COLUMN = 74
MAX_DROP_PAIRS = 60
MAX_ITEM_ID = 0xFFFF


def _read_source(path: Path, *, max_bytes: int = 16_777_216) -> bytes:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError(f"missing or symbolic Old Server source: {source}")
    size = source.stat().st_size
    if size < 1 or size > max_bytes:
        raise ValueError(f"Old Server source size outside bounds: {source}")
    return source.read_bytes()


def _cfg_int(text: str, key: str, *, minimum: int, maximum: int) -> int:
    match = re.search(
        rf"(?mi)^\s*{re.escape(key)}\s*=\s*([0-9]+)\b", text)
    if match is None:
        raise ValueError(f"enemy.cfg missing {key}")
    value = int(match.group(1), 10)
    if value < minimum or value > maximum:
        raise ValueError(f"enemy.cfg {key} outside bounds")
    return value


def read_enemy_drop_settings(enemy_cfg: Path) -> tuple[dict, dict]:
    payload = _read_source(enemy_cfg, max_bytes=1_048_576)
    try:
        text = payload.decode("cp950")
    except UnicodeDecodeError as exc:
        raise ValueError("enemy.cfg is not valid cp950") from exc
    level_delta = _cfg_int(
        text, "LvDeltaForDropItemRateDrop", minimum=1, maximum=255)
    max_luck_units = _cfg_int(
        text, "MaxDropRateForCheckLuckItem", minimum=1, maximum=10_000)
    return {
        "level_delta_divisor": level_delta,
        "max_check_luck_rate_units": max_luck_units,
        "check_luck_cap_threshold": max_luck_units * OLD_RATE_UNIT,
    }, {
        "enemy_cfg_sha256": hashlib.sha256(payload).hexdigest(),
        "enemy_cfg_bytes": len(payload),
    }


def _parse_rate(value: str, *, row_number: int, pair_index: int) -> int:
    text = value.strip()
    try:
        rate = Decimal(text)
    except InvalidOperation as exc:
        raise ValueError(
            f"invalid drop rate row {row_number} pair {pair_index}") from exc
    if not rate.is_finite() or rate < 0 or rate > 1:
        raise ValueError(
            f"unsafe drop rate row {row_number} pair {pair_index}")
    # Old LoadEnemyData multiplies the positive double by 0xffffffff and
    # converts to an integer before CheckDropItem compares roll < threshold.
    return int(rate * UINT32_MAX)


def parse_enemy_drop_rows(
    text: str,
    *,
    required_enemy_ids: set[int],
    check_luck_cap_threshold: int,
) -> tuple[dict[int, list[dict]], list[dict]]:
    if not required_enemy_ids:
        raise ValueError("at least one required EnemyId is needed")
    if check_luck_cap_threshold < 1 or check_luck_cap_threshold > UINT32_MAX:
        raise ValueError("invalid check-luck threshold")

    result: dict[int, list[dict]] = {}
    skipped: list[dict] = []
    seen_required: set[int] = set()
    for row_number, row in enumerate(csv.reader(text.splitlines()), 1):
        if not row or all(not value.strip() for value in row):
            continue
        try:
            enemy_id = int(row[0].strip(), 10)
        except (ValueError, IndexError) as exc:
            raise ValueError(f"invalid EnemyId row {row_number}") from exc
        if enemy_id not in required_enemy_ids:
            continue
        if enemy_id in seen_required:
            raise ValueError(f"duplicate Enemy_Data.csv EnemyId {enemy_id}")
        seen_required.add(enemy_id)
        if len(row) <= DROP_COUNT_B_COLUMN:
            raise ValueError(f"EnemyId {enemy_id} row too short for drop counts")
        try:
            count_a = int(row[DROP_COUNT_A_COLUMN].strip(), 10)
            count_b = int(row[DROP_COUNT_B_COLUMN].strip(), 10)
        except ValueError as exc:
            raise ValueError(f"EnemyId {enemy_id} invalid drop counts") from exc
        total = count_a + count_b
        if count_a < 0 or count_b < 0 or total > MAX_DROP_PAIRS:
            raise ValueError(f"EnemyId {enemy_id} unsafe drop counts")
        needed = DROP_PAIR_START_COLUMN + total * 2
        if len(row) < needed:
            raise ValueError(f"EnemyId {enemy_id} truncated drop pairs")

        rules: list[dict] = []
        runtime_slot = 0
        for pair_index in range(total):
            base = DROP_PAIR_START_COLUMN + pair_index * 2
            try:
                item_id = int(row[base].strip(), 10)
            except ValueError as exc:
                raise ValueError(
                    f"EnemyId {enemy_id} invalid item pair {pair_index}") from exc
            threshold = _parse_rate(
                row[base + 1], row_number=row_number, pair_index=pair_index)
            check_luck = pair_index < count_a
            if item_id == 0:
                continue
            if item_id < 1 or item_id > MAX_ITEM_ID:
                raise ValueError(f"EnemyId {enemy_id} item outside WORD range")
            if threshold <= 0:
                skipped.append({
                    "enemy_id": enemy_id,
                    "source_pair_index": pair_index,
                    "item_definition_id": item_id,
                    "reason": "zero_probability",
                })
                continue
            if check_luck and threshold < check_luck_cap_threshold:
                # Old CheckDropItem multiplies this threshold by (Luck+1) before
                # applying the cap. Without a verified runtime Luck field the
                # effective probability depends on player state, so fail closed.
                skipped.append({
                    "enemy_id": enemy_id,
                    "source_pair_index": pair_index,
                    "item_definition_id": item_id,
                    "reason": "luck_sensitive_below_verified_cap",
                })
                continue
            effective = (
                min(threshold, check_luck_cap_threshold)
                if check_luck else threshold
            )
            rules.append({
                "slot": runtime_slot,
                "source_pair_index": pair_index,
                "item_definition_id": item_id,
                "roll_threshold": effective,
                "check_luck_source": check_luck,
            })
            runtime_slot += 1
        result[enemy_id] = rules

    missing = sorted(required_enemy_ids.difference(seen_required))
    if missing:
        raise ValueError(f"Enemy_Data.csv missing required EnemyIds: {missing}")
    return result, skipped


def read_old_enemy_drop_catalog(
    enemy_data_csv: Path,
    enemy_cfg: Path,
    *,
    required_enemy_ids: set[int],
) -> tuple[dict[int, list[dict]], dict, dict]:
    settings, cfg_audit = read_enemy_drop_settings(enemy_cfg)
    payload = _read_source(enemy_data_csv)
    try:
        text = payload.decode("cp950")
    except UnicodeDecodeError as exc:
        raise ValueError("Enemy_Data.csv is not valid cp950") from exc
    rules, skipped = parse_enemy_drop_rows(
        text,
        required_enemy_ids=required_enemy_ids,
        check_luck_cap_threshold=settings["check_luck_cap_threshold"],
    )
    audit = {
        "enemy_data_sha256": hashlib.sha256(payload).hexdigest(),
        "enemy_data_bytes": len(payload),
        **cfg_audit,
        "excluded_rules": skipped,
    }
    return rules, settings, audit
