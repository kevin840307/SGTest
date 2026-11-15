import unittest
from PIL import Image

from asset_converter.atlas.dense_atlas import (
    _single_page_geometry,
    _pack_single,
    _verify_packed_frames,
)
from asset_converter.source.runtime_spr import Frame


class DenseAtlasTests(unittest.TestCase):
    def frame(self, width, height, origin_x=0, origin_y=0, inset=0):
        image = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        if width - inset * 2 > 0 and height - inset * 2 > 0:
            image.paste(
                Image.new("RGBA", (width - inset * 2, height - inset * 2), (71, 129, 77, 255)),
                (inset, inset),
            )
        return Frame(image, origin_x, origin_y)

    def test_height_dense_single_page_preserves_pixels_and_origins(self):
        frames = [
            self.frame(21, 61, 4, 7),
            self.frame(47, 19, -3, 8),
            self.frame(32, 40, 11, -2),
        ]
        rows = []
        for index, frame in enumerate(frames):
            bounds = frame.image.getbbox()
            rows.append((index, bounds, bounds[2] - bounds[0], bounds[3] - bounds[1]))
        rows.sort(key=lambda row: (-row[3], -row[2], row[0]))
        geometry = _single_page_geometry(rows)
        self.assertIsNotNone(geometry)
        pages, records = _pack_single(frames, rows, geometry)
        self.assertEqual(len(pages), 1)
        _verify_packed_frames(frames, pages, records)
        self.assertEqual(records[0]["raw_origin"], [4, 7])
        self.assertEqual(records[1]["raw_origin"], [-3, 8])
        self.assertEqual(records[2]["raw_origin"], [11, -2])

    def test_transparent_frame_needs_no_page(self):
        empty = Frame(Image.new("RGBA", (16, 12), (0, 0, 0, 0)), 2, 3)
        rows = []
        geometry = _single_page_geometry(rows)
        pages, records = _pack_single([empty], rows, geometry)
        self.assertEqual(pages, [])
        self.assertEqual(records[0]["page"], -1)
        self.assertEqual(records[0]["crop"], [0, 0, 0, 0])

    def test_single_page_never_exceeds_4096(self):
        frames = [
            self.frame(2048, 1024),
            self.frame(1024, 1024),
            self.frame(512, 512),
        ]
        rows = []
        for index, frame in enumerate(frames):
            bounds = frame.image.getbbox()
            rows.append((index, bounds, bounds[2] - bounds[0], bounds[3] - bounds[1]))
        rows.sort(key=lambda row: (-row[3], -row[2], row[0]))
        width, height, _ = _single_page_geometry(rows)
        self.assertLessEqual(width, 4096)
        self.assertLessEqual(height, 4096)


if __name__ == "__main__":
    unittest.main()
