import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from materialize_legacy_overlay import materialize


def literal_lzss(data: bytes) -> bytes:
    out = bytearray()
    for start in range(0, len(data), 8):
        block = data[start:start + 8]
        out.append((1 << len(block)) - 1)
        out.extend(block)
    return bytes(out)


def make_archive(path: Path) -> None:
    payloads = [(b"base-a", "A.bin"), (b"base-b", "B.bin")]
    body = bytearray()
    entries = []
    for payload, name in payloads:
        offset = len(body)
        body.extend(payload)
        entries.append((len(payload), 0, offset, name))
    index = bytearray(struct.pack("<I", 1))
    index += struct.pack("<I", len(entries)) + b"Enemy\0"
    for raw_size, compressed_size, offset, name in entries:
        index += struct.pack("<III", raw_size, compressed_size, offset)
        index += name.encode("ascii") + b"\0"
    packed = literal_lzss(bytes(index))
    index_offset = len(body)
    path.write_bytes(bytes(body) + struct.pack("<II", len(index), len(packed))
                     + packed + struct.pack("<I", index_offset))


class OverlayTests(unittest.TestCase):
    def test_loose_file_overrides_archive_case_insensitively(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            root.mkdir()
            archive = root / "Data1.dat"
            make_archive(archive)
            enemy = root / "Enemy"
            enemy.mkdir()
            (enemy / "a.BIN").write_bytes(b"override")
            output = Path(temp) / "out"
            result = materialize(root, archive, output, ["Enemy/*.bin"])
            self.assertEqual((result["file_count"], result["loose_override_count"],
                              result["archive_base_count"]), (2, 1, 1))
            self.assertEqual((output / "Enemy/A.bin").read_bytes(), b"override")
            self.assertEqual((output / "Enemy/B.bin").read_bytes(), b"base-b")


if __name__ == "__main__":
    unittest.main()
