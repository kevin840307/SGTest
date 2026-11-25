#!/usr/bin/env python3
"""Decode the original HK UI-084 SPRs into private, Git-ignored PNG frames.

The existing asset converter owns SPR decoding. This script only validates and
copies exact original frames for runtime presentation; it never edits sources.
"""
from __future__ import annotations

import hashlib
import io
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
from tools.asset_converter.source.runtime_spr import decode_spr

SOURCE = Path(r"C:\SGO\Stargate Online IE HK\output\Interface")
DEST = ROOT / "client/ui/private_ie_hk2"
SPECS = {
    "BG_ENEMY_HP.spr": (1, (75, 21)),
    "B_ENEMY_HP.spr": (5, (14, 14)),
    "HP.spr": (16, (107, 10)),
}


def main() -> int:
    frames_to_write: list[tuple[Path, bytes]] = []
    hashes: list[str] = []
    for name, (expected_count, expected_size) in SPECS.items():
        path = SOURCE / name
        if not path.is_file() or path.is_symlink() or not 1 <= path.stat().st_size <= 2 * 1024 * 1024:
            raise ValueError(f"Missing or untrusted HK UI-084 source: {name}")
        frames = decode_spr(path.read_bytes())
        if len(frames) != expected_count:
            raise ValueError(f"HK {name} expected {expected_count} frames, got {len(frames)}")
        for index, frame in enumerate(frames):
            if frame.image.size != expected_size or (frame.origin_x, frame.origin_y) != (0, 0):
                raise ValueError(f"HK {name} frame {index} geometry/origin mismatch")
            out = io.BytesIO()
            frame.image.save(out, format="PNG")
            content = out.getvalue()
            stem = name.removesuffix(".spr")
            filename = f"{stem}.png" if expected_count == 1 else f"{stem}_{index:02d}.png"
            frames_to_write.append((DEST / filename, content))
            hashes.append(hashlib.sha256(content).hexdigest())
    DEST.mkdir(parents=True, exist_ok=True)
    changed = 0
    for target, content in frames_to_write:
        if target.is_file() and target.read_bytes() == content:
            continue
        temporary = target.with_name(target.name + ".tmp")
        temporary.write_bytes(content)
        temporary.replace(target)
        changed += 1
    fingerprint = hashlib.sha256("".join(hashes).encode("ascii")).hexdigest()
    print(f"HK_ENEMY_HP_SOURCE_SYNC_PASS frames={len(frames_to_write)} changed={changed} sha256={fingerprint}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
