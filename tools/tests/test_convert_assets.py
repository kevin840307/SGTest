import json
import struct
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.source.convert_assets import convert_act, convert_draw_order, decode_spr


def one_frame() -> bytes:
    # 2x1: opaque red followed by one transparent pixel; raw origin (-3, 7).
    pixels = struct.pack("<HHHH", 1, 0xF800, 0x8001, 0x8000)
    image = struct.pack("<IIiiI", 2, 1, -3, 7, 20) + pixels
    return image + struct.pack("<II", 0, 1)


class ConversionTests(unittest.TestCase):



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
