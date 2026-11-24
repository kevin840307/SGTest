"""Old Server character EXP reward evidence tests; no private data required."""

from __future__ import annotations

import json
import struct
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_character_experience_rewards import (
    KIND,
    compile_character_experience_rewards,
    publish_character_experience_rewards,
)
from asset_converter.source.old_enemy_rewards import (
    parse_enemy_attribute_table,
    parse_enemy_catalog,
)


def write_enemy_dat(path: Path, enemy_ids: list[int]) -> None:
    payload = bytearray(struct.pack("<i", len(enemy_ids)))
    for source_index, enemy_id in enumerate(enemy_ids):
        payload += struct.pack("<i", 0)
        payload += str(enemy_id).encode("ascii") + b"\0"
        payload += struct.pack("<ii", 1000 + source_index, 2000 + source_index)
        payload += bytes(207)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


class CharacterExperienceRewardEvidenceTests(unittest.TestCase):
    def make_dat(self, root: Path) -> Path:
        dat = root / "Dat"
        enemy = dat / "Enemy"
        enemy.mkdir(parents=True)
        (enemy / "Enemy_Data.csv").write_text(
            "101,101,Blob,5,2,,EN001A\n"
            "102,102,Wolf,7,1,,EN002A\n"
            "999,999,UnusedSentinel,65535,7,,EN099A\n",
            encoding="cp950")
        (enemy / "EN_Att01.csv").write_text(
            "7,100,2,3,4,5,6,99,8\n", encoding="ascii")
        (enemy / "EN_Att02.csv").write_text(
            "5,100,2,3,4,5,6,77,8\n", encoding="ascii")
        write_enemy_dat(dat / "Map" / "Map13" / "Enemy.dat", [102, 101, 102])
        write_enemy_dat(dat / "Map" / "Map15" / "Enemy.dat", [999, 999])
        return dat

    def test_old_loader_schema_resolves_dr_and_r_exp_from_distinct_columns(self):
        catalog = parse_enemy_catalog(
            "101,101,Blob,5,2,,EN001A\n")
        self.assertEqual((5, 2), catalog[101])
        table = parse_enemy_attribute_table(
            "5,100,2,3,4,5,6,77,8\n", table_id=2)
        self.assertEqual(8, table[5])

    def test_manifest_is_exact_server_contract_and_ignores_unused_sentinel(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            result = compile_character_experience_rewards(dat, {13, 15})
            self.assertEqual(KIND, result["kind"])
            self.assertTrue(result["rules_verified"])
            self.assertTrue(result["gameplay_ready"])
            self.assertEqual(
                [
                    {"map_id": 13, "enemy_id": 101,
                     "enemy_level": 5, "enemy_dr": 77, "r_exp": 8},
                    {"map_id": 13, "enemy_id": 102,
                     "enemy_level": 7, "enemy_dr": 99, "r_exp": 8},
                ],
                result["rules"])
            self.assertEqual(3, result["audit"]["maps"][0]["spawn_count"])
            self.assertEqual(2, result["audit"]["maps"][0]["unique_enemy_ids"])
            self.assertEqual(
                [{
                    "enemy_id": 999,
                    "level_key": 65535,
                    "attribute_table": 7,
                    "reason": "old_add_attr_find_attr_miss",
                }],
                result["audit"]["excluded_no_attribute_reward"])

    def test_selected_unresolved_or_duplicate_source_fails_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            (dat / "Enemy" / "EN_Att02.csv").write_text(
                "4,100,2,3,4,5,6,77,8\n", encoding="ascii")
            with self.assertRaises(ValueError):
                compile_character_experience_rewards(dat, {13})

        duplicate_same = parse_enemy_attribute_table(
            "5,1,2,3,4,5,6,7,8\n"
            "5,9,9,9,9,9,9,99,8\n", table_id=1)
        self.assertEqual(8, duplicate_same[5])
        with self.assertRaises(ValueError):
            parse_enemy_attribute_table(
                "5,1,2,3,4,5,6,7,8\n"
                "5,1,2,3,4,5,6,7,9\n", table_id=1)

    def test_publication_writes_audited_manifest_outside_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            output = root / "output" / "character_exp.json"
            result = publish_character_experience_rewards(
                dat, output, {13}, source_reference="unit-test Old reward chain")
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("pass", result["status"])
            self.assertEqual(KIND, document["kind"])
            self.assertEqual("unit-test Old reward chain",
                             document["source_reference"])
            self.assertEqual(2, len(document["rules"]))


if __name__ == "__main__":
    unittest.main()
