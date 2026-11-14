import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from benchmark_runtime_fps_concurrent import checkpoints
from benchmark_runtime_isolation import parse_traces, summarize


class RuntimeIsolationToolTests(unittest.TestCase):
    def test_trace_parser_and_summary(self):
        text = "\n".join([
            'noise',
            'runtime_trace {"elapsed_s":5.0,"window_s":5.0,"physics_hz":30.0,"engine_frame_hz":60.0,"visual_update_hz":15.0,"rx_packets":100,"tx_packets":155,"input_hz":30.0,"queue_age_p95_ms":20.0,"queue_age_p99_ms":25.0,"queue_age_max_ms":30.0,"max_incoming_depth":3,"snapshots":50,"owner_states":50,"actions":0,"texture_memory_bytes":1048576,"video_memory_bytes":2097152,"static_memory_bytes":3145728,"network_failed":false}',
            'runtime_trace {"elapsed_s":10.0,"window_s":5.0,"physics_hz":30.0,"engine_frame_hz":60.0,"visual_update_hz":15.0,"rx_packets":105,"tx_packets":155,"input_hz":30.0,"queue_age_p95_ms":21.0,"queue_age_p99_ms":26.0,"queue_age_max_ms":31.0,"max_incoming_depth":4,"snapshots":50,"owner_states":50,"actions":1,"texture_memory_bytes":1048576,"video_memory_bytes":2097152,"static_memory_bytes":3145728,"network_failed":false}',
        ])
        rows = parse_traces(text)
        self.assertEqual(len(rows), 2)
        summary = summarize(rows, minimum_elapsed=0)
        self.assertEqual(summary["physics_hz_median"], 30.0)
        self.assertEqual(summary["input_hz_median"], 30.0)
        self.assertEqual(summary["rx_hz_median"], 20.5)
        self.assertEqual(summary["tx_hz_median"], 31.0)
        self.assertEqual(summary["queue_age_p99_ms_worst"], 26.0)
        self.assertEqual(summary["max_incoming_depth"], 4)
        self.assertEqual(summary["texture_memory_mib_median"], 1.0)
        self.assertFalse(summary["network_failed"])

    def test_checkpoint_parser_is_tick_keyed(self):
        text = "\n".join([
            'state_checkpoint {"tick":150,"hash":"aaa","players":2,"monsters":0,"drops":0}',
            'junk',
            'state_checkpoint {"tick":300,"hash":"bbb","players":2,"monsters":0,"drops":0}',
        ])
        self.assertEqual(checkpoints(text), {150: "aaa", 300: "bbb"})


if __name__ == "__main__":
    unittest.main()
