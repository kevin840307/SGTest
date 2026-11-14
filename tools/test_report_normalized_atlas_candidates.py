import unittest

from report_normalized_atlas_candidates import choose


def row(name, rgba, video, p99=25, pages=10, recovery=True, multiplayer=True):
    return {
        "candidate":name,
        "rgba_bytes":rgba,
        "video_memory_bytes":video,
        "multiplayer_frame_ms_p99":p99,
        "pages":pages,
        "all_texture_handles_released":recovery,
        "engine_texture_recovery_passed":recovery,
        "ram_recovery_passed":recovery,
        "multiplayer_execution_passed":multiplayer,
        "multiplayer_frame_gate_passed":multiplayer,
    }


class Asset202ReportTests(unittest.TestCase):
    def test_selects_lowest_resource_eligible_base(self):
        self.assertEqual(choose([
            row("small",100,200,p99=30,pages=14),
            row("few_pages",104,210,p99=20,pages=9),
        ])["candidate"],"small")

    def test_rejects_recovery_or_multiplayer_failures(self):
        self.assertEqual(choose([
            row("bad_recovery",90,180,recovery=False),
            row("good",100,200),
        ])["candidate"],"good")
        with self.assertRaises(ValueError):
            choose([row("bad",100,200,multiplayer=False)])


if __name__=="__main__":
    unittest.main()
