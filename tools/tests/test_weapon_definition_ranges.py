"""Verified SGTest weapon-definition attack-range bridge tests."""

from __future__ import annotations

import json
import struct
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_weapon_definition_ranges import (
    KIND,
    compile_weapon_definition_ranges,
    publish_weapon_definition_ranges,
)
from asset_converter.source.hk_item_itd import ID_OFFSET, NAME_OFFSET, RECORD_LENGTH
from asset_converter.source.old_weapon_itd import (
    ATT_DISTANCE_WORD,
    ITEM_INDEX_WORD,
    RECORD_BYTES,
    USE_ITEM_WORD,
    WORD_BLOCK_OFFSET,
)


def old_weapon(item_index: int, attack_distance: int, *, use_item: int = 0) -> bytes:
    payload = bytearray(RECORD_BYTES)
    payload[0] = 1
    for index, value in (
        (ITEM_INDEX_WORD, item_index),
        (USE_ITEM_WORD, use_item),
        (ATT_DISTANCE_WORD, attack_distance),
    ):
        struct.pack_into("<H", payload, WORD_BLOCK_OFFSET + (index - 1) * 2, value)
    return bytes(payload)


def hk_weapon(item_id: int, name: str) -> bytes:
    item_type = 1
    payload = bytearray(RECORD_LENGTH[item_type])
    payload[0] = item_type
    struct.pack_into("<H", payload, ID_OFFSET[item_type], item_id)
    encoded = name.encode("cp950")
    payload[NAME_OFFSET[item_type]:NAME_OFFSET[item_type] + len(encoded)] = encoded
    return bytes(payload)


class WeaponDefinitionRangeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.items = self.root / "Items" / "weapon"
        self.items.mkdir(parents=True)

    def runtime_weapon(self, definition_id: int, name: str, attack_range: int):
        path = self.items / f"{definition_id}.tres"
        path.write_text(
            '[gd_resource type="Resource" format=3]\n'
            '[resource]\n'
            f'id = {definition_id}\n'
            f'item_name = {json.dumps(name, ensure_ascii=False)}\n'
            'icon_id = 1\n'
            'item_type_category = "Weapon"\n'
            f'attack_range = {attack_range}\n',
            encoding="utf-8",
        )
        return path

    def sources(self, *, runtime_range=140, old_range=140):
        source = self.runtime_weapon(20001, "跨版測試劍", runtime_range)
        hk = self.root / "item.itd"
        hk.write_bytes(hk_weapon(31229, "跨版測試劍"))
        old = self.root / "Weapon.itd"
        old.write_bytes(
            old_weapon(31229, old_range, use_item=7)
            + old_weapon(1587, 60)
        )
        return source, hk, old

    def test_three_way_match_emits_only_verified_rule(self):
        source, hk, old = self.sources()
        result = compile_weapon_definition_ranges(
            self.root / "Items", hk, old,
            item_source_reference="runtime fixture",
            hk_source_reference="HK fixture",
            old_source_reference="Old fixture",
        )
        self.assertEqual(KIND, result["kind"])
        self.assertTrue(result["rules_verified"])
        self.assertTrue(result["partial"])
        self.assertEqual(1, len(result["rules"]))
        rule = result["rules"][0]
        self.assertEqual(20001, rule["item_definition_id"])
        self.assertEqual(31229, rule["old_item_index"])
        self.assertEqual(140, rule["attack_distance"])
        self.assertEqual(7, rule["use_item"])
        self.assertEqual(64, len(rule["runtime_source_sha256"]))
        self.assertEqual([1587], result["audit"]["unresolved_old_item_indices"])
        self.assertEqual([140], result["audit"]["matched_attack_distance_values"])
        self.assertNotIn("跨版測試劍", json.dumps(result, ensure_ascii=False))
        self.assertNotIn(str(self.root), json.dumps(result, ensure_ascii=False))

        output = self.root / "out" / "weapon_definition_ranges.json"
        published = publish_weapon_definition_ranges(
            self.root / "Items", hk, old, output,
            item_source_reference="runtime fixture",
            hk_source_reference="HK fixture",
            old_source_reference="Old fixture",
        )
        self.assertEqual("pass", published["status"])
        self.assertEqual(KIND, json.loads(output.read_text(encoding="utf-8"))["kind"])
        self.assertTrue(source.is_file())

    def test_disagreeing_three_way_range_fails_closed(self):
        _, hk, old = self.sources(runtime_range=120, old_range=140)
        with self.assertRaisesRegex(ValueError, "evidence disagrees"):
            compile_weapon_definition_ranges(
                self.root / "Items", hk, old,
                item_source_reference="runtime fixture",
                hk_source_reference="HK fixture",
                old_source_reference="Old fixture",
            )

    def test_ambiguous_hk_name_cannot_create_authority(self):
        self.runtime_weapon(20001, "重名武器", 140)
        hk = self.root / "item.itd"
        hk.write_bytes(hk_weapon(31229, "重名武器") + hk_weapon(31230, "重名武器"))
        old = self.root / "Weapon.itd"
        old.write_bytes(old_weapon(31229, 140) + old_weapon(31230, 140))
        with self.assertRaisesRegex(ValueError, "no verified rules"):
            compile_weapon_definition_ranges(
                self.root / "Items", hk, old,
                item_source_reference="runtime fixture",
                hk_source_reference="HK fixture",
                old_source_reference="Old fixture",
            )


if __name__ == "__main__":
    unittest.main()
