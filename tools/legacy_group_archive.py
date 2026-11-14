#!/usr/bin/env python3
"""Read legacy SGO xnFile group archives (e.g. Data1.dat) without modifying them.

Format evidence comes from Paradise.exe xnFileOpenGroup/FileData::ReadBuf and
LzssDecompress. Original media must remain local unless redistribution rights
are separately confirmed.
"""
from __future__ import annotations

import argparse
import fnmatch
import hashlib
import json
import struct
from dataclasses import asdict, dataclass
from pathlib import Path

WINDOW = 4096
WINDOW_START = 0xFEE


@dataclass(frozen=True)
class ArchiveEntry:
    folder: str
    filename: str
    uncompressed_size: int
    compressed_size: int
    data_offset: int

    @property
    def path(self) -> str:
        folder = self.folder.replace("\\", "/").strip("/")
        return f"{folder}/{self.filename}" if folder else self.filename


def lzss_decompress(source: bytes, expected_size: int | None = None) -> bytes:
    text = bytearray(b" " * WINDOW)
    ring = WINDOW_START
    flags = 0
    pos = 0
    out = bytearray()
    while pos < len(source):
        flags >>= 1
        if flags & 0x100 == 0:
            flags = source[pos] | 0xFF00
            pos += 1
        if flags & 1:
            if pos >= len(source):
                raise ValueError("truncated LZSS literal")
            value = source[pos]
            pos += 1
            out.append(value)
            text[ring] = value
            ring = (ring + 1) & 0xFFF
        else:
            if pos + 2 > len(source):
                raise ValueError("truncated LZSS reference")
            lo, hi = source[pos], source[pos + 1]
            pos += 2
            ref = lo | ((hi & 0xF0) << 4)
            length = (hi & 0x0F) + 3
            for _ in range(length):
                value = text[ref]
                ref = (ref + 1) & 0xFFF
                out.append(value)
                text[ring] = value
                ring = (ring + 1) & 0xFFF
        if expected_size is not None and len(out) > expected_size:
            raise ValueError("LZSS output exceeds expected size")
    if expected_size is not None and len(out) != expected_size:
        raise ValueError(f"LZSS size mismatch: {len(out)} != {expected_size}")
    return bytes(out)


def _u32(data: bytes, offset: int) -> tuple[int, int]:
    if offset + 4 > len(data):
        raise ValueError("truncated archive index integer")
    return struct.unpack_from("<I", data, offset)[0], offset + 4


def _cstr(data: bytes, offset: int, max_len: int = 4096) -> tuple[str, int]:
    end_limit = min(len(data), offset + max_len + 1)
    end = data.find(b"\0", offset, end_limit)
    if end < 0:
        raise ValueError("unterminated archive index string")
    try:
        value = data[offset:end].decode("ascii")
    except UnicodeDecodeError as exc:
        raise ValueError("archive index string is not ASCII") from exc
    return value, end + 1


def parse_index(data: bytes) -> list[ArchiveEntry]:
    offset = 0
    folder_count, offset = _u32(data, offset)
    if folder_count > 100_000:
        raise ValueError("unreasonable archive folder count")
    entries: list[ArchiveEntry] = []
    for _ in range(folder_count):
        file_count, offset = _u32(data, offset)
        if file_count > 1_000_000:
            raise ValueError("unreasonable archive file count")
        folder, offset = _cstr(data, offset)
        for _ in range(file_count):
            raw_size, offset = _u32(data, offset)
            compressed_size, offset = _u32(data, offset)
            data_offset, offset = _u32(data, offset)
            filename, offset = _cstr(data, offset)
            entries.append(ArchiveEntry(folder, filename, raw_size, compressed_size, data_offset))
    if offset != len(data):
        raise ValueError(f"archive index has {len(data) - offset} trailing bytes")
    return entries


def probe_archive_footer(path: Path) -> dict | None:
    """Cheap fail-closed xnFile group footer prefilter without decompressing media."""
    path = path.resolve()
    try:
        file_size = path.stat().st_size
    except OSError:
        return None
    if file_size < 12:
        return None
    try:
        with path.open("rb") as stream:
            stream.seek(-4, 2)
            footer = stream.read(4)
            if len(footer) != 4:
                return None
            index_offset = struct.unpack("<I", footer)[0]
            if index_offset > file_size - 12:
                return None
            stream.seek(index_offset)
            header = stream.read(8)
            if len(header) != 8:
                return None
            raw_size, compressed_size = struct.unpack("<II", header)
    except OSError:
        return None
    if raw_size < 4 or compressed_size <= 0:
        return None
    if index_offset + 8 + compressed_size != file_size - 4:
        return None
    return {
        "file_size": file_size,
        "index_offset": index_offset,
        "index_raw_size": raw_size,
        "index_compressed_size": compressed_size,
    }


class GroupArchive:
    def __init__(self, path: Path):
        self.path = path.resolve()
        self.file_size = self.path.stat().st_size
        if self.file_size < 12:
            raise ValueError("archive is too short")
        with self.path.open("rb") as stream:
            stream.seek(-4, 2)
            self.index_offset = struct.unpack("<I", stream.read(4))[0]
            if self.index_offset > self.file_size - 12:
                raise ValueError("archive index offset is outside file")
            stream.seek(self.index_offset)
            raw_size, compressed_size = struct.unpack("<II", stream.read(8))
            if self.index_offset + 8 + compressed_size != self.file_size - 4:
                raise ValueError("archive index compressed size does not reach footer")
            compressed = stream.read(compressed_size)
        self.index_raw_size = raw_size
        self.index_compressed_size = compressed_size
        self.entries = parse_index(lzss_decompress(compressed, raw_size))
        self._validate_entries()

    def _validate_entries(self) -> None:
        seen = set()
        for entry in self.entries:
            key = entry.path.casefold()
            if key in seen:
                raise ValueError(f"duplicate archive path: {entry.path}")
            seen.add(key)
            stored = entry.compressed_size or entry.uncompressed_size
            if entry.data_offset + stored > self.index_offset:
                raise ValueError(f"archive entry overlaps index: {entry.path}")

    def find(self, pattern: str) -> list[ArchiveEntry]:
        normalized = pattern.replace("\\", "/").casefold()
        return [entry for entry in self.entries
                if fnmatch.fnmatchcase(entry.path.casefold(), normalized)]

    def read_entry(self, entry: ArchiveEntry) -> bytes:
        stored_size = entry.compressed_size or entry.uncompressed_size
        with self.path.open("rb") as stream:
            stream.seek(entry.data_offset)
            blob = stream.read(stored_size)
        if len(blob) != stored_size:
            raise ValueError(f"short archive entry: {entry.path}")
        if entry.compressed_size:
            return lzss_decompress(blob, entry.uncompressed_size)
        if len(blob) != entry.uncompressed_size:
            raise ValueError(f"raw archive entry size mismatch: {entry.path}")
        return blob


def safe_output_path(root: Path, archive_path: str) -> Path:
    parts = [part for part in archive_path.replace("\\", "/").split("/") if part]
    if not parts or any(part in (".", "..") for part in parts):
        raise ValueError("unsafe archive path")
    target = root.joinpath(*parts).resolve()
    root = root.resolve()
    if root != target and root not in target.parents:
        raise ValueError("archive path escapes output directory")
    return target


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--inventory", type=Path, help="Write metadata-only JSON inventory")
    parser.add_argument("--match", action="append", default=[],
                        help="Case-insensitive glob path, e.g. Enemy/EN009A*")
    parser.add_argument("--extract-dir", type=Path,
                        help="Extract only entries selected by --match")
    parser.add_argument("--hash-archive", action="store_true")
    args = parser.parse_args()

    archive = GroupArchive(args.archive)
    selected = archive.entries
    if args.match:
        by_path = {}
        for pattern in args.match:
            for entry in archive.find(pattern):
                by_path[entry.path.casefold()] = entry
        selected = sorted(by_path.values(), key=lambda e: e.path.casefold())

    metadata = {
        "version": 1,
        "archive": args.archive.name,
        "file_size": archive.file_size,
        "sha256": hashlib.sha256(args.archive.read_bytes()).hexdigest()
                  if args.hash_archive else None,
        "index_offset": archive.index_offset,
        "index_raw_size": archive.index_raw_size,
        "index_compressed_size": archive.index_compressed_size,
        "entry_count": len(archive.entries),
        "selected_count": len(selected),
        "entries": [asdict(entry) | {"path": entry.path} for entry in selected],
    }
    if args.inventory:
        args.inventory.parent.mkdir(parents=True, exist_ok=True)
        args.inventory.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n",
                                  encoding="utf-8")
    if args.extract_dir:
        if not args.match:
            parser.error("--extract-dir requires at least one --match")
        for entry in selected:
            target = safe_output_path(args.extract_dir, entry.path)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(archive.read_entry(entry))
    print(json.dumps({
        "archive": args.archive.name,
        "entries": len(archive.entries),
        "selected": len(selected),
        "index_offset": archive.index_offset,
        "index_raw_size": archive.index_raw_size,
        "index_compressed_size": archive.index_compressed_size,
    }, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
