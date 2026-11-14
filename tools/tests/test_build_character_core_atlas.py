import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from build_character_core_atlas import collect_physical_states


class CharacterAtlasTests(unittest.TestCase):
    def test_logical_mirrors_collapse_to_physical_state(self):
        audit = {"logical_states": [
            {"action_id": 0, "action": "NS", "facing": 3, "source_direction": 3,
             "flip_x": False, "sources": {"Body": "A-Body_NS3.spr"},
             "frames": [{"index": 0, "raw_time": 6}], "loops": True, "events": [],
             "draw_key": "NS3", "draw_slots": [10], "raw_layers": ["Body+2"]},
            {"action_id": 0, "action": "NS", "facing": 5, "source_direction": 3,
             "flip_x": True, "sources": {"Body": "A-Body_NS3.spr"},
             "frames": [{"index": 0, "raw_time": 6}], "loops": True, "events": [],
             "draw_key": "NS3", "draw_slots": [10], "raw_layers": ["Body+2"]},
        ]}
        states = collect_physical_states(audit)
        self.assertEqual(len(states), 1)
        self.assertEqual(states[0]["source_direction"], 3)


if __name__ == "__main__":
    unittest.main()
