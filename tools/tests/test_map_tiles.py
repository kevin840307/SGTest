import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.build_maps import build


class OriginalTileMappingTests(unittest.TestCase):
    def test_runtime_atlas_uses_original_signed_tile_plus_one_mapping(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            source = root / 'source'
            output = root / 'output'
            (source / 'map/map01').mkdir(parents=True)
            (source / 'map/map1').mkdir(parents=True)

            side = 16
            # Four distinct 16x16 RGB565 source tiles. The original client reads
            # past the 8-byte WH header, then indexes this payload with
            # (signed MapBlock value + 1) wrapped to ushort.
            pixels = b''.join(struct.pack('<H', value) * (side * side)
                              for value in (0x001f, 0x07e0, 0xf800, 0xffff))
            pic = struct.pack('<II', side, side * 4) + pixels
            (source / 'map/map01/map.pic').write_bytes(pic)

            raw_ids = (0, 1, 0xffff, 2)
            mapblock = struct.pack('<II4H', 2, 2, *raw_ids)
            (source / 'map/map1/mapblock.map').write_bytes(mapblock)
            (source / 'map/map1/blocking.dat').write_bytes(
                struct.pack('<II', 2, 2) + bytes(4))
            (source / 'map/map1/objectlist.dat').write_bytes(struct.pack('<I', 0))
            (source / 'map/map1.map').write_text(
                '[Map]\n'
                'Tiles = Map\\Map01\\Map.pic\n'
                'Array = Map\\Map1\\MapBlock.map\n'
                'List = Map\\Map1\\ObjectList.dat\n'
                'Blocking = Map\\Map1\\Blocking.dat\n'
                'Name = fixture\n',
                encoding='cp950')

            result = build(source, output, 1)
            world = json.loads((output / 'maps/1/world.json').read_text(encoding='utf-8'))
            self.assertEqual(world['tile_source_rule'], 'int16_plus_one_wrap')
            self.assertEqual(world['tile_ids'], [0, 1, 2, 65535])
            self.assertEqual(result['tiles'], 4)
            self.assertEqual((output / 'maps/1/tiles.bin').read_bytes(), mapblock)

            source_strip = Image.frombytes('RGB', (side, side * 4), pic[8:], 'raw', 'BGR;16')
            atlas = Image.open(output / 'maps/1/terrain.png').convert('RGB')
            expected_sources = [1, 2, 3, 0]
            for atlas_index, source_index in enumerate(expected_sources):
                x = (atlas_index % 2) * side
                y = (atlas_index // 2) * side
                actual = atlas.crop((x, y, x + side, y + side))
                expected = source_strip.crop(
                    (0, source_index * side, side, (source_index + 1) * side))
                self.assertEqual(actual.tobytes(), expected.tobytes())

            # This is the regression that previously made Map15 tiles look misplaced:
            # raw 0 must NOT select PIC tile 0.
            self.assertNotEqual(
                atlas.crop((0, 0, side, side)).tobytes(),
                source_strip.crop((0, 0, side, side)).tobytes())


if __name__ == '__main__':
    unittest.main()
