"""Old Server monster drop evidence tests; no private data required."""

from __future__ import annotations

import json
import struct
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_monster_drop_rules import (
    KIND,
    VERSION,
    compile_monster_drop_rules,
    publish_monster_drop_rules,
)
from asset_converter.source.old_enemy_drops import (
    OLD_RATE_UNIT,
    parse_enemy_drop_rows,
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


def enemy_row(
    enemy_id: int,
    *,
    level: int,
    selector: int,
    first: list[tuple[int, str]],
    second: list[tuple[int, str]],
) -> str:
    row = [""] * (74 + 2 * (len(first) + len(second)))
    row[0] = str(enemy_id)
    row[1] = str(enemy_id)
    row[2] = "Synthetic"
    row[3] = str(level)
    row[4] = str(selector)
    row[72] = str(len(first))
    row[73] = str(len(second))
    for index, (item, rate) in enumerate(first + second):
        row[74 + 2 * index] = str(item)
        row[75 + 2 * index] = rate
    return ",".join(row)


class MonsterDropEvidenceTests(unittest.TestCase):
    def make_dat(self, root: Path) -> Path:
        server = root / "server"
        dat = server / "Dat"
        enemy = dat / "Enemy"
        enemy.mkdir(parents=True)
        (server / "enemy.cfg").write_text(
            "LvDeltaForDropItemRateDrop=15\n"
            "[DropItemLuckNeed]\n"
            "MaxDropRateForCheckLuckItem=100\n",
            encoding="cp950",
        )
        (enemy / "Enemy_Data.csv").write_text(
            enemy_row(
                101, level=5, selector=2,
                first=[(2701, "0.02"), (2702, "0.005")],
                second=[(2703, "0.003")],
            ) + "\n",
            encoding="cp950",
        )
        (enemy / "EN_Att02.csv").write_text(
            "5,100,2,3,4,5,6,7,8\n", encoding="ascii")
        write_enemy_dat(dat / "Map" / "Map13" / "Enemy.dat", [101, 101])
        return dat

    def test_parser_caps_luck_independent_rule_and_excludes_sensitive_rule(self):
        cap = 100 * OLD_RATE_UNIT
        rules, skipped = parse_enemy_drop_rows(
            enemy_row(
                101, level=5, selector=2,
                first=[(2701, "0.02"), (2702, "0.005")],
                second=[(2703, "0.003")],
            ),
            required_enemy_ids={101},
            check_luck_cap_threshold=cap,
        )
        self.assertEqual(
            [
                {
                    "slot": 0,
                    "source_pair_index": 0,
                    "item_definition_id": 2701,
                    "roll_threshold": cap,
                    "check_luck_source": True,
                },
                {
                    "slot": 1,
                    "source_pair_index": 2,
                    "item_definition_id": 2703,
                    "roll_threshold": int(0.003 * 0xFFFFFFFF),
                    "check_luck_source": False,
                },
            ],
            rules[101],
        )
        self.assertEqual("luck_sensitive_below_verified_cap", skipped[0]["reason"])
        self.assertEqual(2702, skipped[0]["item_definition_id"])

    def test_manifest_is_v2_probability_contract_with_level_penalty_source(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            result = compile_monster_drop_rules(dat, {13})
            self.assertEqual(KIND, result["kind"])
            self.assertEqual(VERSION, result["version"])
            self.assertEqual(15, result["level_delta_divisor"])
            self.assertEqual(2, len(result["rules"]))
            self.assertEqual(5, result["rules"][0]["enemy_level"])
            self.assertEqual(100 * OLD_RATE_UNIT,
                             result["rules"][0]["roll_threshold"])
            self.assertEqual(
                "luck_sensitive_below_verified_cap",
                result["audit"]["excluded_rules"][0]["reason"],
            )

    def test_publication_stays_outside_private_source_and_fails_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            output = root / "public" / "monster_drop_rules.json"
            result = publish_monster_drop_rules(
                dat, output, {13}, source_reference="unit-test Old drop chain")
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual("pass", result["status"])
            self.assertEqual("unit-test Old drop chain",
                             document["source_reference"])
            with self.assertRaises(ValueError):
                publish_monster_drop_rules(dat, dat / "bad.json", {13})

    def test_missing_cfg_or_selected_enemy_fails_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dat = self.make_dat(root)
            (dat.parent / "enemy.cfg").write_text(
                "LvDeltaForDropItemRateDrop=15\n", encoding="cp950")
            with self.assertRaises(ValueError):
                compile_monster_drop_rules(dat, {13})
            write_enemy_dat(dat / "Map" / "Map13" / "Enemy.dat", [999])
            with self.assertRaises(ValueError):
                compile_monster_drop_rules(dat, {13})


if __name__ == "__main__":
    unittest.main()
