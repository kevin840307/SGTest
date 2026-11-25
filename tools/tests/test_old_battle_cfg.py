"""GP-012-01 Old SGOBattleSys battle.cfg SP (not cadence) evidence checks."""

from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from asset_converter.content.build_battle_sp_evidence import (
    KIND, compile_battle_sp_evidence, publish_battle_sp_evidence,
)
from asset_converter.source.old_battle_cfg import parse_old_battle_cfg

SOURCE = b"""[SP]
Multiple=10 // the SP bucket width, not a proven attack cooldown
Count=3
SP00=32
SP01=27
SP02=37
RaceDelta00=32
RaceDelta01=32
RaceDelta02=27
RaceDelta03=37
[Damage]
DamageDelta=10
"""


class OldBattleConfigTests(unittest.TestCase):
    def test_parses_sp_and_race_delta_without_inventing_cooldown(self):
        timing = parse_old_battle_cfg(SOURCE)
        self.assertEqual(10, timing.multiple)
        self.assertEqual((32, 27, 37), timing.sp)
        self.assertEqual((32, 32, 27, 37), timing.race_delta)
        self.assertEqual(
            [38, 32, 44],
            timing.scaled_for_fps(30, 25)["sp_loaded_bytes"])
        self.assertEqual(
            [38, 38, 32, 44],
            timing.scaled_for_fps(30, 25)["race_delta_loaded_words"])

    def test_bad_input_fail_closed(self):
        invalid = (
            b"",
            SOURCE.replace(b"Count=3", b"Count=4"),
            SOURCE.replace(b"SP01=27", b"SP01=0"),
            SOURCE.replace(b"SP01=27", b"SP01=27\nSP01=28"),
            SOURCE.replace(b"SP01=27", b"SP01=27\nSP03=42"),
            SOURCE.replace(b"RaceDelta02=27", b"RaceDelta02=27\nRaceDelta02=28"),
            SOURCE.replace(b"RaceDelta02=27\n", b""),
            SOURCE.replace(b"[SP]", b"[SP]\n[SP]"),
        )
        for sample in invalid:
            with self.subTest(sample=sample[:35]):
                with self.assertRaises(ValueError):
                    parse_old_battle_cfg(sample)
        with self.assertRaises(ValueError):
            parse_old_battle_cfg(b"[SP]\nSP00=\xff")
        with self.assertRaises(ValueError):
            parse_old_battle_cfg(SOURCE + b" " * (64 * 1024))

    def test_overflow_and_explicit_unverified_status(self):
        timing = parse_old_battle_cfg(SOURCE)
        for now, base in ((0, 25), (30, 0), (240, 1), (241, 25)):
            with self.subTest(now=now, base=base):
                with self.assertRaises(ValueError):
                    timing.scaled_for_fps(now, base)
        with tempfile.TemporaryDirectory() as temporary:
            src = Path(temporary) / "source" / "battle.cfg"
            src.parent.mkdir()
            src.write_bytes(SOURCE)
            compiled = compile_battle_sp_evidence(src)
            self.assertEqual(KIND, compiled["kind"])
            self.assertEqual("partial_source_only", compiled["status"])
            self.assertFalse(compiled["combat_profile_verified"])
            self.assertFalse(compiled["combat_cooldown_verified"])
            self.assertIn("SGOBattleSys::WillDamage_ForSP", compiled["observed_consumers"])
            self.assertIsNone(compiled["fps_scaled_loader_values"])
            with self.assertRaises(ValueError):
                compile_battle_sp_evidence(src, current_fps=30)
            output = Path(temporary) / "export" / "battle_sp.json"
            result = publish_battle_sp_evidence(
                src, output, current_fps=30, default_fps=25)
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(3, len(document["sp_values_at_source_fps"]))
            self.assertEqual(30, result["fps_scaled_loader_values"]["current_fps"])
            with self.assertRaises(ValueError):
                publish_battle_sp_evidence(src, src.parent / "bad.json")


if __name__ == "__main__":
    unittest.main()
