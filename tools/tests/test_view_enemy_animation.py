import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from view_enemy_animation import load_selection, read_catalog
from view_legacy_asset import render_preview


class EnemyViewerTests(unittest.TestCase):
    def test_restores_action_frame_crop_origin_and_mirror(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "Enemy" / "EN001").mkdir(parents=True)
            page = Image.new("RGBA", (8, 8))
            page.putpixel((2, 3), (255, 0, 0, 255))
            page.save(root / "Enemy" / "EN001" / "page_0.png")
            step = {"page": 0, "rect": [2, 3, 1, 1], "crop": [1, 0, 2, 1],
                    "size": [3, 1], "raw_origin": [1, 0], "source_frame": 2,
                    "raw_time": 5, "legacy_default_update_calls": 8}
            group = {"enemy": "EN001", "pages": ["Enemy/EN001/page_0.png"],
                     "actions": [{"key": "S", "events": [], "special_init_path": False,
                                  "directions": [{"facing": i, "flip_x": i == 6,
                                                  "frames": [step]} for i in range(1, 9)]}]}
            path = root / "enemy_catalog.json"
            path.write_text(json.dumps({"version": 1, "groups": [group]}), encoding="utf-8")
            catalog = read_catalog(path)
            steps, action = load_selection(path, catalog["groups"][0], "S", 6)
            self.assertEqual(action["key"], "S")
            self.assertEqual((steps[0]["frame"].origin_x, steps[0]["source_frame"],
                              steps[0]["legacy_default_update_calls"]), (1, 2, 8))
            self.assertEqual(steps[0]["frame"].image.getpixel((1, 0)), (255, 0, 0, 255))
            self.assertEqual(render_preview(steps[0]["frame"], 1, steps[0]["flip_x"], False)
                             .getpixel((1, 0)), (255, 0, 0))
            with self.assertRaisesRegex(ValueError, "unknown action"):
                load_selection(path, group, "X", 1)


if __name__ == "__main__":
    unittest.main()
