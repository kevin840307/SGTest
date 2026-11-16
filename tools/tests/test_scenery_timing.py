"""Synthetic literal scenery action bindings; no original asset payloads."""
import struct
import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_converter.source.scenery import compile_scenery_action


def act(frames=((1,6),(2,300)),loop=True,key='action'):
    name = key.encode()+b'\0'
    return (b'\1'+struct.pack('<I',len(name))+name+b'\3'+struct.pack('<I',1)
            +bytes([loop,2])+struct.pack('<I',len(frames))
            +b''.join(struct.pack('<II',*row) for row in frames)+b'\xff')


class SceneryTimingTests(unittest.TestCase):
    def test_literal_binding_keeps_nonzero_first_frame_and_long_empty_hold(self):
        result = compile_scenery_action(act(),3)
        self.assertEqual(result['frames'],[{'index':1,'raw_time':6},{'index':2,'raw_time':300}])
        self.assertTrue(result['loops'])
        self.assertEqual(result['fixed_hz'],50)
        self.assertEqual(result['default_speed'],25)
        self.assertFalse(result['dynamic_speed_verified'])

    def test_rejects_missing_duplicate_empty_and_out_of_bounds_bindings(self):
        for raw,count in [(act(key='other'),3),(act()[:-1]+act(),3),(act(frames=()),3),
                          (act(),2),(act()[:-3],3)]:
            with self.subTest(raw=raw[:20]), self.assertRaises(ValueError):
                compile_scenery_action(raw,count)

    def test_zero_duration_preserved_for_runtime_minimum_one_update(self):
        result = compile_scenery_action(act(((0,0),),False),1)
        self.assertEqual(result['frames'],[{'index':0,'raw_time':0}])
        self.assertFalse(result['loops'])
