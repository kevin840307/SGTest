#!/usr/bin/env python3
"""Fail-closed, read-only XN SPR frame decoder, with explicit unverified visual semantics.

Evidence: Paradise.exe.lst xnSpr::LoadSpr at 00552FC0, DecodeFrameLine at
00553EB0. The 16-bit colors are interpreted provisionally as RGB565;
independent original-client visual comparison is still required.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path


class FormatError(ValueError):
    """Untrusted SPR bytes violate a supported structural invariant."""


@dataclass(frozen=True)
class Frame:
    width: int
    height: int
    anchor_x: int
    anchor_y: int
    rgba: bytes
    frame_offset: int


def u32(data: bytes, pos: int) -> int:
    if pos < 0 or pos + 4 > len(data):
        raise FormatError(f"u32 outside data at {pos}")
    return struct.unpack_from("<I", data, pos)[0]


def i32(data: bytes, pos: int) -> int:
    if pos < 0 or pos + 4 > len(data):
        raise FormatError(f"i32 outside data at {pos}")
    return struct.unpack_from("<i", data, pos)[0]


def u16(data: bytes, pos: int, limit: int) -> int:
    if pos < 0 or pos + 2 > limit:
        raise FormatError(f"u16 outside row at {pos}")
    return struct.unpack_from("<H", data, pos)[0]


def rgb565(pixel: int) -> tuple[int, int, int]:
    r, g, b = (pixel >> 11) & 31, (pixel >> 5) & 63, pixel & 31
    return (r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2)


def parse_spr(data: bytes) -> list[Frame]:
    """Decode only the legacy XN footer-offset, 16-bit row-run SPR variant."""
    if len(data) < 28:
        raise FormatError("too short for an XN SPR")
    count = u32(data, len(data) - 4)
    if not 1 <= count <= 2048:
        raise FormatError("invalid footer frame count")
    table = len(data) - 4 - 4 * count
    if table < 20:
        raise FormatError("invalid footer offset table")
    offsets = [u32(data, table + i * 4) for i in range(count)]
    if offsets[0] != 0 or offsets != sorted(set(offsets)):
        raise FormatError("invalid/duplicate frame offsets")
    frames = []
    for index, start in enumerate(offsets):
        end = offsets[index + 1] if index + 1 < count else table
        if end > table or start + 16 > end:
            raise FormatError(f"frame {index}: invalid extent")
        width, height = u32(data, start), u32(data, start + 4)
        anchor_x, anchor_y = i32(data, start + 8), i32(data, start + 12)
        if not (1 <= width <= 8192 and 1 <= height <= 8192 and width * height <= 16_000_000):
            raise FormatError(f"frame {index}: excessive/invalid dimensions")
        header_end = start + 16 + height * 4
        if header_end > end:
            raise FormatError(f"frame {index}: row table exceeds extent")
        rows = [start + u32(data, start + 16 + y * 4) for y in range(height)]
        if rows[0] != header_end or rows != sorted(set(rows)) or rows[-1] >= end:
            raise FormatError(f"frame {index}: malformed row offsets")
        pixels = bytearray(width * height * 4)
        for y, pos in enumerate(rows):
            limit = rows[y + 1] if y + 1 < height else end
            x = 0
            while True:
                cmd = u16(data, pos, limit)
                pos += 2
                tag, length = cmd & 0xC000, cmd & 0x3FFF
                if tag == 0x8000:
                    if length == 0:
                        break
                    x += length
                    if x > width:
                        raise FormatError(f"frame {index} row {y}: skip exceeds width")
                    continue
                if tag == 0xC000 or length == 0 or x + length > width:
                    raise FormatError(f"frame {index} row {y}: unsupported/oversized run")
                alpha = 255
                if tag == 0x4000:
                    alpha = u16(data, pos, limit)
                    pos += 2
                    if alpha > 255:
                        raise FormatError(f"frame {index} row {y}: alpha >255")
                if pos + length * 2 > limit:
                    raise FormatError(f"frame {index} row {y}: pixel data truncated")
                for offset in range(length):
                    rgb = rgb565(u16(data, pos, limit))
                    pos += 2
                    pixel_at = (y * width + x + offset) * 4
                    pixels[pixel_at:pixel_at + 4] = bytes((*rgb, alpha))
                x += length
            if pos != limit:
                raise FormatError(f"frame {index} row {y}: trailing unknown bytes")
        frames.append(Frame(width, height, anchor_x, anchor_y, bytes(pixels), start))
    return frames


def png_rgba(width: int, height: int, rgba: bytes) -> bytes:
    if len(rgba) != width * height * 4:
        raise ValueError("invalid RGBA length")
    scanlines = b"".join(b"\x00" + rgba[y * width * 4:(y + 1) * width * 4]
                         for y in range(height))
    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(scanlines, level=9)) + chunk(b"IEND", b""))


def export_spr(source: Path, output: Path) -> dict:
    src = source.resolve(strict=True)
    dest = output.resolve()
    if dest == src.parent or dest.is_relative_to(src.parent):
        raise ValueError("output must be outside the original source directory")
    data = src.read_bytes()
    frames = parse_spr(data)  # parse before creating any outputs
    if dest.exists() and any(dest.iterdir()):
        raise FileExistsError("output directory must be empty (no implicit overwrites)")
    records = []
    for i, frame in enumerate(frames):
        filename = f"frame_{i:04d}.png"
        records.append({
            "frame_index": i, "png": filename, "frame_offset_bytes": frame.frame_offset,
            "width": frame.width, "height": frame.height,
            "raw_anchor_x": frame.anchor_x, "raw_anchor_y": frame.anchor_y,
            "anchor_coordinate_semantics": "UNVERIFIED",
            "direction": None, "action": None, "duration_ms": None,
            "offset_x": None, "offset_y": None, "z_order": None,
            "events": None,
        })
    metadata = {
        "schema_version": 1, "source_filename": src.name,
        "source_sha256": hashlib.sha256(data).hexdigest(),
        "decoder_variant": "XN_SPR_FOOTER_ROW_RUNS",
        "structure_evidence": "DIRECT: Paradise.exe.lst 0x00552FC0 / 0x00553EB0",
        "color_interpretation": "PROVISIONAL_RGB565_NOT_VISUALLY_VERIFIED",
        "alpha_interpretation": "8-bit source run alpha; global opacity not applied",
        "animation_verified": False, "frame_count": len(frames), "frames": records,
    }
    dest.mkdir(parents=True, exist_ok=True)
    for i, frame in enumerate(frames):
        (dest / records[i]["png"]).write_bytes(png_rgba(frame.width, frame.height, frame.rgba))
    (dest / "animation.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return metadata


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    metadata = export_spr(args.input, args.output)
    print(f"Exported {metadata['frame_count']} structural frame(s). Visual/animation parity NOT VERIFIED.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
