"""HK skill.dat source-backed icon frame regressions."""
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))

from asset_converter.source.hk_skill_icons import (
    parse_skill_icon_frames_plain, export_private_skill_icons
)


def row(skill_id: int, frame: int, name: str = "Skill") -> str:
    fields = [str(skill_id), name, "description", "150", "0", "9", "0", "0",
              "0", "0", "0", "1", "0", str(frame), "0", "Use", "Hit", "",
              "1", "Effect"]
    return ",".join(fields) + "\r\n"


class HKSkillIconChecks(unittest.TestCase):
    def test_field_14_maps_skill_to_original_frame(self):
        raw = (row(20101, 0) + row(20101, 0) + row(20102, 1) +
               row(20103, 80)).encode("big5hkscs")
        icons = parse_skill_icon_frames_plain(raw)
        self.assertEqual(icons, {20101: 0, 20102: 1, 20103: 80})

    def test_conflicting_or_out_of_range_frame_rejected(self):
        with self.assertRaisesRegex(ValueError, "conflicting"):
            parse_skill_icon_frames_plain(
                (row(20101, 0) + row(20101, 1)).encode("big5hkscs"))
        for frame in (-1, 114):
            with self.subTest(frame=frame):
                with self.assertRaises(ValueError):
                    parse_skill_icon_frames_plain(row(20101, frame).encode("big5hkscs"))

    def test_private_export_requires_original_cde(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "skill.dat"
            source.write_text(row(20101, 0), encoding="big5hkscs")
            with self.assertRaisesRegex(ValueError, "original C.D.E."):
                export_private_skill_icons(source, Path(directory) / "icons.json")


if __name__ == "__main__":
    unittest.main()
