from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "tools"))

from asset_converter.atlas.adaptive_atlas import build_group as build_atomic
from asset_converter.atlas.build_atlas_groups import pack_atomic_rectangles
from asset_converter.source.convert_assets import decode_spr, pack

SOURCE = ROOT / ".cache" / "asset_converter" / "source"
OUTPUT = ROOT / ".cache" / "asset_packing_benchmark" / "candidates"
MONSTERS = [
    "EN114A","EN089A","EN121A","EN116A","EN023A","EN084A",
    "EN111A","EN125A","EN050C","EN080A","EN093A","EN079A",
]
PADDING = 2
SIDES = (1024, 2048, 4096)


def _sources(monster: str) -> list[Path]:
    paths = sorted(SOURCE.glob(f"enemy/{monster.lower()}_*.spr"), key=lambda p: p.as_posix())
    if not paths:
        raise ValueError(f"missing source for {monster}")
    return paths


def _decode(paths: list[Path]):
    frames = []
    frame_sources = []
    for path in paths:
        relative = path.relative_to(SOURCE).as_posix()
        decoded = decode_spr(path.read_bytes())
        for index, frame in enumerate(decoded):
            frames.append(frame)
            frame_sources.append((relative, index))
    return frames, frame_sources


def _extrude(page: Image.Image, cropped: Image.Image, x: int, y: int, padding: int = PADDING) -> None:
    w, h = cropped.size
    px, py = x + padding, y + padding
    page.paste(cropped, (px, py))
    if not padding:
        return
    left = cropped.crop((0, 0, 1, h)).resize((padding, h))
    right = cropped.crop((w - 1, 0, w, h)).resize((padding, h))
    page.paste(left, (x, py))
    page.paste(right, (px + w, py))
    top = page.crop((x, py, x + w + padding * 2, py + 1)).resize((w + padding * 2, padding))
    bottom = page.crop((x, py + h - 1, x + w + padding * 2, py + h)).resize((w + padding * 2, padding))
    page.paste(top, (x, y))
    page.paste(bottom, (x, py + h))


def pack_maxrects(frames, side: int):
    rectangles = []
    crops = []
    records = []
    for index, frame in enumerate(frames):
        bounds = frame.image.getbbox()
        crops.append(bounds)
        records.append({
            "page": -1,
            "rect": [0, 0, 0, 0],
            "raw_origin": [frame.origin_x, frame.origin_y],
            "crop": list(bounds or (0, 0, 0, 0)),
            "size": list(frame.image.size),
        })
        if bounds is None:
            continue
        w = bounds[2] - bounds[0]
        h = bounds[3] - bounds[1]
        cell_w, cell_h = w + PADDING * 2, h + PADDING * 2
        if cell_w > side or cell_h > side:
            raise ValueError(f"frame {cell_w}x{cell_h} exceeds {side}")
        rectangles.append((index, cell_w, cell_h))
    placements, bounds = pack_atomic_rectangles(rectangles, side)
    pages = [Image.new("RGBA", size) for size in bounds]
    for index, crop in enumerate(crops):
        if crop is None:
            continue
        page_id, x, y = placements[index]
        cropped = frames[index].image.crop(crop)
        _extrude(pages[page_id], cropped, x, y)
        records[index]["page"] = page_id
        records[index]["rect"] = [x + PADDING, y + PADDING, cropped.width, cropped.height]
    return pages, records


def choose(candidates: list[dict]) -> dict:
    minimum = min(row["rgba_bytes"] for row in candidates)
    near = [row for row in candidates if row["rgba_bytes"] <= minimum * 1.05]
    return min(near, key=lambda row: (row["pages"], row["rgba_bytes"], row["side"]))


def _attach_sources(records: list[dict], frame_sources) -> None:
    for row, (source, source_frame) in zip(records, frame_sources):
        row["source"] = source
        row["source_frame"] = source_frame


def _save(candidate: str, monster: str, pages, records, frame_sources, policy: str, side: int) -> dict:
    target = OUTPUT / candidate / "Enemy" / monster
    target.mkdir(parents=True, exist_ok=True)
    _attach_sources(records, frame_sources)
    page_names = []
    for index, page in enumerate(pages):
        name = f"page_{index}.png"
        page.save(target / name, format="PNG", compress_level=3)
        page_names.append(name)
    doc = {
        "version": 1,
        "group_key": f"Enemy/{monster}",
        "packing_policy": policy,
        "atlas_side": side,
        "pages": page_names,
        "frames": records,
    }
    (target / "group.atlas.json").write_text(json.dumps(doc, separators=(",", ":")), encoding="utf-8")
    return {
        "candidate": candidate,
        "monster": monster,
        "pages": len(pages),
        "rgba_bytes": sum(p.width * p.height * 4 for p in pages),
        "png_bytes": sum((target / n).stat().st_size for n in page_names),
        "side": side,
    }


def build_dense_single(monster: str, frames, frame_sources):
    active = []
    for index, frame in enumerate(frames):
        bbox = frame.image.getbbox()
        if bbox is not None:
            active.append((index, frame.image.crop(bbox), bbox))
    records = [{
        "page": -1,
        "rect": [0, 0, 0, 0],
        "raw_origin": [frame.origin_x, frame.origin_y],
        "crop": list(frame.image.getbbox() or (0, 0, 0, 0)),
        "size": list(frame.image.size),
    } for frame in frames]
    best = None
    ordered = sorted(active, key=lambda row: (-row[1].height, -row[1].width, row[0]))
    for width in (512,768,1024,1536,2048,2560,3072,4096,6144,8192):
        if any(img.width + PADDING * 2 > width for _, img, _ in ordered):
            continue
        places = {}
        x = y = shelf_h = PADDING
        used_w = PADDING
        for index, image, _ in ordered:
            if x + image.width + PADDING > width:
                y += shelf_h
                x = PADDING
                shelf_h = 0
            places[index] = (x, y)
            x += image.width + PADDING
            shelf_h = max(shelf_h, image.height + PADDING)
            used_w = max(used_w, x)
        height = y + shelf_h + PADDING
        if height > 8192:
            continue
        area = used_w * height
        if best is None or area < best[0]:
            best = (area, used_w, height, places)
    if best is None:
        raise ValueError(f"{monster}: dense single cannot fit <=8192")
    _, width, height, places = best
    page = Image.new("RGBA", (width, height))
    for index, image, _ in active:
        x, y = places[index]
        page.paste(image, (x, y))
        records[index]["page"] = 0
        records[index]["rect"] = [x, y, image.width, image.height]
    return _save("dense_single", monster, [page], records, frame_sources, "dense_single_v1", max(width, height))


def build_adaptive(monster: str, frames, frame_sources, kind: str):
    rows = []
    packed = {}
    for side in SIDES:
        try:
            pages, records = (pack(frames, side) if kind == "shelf" else pack_maxrects(frames, side))
        except ValueError:
            continue
        rgba = sum(page.width * page.height * 4 for page in pages)
        row = {"side": side, "pages": len(pages), "rgba_bytes": rgba}
        rows.append(row)
        packed[side] = (pages, records)
    if not rows:
        raise ValueError(f"{monster}: no {kind} candidate")
    selected = choose(rows)
    pages, records = packed[selected["side"]]
    name = "adaptive_dense_shelf" if kind == "shelf" else "adaptive_dense_maxrects"
    return _save(name, monster, pages, records, frame_sources, name + "_v1", selected["side"])


def build_atomic_candidate(monster: str, paths: list[Path]) -> dict:
    root = OUTPUT / "adaptive_atomic"
    result = build_atomic(SOURCE, root, Path("Enemy") / monster, paths, force=True)
    folder = root / "Enemy" / monster
    atlas = folder / "atlas.json"
    group = folder / "group.atlas.json"
    if atlas.exists():
        shutil.copy2(atlas, group)
    return {
        "candidate": "adaptive_atomic",
        "monster": monster,
        "pages": result["pages"],
        "rgba_bytes": result["rgba_bytes"],
        "png_bytes": result["png_bytes"],
        "side": result.get("side"),
    }


def main() -> int:
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    rows = []
    for monster in MONSTERS:
        paths = _sources(monster)
        frames, frame_sources = _decode(paths)
        rows.append(build_dense_single(monster, frames, frame_sources))
        rows.append(build_adaptive(monster, frames, frame_sources, "shelf"))
        rows.append(build_adaptive(monster, frames, frame_sources, "maxrects"))
        rows.append(build_atomic_candidate(monster, paths))
        print(monster, "done", flush=True)
    report = {"monsters": MONSTERS, "rows": rows}
    output = ROOT / ".cache" / "asset_packing_benchmark" / "build.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(output)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
