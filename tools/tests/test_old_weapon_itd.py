"""Old Server Weapon.itd attack-distance evidence tests."""

from __future__ import annotations

import json
import struct
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_weapon_attack_ranges import (
    KIND,
    compile_weapon_attack_ranges,
    publish_weapon_attack_ranges,
)
from asset_converter.source.old_weapon_itd import (
    ATT_DISTANCE_WORD,
    ITEM_INDEX_WORD,
    RECORD_BYTES,
    USE_ITEM_WORD,
    WORD_BLOCK_OFFSET,
    parse_old_weapon_itd,
)


def record(item_index: int, attack_distance: int, *, use_item: int = 0) -> bytes:
    payload = bytearray(RECORD_BYTES)
    payload[0] = 1
    for index, value in (
        (ITEM_INDEX_WORD, item_index),
        (USE_ITEM_WORD, use_item),
        (ATT_DISTANCE_WORD, attack_distance),
    ):
        struct.pack_into("<H", payload, WORD_BLOCK_OFFSET + (index - 1) * 2, value)
    return bytes(payload)


class OldWeaponAttackRangeTests(unittest.TestCase):
    def test_source_layout_resolves_item_index_and_att_distance(self):
        rows = parse_old_weapon_itd(
            record(31225, 60) + record(31816, 350, use_item=99))
        self.assertEqual(31225, rows[0].item_index)
        self.assertEqual(60, rows[0].attack_distance)
        self.assertEqual(31816, rows[1].item_index)
        self.assertEqual(99, rows[1].use_item)
        self.assertEqual(350, rows[1].attack_distance)

    def test_malformed_duplicate_or_zero_distance_fails_closed(self):
        with self.assertRaises(ValueError):
            parse_old_weapon_itd(record(31225, 60)[:-1])
        with self.assertRaises(ValueError):
            parse_old_weapon_itd(record(31225, 60) + record(31225, 100))
        with self.assertRaises(ValueError):
            parse_old_weapon_itd(record(31225, 0))

    def test_manifest_and_publication_are_audited(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "source" / "Weapon.itd"
            source.parent.mkdir()
            source.write_bytes(record(31225, 60) + record(31226, 140))
            result = compile_weapon_attack_ranges(source)
            self.assertEqual(KIND, result["kind"])
            self.assertTrue(result["rules_verified"])
            self.assertEqual([60, 140],
                             result["audit"]["attack_distance_values"])
            self.assertEqual(1247, result["audit"]["record_bytes"])
            output = root / "out" / "weapon_attack_ranges.json"
            published = publish_weapon_attack_ranges(
                source, output, source_reference="unit-test Old Weapon.itd")
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("pass", published["status"])
            self.assertEqual("unit-test Old Weapon.itd",
                             document["source_reference"])
            self.assertEqual(2, len(document["rules"]))


if __name__ == "__main__":
    unittest.main()
