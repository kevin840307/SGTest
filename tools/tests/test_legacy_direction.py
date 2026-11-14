import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from legacy_direction import map_character_direction, map_enemy_direction


class DirectionTests(unittest.TestCase):
    def test_default_action(self):
        self.assertEqual([map_character_direction(d, 0) for d in range(1, 9)],
                         [(1, False), (2, False), (3, False), (4, False),
                          (3, True), (2, True), (1, True), (8, False)])

    def test_action_specific_edges(self):
        for action_id in (4, 9, 13, 17):
            self.assertEqual(map_character_direction(4, action_id), (3, True))
            self.assertEqual(map_character_direction(8, action_id), (1, True))
        for action_id in (3, 18):
            self.assertEqual(map_character_direction(4, action_id), (4, False))
            self.assertEqual(map_character_direction(8, action_id), (8, False))

    def test_invalid_facing(self):
        for facing in (0, 9, -1):
            with self.assertRaises(ValueError):
                map_character_direction(facing, 0)

    def test_enemy_action_specific_mapping(self):
        self.assertEqual([map_enemy_direction(d, 0) for d in range(1, 9)],
                         [(1, False), (1, False), (3, False), (3, True),
                          (3, True), (1, True), (1, True), (1, True)])
        self.assertEqual([map_enemy_direction(d, 1) for d in range(1, 9)],
                         [(1, False), (2, False), (3, False), (4, False),
                          (3, True), (2, True), (1, True), (8, False)])
        self.assertEqual(map_enemy_direction(6, 2), (1, True))
        self.assertEqual(map_enemy_direction(6, 3), (2, True))
        for action_id in (-1, 5):
            with self.assertRaises(ValueError):
                map_enemy_direction(1, action_id)


if __name__ == "__main__":
    unittest.main()
