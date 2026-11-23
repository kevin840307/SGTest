"""Build a fail-closed SGTest Pet visual catalog from audited HK evidence.

The bridge assigns SGTest-owned DefinitionIds to source PetIDs explicitly. It
never assumes that a runtime DefinitionId is an original PetID.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

from asset_converter.content.build_pet_visual_evidence import _validate_runtime_asset
from asset_converter.json_files import write_json

SCHEMA = "sgtest.pet_visual_catalog/v1"
BRIDGE_SCHEMA = "sgtest.pet_definition_bridge/v1"


def _read_json(path: Path, label: str) -> dict:
    path = Path(path).resolve()
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"invalid {label}") from exc
    if not isinstance(value, dict):
        raise ValueError(f"invalid {label}")
    return value


def _read_bridge(path: Path) -> tuple[dict[int, int], str]:
    bridge = _read_json(path, "Pet definition bridge")
    if bridge.get("schema") != BRIDGE_SCHEMA or bridge.get("assignment") != "sgtest_owned":
        raise ValueError("unsupported Pet definition bridge")
    reference = str(bridge.get("source_reference", "")).strip()
    if not reference or len(reference) > 512:
        raise ValueError("Pet definition bridge source_reference is invalid")
    rows = bridge.get("mappings")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Pet definition bridge mappings are missing")
    result: dict[int, int] = {}
    source_ids: set[int] = set()
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Pet definition bridge row is invalid")
        definition = int(row.get("definition_id", 0))
        source_pet = int(row.get("source_pet_id", 0))
        if not 1 <= definition <= 100_000_000 or not 1 <= source_pet <= 65535:
            raise ValueError("Pet definition bridge identity is invalid")
        if definition in result:
            raise ValueError(f"duplicate SGTest Pet DefinitionId {definition}")
        if source_pet in source_ids:
            raise ValueError(f"duplicate source PetID {source_pet}")
        result[definition] = source_pet
        source_ids.add(source_pet)
    return result, reference


def _logical_view(manifest: dict, name: str, *, loop: bool) -> dict:
    views = manifest.get("logical_views", {})
    value = views.get(name) if isinstance(views, dict) else None
    if (
        not isinstance(value, dict)
        or not all(field in value for field in ("clip", "flip_h", "timeline_ticks", "loop"))
        or not isinstance(value.get("timeline_ticks"), list)
        or not value["timeline_ticks"]
        or bool(value.get("loop")) != loop
    ):
        raise ValueError(f"Pet runtime atlas has invalid logical view {name}")
    return value


def compile_pet_runtime_catalog(
    evidence_path: Path,
    bridge_path: Path,
    assets: Path,
) -> dict:
    evidence = _read_json(evidence_path, "HK Pet visual evidence")
    if (
        evidence.get("version") != 1
        or evidence.get("kind") != "hk_pet_visual_evidence"
        or evidence.get("status") != "pass"
        or evidence.get("source_identity_status") != "CONFIRMED"
    ):
        raise ValueError("HK Pet visual evidence is not confirmed")
    mappings = evidence.get("mappings")
    if not isinstance(mappings, list) or int(evidence.get("mapping_count", -1)) != len(mappings):
        raise ValueError("HK Pet visual evidence mappings are invalid")
    evidence_sha = str(evidence.get("content_sha256", ""))
    if len(evidence_sha) != 64:
        raise ValueError("HK Pet visual evidence digest is invalid")

    bridge, bridge_reference = _read_bridge(bridge_path)
    assets = Path(assets).resolve()
    if not assets.is_dir():
        raise ValueError("runtime asset root is missing")

    by_source: dict[int, list[dict]] = {}
    for row in mappings:
        if not isinstance(row, dict):
            raise ValueError("HK Pet visual evidence row is invalid")
        source_pet = int(row.get("source_pet_id", 0))
        evolution = int(row.get("evolution_id", -1))
        sprite = str(row.get("sprite", ""))
        asset_id = str(row.get("asset_id", ""))
        if (
            not 1 <= source_pet <= 65535
            or not 0 <= evolution <= 65535
            or not sprite
            or asset_id != "Enemy/" + sprite.casefold()
        ):
            raise ValueError("HK Pet visual evidence identity is invalid")
        by_source.setdefault(source_pet, []).append(row)

    definitions: dict[str, dict] = {}
    required_assets: set[str] = set()
    for definition_id, source_pet_id in sorted(bridge.items()):
        rows = by_source.get(source_pet_id, [])
        if not rows:
            raise ValueError(f"source PetID {source_pet_id} has no HK visual evidence")
        evolutions: dict[str, dict] = {}
        seen_evolutions: set[int] = set()
        for row in sorted(rows, key=lambda item: int(item["evolution_id"])):
            evolution_id = int(row["evolution_id"])
            if evolution_id in seen_evolutions:
                raise ValueError(
                    f"duplicate HK Pet evolution {source_pet_id}/{evolution_id}")
            seen_evolutions.add(evolution_id)
            sprite = str(row["sprite"])
            asset_id, relative = _validate_runtime_asset(assets, sprite)
            manifest = json.loads((assets / relative).read_text(encoding="utf-8"))
            idle_name = f"{sprite}_NS1"
            attack_name = f"{sprite}_FA1"
            idle = _logical_view(manifest, idle_name, loop=True)
            _logical_view(manifest, attack_name, loop=False)
            first_step = idle["timeline_ticks"][0]
            if not isinstance(first_step, list) or len(first_step) != 2:
                raise ValueError(f"Pet runtime atlas has invalid idle timeline {idle_name}")
            evolutions[str(evolution_id)] = {
                "asset": asset_id,
                "clip": str(idle["clip"]),
                "frame": int(first_step[0]),
                "idle_view": idle_name,
                "attack_view": attack_name,
                "source_sprite": sprite,
            }
            required_assets.add(asset_id)
        definitions[str(definition_id)] = {
            "source_pet_id": source_pet_id,
            "evolutions": evolutions,
        }

    result = {
        "schema": SCHEMA,
        "status": "pass",
        "source_identity_status": "CONFIRMED",
        "definition_assignment": "SGTEST_OWNED",
        "action_semantics_status": "INFERRED",
        "action_semantics_note": (
            "HK petcli confirms Enemy outlook identity; normalized Enemy S/A -> NS/FA "
            "is used as an interim presentation candidate until dynamic HK A/B is verified"
        ),
        "source_evidence_sha256": evidence_sha,
        "bridge_reference": bridge_reference,
        "definition_count": len(definitions),
        "evolution_count": sum(len(row["evolutions"]) for row in definitions.values()),
        "definitions": definitions,
        "required_assets": sorted(required_assets),
        "unknown_fields_promoted_to_gameplay": False,
    }
    stable = json.dumps(
        result, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode()
    result["content_sha256"] = hashlib.sha256(stable).hexdigest()
    return result


def publish_pet_runtime_catalog(
    evidence_path: Path,
    bridge_path: Path,
    assets: Path,
    output: Path,
) -> dict:
    result = compile_pet_runtime_catalog(evidence_path, bridge_path, assets)
    output = Path(output).resolve()
    for source in (Path(evidence_path).resolve(), Path(bridge_path).resolve()):
        if output == source:
            raise ValueError("Pet runtime catalog must not overwrite its input")
    write_json(output, result)
    return {
        "status": "pass",
        "output": str(output),
        "definition_count": result["definition_count"],
        "evolution_count": result["evolution_count"],
        "required_assets": len(result["required_assets"]),
        "action_semantics_status": result["action_semantics_status"],
        "content_sha256": result["content_sha256"],
    }
