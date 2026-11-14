"""Regression tests: dry-run geometry must match the full Atomic Atlas pixel packer."""
import sys
import unittest
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from asset_converter.atlas.build_atlas_groups import (
    measure_source_blocks,
    pack_source_blocks,
)
from asset_converter.source.runtime_spr import Frame


def fixture(spec):
    frames = []
    ranges = []
    for sizes in spec:
        start = len(frames)
        for index, (width, height) in enumerate(sizes):
            image = Image.new("RGBA", (width, height))
            if index % 4 != 3:
                image.paste((220, 20 + index, 60, 255),
                            (1, 1, width - 1, height - 1))
            frames.append(Frame(image, origin_x=index, origin_y=-index))
        ranges.append((start, len(sizes)))
    return frames, ranges


class AtomicCandidateMeasurementTests(unittest.TestCase):
    def assert_equivalent(self, specs, sides):
        frames, ranges = fixture(specs)
        for side in sides:
            with self.subTest(side=side):
                try:
                    real_pages, _ = pack_source_blocks(frames, ranges, side)
                except ValueError:
                    with self.assertRaises(ValueError):
                        measure_source_blocks(frames, ranges, side)
                    continue
                estimated = measure_source_blocks(frames, ranges, side)
                self.assertEqual(estimated["pages"], len(real_pages))
                self.assertEqual(estimated["rgba_bytes"],
                                 sum(img.width * img.height * 4 for img in real_pages))

    def test_mixed_source_dimensions_and_empty_frames(self):
        self.assert_equivalent(
            [[(19, 16), (28, 20), (51, 18), (13, 18)],
             [(41, 27), (40, 27), (18, 26), (15, 24), (50, 40)],
             [(18, 40), (21, 43), (37, 42)]],
            (64, 128, 256, 512, 1024),
        )

    def test_source_requires_second_page(self):
        self.assert_equivalent(
            [[(92, 92), (92, 92), (92, 92)],
             [(40, 40)], [(8, 8)]],
            (64, 96, 128, 256, 512),
        )

    def test_all_transparent_source(self):
        self.assert_equivalent([[(6, 6)] * 4, [(41, 19)] * 3], (64, 512))

    def test_varied_source_counts_and_padding_edges(self):
        self.assert_equivalent(
            [[(505, 505)], [(504, 506)], [(200, 100), (200, 100)]],
            (512, 1024, 2048),
        )


if __name__ == "__main__":
    unittest.main()
