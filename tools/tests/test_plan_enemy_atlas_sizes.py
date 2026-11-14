import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from plan_enemy_atlas_sizes import make_plan


class PlanTests(unittest.TestCase):
    def test_memory_priority_and_missing_candidates(self):
        rows = [{"side": side, "rgba_area_bytes": area, "pages": pages}
                for side, area, pages in [(1024, 100, 4), (2048, 90, 2), (4096, 110, 1)]]
        report = {"headless": True, "groups": [{"enemy": "EN001", "candidates": rows}]}
        self.assertEqual(make_plan(report)["groups"]["Enemy/EN001"]["side"], 2048)
        self.assertEqual(make_plan(report, 0.25)["groups"]["Enemy/EN001"]["side"], 4096)
        rows.pop()
        with self.assertRaises(ValueError):
            make_plan(report)
