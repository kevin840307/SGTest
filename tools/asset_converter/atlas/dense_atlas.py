"""Deterministic per-asset Height-Dense atlas packing.

Every logical asset remains physically independent. Frames are transparency-
trimmed, sorted by height then width descending, and packed into one <=4096
page when possible. A frame-level paged MaxRects fallback is used only when a
future single asset cannot fit one page. No padding is emitted: runtime region
draws must keep clip_uv enabled.
"""

from __future__ import annotations

import hashlib
import json
import struct
from contextlib import ExitStack
from pathlib import Path

from PIL import Image

from .build_atlas_groups import pack_atomic_rectangles
from ..source.convert_assets import decode_spr
from ..source import runtime_spr

POLICY = "per_asset_height_dense_v1"
MANIFEST_VERSION = 3
MAX_SIDE = 4096
WIDTHS = (
    128, 160, 192, 224, 256, 320, 384, 448, 512, 640, 768, 896,
    1024, 1152, 1280, 1408, 1536, 1664, 1792, 1920, 2048, 2304,
    2560, 2816, 3072, 3328, 3584, 3840, 4096,
)
DECODER_SHA256 = hashlib.sha256(Path(runtime_spr.__file__).read_bytes()).hexdigest()


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _source_fingerprint(root: Path, paths: list[Path]) -> tuple[str, list[dict]]:
    digest = hashlib.sha256()
    details: list[dict] = []
    for path in paths:
        relative = path.relative_to(root).as_posix()
        file_hash = _sha256(path)
        digest.update(relative.encode("utf-8"))
        digest.update(bytes.fromhex(file_hash))
        details.append({"path": relative, "sha256": file_hash})
    return digest.hexdigest(), details


def _previous_manifest(path: Path) -> dict:
    try:
        parsed = json.loads(path.read_text(encoding="utf-8"))
        return parsed if isinstance(parsed, dict) else {}
    except (OSError, ValueError, TypeError):
        return {}


def _metadata_digest(data: dict) -> str:
    fields = {key: value for key, value in data.items() if key != "metadata_sha256"}
    payload = json.dumps(fields, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _is_cached(folder: Path, old: dict, source_digest: str) -> bool:
    if (
        old.get("version") != MANIFEST_VERSION
        or old.get("packing_policy") != POLICY
        or old.get("source_digest") != source_digest
    ):
        return False
    # A manifest written by an older Dense builder may not have the decoder
    # stamp/metadata digest yet. Treat it as a cache candidate, then pixel/pivot
    # revalidate it once in build_group(). A present but wrong digest is stale.
    metadata_sha256 = old.get("metadata_sha256")
    if metadata_sha256 is not None and metadata_sha256 != _metadata_digest(old):
        return False
    pages = old.get("pages", [])
    hashes = old.get("page_sha256", {})
    if not isinstance(pages, list) or not isinstance(hashes, dict):
        return False
    for name in pages:
        page = folder / str(name)
        if Path(str(name)).name != str(name) or not page.is_file() or _sha256(page) != hashes.get(name):
            return False
    return True


def _empty_records(frames: list) -> list[dict]:
    records = []
    for frame in frames:
        bounds = frame.image.getbbox()
        records.append(
            {
                "page": -1,
                "rect": [0, 0, 0, 0],
                "crop": list(bounds or (0, 0, 0, 0)),
                "size": list(frame.image.size),
                "raw_origin": [frame.origin_x, frame.origin_y],
            }
        )
    return records


def _visible_rows(frames: list) -> list[tuple[int, tuple[int, int, int, int], int, int]]:
    rows = []
    for index, frame in enumerate(frames):
        bounds = frame.image.getbbox()
        if bounds is None:
            continue
        width = bounds[2] - bounds[0]
        height = bounds[3] - bounds[1]
        if width > MAX_SIDE or height > MAX_SIDE:
            raise ValueError(f"frame {index} exceeds {MAX_SIDE}: {width}x{height}")
        rows.append((index, bounds, width, height))
    rows.sort(key=lambda row: (-row[3], -row[2], row[0]))
    return rows


def _single_page_geometry(rows: list[tuple[int, tuple[int, int, int, int], int, int]]):
    if not rows:
        return (0, 0, {})
    best = None
    min_width = max(row[2] for row in rows)
    for width in WIDTHS:
        if width < min_width:
            continue
        x = y = shelf_height = used_width = 0
        placements = {}
        valid = True
        for index, _bounds, frame_width, frame_height in rows:
            if x and x + frame_width > width:
                y += shelf_height
                x = 0
                shelf_height = 0
            if y + frame_height > MAX_SIDE:
                valid = False
                break
            placements[index] = (x, y)
            x += frame_width
            shelf_height = max(shelf_height, frame_height)
            used_width = max(used_width, x)
        if not valid:
            continue
        used_height = y + shelf_height
        if used_width > MAX_SIDE or used_height > MAX_SIDE:
            continue
        candidate = (used_width * used_height, max(used_width, used_height), used_width, used_height, placements)
        if best is None or candidate[:4] < best[:4]:
            best = candidate
    if best is None:
        return None
    return best[2], best[3], best[4]


def _pack_single(frames: list, rows: list, geometry):
    width, height, placements = geometry
    records = _empty_records(frames)
    if not rows:
        return [], records
    page = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for index, bounds, frame_width, frame_height in rows:
        x, y = placements[index]
        page.paste(frames[index].image.crop(bounds), (x, y))
        records[index]["page"] = 0
        records[index]["rect"] = [x, y, frame_width, frame_height]
    return [page], records


def _pack_paged(frames: list, rows: list):
    records = _empty_records(frames)
    if not rows:
        return [], records
    rectangles = [(index, width, height) for index, _bounds, width, height in rows]
    placements, bounds = pack_atomic_rectangles(rectangles, MAX_SIDE)
    pages = [Image.new("RGBA", size, (0, 0, 0, 0)) for size in bounds]
    by_id = {index: (crop, width, height) for index, crop, width, height in rows}
    for index, (page_id, x, y) in placements.items():
        crop, width, height = by_id[index]
        pages[page_id].paste(frames[index].image.crop(crop), (x, y))
        records[index]["page"] = page_id
        records[index]["rect"] = [x, y, width, height]
    return pages, records


def _verify_packed_frames(frames: list, pages: list, records: list) -> None:
    if len(frames) != len(records):
        raise ValueError("source frames missing from atlas")
    for original, record in zip(frames, records):
        bounds = original.image.getbbox()
        if list(record["crop"]) != list(bounds or (0, 0, 0, 0)):
            raise ValueError("atlas crop differs from source bounds")
        if list(record["size"]) != list(original.image.size):
            raise ValueError("atlas source size differs from SPR")
        origin = record.get("origin", record.get("raw_origin"))
        if not isinstance(origin, (list, tuple)) or list(origin) != [original.origin_x, original.origin_y]:
            raise ValueError("atlas source pivot differs from SPR")
        page_id = int(record["page"])
        if page_id < 0:
            if bounds is not None:
                raise ValueError("nonempty source frame was discarded")
            continue
        if page_id >= len(pages):
            raise ValueError("frame page index out of bounds")
        x, y, width, height = map(int, record["rect"])
        page = pages[page_id]
        if min(x, y) < 0 or min(width, height) <= 0 or x + width > page.width or y + height > page.height:
            raise ValueError("atlas frame region out of bounds")
        actual = page.crop((x, y, x + width, y + height))
        expected = original.image.crop(tuple(record["crop"]))
        if actual.tobytes() != expected.tobytes():
            raise ValueError("atlas frame pixels differ from original SPR")


def build_group(root: Path, output: Path, key: Path, paths: list[Path], force: bool = False) -> dict:
    folder = output / key
    manifest_path = folder / "atlas.json"
    source_digest, source_details = _source_fingerprint(root, paths)
    old = _previous_manifest(manifest_path)
    cached = not force and _is_cached(folder, old, source_digest)
    if cached and old.get("decoder_sha256") == DECODER_SHA256 and old.get("metadata_sha256"):
        return {
            "group": key.as_posix(),
            "status": "cached",
            "pages": len(old["pages"]),
            "frames": len(old["frames"]),
            "rgba_bytes": int(old["rgba_bytes"]),
            "png_bytes": int(old["png_bytes"]),
        }

    frames = []
    frame_sources = []
    for source, details in zip(paths, source_details):
        try:
            decoded = decode_spr(source.read_bytes())
        except (ValueError, struct.error) as exc:
            raise ValueError(f'{details["path"]} (SHA256 {details["sha256"]}): {exc}') from exc
        details["frames"] = len(decoded)
        for source_frame, frame in enumerate(decoded):
            frames.append(frame)
            frame_sources.append((details["path"], source_frame))

    if cached:
        # Upgrade a valid old Dense cache without repacking it. This is both
        # faster for a full archive migration and protects against silently
        # accepting changed pivots/crops/frames.
        try:
            with ExitStack() as stack:
                cached_pages = [
                    stack.enter_context(Image.open(folder / str(name)))
                    for name in old["pages"]
                ]
                _verify_packed_frames(frames, cached_pages, old["frames"])
            old["decoder_sha256"] = DECODER_SHA256
            old["metadata_sha256"] = _metadata_digest(old)
            temporary = manifest_path.with_name("atlas.json.tmp")
            temporary.write_text(
                json.dumps(old, ensure_ascii=False, separators=(",", ":")) + "\n",
                encoding="utf-8",
            )
            temporary.replace(manifest_path)
            return {
                "group": key.as_posix(),
                "status": "revalidated",
                "pages": len(old["pages"]),
                "frames": len(frames),
                "rgba_bytes": int(old["rgba_bytes"]),
                "png_bytes": int(old["png_bytes"]),
            }
        except (OSError, ValueError, KeyError, TypeError):
            pass

    rows = _visible_rows(frames)
    geometry = _single_page_geometry(rows)
    if geometry is not None:
        pages, records = _pack_single(frames, rows, geometry)
        strategy = "height_dense_single"
    else:
        pages, records = _pack_paged(frames, rows)
        strategy = "paged_maxrects_fallback"
    _verify_packed_frames(frames, pages, records)

    folder.mkdir(parents=True, exist_ok=True)
    names = []
    for index, page in enumerate(pages):
        name = f"page_{index}.png"
        temporary = folder / (name + ".tmp")
        page.save(temporary, format="PNG", compress_level=3)
        temporary.replace(folder / name)
        names.append(name)

    metadata = []
    for record, (relative, source_frame) in zip(records, frame_sources):
        metadata.append(
            {
                "page": int(record["page"]),
                "rect": list(record["rect"]),
                "crop": list(record["crop"]),
                "size": list(record["size"]),
                "origin": list(record["raw_origin"]),
                "source": relative,
                "source_frame": source_frame,
            }
        )

    rgba_bytes = sum(page.width * page.height * 4 for page in pages)
    png_bytes = sum((folder / name).stat().st_size for name in names)
    page_sizes = [list(page.size) for page in pages]
    atlas_side = max((max(size) for size in page_sizes), default=0)
    candidate = {
        "valid": True,
        "side": atlas_side,
        "pages": len(names),
        "rgba_bytes": rgba_bytes,
        "strategy": strategy,
    }
    document = {
        "version": MANIFEST_VERSION,
        "packing_policy": POLICY,
        "packing_strategy": strategy,
        "group": key.as_posix(),
        "source_digest": source_digest,
        "decoder_sha256": DECODER_SHA256,
        "sources": source_details,
        "atlas_side": atlas_side,
        "pages": names,
        "page_sizes": page_sizes,
        "page_sha256": {name: _sha256(folder / name) for name in names},
        "frames": metadata,
        "candidates": [candidate],
        "rgba_bytes": rgba_bytes,
        "png_bytes": png_bytes,
    }
    document["metadata_sha256"] = _metadata_digest(document)
    temporary_manifest = folder / "atlas.json.tmp"
    temporary_manifest.write_text(
        json.dumps(document, ensure_ascii=False, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    temporary_manifest.replace(manifest_path)

    for stale in set(old.get("pages", [])).difference(names):
        if Path(str(stale)).name == str(stale):
            (folder / str(stale)).unlink(missing_ok=True)

    return {
        "group": key.as_posix(),
        "status": "converted",
        "pages": len(names),
        "frames": len(frames),
        "rgba_bytes": rgba_bytes,
        "png_bytes": png_bytes,
        "side": atlas_side,
        "packing_strategy": strategy,
    }
