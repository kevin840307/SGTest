#!/usr/bin/env python3
"""Decode only explicit classic SGO Interface SPR art to a private UI directory.

A local visual reference, NOT an import of client gameplay code. Native .spr
files remain untouched and must never be uploaded to the public repository.
"""
from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO))
from tools.asset_converter.source.runtime_spr import decode_spr
DEFAULT = Path(r"C:\SGO\Stargate Online IE HK2\Interface")
OUTPUT = REPO / "client/ui/private_ie_hk2"
NAMES = (
    "ACCOUNT_LOGIN", "SELECT_CHARACTER", "CREATE_CHARACTER",
    "COMMAND", "HPBAR", "BG_ITEM", "BG_CHARACTER_INFO",
    "BG_SKILL", "BG_SHOP", "BG_NPC_MESSAGE", "BG_QUEST_LIST",
    "BG_RIGHTBOX", "MINIMAP_S", "MINIMAP_L", "HOTKEY_LY",
    "HOTKEY_YL_B", "HOTKEY_SY", "BG_TEAM", "BG_CLUB",
    "BG_MESSAGE_INGAME", "BG_SYSTEM",
)

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT)
    args = parser.parse_args()
    if not args.source.is_dir():
        parser.error("Original client Interface directory is missing")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    complete = 0
    for name in NAMES:
        source = args.source / f"{name}.spr"
        if not source.is_file():
            print("CLASSIC_SOURCE_NOT_FOUND", name)
            continue
        frames = decode_spr(source.read_bytes())
        if len(frames) != 1:
            print("CLASSIC_SOURCE_NOT_SINGLE_FRAME", name, len(frames))
            continue
        target = OUTPUT / f"{name}.png"
        if target.is_file():
            # Write only on change to avoid invalidating Godot imports.
            import io
            output = io.BytesIO()
            frames[0].image.save(output,format="PNG")
            raw = output.getvalue()
            if hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(raw).digest():
                print("CLASSIC_UNCHANGED", name, frames[0].image.size)
                complete += 1
                continue
            target.write_bytes(raw)
        else:
            frames[0].image.save(target)
        print("CLASSIC_PRIVATE_ART", name, frames[0].image.size)
        complete += 1
    print("CLASSIC_ASSETS_READY",complete, "source_spr_unchanged=true git_private=true")
    return 0 if complete >= 12 else 1

if __name__ == "__main__":
    raise SystemExit(main())
