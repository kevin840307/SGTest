#!/usr/bin/env python3
"""Sync source-backed HK combat feedback sprites into the ignored local UI cache.

The HK source tree is read-only reference material. This helper decodes only the
known single-frame Map/Animation feedback sprites and writes PNGs below
client/ui/private_ie_hk2/, which is ignored by Git.
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))

from tools.asset_converter.source.runtime_spr import decode_spr

DEFAULT_SOURCE = Path(r"C:\SGO\Stargate Online IE HK\Map\Animation")
OUTPUT = REPO / "client/ui/private_ie_hk2"

EXPECTED = {
    "MISS": (45, 35, -7, 0),
    "MISS_BLUE": (45, 35, -7, 0),
    "MISS_ORANGE": (45, 35, -7, 0),
    "MISS_PURPLE": (45, 35, -7, 0),
    "GUARD": (87, 46, -1, -2),
}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    args = parser.parse_args()
    if not args.source.is_dir():
        parser.error(f"HK Map/Animation directory not found: {args.source}")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    fallback = args.source.parent.parent / "output" / "MAP" / "Animation"
    for name, geometry in EXPECTED.items():
        source = args.source / f"{name}.spr"
        if not source.is_file():
            source = fallback / f"{name}.spr"
        if not source.is_file():
            parser.error(f"HK combat sprite not found: {name}.spr")
        frames = decode_spr(source.read_bytes())
        if len(frames) != 1:
            parser.error(f"unexpected HK frame count for {name}: {len(frames)} != 1")
        frame = frames[0]
        actual = (frame.image.size[0], frame.image.size[1], frame.origin_x, frame.origin_y)
        if actual != geometry:
            parser.error(f"unexpected HK geometry for {name}: {actual} != {geometry}")
        frame.image.save(OUTPUT / f"{name}.png", format="PNG")
        print(
            "HK_COMBAT_SPR", name,
            f"size={actual[0]}x{actual[1]}",
            f"origin=({actual[2]},{actual[3]})",
            f"sha256={_sha256(source)}",
            "source_unchanged=true",
        )

    print("HK_COMBAT_ASSETS_READY output_git_ignored=true")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
