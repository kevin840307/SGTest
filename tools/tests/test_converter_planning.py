"""Planning must match real packing while allocating no texture pages."""
import random
import json
import tempfile
import sys
import unittest
from pathlib import Path
from unittest.mock import patch
from PIL import Image

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_converter.atlas.build_atlas_groups import measure_source_blocks, pack_source_blocks
from asset_converter.source.runtime_spr import Frame
from asset_converter.atlas.adaptive_atlas import build_group
from test_asset_converter import tiny_spr


class PlanningTests(unittest.TestCase):
    def test_invalid_json_type_is_rebuilt_as_a_cache_miss(self):
        with tempfile.TemporaryDirectory() as temp:
            source,output = Path(temp)/'source',Path(temp)/'output'
            source.mkdir()
            path = source/'one.spr'
            path.write_bytes(tiny_spr())
            manifest = output/'one/atlas.json'
            manifest.parent.mkdir(parents=True)
            manifest.write_text('[]')
            self.assertEqual(build_group(source,output,Path('one'),[path])['status'],'converted')
            self.assertIsInstance(json.loads(manifest.read_text()),dict)

    def test_bad_planning_cost_fails_before_publishing(self):
        with tempfile.TemporaryDirectory() as temp:
            source,output = Path(temp)/'source',Path(temp)/'output'
            source.mkdir()
            path = source/'one.spr'
            path.write_bytes(tiny_spr())
            wrong = [{'valid':True,'side':512,'pages':1,'rgba_bytes':4}]
            with patch('asset_converter.atlas.adaptive_atlas._measure_candidates',return_value=wrong):
                with self.assertRaisesRegex(ValueError,'differs from actual packed'):
                    build_group(source,output,Path('one'),[path])
            self.assertFalse((output/'one/atlas.json').exists())

    def test_geometry_matches_painted_pages_and_atomic_failures(self):
        rng = random.Random(209)
        for case in range(40):
            frames = [Frame(Image.new('RGBA',(rng.randrange(1,35),rng.randrange(1,35)),
                      (0,0,0,0) if rng.random()<.15 else (10,20,30,255)),0,0)
                      for _ in range(rng.randrange(1,15))]
            split = len(frames)//2
            ranges = [(0,split),(split,len(frames)-split)] if split else [(0,len(frames))]
            for side in [32,64,128,512]:
                with self.subTest(case=case,side=side):
                    try:
                        pages,_ = pack_source_blocks(frames,ranges,side)
                        expected = (len(pages),sum(page.width*page.height*4 for page in pages))
                    except ValueError:
                        with self.assertRaises(ValueError):
                            measure_source_blocks(frames,ranges,side)
                        continue
                    measured = measure_source_blocks(frames,ranges,side)
                    self.assertEqual((measured['pages'],measured['rgba_bytes']),expected)

    def test_planning_allocates_no_image_surfaces(self):
        frames = [Frame(Image.new('RGBA',(30,40),(255,0,0,255)),2,3)]*20
        with patch('PIL.Image.new',side_effect=AssertionError('planning allocated an image surface')):
            self.assertTrue(measure_source_blocks(frames,[(0,20)],512)['valid'])

    def test_transparent_source_has_no_page(self):
        frames = [Frame(Image.new('RGBA',(20,20)),0,0)]*3
        measured = measure_source_blocks(frames,[(0,3)],512)
        pages,records = pack_source_blocks(frames,[(0,3)],512)
        self.assertEqual(measured['pages'],0)
        self.assertEqual(measured['rgba_bytes'],0)
        self.assertEqual(pages,[])
        self.assertEqual([record['page'] for record in records],[-1,-1,-1])


if __name__ == '__main__':
    unittest.main()
