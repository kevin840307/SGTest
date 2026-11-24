#!/usr/bin/env python3
"""Sync the HK 027_Skill interface sprites into the ignored local UI cache.

The HK source tree is read-only reference material. This helper only decodes
loose .spr files and writes PNGs below client/ui/private_ie_hk2/, which is
ignored by Git.
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

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
        source = args.source / f"{name}.spr"
        if not source.is_file():
            source = fallback_source / f"{name}.spr"
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

    print("HK_SKILL_ASSETS_READY output_git_ignored=true")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
