"""Deterministic Adaptive Atomic Atlas packing for offline game assets.

One source SPR is the smallest physical placement block. All emitted frame
pixels and coordinates are checked before a manifest is published. The Godot
Runtime never needs to know which atlas side was chosen.
"""

from __future__ import annotations

import hashlib
import json
import struct
from contextlib import ExitStack
from pathlib import Path

from .selector import choose_candidate
from .build_atlas_groups import measure_source_blocks, pack_source_blocks
from ..source.convert_assets import decode_spr
from ..source import runtime_spr

ATLAS_SIDES = (512, 1024, 2048, 4096)
POLICY = "adaptive_atomic_atlas_v1"
MANIFEST_VERSION = 2
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
        return parsed if isinstance(parsed,dict) else {}
    except (OSError, ValueError, TypeError):
        return {}


def _metadata_digest(data: dict) -> str:
    fields = {key:value for key,value in data.items() if key != 'metadata_sha256'}
    return hashlib.sha256(json.dumps(fields,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest()


def _is_cached(folder: Path, old: dict, source_digest: str) -> bool:
    if (
        old.get("version") != MANIFEST_VERSION
        or old.get("packing_policy") != POLICY
        or old.get("source_digest") != source_digest
    ):
        return False
    if old.get('metadata_sha256') is not None and old['metadata_sha256'] != _metadata_digest(old):
        return False

    page_names = old.get("pages", [])
    expected_hashes = old.get("page_sha256", {})
    if not isinstance(page_names, list) or not isinstance(expected_hashes, dict):
        return False
    if not page_names:
        return all(record.get("page") == -1 for record in old.get("frames", []))

    for name in page_names:
        path = folder / name
        if (
            Path(name).name != name
            or not path.is_file()
            or _sha256(path) != expected_hashes.get(name)
        ):
            return False
    return True


def _measure_candidates(frames: list, ranges: list) -> list[dict]:
    candidates: list[dict] = []

    # One-frame icons cannot benefit from repacking at multiple side lengths.
    # Compute their actual cropped dimensions instead of allocating four pages.
    if len(frames) == 1:
        bounds = frames[0].image.getbbox()
        required_side = 0
        required_bytes = 0
        if bounds:
            width = bounds[2] - bounds[0] + 4
            height = bounds[3] - bounds[1] + 4
            required_side = max(width, height)
            required_bytes = width * height * 4
        for side in ATLAS_SIDES:
            if side >= required_side:
                candidates.append(
                    {
                        "valid": True,
                        "side": side,
                        "pages": 1 if bounds else 0,
                        "rgba_bytes": required_bytes,
                    }
                )
            else:
                candidates.append(
                    {
                        "valid": False,
                        "side": side,
                        "reason": "single frame exceeds side",
                    }
                )
        return candidates

    for side in ATLAS_SIDES:
        try:
            candidates.append(measure_source_blocks(frames, ranges, side))
        except ValueError as exc:
            candidates.append({"valid": False, "side": side, "reason": str(exc)})

    return candidates


def _verify_packed_frames(frames: list, pages: list, records: list) -> None:
    if len(frames) != len(records):
        raise ValueError("source frames missing from atlas")

    for original, record in zip(frames, records):
        bounds = original.image.getbbox()
        if list(record['crop']) != list(bounds or (0,0,0,0)):
            raise ValueError('atlas crop differs from source bounds')
        if list(record['size']) != list(original.image.size):
            raise ValueError('atlas source size differs from SPR')
        origin = record.get('origin',record.get('raw_origin'))
        if list(origin) != [original.origin_x,original.origin_y]:
            raise ValueError('atlas source pivot differs from SPR')
        page_id = int(record["page"])
        if page_id < 0:
            if original.image.getbbox() is not None:
                raise ValueError("nonempty source frame was discarded")
            continue

        if page_id >= len(pages):
            raise ValueError("frame page index out of bounds")
        x, y, width, height = map(int, record["rect"])
        page = pages[page_id]
        if (
            x < 0
            or y < 0
            or width <= 0
            or height <= 0
            or x + width > page.width
            or y + height > page.height
        ):
            raise ValueError("atlas frame region out of bounds")

        actual = page.crop((x, y, x + width, y + height))
        expected = original.image.crop(tuple(record["crop"]))
        if actual.tobytes() != expected.tobytes():
            raise ValueError("atlas frame pixels differ from original SPR")


def build_group(
    root: Path,
    output: Path,
    key: Path,
    paths: list[Path],
    force: bool = False,
) -> dict:
    """Build one appearance group with incremental cache and exact pixel checks."""
    folder = output / key
    manifest_path = folder / "atlas.json"
    source_digest, source_details = _source_fingerprint(root, paths)
    old = _previous_manifest(manifest_path)

    cached = not force and _is_cached(folder, old, source_digest)
    if cached and old.get('decoder_sha256') == DECODER_SHA256 and old.get('metadata_sha256'):
        return {
            "group": key.as_posix(),
            "status": "cached",
            "pages": len(old["pages"]),
            "frames": len(old["frames"]),
            "rgba_bytes": old["rgba_bytes"],
            "png_bytes": old["png_bytes"],
        }

    frames = []
    source_ranges = []
    frame_sources = []
    for source, details in zip(paths, source_details):
        try:
            decoded = decode_spr(source.read_bytes())
        except (ValueError,struct.error) as exc:
            raise ValueError(f'{details["path"]} (SHA256 {details["sha256"]}): {exc}') from exc
        details["frames"] = len(decoded)
        source_ranges.append((len(frames), len(decoded)))
        for index, frame in enumerate(decoded):
            frames.append(frame)
            frame_sources.append((details["path"], index))

    if cached:
        # Migrate old caches by decoding/revalidating pixels and pivots once;
        # do not repack thousands of already-correct pages just for a decoder stamp.
        from PIL import Image
        try:
            with ExitStack() as stack:
                pages = [stack.enter_context(Image.open(folder/name)) for name in old['pages']]
                _verify_packed_frames(frames,pages,old['frames'])
            old['decoder_sha256'] = DECODER_SHA256
            old['metadata_sha256'] = _metadata_digest(old)
            temporary = manifest_path.with_name('atlas.json.tmp')
            temporary.write_text(json.dumps(old,ensure_ascii=False,separators=(',',':'))+'\n',encoding='utf-8')
            temporary.replace(manifest_path)
            return {'group':key.as_posix(),'status':'revalidated','pages':len(old['pages']),
                    'frames':len(frames),'rgba_bytes':old['rgba_bytes'],'png_bytes':old['png_bytes']}
        except (OSError,ValueError,KeyError,TypeError):
            pass  # Current source remains valid; rebuild a stale/invalid cache.

    candidates = _measure_candidates(frames, source_ranges)
    selected = choose_candidate(candidates)
    pages, records = pack_source_blocks(frames, source_ranges, int(selected["side"]))
    if len(pages) != selected['pages'] or sum(page.width*page.height*4 for page in pages) != selected['rgba_bytes']:
        raise ValueError('measured atlas geometry differs from actual packed pages')
    _verify_packed_frames(frames, pages, records)

    folder.mkdir(parents=True, exist_ok=True)
    names = []
    for index, page in enumerate(pages):
        name = f"page_{index}.png"
        destination = folder / name
        temporary = folder / (name + ".tmp")
        page.save(temporary, format="PNG", compress_level=3)
        temporary.replace(destination)
        names.append(name)

    metadata = []
    for record, (relative, frame_id) in zip(records, frame_sources):
        metadata.append(
            {
                "page": int(record["page"]),
                "rect": list(record["rect"]),
                "crop": list(record["crop"]),
                "size": list(record["size"]),
                "origin": list(record["raw_origin"]),
                "source": relative,
                "source_frame": frame_id,
            }
        )

    rgba_bytes = sum(page.width * page.height * 4 for page in pages)
    png_bytes = sum((folder / name).stat().st_size for name in names)
    document = {
        "version": MANIFEST_VERSION,
        "packing_policy": POLICY,
        "group": key.as_posix(),
        "source_digest": source_digest,
        "decoder_sha256": DECODER_SHA256,
        "sources": source_details,
        "atlas_side": int(selected["side"]),
        "pages": names,
        "page_sizes": [list(page.size) for page in pages],
        "page_sha256": {name: _sha256(folder / name) for name in names},
        "frames": metadata,
        "candidates": candidates,
        "rgba_bytes": rgba_bytes,
        "png_bytes": png_bytes,
    }
    document['metadata_sha256'] = _metadata_digest(document)

    temporary_manifest = folder / "atlas.json.tmp"
    temporary_manifest.write_text(
        json.dumps(document, ensure_ascii=False, separators=(",", ":")) + "\n",
        encoding="utf-8",
    )
    temporary_manifest.replace(manifest_path)

    # Only remove pages previously owned by this group; never touch other assets.
    for stale in set(old.get("pages", [])).difference(names):
        if Path(stale).name == stale:
            (folder / stale).unlink(missing_ok=True)

    return {
        "group": key.as_posix(),
        "status": "converted",
        "pages": len(names),
        "frames": len(frames),
        "rgba_bytes": rgba_bytes,
        "png_bytes": png_bytes,
        "side": int(selected["side"]),
    }
