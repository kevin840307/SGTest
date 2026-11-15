import sys
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.runtime_pack import use_vram_compression


class RuntimePackPolicyTests(unittest.TestCase):
    def test_world_visuals_use_vram_compression(self):
        for value in [
            "Enemy/en001a/page_0.png",
            "npcs/shopkeeper/page_0.png",
            "players/b/base/body/page_0.png",
            "equipment/sword/page_0.png",
            "pets/pet01/page_0.png",
            "mecha/m01/page_0.png",
            "effects/skill_effect/e01/page_0.png",
            "maps/visuals/tree/page_0.png",
            "items/ground/item01/page_0.png",
        ]:
            self.assertTrue(use_vram_compression(Path(value)), value)

    def test_pixel_sensitive_ui_and_icons_stay_lossless(self):
        for value in [
            "ui/account_login/page_0.png",
            "ui/bg_bank/page_0.png",
            "items/icons/item01/page_0.png",
        ]:
            self.assertFalse(use_vram_compression(Path(value)), value)


if __name__ == "__main__":
    unittest.main()
