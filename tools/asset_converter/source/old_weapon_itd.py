"""Parse Old Server Dat/Item/Weapon.itd with source-confirmed SGOEquip layout."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
from pathlib import Path
import struct

MAX_FILE = 4 * 1024 * 1024
MAX_RECORDS = 4096

# CONFIRMED by SGOEquip::ReadWeaponData:
# type byte + 0x2A bytes + 0x34 bytes + 8 bytes + 0x478 bytes.
RECORD_BYTES = 1 + 0x2A + 0x34 + 8 + 0x478
LEADING_TYPE = 1
WORD_BLOCK_OFFSET = 1 + 0x2A
WORD_COUNT = 0x34 // 2

# CONFIRMED by SearchIndex inheritance offsets:
# SGOItemData word fields begin at index 1 and ItemIndex is the first field.
# SGOWeaponData word fields occupy indexes 25..26: UseItem, AttDistance.
# ReadWeaponData copies internal word indexes 1..26 to the file word block.
ITEM_INDEX_WORD = 1
USE_ITEM_WORD = 25
ATT_DISTANCE_WORD = 26


@dataclass(frozen=True)
class OldWeaponRecord:
    source_index: int
    item_index: int
    use_item: int
    attack_distance: int


def _word_offset(index: int) -> int:
    if not 1 <= index <= WORD_COUNT:
        raise ValueError("Old Weapon.itd word index outside source layout")
    return WORD_BLOCK_OFFSET + (index - 1) * 2


def parse_old_weapon_itd(payload: bytes) -> list[OldWeaponRecord]:
    if not payload or len(payload) > MAX_FILE:
        raise ValueError("Old Weapon.itd payload size is invalid")
    if len(payload) % RECORD_BYTES:
        raise ValueError("Old Weapon.itd size is not a whole number of records")
    count = len(payload) // RECORD_BYTES
    if not 1 <= count <= MAX_RECORDS:
        raise ValueError("Old Weapon.itd record count is outside bounds")

    records: list[OldWeaponRecord] = []
    seen: set[int] = set()
    for source_index in range(count):
        start = source_index * RECORD_BYTES
        record = payload[start:start + RECORD_BYTES]
        if record[0] != LEADING_TYPE:
            raise ValueError(
                f"Old Weapon.itd record {source_index} has unexpected type {record[0]}")
        item_index = struct.unpack_from(
            "<H", record, _word_offset(ITEM_INDEX_WORD))[0]
        use_item = struct.unpack_from(
            "<H", record, _word_offset(USE_ITEM_WORD))[0]
        attack_distance = struct.unpack_from(
            "<H", record, _word_offset(ATT_DISTANCE_WORD))[0]
        if item_index == 0 or item_index in seen:
            raise ValueError(
                f"Old Weapon.itd invalid/duplicate ItemIndex {item_index}")
        if attack_distance == 0:
            raise ValueError(
                f"Old Weapon.itd ItemIndex {item_index} has zero AttDistance")
        seen.add(item_index)
        records.append(OldWeaponRecord(
            source_index, item_index, use_item, attack_distance))
    return records


def read_old_weapon_itd(path: Path) -> tuple[list[OldWeaponRecord], dict]:
    source = Path(path).resolve()
    if not source.is_file() or source.is_symlink():
        raise ValueError("Old Weapon.itd source is missing or symbolic")
    payload = source.read_bytes()
    records = parse_old_weapon_itd(payload)
    distances = sorted({row.attack_distance for row in records})
    return records, {
        "source_sha256": hashlib.sha256(payload).hexdigest(),
        "source_bytes": len(payload),
        "record_bytes": RECORD_BYTES,
        "record_count": len(records),
        "leading_type": LEADING_TYPE,
        "item_index_word": ITEM_INDEX_WORD,
        "use_item_word": USE_ITEM_WORD,
        "attack_distance_word": ATT_DISTANCE_WORD,
        "attack_distance_values": distances,
        "layout": (
            "ReadWeaponData: 1 + 0x2A + 0x34 + 8 + 0x478; "
            "SGOItemData ItemIndex=WORD[1]; "
            "SGOWeaponData UseItem/AttDistance=WORD[25]/WORD[26]"),
    }
