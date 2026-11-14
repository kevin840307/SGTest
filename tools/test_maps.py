from pathlib import Path
import struct
import unittest
from build_maps import dimensions, object_sprite, resolve


class MapTest(unittest.TestCase):
    def test_grid_payload_size_is_exact(self):
        self.assertEqual(dimensions(struct.pack('<II',2,2)+b'\0'*8,2),(2,2))
        with self.assertRaises(ValueError):
            dimensions(struct.pack('<II',2,2)+b'\0'*7,2)

    def test_original_building_alias_is_a_reference_not_a_missing_picture(self):
        root = Path('.cache/client').resolve()
        sprite = object_sprite(root,root/'map/object/wall/m_baluster_c02.blg')
        self.assertEqual(sprite.name,'m_balusterl_c02.spr')
        self.assertTrue(sprite.is_file())

    def test_reference_cannot_escape_original_tree(self):
        with self.assertRaises(ValueError):
            resolve(Path('.cache/client'),'../../outside.spr')


if __name__ == '__main__':
    unittest.main()
