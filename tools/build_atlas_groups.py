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

from PIL import Image

if __package__:
    from .convert_assets import decode_spr, pack
else:
    from convert_assets import decode_spr, pack


def group_key(relative: Path) -> Path:
    category = relative.parts[0].casefold()
    stem = (relative.stem.split("_", 1)[0]
            if category in ("enemy", "player") else relative.stem)
    if category == "enemy":
        stem = stem.upper()
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


def _contains_rect(outer, inner) -> bool:
    ox, oy, ow, oh = outer
    ix, iy, iw, ih = inner
    return ix >= ox and iy >= oy and ix + iw <= ox + ow and iy + ih <= oy + oh


def _split_free_rect(free, used):
    fx, fy, fw, fh = free
    ux, uy, uw, uh = used
    if ux >= fx + fw or ux + uw <= fx or uy >= fy + fh or uy + uh <= fy:
        return [free]
    out = []
    if ux > fx:
        out.append((fx, fy, ux - fx, fh))
    if ux + uw < fx + fw:
        out.append((ux + uw, fy, fx + fw - ux - uw, fh))
    if uy > fy:
        out.append((fx, fy, fw, uy - fy))
    if uy + uh < fy + fh:
        out.append((fx, uy + uh, fw, fy + fh - uy - uh))
    return [rect for rect in out if rect[2] > 0 and rect[3] > 0]


def _prune_free_rects(rects):
    unique = list(dict.fromkeys(rects))
    return [rect for i, rect in enumerate(unique)
            if not any(i != j and _contains_rect(other, rect)
                       for j, other in enumerate(unique))]


def pack_atomic_rectangles(rectangles: list[tuple[int, int, int]], side: int):
    """Deterministic no-rotation MaxRects-style placement for source SPR blocks."""
    ordered = sorted(rectangles, key=lambda row: (-(row[1] * row[2]),
                                                  -max(row[1], row[2]),
                                                  -min(row[1], row[2]), row[0]))
    pages = []
    placements = {}
    for block_id, width, height in ordered:
        if width > side or height > side:
            raise ValueError(f"atomic block {width}x{height} exceeds atlas side {side}")
        best = None
        for page_index, page in enumerate(pages):
            for free in page["free"]:
                x, y, free_w, free_h = free
                if width > free_w or height > free_h:
                    continue
                new_w = max(page["used_w"], x + width)
                new_h = max(page["used_h"], y + height)
                growth = new_w * new_h - page["used_w"] * page["used_h"]
                waste = free_w * free_h - width * height
                score = (growth, waste, min(free_w - width, free_h - height),
                         page_index, y, x)
                if best is None or score < best[0]:
                    best = (score, page_index, x, y)
        if best is None:
            pages.append({"free": [(0, 0, side, side)], "used_w": 0, "used_h": 0})
            best = ((width * height, side * side - width * height,
                     min(side - width, side - height), len(pages) - 1, 0, 0),
                    len(pages) - 1, 0, 0)
        _, page_index, x, y = best
        page = pages[page_index]
        used = (x, y, width, height)
        split = []
        for free in page["free"]:
            split.extend(_split_free_rect(free, used))
        page["free"] = _prune_free_rects(split)
        page["used_w"] = max(page["used_w"], x + width)
        page["used_h"] = max(page["used_h"], y + height)
        placements[block_id] = (page_index, x, y)
    bounds = [(page["used_w"], page["used_h"]) for page in pages]
    return placements, bounds


def pack_source_blocks(frames, source_ranges: list[tuple[int, int]], side: int):
    """Pack each original SPR atomically, then co-pack those blocks onto atlas pages."""
    blocks = []
    records = [None] * len(frames)
    for source_index, (start, length) in enumerate(source_ranges):
        local_pages, local_records = pack(frames[start:start + length], side)
        if len(local_pages) > 1:
            raise ValueError(f"source SPR needs {len(local_pages)} pages at side {side}")
        if not local_pages:
            for offset, record in enumerate(local_records):
                records[start + offset] = record
            continue
        blocks.append((source_index, start, local_pages[0], local_records))

    placements, bounds = pack_atomic_rectangles(
        [(source_index, block.width, block.height)
         for source_index, _, block, _ in blocks], side)
    pages = [Image.new("RGBA", (width, height)) for width, height in bounds]
    for source_index, start, block, local_records in blocks:
        page_index, x, y = placements[source_index]
        pages[page_index].paste(block, (x, y))
        for offset, record in enumerate(local_records):
            record = dict(record)
            if record["page"] >= 0:
                rx, ry, rw, rh = record["rect"]
                record["page"] = page_index
                record["rect"] = [rx + x, ry + y, rw, rh]
            records[start + offset] = record
    if any(record is None for record in records):
        raise ValueError("internal source-locality record gap")
    return pages, records


def build_group(root: Path, output: Path, key: Path, paths: list[Path], side: int,
                source_locality: bool = False) -> dict:
    if not paths or side not in (512, 1024, 2048, 4096):
        raise ValueError("empty group or unsupported atlas side")
    frames = []
    sources = []
    origin_records = []
    source_ranges = []
    for path in paths:
        raw = path.read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        decoded = decode_spr(raw)
        relative = path.relative_to(root).as_posix()
        sources.append({"path": relative, "sha256": digest, "frames": len(decoded)})
        source_ranges.append((len(frames), len(decoded)))
        for index, frame in enumerate(decoded):
            frames.append(frame)
            origin_records.append((relative, index))
    if source_locality:
        pages, records = pack_source_blocks(frames, source_ranges, side)
    else:
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
                "packing_policy": "source_spr_atomic_maxrects_v3" if source_locality else "dense_frames_v1",
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
    parser.add_argument("--source-locality", action="store_true",
                        help="Pack each original SPR independently; A/B candidate only")
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
            rows.append(build_group(root, output, key, paths, side, args.source_locality))
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
