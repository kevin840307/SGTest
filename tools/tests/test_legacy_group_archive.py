import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from legacy_group_archive import (GroupArchive, lzss_decompress, probe_archive_footer,
                                  safe_output_path)
from scan_group_archives import scan_archives


def literal_lzss(data: bytes) -> bytes:
    out = bytearray()
    for start in range(0, len(data), 8):
        block = data[start:start + 8]
        out.append((1 << len(block)) - 1)
        out.extend(block)
    return bytes(out)


def make_archive(path: Path) -> None:
    raw = b"XYZ"
    compressed_payload = literal_lzss(b"ABC")
    index = bytearray()
    index += struct.pack("<I", 1)
    index += struct.pack("<I", 2) + b"Enemy\0"
    index += struct.pack("<III", len(raw), 0, 0) + b"A.bin\0"
    index += struct.pack("<III", 3, len(compressed_payload), len(raw)) + b"B.bin\0"
    compressed_index = literal_lzss(bytes(index))
    index_offset = len(raw) + len(compressed_payload)
    path.write_bytes(
        raw + compressed_payload
        + struct.pack("<II", len(index), len(compressed_index))
        + compressed_index
        + struct.pack("<I", index_offset)
    )


class ArchiveTests(unittest.TestCase):
    def test_literal_lzss(self):
        self.assertEqual(lzss_decompress(literal_lzss(b"hello world"), 11), b"hello world")

    def test_archive_index_and_extract(self):
        with tempfile.TemporaryDirectory() as temp:
            archive_path = Path(temp) / "Data1.dat"
            make_archive(archive_path)
            archive = GroupArchive(archive_path)
            self.assertEqual([e.path for e in archive.entries], ["Enemy/A.bin", "Enemy/B.bin"])
            self.assertEqual(archive.read_entry(archive.entries[0]), b"XYZ")
            self.assertEqual(archive.read_entry(archive.entries[1]), b"ABC")
            self.assertEqual([e.path for e in archive.find("enemy/*.BIN")],
                             ["Enemy/A.bin", "Enemy/B.bin"])

    def test_footer_probe_and_full_tree_scan(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive_path = root / "Data1.dat"
            make_archive(archive_path)
            (root / "plain.dat").write_bytes(b"not an archive footer")
            footer = probe_archive_footer(archive_path)
            self.assertIsNotNone(footer)
            self.assertEqual(footer["file_size"], archive_path.stat().st_size)
            self.assertIsNone(probe_archive_footer(root / "plain.dat"))
            report = scan_archives(root)
            self.assertEqual(report["scanned_files"], 2)
            self.assertEqual(report["footer_candidate_count"], 1)
            self.assertEqual(report["valid_archive_count"], 1)
            self.assertEqual(report["rejected_candidate_count"], 0)
            self.assertEqual(report["valid_archives"][0]["path"], "Data1.dat")
            self.assertEqual(report["valid_archives"][0]["entry_count"], 2)

    def test_safe_output_rejects_parent(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                safe_output_path(Path(temp), "../evil.bin")


if __name__ == "__main__":
    unittest.main()
