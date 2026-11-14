import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_act_events import audit_sources, classify_token, split_audio_candidates


def act_with_event(token: str) -> bytes:
    name = b"A\0"
    frame = struct.pack("<II", 0, 3)
    payload = struct.pack("<I", 0) + token.encode("ascii") + b"\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + b"\x00\x02"
            + struct.pack("<I", 1) + frame
            + b"\x04" + struct.pack("<I", len(payload)) + payload
            + b"\xff")


class EventAuditTests(unittest.TestCase):
    def test_token_classes(self):
        self.assertEqual(classify_token("+"), "control")
        self.assertEqual(classify_token("SE0001"), "audio_candidate")
        self.assertEqual(classify_token("SE0001,SE0002"), "audio_candidate")
        self.assertEqual(classify_token("SE0003.SE0004,SE0005"), "audio_candidate")
        self.assertEqual(split_audio_candidates("SE1,SE2\nSE3"), ["SE1", "SE2", "SE3"])
        self.assertEqual(classify_token("bad token!"), "unknown")

    def test_audit_counts_control_and_audio(self):
        sources = {
            "a.act": {"path": "A.ACT", "kind": "archive", "raw": act_with_event("+")},
            "b.act": {"path": "B.ACT", "kind": "loose", "raw": act_with_event("SE0001,SE0002")},
        }
        result = audit_sources(sources, {"effective_act_count": 2}, {"se0001"})
        self.assertEqual(result["event_count"], 2)
        self.assertEqual(result["classification_counts"], {"audio_candidate": 1, "control": 1})
        self.assertEqual(result["control_token_counts"], {"+": 1})
        self.assertEqual(result["audio_atom_counts"], {"SE0001": 1, "SE0002": 1})
        self.assertEqual(result["missing_audio_atoms"], ["SE0002"])
        self.assertEqual(result["failures"], [])


if __name__ == "__main__":
    unittest.main()
