#!/usr/bin/env python3
"""Scan a clean HK2 tree for xnFile group archives without extracting media."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

if __package__:
    from .legacy_group_archive import GroupArchive, probe_archive_footer
else:
    from legacy_group_archive import GroupArchive, probe_archive_footer


def scan_archives(root: Path) -> dict:
    root = root.resolve()
    if not root.is_dir():
        raise ValueError("scan root must be a directory")
    scanned = 0
    footer_candidates = []
    valid = []
    rejected = []
    for path in root.rglob("*"):
        if not path.is_file() or path.is_symlink():
            continue
        scanned += 1
        footer = probe_archive_footer(path)
        if footer is None:
            continue
        relative = path.relative_to(root).as_posix()
        footer_candidates.append({"path": relative, **footer})
        try:
            archive = GroupArchive(path)
        except (OSError, ValueError) as exc:
            rejected.append({"path": relative, "reason": str(exc), **footer})
            continue
        valid.append({
            "path": relative,
            "file_size": archive.file_size,
            "index_offset": archive.index_offset,
            "index_raw_size": archive.index_raw_size,
            "index_compressed_size": archive.index_compressed_size,
            "entry_count": len(archive.entries),
        })
    return {
        "version": 1,
        "scanned_files": scanned,
        "footer_candidate_count": len(footer_candidates),
        "valid_archive_count": len(valid),
        "rejected_candidate_count": len(rejected),
        "footer_candidates": footer_candidates,
        "valid_archives": valid,
        "rejected_candidates": rejected,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.client.resolve()
    output = args.output.resolve()
    if not root.is_dir() or output == root or root in output.parents:
        parser.error("client must exist and output must be outside the clean client")
    result = scan_archives(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "scanned_files": result["scanned_files"],
        "footer_candidates": result["footer_candidate_count"],
        "valid_archives": result["valid_archive_count"],
        "rejected_candidates": result["rejected_candidate_count"],
        "archive_paths": [row["path"] for row in result["valid_archives"]],
    }, ensure_ascii=False))
    return 1 if result["rejected_candidate_count"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
