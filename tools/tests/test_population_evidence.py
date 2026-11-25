"""Original Enemy source evidence; fixture contains no game images or data."""
import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.build_population import compile_spawn_evidence
from asset_converter import cli


def enemy_row(enemy_id, x, y, flags):
    return (struct.pack('<i', flags) + str(enemy_id).encode('ascii') + b'\0'
            + struct.pack('<ii', x, y) + bytes(207))


class PopulationEvidenceTests(unittest.TestCase):
    def fixture(self, root):
        source, server = root/'source', root/'server'
        (source/'enemy').mkdir(parents=True)
        (source/'enemy/en010a.act').write_bytes(b'act fixture')
        (source/'enemy/en010a_ns1.spr').write_bytes(b'spr fixture')
        (server/'Enemy').mkdir(parents=True)
        (server/'Enemy/Enemy_Data.csv').write_bytes(
            b'1,,Enemy One,3,1,,EN010A,,,120\n')
        (server/'Enemy/EN_Att01.csv').write_bytes(b'3,222,17,0,15,0,30,18,41\n')
        (server/'Map/Map13').mkdir(parents=True)
        spawn = (struct.pack('<i', 2) + enemy_row(1,100,200,9)
                 + enemy_row(1,300,400,17))
        (server/'Map/Map13/Enemy.dat').write_bytes(spawn)
        return source, server

    def fake_animation(self, *args):
        return {'content_sha256':'a'*64, 'source_sha256':{'enemy/en010a.act':'b'*64},
                'behaviors':{code:{} for code in ('FA','ND','NH','NS','NW')},
                'frames':[{},{}]}

    def test_original_spawn_order_and_known_animation_without_fabricated_stats(self):
        with tempfile.TemporaryDirectory() as directory:
            source, server = self.fixture(Path(directory))
            with patch('asset_converter.content.build_population.compile_definition',
                       side_effect=self.fake_animation) as compile_act:
                result = compile_spawn_evidence(source,server,13)
                self.assertEqual(result,compile_spawn_evidence(source,server,13))
                self.assertEqual(compile_act.call_count,2)
            self.assertEqual(result['kind'],'original_spawn_evidence_not_runtime')
            self.assertEqual([s['p'] for s in result['spawns']],[[100,200],[300,400]])
            self.assertEqual([s['source_flags'] for s in result['spawns']],[9,17])
            self.assertEqual(len(result['definitions']),1)
            definition=result['definitions'][0]
            self.assertEqual(definition['atlas'],'Enemy/en010a/atlas.json')
            self.assertEqual(definition['animation_behaviors'],['FA','ND','NH','NS','NW'])
            self.assertEqual(definition['source_combat_evidence'], {
                'level':3, 'max_hp':222, 'attack_power':17,
                'defense_power':0, 'magic_attack_power':15,
                'magic_defense_power':0, 'hit_rate':30, 'dodge_rate':18,
                'r_exp':41, 'attribute_table':1,
                'attribute_source':'Enemy/EN_Att01.csv',
                'source_columns':{
                    'level':3, 'max_hp':1, 'attack_power':2,
                    'defense_power':3, 'magic_attack_power':4,
                    'magic_defense_power':5, 'hit_rate':6,
                    'dodge_rate':7, 'r_exp':8,
                },
            })
            self.assertFalse(definition['combat_stats_verified'])
            self.assertFalse(result['stats_verified'])
            self.assertFalse(result['runtime_implemented'])
            self.assertNotIn('stats',definition)
            self.assertEqual(result['sources']['Map/Map13/Enemy.dat'],
                             hashlib.sha256((server/'Map/Map13/Enemy.dat').read_bytes()).hexdigest())
            self.assertEqual(result['sources']['Enemy/EN_Att01.csv'],
                             hashlib.sha256((server/'Enemy/EN_Att01.csv').read_bytes()).hexdigest())

    def test_definition_attack_length_and_reborn_time_are_not_runtime_stats(self):
        with tempfile.TemporaryDirectory() as directory:
            source, server = self.fixture(Path(directory))
            path = server/'Enemy/Enemy_Data.csv'
            path.write_bytes(b'1,1,Enemy One,3,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,15\n')
            with patch('asset_converter.content.build_population.compile_definition',
                       side_effect=self.fake_animation):
                result = compile_spawn_evidence(source, server, 13)
                definition = result['definitions'][0]
                self.assertEqual(definition['source_behavior_evidence'], {
                    'attack_length': 150, 'reborn_time_ms': 15000,
                    'source': 'Enemy/Enemy_Data.csv',
                    'source_columns': {'attack_length': 10, 'reborn_time_ms': 19},
                })
                self.assertFalse(result['runtime_implemented'])
                self.assertFalse(definition['combat_stats_verified'])
                path.write_bytes(b'1,1,Enemy One,3,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,\n')
                partial = compile_spawn_evidence(source, server, 13)
                self.assertNotIn('reborn_time_ms', partial['definitions'][0]['source_behavior_evidence'])
                path.write_bytes(b'1,1,Enemy One,3,1,,EN010A,0,1,40,150,4,0,400,B,0,2,1,800,-1\n')
                with self.assertRaisesRegex(ValueError, 'unsafe EnemyId 1 reborn_time_ms'):
                    compile_spawn_evidence(source, server, 13)

    def test_conflicting_combat_field_is_marked_ambiguous_without_guessing(self):
        with tempfile.TemporaryDirectory() as directory:
            source, server = self.fixture(Path(directory))
            (server/'Enemy/EN_Att01.csv').write_bytes(
                b'3,222,17,0,15,0,30,18,41\n3,223,17,0,15,0,30,18,41\n')
            with patch('asset_converter.content.build_population.compile_definition',
                       side_effect=self.fake_animation):
                result = compile_spawn_evidence(source, server, 13)
            evidence = result['definitions'][0]['source_combat_evidence']
            self.assertNotIn('max_hp', evidence)
            self.assertEqual(['max_hp'], evidence['ambiguous_fields'])
            self.assertEqual(18, evidence['dodge_rate'])
            self.assertEqual(41, evidence['r_exp'])

    def test_lookup_miss_preserves_raw_key_without_publishing_combat_attributes(self):
        with tempfile.TemporaryDirectory() as directory:
            source, server = self.fixture(Path(directory))
            (server/'Enemy/Enemy_Data.csv').write_bytes(
                b'1,,Enemy One,65535,1,,EN010A,,,120\n')
            with patch('asset_converter.content.build_population.compile_definition',
                       side_effect=self.fake_animation):
                result = compile_spawn_evidence(source, server, 13)
            self.assertEqual(result['definitions'][0]['source_combat_evidence'], {
                'level_key':65535, 'source_columns':{'level_key':3},
                'combat_attributes_unavailable':'old_add_attr_find_attr_miss',
            })

    def test_missing_original_act_and_unknown_definition_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            source,server=self.fixture(Path(directory))
            (source/'enemy/en010a.act').unlink()
            with self.assertRaisesRegex(ValueError,'missing original ACT/SPR'):
                compile_spawn_evidence(source,server,13)
            (source/'enemy/en010a.act').write_bytes(b'act')
            (server/'Enemy/Enemy_Data.csv').write_bytes(b'')
            with self.assertRaisesRegex(ValueError,'unresolved original'):
                compile_spawn_evidence(source,server,13)
            (server/'Enemy/Enemy_Data.csv').write_bytes(b'1,,Name,1,,,../unsafe\n')
            with self.assertRaisesRegex(ValueError,'unsafe Enemy sprite'):
                compile_spawn_evidence(source,server,13)

    def test_truncated_spawn_fails_instead_of_inventing_missing_monsters(self):
        with tempfile.TemporaryDirectory() as directory:
            source,server=self.fixture(Path(directory))
            path=server/'Map/Map13/Enemy.dat'
            path.write_bytes(path.read_bytes()[:-1])
            with self.assertRaises(ValueError):
                compile_spawn_evidence(source,server,13)

    def test_world_cli_batched_population_evidence_is_map_isolated_and_failure_safe(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            source,server=self.fixture(root)
            destination,report=root/'out',root/'report.json'
            args=['cli.py','world-content',str(source),'--server-dat',str(server),
                  '--map','15','--map','13','--include-population',
                  '--output-root',str(destination),'--report',str(report)]
            def fake_population(_source, _server, map_id):
                return {'kind':'original_spawn_evidence_not_runtime','map_id':map_id,
                        'spawns':[{'source_index':0,'p':[map_id,100]}],
                        'definitions':[], 'stats_verified':False,'runtime_implemented':False}
            with patch.object(sys,'argv',args), \
                 patch.object(cli,'build_npcs',side_effect=lambda s,d,m,p: {'status':'pass','map_id':m}), \
                 patch.object(cli,'compile_spawn_evidence',side_effect=fake_population):
                self.assertEqual(cli.main(),0)
            files=[destination/f'maps/{i}/population_evidence.json' for i in (13,15)]
            digests=[hashlib.sha256(p.read_bytes()).hexdigest() for p in files]
            self.assertEqual([json.loads(p.read_text())['map_id'] for p in files],[13,15])
            self.assertEqual([r['enemy_source']['spawns'] for r in json.loads(report.read_text())['maps']],[1,1])
            with patch.object(sys,'argv',args), \
                 patch.object(cli,'build_npcs',side_effect=lambda s,d,m,p: {'status':'pass','map_id':m}), \
                 patch.object(cli,'compile_spawn_evidence',side_effect=fake_population):
                self.assertEqual(cli.main(),0)
            self.assertEqual([hashlib.sha256(p.read_bytes()).hexdigest() for p in files],digests)
            def broken(_source,_server,map_id):
                if map_id==15:
                    raise ValueError('missing original ACT')
                return fake_population(_source,_server,map_id)
            with patch.object(sys,'argv',args), \
                 patch.object(cli,'build_npcs',side_effect=lambda s,d,m,p: {'status':'pass','map_id':m}), \
                 patch.object(cli,'compile_spawn_evidence',side_effect=broken):
                self.assertEqual(cli.main(),1)
            self.assertEqual(json.loads(report.read_text())['status'],'partial')
            self.assertEqual([hashlib.sha256(p.read_bytes()).hexdigest() for p in files],digests)


if __name__ == '__main__':
    unittest.main()
