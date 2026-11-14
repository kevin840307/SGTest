"""Compile sgtest.animation/v2 semantics into runtime manifest overlays."""
from __future__ import annotations

import hashlib
import json

if __package__:
    from .animation_definition import validate_definition
else:
    from animation_definition import validate_definition


def logical_views(definition: dict) -> dict:
    validate_definition(definition)
    prefixes = {view["clip"].rsplit("_", 1)[0]
                for behavior in definition["behaviors"].values()
                for view in behavior["facings"].values()}
    if len(prefixes) != 1:
        raise ValueError("normalized definition has multiple clip prefixes")
    prefix = next(iter(prefixes))
    result = {}
    for behavior_code, behavior in sorted(definition["behaviors"].items()):
        timeline = [[int(step["frame_index"]), int(step["default_ticks"])]
                    for step in behavior["timeline"]]
        if not timeline or any(index < 0 or ticks < 1 for index, ticks in timeline):
            raise ValueError(f"{behavior_code}: invalid normalized timeline")
        for facing, view in sorted(behavior["facings"].items(), key=lambda item: int(item[0])):
            requested = f"{prefix}_{behavior_code}{facing}"
            result[requested] = {
                "clip": view["clip"],
                "flip_h": bool(view["flip_h"]),
                "timeline_ticks": timeline,
                "loop": bool(behavior["loop"]),
                "act_key": behavior["act_key"],
                "draw_order_key": view.get("draw_order_key"),
                "draw_layers": view.get("draw_layers"),
                "draw_slots": view.get("draw_slots"),
            }
    return result


def apply_overlay(definition: dict, manifest: dict) -> dict:
    """Return a copied runtime manifest with fail-closed logical semantics."""
    validate_definition(definition)
    if int(definition["version"]) != 2:
        raise ValueError("runtime overlay requires sgtest.animation/v2")
    if not isinstance(manifest.get("clips"), dict) or not isinstance(manifest.get("frames"), list):
        raise ValueError("runtime manifest is missing clips/frames")
    views = logical_views(definition)
    for requested, view in views.items():
        source = view["clip"]
        if source not in manifest["clips"]:
            raise ValueError(f"{requested}: normalized source clip {source} missing from runtime atlas")
        runtime_frames = manifest["clips"][source]
        normalized_frames = definition["clips"][source]["frames"]
        if len(runtime_frames) != len(normalized_frames):
            raise ValueError(f"{source}: runtime/normalized frame count mismatch")
        for index, _ticks in view["timeline_ticks"]:
            if index >= len(runtime_frames):
                raise ValueError(f"{requested}: normalized timeline exceeds runtime source clip")
    result = json.loads(json.dumps(manifest))
    result["logical_views"] = views
    result["normalized_schema_version"] = 2
    result["normalized_definition_sha256"] = definition["content_sha256"]
    result["semantic_source"] = "sgtest.animation/v2"
    semantic_payload = {
        "definition": definition["content_sha256"],
        "logical_views": views,
    }
    result["semantic_sha256"] = hashlib.sha256(
        json.dumps(semantic_payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    ).hexdigest()
    return result
