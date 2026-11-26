"""Build fail-closed player equipment combat modifier authority.

Rules are emitted only when converted SGTest item stats and HK item.itd agree
exactly. Old SGOEquip::EquipItem decompilation supplies the gameplay semantics:
EffectAP/DP/HR/DR are accumulated into Player EffAP/DP/HR/DR. Weapons must also
pass the existing runtime/HK/Old Weapon.itd attack-distance bridge.
"""

from __future__ import annotations

from pathlib import Path

from asset_converter.content.build_item_catalog import compile_item_catalog
from asset_converter.content.build_weapon_definition_ranges import (
    compile_weapon_definition_ranges,
)
from asset_converter.json_files import write_json

KIND = "verified_equipment_combat_rules"
VERSION = 1


def _label(value: str, name: str) -> str:
    result = value.strip()
    if not result or len(result) > 256:
        raise ValueError(f"{name} source reference is invalid")
    return result


def _runtime_stat(presentation: dict, key: str) -> int:
    value = presentation.get(key, 0)
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        raise ValueError(f"runtime equipment {key} must be a nonnegative integer")
    return value


def compile_equipment_combat_rules(
    items_root: Path,
    hk_item_itd: Path,
    weapon_itd: Path,
    *,
    item_source_reference: str = "converted SGTest Items/*.tres",
    hk_source_reference: str = "HK SGO Item/item.itd",
    old_equip_source_reference: str = (
        "Old SGOEquip::EquipItem + SGOPlayer::UpdateParam decompilation"
    ),
    old_weapon_source_reference: str = (
        "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd"
    ),
) -> dict:
    item_label = _label(item_source_reference, "item")
    hk_label = _label(hk_source_reference, "HK item")
    equip_label = _label(old_equip_source_reference, "Old equipment")
    weapon_label = _label(old_weapon_source_reference, "Old weapon")

    catalog = compile_item_catalog(
        Path(items_root),
        source_reference=item_label,
        hk_item_itd=Path(hk_item_itd),
        hk_source_reference=hk_label,
    )
    hk_audit = catalog.get("hk_evidence")
    if not isinstance(hk_audit, dict):
        raise ValueError("equipment combat bridge requires HK item evidence")
    if hk_audit.get("cross_version_match") != "unique_exact_name":
        raise ValueError("equipment combat bridge requires unique exact-name evidence")
    if hk_audit.get("runtime_item_ids_preserved") is not True:
        raise ValueError("equipment combat bridge requires preserved runtime item IDs")

    weapon_bridge = compile_weapon_definition_ranges(
        Path(items_root),
        Path(hk_item_itd),
        Path(weapon_itd),
        item_source_reference=item_label,
        hk_source_reference=hk_label,
        old_source_reference=weapon_label,
    )
    weapon_ranges = {
        row["item_definition_id"]: row["attack_distance"]
        for row in weapon_bridge["rules"]
    }

    candidates = stat_matches = shield_excluded = 0
    unresolved_weapon = stat_mismatches = 0
    rules: list[dict] = []
    seen: set[int] = set()

    for entry in catalog["items"]:
        category = entry.get("category")
        if category not in ("equip", "weapon"):
            continue
        presentation = entry.get("presentation")
        if not isinstance(presentation, dict):
            continue
        if "hk_equipment_category" not in presentation:
            continue
        candidates += 1

        # Original client comparison formatter gives this field a different
        # semantic label for type-0 category-6 shields. Do not guess whether it
        # contributes to EffHR, block, or another combat bucket.
        if (
            presentation.get("hk_original_type") == 0
            and presentation.get("hk_equipment_category") == 6
        ):
            shield_excluded += 1
            continue

        pairs = (
            ("atk", "hk_attack"),
            ("def", "hk_defense"),
            ("hit", "hk_hit_or_block"),
            ("flee", "hk_evasion"),
        )
        values: list[int] = []
        mismatch = False
        for runtime_key, hk_key in pairs:
            runtime = _runtime_stat(presentation, runtime_key)
            hk = presentation.get(hk_key)
            if not isinstance(hk, int) or isinstance(hk, bool) or hk < 0:
                mismatch = True
                break
            if runtime != hk:
                mismatch = True
                break
            values.append(runtime)
        if mismatch:
            stat_mismatches += 1
            continue
        stat_matches += 1

        definition = entry.get("item_id")
        if not isinstance(definition, int) or definition <= 0:
            raise ValueError("matched equipment has invalid runtime definition id")
        if definition in seen:
            raise ValueError("equipment combat bridge has duplicate runtime definition")
        source_sha256 = entry.get("source_sha256")
        if (
            not isinstance(source_sha256, str)
            or len(source_sha256) != 64
            or any(ch not in "0123456789abcdef" for ch in source_sha256)
        ):
            raise ValueError(
                f"matched equipment {definition} has invalid runtime source SHA256"
            )

        is_weapon = category == "weapon"
        attack_range = 0
        if is_weapon:
            attack_range = weapon_ranges.get(definition, 0)
            if attack_range <= 0:
                unresolved_weapon += 1
                continue

        attack, defense, hit, dodge = values
        seen.add(definition)
        rules.append(
            {
                "item_definition_id": definition,
                "attack_bonus": attack,
                "defense_bonus": defense,
                "hit_bonus": hit,
                "dodge_bonus": dodge,
                "is_weapon": is_weapon,
                "attack_range": attack_range,
                "runtime_source_sha256": source_sha256,
            }
        )

    rules.sort(key=lambda row: row["item_definition_id"])
    if not rules:
        raise ValueError("equipment combat bridge produced no verified rules")
    source_reference = (
        f"{item_label} | {hk_label} | {equip_label} | {weapon_label}"
    )
    if len(source_reference) > 512:
        raise ValueError("combined equipment combat source reference is too long")

    return {
        "kind": KIND,
        "version": VERSION,
        "rules_verified": True,
        "gameplay_ready": True,
        "rule_scope": "exact_runtime_hk_matches_only_fail_closed",
        "source_reference": source_reference,
        "rules": rules,
        "audit": {
            "runtime_item_count": catalog["item_count"],
            "equipment_candidates": candidates,
            "exact_stat_matches": stat_matches,
            "stat_mismatches": stat_mismatches,
            "shield_semantic_exclusions": shield_excluded,
            "unresolved_weapon_ranges": unresolved_weapon,
            "verified_rule_count": len(rules),
            "verified_weapon_rule_count": sum(1 for x in rules if x["is_weapon"]),
            "hk_source_sha256": hk_audit.get("source_sha256"),
            "weapon_bridge_verified_rule_count": len(weapon_bridge["rules"]),
        },
        "partial": (
            stat_mismatches > 0
            or shield_excluded > 0
            or unresolved_weapon > 0
        ),
        "status": "pass",
    }


def publish_equipment_combat_rules(
    items_root: Path,
    hk_item_itd: Path,
    weapon_itd: Path,
    output: Path,
    **kwargs,
) -> dict:
    items = Path(items_root).resolve()
    if (items / "Items").is_dir():
        items /= "Items"
    hk_source = Path(hk_item_itd).resolve()
    old_weapon = Path(weapon_itd).resolve()
    destination = Path(output).resolve()
    if (
        destination == hk_source
        or destination == old_weapon
        or destination == items
        or destination.is_relative_to(items)
    ):
        raise ValueError("equipment combat output overlaps a source")
    result = compile_equipment_combat_rules(
        items, hk_source, old_weapon, **kwargs
    )
    write_json(destination, result)
    return {**result, "output": str(destination)}
