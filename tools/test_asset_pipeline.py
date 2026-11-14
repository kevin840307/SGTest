import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from animation_definition import compile_definition
from asset_bundle_builder import build_bundle, choose_candidate
from asset_pipeline import prune_legacy_runtime_assets, resolve_server_dat
from test_assets import spr


def action(key: str):
    name = key.encode("ascii") + b"\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + bytes([1, 2])
            + struct.pack("<I", 1) + struct.pack("<II", 0, 5))


class AssetPipelineTests(unittest.TestCase):
    def definition(self, root: Path, prefix: str) -> dict:
        enemy = root / "enemy"
        enemy.mkdir(exist_ok=True)
        sources = []
        for direction in (1, 3):
            path = enemy / f"{prefix.lower()}_ns{direction}.spr"
            path.write_bytes(spr())
            sources.append(path)
        act_path = enemy / f"{prefix.lower()}.act"
        act_path.write_bytes(action("S") + b"\xff")
        return compile_definition(
            f"enemy/{prefix.lower()}", sources, root, "monster", act_path
        )

    def test_bundle_merges_entities_but_keeps_semantic_prefixes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            a = self.definition(root, "ENA")
            b = self.definition(root, "ENB")
            output = root / "out" / "sprites.json"
            report = build_bundle(
                [a, b], root, output, "map:99:sprites", "map", side=512
            )
            document = json.loads(output.read_text(encoding="utf-8"))
            self.assertEqual(report["entities"], ["enemy/ena", "enemy/enb"])
            self.assertEqual(document["bundle_scope"], "map")
            self.assertEqual(document["normalized_schema_version"], 2)
            self.assertIn("ENA_NS2", document["logical_views"])
            self.assertIn("ENB_NS2", document["logical_views"])
            self.assertEqual(document["logical_views"]["ENA_NS2"]["clip"], "ENA_NS1")
            self.assertEqual(document["logical_views"]["ENB_NS2"]["clip"], "ENB_NS1")
            self.assertGreaterEqual(len(document["pages"]), 1)

    def test_candidate_uses_memory_then_page_count_with_small_overhead(self):
        rows = [
            {"side": 512, "valid": True, "pages": 4, "rgba_bytes": 100},
            {"side": 1024, "valid": True, "pages": 2, "rgba_bytes": 103},
            {"side": 2048, "valid": True, "pages": 1, "rgba_bytes": 105},
            {"side": 4096, "valid": True, "pages": 1, "rgba_bytes": 130},
        ]
        self.assertEqual(choose_candidate(rows, 0.05)["side"], 2048)
        self.assertEqual(choose_candidate(rows, 0.02)["side"], 512)
        near_equal_one_page = [
            {"side": 2048, "valid": True, "pages": 1, "rgba_bytes": 1005},
            {"side": 4096, "valid": True, "pages": 1, "rgba_bytes": 1000},
        ]
        self.assertEqual(choose_candidate(near_equal_one_page, 0.05)["side"], 2048)

    def test_server_dat_auto_discovery_requires_exactly_one_candidate(self):
        with tempfile.TemporaryDirectory() as temp:
            old_root = Path(temp)
            target = old_root / "server-a" / "Dat"
            target.mkdir(parents=True)
            self.assertEqual(resolve_server_dat(None, old_root), target.resolve())
            second = old_root / "server-b" / "Dat"
            second.mkdir(parents=True)
            with self.assertRaisesRegex(ValueError, "uniquely"):
                resolve_server_dat(None, old_root)

    def test_prune_removes_only_legacy_generated_atlases(self):
        with tempfile.TemporaryDirectory() as temp:
            assets = Path(temp)
            enemy = assets / "enemy"
            player = assets / "player/b/standard"
            bundle = assets / "bundles/map/13"
            enemy.mkdir(parents=True)
            player.mkdir(parents=True)
            bundle.mkdir(parents=True)
            (enemy / "old_0.png").write_bytes(b"png")
            (enemy / "old.json").write_text(
                json.dumps({"pages": ["old_0.png"]}), encoding="utf-8"
            )
            (player / "body_0.png").write_bytes(b"png")
            (player / "b-body.json").write_text(
                json.dumps({"pages": ["body_0.png"]}), encoding="utf-8"
            )
            (bundle / "sprites_0.png").write_bytes(b"keep")
            (bundle / "sprites.json").write_text(
                json.dumps({"pages": ["sprites_0.png"]}), encoding="utf-8"
            )
            removed = prune_legacy_runtime_assets(assets, ["OLD"])
            self.assertEqual(removed, 4)
            self.assertFalse((enemy / "old.json").exists())
            self.assertFalse((player / "b-body.json").exists())
            self.assertTrue((bundle / "sprites.json").exists())
            self.assertTrue((bundle / "sprites_0.png").exists())


if __name__ == "__main__":
    unittest.main()
