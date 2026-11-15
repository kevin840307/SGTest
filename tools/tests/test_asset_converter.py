"""Converter tests for the independent Height-Dense asset pipeline."""

import hashlib
import json
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
sys.path.insert(0, str(REPO / "tools" / "asset_converter"))
from asset_converter.atlas.dense_atlas import build_group, POLICY
from asset_converter.cli import archive_all, check_runtime
from asset_converter import cli
from asset_converter.source.convert_assets import decode_spr
from asset_converter.source.runtime_spr import decode_spr as runtime_decode
from PIL import Image
from asset_converter.source.extract_client import audit_sprite_coverage


def tiny_spr(rgb=0xF800):
    row = struct.pack("<HHHH", 1, rgb, 0x8001, 0x8000)
    frame = struct.pack("<IIiiI", 2, 1, 1, 0, 20) + row
    return frame + struct.pack("<II", 0, 1)


class Settings:
    category = ["Enemy"]
    preview_groups = 2
    skip_sidecars = True
    force = False


class ConverterTests(unittest.TestCase):
    def test_cli_can_start_from_another_directory(self):
        with tempfile.TemporaryDirectory() as temp:
            result = subprocess.run([sys.executable,str(REPO/'tools/asset_converter/cli.py'),'--help'],
                                    cwd=temp,capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr)
            self.assertIn('build',result.stdout)

    def test_indexed_spr_is_shared_by_archive_and_runtime_and_roundtrips(self):
        row = struct.pack('<H',1)+b'\x07'+struct.pack('<H',0x8000)
        header = struct.pack('<IIiiI',1,1,2,3,0)
        palette = [0]*256
        palette[7] = 0x07e0
        palette_at = len(row)+len(header)
        blob = row+header+struct.pack('<I',1)+struct.pack('<256H',*palette)
        blob += struct.pack('<I',palette_at)+b'Ver\x08'+struct.pack('<II',len(row),1)
        self.assertIs(decode_spr,runtime_decode)
        self.assertEqual(decode_spr(blob)[0].image.getpixel((0,0)),(0,255,0,255))
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)/'source'
            source.mkdir()
            path = source/'palette.spr'
            path.write_bytes(blob)
            output = Path(temp)/'out'
            result = build_group(source,output,Path('palette'),[path])
            self.assertEqual(result['frames'],1)
            manifest = json.loads((output/'palette/atlas.json').read_text())
            frame = manifest['frames'][0]
            self.assertEqual(frame['origin'],[2,3])
            x,y,w,h = frame['rect']
            with Image.open(output/'palette'/manifest['pages'][0]) as page:
                self.assertEqual(page.crop((x,y,x+w,y+h)).getpixel((0,0)),(0,255,0,255))

    def test_old_cache_is_pixel_revalidated_and_bad_pivot_is_rebuilt(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)/'source'
            source.mkdir()
            path = source/'one.spr'
            path.write_bytes(tiny_spr())
            output = Path(temp)/'out'
            build_group(source,output,Path('one'),[path])
            manifest = output/'one/atlas.json'
            data = json.loads(manifest.read_text())
            data.pop('decoder_sha256')
            data.pop('metadata_sha256')
            manifest.write_text(json.dumps(data))
            self.assertEqual(build_group(source,output,Path('one'),[path])['status'],'revalidated')
            data = json.loads(manifest.read_text())
            data.pop('decoder_sha256')
            data.pop('metadata_sha256')
            data['frames'][0]['origin'][0] += 100
            manifest.write_text(json.dumps(data))
            self.assertEqual(build_group(source,output,Path('one'),[path])['status'],'converted')
            self.assertEqual(json.loads(manifest.read_text())['frames'][0]['origin'],[1,0])
            data = json.loads(manifest.read_text())
            data['frames'][0]['origin'][0] += 1
            manifest.write_text(json.dumps(data))
            self.assertEqual(build_group(source,output,Path('one'),[path])['status'],'converted')

    def test_truncated_extraction_and_loose_only_sources_are_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            original, source = Path(temp)/'original', Path(temp)/'source'
            (original/'Enemy').mkdir(parents=True)
            (source/'enemy').mkdir(parents=True)
            (original/'Data1.dat').write_bytes(b'fixture')
            (original/'Enemy/loose.spr').write_bytes(tiny_spr())
            (source/'enemy/base.spr').write_bytes(tiny_spr())
            entries = [(Path('enemy/base.spr'),0,0,0),(Path('enemy/missing.spr'),0,0,0)]
            with patch('asset_converter.source.extract_client.read_index',return_value=entries):
                report = audit_sprite_coverage(original,source)
            self.assertEqual(report['expected_spr'],3)
            self.assertEqual(report['missing'],['enemy/loose.spr','enemy/missing.spr'])
            self.assertEqual(report['extra'],[])

    def test_full_build_rejects_partial_source_before_touching_live_assets(self):
        with tempfile.TemporaryDirectory() as temp:
            original, source = Path(temp)/'original', Path(temp)/'source'
            original.mkdir()
            source.mkdir()
            (original/'Data1.dat').write_bytes(b'fixture')
            coverage = {'expected_spr':2,'actual_spr':1,'missing':['enemy/missing.spr'],'extra':[]}
            with patch.object(sys,'argv',['cli.py','build',str(original),'--runtime-only','--extracted-source',str(source)]), \
                 patch.object(cli,'REPO',Path(temp)), \
                 patch.object(cli,'audit_sprite_coverage',return_value=coverage), \
                 patch.object(cli,'build_runtime_assets') as build, patch.object(cli,'write_json') as write:
                self.assertEqual(cli.main(),1)
                build.assert_not_called()
                report = write.call_args.args[1]
                self.assertEqual(report['status'],'failed')
                self.assertIn('incomplete',report['error'])

    def test_archive_persists_failed_group_with_source_identity(self):
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)/'source'
            (source/'Enemy').mkdir(parents=True)
            path = source/'Enemy/EN001_NS1.spr'
            path.write_bytes(b'corrupt SPR')
            output = Path(temp)/'out'
            report = archive_all(source,output,Settings())
            self.assertEqual(report['status'],'partial')
            self.assertEqual(len(report['errors']),1)
            self.assertIn('EN001_NS1.spr',report['errors'][0]['error'])
            self.assertIn(hashlib.sha256(path.read_bytes()).hexdigest(),report['errors'][0]['error'])
            self.assertEqual(json.loads((output/'checkpoint.json').read_text())['status'],'partial')

    def test_all_project_converter_dependencies_are_local(self):
        import importlib

        package = REPO / "tools" / "asset_converter"
        modules = [
            "dense_atlas",
            "animation_definition",
            "asset_paths",
            "audit_character_layers",
            "layout",
            "build_enemy_map_palette",
            "build_maps",
            "build_population",
            "convert_assets",
            "export_assets",
            "extract_client",
            "inspect_act",
            "legacy_direction",
            "npc_content",
            "runtime_animation_semantics",
            "runtime_spr",
            "server_source",
            "cli",
            "json_files",
        ]
        group_by_module = {
            "extract_client": "source",
            "convert_assets": "source",
            "runtime_spr": "source",
            "inspect_act": "source",
            "legacy_direction": "source",
            "dense_atlas": "atlas",
            "layout": "atlas",
            "animation_definition": "content",
            "runtime_animation_semantics": "content",
            "audit_character_layers": "content",
            "asset_paths": "content",
            "export_assets": "content",
            "npc_content": "content",
            "server_source": "content",
            "build_maps": "content",
            "build_enemy_map_palette": "content",
            "build_population": "content",
        }
        for name in modules:
            mod = importlib.import_module(
                "asset_converter."
                + (group_by_module.get(name, "") + ("." if name not in ("cli","json_files") else ""))
                + name
            )
            self.assertTrue(
                Path(mod.__file__).resolve().is_relative_to(package),
                "converter dependency must be packaged locally: " + name,
            )

    def test_dense_build_incremental_repair_and_source_edit(self):
        with tempfile.TemporaryDirectory() as t:
            src = Path(t) / "client"
            dst = Path(t) / "out"
            src.joinpath("Enemy").mkdir(parents=True)
            files = [src / "Enemy" / "EN001_NS1.spr", src / "Enemy" / "EN001_NW1.spr"]
            for p in files:
                p.write_bytes(tiny_spr())
            key = Path("Enemy/EN001")
            first = build_group(src, dst, key, files)
            self.assertEqual(first["status"], "converted")
            doc = json.loads((dst / key / "atlas.json").read_text())
            self.assertEqual(doc["packing_policy"], POLICY)
            self.assertEqual(len(doc["frames"]), 2)
            self.assertEqual(len(doc["sources"]), 2)
            self.assertEqual(first["pages"], 1)
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")
            page = dst / key / doc["pages"][0]
            page.write_bytes(b"garbled")
            self.assertEqual(build_group(src, dst, key, files)["status"], "converted")
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")
            before = json.loads((dst / key / "atlas.json").read_text())["source_digest"]
            files[0].write_bytes(tiny_spr(0x001F))
            self.assertEqual(build_group(src, dst, key, files)["status"], "converted")
            after = json.loads((dst / key / "atlas.json").read_text())["source_digest"]
            self.assertNotEqual(before, after)
            self.assertEqual(build_group(src, dst, key, files)["status"], "cached")

    def test_archive_preview_only_selected_groups(self):
        with tempfile.TemporaryDirectory() as t:
            src = Path(t) / "client"
            dst = Path(t) / "archive"
            src.joinpath("Enemy").mkdir(parents=True)
            src.joinpath("Interface").mkdir()
            for name in ("EN001_NS1.spr", "EN001_NW1.spr", "EN002_NS1.spr"):
                (src / "Enemy" / name).write_bytes(tiny_spr())
            (src / "Interface" / "test.spr").write_bytes(tiny_spr())
            obj = archive_all(src, dst, Settings())
            self.assertEqual(obj["groups_expected"], 2)
            self.assertEqual(len(obj["groups"]), 2)
            self.assertFalse(obj["errors"])
            self.assertEqual(obj["total_frames"], 3)
            self.assertTrue((dst / "index.json").is_file())
            self.assertFalse((dst / "sprites" / "Interface").exists())

    def test_runtime_contract_requires_independent_assets(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            stage = root / "stage"
            assets = root / "assets"
            map_dir = stage / "maps" / "13"
            map_dir.mkdir(parents=True)
            collision = b"original-blocking-grid"
            (map_dir / "blocking.dat").write_bytes(collision)
            world = {
                "version": 2,
                "collision_sha256": hashlib.sha256(collision).hexdigest(),
                "objects": [{
                    "atlas": "maps/visuals/tree/atlas.json",
                    "source": "map/tree.spr",
                }],
            }
            (map_dir / "world.json").write_text(json.dumps(world))
            population = {
                "version": 3, "map_id": 13, "kind": "visual_spawn_content",
                "stats_verified": False, "gameplay_ready": False,
                "definitions": [{
                    "id": 1,
                    "sprite": "EN001A",
                    "atlas": "Enemy/en001a/atlas.json",
                    "stats": None, "stats_verified": False,
                }],
                "spawns": [],
            }
            (map_dir / "population.json").write_text(json.dumps(population))
            npc_document = {'schema':'sgtest.npcs/v1','map_id':13,'npcs':[
                {'visual':{'resolved':True,'source':'npc/npc001.spr','atlas':'npcs/npc001/atlas.json'}}]}
            npc_path = map_dir/'npcs.json'
            npc_path.write_text(json.dumps(npc_document))
            required = [
                "maps/visuals/tree/atlas.json",
                "Enemy/en001a/atlas.json",
                "npcs/npc001/atlas.json",
                "players/b/base/body/atlas.json",
                "players/b/base/leg/atlas.json",
                "players/b/base/head/atlas.json",
                "players/b/hair/hair00/atlas.json",
                "players/b/base/left_arm/atlas.json",
                "players/b/base/right_arm/atlas.json",
                "players/b/base/death/atlas.json",
            ]
            for relative in required:
                path = assets / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("{}")
            result = check_runtime(stage, assets, 13)
            self.assertFalse(result["cross_asset_bundle"])
            self.assertEqual(result["map_visual_assets"], 1)
            self.assertEqual(result["monster_visual_assets"], 1)
            self.assertEqual(result["player_part_assets"], 7)
            self.assertEqual(result['npc_visual_assets'], 1)
            self.assertFalse(result['gameplay_ready'])
            npc_atlas = assets/'npcs/npc001/atlas.json'
            npc_atlas.unlink()
            with self.assertRaisesRegex(ValueError,'missing independent NPC'):
                check_runtime(stage, assets, 13)
            npc_atlas.write_text('{}')
            for broken in (
                {'schema':'sgtest.npcs/v1','map_id':15,'npcs':[]},
                {'schema':'sgtest.npcs/v1','map_id':13,'npcs':[
                    {'visual':{'resolved':False,'reason':'type_not_in_table'}}]},
                {'schema':'sgtest.npcs/v1','map_id':13,'npcs':[
                    {'visual':{'resolved':True,'source':'npc/npc001.spr','atlas':'npcs/wrong/atlas.json'}}]},
            ):
                npc_path.write_text(json.dumps(broken))
                with self.assertRaises(ValueError):
                    check_runtime(stage, assets, 13)
            npc_path.write_text(json.dumps(npc_document))
            population['version'] = 2
            (map_dir/'population.json').write_text(json.dumps(population))
            with self.assertRaisesRegex(ValueError,'population v3'):
                check_runtime(stage, assets, 13)
            population['version'] = 3
            population['definitions'][0]['stats'] = {'hp':30}
            (map_dir/'population.json').write_text(json.dumps(population))
            with self.assertRaisesRegex(ValueError,'population v3'):
                check_runtime(stage, assets, 13)
            population['definitions'][0]['stats'] = None
            (map_dir/'population.json').write_text(json.dumps(population))
            world["sprite_bundle"] = "bundles/map/13/sprites.json"
            (map_dir / "world.json").write_text(json.dumps(world))
            with self.assertRaises(ValueError):
                check_runtime(stage, assets, 13)


if __name__ == "__main__":
    unittest.main()
