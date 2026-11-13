"""Synthetic public fixtures; no game-owned assets copied into the repository."""
import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
import sys
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from legacy_spr_decoder import FormatError, export_spr, parse_spr, png_rgba


def make_frame(first_color=0xF800):
    row_a = struct.pack("<HHHHHH", 0x8001, 0x4002, 128, first_color, 0x07E0, 0x8000)
    row_b = struct.pack("<HHHHH", 3, 0x001F, 0xFFFF, 0x0000, 0x8000)
    return struct.pack("<IIiiII", 3, 2, 1, -2, 24, 24 + len(row_a)) + row_a + row_b


def make_spr(frames):
    offsets = []
    data = bytearray()
    for frame in frames:
        offsets.append(len(data))
        data.extend(frame)
    return bytes(data) + struct.pack(f"<{len(offsets)}I", *offsets) + struct.pack("<I", len(frames))


class SprTests(unittest.TestCase):
    def test_decode_alpha_and_rgb565(self):
        f = parse_spr(make_spr([make_frame()]))[0]
        self.assertEqual((f.width, f.height, f.anchor_x, f.anchor_y), (3, 2, 1, -2))
        self.assertEqual(f.rgba[:4], b"\x00\x00\x00\x00")
        self.assertEqual(f.rgba[4:8], b"\xff\x00\x00\x80")
        self.assertEqual(f.rgba[8:12], b"\x00\xff\x00\x80")
        self.assertEqual(f.rgba[12:16], b"\x00\x00\xff\xff")
        self.assertEqual(f.rgba[20:24], b"\x00\x00\x00\xff")

    def test_two_frames_and_export_without_fake_animation(self):
        payload = make_spr([make_frame(), make_frame(0x001F)])
        self.assertEqual(len(parse_spr(payload)), 2)
        with tempfile.TemporaryDirectory() as temp:
            source_dir = Path(temp) / "originals"
            source_dir.mkdir()
            source = source_dir / "dummy.spr"
            source.write_bytes(payload)
            output = Path(temp) / "reports" / "dummy"
            meta = export_spr(source, output)
            self.assertEqual(meta["source_sha256"], hashlib.sha256(payload).hexdigest())
            self.assertEqual(meta["frame_count"], 2)
            self.assertFalse(meta["animation_verified"])
            self.assertIsNone(meta["frames"][0]["duration_ms"])
            self.assertEqual(len(list(output.glob("*.png"))), 2)
            self.assertEqual(json.loads((output / "animation.json").read_text()), meta)
            with self.assertRaises(FileExistsError):
                export_spr(source, output)

    def test_png_round_trip_raw_scanlines(self):
        f = parse_spr(make_spr([make_frame()]))[0]
        png = png_rgba(f.width, f.height, f.rgba)
        self.assertEqual(png[:8], b"\x89PNG\r\n\x1a\n")
        pos = 8
        decoded = b""
        while pos < len(png):
            n = struct.unpack_from(">I", png, pos)[0]
            kind = png[pos + 4:pos + 8]
            body = png[pos + 8:pos + 8 + n]
            if kind == b"IDAT":
                decoded += zlib.decompress(body)
            pos += n + 12
        self.assertEqual(decoded, b"\x00" + f.rgba[:12] + b"\x00" + f.rgba[12:])

    def test_corrupt_footer_and_bad_run_fail_closed(self):
        valid = make_spr([make_frame()])
        with self.assertRaises(FormatError):
            parse_spr(valid[:-4] + struct.pack("<I", 900_000))
        with self.assertRaises(FormatError):
            parse_spr(valid[:-1])
        bad = bytearray(valid)
        struct.pack_into("<H", bad, 24, 0xC001)
        with self.assertRaises(FormatError):
            parse_spr(bytes(bad))

    def test_reject_writing_inside_original_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "game.spr"
            source.write_bytes(make_spr([make_frame()]))
            with self.assertRaises(ValueError):
                export_spr(source, Path(temp) / "export")


if __name__ == "__main__":
    unittest.main()
