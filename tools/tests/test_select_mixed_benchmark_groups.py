import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from select_mixed_benchmark_groups import choose


def spr(color: int) -> bytes:
    pixels = struct.pack("<HH", 1, color) + struct.pack("<H", 0x8000)
    frame = struct.pack("<IIiiI", 1, 1, 0, 0, 20) + pixels
    return frame + struct.pack("<II", 0, 1)


class SelectionTests(unittest.TestCase):
    def test_selection_from_clean_source_and_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for folder, name in (("Enemy", "EN001_NS1.spr"),
                                 ("Enemy", "EN002_NS1.spr"),
                                 ("Player/A/Cloth", "A-Back07_NS1.spr"),
                                 ("MS", "A-Hat01.spr")):
                path = root / folder / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(spr(0xF800))
            keys, ranking = choose(root, "Player/A/Cloth/A-Back07", 2, 1)
            self.assertEqual(len(keys), 4)
            self.assertEqual([r["key"] for r in ranking], ["Enemy/EN001", "Enemy/EN002"])
            self.assertEqual(keys[-1], "MS/A-Hat01")
            with self.assertRaises(ValueError):
                choose(root, "Player/A/Cloth/Missing", 2, 1)


if __name__ == "__main__":
    unittest.main()
