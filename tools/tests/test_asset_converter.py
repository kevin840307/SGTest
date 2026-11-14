"""Portable fixture tests for Adaptive Atomic Atlas, without copyrighted content."""

import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
sys.path.insert(0, str(REPO / "tools" / "asset_converter"))
from asset_converter.atlas.adaptive_atlas import build_group, POLICY
from asset_converter.cli import archive_all, check_runtime


def tiny_spr(rgb=0xF800):
    row = struct.pack("<HHHH", 1, rgb, 0x8001, 0x8000)
    frame = struct.pack("<IIiiI", 2, 1, 1, 0, 20) + row
    return frame + struct.pack("<II", 0, 1)


class Settings:
    category = ["Enemy"]
    preview_groups = 2
    skip_sidecars = True
    force = False


class ConverterTests(unittest.TestCase):
    def test_all_project_converter_dependencies_are_local(self):
        import importlib

        package = REPO / "tools" / "asset_converter"
        modules = [
            "adaptive_atlas",
            "selector",
            "animation_definition",
            "asset_bundle_builder",
            "asset_pipeline",
            "audit_character_layers",
            "build_assets",
            "build_atlas_groups",
            "build_enemy_map_palette",
            "build_maps",
            "build_population",
            "convert_assets",
            "extract_client",
            "inspect_act",
            "legacy_direction",
            "runtime_animation_semantics",
            "runtime_spr",
            "cli",
        ]
        group_by_module = {
            "extract_client": "source",
            "convert_assets": "source",
            "runtime_spr": "source",
            "inspect_act": "source",
            "legacy_direction": "source",
            "build_assets": "source",
            "adaptive_atlas": "atlas", "selector": "atlas",
            "build_atlas_groups": "atlas",
            "asset_bundle_builder": "content",
            "animation_definition": "content",
            "runtime_animation_semantics": "content",
            "audit_character_layers": "content",
            "asset_pipeline": "content",
            "build_maps": "content",
            "build_enemy_map_palette": "content",
            "build_population": "content",
        }
        for name in modules:
            mod = importlib.import_module(
                "asset_converter."
                + (group_by_module.get(name, "") + ("." if name != "cli" else ""))
                + name
            )
            self.assertTrue(
                Path(mod.__file__).resolve().is_relative_to(package),
                "converter dependency must be packaged locally: " + name,
            )

    def test_atomic_build_incremental_repair_and_source_edit(self):
        with tempfile.TemporaryDirectory() as t:
            src = Path(t) / "client"
            dst = Path(t) / "out"
            src.joinpath("Enemy").mkdir(parents=True)
            files = [src / "Enemy" / "EN001_NS1.spr", src / "Enemy" / "EN001_NW1.spr"]
            for p in files:
                p.write_bytes(tiny_spr())
            key = Path("Enemy/EN001")
            first = build_group(src, dst, key, files)
            self.assertEqual(first["status"], "converted")
            doc = json.loads((dst / key / "atlas.json").read_text())
            self.assertEqual(doc["packing_policy"], POLICY)
            self.assertEqual(len(doc["frames"]), 2)
            self.assertEqual(len(doc["sources"]), 2)
            self.assertEqual(first["pages"], 1)
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")
            page = dst / key / doc["pages"][0]
            page.write_bytes(b"garbled")
            self.assertEqual(build_group(src, dst, key, files)["status"], "converted")
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")
            before = json.loads((dst / key / "atlas.json").read_text())["source_digest"]
            files[0].write_bytes(tiny_spr(0x001F))
            self.assertEqual(build_group(src, dst, key, files)["status"], "converted")
            after = json.loads((dst / key / "atlas.json").read_text())["source_digest"]
            self.assertNotEqual(before, after)
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")

    def test_archive_preview_only_selected_groups(self):
        with tempfile.TemporaryDirectory() as t:
            src = Path(t) / "client"
            dst = Path(t) / "archive"
            src.joinpath("Enemy").mkdir(parents=True)
            src.joinpath("Interface").mkdir()
            for name in ("EN001_NS1.spr", "EN001_NW1.spr", "EN002_NS1.spr"):
                (src / "Enemy" / name).write_bytes(tiny_spr())
            (src / "Interface" / "test.spr").write_bytes(tiny_spr())
            obj = archive_all(src, dst, Settings())
            self.assertEqual(obj["groups_expected"], 2)
            self.assertEqual(len(obj["groups"]), 2)
            self.assertFalse(obj["errors"])
            self.assertEqual(obj["total_frames"], 3)
            self.assertTrue((dst / "index.json").is_file())
            self.assertFalse((dst / "sprites" / "Interface").exists())

    def test_runtime_manifest_validation_rejects_unsafe_or_bad_rect(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            for name, relative in (
                ("map", "bundles/map/13/sprites.json"),
                ("session", "bundles/session/player-b-base.json"),
            ):
                manifest = root / relative
                manifest.parent.mkdir(parents=True)
                (manifest.parent / "page_0.png").write_bytes(b"test")
                doc = {
                    "pages": ["page_0.png"],
                    "page_sizes": [[16, 16]],
                    "frames": [{"page": 0, "rect": [1, 1, 5, 5]}],
                    "bundle_scope": name,
                    "packing_policy": "test",
                }
                manifest.write_text(json.dumps(doc))
            self.assertEqual(check_runtime(root, 13)["map"]["frames"], 1)
            target = root / "bundles" / "map" / "13" / "sprites.json"
            bad = json.loads(target.read_text())
            bad["frames"][0]["rect"] = [20, 10, 5, 5]
            target.write_text(json.dumps(bad))
            with self.assertRaises(ValueError):
                check_runtime(root, 13)


if __name__ == "__main__":
    unittest.main()
