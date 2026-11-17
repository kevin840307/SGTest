"""Compile source-derived Godot item .tres metadata into private client names.

This is *presentation only*: names, source category and icon IDs are not
server-authoritative item effects. Never publish private source content.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

CATEGORIES = frozenset((
    "booster", "equip", "general", "horse", "horsebattery",
    "horseequip", "recovery", "weapon",
))
NUMBER = re.compile(r"^([0-9]+)$")
FIELD = re.compile(r'^([a-z_]+)\s*=\s*(.+)$')
MAX_NAME = 128
MAX_ITEM_FILE = 128 * 1024
MAX_ITEMS = 20000


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
        if match and match[1] in ("id", "item_name", "icon_id", "item_type_category"):
            if match[1] in fields:
                raise ValueError(f"duplicate {match[1]} in {relative}")
            fields[match[1]] = match[2]
    return fields


def _string_field(fields: dict, key: str, relative: str) -> str:
    value = fields.get(key)
    if value is None:
        raise ValueError(f"missing {key} in {relative}")
    try:
        parsed = json.loads(value)
    except (ValueError, TypeError) as exc:
        raise ValueError(f"malformed string {key} in {relative}") from exc
    if not isinstance(parsed, str) or not parsed.strip() or len(parsed) > MAX_NAME or any(ord(c) < 32 or ord(c) == 127 for c in parsed):
        raise ValueError(f"invalid {key} in {relative}")
    return parsed.strip()


def _integer(fields: dict, key: str, relative: str, *, default=None) -> int:
    value = fields.get(key)
    if value is None and default is not None:
        return default
    if value is None or not NUMBER.fullmatch(value):
        raise ValueError(f"invalid {key} in {relative}")
    return int(value)


def compile_item_catalog(items_root: Path, *, source_reference: str,
                         only_ids: set[int] | None = None) -> dict:
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
            if not NUMBER.fullmatch(path.stem):
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
            entries.append({
                "item_id": item_id, "name": name, "category": category,
                "icon_id": icon_id, "source_path": relative,
                "source_sha256": hashlib.sha256(raw).hexdigest(),
            })
            if len(entries) > MAX_ITEMS:
                raise ValueError("catalog exceeds bounded item count")
    entries.sort(key=lambda item: item["item_id"])
    if not entries:
        raise ValueError("source contains no resolvable selected item definitions")
    if only_ids is not None and set(x["item_id"] for x in entries) != only_ids:
        raise ValueError("requested item ID has no source definition")
    return {
        "kind": "source_derived_item_catalog", "version": 1,
        "source_reference": source_reference.strip(),
        "item_count": len(entries), "items": entries,
    }


def publish_item_catalog(items_root: Path, output: Path, *, source_reference: str,
                         only_ids: set[int] | None = None) -> dict:
    source = Path(items_root).resolve()
    if (source / "Items").is_dir():
        source /= "Items"
    destination = Path(output).resolve()
    if destination == source or destination.is_relative_to(source):
        raise ValueError("output may not overwrite source Items")
    result = compile_item_catalog(source, source_reference=source_reference,
                                  only_ids=only_ids)
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
        "runtime_item_effects_verified": False,
    }
