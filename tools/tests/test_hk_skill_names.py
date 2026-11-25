"""HK skill.dat source-backed UI-only label extraction regressions."""
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

from asset_converter.source.hk_skill_names import (
    parse_skill_labels_plain, export_private_skill_labels
)


def row(skill_id: int, name: str, description: str, cost: int) -> str:
    return ",".join([str(skill_id), name, description, "150", "0", str(cost)] +
                    ["0"] * 14) + "\r\n"


class HKSkillNamesChecks(unittest.TestCase):
    def test_source_names_consistent_across_skill_levels(self):
        raw = (row(20101, "赤色光彈", "原版攻擊魔法", 9) +
               row(20101, "赤色光彈", "原版攻擊魔法", 12) +
               row(28101, "治療術", "原版治療說明", 10)).encode("big5hkscs")
        labels = parse_skill_labels_plain(raw)
        self.assertEqual(len(labels), 2)
        self.assertEqual(labels[20101], ("赤色光彈", "原版攻擊魔法"))
        self.assertEqual(labels[28101][0], "治療術")

    def test_conflicting_name_or_description_rejected(self):
        with self.assertRaisesRegex(ValueError, "conflicting"):
            parse_skill_labels_plain(
                (row(20101, "赤色光彈", "說明", 9) +
                 row(20101, "錯誤名稱", "說明", 12)).encode("big5hkscs"))
        with self.assertRaisesRegex(ValueError, "conflicting"):
            parse_skill_labels_plain(
                (row(20101, "赤色光彈", "說明甲", 9) +
                 row(20101, "赤色光彈", "說明乙", 12)).encode("big5hkscs"))

    def test_missing_and_invalid_source_fails_closed(self):
        bad = [
            b"",
            b"20101",
            row(0, "合法名", "說明", 9).encode("big5hkscs"),
            row(20101, "", "說明", 9).encode("big5hkscs"),
            row(20101, "合法", "說明", 9).encode("big5hkscs") + b"\xff\xff",
        ]
        for source in bad:
            with self.subTest(source=source[:15]):
                with self.assertRaises(ValueError):
                    parse_skill_labels_plain(source)
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "skill.dat"
            source.write_text(row(20101, "赤色光彈", "說明", 9), encoding="big5hkscs")
            with self.assertRaisesRegex(ValueError, "original C.D.E."):
                export_private_skill_labels(source, Path(directory) / "private.json")


if __name__ == "__main__":
    unittest.main()
