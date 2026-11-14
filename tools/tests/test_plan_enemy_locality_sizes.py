import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from plan_enemy_locality_sizes import select_locality_side


class LocalitySizePlanTests(unittest.TestCase):
    def test_keeps_baseline_when_one_page(self):
        self.assertEqual(select_locality_side(1024, {1024: 1}), 1024)

    def test_grows_to_smallest_local_side(self):
        self.assertEqual(select_locality_side(1024, {1024: 3, 2048: 1}), 2048)

    def test_never_shrinks_below_measured_baseline(self):
        self.assertEqual(select_locality_side(2048, {1024: 1, 2048: 1}), 2048)

    def test_rejects_when_no_supported_side_works(self):
        with self.assertRaises(ValueError):
            select_locality_side(1024, {1024: 2, 2048: 2, 4096: 2})


if __name__ == "__main__":
    unittest.main()
