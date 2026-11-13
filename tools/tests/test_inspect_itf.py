import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_itf import references


class ItfTests(unittest.TestCase):
    def test_reference_extraction_only(self):
        self.assertEqual(references(b"\x0bBG_GEXP.spr\0BG_GEXP.spr B_OK.SPR"),
                         ["B_OK.SPR", "BG_GEXP.spr"])


if __name__ == "__main__":
    unittest.main()
