import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from legacy_asset_viewer import select_frames


class ViewerFilterTests(unittest.TestCase):
    def test_filter_unknown_is_not_fabricated(self):
        doc = {"frames": [{"direction": None, "action": None, "frame_index": 0},
                          {"direction": "N", "action": "Walk", "frame_index": 1},
                          {"direction": "S", "action": "Walk", "frame_index": 2}]}
        self.assertEqual(len(select_frames(doc)), 3)
        self.assertEqual([f["frame_index"] for f in select_frames(doc, "N", "Walk")], [1])
        self.assertEqual(select_frames(doc, "N", "Attack"), [])


if __name__ == "__main__":
    unittest.main()
