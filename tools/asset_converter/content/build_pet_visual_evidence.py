"""Compile non-textual HK PetID/EvolID -> world visual evidence.

This keeps original prose out of runtime output and deliberately preserves HK
source identity separately from SGTest DefinitionId.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from asset_converter.content.animation_definition import compile_definition
from asset_converter.content.asset_paths import monster_manifest
from asset_converter.json_files import write_json
from asset_converter.source.hk_petcli_itd import read_hk_petcli


def _sprite_from_outlook(outlook: str) -> str:
    prefix, separator, name = outlook.partition("\\")
    if separator != "\\" or prefix.casefold() != "enemy" or not name:
        raise ValueError("pet outlook is not an Enemy source path")
    sprite = name.upper()
    if not sprite.isascii() or not sprite.replace("_", "").replace("-", "").isalnum():
        raise ValueError("pet outlook has unsafe sprite identity")
    return sprite


def _validate_runtime_asset(assets: Path, sprite: str) -> tuple[str, str]:
    relative = monster_manifest(sprite)
    path = assets / relative
    if not path.is_file():
        raise ValueError(f"missing runtime Pet Enemy atlas: {relative}")
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid runtime Pet Enemy atlas: {relative}") from exc
    expected_id = "Enemy/" + sprite.casefold()
    if manifest.get("version") != 9 or manifest.get("asset_id") != expected_id:
        raise ValueError(f"wrong runtime Pet Enemy atlas identity: {relative}")
    semantics = manifest.get("animation_semantics", {})
    full_verified = (
        isinstance(semantics, dict)
        and bool(semantics.get("timing_verified"))
        and bool(semantics.get("direction_mapping_verified"))
    )
    partial_verified = (
        isinstance(semantics, dict)
        and semantics.get("status") == "normalized_enemy_partial"
        and {"NS", "FA"}.issubset(set(semantics.get("verified_behaviors", [])))
    )
    if not full_verified and not partial_verified:
        raise ValueError(f"Pet Enemy atlas lacks verified NS/FA ACT semantics: {relative}")
    views = manifest.get("logical_views", {})
    if not isinstance(views, dict):
        raise ValueError(f"Pet Enemy atlas lacks logical views: {relative}")
    for action in ("NS", "FA"):
        for facing in range(1, 9):
            key = f"{sprite}_{action}{facing}"
            view = views.get(key)
            if not isinstance(view, dict) or not all(
                field in view for field in ("clip", "flip_h", "timeline_ticks", "loop")
            ):
                raise ValueError(f"Pet Enemy atlas missing logical view {key}")
    return expected_id, relative


def _validate_source_animation(source: Path, sprite: str) -> str:
    enemy = source / "enemy"
    stem = sprite.casefold()
    act = enemy / f"{stem}.act"
    sprites = sorted(enemy.glob(f"{stem}_*.spr"))
    if not act.is_file() or not sprites:
        raise ValueError(f"missing extracted Pet Enemy SPR/ACT: {sprite}")
    definition = compile_definition(
        "Enemy/" + stem, sprites, source, "monster", act,
        requested_behaviors=["NS", "FA"])
    if set(definition.get("behaviors", {})) != {"NS", "FA"}:
        raise ValueError(f"Pet Enemy source lacks NS/FA semantics: {sprite}")
    for action in ("NS", "FA"):
        behavior = definition["behaviors"][action]
        if set(behavior.get("facings", {})) != {str(i) for i in range(1, 9)}:
            raise ValueError(f"Pet Enemy source lacks eight facings: {sprite}/{action}")
        if not behavior.get("timeline"):
            raise ValueError(f"Pet Enemy source has empty ACT timeline: {sprite}/{action}")
    return str(definition["content_sha256"])


def compile_pet_visual_evidence(
    hk_petcli_itd: Path,
    assets: Path | None = None,
    *,
    extracted_source: Path | None = None,
    source_reference: str = "HK SGO Item/petcli.ITD",
) -> dict:
    if not source_reference or len(source_reference) > 512:
        raise ValueError("pet visual source_reference is invalid")
    families, audit = read_hk_petcli(Path(hk_petcli_itd))
    asset_root = Path(assets).resolve() if assets is not None else None
    if asset_root is not None and not asset_root.is_dir():
        raise ValueError("runtime asset root is missing")
    source_root = Path(extracted_source).resolve() if extracted_source is not None else None
    if source_root is not None and not source_root.is_dir():
        raise ValueError("extracted HK source root is missing")

    mappings: list[dict] = []
    validated_assets: set[str] = set()
    asset_cache: dict[str, tuple[str, str]] = {}
    source_animation_cache: dict[str, str] = {}
    for family in families:
        for evolution in family.evolutions:
            sprite = _sprite_from_outlook(evolution.outlook)
            asset_id = "Enemy/" + sprite.casefold()
            atlas = monster_manifest(sprite)
            if asset_root is not None:
                if sprite not in asset_cache:
                    asset_cache[sprite] = _validate_runtime_asset(asset_root, sprite)
                asset_id, atlas = asset_cache[sprite]
                validated_assets.add(asset_id)
            if source_root is not None and sprite not in source_animation_cache:
                source_animation_cache[sprite] = _validate_source_animation(
                    source_root, sprite)
            mappings.append({
                "source_pet_id": family.source_pet_id,
                "evolution_id": evolution.evolution_id,
                "sprite": sprite,
                "asset_id": asset_id,
                "atlas": atlas,
                "source_unknown_u16_a": evolution.unknown_u16_a,
                "source_unknown_u16_b": evolution.unknown_u16_b,
                "source_variant_byte": evolution.variant_byte,
            })

    mappings.sort(key=lambda row: (row["source_pet_id"], row["evolution_id"]))
    result = {
        "version": 1,
        "kind": "hk_pet_visual_evidence",
        "status": "pass",
        "source_reference": source_reference,
        "source_audit": audit,
        "mapping_count": len(mappings),
        "mappings": mappings,
        "source_identity_status": "CONFIRMED",
        "runtime_atlas_validation": asset_root is not None,
        "validated_runtime_assets": len(validated_assets),
        "source_animation_validation": source_root is not None,
        "validated_source_animations": {
            "count": len(source_animation_cache),
            "content_sha256": dict(sorted(source_animation_cache.items())),
        },
        "runtime_definition_mapping_required": True,
        "sgtest_definition_ids_preserved": True,
        "action_semantics": {
            "idle_candidate": "NS",
            "attack_candidate": "FA",
            "status": "INFERRED",
            "reason": "petcli outlook selects Enemy assets; normalized Enemy ACT maps S->NS and A->FA",
        },
        "unknown_fields_promoted_to_gameplay": False,
    }
    stable = json.dumps(
        result, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
    result["content_sha256"] = hashlib.sha256(stable).hexdigest()
    return result


def publish_pet_visual_evidence(
    hk_petcli_itd: Path,
    output: Path,
    assets: Path | None = None,
    *,
    extracted_source: Path | None = None,
    source_reference: str = "HK SGO Item/petcli.ITD",
) -> dict:
    result = compile_pet_visual_evidence(
        hk_petcli_itd, assets, extracted_source=extracted_source,
        source_reference=source_reference)
    output = Path(output).resolve()
    source = Path(hk_petcli_itd).resolve()
    if output == source:
        raise ValueError("pet visual evidence output must not overwrite original source")
    write_json(output, result)
    return {
        "status": "pass",
        "output": str(output),
        "mapping_count": result["mapping_count"],
        "family_count": result["source_audit"]["family_count"],
        "evolution_count": result["source_audit"]["evolution_count"],
        "validated_runtime_assets": result["validated_runtime_assets"],
        "runtime_atlas_validation": result["runtime_atlas_validation"],
        "validated_source_animations": result["validated_source_animations"]["count"],
        "source_animation_validation": result["source_animation_validation"],
        "content_sha256": result["content_sha256"],
    }
