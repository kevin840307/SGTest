"""Canonical logical paths for converted runtime assets.

These paths describe WHAT an asset is. Lifetime/ownership remains metadata.
No function here groups multiple source assets into one runtime asset.
"""
from __future__ import annotations

from pathlib import PurePosixPath
import re


TOP_LEVEL = (
    "maps", "monsters", "npcs", "players", "equipment", "pets",
    "mecha", "items", "effects", "ui", "audio", "data",
)


def _safe(value: str) -> PurePosixPath:
    path = PurePosixPath(value)
    if (
        not value
        or "\\" in value
        or ":" in value
        or path.is_absolute()
        or any(part in ("", ".", "..") for part in value.split("/"))
    ):
        raise ValueError("unsafe source asset path: " + value)
    return path


def _strip_race_prefix(name: str, race: str) -> str:
    prefix = race.casefold() + "-"
    return name[len(prefix):] if name.casefold().startswith(prefix) else name


def _normalize_part(name: str) -> str:
    aliases = {
        "l-arm": "left_arm",
        "r-arm": "right_arm",
    }
    return aliases.get(name.casefold(), name.casefold().replace("-", "_"))


def sprite_asset_id(group: str) -> tuple[str, str]:
    """Map one verified source SPR group to one logical runtime asset id."""
    parts = _safe(group.casefold()).parts
    category = parts[0]
    rest_parts = list(parts[1:])
    if not rest_parts:
        raise ValueError("sprite group has no asset name: " + group)
    rest = "/".join(rest_parts)

    if category == "enemy":
        return "monsters/" + rest, "monster"

    if category == "npc":
        return "npcs/" + rest, "npc"

    if category == "player":
        # The legacy tree is mostly Player/<race>/<family>/<asset>, but a few
        # verified files use Player/<race>/<asset> and Player/Cloth/<race>/<asset>.
        # Preserve identity; do not invent a gameplay meaning for those outliers.
        if len(parts) == 3:
            race, raw_name = parts[1], parts[2]
            name = _strip_race_prefix(raw_name, race)
            return f"equipment/{race}/unclassified/{_normalize_part(name)}", "player_equipment_unclassified"
        if len(parts) < 4:
            raise ValueError("unsupported player source layout: " + group)
        if parts[1] == "cloth" and len(parts) >= 4:
            # Preserve the alternate legacy tree as a distinct identity; the
            # normal Player/<race>/Cloth tree contains some of the same names.
            race, family = parts[2], "cloth_legacy"
            raw_name = "/".join(parts[3:])
        else:
            race, family = parts[1], parts[2]
            raw_name = "/".join(parts[3:])
        name = _strip_race_prefix(raw_name, race)
        if family == "standard":
            if name.startswith("hair"):
                return f"players/{race}/hair/{name}", "player_hair"
            return f"players/{race}/base/{_normalize_part(name)}", "player_base"
        return (
            f"equipment/{race}/{family}/{_normalize_part(name)}",
            "player_equipment",
        )

    # MA/MS/SR are kept as verified source families until their exact gameplay
    # meaning is proven from the original client. Do not rename them to mecha,
    # pet, mount, etc. merely because Recovered code references them together.
    if category in ("ma", "ms", "sr"):
        return f"data/legacy/{category}/{rest}", f"legacy_{category}"

    if category == "effect":
        return "effects/" + rest, "effect"

    if category == "map":
        return "maps/visuals/" + rest, "map_visual"

    if category == "interface":
        if re.fullmatch(r"ground\d*", rest):
            return "items/ground/" + rest, "item_ground"
        if rest.startswith("pet_photo/"):
            return "pets/photos/" + rest.removeprefix("pet_photo/"), "pet_photo"
        if rest.startswith("pet_icon"):
            return "pets/icons/" + rest, "pet_icon"
        if rest.startswith("itemicon"):
            return "items/icons/" + rest, "item_icon"
        return "ui/" + rest, "ui"

    if category == "item":
        return "items/" + rest, "item"

    # Fail-safe for a future source category: preserve it under data/legacy
    # rather than guessing gameplay semantics.
    return f"data/legacy/{category}/{rest}", "unverified_source_category"


def atlas_manifest(asset_id: str) -> str:
    _safe(asset_id)
    return asset_id.rstrip("/") + "/atlas.json"


def monster_manifest(sprite: str) -> str:
    name = sprite.strip().casefold()
    if not name or not name.replace("_", "").replace("-", "").isalnum():
        raise ValueError("invalid monster sprite id: " + sprite)
    return atlas_manifest("monsters/" + name)


def source_sprite_manifest(relative_source: str) -> str:
    path = _safe(relative_source.casefold())
    if path.suffix != ".spr":
        raise ValueError("source visual is not SPR: " + relative_source)
    group = path.with_suffix("").as_posix()
    asset_id, _ = sprite_asset_id(group)
    return atlas_manifest(asset_id)
