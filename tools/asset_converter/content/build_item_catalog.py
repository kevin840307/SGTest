"""Compile source-derived item presentation metadata into a private client catalog.

The catalog is presentation-only. It may describe the source item's name,
requirements and display stats, but it never becomes server authority for
ownership, prices, recovery, damage, equip legality or any other gameplay rule.
Never publish private source content.
"""
from __future__ import annotations

from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

from asset_converter.source.hk_item_itd import read_hk_item_itd
from asset_converter.source.hk_item_metadata import read_hk_grading,read_hk_item_extra

CATEGORIES = frozenset((
    "booster", "equip", "general", "horse", "horsebattery",
    "horseequip", "recovery", "weapon",
))
INTEGER = re.compile(r"^-?[0-9]+$")
FIELD = re.compile(r'^([a-z_]+)\s*=\s*(.+)$')
MAX_NAME = 128
MAX_TEXT = 4096
MAX_ITEM_FILE = 128 * 1024
MAX_ITEMS = 20000
PRESENTATION_STRING_FIELDS = frozenset((
    "description", "special_effect", "req_job", "req_race", "horse_type",
))
PRESENTATION_INTEGER_FIELDS = frozenset((
    "price", "req_level", "durability", "spr_id", "equip_slot",
    "color_index", "rarity", "atk", "def", "matk", "mdef",
    "attack_range", "tick_count", "req_str", "req_dex", "req_con",
    "req_wis", "max_hp_bonus", "max_mp_bonus", "max_sp_bonus",
    "hit", "flee", "luk", "no_shield", "bullet_id",
    "use_range", "recover_hp", "recover_mp", "recover_sp", "move_speed",
    "body_id", "weapon_id", "engine_id", "max_battery", "battery",
    "atk_bonus", "def_bonus", "matk_bonus", "mdef_bonus", "luk_bonus",
    "hit_bonus", "flee_bonus", "crit_rate", "crit_damage", "aspd",
    "effect_type", "can_trade", "can_sell", "can_store", "can_repair",
))
RESOURCE_FIELDS = frozenset((
    "id", "item_name", "icon_id", "item_type_category",
)) | PRESENTATION_STRING_FIELDS | PRESENTATION_INTEGER_FIELDS


def _item_fields(raw: bytes, relative: str) -> dict:
    if len(raw) > MAX_ITEM_FILE or len(raw) == 0:
        raise ValueError(f"missing/oversized item source: {relative}")
    try:
        text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as exc:
        raise ValueError(f"item source not UTF-8: {relative}") from exc
    fields = {}
    # [resource] is the authoritative content block; ext_resource may also
    # contain script paths or other unrelated assignments.
    if "[resource]" not in text:
        raise ValueError(f"item has no Godot resource block: {relative}")
    for line in text.split("[resource]", 1)[1].splitlines():
        match = FIELD.fullmatch(line.strip())
        if match and match[1] in RESOURCE_FIELDS:
            if match[1] in fields:
                raise ValueError(f"duplicate {match[1]} in {relative}")
            fields[match[1]] = match[2]
    return fields


def _decode_string(fields: dict, key: str, relative: str, *,
                   required: bool, multiline: bool = False,
                   max_length: int = MAX_TEXT) -> str | None:
    value = fields.get(key)
    if value is None:
        if required:
            raise ValueError(f"missing {key} in {relative}")
        return None
    try:
        parsed = json.loads(value)
    except (ValueError, TypeError) as exc:
        raise ValueError(f"malformed string {key} in {relative}") from exc
    if not isinstance(parsed, str):
        raise ValueError(f"invalid {key} in {relative}")
    parsed = parsed.replace("\r\n", "\n").replace("\r", "\n").strip()
    if not parsed:
        if required:
            raise ValueError(f"invalid {key} in {relative}")
        return None
    if len(parsed) > max_length:
        raise ValueError(f"invalid {key} in {relative}")
    for char in parsed:
        code = ord(char)
        if code == 127 or (code < 32 and not (multiline and char in "\n\t")):
            raise ValueError(f"invalid {key} in {relative}")
    return parsed


def _string_field(fields: dict, key: str, relative: str) -> str:
    value = _decode_string(
        fields, key, relative, required=True, multiline=False, max_length=MAX_NAME)
    assert value is not None
    return value


def _integer(fields: dict, key: str, relative: str, *, default=None) -> int:
    value = fields.get(key)
    if value is None and default is not None:
        return default
    if value is None or not INTEGER.fullmatch(value):
        raise ValueError(f"invalid {key} in {relative}")
    parsed = int(value)
    if parsed < -1_000_000_000 or parsed > 1_000_000_000:
        raise ValueError(f"out-of-range {key} in {relative}")
    return parsed


def _presentation(fields: dict, relative: str) -> dict:
    result = {}
    for key in sorted(PRESENTATION_STRING_FIELDS):
        value = _decode_string(
            fields, key, relative, required=False, multiline=True,
            max_length=MAX_TEXT)
        if value is not None:
            result[key] = value
    for key in sorted(PRESENTATION_INTEGER_FIELDS):
        if key in fields:
            result[key] = _integer(fields, key, relative)
    return result


def _augment_hk_original(entries: list[dict], hk_item_itd: Path,
                         hk_source_reference: str,
                         hk_item_extra: Path | None = None,
                         hk_grading: Path | None = None) -> dict:
    if not hk_source_reference.strip() or len(hk_source_reference) > 256:
        raise ValueError("provide a nonempty HK source reference")
    if (hk_item_extra is None) != (hk_grading is None):
        raise ValueError("HK item_extra.csv and grading.csv must be supplied together")
    records,audit = read_hk_item_itd(hk_item_itd)
    hk_counts = Counter(x.name for x in records if x.name_valid and x.name)
    runtime_counts = Counter(x["name"] for x in entries)
    by_name = {
        x.name:x for x in records
        if x.name_valid and x.name and hk_counts[x.name] == 1
    }

    extras_by_id = {}
    grades_by_name = {}
    extra_audit = grading_audit = None
    if hk_item_extra is not None and hk_grading is not None:
        extras,extra_audit = read_hk_item_extra(hk_item_extra)
        grades,grading_audit = read_hk_grading(hk_grading)
        extras_by_id = {x.hk_item_id:x for x in extras}
        grades_by_name = {x.grade:x for x in grades}

    matched = tooltip_matches = grade_color_matches = extra_name_mismatches = 0
    structured_equipment_matches = 0
    for entry in entries:
        name = entry["name"]
        if runtime_counts[name] != 1:
            continue
        record = by_name.get(name)
        if record is None:
            continue
        matched += 1
        presentation = entry.setdefault("presentation",{})
        presentation["hk_original_item_id"] = record.hk_item_id
        presentation["hk_original_type"] = record.item_type
        presentation["hk_record_offset"] = record.offset
        if record.tooltip_valid and record.tooltip:
            presentation["hk_original_tooltip"] = record.tooltip
            tooltip_matches += 1
        if record.equipment_category is not None:
            presentation["hk_equipment_category"] = record.equipment_category
            presentation["hk_original_max_loss"] = record.max_loss
            presentation["hk_tooltip_loss_insert"] = record.tooltip_loss_insert
            presentation["hk_attack"] = record.attack
            presentation["hk_defense"] = record.defense
            presentation["hk_magic_attack"] = record.magic_attack
            presentation["hk_magic_defense"] = record.magic_defense
            presentation["hk_hit_or_block"] = record.hit_or_block
            presentation["hk_hit_or_block_label"] = (
                "阻擋率" if record.item_type == 0
                and record.equipment_category == 6 else "命中率")
            presentation["hk_evasion"] = record.evasion
            presentation["hk_luck"] = record.luck
            structured_equipment_matches += 1

        extra = extras_by_id.get(record.hk_item_id)
        if extra is not None:
            # Same-version ID join is additionally guarded by exact name so an
            # unexpected table mismatch can never leak a wrong grade/color.
            if extra.name != record.name:
                extra_name_mismatches += 1
                continue
            grade = grades_by_name.get(extra.grade)
            if grade is not None:
                presentation["hk_grade"] = extra.grade
                r,g,b,a = grade.effective_rgba
                presentation["hk_grade_r"] = r
                presentation["hk_grade_g"] = g
                presentation["hk_grade_b"] = b
                presentation["hk_grade_a"] = a
                grade_color_matches += 1
    result = {
        "source_reference":hk_source_reference.strip(),
        **audit,
        "cross_version_match":"unique_exact_name",
        "runtime_item_ids_preserved":True,
        "unique_name_matches":matched,
        "tooltip_matches":tooltip_matches,
        "structured_equipment_matches":structured_equipment_matches,
        "grade_color_matches":grade_color_matches,
        "extra_name_mismatches":extra_name_mismatches,
    }
    if extra_audit is not None and grading_audit is not None:
        result["item_extra"] = extra_audit
        result["grading"] = grading_audit
        result["grade_color_semantics"] = "original_client_effective_rgba"
    return result


def compile_item_catalog(items_root: Path, *, source_reference: str,
                         only_ids: set[int] | None = None,
                         hk_item_itd: Path | None = None,
                         hk_item_extra: Path | None = None,
                         hk_grading: Path | None = None,
                         hk_source_reference: str = "HK SGO Item/item.itd") -> dict:
    root = Path(items_root).resolve()
    if (root / "Items").is_dir():
        root /= "Items"
    if not root.is_dir():
        raise ValueError("Items source directory is missing")
    if not source_reference.strip() or len(source_reference) > 256:
        raise ValueError("provide a nonempty source reference (not a private absolute path)")
    if only_ids is not None and (
        not only_ids or any(not 1 <= x <= 100_000_000 for x in only_ids)):
        raise ValueError("invalid selected item IDs")
    if (hk_item_extra is None) != (hk_grading is None):
        raise ValueError("HK item_extra.csv and grading.csv must be supplied together")
    if hk_item_itd is None and hk_item_extra is not None:
        raise ValueError("HK item.itd is required for item_extra/grading cross-version evidence")
    entries = []
    seen = set()
    for category in sorted(CATEGORIES):
        folder = root / category
        if not folder.is_dir():
            continue
        for path in sorted(folder.glob("*.tres"), key=lambda p: p.name):
            if path.is_symlink() or not path.is_file():
                raise ValueError(f"symbolic/invalid source: {path.name}")
            relative = path.relative_to(root).as_posix()
            if not path.stem.isdigit():
                raise ValueError(f"non-numeric item source: {relative}")
            item_id = int(path.stem)
            if item_id <= 0 or item_id > 100_000_000:
                raise ValueError(f"invalid item ID: {relative}")
            if item_id in seen:
                raise ValueError(f"ambiguous duplicate item ID: {item_id}")
            seen.add(item_id)
            if only_ids is not None and item_id not in only_ids:
                continue
            raw = path.read_bytes()
            fields = _item_fields(raw, relative)
            if _integer(fields, "id", relative) != item_id:
                raise ValueError(f"file ID differs from resource ID: {relative}")
            name = _string_field(fields, "item_name", relative)
            declared_category = fields.get("item_type_category")
            if declared_category is not None:
                if _string_field(fields, "item_type_category", relative).lower() != category:
                    raise ValueError(f"category mismatch: {relative}")
            icon_id = _integer(fields, "icon_id", relative, default=0)
            if icon_id < 0 or icon_id > 100_000_000:
                raise ValueError(f"invalid icon ID: {relative}")
            entry = {
                "item_id": item_id, "name": name, "category": category,
                "icon_id": icon_id, "source_path": relative,
                "source_sha256": hashlib.sha256(raw).hexdigest(),
            }
            presentation = _presentation(fields, relative)
            if presentation:
                entry["presentation"] = presentation
            entries.append(entry)
            if len(entries) > MAX_ITEMS:
                raise ValueError("catalog exceeds bounded item count")
    entries.sort(key=lambda item: item["item_id"])
    if not entries:
        if only_ids is not None:
            raise ValueError("requested item ID has no source definition")
        raise ValueError("source contains no resolvable selected item definitions")
    if only_ids is not None and set(x["item_id"] for x in entries) != only_ids:
        raise ValueError("requested item ID has no source definition")
    result = {
        "kind": "source_derived_item_catalog", "version": 2,
        "source_reference": source_reference.strip(),
        "item_count": len(entries), "items": entries,
    }
    if hk_item_itd is not None:
        result["hk_evidence"] = _augment_hk_original(
            entries,Path(hk_item_itd),hk_source_reference,
            Path(hk_item_extra) if hk_item_extra is not None else None,
            Path(hk_grading) if hk_grading is not None else None)
    return result


def publish_item_catalog(items_root: Path, output: Path, *, source_reference: str,
                         only_ids: set[int] | None = None,
                         hk_item_itd: Path | None = None,
                         hk_item_extra: Path | None = None,
                         hk_grading: Path | None = None,
                         hk_source_reference: str = "HK SGO Item/item.itd") -> dict:
    source = Path(items_root).resolve()
    if (source / "Items").is_dir():
        source /= "Items"
    destination = Path(output).resolve()
    if destination == source or destination.is_relative_to(source):
        raise ValueError("output may not overwrite source Items")
    result = compile_item_catalog(
        source,source_reference=source_reference,only_ids=only_ids,
        hk_item_itd=hk_item_itd,hk_item_extra=hk_item_extra,
        hk_grading=hk_grading,hk_source_reference=hk_source_reference)
    content = (json.dumps(result, ensure_ascii=False, sort_keys=True,
                          separators=(",", ":")) + "\n").encode("utf-8")
    destination.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=".item-catalog-", suffix=".tmp",
                                     dir=destination.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(content)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)
    return {
        "status": "pass",
        "item_count": result["item_count"],
        "catalog_sha256": hashlib.sha256(content).hexdigest(),
        "source_reference": result["source_reference"],
        "output": str(destination),
        "contains_original_artwork": False,
        "presentation_metadata_source_derived": True,
        "runtime_item_effects_verified": False,
        "hk_evidence": result.get("hk_evidence"),
    }
