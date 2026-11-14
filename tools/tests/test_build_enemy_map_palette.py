import csv
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_enemy_map_palette import build_palettes, read_enemy_dat


def record(enemy_id: int, x: int, y: int) -> bytes:
    return (struct.pack("<i", 7) + str(enemy_id).encode("ascii") + b"\0"
            + struct.pack("<ii", x, y) + bytes(207))


class MapPaletteTests(unittest.TestCase):
    def test_reads_spawn_binary_and_groups_visuals(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            enemy_dir = root / "Enemy"
            map_dir = root / "Map" / "Map13"
            enemy_dir.mkdir()
            map_dir.mkdir(parents=True)
            with (enemy_dir / "Enemy_Data.csv").open("w", encoding="cp950", newline="") as stream:
                writer = csv.writer(stream)
                writer.writerow([1, 1, "A", 5, 0, "", "EN001A"])
                writer.writerow([2, 2, "B", 6, 0, "", "EN002A"])
            payload = struct.pack("<i", 3) + record(1, 10, 20) + record(1, 30, 40) + record(2, 50, 60)
            (map_dir / "Enemy.dat").write_bytes(payload)
            result = build_palettes(root, {13})
            row = result["maps"][0]
            self.assertEqual((row["spawn_count"], row["visual_kind_count"]), (3, 2))
            self.assertEqual(row["visuals"], [
                {"sprite": "EN001A", "spawn_count": 2},
                {"sprite": "EN002A", "spawn_count": 1},
            ])
            self.assertTrue(row["complete"])

    def test_rejects_trailing_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "Enemy.dat"
            path.write_bytes(struct.pack("<i", 0) + b"x")
            with self.assertRaises(ValueError):
                read_enemy_dat(path)


if __name__ == "__main__":
    unittest.main()
