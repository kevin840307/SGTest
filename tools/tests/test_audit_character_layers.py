import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_character_layers import expand_layers


class LayerTests(unittest.TestCase):
    def test_pair_expansion_preserves_order_and_weapon_aliases(self):
        self.assertEqual(expand_layers(["Back", "leg+2", "BODY+2", "L-Arm+2", "R-Arm+2"]),
                         [11, 13, 12, 10, 9, 6, 5, 8, 7])
        self.assertEqual(expand_layers(["Sword", "L-Gun", "Shield", "R-Gun"]), [14, 14, 15, 15])
        with self.assertRaises(ValueError):
            expand_layers(["Body+3"])
