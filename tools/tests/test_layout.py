"""Regression tests for production grouping and the same-asset page fallback."""
from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from asset_converter.atlas.layout import group_key, pack_atomic_rectangles, select_groups


class LayoutTests(unittest.TestCase):
    def test_grouping_keeps_independent_asset_identities(self):
        self.assertEqual(group_key(Path("Enemy/En023A_NW1.spr")), Path("Enemy/EN023A"))
        self.assertEqual(group_key(Path("Enemy/EN023A_FA1.spr")), Path("Enemy/EN023A"))
        self.assertEqual(group_key(Path("Map/Gaea/a.spr")), Path("Map/Gaea/a"))
        self.assertNotEqual(group_key(Path("Map/Gaea/a.spr")), group_key(Path("Map/Gaea/b.spr")))

    def test_group_selection_is_deterministic_and_filters_without_merging(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "Enemy").mkdir()
            (root / "Map").mkdir()
            for name in ("EN023A_NW1.spr", "EN023A_NS1.spr", "EN024A_NS1.spr"):
                (root / "Enemy" / name).write_bytes(b"fixture")
            (root / "Map" / "tree.spr").write_bytes(b"fixture")
            results = select_groups(root)
            self.assertEqual(len(results), 3)
            self.assertEqual(
                [p.name for p in results[Path("Enemy/EN023A")]],
                ["EN023A_NS1.spr", "EN023A_NW1.spr"],
            )
            chosen = select_groups(root, category="enemy", keys={"enemy/en023a"})
            self.assertEqual(list(chosen), [Path("Enemy/EN023A")])

    def test_single_asset_fallback_never_overlaps_pages(self):
        blocks = [(0, 250, 200), (1, 200, 250), (2, 240, 240), (3, 100, 100)]
        placements, bounds = pack_atomic_rectangles(blocks, 256)
        sizes = {index: (w, h) for index, w, h in blocks}
        self.assertGreaterEqual(len(bounds), 2)
        for index, (page, x, y) in placements.items():
            w, h = sizes[index]
            self.assertLessEqual(x + w, bounds[page][0])
            self.assertLessEqual(y + h, bounds[page][1])
            for other in range(index):
                if placements[other][0] != page:
                    continue
                ox, oy = placements[other][1:]
                ow, oh = sizes[other]
                self.assertTrue(x + w <= ox or ox + ow <= x or y + h <= oy or oy + oh <= y)

    def test_fallback_rejects_oversize_and_is_deterministic(self):
        blocks = [(0, 70, 40), (1, 50, 110), (2, 25, 16)]
        self.assertEqual(pack_atomic_rectangles(blocks, 128), pack_atomic_rectangles(blocks, 128))
        with self.assertRaises(ValueError):
            pack_atomic_rectangles([(0, 4097, 16)], 4096)


if __name__ == "__main__":
    unittest.main()
