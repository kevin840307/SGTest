#!/usr/bin/env python3
"""Read-only inventory for owned legacy game assets (never decodes or uploads bytes).

Example:
  python tools/legacy_asset_inventory.py --root "C:\OldGame" --output reports/assets.json --sha256

The report contains relative paths, sizes, file extensions and optional SHA256.
No timestamps or absolute paths are recorded, so repeated scans are comparable.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from collections import Counter
from pathlib import Path

SCHEMA_VERSION = 2
KNOWN_EXTENSIONS = {
    ".spr", ".sif", ".act", ".itf", ".itd", ".dat", ".csv", ".lst", ".grp", ".cab",
    ".png", ".bmp", ".tga", ".jpg", ".jpeg", ".webp",
    ".wav", ".ogg", ".mp3", ".mid", ".avi", ".mp4", ".zip"
}


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def inventory(root: Path, with_sha256: bool = False, header_bytes: int = 16) -> dict:
    if not 0 <= header_bytes <= 64:
        raise ValueError("header_bytes must be between 0 and 64")
    if not root.is_dir():
        raise ValueError("input root must be a directory")
    root = root.resolve(strict=True)
    counts: Counter[str] = Counter()
    files = []
    rejected_links = 0
    # rglob does not follow symlinked directories in current Python versions;
    # individual symlinked files are also excluded.
    for path in sorted(root.rglob("*"), key=lambda p: p.as_posix().casefold()):
        if path.is_symlink():
            rejected_links += 1
            continue
        if not path.is_file() or not path.resolve().is_relative_to(root):
            continue
        relative = path.relative_to(root).as_posix()
        suffix = path.suffix.lower() or "[none]"
        size = path.stat().st_size
        with path.open("rb") as header_stream:
            header_hex = header_stream.read(header_bytes).hex()
        item = {"relative_path": relative, "extension": suffix, "size_bytes": size,
                "header_hex": header_hex}
        if with_sha256:
            item["sha256"] = sha256_file(path)
        files.append(item)
        counts[suffix] += 1
    found = set(counts)
    return {
        "schema_version": SCHEMA_VERSION,
        "file_count": len(files),
        "total_bytes": sum(x["size_bytes"] for x in files),
        "extension_counts": dict(sorted(counts.items())),
        "missing_legacy_formats": sorted(x for x in (".spr", ".sif", ".itf", ".itd") if x not in found),
        "skipped_symbolic_links": rejected_links,
        "files": files,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True,
                        help="folder with your locally held original client assets")
    parser.add_argument("--output", type=Path, required=True,
                        help="JSON report path outside the input folder")
    parser.add_argument("--sha256", action="store_true",
                        help="stream files and compute hashes (slower)")
    parser.add_argument("--header-bytes", type=int, default=16,
                        help="first N bytes as hex in JSON (default 16, maximum 64)")
    args = parser.parse_args()
    src = args.root.resolve(strict=True)
    dst = args.output.resolve()
    if dst == src or dst.is_relative_to(src):
        parser.error("output must be outside source folder (do not write into originals)")
    payload = inventory(src, with_sha256=args.sha256, header_bytes=args.header_bytes)
    dst.parent.mkdir(parents=True, exist_ok=True)
    dst.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
                   encoding="utf-8")
    print("Scanned %d files / %d bytes. Legacy formats missing: %s" %
          (payload["file_count"], payload["total_bytes"],
           ", ".join(payload["missing_legacy_formats"]) or "(none)"))
    print("Output: %s" % dst)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
