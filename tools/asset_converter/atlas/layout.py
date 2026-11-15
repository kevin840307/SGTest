"""Independent source asset grouping and bounded atlas fallback placement.

Height-Dense is the only production packer. MaxRects is used *only* for
additional pages of the same asset when a single <=4096 atlas cannot fit.
"""
from __future__ import annotations
from collections import defaultdict
from pathlib import Path

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
    """Deterministic no-rotation fallback for frames of ONE asset."""
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
