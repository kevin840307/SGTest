import hashlib
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from legacy_asset_inventory import inventory


class InventoryTests(unittest.TestCase):
    def test_known_extensions_and_deterministic_hash(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "NPC").mkdir()
            raw = b"\x00\x01\xfe\xff"
            (root / "NPC" / "NPC001.SPR").write_bytes(raw)
            (root / "Hud.itf").write_bytes(b"ui")
            first = inventory(root, with_sha256=True)
            second = inventory(root, with_sha256=True)
            self.assertEqual(first, second)
            self.assertEqual(first["file_count"], 2)
            self.assertEqual(first["schema_version"], 2)
            self.assertEqual(first["files"][1]["header_hex"], raw.hex())
            self.assertEqual(first["total_bytes"], 6)
            self.assertEqual(first["extension_counts"], {".itf": 1, ".spr": 1})
            self.assertEqual(first["missing_legacy_formats"], [".itd", ".sif"])
            self.assertEqual(first["files"][1]["sha256"], hashlib.sha256(raw).hexdigest())
            self.assertEqual(first["files"][1]["relative_path"], "NPC/NPC001.SPR")

    def test_header_bytes_and_bounds(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / 'image.spr').write_bytes(b'abcdef')
            self.assertEqual(inventory(root, header_bytes=3)['files'][0]['header_hex'], '616263')
            self.assertEqual(inventory(root, header_bytes=0)['files'][0]['header_hex'], '')
            with self.assertRaises(ValueError):
                inventory(root, header_bytes=65)

    def test_root_must_exist(self):
        with self.assertRaises(ValueError):
            inventory(Path("/some-nonexistent-folder/sgtest-asset-inventory"), True)

    def test_skip_symlinks(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "originals"
            root.mkdir()
            outside = Path(temp) / "outside.spr"
            outside.write_bytes(b"secret")
            try:
                (root / "unsafe.spr").symlink_to(outside)
            except (OSError, NotImplementedError):
                self.skipTest("symlinks unavailable")
            result = inventory(root, True)
            self.assertEqual(result["file_count"], 0)
            self.assertEqual(result["skipped_symbolic_links"], 1)


if __name__ == "__main__":
    unittest.main()
