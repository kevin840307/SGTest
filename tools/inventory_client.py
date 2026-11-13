#!/usr/bin/env python3
"""Read-only inventory of a local original client; never copies asset bytes."""
from __future__ import annotations

import argparse
import hashlib
import json
from collections import Counter
from pathlib import Path


def scan(root: Path, hashes: bool = True) -> dict:
    if not root.is_dir():
        raise ValueError(f"missing client directory: {root}")
    rows = []
    by_extension: Counter[str] = Counter()
    by_category: Counter[str] = Counter()
    for path in sorted(root.rglob("*"), key=lambda p: str(p.relative_to(root)).casefold()):
        if path.is_symlink() or not path.is_file():
            continue
        relative = path.relative_to(root)
        digest = hashlib.sha256()
        with path.open("rb") as handle:
            magic = handle.read(16)
            if hashes:
                digest.update(magic)
                for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                    digest.update(chunk)
        row = {"path": relative.as_posix(), "size": path.stat().st_size,
               "magic16": magic.hex(), "extension": path.suffix.lower()}
        if hashes:
            row["sha256"] = digest.hexdigest()
        rows.append(row)
        by_extension[row["extension"] or "[none]"] += 1
        by_category[relative.parts[0] if len(relative.parts) > 1 else "[root]"] += 1
    return {"schema_version": 1, "file_count": len(rows),
            "total_bytes": sum(row["size"] for row in rows),
            "by_extension": dict(sorted(by_extension.items())),
            "by_category": dict(sorted(by_category.items())), "files": rows}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("report", type=Path)
    parser.add_argument("--no-hash", action="store_true")
    args = parser.parse_args()
    if args.report.resolve() == args.client.resolve() or args.client.resolve() in args.report.resolve().parents:
        parser.error("report must be outside the source client")
    result = scan(args.client, not args.no_hash)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: result[k] for k in ("file_count", "total_bytes", "by_extension", "by_category")},
                     ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
