"""Original world-content fixtures contain no game text or media."""
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.source.legacy_npc import npc_placements, npc_visual_table
from asset_converter.content.npc_content import build, map_directory
from asset_converter.content.asset_pipeline import original_population
from test_assets import spr

REPO = Path(__file__).resolve().parents[2]
COMMON = b'-- synthetic trusted template fixture, not legacy code\n'
MAIN = ('function start() CanMove() end\n'
        'function GetLocName() return "name--literal" end\n'
        'function GetLoc() return 24,24,2 end\n'
        '#include "TS_Common.txt"\n')


def placement(script='route.txt', reserved=7):
    return struct.pack('<iiiiB', 1, 0, 8, 8, reserved)+script.encode('ascii')+b'\0'+b'Fixture\0'


class NpcContentTests(unittest.TestCase):
    def fixture(self, folder):
        root = Path(folder)
        source, server = root/'source', root/'server'
        (source/'npc').mkdir(parents=True)
        (server/'Map/Map01').mkdir(parents=True)
        (server/'Quest').mkdir()
        (source/'npc/npc_table.dat').write_bytes(struct.pack('<i', 1)+b'npc\\npc001.spr\0'+b'\0'*5)
        (source/'npc/npc001.spr').write_bytes(spr())
        (server/'Map/Map01/NPC.dat').write_bytes(placement())
        (server/'Quest/route.txt').write_text(MAIN)
        (server/'Quest/TS_Common.txt').write_bytes(COMMON)
        for map_id in [1, 2]:
            (source/f'map/map{map_id}').mkdir(parents=True)
            (source/f'map/map{map_id}.map').write_text(f'[Map]\nBlocking=map/map{map_id}/blocking.dat\n')
            (source/f'map/map{map_id}/blocking.dat').write_bytes(struct.pack('<II',4,4)+bytes(16))
        return source, server, root/'out/npcs.json'

    def compile(self, source, server, output):
        with patch('asset_converter.content.npc_content.TRANSFER_TEMPLATE_SHA256', hashlib.sha256(COMMON).hexdigest()):
            report = build(source, server, 1, output)
        return report, json.loads(output.read_text(encoding='utf-8'))

    def test_bounded_binary_records_and_reserved_value(self):
        row, = npc_placements(placement())
        self.assertEqual((row['source_index'],row['type_id'],row['p'],row['reserved']), (0,0,[8,8],7))
        for raw in [b'',struct.pack('<i',-1),struct.pack('<i',10001),placement()[:-1],placement()+b'x',placement()[:10]]:
            with self.subTest(raw=raw), self.assertRaises(ValueError):
                npc_placements(raw)
        self.assertEqual(npc_placements(struct.pack('<i',0)),[])
        with self.assertRaises(ValueError):npc_placements(placement('x'*256))

    def test_visual_table_six_slots_and_shared_CDE_decoder(self):
        raw = struct.pack('<i',1)+b'npc/a.spr\0'+b'\0'*5
        self.assertEqual(npc_visual_table(raw),[['npc/a.spr','','','','','']])
        with patch('asset_converter.source.legacy_npc.unwrap_cde',return_value=raw) as decoder:
            self.assertEqual(npc_visual_table(b'C.D.E.fixture'),npc_visual_table(raw))
            decoder.assert_called_once_with(b'C.D.E.fixture')
        for broken in [raw[:-1],raw+b'\0',struct.pack('<i',1)+b'\0'*5]:
            with self.assertRaises(ValueError):npc_visual_table(broken)

    def test_static_transfer_is_deterministic_and_checks_both_positions(self):
        with tempfile.TemporaryDirectory() as folder:
            source, server, output = self.fixture(folder)
            first, data = self.compile(source,server,output)
            second, _ = self.compile(source,server,output)
            self.assertEqual(first['fingerprint'],second['fingerprint'])
            self.assertEqual(first['interactions'],{'confirm_transfer':1})
            row = data['npcs'][0]
            self.assertEqual(row['interaction']['destination'],{'map_id':2,'p':[24,24]})
            self.assertEqual(row['position_status'],'walkable')
            self.assertEqual(row['reserved'],7)
            self.assertEqual(row['visual']['frames'],1)
            self.assertFalse(row['visual']['timing_verified'])
            self.assertFalse(row['visual']['direction_mapping_verified'])
            self.assertEqual(data['visual_table_rows'],1)
            self.assertIn('server/Quest/TS_Common.txt',data['source_sha256'])
            self.assertFalse(first['runtime_transfer_implemented'])
            # An NPC is scenery, not a walking actor. Preserve its blocked-cell
            # status without inventing a new legacy script entry restriction.
            origin = source/'map/map1/blocking.dat'
            cells = bytearray(origin.read_bytes());cells[8] = 1;origin.write_bytes(cells)
            _, data = self.compile(source,server,output)
            self.assertEqual(data['npcs'][0]['position_status'],'blocked')
            self.assertEqual(data['npcs'][0]['interaction']['mode'],'confirm_transfer')
            grid = source/'map/map2/blocking.dat'
            cells = bytearray(grid.read_bytes());cells[8+5] = 1;grid.write_bytes(cells)
            report, data = self.compile(source,server,output)
            self.assertEqual(report['interactions'],{'transfer_unavailable':1})
            self.assertEqual(data['npcs'][0]['interaction']['destination_status'],'blocked')
            self.assertNotEqual(report['fingerprint'],first['fingerprint'])

    def test_comments_do_not_enable_dynamic_or_disabled_scripts(self):
        with tempfile.TemporaryDirectory() as folder:
            source, server, output = self.fixture(folder)
            path = server/'Quest/route.txt'
            for text, mode in [('-- header\n'+MAIN,'confirm_transfer'),
                               (MAIN.replace('CanMove()','CannnotMove()'),'blocked'),
                               (MAIN+'\nMovePlayer(8,8,3)','script_required'),
                               ('--[[\n'+MAIN+'\n]]','script_required'),
                               ('--[=[\n'+MAIN+'\n--]=]','script_required'),
                               (MAIN.replace('24,24,2','-1,24,2'),'unavailable'),
                               (MAIN.replace('24,24,2','999,24,2'),'transfer_unavailable'),
                               (MAIN.replace('24,24,2','value,24,2'),'script_required')]:
                with self.subTest(mode=mode,text=text):
                    path.write_text(text)
                    _,data = self.compile(source,server,output)
                    self.assertEqual(data['npcs'][0]['interaction']['mode'],mode)
            path.write_text(MAIN)
            report = build(source,server,1,output)  # fixture template is not the audited original
            self.assertEqual(report['interactions'],{'script_required':1})

    def test_missing_and_unsafe_scripts_do_not_become_portals(self):
        with tempfile.TemporaryDirectory() as folder:
            source, server, output = self.fixture(folder)
            (server/'Quest/route.txt').unlink()
            report,_ = self.compile(source,server,output)
            self.assertEqual(report['interactions'],{'unavailable':1})
            previous = output.read_bytes()
            (server/'Map/Map01/NPC.dat').write_bytes(placement('../route.txt'))
            with self.assertRaisesRegex(ValueError,'unsafe'):self.compile(source,server,output)
            self.assertEqual(output.read_bytes(),previous,'failed build must not overwrite prior output')
            with self.assertRaisesRegex(ValueError,'overwrite'):build(source,server,1,source/'overwrite.json')

    def test_leading_zero_map_directory_and_population_use_same_resolution(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root/'Map/Map07').mkdir(parents=True); (root/'Enemy').mkdir()
            (root/'Map/Map07/Enemy.dat').write_bytes(struct.pack('<i',0))
            (root/'Enemy/Enemy_Data.csv').write_bytes(b'')
            self.assertEqual(map_directory(root,7),root/'Map/Map07')
            self.assertEqual(original_population(root,7,'bundles/map/7/sprites.json')['spawns'],[])
            previous = Path.cwd()
            try:
                os.chdir(root)
                self.assertEqual(original_population(Path('.'),7,'bundle.json')['spawns'],[])
            finally:
                os.chdir(previous)
            (root/'Map/Map7').mkdir()
            with self.assertRaisesRegex(ValueError,'ambiguous'):map_directory(root,7)

    def test_cli_conflicting_build_flags_are_usage_error_without_traceback(self):
        result = subprocess.run([sys.executable,str(REPO/'tools/asset_converter/cli.py'),'build','unused',
                                 '--archive-only','--runtime-only'],capture_output=True,text=True)
        self.assertEqual(result.returncode,2)
        self.assertIn('conflict',result.stderr)
        self.assertNotIn('Traceback',result.stderr)

    def test_cli_all_maps_from_another_directory_and_missing_files_are_reported(self):
        with tempfile.TemporaryDirectory() as folder:
            source,server,_ = self.fixture(folder)
            (server/'Map/Map02').mkdir()  # Missing file must not be guessed empty.
            report = Path(folder)/'report.json'
            result = subprocess.run([sys.executable,str(REPO/'tools/asset_converter/cli.py'),'world-content',str(source),
                                     '--server-dat',str(server),'--all-maps','--output-root',str(Path(folder)/'private'),
                                     '--report',str(report)],cwd=folder,capture_output=True,text=True)
            self.assertEqual(result.returncode,0,result.stderr+result.stdout)
            data = json.loads(report.read_text())
            self.assertEqual(data['map_files'],1)
            self.assertEqual(data['npcs'],1)
            self.assertEqual(data['maps_without_npc_file'],[2])
            self.assertTrue((Path(folder)/'private/maps/1/npcs.json').is_file())


if __name__ == '__main__':
    unittest.main()
