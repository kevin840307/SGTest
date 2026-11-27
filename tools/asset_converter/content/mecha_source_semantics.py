"""Normalize source-backed MA/MS presentation semantics without inventing gameplay.

The original client supplies MS.ACT + MS_CharDrawOrder.txt and one MAxx.ACT per
MA controller. Runtime atlases already preserve the matching SPR clips. This
module joins those sources into logical views only when every required frame
and facing can be resolved from those files.
"""
from __future__ import annotations

from pathlib import Path
import re

from ..source.inspect_act import parse_act

_CLIP = re.compile(r"^(.+)_([A-Z]{2})([1-8])$", re.I)
_MA_GROUP = re.compile(r"^[agrby]_ma(\d{2})$", re.I)
_MS_SLOT = {
    "head": 0,
    "hair": 1,
    "bonnet": 2,
    "l-arm": 5,
    "r-arm": 7,
    "body": 9,
    "leg": 12,
}


def _read_draw_order(path: Path) -> dict[str, list[str]]:
    rows: dict[str, list[str]] = {}
    for raw in path.read_text(encoding="ascii").splitlines():
        if not raw.strip():
            continue
        key, value = raw.split(":", 1)
        key = key.strip().upper()
        if key in rows:
            raise ValueError("duplicate MS draw-order key: " + key)
        rows[key] = [token.strip() for token in value.split(">")]
    return rows


def _draw_slots(tokens: list[str]) -> list[int]:
    slots: list[int] = []
    for token in tokens:
        key = token.casefold()
        if key not in _MS_SLOT:
            raise ValueError("unverified MS draw-order token: " + token)
        slot = _MS_SLOT[key]
        if slot in slots:
            raise ValueError("duplicate MS draw-order slot")
        slots.append(slot)
    return slots


def _logical_direction(facing: int, available: set[int]) -> tuple[int, bool]:
    if facing in available:
        return facing, False
    # Original character draw logic mirrors the missing western source
    # directions 5/6/7. Some mecha attacks also omit 4/8 and therefore use
    # their adjacent mirrored source directions 3/1, matching Recovered2.
    mirror = {4: 3, 5: 3, 6: 2, 7: 1, 8: 1}
    source = mirror.get(facing, 0)
    if source and source in available:
        return source, True
    raise ValueError(f"no source-backed mecha direction for facing {facing}")


def _controller(group: str, source: Path) -> tuple[Path, Path | None]:
    parts = group.casefold().split("/")
    if len(parts) < 3:
        raise ValueError("invalid mecha source group")
    if parts[0] == "ms":
        return source / "ms" / "ms.act", source / "ms" / "ms_chardraworder.txt"
    if parts[0] == "ma":
        match = _MA_GROUP.fullmatch(parts[-1])
        if match is None:
            raise ValueError("MA source group does not identify a controller")
        return source / "ma" / f"ma{match.group(1)}.act", None
    raise ValueError("not a mecha source group")


def logical_views_for_group(group: str, clips: dict, source: Path) -> tuple[dict, dict]:
    """Return (logical_views, semantics) for a single MA/MS sprite group.

    Groups outside MA/MS return empty views and an empty semantics dictionary.
    The caller may retain its ordinary source-clips-only behavior for them.
    """
    lowered = group.casefold()
    if not (lowered.startswith("ms/") or lowered.startswith("ma/")):
        return {}, {}

    parsed: dict[str, dict[int, str]] = {}
    prefix = ""
    for clip in clips:
        match = _CLIP.fullmatch(str(clip))
        if match is None:
            continue
        current_prefix = match.group(1).upper()
        if prefix and current_prefix != prefix:
            raise ValueError("mecha atlas has multiple source prefixes")
        prefix = current_prefix
        action = match.group(2).upper()
        facing = int(match.group(3))
        parsed.setdefault(action, {})[facing] = str(clip)
    if not prefix or not parsed:
        raise ValueError("mecha atlas has no resolvable action clips")

    act_path, draw_path = _controller(group, source)
    if not act_path.is_file():
        raise ValueError("missing original mecha ACT: " + str(act_path))
    actions = {row["key"].upper(): row for row in parse_act(act_path.read_bytes())}
    draw = _read_draw_order(draw_path) if draw_path is not None else {}

    views: dict[str, dict] = {}
    normalized_actions: list[str] = []
    for action in sorted(parsed):
        if action not in actions:
            continue
        available = set(parsed[action])
        source_action = actions[action]
        timeline = [
            [int(step["index"]), max(1, int(step["raw_time"]) * 40 // 25)]
            for step in source_action["frames"]
        ]
        if not timeline:
            raise ValueError("empty mecha ACT timeline: " + action)
        static_head = lowered.startswith("ms/head/") and all(
            len(clips[parsed[action][source_facing]]) == 1
            for source_facing in available
        )
        for facing in range(1, 9):
            source_facing, flip = _logical_direction(facing,available)
            clip = parsed[action][source_facing]
            view_timeline = [[0,1]] if static_head else timeline
            if any(index < 0 or index >= len(clips[clip])
                   for index, _ in view_timeline):
                raise ValueError("mecha ACT frame exceeds source clip: " + clip)
            draw_key = f"{action}{source_facing}"
            layers = None
            slots = None
            if draw_path is not None:
                if draw_key not in draw:
                    raise ValueError("missing original MS draw-order key: " + draw_key)
                layers = draw[draw_key]
                slots = _draw_slots(layers)
            elif lowered.startswith("ma/standard/"):
                # MA is one source body sprite; keep it in the body layer.
                layers = ["Body"]
                slots = [9]
            views[f"{prefix}_{action}{facing}"] = {
                "clip": clip,
                "flip_h": flip,
                "timeline_ticks": view_timeline,
                "loop": True if static_head else bool(source_action["loops"]),
                "act_key": action,
                "draw_order_key": draw_key if layers is not None else None,
                "draw_layers": layers,
                "draw_slots": slots,
                "source_facing": source_facing,
                "static_source_frame": static_head,
            }
        normalized_actions.append(action)

    if not views:
        raise ValueError("mecha atlas has no ACT-backed actions")
    semantics = {
        "status": "normalized_mecha_source",
        "timing_verified": True,
        "direction_mapping_verified": True,
        "controller": act_path.relative_to(source).as_posix(),
        "draw_order": draw_path.relative_to(source).as_posix() if draw_path is not None else None,
        "verified_behaviors": normalized_actions,
        "static_single_frame_layers": lowered.startswith("ms/head/"),
    }
    return views, semantics
