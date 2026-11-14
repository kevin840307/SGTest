import struct
import tempfile
import unittest
from pathlib import Path
from PIL import Image
from build_assets import aliases, convert_group, directional_layout, group_key
from convert_assets import decode_spr
from extract_client import lzss, safe_relative


def spr(color=0xf800):
    row = struct.pack('<HHH', 1, color, 0x8000)
    return struct.pack('<IIiiI', 1, 1, 2, 3, 20) + row + struct.pack('<II', 0, 1)


class AssetsTest(unittest.TestCase):
    def test_rgb565_origins_and_corruption(self):
        frame = decode_spr(spr())[0]
        self.assertEqual(frame.image.getpixel((0,0)), (255,0,0,255))
        self.assertEqual((frame.origin_x, frame.origin_y), (2,3))
        with self.assertRaises(ValueError):
            decode_spr(spr()[:-5])

    def test_indexed_palette_pixels_precede_frame_header(self):
        row = struct.pack('<H',1)+b'\x07'+struct.pack('<H',0x8000)
        header = struct.pack('<IIiiI',1,1,2,3,0)
        palette = [0]*256
        palette[7] = 0x07e0
        palette_at = len(row)+len(header)
        raw = row+header+struct.pack('<I',1)+struct.pack('<256H',*palette)
        raw += struct.pack('<I',palette_at)+b'Ver\x08'+struct.pack('<II',len(row),1)
        self.assertEqual(decode_spr(raw)[0].image.getpixel((0,0)), (0,255,0,255))

    def test_mirrors_and_monster_idle_reuse(self):
        clips = {'EN004A_NS1':[0], 'EN004A_NS3':[1], 'EN004A_NW2':[2]}
        resolved = aliases(clips, True)
        self.assertEqual(resolved['EN004A_NS2'], {'clip':'EN004A_NS3','flip_h':False})
        self.assertEqual(resolved['EN004A_NS4'], {'clip':'EN004A_NS3','flip_h':True})
        self.assertEqual(resolved['EN004A_NS6'], {'clip':'EN004A_NS1','flip_h':True})
        self.assertEqual(resolved['EN004A_NW6'], {'clip':'EN004A_NW2','flip_h':True})
        self.assertEqual(group_key(Path('Enemy/EN004A_NW1.spr')), 'enemy/en004a')

    def test_conversion_groups_directions_and_preserves_frame_indices(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)/'source'
            output = Path(directory)/'out'
            root.mkdir()
            paths = []
            for name in ['EN001_NS1','EN001_NW1','EN001_NS3','EN001_NW3']:
                path = root/(name+'.spr')
                path.write_bytes(spr())
                paths.append(path)
            for mode in ['entity','direction']:
                doc, _ = convert_group('enemy/en001',paths,root,output,[1024], True, mode)
                self.assertEqual(len(doc['pages']), 1 if mode == 'entity' else 2)
                self.assertEqual(len(doc['frames']),4)
                self.assertEqual(doc['clips']['EN001_NW3'],[3])
                for name, indices in doc['clips'].items():
                    region = doc['direction_regions'][name[-1]]
                    frame = doc['frames'][indices[0]]
                    self.assertEqual(frame['page'],region['page'])
                    x,y,w,h = frame['rect']
                    rx,ry,rw,rh = region['rect']
                    self.assertTrue(rx <= x < x+w <= rx+rw and ry <= y < y+h <= ry+rh)

    def test_lzss_and_archive_traversal(self):
        self.assertEqual(lzss(b'\x07abc',3), b'abc')
        self.assertEqual(lzss(b'\x00\x00\x00',3), b'   ')
        with self.assertRaises(ValueError):
            lzss(b'',1)
        for name in ['../outside', 'C:\\outside', '\\rooted', 'a/../../b']:
            with self.assertRaises(ValueError):
                safe_relative(name)


if __name__ == '__main__':
    unittest.main()
