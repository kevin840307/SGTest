#!/usr/bin/env python3
"""Build deterministic appearance-local atlas candidates from a clean client.

Enemy and Player share a directory plus prefix before the first underscore.
Other categories keep each SPR separate until their semantic grouping is verified.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from collections import defaultdict
from pathlib import Path

if __package__:
    from .convert_assets import decode_spr, pack
else:
    from convert_assets import decode_spr, pack


def group_key(relative: Path) -> Path:
    stem = (relative.stem.split("_", 1)[0]
            if relative.parts[0].casefold() in ("enemy", "player") else relative.stem)
    return relative.parent / stem


def select_groups(root: Path, category: str | None = None,
                  prefix: str | None = None,
                  keys: set[str] | None = None) -> dict[Path, list[Path]]:
    groups: dict[Path, list[Path]] = defaultdict(list)
    for path in root.rglob("*"):
        if not path.is_file() or path.is_symlink() or path.suffix.lower() != ".spr":
            continue
        relative = path.relative_to(root)
        if category and relative.parts[0].casefold() != category.casefold():
            continue
        key = group_key(relative)
        if prefix and not key.as_posix().casefold().startswith(prefix.casefold()):
            continue
        if keys is not None and key.as_posix().casefold() not in keys:
            continue
        groups[key].append(path)
    return {key: sorted(paths, key=lambda p: p.relative_to(root).as_posix().casefold())
            for key, paths in sorted(groups.items(), key=lambda pair: pair[0].as_posix().casefold())}


def build_group(root: Path, output: Path, key: Path, paths: list[Path], side: int) -> dict:
    if not paths or side not in (512, 1024, 2048, 4096):
        raise ValueError("empty group or unsupported atlas side")
    frames = []
    sources = []
    origin_records = []
    for path in paths:
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        decoded = decode_spr(raw)
        relative = path.relative_to(root).as_posix()
        sources.append({"path": relative, "sha256": digest, "frames": len(decoded)})
        for index, frame in enumerate(decoded):
            frames.append(frame)
            origin_records.append((relative, index))
    pages, records = pack(frames, side)
    for frame, record in zip(frames, records):
        if record["page"] < 0:
            continue
        x, y, w, h = record["rect"]
        crop = record["crop"]
        actual = pages[record["page"]].crop((x, y, x + w, y + h))
        expected = frame.image.crop(tuple(crop))
        if actual.tobytes() != expected.tobytes():
            raise ValueError("packed frame differs from decoded source")
    target = output / key
    target.mkdir(parents=True, exist_ok=True)
    page_names = []
    for index, page in enumerate(pages):
        name = f"page_{index}.png"
        temporary = target / (name + ".tmp")
        page.save(temporary, format="PNG", optimize=True)
        temporary.replace(target / name)
        page_names.append(name)
    for record, (relative, index) in zip(records, origin_records):
        record["source"] = relative
        record["source_frame"] = index
    document = {"version": 1, "group_key": key.as_posix(), "group_rule": "category_prefix_v1",
                "atlas_side": side, "padding": 2, "pages": page_names, "sources": sources, "frames": records,
                "animation_semantics": None, "layer_semantics": None}
    manifest = target / "group.atlas.json"
    temporary = target / "group.atlas.json.tmp"
    temporary.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temporary.replace(manifest)
    page_area = sum(p.width * p.height for p in pages)
    crop_area = sum((record["crop"][2] - record["crop"][0])
                    * (record["crop"][3] - record["crop"][1]) for record in records)
    return {"group": key.as_posix(), "sources": len(sources), "frames": len(records),
            "pages": len(page_names), "crop_rect_pixels": crop_area,
            "page_fill_ratio": round(crop_area / page_area, 6) if page_area else 0.0,
            "rgba_area_bytes": page_area * 4,
            "png_bytes": sum((target / name).stat().st_size for name in page_names)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--category", help="First-level source category, e.g. Enemy")
    parser.add_argument("--prefix", help="Group key prefix, e.g. Enemy/EN023A")
    parser.add_argument("--group-list", type=Path, help="UTF-8 file with exact group keys, one per line")
    sizing = parser.add_mutually_exclusive_group(required=True)
    sizing.add_argument("--atlas-side", type=int, choices=(512, 1024, 2048, 4096))
    sizing.add_argument("--size-plan", type=Path, help="Measured per-group atlas size plan")
    parser.add_argument("--limit-groups", type=int, help="Trial only; stable first N groups")
    args = parser.parse_args()
    root, output = args.client.resolve(), args.output.resolve()
    if not root.is_dir() or output == root or root in output.parents:
        parser.error("source must exist and output must be outside the clean client")
    keys = None
    if args.group_list is not None:
        keys = {line.strip().casefold() for line in args.group_list.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")}
    groups = select_groups(root, args.category, args.prefix, keys)
    plan = None
    if args.size_plan:
        plan = json.loads(args.size_plan.read_text(encoding="utf-8"))["groups"]
        groups = {key: paths for key, paths in groups.items() if key.as_posix() in plan}
        if {key.as_posix() for key in groups} != set(plan):
            parser.error("size plan contains a group absent from source or filters")
    if keys is not None and {key.as_posix().casefold() for key in groups} != keys:
        parser.error("group list contains a key absent from the clean source")
    if args.limit_groups is not None:
        groups = dict(list(groups.items())[:args.limit_groups])
    rows = []
    for key, paths in groups.items():
        try:
            side = int(plan[key.as_posix()]["side"]) if plan else args.atlas_side
            rows.append(build_group(root, output, key, paths, side))
        except (OSError, ValueError) as exc:
            rows.append({"group": key.as_posix(), "error": str(exc)})
    report = {"version": 1, "atlas_side": args.atlas_side, "groups": rows,
              "failed": sum("error" in row for row in rows)}
    output.mkdir(parents=True, exist_ok=True)
    (output / "group_report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                                               encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 1 if report["failed"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
