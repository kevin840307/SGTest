"""Deterministic Adaptive Atomic Atlas packing for offline game assets.

One source SPR is the smallest physical placement block. All emitted frame
pixels and coordinates are checked before a manifest is published. The Godot
Runtime never needs to know which atlas side was chosen.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from .selector import choose_candidate
from .build_atlas_groups import pack_source_blocks
from ..source.convert_assets import decode_spr

ATLAS_SIDES = (512, 1024, 2048, 4096)
POLICY = "adaptive_atomic_atlas_v1"
MANIFEST_VERSION = 2


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
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError, TypeError):
        return {}


def _is_cached(folder: Path, old: dict, source_digest: str) -> bool:
    if (
        old.get("version") != MANIFEST_VERSION
        or old.get("packing_policy") != POLICY
        or old.get("source_digest") != source_digest
    ):
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
            pages, _ = pack_source_blocks(frames, ranges, side)
            candidates.append(
                {
                    "valid": True,
                    "side": side,
                    "pages": len(pages),
                    "rgba_bytes": sum(p.width * p.height * 4 for p in pages),
                }
            )
        except ValueError as exc:
            candidates.append({"valid": False, "side": side, "reason": str(exc)})

    return candidates


def _verify_packed_frames(frames: list, pages: list, records: list) -> None:
    if len(frames) != len(records):
        raise ValueError("source frames missing from atlas")

    for original, record in zip(frames, records):
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

    if not force and _is_cached(folder, old, source_digest):
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
        decoded = decode_spr(source.read_bytes())
        details["frames"] = len(decoded)
        source_ranges.append((len(frames), len(decoded)))
        for index, frame in enumerate(decoded):
            frames.append(frame)
            frame_sources.append((details["path"], index))

    candidates = _measure_candidates(frames, source_ranges)
    selected = choose_candidate(candidates)
    pages, records = pack_source_blocks(frames, source_ranges, int(selected["side"]))
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
