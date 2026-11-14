import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from run_enemy_render_ab import extract_result, summarize


class RenderAbTests(unittest.TestCase):
    def row(self, p95=8.0, load=10.0):
        return {
            "kinds": 8, "instances": 300, "pages": 10,
            "rgba_area_bytes": 1000, "png_bytes": 500,
            "renderer": "GPU", "load_ms": load,
            "frame_p50_ms": 7.0, "frame_p95_ms": p95, "frame_p99_ms": 9.0,
            "draw_calls_p50": 20, "draw_calls_p95": 22,
            "video_mem_monitor_bytes": 2000,
            "texture_mem_monitor_bytes": 1500,
        }

    def test_extracts_last_benchmark_json(self):
        text = 'Godot Engine\n{"noise":1}\n' + __import__("json").dumps(self.row())
        self.assertEqual(extract_result(text)["renderer"], "GPU")

    def test_summary_uses_median(self):
        result = summarize([self.row(8, 10), self.row(10, 14), self.row(9, 12)])
        self.assertEqual(result["frame_p95_ms_median"], 9)
        self.assertEqual(result["load_ms_median"], 12)

    def test_summary_rejects_changed_workload(self):
        a = self.row()
        b = self.row()
        b["instances"] = 301
        with self.assertRaises(ValueError):
            summarize([a, b])


if __name__ == "__main__":
    unittest.main()
