#!/usr/bin/env python3
"""Convert an original SGO client SPR tree into small Godot atlases.

This converter does not depend on any recovered game's code or generated media.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path

from PIL import Image

KEY = bytes.fromhex("0d6f041707d3044420574d832b7fc59053290d43f197df2063d4b6b52da68824")
MAX_FILE = 512 * 1024 * 1024
MAX_FRAMES = 100_000
MAX_PIXELS = 64_000_000
RGB565 = tuple(bytes((((c >> 11) << 3) | ((c >> 11) >> 2),
                      (((c >> 5) & 63) << 2) | (((c >> 5) & 63) >> 4),
                      ((c & 31) << 3) | ((c & 31) >> 2))) for c in range(65536))


@dataclass
class Frame:
    image: Image.Image
    origin_x: int
    origin_y: int


def _cbc(data: bytes, iv: bytes) -> bytes:
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

    decryptor = Cipher(algorithms.AES(KEY), modes.CBC(iv)).decryptor()
    return decryptor.update(data) + decryptor.finalize()


def unwrap_cde(blob: bytes) -> bytes:
    if len(blob) < 98 or blob[:6] != b"C.D.E.":
        raise ValueError("invalid CDE header")
    header = _cbc(blob[0x12:0x32], bytes(16))
    plain_size, cipher_size = struct.unpack_from("<II", header, 12)
    if not 24 <= plain_size <= MAX_FILE or cipher_size != len(blob) - 0x52 or cipher_size % 16:
        raise ValueError("invalid CDE sizes")
    iv = struct.pack("<IIII", struct.unpack_from("<I", header, 8)[0],
                     struct.unpack_from("<I", header, 4)[0], header[28], header[29])
    packed = _cbc(blob[0x52:], iv)
    inflater = zlib.decompressobj(-15)
    plain = inflater.decompress(packed, plain_size + 1)
    if not inflater.eof or inflater.unconsumed_tail or len(plain) != plain_size:
        raise ValueError("invalid CDE deflate payload")
    tail = inflater.unused_data
    if len(tail) > 15 and (len(tail) > 20 or not tail.startswith(b"\0\0\xff\xff\0")
                           or any(tail[5:])):
        raise ValueError("invalid CDE trailer")
    return plain


def decode_spr(blob: bytes) -> list[Frame]:
    if blob.startswith(b"C.D.E."):
        blob = unwrap_cde(blob)
    if len(blob) < 24:
        raise ValueError("short SPR")
    count = struct.unpack_from("<I", blob, len(blob) - 4)[0]
    table = len(blob) - 4 - 4 * count
    if not 0 < count <= MAX_FRAMES or table < 20:
        raise ValueError("invalid SPR frame count")
    offsets = struct.unpack_from(f"<{count}I", blob, table)
    palette = None
    if table >= 8 and blob[table-4:table] == b'Ver\x08':
        palette_at = struct.unpack_from('<I', blob, table-8)[0]
        if palette_at + 516 != table-8 or struct.unpack_from('<I', blob, palette_at)[0] != 1:
            raise ValueError('unsupported indexed SPR palette chunk')
        palette = struct.unpack_from('<256H', blob, palette_at+4)
    if any(a >= b for a, b in zip(offsets, offsets[1:])) or offsets[-1] >= table:
        raise ValueError("invalid SPR frame offsets")
    frames = []
    for index, start in enumerate(offsets):
        end = offsets[index + 1] if index + 1 < count else table
        if start + 16 > end:
            raise ValueError(f"frame {index}: short header")
        width, height, ox, oy = struct.unpack_from("<IIii", blob, start)
        if not 0 < width <= 16384 or not 0 < height <= 16384 or width * height > MAX_PIXELS:
            raise ValueError(f"frame {index}: invalid dimensions")
        row_table = start + 16 + height * 4
        if row_table > end:
            raise ValueError(f"frame {index}: short row table")
        rows = struct.unpack_from(f"<{height}I", blob, start + 16)
        data_end = start if palette is not None else end
        data_start = 0 if palette is not None else row_table
        if rows[0] < data_start or rows[-1] >= data_end or any(a >= b for a, b in zip(rows, rows[1:])):
            raise ValueError(f"frame {index}: invalid row offsets")
        rgba = bytearray(width * height * 4)
        for y, row_start in enumerate(rows):
            row_end = rows[y + 1] if y + 1 < height else data_end
            p, x, terminated = row_start, 0, False
            while p + 2 <= row_end:
                command = struct.unpack_from("<H", blob, p)[0]
                p += 2
                run = command & 0x3fff
                if command & 0x8000:
                    if command & 0x4000:
                        raise ValueError("invalid RLE flags")
                    if run == 0:
                        terminated = True
                        break
                    x += run
                    if x > width:
                        raise ValueError("RLE skip exceeds row")
                    continue
                if run == 0 or x + run > width:
                    raise ValueError("RLE color exceeds row")
                alpha = 255
                if command & 0x4000:
                    if p + 2 > row_end:
                        raise ValueError("short alpha")
                    alpha = struct.unpack_from("<H", blob, p)[0]
                    p += 2
                    if alpha > 255:
                        raise ValueError("invalid alpha")
                pixel_bytes = 1 if palette is not None else 2
                if p + run * pixel_bytes > row_end:
                    raise ValueError("short RGB565 run")
                at = (y * width + x) * 4
                colors = (tuple(palette[c] for c in blob[p:p+run]) if palette is not None
                          else struct.unpack_from(f"<{run}H", blob, p))
                rgba[at:at + run * 4] = b"".join(
                    RGB565[color] + bytes((alpha,)) for color in colors)
                x += run
                p += run * pixel_bytes
            if not terminated or p != row_end:
                raise ValueError(f"frame {index} row {y}: missing end marker")
        frames.append(Frame(Image.frombytes("RGBA", (width, height), bytes(rgba)), ox, oy))
    return frames


def pack(frames: list[Frame], max_side: int) -> tuple[list[Image.Image], list[dict]]:
    """Deterministic shelf packing; one source file stays in one small texture set."""
    pages: list[Image.Image] = []
    records: list[dict] = []
    page = Image.new("RGBA", (max_side, max_side))
    x = y = shelf_h = 0
    used_w = used_h = 0

    def flush() -> None:
        nonlocal page, x, y, shelf_h, used_w, used_h
        if used_w:
            pages.append(page.crop((0, 0, used_w, used_h)))
        page = Image.new("RGBA", (max_side, max_side))
        x = y = shelf_h = used_w = used_h = 0

    for frame in frames:
        bounds = frame.image.getbbox()
        if bounds is None:
            records.append({"page": -1, "rect": [0, 0, 0, 0],
                            "offset": [frame.origin_x, frame.origin_y],
                            "size": list(frame.image.size)})
            continue
        cropped = frame.image.crop(bounds)
        w, h = cropped.size
        if w > max_side or h > max_side:
            raise ValueError(f"frame {w}x{h} exceeds atlas side {max_side}")
        if x + w > max_side:
            x, y, shelf_h = 0, y + shelf_h + 1, 0
        if y + h > max_side:
            flush()
        page.paste(cropped, (x, y))
        records.append({"page": len(pages), "rect": [x, y, w, h],
                        "offset": [frame.origin_x + bounds[0], frame.origin_y + bounds[1]],
                        "size": list(frame.image.size)})
        used_w, used_h = max(used_w, x + w), max(used_h, y + h)
        x += w + 1
        shelf_h = max(shelf_h, h)
    flush()
    return pages, records


def convert(source: Path, root: Path, dest: Path, side: int, force: bool = False) -> dict:
    relative = source.relative_to(root)
    target = dest / relative.parent / source.stem
    manifest_path = target.with_suffix(".json")
    raw = source.read_bytes()
    if len(raw) > MAX_FILE:
        raise ValueError("source file exceeds safety limit")
    digest = hashlib.sha256(raw).hexdigest()
    if manifest_path.exists() and not force:
        old = json.loads(manifest_path.read_text(encoding="utf-8"))
        if old.get("sha256") == digest and old.get("atlas_side") == side and all(
                (target.parent / p).exists() for p in old.get("pages", [])):
            return {"source": relative.as_posix(), "status": "cached", "frames": len(old["frames"])}
    pages, records = pack(decode_spr(raw), side)
    target.parent.mkdir(parents=True, exist_ok=True)
    names = []
    for index, page in enumerate(pages):
        name = f"{source.stem}_{index}.png"
        path = target.parent / name
        temporary = path.with_name(path.name + ".tmp")
        page.save(temporary, format="PNG", optimize=True)
        temporary.replace(path)
        names.append(name)
    document = {"version": 1, "source": relative.as_posix(), "sha256": digest,
                "atlas_side": side, "pages": names, "frames": records}
    temporary = manifest_path.with_name(manifest_path.name + ".tmp")
    temporary.write_text(json.dumps(document, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    temporary.replace(manifest_path)
    return {"source": relative.as_posix(), "status": "converted", "frames": len(records)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path, help="Original clean client directory")
    parser.add_argument("output", type=Path, help="Godot asset output directory")
    parser.add_argument("--atlas-side", type=int, default=2048, choices=(1024, 2048, 4096))
    parser.add_argument("--category", action="append", help="Convert only these first-level directories")
    parser.add_argument("--limit", type=int, help="Convert first N files, useful for a trial")
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    root = args.client.resolve()
    out = args.output.resolve()
    if not root.is_dir():
        parser.error(f"client directory does not exist: {root}")
    if out == root or root in out.parents:
        parser.error("output must be outside the client directory")
    categories = set(args.category or [])
    sources = sorted((p for p in root.rglob("*") if p.is_file() and p.suffix.lower() == ".spr"
                      and (not categories or p.relative_to(root).parts[0] in categories)),
                     key=lambda p: str(p.relative_to(root)).lower())
    if args.limit is not None:
        sources = sources[:args.limit]
    ok = cached = 0
    errors = []
    for source in sources:
        try:
            result = convert(source, root, out, args.atlas_side, args.force)
            ok += result["status"] == "converted"
            cached += result["status"] == "cached"
        except (OSError, ValueError, struct.error, zlib.error) as exc:
            errors.append({"source": source.relative_to(root).as_posix(), "error": str(exc)})
    print(json.dumps({"scanned": len(sources), "converted": ok, "cached": cached,
                      "failed": len(errors), "errors": errors[:20]}, ensure_ascii=False, indent=2))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
