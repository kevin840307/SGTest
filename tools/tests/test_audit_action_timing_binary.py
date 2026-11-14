import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_action_timing_binary import (
    ACTION_DEFAULTS, LOOP_PREFIX, LOOP_SUFFIX, SYSTEM_FRAME_CORE,
    find_all, find_loop_signatures, legacy_default_update_calls,
    legacy_reference_duration_ms,
)


class TimingBinaryTests(unittest.TestCase):
    def test_find_all(self):
        blob = b"x" + ACTION_DEFAULTS + b"y" + ACTION_DEFAULTS
        self.assertEqual(find_all(blob, ACTION_DEFAULTS), [1, 1 + len(ACTION_DEFAULTS) + 1])

    def test_loop_signature_skips_iat_pointer(self):
        blob = b"x" + LOOP_PREFIX + struct.pack("<I", 0x12345678) + LOOP_SUFFIX + b"z"
        self.assertEqual(find_loop_signatures(blob), [1])

    def test_loop_signature_rejects_wrong_suffix(self):
        blob = LOOP_PREFIX + b"\0" * 4 + LOOP_SUFFIX[:-1] + b"\0"
        self.assertEqual(find_loop_signatures(blob), [])

    def test_default_duration_math(self):
        self.assertEqual(legacy_default_update_calls(6), 9)
        self.assertEqual(legacy_default_update_calls(3), 4)
        self.assertEqual(legacy_reference_duration_ms(6), 180)
        self.assertEqual(legacy_reference_duration_ms(3), 80)

    def test_invalid_speed(self):
        with self.assertRaises(ValueError):
            legacy_default_update_calls(6, 0)


if __name__ == "__main__":
    unittest.main()
