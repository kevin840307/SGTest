"""HK item_extra.csv and grading.csv presentation-evidence tests."""
import sys
import unittest
from pathlib import Path

REPO=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO/"tools"))
from asset_converter.source.hk_item_metadata import (
    parse_hk_grading_plain,parse_hk_item_extra_plain,
)


GRADING = (
    "S,248,60,60,255\r\n"
    "A,255,0,255,255\r\n"
    "B,75,255,255,255\r\n"
    "C,123,255,0,255\r\n"
    "D,255,255,255,255\r\n"
).encode("cp950")


class HKItemMetadataTests(unittest.TestCase):
    def test_item_extra_exact_eight_fields_and_grade(self):
        rows=parse_hk_item_extra_plain(
            "31207,跨版本測試盔甲,EQUIP,0,1,1,0,A\r\n".encode("cp950"))
        self.assertEqual(len(rows),1)
        self.assertEqual(rows[0].hk_item_id,31207)
        self.assertEqual(rows[0].name,"跨版本測試盔甲")
        self.assertEqual(rows[0].kind,"EQUIP")
        self.assertEqual(rows[0].flags,(False,True,True,False))
        self.assertEqual(rows[0].grade,"A")

    def test_grading_reproduces_original_client_zero_to_one_channels(self):
        grades={x.grade:x for x in parse_hk_grading_plain(GRADING)}
        self.assertEqual(grades["S"].effective_rgba,(248,60,60,255))
        self.assertEqual(grades["A"].raw_rgba,(255,0,255,255))
        self.assertEqual(grades["A"].effective_rgba,(255,1,255,255))
        self.assertEqual(grades["C"].effective_rgba,(123,255,1,255))
        self.assertEqual(grades["D"].effective_rgba,(255,255,255,255))

    def test_malformed_or_incomplete_metadata_fails_closed(self):
        with self.assertRaisesRegex(ValueError,"8 fields"):
            parse_hk_item_extra_plain(
                "31207,測試,EQUIP,0,1,1,A\r\n".encode("cp950"))
        with self.assertRaisesRegex(ValueError,"invalid/duplicate item ID"):
            parse_hk_item_extra_plain(
                ("31207,測試,EQUIP,0,1,1,0,A\r\n"
                 "31207,測試2,EQUIP,0,1,1,0,A\r\n").encode("cp950"))
        with self.assertRaisesRegex(ValueError,"exactly S/A/B/C/D"):
            parse_hk_grading_plain(
                "A,255,0,255,255\r\nD,255,255,255,255\r\n".encode("cp950"))


if __name__=="__main__":
    unittest.main()
