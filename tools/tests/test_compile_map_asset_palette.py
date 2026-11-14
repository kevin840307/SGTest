import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from compile_map_asset_palette import compile_palettes


class MapAssetPaletteTests(unittest.TestCase):
    def test_joins_map_visuals_to_catalog_pages(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / "Enemy" / "EN001A"
            folder.mkdir(parents=True)
            Image.new("RGBA", (10, 20)).save(folder / "page_0.png")
            map_report = {"maps": [{
                "map_id": 13, "spawn_count": 4, "visual_kind_count": 1,
                "visuals": [{"sprite": "en001a", "spawn_count": 4}],
            }]}
            catalog = {
                "quarantined_groups": 0,
                "groups": [{"enemy": "EN001A", "pages": ["Enemy/EN001A/page_0.png"]}],
            }
            result = compile_palettes(map_report, catalog, root)
            row = result["maps"][0]
            self.assertTrue(result["complete"])
            self.assertEqual((row["atlas_page_count"], row["estimated_rgba_area_bytes"]),
                             (1, 10 * 20 * 4))

    def test_reports_missing_visual(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            map_report = {"maps": [{
                "map_id": 13, "spawn_count": 1, "visual_kind_count": 1,
                "visuals": [{"sprite": "EN999A", "spawn_count": 1}],
            }]}
            catalog = {"quarantined_groups": 0,
                       "groups": [{"enemy": "EN001A", "pages": []}]}
            result = compile_palettes(map_report, catalog, root)
            self.assertFalse(result["complete"])
            self.assertEqual(result["unresolved"][0]["sprite"], "EN999A")


if __name__ == "__main__":
    unittest.main()
