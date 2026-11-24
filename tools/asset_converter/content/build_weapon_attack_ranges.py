"""Publish source-audited Old Server weapon attack-distance rules."""

from __future__ import annotations

from pathlib import Path

from asset_converter.json_files import write_json
from asset_converter.source.old_weapon_itd import read_old_weapon_itd

KIND = "verified_old_weapon_attack_ranges"
VERSION = 1


def compile_weapon_attack_ranges(
    weapon_itd: Path,
    *,
    source_reference: str = "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd",
) -> dict:
    label = source_reference.strip()
    if not label or len(label) > 256:
        raise ValueError("weapon attack-range source reference is invalid")
    records, audit = read_old_weapon_itd(weapon_itd)
    return {
        "kind": KIND,
        "version": VERSION,
        "rules_verified": True,
        "source_reference": label,
        "rules": [
            {
                "item_index": row.item_index,
                "attack_distance": row.attack_distance,
                "use_item": row.use_item,
            }
            for row in records
        ],
        "audit": audit,
        "status": "pass",
    }


def publish_weapon_attack_ranges(
    weapon_itd: Path,
    output: Path,
    *,
    source_reference: str = "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd",
) -> dict:
    source = Path(weapon_itd).resolve()
    destination = Path(output).resolve()
    if destination == source or destination.is_relative_to(source.parent):
        raise ValueError("weapon attack-range output must not overwrite Old Item source")
    result = compile_weapon_attack_ranges(
        source, source_reference=source_reference)
    write_json(destination, result)
    return {**result, "output": str(destination)}
