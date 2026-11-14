import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_ground_candidates import build_candidates, sample_indices


def tiny_spr() -> bytes:
    frames = []
    for color in (0xF800, 0x07E0, 0x001F):
        row = struct.pack("<HHH", 1, color, 0x8000)
        start = sum(len(part) for part in frames)
        frames.append(struct.pack("<IIiiI", 1, 1, 0, 0, start + 20) + row)
    offsets = [sum(len(part) for part in frames[:index]) for index in range(3)]
    return b"".join(frames) + struct.pack("<IIII", *offsets, 3)


class GroundCandidateTests(unittest.TestCase):
    def test_even_sample_and_identical_pixels_across_variants(self):
        self.assertEqual(sample_indices(500, 25), list(range(0, 500, 20)))
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "ground.spr"
            source.write_bytes(tiny_spr())
            output = Path(temp) / "output"
            report = build_candidates(source, output, 3)
            self.assertEqual(report["sample_indices"], [0, 1, 2])
            pixels = []
            for variant in ("individual", "1024", "2048", "4096"):
                path = output / variant
                catalog = json.loads((path / "catalog.json").read_text(encoding="utf-8"))
                values = []
                for record in catalog["frames"]:
                    with Image.open(path / catalog["pages"][record["page"]]) as page:
                        x, y, w, h = record["rect"]
                        values.append(page.crop((x, y, x + w, y + h)).tobytes())
                pixels.append(values)
            self.assertTrue(all(value == pixels[0] for value in pixels))


if __name__ == "__main__":
    unittest.main()
