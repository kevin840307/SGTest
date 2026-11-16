"""Scoped publication still requires a complete, verified source archive."""
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest

from test_asset_converter import tiny_spr
from asset_converter.cli import archive_all
from asset_converter.content.export_assets import publish, runtime_page, PAGE_LAYOUT
from PIL import Image
from test_scenery_timing import act


class Settings:
    category = prefix = preview_groups = None
    skip_sidecars = force = False


class ScopedExportTests(unittest.TestCase):
    def fixture(self, folder):
        root = Path(folder)
        source, archive, assets = root/'source', root/'archive', root/'assets'
        (source/'map').mkdir(parents=True)
        (source/'map/one.spr').write_bytes(tiny_spr())
        (source/'map/two.spr').write_bytes(tiny_spr(0x07e0))
        (source/'map/extra.dat').write_bytes(b'uninterpreted fixture')
        archive_all(source, archive, Settings())
        return source, archive, assets

    def test_selected_catalog_matches_pages_and_repeats_deterministically(self):
        with tempfile.TemporaryDirectory() as folder:
            source, archive, assets = self.fixture(folder)
            report = publish(source, archive, assets, {'maps/visuals/one'})
            self.assertEqual(report['status'], 'pass')
            self.assertEqual(report['sprite_groups'], 1)
            self.assertEqual(report['source_files'], 1)
            self.assertFalse((assets/'maps/visuals/two').exists())
            self.assertFalse((assets/'data/source_files/map/extra.dat.bin').exists())
            catalog = json.loads((assets/'catalog/assets.json').read_text())
            self.assertEqual(catalog['source_scope'], 'selected_sprite_assets')
            manifest = json.loads((assets/'maps/visuals/one/atlas.json').read_text())
            page = assets/'maps/visuals/one/page_0.png'
            self.assertEqual(hashlib.sha256(page.read_bytes()).hexdigest(), manifest['page_sha256']['page_0.png'])
            archived = json.loads((archive/'sprites/map/one/atlas.json').read_text())
            self.assertEqual(manifest['page_layout'],PAGE_LAYOUT)
            self.assertEqual(manifest['archive_page_sizes'],archived['page_sizes'])
            self.assertEqual(manifest['archive_page_sha256'],archived['page_sha256'])
            self.assertEqual(manifest['frames'][0]['rect'],archived['frames'][0]['rect'])
            self.assertEqual(manifest['frames'][0]['origin'],archived['frames'][0]['origin'])
            self.assertEqual(manifest['page_sizes'],[[4,4]])
            before = {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()}
            publish(source, archive, assets, {'maps/visuals/one'})
            self.assertEqual(before, {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()})

    def test_runtime_page_alignment_keeps_source_texels_and_transparent_extension(self):
        for size in ((1,1),(51,102),(8,12),(4095,1)):
            image = Image.new('RGBA',size,(129,61,88,211))
            image.putpixel((size[0]-1,size[1]-1),(3,7,9,0))
            stream = io.BytesIO()
            image.save(stream,format='PNG')
            original = stream.getvalue()
            payload, actual_size = runtime_page(original,list(size))
            expected_size = [(s+3)//4*4 for s in size]
            self.assertEqual(actual_size,expected_size)
            with Image.open(io.BytesIO(payload)) as actual:
                self.assertEqual(actual.crop((0,0,*size)).tobytes(),image.tobytes())
                for x,y in ((actual.width-1,0),(0,actual.height-1),(actual.width-1,actual.height-1)):
                    if x>=size[0] or y>=size[1]:
                        self.assertEqual(actual.getpixel((x,y)),(0,0,0,0))
            self.assertEqual(runtime_page(payload,actual_size),(payload,actual_size))
            if list(size)==expected_size:
                self.assertEqual(payload,original)
            with self.assertRaisesRegex(ValueError,'dimension mismatch'):
                runtime_page(original,[size[0]+1,size[1]])

    def test_scenery_act_is_embedded_only_when_original_literal_action_exists(self):
        with tempfile.TemporaryDirectory() as folder:
            source,archive,assets = self.fixture(folder)
            payload = act(((0,6),))
            (source/'map/one.act').write_bytes(payload)
            archive_all(source,archive,Settings())
            report = publish(source,archive,assets,{'maps/visuals/one','maps/visuals/two'})
            self.assertEqual(report['animation_semantics']['normalized_scenery_act'],1)
            manifest = json.loads((assets/'maps/visuals/one/atlas.json').read_text())
            self.assertEqual(manifest['act']['ONE']['frames'],[{'index':0,'raw_time':6}])
            self.assertEqual(manifest['act']['ONE']['source_sha256'],hashlib.sha256(payload).hexdigest())
            self.assertEqual(json.loads((assets/'maps/visuals/two/atlas.json').read_text())['act'],{})

    def test_unknown_or_empty_selection_rejects_before_output(self):
        with tempfile.TemporaryDirectory() as folder:
            source, archive, assets = self.fixture(folder)
            for selected in (set(), {'maps/visuals/missing'}):
                with self.subTest(selected=selected), self.assertRaisesRegex(ValueError, 'selected asset IDs'):
                    publish(source, archive, assets, selected)
                self.assertFalse(assets.exists())

    def test_scope_change_removes_owned_stale_files_and_keeps_modified_files(self):
        with tempfile.TemporaryDirectory() as folder:
            source, archive, assets = self.fixture(folder)
            publish(source, archive, assets, {'maps/visuals/one'})
            page = assets/'maps/visuals/one/page_0.png'
            page.write_bytes(b'locally modified output')
            report = publish(source, archive, assets, {'maps/visuals/two'})
            self.assertEqual(page.read_bytes(), b'locally modified output')
            self.assertIn('maps/visuals/one/page_0.png', report['modified_stale_files_retained'])
            self.assertFalse((assets/'maps/visuals/one/atlas.json').exists())
            self.assertTrue((assets/'maps/visuals/two/atlas.json').exists())

    def test_full_export_remains_complete_and_partial_archive_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            source, archive, assets = self.fixture(folder)
            report = publish(source, archive, assets)
            self.assertEqual(report['source_files'], 3)
            self.assertEqual(report['sprite_groups'], 2)
            self.assertTrue((assets/'data/source_files/map/extra.dat.bin').is_file())
            (archive/'sprites/map/two/page_0.png').write_bytes(b'corrupt unrelated asset')
            with self.assertRaisesRegex(ValueError, 'complete archive audit failed'):
                publish(source, archive, assets, {'maps/visuals/one'})


if __name__ == '__main__':
    unittest.main()
