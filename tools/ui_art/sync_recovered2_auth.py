#!/usr/bin/env python3
"""Copy authorized local Recovered appearance assets for SGTest UI.

Only a local developer copy is created. This directory is Git-ignored and is
never bundled in the public source repository. Recovered gameplay scripts,
account data and network logic are intentionally not copied.
"""
from __future__ import annotations
import argparse
import hashlib
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT/"client/ui/private_recovered2"
SOURCE = Path(r"C:\SGO\NewVersion\Recovered2")
CURSOR_SOURCE = Path(r"C:\SGO\Recovered\client\ui\cursor")
FILES = {
    "ui/themes/paradise_window_theme.tres":"paradise_window_theme.tres",
    "ui/SELECT_CHARACTER.png":"SELECT_CHARACTER.png",
    "ui/CREATE_CHARACTER.png":"CREATE_CHARACTER.png",
    **{
        f"ui/create_anim/NEW_CHARACTER.spr.{i:04d}.png":
            f"create_anim/NEW_CHARACTER.spr.{i:04d}.png"
        for i in range(4)
    },
    "ui/LEFT_BUTTON.png":"LEFT_BUTTON.png",
    "ui/RIGHT_BUTTON.png":"RIGHT_BUTTON.png",
    "ui/BG_LOADING.png":"BG_LOADING.png",
    "ui/COMMAND.png":"COMMAND.png",
    "ui/BH_HOTKEY.png":"BH_HOTKEY.png",
    "ui/HPBAR.png":"HPBAR.png",
    "ui/MINIMAP.png":"MINIMAP.png",
    "ui/CLOSESCREEN.png":"CLOSESCREEN.png",
    "ui/BG_RIGHTBOX.png":"BG_RIGHTBOX.png",
    "ui/BG_NPC_MESSAGE.png":"BG_NPC_MESSAGE.png",
    "ui/icons/craft_dismantle_icon.png":"craft_dismantle_icon.png",
    "ui/icons/game_info_icon.png":"game_info_icon.png",
    "ui/ICON_CHANNEL1.png":"ICON_CHANNEL1.png",
    "ui/B_EMOTION.spr.0000.png":"B_EMOTION.spr.0000.png",
    "ui/ICON_BUBBLE1.png":"ICON_BUBBLE1.png",
    "ui/ICON_CHAT_EXPAND.png":"ICON_CHAT_EXPAND.png",
    "ui/ICON_CHAT_COLLAPSE.png":"ICON_CHAT_COLLAPSE.png",
    "2.ogv":"2.ogv",
}
CURSOR_PATTERNS = (
    "C_NORMAL.spr.[0-9][0-9][0-9][0-9].png",
    "C_ATTACK.spr.[0-9][0-9][0-9][0-9].png",
    "C_CHECK.spr.[0-9][0-9][0-9][0-9].png",
    "C_TAKE.spr.[0-9][0-9][0-9][0-9].png",
    "C_PLAYER.spr.[0-9][0-9][0-9][0-9].png",
    "C_SKILL.spr.[0-9][0-9][0-9][0-9].png",
    "C_AUTOWALK.spr.png",
)


def copy_if_changed(source: Path, target: Path, label: str) -> None:
    if not source.is_file():
        raise SystemExit(f"MISSING_LOCAL_REFERENCE {source}")
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.is_file() and target.stat().st_size == source.stat().st_size:
        if hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(source.read_bytes()).digest():
            print(f"LOCAL_REFERENCE_UNCHANGED {label} bytes={target.stat().st_size}")
            return
    shutil.copy2(source, target)
    print(f"LOCAL_REFERENCE_COPY {label} bytes={target.stat().st_size}")


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--source",type=Path,default=SOURCE)
    parser.add_argument(
        "--cursor-source",type=Path,default=CURSOR_SOURCE,
        help="Recovered cursor directory used as the exact visual reference")
    parser.add_argument(
        "--item-photos",action="store_true",
        help="copy optional inventory Photos/PhotoNNNN.png to private local UI area")
    args=parser.parse_args()

    files=dict(FILES)
    if args.item_photos:
        for icon in sorted((args.source/"Assets/Items/Photos").glob("Photo[0-9][0-9][0-9][0-9].png")):
            files[f"Assets/Items/Photos/{icon.name}"]=f"item_photos/{icon.name}"

    for src,dst in files.items():
        copy_if_changed(args.source/src,DEST/dst,dst)

    cursor_files: dict[str, Path] = {}
    for pattern in CURSOR_PATTERNS:
        matches=sorted(args.cursor_source.glob(pattern))
        if not matches:
            raise SystemExit(f"MISSING_LOCAL_CURSOR_REFERENCE {args.cursor_source / pattern}")
        for source in matches:
            cursor_files[source.name]=source
    for name,source in sorted(cursor_files.items()):
        copy_if_changed(source,DEST/"cursor"/name,f"cursor/{name}")

    print(f"LOCAL_CURSOR_REFERENCE_READY files={len(cursor_files)} source={args.cursor_source}")
    print("LOCAL_REFERENCE_READY original Recovered files are excluded from Git")


if __name__=="__main__":
    main()
