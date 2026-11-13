import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from convert_assets import convert_sif, parse_sif


class SifTests(unittest.TestCase):
    def test_record_and_export(self):
        name = b"OBJECT.spr"
        raw = bytes([len(name)]) + name + b"\x01" + struct.pack("<4i", 2, -3, 4, 5)
        self.assertEqual(parse_sif(raw), {"spr_name": "OBJECT.spr", "raw_flag": 1,
                                         "raw_values": [2, -3, 4, 5],
                                         "coordinate_semantics": None})
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "client" / "OBJECT.sif"
            source.parent.mkdir()
            source.write_bytes(raw)
            self.assertEqual(convert_sif(source, source.parent, root / "out"), "converted")
            output = json.loads((root / "out" / "OBJECT.sif.json").read_text())
            self.assertEqual(output["raw_values"], [2, -3, 4, 5])
            self.assertEqual(convert_sif(source, source.parent, root / "out"), "cached")

    def test_bad_record_rejected(self):
        with self.assertRaises(ValueError):
            parse_sif(b"\x02no\x01")


if __name__ == "__main__":
    unittest.main()
