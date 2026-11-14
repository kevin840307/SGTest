import json
import struct
import tempfile
import unittest
from pathlib import Path

from monster_workflow import (
    compile_monster_definition, merge_population, validate_recipe,
    validate_spawns,
)
from test_assets import spr


def action(key):
    name = key.encode("ascii") + b"\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + bytes([1, 2])
            + struct.pack("<I", 1) + struct.pack("<II", 0, 5))


class MonsterWorkflowTests(unittest.TestCase):
    def recipe(self):
        return {
            "version": 1,
            "map_id": 13,
            "definition": {
                "id": 81,
                "sprite": "EN777A",
                "stats": {
                    "hp": 120,
                    "damage": 7,
                    "move_speed": 80,
                    "aggro_range": 600,
                    "attack_range": 75,
                    "chase_stop_range": 42,
                    "attack_interval_seconds": .8,
                    "respawn_seconds": 3,
                },
            },
            "spawns": [{"p": [24, 24], "source_flags": 5}],
        }

    def test_recipe_stats_and_population_v1_migration(self):
        recipe = validate_recipe(self.recipe())
        self.assertEqual(recipe["definition"]["stats"]["hp"], 120)
        base = {
            "version": 1,
            "map_id": 13,
            "definitions": [{"id": 1, "sprite": "EN001A", "atlas": "enemy/en001a.json"}],
            "spawns": [{"definition": 1, "p": [8, 8], "source_index": 0, "source_flags": 9}],
        }
        merged = merge_population(base, recipe, recipe["spawns"])
        self.assertEqual(merged["version"], 2)
        self.assertEqual(merged["definitions"][0]["stats"]["hp"], 30)
        self.assertEqual(merged["definitions"][1]["stats"]["move_speed"], 80.0)
        self.assertEqual(merged["spawns"][-1]["definition"], 81)
        self.assertLess(merged["spawns"][-1]["source_index"], 0)
        with self.assertRaisesRegex(ValueError, "already exists"):
            merge_population(merged, recipe, recipe["spawns"])
        replaced = merge_population(merged, recipe, [{"p": [40, 40], "source_flags": 0}], replace=True)
        self.assertEqual(sum(row["definition"] == 81 for row in replaced["spawns"]), 1)

    def test_recipe_rejects_bad_stats_and_spawn(self):
        recipe = self.recipe()
        recipe["definition"]["stats"]["attack_range"] = 700
        recipe["definition"]["stats"]["aggro_range"] = 500
        with self.assertRaisesRegex(ValueError, "outside supported"):
            validate_recipe(recipe)
        recipe = self.recipe()
        recipe["spawns"][0]["p"] = [float("nan"), 1]
        with self.assertRaisesRegex(ValueError, "finite"):
            validate_recipe(recipe)

    def test_collision_exact_and_optional_snap(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "blocking.bin"
            # 3x3, center blocked.
            cells = bytearray(9)
            cells[4] = 1
            path.write_bytes(struct.pack("<ii", 3, 3) + cells)
            rows = [{"p": [24.0, 24.0], "source_flags": 0}]
            with self.assertRaisesRegex(ValueError, "blocked"):
                validate_spawns(rows, path, False)
            snapped = validate_spawns(rows, path, True)
            self.assertTrue(snapped[0]["snapped"])
            self.assertNotEqual(snapped[0]["p"], [24.0, 24.0])

    def test_synthetic_source_requires_all_five_actions_and_compiles_40_views(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            enemy = root / "enemy"
            enemy.mkdir()
            for behavior in ("NS", "NW", "FA", "NH", "ND"):
                for facing in range(1, 9):
                    (enemy / f"en777a_{behavior.lower()}{facing}.spr").write_bytes(spr())
            (enemy / "en777a.act").write_bytes(
                action("S") + action("W") + action("A") + action("H") + action("D") + b"\xff"
            )
            definition = compile_monster_definition(root, "EN777A")
            self.assertEqual(set(definition["behaviors"]), {"NS", "NW", "FA", "NH", "ND"})
            self.assertEqual(sum(len(row["facings"]) for row in definition["behaviors"].values()), 40)
            (enemy / "en777a_nd3.spr").unlink()
            with self.assertRaises(ValueError):
                compile_monster_definition(root, "EN777A")


if __name__ == "__main__":
    unittest.main()
