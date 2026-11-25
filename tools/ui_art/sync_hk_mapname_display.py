#!/usr/bin/env python3
"""Sync the one HK UI-045 SPR through the existing asset-converter decoder.

The source is read-only; the decoded PNG is private Git-ignored runtime art.
This is not a second decoder or a game-data authority.
"""
from __future__ import annotations

import hashlib
import io
from pathlib import Path
import sys

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO))
from tools.asset_converter.source.runtime_spr import decode_spr

SOURCE = Path(r"C:\SGO\Stargate Online IE HK\output\Interface\BG_MAPNAME_DISPLAY.spr")
TARGET = REPO / "client/ui/private_ie_hk2/BG_MAPNAME_DISPLAY.png"


def main() -> int:
    if not SOURCE.is_file() or SOURCE.is_symlink() or not 1 <= SOURCE.stat().st_size <= 1024 * 1024:
        raise ValueError("HK BG_MAPNAME_DISPLAY.spr source unavailable or untrusted")
    frames = decode_spr(SOURCE.read_bytes())
    if (len(frames) != 1 or frames[0].image.size != (346, 19) or
            frames[0].origin_x != 0 or frames[0].origin_y != 0):
        raise ValueError("HK UI-045 source frame/geometry/origin mismatch")
    payload = io.BytesIO()
    frames[0].image.save(payload, format="PNG")
    raw = payload.getvalue()
    TARGET.parent.mkdir(parents=True, exist_ok=True)
    if not TARGET.exists() or TARGET.read_bytes() != raw:
        temporary = TARGET.with_suffix(".png.tmp")
        temporary.write_bytes(raw)
        temporary.replace(TARGET)
    print("HK_MAPNAME_SOURCE_SYNC_PASS frame=1 width=346 height=19 origin=0,0 png_sha256=" +
          hashlib.sha256(raw).hexdigest())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
