import struct
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_act import parse_act


def action(key: str, loop: int, frames: list[tuple[int, int]]) -> bytes:
    name = key.encode("ascii") + b"\0"
    payload = b"\x01" + struct.pack("<I", len(name)) + name
    payload += b"\x03" + struct.pack("<I", 1)
    payload += bytes([loop, 2]) + struct.pack("<I", len(frames))
    for index, raw_time in frames:
        payload += struct.pack("<II", index, raw_time)
    return payload


class ActTests(unittest.TestCase):
    def test_action_frames_and_event_boundary(self):
        event = b"\x04" + struct.pack("<I", 8) + struct.pack("<I", 2) + b"X\0\0\0"
        raw = action("S", 1, [(0, 6), (1, 6), (2, 6)]) + event + action("D", 0, [(0, 3)]) + b"\xff"
        result = parse_act(raw)
        self.assertEqual([x["key"] for x in result], ["S", "D"])
        self.assertEqual(result[0]["frames"][1], {"index": 1, "raw_time": 6,
                                                     "legacy_default_update_calls": 9,
                                                     "legacy_reference_default_duration_ms": 180})
        self.assertEqual(result[0]["events"][0]["at_frame_raw"], 2)
        self.assertTrue(result[0]["events"][0]["fires_on_enter_frame"])
        self.assertEqual(result[0]["events"][0]["legacy_reference_default_offset_ms"], 360)
        self.assertEqual(result[0]["events"][0]["token"], "X")
        self.assertEqual(result[1]["raw_loop_flag"], 0)
        self.assertFalse(result[1]["loops"])

    def test_truncation_rejected(self):
        with self.assertRaises(ValueError):
            parse_act(action("A", 0, [(0, 6)])[:-3])


if __name__ == "__main__":
    unittest.main()
