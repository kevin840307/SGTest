"""Sync only verified HK 011_LOGIN_LOADING SPR PNG frames into ignored local art.

The source tree is read-only. This script refuses to replace nonmatching local
art or to publish original/private PNG bytes into Git.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SOURCE = Path(r"C:\SGO\Stargate Online IE HK\Data1_PNG\Interface")
DEFAULT_TARGET = ROOT / "client" / "ui" / "private_ie_hk2"


def _png_size(raw: bytes) -> tuple[int, int]:
    if len(raw) < 24 or raw[:8] != b"\x89PNG\r\n\x1a\n" or raw[12:16] != b"IHDR":
        raise ValueError("invalid PNG/IHDR")
    return struct.unpack(">II", raw[16:24])


def sync(source: Path, target: Path) -> dict:
    source = source.resolve()
    target = target.resolve()
    if not source.is_dir():
        raise FileNotFoundError(f"HK source Interface not found: {source}")
    specs = [("BG_LOGIN_LOADING", None, 1, (233, 49), (-2, -1))]
    specs += [("EXP", index, 16, (225, 12), (0, 0)) for index in range(16)]
    staged: list[tuple[str, bytes]] = []
    for stem, index, count, size, origin in specs:
        name = stem if index is None else f"{stem}.{index:04d}"
        png = source / f"{name}.png"
        meta = source / f"{name}.json"
        document = json.loads(meta.read_text(encoding="utf-8"))
        expected_source = f"{stem}.spr"
        if (
            document.get("source") != expected_source
            or document.get("frame") != (index if index is not None else 0)
            or document.get("frames") != count
            or (document.get("width"), document.get("height")) != size
            or (document.get("origin_x"), document.get("origin_y")) != origin
        ):
            raise ValueError(f"HK original frame provenance mismatch: {name}")
        raw = png.read_bytes()
        if _png_size(raw) != size:
            raise ValueError(f"HK PNG frame geometry mismatch: {name}")
        out = target / f"{name}.png"
        if out.exists() and out.read_bytes() != raw:
            raise FileExistsError(f"Refusing to replace changed local HK art: {out}")
        staged.append((name, raw))
    target.mkdir(parents=True, exist_ok=True)
    copied = 0
    for name, raw in staged:
        out = target / f"{name}.png"
        if not out.exists():
            out.write_bytes(raw)
            copied += 1
    result = {
        "source": str(source), "destination": str(target),
        "validated": len(staged), "copied": copied,
        "sha256": {
            f"{name}.png": hashlib.sha256(raw).hexdigest()
            for name, raw in staged
        },
    }
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--destination", type=Path, default=DEFAULT_TARGET)
    args = parser.parse_args()
    result = sync(args.source, args.destination)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
