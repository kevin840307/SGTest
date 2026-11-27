#!/usr/bin/env python3
"""Sync the HK skill interface presentation into the ignored local UI cache.

The HK source tree is read-only reference material. This helper decodes only
source-backed presentation sprites and writes PNG/JSON files below
client/ui/private_ie_hk2/, which is ignored by Git. It never emits gameplay
skill rules.
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from tools.asset_converter.source.hk_skill_icons import export_private_skill_icons
from tools.asset_converter.source.hk_skill_names import export_private_skill_labels
from tools.asset_converter.source.runtime_spr import decode_spr

DEFAULT_SOURCE = Path(r"C:\SGO\Stargate Online IE HK\Interface")
OUTPUT = REPO / "client/ui/private_ie_hk2"

SINGLE = ("BG_SKILL_S",)
MULTI = ("CLOSESCREEN", "B_ONLINEHELP", "SCROLLUP_BUTTON", "SCROLLDOWN_BUTTON")


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_png(target: Path, image) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target, format="PNG")


def _decode(source: Path):
    raw = source.read_bytes()
    frames = decode_spr(raw)
    if not frames:
        raise ValueError(f"no frames decoded: {source}")
    return frames


def _source_file(interface: Path, fallback: Path, name: str, suffix: str) -> Path:
    source = interface / f"{name}.{suffix}"
    if not source.is_file():
        source = fallback / f"{name}.{suffix}"
    return source


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    args = parser.parse_args()

    if not args.source.is_dir():
        parser.error(f"HK Interface directory not found: {args.source}")
    OUTPUT.mkdir(parents=True, exist_ok=True)

    expected = {
        "BG_SKILL_S": 1,
        "CLOSESCREEN": 4,
        "B_ONLINEHELP": 4,
        "SCROLLUP_BUTTON": 4,
        "SCROLLDOWN_BUTTON": 4,
    }
    fallback_source = args.source.parent / "output" / "Interface"
    for name in (*SINGLE, *MULTI):
        source = _source_file(args.source, fallback_source, name, "spr")
        if not source.is_file():
            parser.error(
                f"HK skill sprite not found in {args.source} or {fallback_source}: "
                f"{name}.spr"
            )
        frames = _decode(source)
        if len(frames) != expected[name]:
            parser.error(
                f"unexpected HK frame count for {name}: "
                f"{len(frames)} != {expected[name]}"
            )
        if name in SINGLE:
            _write_png(OUTPUT / f"{name}.png", frames[0].image)
        else:
            for index, frame in enumerate(frames):
                _write_png(OUTPUT / f"{name}_{index}.png", frame.image)
        first_size = frames[0].image.size
        print(
            "HK_SKILL_SPR",
            name,
            f"frames={len(frames)}",
            f"first={first_size[0]}x{first_size[1]}",
            f"sha256={_sha256(source)}",
            "source_unchanged=true",
        )

    # Old CHotKeyItf::OnLoadHotKey renders SKILLDATA+7 from this sprite.
    # Old InitSkillLib maps that byte exactly to skill.dat CSV field 14.
    icon_source = _source_file(args.source, fallback_source, "SKILL_ICON", "spr")
    if not icon_source.is_file():
        parser.error(
            f"HK skill icon sprite not found in {args.source} or {fallback_source}: "
            "SKILL_ICON.spr"
        )
    icon_frames = _decode(icon_source)
    if len(icon_frames) != 114:
        parser.error(f"unexpected HK SKILL_ICON frame count: {len(icon_frames)} != 114")
    for index, frame in enumerate(icon_frames):
        if frame.image.size != (24, 24) or (frame.origin_x, frame.origin_y) != (0, 0):
            parser.error(
                f"unexpected HK SKILL_ICON frame {index}: "
                f"size={frame.image.size} origin=({frame.origin_x},{frame.origin_y})"
            )
        _write_png(OUTPUT / f"SKILL_ICON_{index:03d}.png", frame.image)
    print(
        "HK_SKILL_ICON_SPR",
        f"frames={len(icon_frames)}",
        "size=24x24",
        f"sha256={_sha256(icon_source)}",
        "source_unchanged=true",
    )

    skill_dat = _source_file(args.source, fallback_source, "skill", "dat")
    if not skill_dat.is_file():
        # Preserve the original filename's common uppercase first letter too.
        skill_dat = _source_file(args.source, fallback_source, "Skill", "dat")
    if not skill_dat.is_file():
        parser.error(
            f"HK skill.dat not found in {args.source} or {fallback_source}"
        )
    label_count, _ = export_private_skill_labels(
        skill_dat, OUTPUT / "HK_SKILL_LABELS.json")
    icon_count, _ = export_private_skill_icons(
        skill_dat, OUTPUT / "HK_SKILL_ICONS.json")
    print(
        "HK_SKILL_PRESENTATION_MANIFESTS",
        f"labels={label_count}",
        f"icons={icon_count}",
        f"skill_dat_sha256={_sha256(skill_dat)}",
        "source_unchanged=true",
    )

    print("HK_SKILL_ASSETS_READY output_git_ignored=true")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
