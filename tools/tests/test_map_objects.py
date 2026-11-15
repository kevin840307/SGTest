"""Synthetic ObjectList mixed XY/XYZ fixtures; no copyrighted map data."""
import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.build_maps import object_records


def record(name, x, y, z=None):
    encoded = name.encode('cp950')
    assert len(encoded) <= 255
    return bytes([len(encoded)]) + encoded + struct.pack('<ii', x, y) + (struct.pack('<i', z) if z is not None else b'')


class MapObjectRecordsTests(unittest.TestCase):
    def test_mixed_legacy_xy_and_xyz_preserves_order_and_exact_values(self):
        raw = (struct.pack('<I', 4)
               + record('OBJECT\\wall\\WALL.blg', 32, 64, 0)
               + record('OBJECT\\Building_object\\ROOM.blg', 73, 99)
               + record('OBJECT\\Plant&Tree\\TREE.blg', 160, 192, 4)
               + record('BUILDING\\HOUSE.blg', 240, 256, 1)
               + b'\0\0\0\0')
        self.assertEqual(object_records(raw), [
            ('OBJECT\\wall\\WALL.blg', 32, 64, 0, True),
            ('OBJECT\\Building_object\\ROOM.blg', 73, 99, 0, False),
            ('OBJECT\\Plant&Tree\\TREE.blg', 160, 192, 4, True),
            ('BUILDING\\HOUSE.blg', 240, 256, 1, True),
        ])
        self.assertEqual(object_records(raw[:-4]), object_records(raw))

    def test_invalid_count_length_and_trailer_rejected(self):
        good = struct.pack('<I', 1) + record('OBJECT\\wall\\A.blg', 1, 2, 0)
        for raw in [b'', struct.pack('<I', 1_000_001), struct.pack('<I', 1),
                    struct.pack('<I', 1)+b'\0', good[:-1], good+b'junk',
                    good+b'\0\0', good.replace(b'A.blg', b'\xff.blg')]:
            with self.subTest(raw=raw[:24]), self.assertRaises(ValueError):
                object_records(raw)

    def test_missing_z_cannot_consume_next_record_or_hide_bad_tail(self):
        good = (struct.pack('<I', 2)
                + record('OBJECT\\Building_object\\INNER.blg', 24, 48)
                + record('OBJECT\\road\\ROAD.blg', 50, 75, 0))
        self.assertFalse(object_records(good)[0][-1])
        with self.assertRaises(ValueError):
            object_records(good + b'bad')
        with self.assertRaises(ValueError):
            object_records(good[:-3])


if __name__ == '__main__':
    unittest.main()
