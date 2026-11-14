import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from plan_enemy_atomic_sizes import choose_candidate


class AtomicSizePlanTests(unittest.TestCase):
    def test_chooses_fewer_pages_within_memory_budget(self):
        rows = [
            {"side": 1024, "valid": True, "pages": 3, "rgba_area_bytes": 100},
            {"side": 2048, "valid": True, "pages": 1, "rgba_area_bytes": 104},
            {"side": 4096, "valid": True, "pages": 1, "rgba_area_bytes": 130},
        ]
        self.assertEqual(choose_candidate(rows, 0.05)["side"], 2048)

    def test_keeps_minimum_memory_when_page_reduction_costs_too_much(self):
        rows = [
            {"side": 1024, "valid": True, "pages": 3, "rgba_area_bytes": 100},
            {"side": 2048, "valid": True, "pages": 1, "rgba_area_bytes": 111},
        ]
        self.assertEqual(choose_candidate(rows, 0.05)["side"], 1024)

    def test_ignores_invalid_candidates(self):
        rows = [
            {"side": 512, "valid": False},
            {"side": 1024, "valid": True, "pages": 2, "rgba_area_bytes": 100},
        ]
        self.assertEqual(choose_candidate(rows)["side"], 1024)

    def test_rejects_no_valid_candidate(self):
        with self.assertRaises(ValueError):
            choose_candidate([{"side": 512, "valid": False}])


if __name__ == "__main__":
    unittest.main()
