import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_action_page_locality import audit_catalog


class LocalityTests(unittest.TestCase):
    def catalog(self, pages):
        return {
            "quarantined_groups": 0,
            "groups": [{
                "enemy": "EN001", "pages": ["one.png", "two.png"],
                "actions": [{"key": "S", "directions": [
                    {"facing": 1, "source_direction": 1,
                     "frames": [{"page": p} for p in pages]},
                ]}],
            }],
        }

    def test_single_page(self):
        result = audit_catalog(self.catalog([0, 0, 0]))
        self.assertTrue(result["locality_verified"])
        self.assertEqual(result["single_page"], 1)

    def test_split_page(self):
        result = audit_catalog(self.catalog([0, 1, 0]))
        self.assertFalse(result["locality_verified"])
        self.assertEqual(result["split_across_pages"], 1)
        self.assertEqual(result["split_details"][0]["pages"], [0, 1])

    def test_invalid_page(self):
        with self.assertRaises(ValueError):
            audit_catalog(self.catalog([2]))

    def test_quarantined_group(self):
        catalog = self.catalog([0])
        catalog["quarantined_groups"] = 1
        with self.assertRaises(ValueError):
            audit_catalog(catalog)


if __name__ == "__main__":
    unittest.main()
