import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inspect_map_refs import references, scan


class MapReferenceTests(unittest.TestCase):
    def test_manifest_paths_and_missing_detection(self):
        raw = b"[Map]\nTiles = Map\\Map01\\Map.pic\nBlocking = Map\\Map01\\Blocking.dat\n"
        self.assertEqual(references(raw)["Tiles"], "Map/Map01/Map.pic")
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "Map" / "Map01").mkdir(parents=True)
            (root / "Map" / "Map1.map").write_bytes(raw)
            (root / "Map" / "Map01" / "Map.pic").write_bytes(b"pic")
            result = scan(root)
            self.assertEqual(result["by_key"]["Tiles"]["present"], 1)
            self.assertEqual(result["by_key"]["Blocking"]["present"], 0)
            self.assertEqual(result["missing"][0]["key"], "Blocking")


if __name__ == "__main__":
    unittest.main()
