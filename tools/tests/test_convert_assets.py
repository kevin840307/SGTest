import json
import struct
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from convert_assets import Frame, convert, convert_act, convert_draw_order, decode_spr, pack


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
        self.assertEqual(pages[0].size, (5, 5))
        self.assertEqual(records[0]["raw_origin"], [-3, 7])
        self.assertEqual(records[0]["crop"], [0, 0, 1, 1])
        self.assertEqual(records[0]["rect"], [2, 2, 1, 1])
        self.assertEqual(pages[0].getpixel((0, 0)), (255, 0, 0, 255))

    def test_atlas_padding_keeps_adjacent_colors_separate(self):
        from PIL import Image
        red = Frame(Image.new("RGBA", (1, 1), (255, 0, 0, 255)), 0, 0)
        blue = Frame(Image.new("RGBA", (1, 1), (0, 0, 255, 255)), 0, 0)
        pages, records = pack([red, blue], 16)
        self.assertEqual(len(pages), 1)
        self.assertEqual(records[0]["rect"], [2, 2, 1, 1])
        self.assertEqual(records[1]["rect"], [7, 2, 1, 1])
        self.assertEqual(pages[0].getpixel((4, 2)), (255, 0, 0, 255))
        self.assertEqual(pages[0].getpixel((5, 2)), (0, 0, 255, 255))

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
            dotted = root / "Enemy" / "test.variant.spr"
            dotted.write_bytes(one_frame())
            convert(dotted, root, out, 1024)
            self.assertTrue((out / "Enemy" / "test.variant.json").exists())

    def test_reject_bad_frame_table(self):
        with self.assertRaises(ValueError):
            decode_spr(one_frame()[:-4] + struct.pack("<I", 4000000))

    def test_act_exports_verified_default_timing_and_raw_values(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            out = Path(temp) / "out"
            (root / "Enemy").mkdir(parents=True)
            source = root / "Enemy" / "test.ACT"
            source.write_bytes(b"\x01\x02\x00\x00\x00S\x00\x03\x01\x00\x00\x00"
                               b"\x01\x02\x01\x00\x00\x00" + struct.pack("<II", 0, 6) + b"\xff")
            self.assertEqual(convert_act(source, root, out), "converted")
            exported = json.loads((out / "Enemy" / "test.act.json").read_text())
            self.assertEqual(exported["version"], 4)
            self.assertEqual(exported["legacy_fixed_update_hz"], 50)
            self.assertEqual(exported["legacy_fixed_update_ms"], 20)
            self.assertTrue(exported["runtime_speed_can_override"])
            frame = exported["actions"][0]["frames"][0]
            self.assertEqual(frame["raw_time"], 6)
            self.assertEqual(frame["legacy_default_update_calls"], 9)
            self.assertEqual(frame["legacy_reference_default_duration_ms"], 180)

    def test_draw_order_preserves_layer_names_and_suffix(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            out = Path(temp) / "out"
            (root / "Player").mkdir(parents=True)
            source = root / "Player" / "CharDrawOrder.dat"
            source.write_bytes(b"NS1 : Back>leg+2>Body+2>Head\r\n")
            self.assertEqual(convert_draw_order(source, root, out), "converted")
            exported = json.loads((out / "Player" / "CharDrawOrder.draw_order.json").read_text())
            self.assertEqual(exported["rows"][0]["raw_layers"], ["Back", "leg+2", "Body+2", "Head"])
            self.assertIsNone(exported["layer_suffix_semantics"])


if __name__ == "__main__":
    unittest.main()
