"""Fail-closed SGTest Pet DefinitionId -> HK visual catalog tests."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.content.build_pet_runtime_catalog import (
    compile_pet_runtime_catalog, publish_pet_runtime_catalog,
)


def write_atlas(root: Path, sprite: str = "EN050A") -> None:
    target = root / "Enemy" / sprite.casefold() / "atlas.json"
    target.parent.mkdir(parents=True, exist_ok=True)
    views = {}
    clips = {}
    for action in ("NS", "FA"):
        for facing in range(1, 9):
            name = f"{sprite}_{action}{facing}"
            clips[name] = [0]
            views[name] = {
                "clip": name,
                "flip_h": False,
                "timeline_ticks": [[0, 2]],
                "loop": action == "NS",
            }
    target.write_text(json.dumps({
        "version": 9,
        "asset_id": "Enemy/" + sprite.casefold(),
        "clips": clips,
        "logical_views": views,
        "animation_semantics": {
            "timing_verified": True,
            "direction_mapping_verified": True,
        },
    }), encoding="utf-8")


class PetRuntimeCatalogTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.assets = self.root / "assets"
        write_atlas(self.assets)
        self.evidence = self.root / "evidence.json"
        self.evidence.write_text(json.dumps({
            "version": 1,
            "kind": "hk_pet_visual_evidence",
            "status": "pass",
            "source_identity_status": "CONFIRMED",
            "content_sha256": "a" * 64,
            "mapping_count": 1,
            "mappings": [{
                "source_pet_id": 5100,
                "evolution_id": 0,
                "sprite": "EN050A",
                "asset_id": "Enemy/en050a",
            }],
        }), encoding="utf-8")
        self.bridge = self.root / "bridge.json"
        self.bridge.write_text(json.dumps({
            "schema": "sgtest.pet_definition_bridge/v1",
            "assignment": "sgtest_owned",
            "source_reference": "unit fixture",
            "mappings": [{"definition_id": 100001, "source_pet_id": 5100}],
        }), encoding="utf-8")

    def test_catalog_preserves_modern_definition_and_marks_action_semantics_inferred(self):
        result = compile_pet_runtime_catalog(
            self.evidence, self.bridge, self.assets)
        self.assertEqual(result["definition_count"], 1)
        self.assertEqual(result["evolution_count"], 1)
        self.assertEqual(result["action_semantics_status"], "INFERRED")
        self.assertEqual(result["definition_assignment"], "SGTEST_OWNED")
        self.assertEqual(result["required_assets"], ["Enemy/en050a"])
        definition = result["definitions"]["100001"]
        self.assertEqual(definition["source_pet_id"], 5100)
        visual = definition["evolutions"]["0"]
        self.assertEqual(visual["asset"], "Enemy/en050a")
        self.assertEqual(visual["idle_view"], "EN050A_NS1")
        self.assertEqual(visual["attack_view"], "EN050A_FA1")
        self.assertNotEqual(100001, 5100)

    def test_unknown_source_and_duplicate_bridge_fail_closed(self):
        bad = json.loads(self.bridge.read_text(encoding="utf-8"))
        bad["mappings"][0]["source_pet_id"] = 5999
        self.bridge.write_text(json.dumps(bad), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "has no HK visual evidence"):
            compile_pet_runtime_catalog(self.evidence, self.bridge, self.assets)

        bad["mappings"] = [
            {"definition_id": 100001, "source_pet_id": 5100},
            {"definition_id": 100002, "source_pet_id": 5100},
        ]
        self.bridge.write_text(json.dumps(bad), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate source PetID"):
            compile_pet_runtime_catalog(self.evidence, self.bridge, self.assets)

    def test_missing_runtime_atlas_fails_and_publish_is_deterministic(self):
        empty = self.root / "empty-assets"
        empty.mkdir()
        with self.assertRaisesRegex(ValueError, "missing runtime Pet Enemy atlas"):
            compile_pet_runtime_catalog(self.evidence, self.bridge, empty)

        output = self.root / "catalog.json"
        first = publish_pet_runtime_catalog(
            self.evidence, self.bridge, self.assets, output)
        before = output.read_bytes()
        second = publish_pet_runtime_catalog(
            self.evidence, self.bridge, self.assets, output)
        self.assertEqual(before, output.read_bytes())
        self.assertEqual(first["content_sha256"], second["content_sha256"])


if __name__ == "__main__":
    unittest.main()
