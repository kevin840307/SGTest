import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_enemy_animations import audit_group, complete_group_keys


def act(key: str, indices: list[int]) -> bytes:
    raw_name = key.encode("ascii") + b"\0"
    header = b"\x01" + struct.pack("<I", len(raw_name)) + raw_name
    header += b"\x03" + struct.pack("<I", 1) + b"\x01\x02" + struct.pack("<I", len(indices))
    return header + b"".join(struct.pack("<II", index, 5) for index in indices) + b"\xff"


def spr() -> bytes:
    row = struct.pack("<HHH", 1, 0xF800, 0x8000)
    frame = struct.pack("<IIiiI", 1, 1, 0, 0, 20) + row
    return frame + struct.pack("<II", 0, 1)


class EnemyAuditTests(unittest.TestCase):
    def test_complete_group_keys_are_sorted_and_exclude_issues(self):
        report = {"groups": [{"enemy": "EN002", "complete_source_mapping": True},
                             {"enemy": "EN001", "complete_source_mapping": False},
                             {"enemy": "EN000", "complete_source_mapping": True}]}
        self.assertEqual(complete_group_keys(report), ["Enemy/EN000", "Enemy/EN002"])

    def test_all_logical_facings_use_shared_source_and_flip(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            action = root / "EN001.ACT"
            action.write_bytes(act("S", [0, 0]))
            for suffix in ("NS1", "NS3"):
                (root / f"EN001_{suffix}.spr").write_bytes(spr())
            result = audit_group(root, action)
            self.assertTrue(result["complete_source_mapping"])
            self.assertEqual(len(result["actions"][0]["directions"]), 8)
            self.assertEqual(result["actions"][0]["directions"][5]["source_direction"], 1)
            self.assertTrue(result["actions"][0]["directions"][5]["flip_x"])
            self.assertFalse(result["default_timing_verified"])
            verified = audit_group(root, action, timing_verified=True)
            self.assertTrue(verified["default_timing_verified"])

    def test_missing_and_invalid_indices_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            action = root / "EN001.ACT"
            action.write_bytes(act("S", [1]))
            (root / "EN001_NS1.spr").write_bytes(spr())
            result = audit_group(root, action)
            reasons = {item["reason"] for item in result["issues"]}
            self.assertEqual(reasons, {"missing_spr", "frame_index_out_of_range"})
            self.assertFalse(result["complete_source_mapping"])


if __name__ == "__main__":
    unittest.main()
