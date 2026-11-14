import copy
import struct
import tempfile
import unittest
from pathlib import Path

from animation_definition import compile_definition, validate_definition, content_hash
from test_assets import spr


def action(key, loop, steps):
    name = key.encode('ascii')+b'\0'
    return (b'\x01'+struct.pack('<I',len(name))+name+b'\x03'+struct.pack('<I',1)
            +bytes([loop,2])+struct.pack('<I',len(steps))
            +b''.join(struct.pack('<II',*pair) for pair in steps))


class AnimationDefinitionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.act = self.root/'E.ACT'
        event = b'\x04'+struct.pack('<I',8)+struct.pack('<I',1)+b'X\0\0\0'
        self.act.write_bytes(action('S',1,[(0,0),(0,5)])+event+b'\xff')
        self.sources = []
        for name in ['E_NS1','E_NS3']:
            path = self.root/(name+'.spr')
            path.write_bytes(spr())
            self.sources.append(path)

    def compile(self, **kwargs):
        return compile_definition('enemy/e', self.sources, self.root, 'monster', self.act, **kwargs)

    def test_aliases_share_original_frames_and_preserve_pivot(self):
        data = self.compile()
        views = data['behaviors']['NS']['facings']
        self.assertEqual(set(views), set(map(str,range(1,9))))
        self.assertEqual(views['2']['clip'], 'E_NS1')
        self.assertEqual(views['8']['clip'], 'E_NS1')
        self.assertTrue(views['8']['flip_h'])
        self.assertTrue(views['4']['flip_h'])
        self.assertEqual(len(data['frames']), 2)
        self.assertEqual(data['frames'][0]['pivot'], [2,3])
        self.assertEqual(data['frames'][0]['source_index'], 0)
        self.assertNotIn('page', data['frames'][0])

    def test_repeated_timeline_min_tick_and_event_step_are_lossless(self):
        data = self.compile()
        behavior = data['behaviors']['NS']
        self.assertEqual([step['frame_index'] for step in behavior['timeline']], [0,0])
        self.assertEqual([step['raw_time'] for step in behavior['timeline']], [0,5])
        self.assertEqual([step['default_ticks'] for step in behavior['timeline']], [1,8])
        self.assertEqual(behavior['events'], [{'step_index':1,'token':'X','raw_payload_hex':'58000000'}])
        self.assertTrue(behavior['loop'])

    def test_missing_source_fails_instead_of_inventing_a_view(self):
        self.sources.pop()
        with self.assertRaisesRegex(ValueError, 'required source E_NS3 is missing'):
            self.compile()

    def test_bad_act_frame_is_rejected_without_clamping(self):
        self.act.write_bytes(action('S',1,[(1,5)])+b'\xff')
        with self.assertRaisesRegex(ValueError, 'outside source clip'):
            self.compile()

    def test_definition_is_independent_of_source_iteration_order(self):
        first = self.compile()
        self.sources.reverse()
        self.assertEqual(first, self.compile())

    def test_character_uses_shared_act_and_action_specific_mirrors(self):
        names = self.root/'names.dat'
        names.write_text('NS,NR,NU,NB,AS,AH,AA,AB,AG,GS,GH,GA,GB,',encoding='ascii')
        draw = self.root/'draw.dat'
        draw.write_text('AS1: Body+2>Head\nAS2: Head>Body+2\nAS3: Body+2>Head\n',encoding='ascii')
        self.act.write_bytes(action('AS',1,[(0,5)])+b'\xff')
        sources = []
        for facing in [1,2,3]:
            path = self.root/f'B-BODY_AS{facing}.spr'
            path.write_bytes(spr())
            sources.append(path)
        data = compile_definition('player/b/body',sources,self.root,'character_part',self.act,names,draw)
        views = data['behaviors']['AS']['facings']
        self.assertEqual(views['4']['source_facing'],3)
        self.assertTrue(views['4']['flip_h'])
        self.assertTrue(views['8']['flip_h'])
        self.assertEqual(data['draw_order']['AS3'], ['Body+2','Head'])
        self.assertEqual(data['draw_slots']['AS3'], [10,9,0])
        self.assertEqual(views['4']['draw_order_key'], 'AS3')
        self.assertEqual(views['4']['draw_layers'], ['Body+2','Head'])
        self.assertEqual(views['4']['draw_slots'], [10,9,0])

    def test_one_shot_has_explicit_single_view_policy(self):
        path = self.root/'B-DEATH_ND1.spr'
        path.write_bytes(spr())
        self.act.write_bytes(action('D',0,[(0,3)])+b'\xff')
        data = compile_definition('player/b/death',[path],self.root,'single_view',self.act)
        self.assertFalse(data['behaviors']['ND']['loop'])
        self.assertTrue(all(view['source_facing']==1 and not view['flip_h']
                            for view in data['behaviors']['ND']['facings'].values()))

    def test_v1_schema_is_rejected_after_z_order_contract_upgrade(self):
        data = self.compile()
        data['version'] = 1
        data['content_sha256'] = content_hash({k:v for k,v in data.items() if k!='content_sha256'})
        with self.assertRaisesRegex(ValueError, 'unsupported animation definition schema'):
            validate_definition(data)

    def test_hash_and_reference_corruption_rejected(self):
        data = self.compile()
        damaged = copy.deepcopy(data)
        damaged['frames'][0]['pivot'][0] += 1
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            validate_definition(damaged)
        damaged = copy.deepcopy(data)
        damaged['clips']['E_NS1']['frames'] = [-1]
        damaged['content_sha256'] = content_hash({k:v for k,v in damaged.items() if k!='content_sha256'})
        with self.assertRaisesRegex(ValueError, 'invalid frame reference'):
            validate_definition(damaged)

    def test_character_draw_order_corruption_is_rejected(self):
        names = self.root/'names.dat'
        names.write_text('NS,NR,NU,NB,AS,AH,AA,AB,AG,GS,GH,GA,GB,',encoding='ascii')
        draw = self.root/'draw.dat'
        draw.write_text('AS1: Body+2>Head\nAS2: Head>Body+2\nAS3: Body+2>Head\n',encoding='ascii')
        self.act.write_bytes(action('AS',1,[(0,5)])+b'\xff')
        sources = []
        for facing in [1,2,3]:
            path = self.root/f'B-BODY_AS{facing}.spr'
            path.write_bytes(spr())
            sources.append(path)
        data = compile_definition('player/b/body',sources,self.root,'character_part',self.act,names,draw)
        data['behaviors']['AS']['facings']['4']['draw_slots'] = [0]
        data['content_sha256'] = content_hash({k:v for k,v in data.items() if k!='content_sha256'})
        with self.assertRaisesRegex(ValueError, 'draw-order view does not match normalized table'):
            validate_definition(data)


if __name__ == '__main__':
    unittest.main()
