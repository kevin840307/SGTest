#!/usr/bin/env python3
"""Materialize a private legacy asset working set using original lookup precedence.

The original xnFileRead searches loose path roots before opened group archives.
This tool reproduces that content precedence for selected globs only:
archive base -> loose-file override. Original/derived media stays local.
"""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import shutil
from pathlib import Path

if __package__:
    from .legacy_group_archive import GroupArchive, safe_output_path
else:
    from legacy_group_archive import GroupArchive, safe_output_path


def palette_patterns(path: Path) -> list[str]:
    document = json.loads(path.read_text(encoding="utf-8"))
    visuals = document.get("visual_to_maps")
    if not isinstance(visuals, dict):
        raise ValueError("palette JSON has no visual_to_maps object")
    return [f"Enemy/{name}*" for name in sorted(visuals, key=str.casefold)]


def matches(path: str, patterns: list[str]) -> bool:
    value = path.replace("\\", "/").casefold()
    return any(fnmatch.fnmatchcase(value, pattern.replace("\\", "/").casefold())
               for pattern in patterns)


def materialize(client_root: Path, archive_path: Path, output: Path,
                patterns: list[str]) -> dict:
    client_root = client_root.resolve()
    archive_path = archive_path.resolve()
    output = output.resolve()
    if not client_root.is_dir() or not archive_path.is_file():
        raise ValueError("client root or archive does not exist")
    if not patterns:
        raise ValueError("at least one selection pattern is required")
    if output == client_root or client_root in output.parents:
        raise ValueError("output must be outside the original client")

    archive = GroupArchive(archive_path)
    archive_rows = {entry.path.casefold(): entry for entry in archive.entries
                    if matches(entry.path, patterns)}

    loose_rows = {}
    for path in client_root.rglob("*"):
        if not path.is_file() or path.is_symlink() or path.resolve() == archive_path:
            continue
        relative = path.relative_to(client_root).as_posix()
        if matches(relative, patterns):
            loose_rows[relative.casefold()] = path

    keys = sorted(set(archive_rows) | set(loose_rows))
    rows = []
    for key in keys:
        archive_entry = archive_rows.get(key)
        loose_path = loose_rows.get(key)
        if archive_entry is not None:
            relative = archive_entry.path
        elif loose_path is not None:
            relative = loose_path.relative_to(client_root).as_posix()
        else:
            raise AssertionError("unreachable materialization key")

        if loose_path is not None:
            payload = loose_path.read_bytes()
            source = "loose_override" if archive_entry is not None else "loose_only"
            source_path = loose_path.relative_to(client_root).as_posix()
        else:
            payload = archive.read_entry(archive_entry)
            source = "archive_base"
            source_path = archive_entry.path

        target = safe_output_path(output, relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(payload)
        rows.append({
            "path": relative,
            "source": source,
            "source_path": source_path,
            "size": len(payload),
            "sha256": hashlib.sha256(payload).hexdigest(),
        })

    report = {
        "version": 1,
        "precedence": "loose_override_then_archive_base",
        "archive": archive_path.name,
        "patterns": patterns,
        "file_count": len(rows),
        "archive_base_count": sum(row["source"] == "archive_base" for row in rows),
        "loose_override_count": sum(row["source"] == "loose_override" for row in rows),
        "loose_only_count": sum(row["source"] == "loose_only" for row in rows),
        "files": rows,
    }
    output.mkdir(parents=True, exist_ok=True)
    (output / "materialization.json").write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client_root", type=Path)
    parser.add_argument("archive", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--match", action="append", default=[])
    parser.add_argument("--palette", type=Path,
                        help="Palette JSON; adds Enemy/<visual>* for every visual")
    args = parser.parse_args()

    patterns = list(args.match)
    if args.palette:
        patterns.extend(palette_patterns(args.palette))
    patterns = sorted(set(patterns), key=str.casefold)
    result = materialize(args.client_root, args.archive, args.output, patterns)
    print(json.dumps({
        "files": result["file_count"],
        "archive_base": result["archive_base_count"],
        "loose_override": result["loose_override_count"],
        "loose_only": result["loose_only_count"],
        "patterns": len(patterns),
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
