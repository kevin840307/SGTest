#!/usr/bin/env python3
"""Prepare classic SGO Interface art in the private HK UI directory.

This is a local visual reference pipeline, NOT an import of client gameplay
code. Native .spr/Data1.dat files remain untouched and must never be uploaded
to the public repository. Prefer local HK PNG exports, then the HK archive,
then loose Interface sprites.
"""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))
from tools.asset_converter.source.extract_client import lzss, read_index
from tools.asset_converter.source.runtime_spr import decode_spr
from tools.ui_art.render_hk_login_captions import render_hk_login_captions
from tools.ui_art.render_hk_login_input_font import write_hk_login_input_font
from tools.ui_art.render_hk_shared_ui_font import write_hk_shared_ui_font

DEFAULT = Path(r"C:\SGO\Stargate Online IE HK\Interface")
OUTPUT = REPO / "client/ui/private_ie_hk2"
LOGIN_OUTPUT = "ACCOUNT_LOGIN_HK.png"
LOGIN_CAPTIONS_OUTPUT = "ACCOUNT_LOGIN_HK_CAPTIONS.png"
PNG_EXPORTS = ("Data1_PNG2", "Data1_PNG")

SINGLE_NAMES = (
    "ACCOUNT_LOGIN", "SELECT_CHARACTER", "CREATE_CHARACTER",
    "LOGIN_CHANGE_PASSWORD", "LOGIN_PASSWORD",
    "COMMAND", "HPBAR", "BG_ITEM", "BG_CHARACTER_INFO",
    "BG_SKILL", "BG_SHOP", "BG_MESSAGE", "BG_NPC_MESSAGE", "BG_NPC_MESSAGE_06",
    "BG_QUEST_LIST", "BG_RIGHTBOX", "BG_BANK", "BG_REPAIR", "BG_TRADE",
    "BG_PSHOP_01", "BG_PSHOP_02", "BG_PSHOP_03", "BG_PSHOP_BUY_01",
    "BG_PSHOP_BUY_02", "BG_PSHOP_ITEM_01", "BG_PSHOP_ITEM_02",
    "BG_Equip", "MINIMAP_S", "MINIMAP_L", "BG_MINIMAP_POINT", "HOTKEY_LY",
    "HOTKEY_SY", "BG_TEAM", "BG_CLUB", "BG_MESSAGE_INGAME", "BG_SYSTEM",
    "CHANNEL", "BG_CHANNEL_BIG", "BG_FRIENDLIST_SMALL", "BG_FRIENDLIST_BIG",
    "BG_TALKBUBBLE", "BG_EMOTION", "BG_DRIVER", "BG_DRIVE_COMPLEX_BAR",
    "BG_BUBBLE_S01", "BG_BUBBLE_S02", "BG_BUBBLE_S03", "BG_BUBBLE_S04",
    "BG_BUBBLE_S05", "BG_BUBBLE_S06", "BG_BUBBLE_S07",
)

MULTI_NAMES = (
    "HOTKEY_YL_B", "HOTKEY_YS_B", "OFFON",
    "B_ITEM_ITEM", "B_ITEM_EQUIP", "B_ITEM_ARMOR", "B_ITEM_PET", "B_ITEM_RIDE",
    "B_BASIC_GUILD", "B_BASIC_PLAYER", "B_BASIC_ITEM", "B_BASIC_PSHOP",
    "B_BASIC_QUEST", "B_BASIC_GM", "B_BASIC_SYSTEM", "B_BASIC_SKILL", "B_COMMAND_TEAM",
    "B_RIGHTBOX_BUTTON", "CLOSESCREEN", "MINISCREEN", "ZONESCREEN", "YELLOW_BUTTON",
    "LEFT_BUTTON", "RIGHT_BUTTON", "NEW_CHARACTER", "PLAY_GAME", "B_ONLINEHELP",
    "ADD_POINT", "B_PVP_LEFT", "B_PVP_RIGHT", "HP", "MP", "WP", "EXP",
    "B_CHANNEL_CA", "B_CHANNEL_CB", "B_CHANNEL_CC", "B_CHANNEL_CD", "B_CHANNEL_CE",
    "ICON_CHANNEL", "ICON_BUBBLE", "B_EMOTETION", "EMOTIONAL_ICON", "B_BACK", "B_NEXT", "SCROLLUP_S", "SCROLLDOWN_S",
    "SCROLLUP_BUTTON", "SCROLLDOWN_BUTTON", "B_RADIO_B",
    "B_LIST_ADD", "B_LIST_DEL", "B_LIST_EDIT", "B_LIST_TO_INGORE",
    "B_LIST_FRIENDLIST", "B_LIST_IGNORELIST",
    "B_DRIVER01", "B_DRIVER02", "B_CHANGE_NAME",
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
    key = f"interface/{name}.spr".casefold()
    meta = entries.get(key)
    if meta is None:
        loose = source_dir / f"{name}.spr"
        return (loose.read_bytes(), "loose") if loose.is_file() else (None, "")
    original, compressed, start = meta
    with archive.open("rb") as handle:
        handle.seek(start)
        payload = handle.read(compressed or original)
    return (lzss(payload, original) if compressed else payload), "archive"


def _read_png_export(directory: Path, name: str, single: bool) -> list[Image.Image]:
    try:
        if single:
            sources = [directory / f"{name}.png"]
        else:
            metadata = json.loads((directory / f"{name}.0000.json").read_text(encoding="utf-8"))
            frame_count = metadata.get("frames")
            if type(frame_count) is not int or not 1 <= frame_count <= 4096:
                return []
            sources = [directory / f"{name}.{index:04d}.png" for index in range(frame_count)]
        frames = []
        for source in sources:
            with Image.open(source) as image:
                frames.append(image.convert("RGBA"))
        return frames
    except (OSError, ValueError, AttributeError):
        return []


def _read_frames(source_dir: Path, name: str, archive: Path, entries: dict, single: bool):
    for export in PNG_EXPORTS:
        frames = _read_png_export(source_dir.parent / export / "Interface", name, single)
        if frames:
            return frames, export
    raw, origin = _read_sprite(source_dir, name, archive, entries)
    return ([frame.image for frame in decode_spr(raw)], origin) if raw is not None else ([], "")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT,
                        help="original client Interface directory")
    args = parser.parse_args()
    archive = args.source.parent / "Data1.dat"
    if not args.source.is_dir() and not archive.is_file() and not any(
        (args.source.parent / export / "Interface").is_dir() for export in PNG_EXPORTS
    ):
        parser.error("Original client Interface, archive, and PNG exports are missing")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    entries = _archive_entries(archive)
    complete = 0
    login_ready = False

    for name in SINGLE_NAMES:
        frames, origin = _read_frames(args.source, name, archive, entries, single=True)
        if not frames:
            print("CLASSIC_SOURCE_NOT_FOUND", name)
            continue
        if len(frames) != 1:
            print("CLASSIC_SOURCE_NOT_SINGLE_FRAME", name, len(frames))
            continue
        target = OUTPUT / (LOGIN_OUTPUT if name == "ACCOUNT_LOGIN" else f"{name}.png")
        changed = _save_if_changed(target, frames[0])
        print("CLASSIC_PRIVATE_ART" if changed else "CLASSIC_UNCHANGED",
              name, frames[0].size, origin)
        complete += 1
        if name == "ACCOUNT_LOGIN":
            login_ready = True
            if sys.platform == "win32":
                captions = render_hk_login_captions()
                _save_if_changed(OUTPUT / LOGIN_CAPTIONS_OUTPUT, captions)
                write_hk_login_input_font(OUTPUT)
                # The same HK build creates a shared 新細明體 12/500 GDI font
                # used by Character Select/Profile and other classic UI text.
                # Keep the generated raster private exactly like the login font.
                write_hk_shared_ui_font(OUTPUT)

    for name in MULTI_NAMES:
        frames, origin = _read_frames(args.source, name, archive, entries, single=False)
        if not frames:
            print("CLASSIC_SOURCE_EMPTY", name)
            continue
        for index, frame in enumerate(frames):
            target = OUTPUT / f"{name}_{index}.png"
            _save_if_changed(target, frame)
        print("CLASSIC_PRIVATE_FRAMES", name, len(frames), frames[0].size, origin)
        complete += 1

    print("CLASSIC_ASSETS_READY", complete,
          "source_spr_unchanged=true archive_unchanged=true git_private=true")
    return 0 if login_ready and complete >= 20 else 1


if __name__ == "__main__":
    raise SystemExit(main())
