import importlib.util
from pathlib import Path
import tempfile
import unittest
from PIL import Image

from report_runtime_baseline import lifecycle_summary, multiplayer_summary, quantile, collect, markdown
from experiments.atlas_builder import convert_group
from experiments.ordered_atlas import ordered_layout
from runtime_spr import decode_spr
from test_assets import spr

spec = importlib.util.spec_from_file_location('baseline_runner', Path(__file__).with_name('benchmark_runtime_baseline.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class BaselineReportTests(unittest.TestCase):
    def test_nearest_rank_and_missing_measurements(self):
        self.assertEqual(quantile(range(1, 101), .95), 95)
        self.assertIsNone(quantile([], .99))
        missing = multiplayer_summary({'execution_passed': True})
        self.assertEqual(missing['gates']['execution'], 'PASS')
        self.assertEqual(missing['gates']['frame_p99'], 'NOT VERIFIED')
        self.assertEqual(missing['gates']['client_ram'], 'NOT VERIFIED')
        self.assertEqual(missing['gates']['runtime_owner_lru_logout'], 'NOT VERIFIED')

    def test_render_success_does_not_hide_memory_failure(self):
        result = multiplayer_summary({'execution_passed': True,
            'rendered': {'frame_ms_p95': 20, 'frame_ms_p99': 30},
            'samples': [[{'rss_bytes': 70*1048576, 'cpu_one_core_percent': 5},
                         {'rss_bytes': 201*1048576, 'cpu_one_core_percent': 45}]]})
        self.assertEqual(result['gates']['frame_p99'], 'PASS')
        self.assertEqual(result['gates']['client_ram'], 'FAIL')
        self.assertEqual(result['gates']['server_cpu'], 'PASS')

    def test_warmup_is_excluded_but_long_loading_frame_fails(self):
        record = {'warmup': False, 'pages': 1, 'after_unload': {'godot_static_bytes': 10},
                  'loading': {'rgba_bytes': 4, 'decode_ms': [2], 'upload_submit_ms': [1],
                              'gpu_sync_wait_ms': [.1], 'load_wall_ms': 4}}
        result = lifecycle_summary({'records': [dict(record, warmup=True,
            loading=dict(record['loading'], decode_ms=[999])), record],
            'warm_baseline': {'godot_static_bytes': 10}, 'frame_ms_p99': 41, 'frame_ms_max': 50})
        self.assertEqual(result['decode_p95_ms'], 2)
        self.assertEqual(result['gates']['loading_frame'], 'FAIL')
        self.assertEqual(result['gates']['thirty_cycles'], 'FAIL')
        self.assertEqual(result['gates']['rss_recovery'], 'NOT VERIFIED')

    def test_report_does_not_invent_unmeasured_candidates(self):
        with tempfile.TemporaryDirectory() as temp:
            report = collect(Path(temp))
            self.assertEqual(report['runs'], [])
            self.assertIn('NOT SELECTED', report['selection'])
            self.assertIn('30～90FPS', markdown(report))

    def test_identity_catches_page_change_and_is_checkout_location_independent(self):
        import json
        import os
        with tempfile.TemporaryDirectory() as temp:
            previous = Path.cwd()
            try:
                os.chdir(temp)
                root = Path('client/assets')
                (root/'maps/13').mkdir(parents=True)
                (root/'enemy').mkdir()
                (root/'maps/13/population.json').write_text(json.dumps({'definitions': [{'atlas':'enemy/a.json'}]}))
                (root/'enemy/a.json').write_text(json.dumps({'version': 9, 'shape':'readable',
                    'pages':['a.png'], 'page_sizes':[[1,1]]}))
                (root/'enemy/a.png').write_bytes(b'fixture')
                first = runner.asset_identity()
                absolute = runner.asset_identity(root.resolve())
                self.assertEqual(first['content_sha256'], absolute['content_sha256'])
                (root/'enemy/a.png').write_bytes(b'changed')
                self.assertNotEqual(first['content_sha256'], runner.asset_identity()['content_sha256'])
            finally:
                os.chdir(previous)


class CandidatePackingTests(unittest.TestCase):
    def test_readable_source_pixels_origins_and_clip_locality(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)/'source'
            root.mkdir()
            paths = []
            for name, color in [('EN001_NS1', 0xf800), ('EN001_NW1', 0x07e0), ('EN001_NW3', 0x001f)]:
                path = root/(name+'.spr')
                path.write_bytes(spr(color))
                paths.append(path)
            doc, _ = convert_group('enemy/en001', paths, root, Path(temp)/'out', [512], True, 'auto', 'readable')
            for path in paths:
                index = doc['clips'][path.stem][0]
                frame = doc['frames'][index]
                expected = decode_spr(path.read_bytes())[0]
                self.assertEqual(frame['origin'], [expected.origin_x, expected.origin_y])
                region = doc['clip_regions'][path.stem]
                self.assertEqual(frame['page'], region['page'])
                x, y, w, h = frame['rect']
                hx, hy, hw, hh = region['header']
                self.assertGreaterEqual(y, hy+hh)
                with Image.open(Path(temp)/'out/enemy'/doc['pages'][frame['page']]) as page:
                    self.assertEqual(page.crop((x,y,x+w,y+h)).tobytes(), expected.image.tobytes())

    def test_direction_action_frames_remain_ordered_without_rotation(self):
        images = [Image.new('RGBA', size) for size in [(20,40), (30,12), (15,25)]]
        placements, _, directions, clips = ordered_layout(images, {'A_NW1':[0,1,2]}, 512)
        self.assertEqual(len(directions), 1)
        self.assertEqual(clips['A_NW1']['rows'], [[0,1,2]])
        self.assertEqual([placements[i][3:] for i in range(3)], [image.size for image in images])
        self.assertLess(placements[0][1], placements[1][1])
        with self.assertRaises(ValueError):
            ordered_layout([Image.new('RGBA',(600,1))], {'A_NW1':[0]}, 512)


if __name__ == '__main__':
    unittest.main()
