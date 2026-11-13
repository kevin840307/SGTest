import hashlib
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventory_client import scan


class InventoryClientTests(unittest.TestCase):
    def test_hash_magic_and_relative_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "Enemy").mkdir()
            payload = b"C.D.E.\x00\x01test"
            (root / "Enemy" / "a.spr").write_bytes(payload)
            first = scan(root)
            self.assertEqual(first, scan(root))
            self.assertEqual(first["file_count"], 1)
            self.assertEqual(first["files"][0]["path"], "Enemy/a.spr")
            self.assertEqual(first["files"][0]["magic16"], payload.hex())
            self.assertEqual(first["files"][0]["sha256"], hashlib.sha256(payload).hexdigest())
            self.assertNotIn(str(root), str(first))

    def test_missing_root_fails(self):
        with self.assertRaises(ValueError):
            scan(Path("__missing_original_client__"))


if __name__ == "__main__":
    unittest.main()
