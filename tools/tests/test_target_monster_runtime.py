"""Target-only monster runtime publication; fixtures contain no original assets."""

import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from asset_converter.content.build_target_monster_runtime import compile_target_runtime


class TargetMonsterRuntimeTests(unittest.TestCase):
    def fixture(self, root: Path):
        visual = root / "population.json"
        visual.write_text(json.dumps({
            "version": 3,
            "kind": "visual_spawn_content",
            "map_id": 13,
            "stats_verified": False,
            "gameplay_ready": False,
            "definitions": [{
                "id": 1,
                "sprite": "EN010A",
                "atlas": "Enemy/en010a/atlas.json",
                "stats": None,
                "stats_verified": False,
            }],
            "spawns": [{
                "definition": 1,
                "p": [100, 200],
                "source_index": 0,
                "source_flags": 9,
            }],
        }), encoding="utf-8")
        enemy = root / "Enemy"
        enemy.mkdir()
        # Enemy_Data col3=level key, col4=EN_Att selector, col19=RebornTime seconds.
        (enemy / "Enemy_Data.csv").write_text(
            "1,1,Enemy One,3,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,15\n",
            encoding="cp950")
        (enemy / "EN_Att01.csv").write_text(
            "3,222,17,0,15,0,30,18,41\n", encoding="ascii")
        return visual, enemy / "Enemy_Data.csv"

    def test_publishes_only_source_backed_target_stats(self):
        with tempfile.TemporaryDirectory() as directory:
            visual, enemy_data = self.fixture(Path(directory))
            result = compile_target_runtime(visual, enemy_data)
            self.assertEqual(result["version"], 5)
            self.assertEqual(result["kind"], "verified_monster_target_runtime")
            self.assertTrue(result["stats_verified"])
            self.assertTrue(result["target_combat_ready"])
            self.assertFalse(result["ai_ready"])
            definition = result["definitions"][0]
            self.assertEqual(definition["id"], 1)
            self.assertEqual(definition["sprite"], "EN010A")
            self.assertEqual(definition["stats"], {
                "level": 3,
                "max_hp": 222,
                "respawn_ticks": 450,
            })
            self.assertNotIn("attack_damage", definition["stats"])
            self.assertNotIn("move_step", definition["stats"])
            self.assertEqual(result["spawns"][0]["source_flags"], 9)

    def test_missing_old_attributes_or_reborn_time_fails_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            visual, enemy_data = self.fixture(root)
            (root / "Enemy" / "Enemy_Data.csv").write_text(
                "1,1,Enemy One,65535,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,15\n",
                encoding="cp950")
            with self.assertRaisesRegex(ValueError, "did not resolve every visual"):
                compile_target_runtime(visual, enemy_data)

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            visual, enemy_data = self.fixture(root)
            (root / "Enemy" / "Enemy_Data.csv").write_text(
                "1,1,Enemy One,3,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,\n",
                encoding="cp950")
            with self.assertRaisesRegex(ValueError, "no verified RebornTime"):
                compile_target_runtime(visual, enemy_data)


if __name__ == "__main__":
    unittest.main()
