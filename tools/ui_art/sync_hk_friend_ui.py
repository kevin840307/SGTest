#!/usr/bin/env python3
"""Sync source-backed HK 020/043 friend-message sprites into the ignored UI cache."""

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

EXPECTED = {
    "B_GET_MAIL": (1, 13, 11, 0, -2),
    "BG_FRIENDMSG_BOX": (1, 256, 175, 0, 0),
}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    args = parser.parse_args()
    fallback = args.source.parent / "output" / "Interface"
    if not args.source.is_dir() and not fallback.is_dir():
        parser.error(f"HK Interface directories not found: {args.source} / {fallback}")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, expected in EXPECTED.items():
        source = args.source / f"{name}.spr"
        if not source.is_file():
            source = fallback / f"{name}.spr"
        if not source.is_file():
            parser.error(f"HK friend UI sprite not found: {name}.spr")
        frames = decode_spr(source.read_bytes())
        if len(frames) != expected[0]:
            parser.error(f"unexpected HK frame count for {name}: {len(frames)} != {expected[0]}")
        frame = frames[0]
        actual = (
            len(frames),
            frame.image.size[0],
            frame.image.size[1],
            frame.origin_x,
            frame.origin_y,
        )
        if actual != expected:
            parser.error(f"unexpected HK geometry for {name}: {actual} != {expected}")
        target = OUTPUT / (f"{name}_0.png" if name == "B_GET_MAIL" else f"{name}.png")
        frame.image.save(target, format="PNG")
        print(
            "HK_FRIEND_SPR",
            name,
            f"size={actual[1]}x{actual[2]}",
            f"origin=({actual[3]},{actual[4]})",
            f"sha256={_sha256(source)}",
            "source_unchanged=true",
        )

    print("HK_FRIEND_ASSETS_READY output_git_ignored=true")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
