import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_character_render_ab import extract_result, summarize


class CharacterRenderAbTests(unittest.TestCase):
    def row(self, p95=8.0):
        return {
            "players": 20, "races": 4, "pages": 8,
            "rgba_area_bytes": 1000, "png_bytes": 500, "renderer": "GPU",
            "load_ms": 10.0, "frame_p50_ms": 7.0,
            "frame_p95_ms": p95, "frame_p99_ms": 9.0,
            "draw_calls_p50": 40, "draw_calls_p95": 42,
            "video_mem_monitor_bytes": 2000,
            "texture_mem_monitor_bytes": 1500,
        }

    def test_extract(self):
        output = "Godot\n" + json.dumps(self.row())
        self.assertEqual(extract_result(output)["players"], 20)

    def test_summary_median(self):
        result = summarize([self.row(8), self.row(10), self.row(9)])
        self.assertEqual(result["frame_p95_ms_median"], 9)

    def test_summary_rejects_mixed_players(self):
        one = self.row()
        two = self.row()
        two["players"] = 100
        with self.assertRaises(ValueError):
            summarize([one, two])


if __name__ == "__main__":
    unittest.main()
