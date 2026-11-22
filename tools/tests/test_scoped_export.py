"""Scoped publication still requires a complete, verified source archive."""
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from argparse import Namespace
from contextlib import redirect_stdout
from unittest.mock import patch

from test_asset_converter import tiny_spr
from asset_converter import cli
from asset_converter.cli import archive_all
from asset_converter.content.export_assets import publish, runtime_page, PAGE_LAYOUT
from PIL import Image
from test_scenery_timing import act


class Settings:
    category = prefix = preview_groups = None
    skip_sidecars = force = False


class ScopedExportTests(unittest.TestCase):
    def character_fixture(self, folder, missing_controller=False):
        root = Path(folder)
        source, archive, assets = root/'source', root/'archive', root/'assets'
        (source/'player').mkdir(parents=True)
        (source/'player/charactionname.dat').write_text('NS,NR,NU,NB,AS,AH,AA,AB,AG,GS,GH,GA,GB',encoding='ascii')
        common = 'Head>Hair>leg+2>Body+2>L-Arm+2>R-Arm+2'
        robot = 'Body+2>Head>Hair>leg+2>L-Arm+2>R-Arm+2'
        for filename, order in [('chardraworder.dat',common),('r_chardraworder.dat',robot)]:
            (source/'player'/filename).write_text('\n'.join(f'NS{f}: {order}' for f in [1,2,3,4,8]),encoding='ascii')
        for race, controller, duration in [('b','boy',3),('g','girl',5),('a','alien',7),('r','robot',9),('y','unverified',11)]:
            standard = source/f'player/{race}/standard'
            standard.mkdir(parents=True)
            for facing in [1,2,3,4,8]:
                (standard/f'{race}-body_ns{facing}.spr').write_bytes(tiny_spr())
            if race != 'y' and not missing_controller:
                (standard.parent/f'{controller}.act').write_bytes(act(((0,duration),),key='NS'))
                (standard/f'{race}-death_nd1.spr').write_bytes(tiny_spr())
                (standard.parent/f'{race}-death.act').write_bytes(act(((0,duration+1),),False,key='Effect'))
        archive_all(source,archive,Settings())
        return source,archive,assets

    def test_race_specific_act_and_robot_draw_order_without_boy_fallback(self):
        with tempfile.TemporaryDirectory() as folder:
            source,archive,assets = self.character_fixture(folder)
            report = publish(source,archive,assets)
            self.assertEqual(report['animation_semantics']['normalized_player_part'],8)
            for race,controller,duration in [('b','boy',3),('g','girl',5),('a','alien',7),('r','robot',9)]:
                path = assets/f'players/{race}/base/body'
                manifest = json.loads((path/'atlas.json').read_text())
                views = manifest['logical_views']
                self.assertEqual(len(views),8)
                self.assertEqual(views[f'{race.upper()}-BODY_NS5']['clip'],f'{race.upper()}-BODY_NS3')
                self.assertTrue(views[f'{race.upper()}-BODY_NS5']['flip_h'])
                self.assertEqual(views[f'{race.upper()}-BODY_NS1']['timeline_ticks'],[[0,duration*40//25]])
                self.assertEqual(views[f'{race.upper()}-BODY_NS1']['draw_slots'][0],10 if race=='r' else 0)
                definition = json.loads((path/'definition.json').read_text())
                self.assertEqual(definition['act_source'],f'player/{race}/{controller}.act')
                self.assertIn(f'player/{race}/{controller}.act',definition['source_sha256'])
                death = json.loads((assets/f'players/{race}/base/death/atlas.json').read_text())
                view = death['logical_views'][f'{race.upper()}-DEATH_ND8']
                self.assertEqual(view['clip'],f'{race.upper()}-DEATH_ND1')
                self.assertFalse(view['loop'])
                self.assertEqual(view['timeline_ticks'],[[0,(duration+1)*40//25]])
            unverified = json.loads((assets/'players/y/base/body/atlas.json').read_text())
            self.assertEqual(unverified['logical_views'],{})
            self.assertFalse(unverified['animation_semantics']['timing_verified'])

    def test_missing_character_controller_never_marks_animation_verified(self):
        with tempfile.TemporaryDirectory() as folder:
            source,archive,assets = self.character_fixture(folder,missing_controller=True)
            publish(source,archive,assets,{'players/g/base/body'})
            manifest = json.loads((assets/'players/g/base/body/atlas.json').read_text())
            self.assertEqual(manifest['logical_views'],{})
            self.assertFalse(manifest['animation_semantics']['timing_verified'])
            self.assertIn('girl.act',manifest['animation_semantics']['reason'])

    def fixture(self, folder):
        root = Path(folder)
        source, archive, assets = root/'source', root/'archive', root/'assets'
        (source/'map').mkdir(parents=True)
        (source/'map/one.spr').write_bytes(tiny_spr())
        (source/'map/two.spr').write_bytes(tiny_spr(0x07e0))
        (source/'map/extra.dat').write_bytes(b'uninterpreted fixture')
        archive_all(source, archive, Settings())
        return source, archive, assets

    def test_cli_explicit_asset_selection_and_map_union(self):
        for include_map in (False,True):
            with self.subTest(include_map=include_map), tempfile.TemporaryDirectory() as folder:
                source,archive,assets = self.fixture(folder)
                report_path = Path(folder)/'report.json'
                args = Namespace(command='export-assets',original=source,extracted_source=source,
                                 archive_output=archive,assets=assets,report=report_path,
                                 map=[15] if include_map else None,map_ids=[15],server_dat=source,
                                 asset_id=['maps/visuals/two'])
                with patch.object(cli,'map_asset_ids',return_value={'maps/visuals/one'}), patch.object(cli,'resolve_server_dat',return_value=source), redirect_stdout(io.StringIO()):
                    self.assertEqual(cli._execute(args),0)
                report = json.loads(report_path.read_text())
                self.assertEqual(report['sprite_groups'],2 if include_map else 1)
                self.assertEqual(report['requested_maps'],[15] if include_map else [])
                self.assertEqual(report['requested_asset_ids'],['maps/visuals/two'])
                self.assertTrue((assets/'maps/visuals/two/atlas.json').is_file())
                self.assertEqual((assets/'maps/visuals/one').exists(),include_map)

    def test_cli_rejects_bad_unknown_or_duplicate_asset_ids_without_output(self):
        for selected in (['../private'],['maps/visuals/missing'],['maps/visuals/one']*2,['x']*4097):
            with self.subTest(selected=selected[:2]), tempfile.TemporaryDirectory() as folder:
                source,archive,assets = self.fixture(folder)
                args = Namespace(command='export-assets',original=source,extracted_source=source,
                                 archive_output=archive,assets=assets,report=Path(folder)/'report.json',
                                 map=None,map_ids=[13],server_dat=None,asset_id=selected)
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(cli._execute(args),1)
                self.assertFalse(assets.exists())
                self.assertFalse(json.loads(args.report.read_text())['complete_asset_conversion'])

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
