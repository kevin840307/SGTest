#!/usr/bin/env python3
"""Copy authorized local Recovered2 appearance assets for SGTest auth UI.

Only a local developer copy is created. This directory is Git-ignored and is
never bundled in the public source repository. Recovered2 gameplay scripts,
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
FILES = {
    "ui/themes/paradise_window_theme.tres":"paradise_window_theme.tres",
    "ui/SELECT_CHARACTER.png":"SELECT_CHARACTER.png",
    "ui/CREATE_CHARACTER.png":"CREATE_CHARACTER.png",
    "ui/LEFT_BUTTON.png":"LEFT_BUTTON.png",
    "ui/RIGHT_BUTTON.png":"RIGHT_BUTTON.png",
    "ui/BG_LOADING.png":"BG_LOADING.png",
    "2.ogv":"2.ogv",
}
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--source",type=Path,default=SOURCE)
    args=parser.parse_args()
    for src,dst in FILES.items():
        target=DEST/dst
        file=args.source/src
        if not file.is_file():
            raise SystemExit(f"MISSING_LOCAL_REFERENCE {file}")
        DEST.mkdir(parents=True,exist_ok=True)
        # A running Godot client may hold an existing imported texture open.
        # Do not overwrite unchanged private data during a UI-only sync.
        if target.is_file() and target.stat().st_size == file.stat().st_size:
            if hashlib.sha256(target.read_bytes()).digest() == hashlib.sha256(file.read_bytes()).digest():
                print(f"LOCAL_REFERENCE_UNCHANGED {dst} bytes={target.stat().st_size}")
                continue
        shutil.copy2(file,target)
        print(f"LOCAL_REFERENCE_COPY {dst} bytes={target.stat().st_size}")
    print("LOCAL_REFERENCE_READY original Recovered2 files are excluded from Git")

if __name__=="__main__":
    main()
