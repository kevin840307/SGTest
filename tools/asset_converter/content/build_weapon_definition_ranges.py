"""Build fail-closed SGTest weapon-definition attack-range rules.

This builder does not promote the presentation item catalog to gameplay
authority.  It re-reads the converted SGTest item sources, independently
crosswalks them to HK item.itd by the existing unique exact-name evidence rule,
then requires that the HK item id is an Old Server Weapon.itd ItemIndex and that
the converted weapon attack_range exactly equals Old AttDistance.

Only three-way matches are emitted.  Unmatched source rows stay unresolved and
must remain combat-locked at runtime.
"""

from __future__ import annotations

from pathlib import Path

from asset_converter.content.build_item_catalog import compile_item_catalog
from asset_converter.json_files import write_json
from asset_converter.source.old_weapon_itd import read_old_weapon_itd

KIND = "verified_weapon_definition_attack_ranges"
VERSION = 1


def _label(value: str, name: str) -> str:
    result = value.strip()
    if not result or len(result) > 256:
        raise ValueError(f"{name} source reference is invalid")
    return result


def compile_weapon_definition_ranges(
    items_root: Path,
    hk_item_itd: Path,
    weapon_itd: Path,
    *,
    item_source_reference: str = "converted SGTest Items/*.tres",
    hk_source_reference: str = "HK SGO Item/item.itd",
    old_source_reference: str = (
        "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd"
    ),
) -> dict:
    """Return individually verified SGTest definition -> attack-distance rules."""
    item_label = _label(item_source_reference, "item")
    hk_label = _label(hk_source_reference, "HK item")
    old_label = _label(old_source_reference, "Old weapon")

    catalog = compile_item_catalog(
        Path(items_root),
        source_reference=item_label,
        hk_item_itd=Path(hk_item_itd),
        hk_source_reference=hk_label,
    )
    hk_audit = catalog.get("hk_evidence")
    if not isinstance(hk_audit, dict):
        raise ValueError("weapon definition bridge requires HK item evidence")
    if hk_audit.get("cross_version_match") != "unique_exact_name":
        raise ValueError("weapon definition bridge requires unique exact-name evidence")
    if hk_audit.get("runtime_item_ids_preserved") is not True:
        raise ValueError("weapon definition bridge requires preserved runtime item IDs")

    old_records, old_audit = read_old_weapon_itd(Path(weapon_itd))
    old_by_item = {row.item_index: row for row in old_records}

    runtime_weapon_count = 0
    hk_weapon_matches = 0
    rules: list[dict] = []
    matched_old: set[int] = set()
    matched_definitions: set[int] = set()

    for entry in catalog["items"]:
        if entry.get("category") != "weapon":
            continue
        runtime_weapon_count += 1
        presentation = entry.get("presentation")
        if not isinstance(presentation, dict):
            continue
        hk_item_id = presentation.get("hk_original_item_id")
        if not isinstance(hk_item_id, int) or hk_item_id <= 0:
            continue
        hk_weapon_matches += 1
        old = old_by_item.get(hk_item_id)
        if old is None:
            continue

        definition_id = entry.get("item_id")
        if not isinstance(definition_id, int) or definition_id <= 0:
            raise ValueError("matched weapon has invalid runtime definition id")
        source_sha256 = entry.get("source_sha256")
        if (
            not isinstance(source_sha256, str)
            or len(source_sha256) != 64
            or any(ch not in "0123456789abcdef" for ch in source_sha256)
        ):
            raise ValueError(
                f"matched weapon {definition_id} has invalid source SHA256"
            )
        converted_range = presentation.get("attack_range")
        if not isinstance(converted_range, int) or converted_range <= 0:
            raise ValueError(
                f"matched weapon {definition_id} has no positive converted attack_range"
            )
        if converted_range != old.attack_distance:
            raise ValueError(
                "weapon attack-range evidence disagrees for "
                f"definition {definition_id}: converted={converted_range}, "
                f"Old={old.attack_distance}"
            )
        if definition_id in matched_definitions or old.item_index in matched_old:
            raise ValueError("weapon definition bridge is not one-to-one")
        matched_definitions.add(definition_id)
        matched_old.add(old.item_index)
        rules.append(
            {
                "item_definition_id": definition_id,
                "old_item_index": old.item_index,
                "attack_distance": old.attack_distance,
                "use_item": old.use_item,
                "runtime_source_sha256": source_sha256,
            }
        )

    rules.sort(key=lambda row: row["item_definition_id"])
    if not rules:
        raise ValueError("weapon definition bridge produced no verified rules")

    unresolved_old = sorted(set(old_by_item) - matched_old)
    matched_distances = sorted({row["attack_distance"] for row in rules})
    return {
        "kind": KIND,
        "version": VERSION,
        "rules_verified": True,
        "rule_scope": "three_way_matches_only_fail_closed",
        "source_reference": {
            "runtime_items": item_label,
            "hk_items": hk_label,
            "old_weapons": old_label,
        },
        "rules": rules,
        "audit": {
            "runtime_item_count": catalog["item_count"],
            "runtime_weapon_count": runtime_weapon_count,
            "hk_unique_name_matches": hk_audit.get("unique_name_matches", 0),
            "hk_weapon_matches": hk_weapon_matches,
            "old_weapon_count": len(old_records),
            "verified_rule_count": len(rules),
            "unresolved_old_item_indices": unresolved_old,
            "matched_attack_distance_values": matched_distances,
            "hk_source_sha256": hk_audit.get("source_sha256"),
            "old_source_sha256": old_audit["source_sha256"],
            "old_record_bytes": old_audit["record_bytes"],
        },
        "partial": bool(unresolved_old),
        "status": "pass",
    }


def publish_weapon_definition_ranges(
    items_root: Path,
    hk_item_itd: Path,
    weapon_itd: Path,
    output: Path,
    *,
    item_source_reference: str = "converted SGTest Items/*.tres",
    hk_source_reference: str = "HK SGO Item/item.itd",
    old_source_reference: str = (
        "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd"
    ),
) -> dict:
    items = Path(items_root).resolve()
    if (items / "Items").is_dir():
        items /= "Items"
    hk_source = Path(hk_item_itd).resolve()
    old_source = Path(weapon_itd).resolve()
    destination = Path(output).resolve()
    if (
        destination == hk_source
        or destination == old_source
        or destination == items
        or destination.is_relative_to(items)
    ):
        raise ValueError("weapon definition-range output overlaps a source")
    result = compile_weapon_definition_ranges(
        items,
        hk_source,
        old_source,
        item_source_reference=item_source_reference,
        hk_source_reference=hk_source_reference,
        old_source_reference=old_source_reference,
    )
    write_json(destination, result)
    return {**result, "output": str(destination)}
