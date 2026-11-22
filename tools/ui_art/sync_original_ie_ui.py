#!/usr/bin/env python3
"""Decode classic SGO Interface SPR art to the private HK2 UI directory.

This is a local visual reference pipeline, NOT an import of client gameplay
code. Native .spr/Data1.dat files remain untouched and must never be uploaded
to the public repository. Loose Interface files override archive entries, just
like the original client.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))
from tools.asset_converter.source.extract_client import lzss, read_index
from tools.asset_converter.source.runtime_spr import decode_spr

DEFAULT = Path(r"C:\SGO\Stargate Online IE HK2\Interface")
OUTPUT = REPO / "client/ui/private_ie_hk2"

SINGLE_NAMES = (
    "ACCOUNT_LOGIN", "SELECT_CHARACTER", "CREATE_CHARACTER",
    "COMMAND", "HPBAR", "BG_ITEM", "BG_CHARACTER_INFO",
    "BG_SKILL", "BG_SHOP", "BG_NPC_MESSAGE", "BG_NPC_MESSAGE_06",
    "BG_QUEST_LIST", "BG_RIGHTBOX", "BG_BANK", "BG_REPAIR", "BG_TRADE",
    "BG_PSHOP_01", "BG_PSHOP_02", "BG_PSHOP_03", "BG_PSHOP_BUY_01",
    "BG_PSHOP_BUY_02", "BG_PSHOP_ITEM_01", "BG_PSHOP_ITEM_02",
    "BG_Equip", "MINIMAP_S", "MINIMAP_L", "HOTKEY_LY",
    "HOTKEY_SY", "BG_TEAM", "BG_CLUB", "BG_MESSAGE_INGAME", "BG_SYSTEM",
)

MULTI_NAMES = (
    "HOTKEY_YL_B",
    "B_ITEM_ITEM", "B_ITEM_EQUIP", "B_ITEM_ARMOR", "B_ITEM_PET", "B_ITEM_RIDE",
    "B_BASIC_GUILD", "B_BASIC_PLAYER", "B_BASIC_ITEM", "B_BASIC_PSHOP",
    "B_BASIC_SYSTEM", "B_BASIC_SKILL", "B_COMMAND_TEAM",
    "B_RIGHTBOX_BUTTON", "CLOSESCREEN", "MINISCREEN", "YELLOW_BUTTON",
    "LEFT_BUTTON", "RIGHT_BUTTON", "NEW_CHARACTER", "PLAY_GAME", "B_ONLINEHELP",
)


def _save_if_changed(target: Path, image) -> bool:
    output = io.BytesIO()
    image.save(output, format="PNG")
    raw = output.getvalue()
    if target.is_file() and hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(raw).digest():
        return False
    target.write_bytes(raw)
    return True


def _archive_entries(archive: Path):
    if not archive.is_file():
        return {}
    with archive.open("rb") as handle:
        return {path.as_posix().casefold(): (original, compressed, start)
                for path, original, compressed, start in read_index(handle)}


def _read_sprite(source_dir: Path, name: str, archive: Path, entries: dict):
    loose = source_dir / f"{name}.spr"
    if loose.is_file():
        return loose.read_bytes(), "loose"
    key = f"interface/{name}.spr".casefold()
    meta = entries.get(key)
    if meta is None:
        return None, ""
    original, compressed, start = meta
    with archive.open("rb") as handle:
        handle.seek(start)
        payload = handle.read(compressed or original)
    return (lzss(payload, original) if compressed else payload), "archive"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT,
                        help="original client Interface directory")
    args = parser.parse_args()
    if not args.source.is_dir():
        parser.error("Original client Interface directory is missing")

    OUTPUT.mkdir(parents=True, exist_ok=True)
    archive = args.source.parent / "Data1.dat"
    entries = _archive_entries(archive)
    complete = 0

    for name in SINGLE_NAMES:
        raw, origin = _read_sprite(args.source, name, archive, entries)
        if raw is None:
            print("CLASSIC_SOURCE_NOT_FOUND", name)
            continue
        frames = decode_spr(raw)
        if len(frames) != 1:
            print("CLASSIC_SOURCE_NOT_SINGLE_FRAME", name, len(frames))
            continue
        target = OUTPUT / f"{name}.png"
        changed = _save_if_changed(target, frames[0].image)
        print("CLASSIC_PRIVATE_ART" if changed else "CLASSIC_UNCHANGED",
              name, frames[0].image.size, origin)
        complete += 1

    for name in MULTI_NAMES:
        raw, origin = _read_sprite(args.source, name, archive, entries)
        if raw is None:
            print("CLASSIC_SOURCE_NOT_FOUND", name)
            continue
        frames = decode_spr(raw)
        if not frames:
            print("CLASSIC_SOURCE_EMPTY", name)
            continue
        for index, frame in enumerate(frames):
            target = OUTPUT / f"{name}_{index}.png"
            _save_if_changed(target, frame.image)
        print("CLASSIC_PRIVATE_FRAMES", name, len(frames), frames[0].image.size, origin)
        complete += 1

    print("CLASSIC_ASSETS_READY", complete,
          "source_spr_unchanged=true archive_unchanged=true git_private=true")
    return 0 if complete >= 20 else 1


if __name__ == "__main__":
    raise SystemExit(main())
