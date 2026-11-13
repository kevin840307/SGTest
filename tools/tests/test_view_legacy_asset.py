import json
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from convert_assets import Frame, pack
from view_legacy_asset import read_frames, render_preview


class ViewerTests(unittest.TestCase):
    def test_atlas_manifest_restores_crop_and_origin(self):
        image = Image.new("RGBA", (8, 6))
        image.putpixel((2, 3), (255, 0, 0, 255))
        pages, records = pack([Frame(image, 2, 3)], 16)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            pages[0].save(root / "page.png")
            (root / "sample.json").write_text(json.dumps({"source": "sample.spr",
                "pages": ["page.png"], "frames": records}), encoding="utf-8")
            frames, source = read_frames(root / "sample.json")
            self.assertEqual(source, "sample.spr")
            self.assertEqual(frames[0].image.tobytes(), image.tobytes())
            self.assertEqual((frames[0].origin_x, frames[0].origin_y), (2, 3))

    def test_mirror_preview_moves_pixel(self):
        image = Image.new("RGBA", (3, 1))
        image.putpixel((0, 0), (255, 0, 0, 255))
        preview = render_preview(Frame(image, 0, 0), 1, True, False)
        self.assertEqual(preview.getpixel((2, 0)), (255, 0, 0))


if __name__ == "__main__":
    unittest.main()
