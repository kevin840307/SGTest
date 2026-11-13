import json
import struct
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from convert_assets import convert, convert_act, decode_spr, pack


def one_frame() -> bytes:
    # 2x1: opaque red followed by one transparent pixel; raw origin (-3, 7).
    pixels = struct.pack("<HHHH", 1, 0xF800, 0x8001, 0x8000)
    image = struct.pack("<IIiiI", 2, 1, -3, 7, 20) + pixels
    return image + struct.pack("<II", 0, 1)


class ConversionTests(unittest.TestCase):
    def test_rgba_origin_and_atlas_crop(self):
        frames = decode_spr(one_frame())
        self.assertEqual(len(frames), 1)
        self.assertEqual(frames[0].image.getpixel((0, 0)), (255, 0, 0, 255))
        self.assertEqual(frames[0].image.getpixel((1, 0)), (0, 0, 0, 0))
        pages, records = pack(frames, 1024)
        self.assertEqual(pages[0].size, (1, 1))
        self.assertEqual(records[0]["raw_origin"], [-3, 7])
        self.assertEqual(records[0]["crop"], [0, 0, 1, 1])

    def test_reproducible_output_and_cache(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "input"
            out = Path(temp) / "output"
            (root / "Enemy").mkdir(parents=True)
            source = root / "Enemy" / "test.spr"
            source.write_bytes(one_frame())
            result = convert(source, root, out, 1024)
            self.assertEqual(result["status"], "converted")
            manifest = json.loads((out / "Enemy" / "test.json").read_text())
            self.assertEqual(manifest["layout"], "frames")
            self.assertIsNone(manifest["frame_duration_ms"])
            self.assertEqual(convert(source, root, out, 1024)["status"], "cached")
            self.assertEqual(convert(source, root, out, 1024, layout="atlas")["status"], "converted")

    def test_reject_bad_frame_table(self):
        with self.assertRaises(ValueError):
            decode_spr(one_frame()[:-4] + struct.pack("<I", 4000000))

    def test_act_exports_unknown_timing_units_without_guessing(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            out = Path(temp) / "out"
            (root / "Enemy").mkdir(parents=True)
            source = root / "Enemy" / "test.ACT"
            source.write_bytes(b"\x01\x02\x00\x00\x00S\x00\x03\x01\x00\x00\x00"
                               b"\x01\x02\x01\x00\x00\x00" + struct.pack("<II", 0, 6) + b"\xff")
            self.assertEqual(convert_act(source, root, out), "converted")
            exported = json.loads((out / "Enemy" / "test.act.json").read_text())
            self.assertIsNone(exported["time_unit"])
            self.assertEqual(exported["actions"][0]["frames"][0]["raw_time"], 6)


if __name__ == "__main__":
    unittest.main()
