import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from audit_enemy_animations import audit_group
from build_atlas_groups import build_group
from compile_enemy_atlas_catalog import compile_catalog, compile_one


def spr() -> bytes:
    row = struct.pack("<HHH", 1, 0xF800, 0x8000)
    frame = struct.pack("<IIiiI", 1, 1, 0, 0, 20) + row
    return frame + struct.pack("<II", 0, 1)


def act() -> bytes:
    name = b"S\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + b"\x01\x02"
            + struct.pack("<I", 1) + struct.pack("<II", 0, 5) + b"\xff")


class CatalogTests(unittest.TestCase):
    def test_compiles_eight_facings_with_source_hash_and_region(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            enemy = root / "Enemy"
            enemy.mkdir(parents=True)
            paths = [enemy / "EN001_NS1.spr", enemy / "EN001_NS3.spr"]
            for path in paths:
                path.write_bytes(spr())
            act_path = enemy / "EN001.ACT"
            act_path.write_bytes(act())
            audit = audit_group(enemy, act_path)
            atlas = Path(temp) / "atlas"
            build_group(root, atlas, Path("Enemy/EN001"), paths, 512)
            result = compile_catalog({"groups": [audit]}, atlas)
            self.assertEqual((result["compiled_groups"], result["quarantined_groups"]), (1, 0))
            group = result["groups"][0]
            self.assertEqual(len(group["actions"][0]["directions"]), 8)
            facing6 = group["actions"][0]["directions"][5]
            self.assertTrue(facing6["flip_x"])
            self.assertEqual(facing6["frames"][0]["legacy_default_update_calls"], 8)
            self.assertEqual(facing6["frames"][0]["legacy_reference_default_duration_ms"], 160)
            self.assertFalse(result["default_timing_verified"])
            self.assertTrue(group["pages"][0].startswith("Enemy/EN001/"))
            manifest = atlas / "Enemy/EN001/group.atlas.json"
            document = json.loads(manifest.read_text(encoding="utf-8"))
            document["sources"][0]["sha256"] = "bad"
            manifest.write_text(json.dumps(document), encoding="utf-8")
            result = compile_catalog({"groups": [audit]}, atlas)
            self.assertEqual(result["quarantined_groups"], 1)

    def test_verified_default_timing_propagates(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            enemy = root / "Enemy"
            enemy.mkdir(parents=True)
            paths = [enemy / "EN001_NS1.spr", enemy / "EN001_NS3.spr"]
            for path in paths:
                path.write_bytes(spr())
            act_path = enemy / "EN001.ACT"
            act_path.write_bytes(act())
            audit = audit_group(enemy, act_path, timing_verified=True)
            atlas = Path(temp) / "atlas"
            build_group(root, atlas, Path("Enemy/EN001"), paths, 512)
            result = compile_catalog({"groups": [audit]}, atlas)
            self.assertTrue(result["default_timing_verified"])
            self.assertTrue(result["groups"][0]["default_timing_verified"])

    def test_source_filename_matching_is_case_insensitive(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            enemy = root / "Enemy"
            enemy.mkdir(parents=True)
            paths = [enemy / "EN001_NS1.spr", enemy / "EN001_NS3.spr"]
            for path in paths:
                path.write_bytes(spr())
            act_path = enemy / "EN001.ACT"
            act_path.write_bytes(act())
            audit = audit_group(enemy, act_path)
            atlas = Path(temp) / "atlas"
            build_group(root, atlas, Path("Enemy/EN001"), paths, 512)
            manifest = atlas / "Enemy/EN001/group.atlas.json"
            document = json.loads(manifest.read_text(encoding="utf-8"))
            for source in document["sources"]:
                source["path"] = source["path"].lower()
            for frame in document["frames"]:
                frame["source"] = frame["source"].lower()
            manifest.write_text(json.dumps(document), encoding="utf-8")
            result = compile_catalog({"groups": [audit]}, atlas)
            self.assertEqual((result["compiled_groups"], result["quarantined_groups"]), (1, 0))

    def test_incomplete_source_mapping_is_quarantined(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "client"
            enemy = root / "Enemy"
            enemy.mkdir(parents=True)
            path = enemy / "EN001_NS1.spr"
            path.write_bytes(spr())
            act_path = enemy / "EN001.ACT"
            act_path.write_bytes(act())
            audit = audit_group(enemy, act_path)
            self.assertFalse(audit["complete_source_mapping"])
            atlas = Path(temp) / "atlas"
            build_group(root, atlas, Path("Enemy/EN001"), [path], 512)
            result = compile_catalog({"groups": [audit]}, atlas)
            self.assertEqual((result["compiled_groups"], result["quarantined_groups"]), (0, 1))


if __name__ == "__main__":
    unittest.main()
