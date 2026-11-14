import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_character_standard import read_action_names, read_draw_order_keys


class CharacterStandardAuditTests(unittest.TestCase):
    def test_action_names(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "CharActionName.dat"
            path.write_text("NS,NR,NU,NB,AS,AH,AA,AB,AG,GS,GH,GA,GB,SS,", encoding="ascii")
            names = read_action_names(path)
            self.assertEqual(names[:4], ["NS", "NR", "NU", "NB"])
            self.assertEqual(len(names), 14)

    def test_draw_order_rejects_duplicate_key(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "draw.dat"
            path.write_text("NS1: Body\nNS1: Head\n", encoding="ascii")
            with self.assertRaises(ValueError):
                read_draw_order_keys(path)


if __name__ == "__main__":
    unittest.main()
