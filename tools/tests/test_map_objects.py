"""Synthetic original root/child ObjectList; no private map bytes."""
import struct
import sys
import unittest
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.build_maps import object_records, objects


def record(name, x, y, children=None):
    encoded = name.encode('cp950')
    return bytes([len(encoded)]) + encoded + struct.pack('<ii', x, y) + (
        struct.pack('<i', children) if children is not None else b'')


class MapObjectRecordsTests(unittest.TestCase):
    def test_roots_and_children_keep_absolute_xy_and_source_order(self):
        raw = (struct.pack('<I',5) + record('wall.blg',32,64,0)
               + record('room.blg',73,99,2) + record('table.blg',160,192)
               + record('lamp.sth',-240,256) + record('tree.blg',400,300,0))
        expected = [
            {'name':'wall.blg','p':[32,64],'parent_index':None,'child_count':0},
            {'name':'room.blg','p':[73,99],'parent_index':None,'child_count':2},
            {'name':'table.blg','p':[160,192],'parent_index':1,'child_count':0},
            {'name':'lamp.sth','p':[-240,256],'parent_index':1,'child_count':0},
            {'name':'tree.blg','p':[400,300],'parent_index':None,'child_count':0},
        ]
        self.assertEqual(object_records(raw), expected)
        self.assertEqual(object_records(raw+bytes(4)), expected)

    def test_empty_and_child_path_without_old_heuristic_prefix(self):
        self.assertEqual(object_records(struct.pack('<I',0)), [])
        raw = struct.pack('<I',2)+record('A.blg',1,2,1)+record('furniture.sth',3,4)
        self.assertEqual(object_records(raw)[1]['parent_index'],0)

    def test_invalid_counts_lengths_encoding_and_trailers(self):
        good = struct.pack('<I',1)+record('A.blg',1,2,0)
        bad = [b'', struct.pack('<I',1_000_001), struct.pack('<I',1),
               struct.pack('<I',1)+bytes(1), good[:-1], good+b'junk', good+bytes(2),
               good.replace(b'A.blg',bytes([255])+b'.blg'),
               struct.pack('<I',1)+record('A.blg',1,2,-1),
               struct.pack('<I',1)+record('A.blg',1,2,1),
               struct.pack('<I',2)+record('A.sth',1,2,1)+record('B.sth',3,4),
               struct.pack('<I',2)+record('A.blg',1,2,1),
               struct.pack('<I',2)+record('A.blg',1,2,1)+record('B.blg',3,4,1)]
        for raw in bad:
            with self.subTest(raw=raw[:24]), self.assertRaises(ValueError):
                object_records(raw)

    def test_published_objects_keep_hierarchy_zero_z_and_trim_origin_bounds(self):
        raw = (struct.pack('<I',2)+record('room.blg',100,200,1)
               +record('table.blg',120,240))
        image = Image.new('RGBA',(8,8))
        image.paste((255,0,0,255),(2,3,6,7))
        frame = SimpleNamespace(image=image,origin_x=5,origin_y=9)
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp)
            sprite = source/'map'/'test.spr'
            sprite.parent.mkdir()
            sprite.write_bytes(b'synthetic')
            with patch('asset_converter.content.build_maps.object_sprite',return_value=sprite), \
                 patch('asset_converter.content.build_maps.decode_spr',return_value=[frame]) as decode:
                rows = objects(raw,source)
                self.assertEqual(decode.call_count,1)
        self.assertEqual([r['z'] for r in rows],[0,0])
        self.assertEqual([r['parent_index'] for r in rows],[None,0])
        self.assertEqual([r['child_count'] for r in rows],[1,0])
        self.assertEqual([r['bounds'] for r in rows],[[97,194,4,4],[117,234,4,4]])
        self.assertTrue(all('z_source_present' not in r for r in rows))


if __name__ == '__main__':
    unittest.main()
