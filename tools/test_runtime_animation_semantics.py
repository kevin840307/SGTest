import struct
import tempfile
import unittest
from pathlib import Path

from animation_definition import compile_definition
from runtime_animation_semantics import apply_overlay, logical_views
from test_assets import spr


def action(key, loop, steps):
    name = key.encode("ascii") + b"\0"
    return (b"\x01" + struct.pack("<I", len(name)) + name
            + b"\x03" + struct.pack("<I", 1) + bytes([loop, 2])
            + struct.pack("<I", len(steps))
            + b"".join(struct.pack("<II", *pair) for pair in steps))


class RuntimeAnimationSemanticTests(unittest.TestCase):
    def test_logical_view_overrides_existing_noncanonical_physical_clip(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            act = root / "E.ACT"
            act.write_bytes(action("S", 1, [(0, 5)]) + b"\xff")
            sources = []
            for direction in (1, 2, 3):
                path = root / f"E_NS{direction}.spr"
                path.write_bytes(spr())
                sources.append(path)
            definition = compile_definition("enemy/e", sources, root, "monster", act)
            manifest = {
                "version": 7,
                "frames": [{"page": 0}, {"page": 0}, {"page": 0}],
                "clips": {"E_NS1": [0], "E_NS2": [1], "E_NS3": [2]},
                "aliases": {}, "act": {}, "pages": ["page.png"], "page_sizes": [[1, 1]],
            }
            patched = apply_overlay(definition, manifest)
            self.assertIn("E_NS2", patched["clips"], "physical source remains available")
            self.assertEqual(patched["logical_views"]["E_NS2"]["clip"], "E_NS1")
            self.assertFalse(patched["logical_views"]["E_NS2"]["flip_h"])
            self.assertEqual(patched["logical_views"]["E_NS8"]["clip"], "E_NS1")
            self.assertTrue(patched["logical_views"]["E_NS8"]["flip_h"])
            self.assertEqual(patched["logical_views"]["E_NS1"]["timeline_ticks"], [[0, 8]])
            self.assertEqual(patched["normalized_schema_version"], 2)

    def test_missing_runtime_source_fails_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            act = root / "E.ACT"
            act.write_bytes(action("S", 1, [(0, 5)]) + b"\xff")
            sources = []
            for direction in (1, 3):
                path = root / f"E_NS{direction}.spr"
                path.write_bytes(spr())
                sources.append(path)
            definition = compile_definition("enemy/e", sources, root, "monster", act)
            manifest = {"version": 7, "frames": [{"page": 0}], "clips": {"E_NS1": [0]}}
            with self.assertRaisesRegex(ValueError, "E_NS3"):
                apply_overlay(definition, manifest)

    def test_character_view_keeps_draw_order_and_full_action_key(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            act = root / "B.ACT"
            act.write_bytes(action("AS", 1, [(0, 5)]) + b"\xff")
            names = root / "names.dat"
            names.write_text("NS,NR,NU,NB,AS,AH,AA,AB,AG,GS,GH,GA,GB,", encoding="ascii")
            draw = root / "draw.dat"
            draw.write_text("AS1: Body+2>Head\nAS2: Head>Body+2\nAS3: Body+2>Head\n", encoding="ascii")
            sources = []
            for direction in (1, 2, 3):
                path = root / f"B-BODY_AS{direction}.spr"
                path.write_bytes(spr())
                sources.append(path)
            definition = compile_definition("player/b/body", sources, root, "character_part", act, names, draw)
            view = logical_views(definition)["B-BODY_AS4"]
            self.assertEqual(view["clip"], "B-BODY_AS3")
            self.assertTrue(view["flip_h"])
            self.assertEqual(view["act_key"], "AS")
            self.assertEqual(view["draw_slots"], [10, 9, 0])


if __name__ == "__main__":
    unittest.main()
