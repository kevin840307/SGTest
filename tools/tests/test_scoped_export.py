"""Scoped publication still requires a complete, verified source archive."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from test_asset_converter import tiny_spr
from asset_converter.cli import archive_all
from asset_converter.content.export_assets import publish


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
            before = {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()}
            publish(source, archive, assets, {'maps/visuals/one'})
            self.assertEqual(before, {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()})

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
