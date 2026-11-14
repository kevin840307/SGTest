import json
import struct
import tempfile
import unittest
from pathlib import Path

from animation_definition import compile_definition
from build_normalized_atlas_candidates import (classify_tiers, choose_candidate, semantic_view,
                                                   validate_runtime_semantics)
from test_assets import spr


def action(key, loop, steps):
    name = key.encode("ascii") + b"\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + bytes([loop, 2])
            + struct.pack("<I", len(steps))
            + b"".join(struct.pack("<II", *pair) for pair in steps))


class NormalizedAtlasCandidateTests(unittest.TestCase):
    def test_candidate_policies_use_all_valid_sizes_without_hiding_fallback(self):
        rows = [
            {"side": 512, "valid": False},
            {"side": 1024, "valid": True, "pages": 3, "rgba_bytes": 100},
            {"side": 2048, "valid": True, "pages": 1, "rgba_bytes": 105},
            {"side": 4096, "valid": True, "pages": 1, "rgba_bytes": 130},
        ]
        self.assertEqual(choose_candidate(rows, "preferred", preferred_side=512)["side"], 1024)
        self.assertEqual(choose_candidate(rows, "memory")["side"], 1024)
        self.assertEqual(choose_candidate(rows, "balanced", "hot")["side"], 2048)
        self.assertEqual(choose_candidate(rows, "balanced", "cold")["side"], 1024)
        self.assertEqual(choose_candidate(rows, "pages")["side"], 2048)

    def test_spawn_coverage_tiers_are_deterministic(self):
        population = {
            "definitions": [
                {"id": 1, "atlas": "enemy/a.json"},
                {"id": 2, "atlas": "enemy/b.json"},
                {"id": 3, "atlas": "enemy/c.json"},
                {"id": 4, "atlas": "enemy/d.json"},
            ],
            "spawns": ([{"definition": 1}] * 8
                       + [{"definition": 2}] * 2
                       + [{"definition": 3}] * 1
                       + [{"definition": 4}] * 1),
        }
        tiers = classify_tiers(population)
        self.assertEqual(tiers["enemy/a"]["tier"], "hot")
        self.assertEqual(tiers["enemy/b"]["tier"], "hot")
        self.assertEqual(tiers["enemy/c"]["tier"], "warm")
        self.assertEqual(tiers["enemy/d"]["tier"], "warm")
        self.assertEqual(sum(row["spawn_count"] for row in tiers.values()), 12)

    def test_semantics_remove_existing_but_noncanonical_direction(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            act = root / "E.ACT"
            act.write_bytes(action("S", 1, [(0, 5)]) + b"\xff")
            sources = []
            # Include NS2 physically on purpose. Reference monster mapping for
            # action S says logical facing 2 reuses NS1, so NS2 must not win.
            for direction in (1, 2, 3):
                path = root / f"E_NS{direction}.spr"
                path.write_bytes(spr())
                sources.append(path)
            definition = compile_definition("enemy/e", sources, root, "monster", act)
            view = semantic_view(definition)
            self.assertNotIn("E_NS2", view["canonical_clips"])
            self.assertEqual(view["aliases"]["E_NS2"], {"clip": "E_NS1", "flip_h": False})
            self.assertEqual(view["aliases"]["E_NS8"], {"clip": "E_NS1", "flip_h": True})
            self.assertIn("E_NS1", view["canonical_clips"])
            self.assertIn("E_NS3", view["canonical_clips"])
            manifest = {
                "normalized_schema_version": 2,
                "clips": {name: [0] for name in view["canonical_clips"]},
                "aliases": view["aliases"],
                "act": view["act"],
                "logical_views": {
                    requested: {
                        "clip": logical["clip"],
                        "flip_h": logical["flip_h"],
                        "timeline_ticks": [[0, 8]],
                        "loop": True,
                    }
                    for requested, logical in {
                        "E_NS1": {"clip": "E_NS1", "flip_h": False},
                        "E_NS2": {"clip": "E_NS1", "flip_h": False},
                        "E_NS3": {"clip": "E_NS3", "flip_h": False},
                        "E_NS4": {"clip": "E_NS3", "flip_h": True},
                        "E_NS5": {"clip": "E_NS3", "flip_h": True},
                        "E_NS6": {"clip": "E_NS1", "flip_h": True},
                        "E_NS7": {"clip": "E_NS1", "flip_h": True},
                        "E_NS8": {"clip": "E_NS1", "flip_h": True},
                    }.items()
                },
            }
            validate_runtime_semantics(definition, manifest)
            # A same-named physical clip is harmless because logical_views wins.
            manifest["clips"]["E_NS2"] = [0]
            validate_runtime_semantics(definition, manifest)
            manifest["logical_views"]["E_NS2"]["clip"] = "E_NS2"
            with self.assertRaisesRegex(ValueError, "runtime resolves"):
                validate_runtime_semantics(definition, manifest)


if __name__ == "__main__":
    unittest.main()
