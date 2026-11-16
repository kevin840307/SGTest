import sys
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.runtime_pack import use_vram_compression
from asset_converter.smoke_runtime_pack import real_contract
from PIL import Image


class RuntimePackPolicyTests(unittest.TestCase):
    def fixture(self, folder):
        assets = Path(folder)
        page = assets/'Enemy/fixture/page_0.png'
        page.parent.mkdir(parents=True)
        Image.new('RGBA',(8,12),(40,80,120,255)).save(page)
        manifest = {'pages':['page_0.png'],'page_sizes':[[8,12]],
                    'page_sha256':{'page_0.png':hashlib.sha256(page.read_bytes()).hexdigest()}}
        (page.parent/'atlas.json').write_text(json.dumps(manifest),encoding='utf-8')
        (assets/'catalog').mkdir()
        (assets/'catalog/assets.json').write_text(json.dumps({'categories':{'Enemy':{'index':'catalog/Enemy.json'}}}),encoding='utf-8')
        (assets/'catalog/Enemy.json').write_text(json.dumps({'assets':{'Enemy/fixture':{
            'kind':'sprite_atlas','file':'Enemy/fixture/atlas.json'}}}),encoding='utf-8')
        return assets, manifest

    def test_real_smoke_contract_binds_metadata_hashes_and_actual_dimensions(self):
        with tempfile.TemporaryDirectory() as folder:
            assets, _ = self.fixture(folder)
            contract, metadata = real_contract(assets, [])
            self.assertEqual(metadata['atlas_assets'],1)
            self.assertEqual(len(contract['files']),4)
            self.assertEqual(contract['textures'],[{'path':'res://assets/Enemy/fixture/page_0.png','width':8,'height':12}])
            page = assets/'Enemy/fixture/page_0.png'
            page.write_bytes(b'corrupt')
            with self.assertRaisesRegex(ValueError,'atlas page hash differs'):
                real_contract(assets, [])

    def test_real_smoke_rejects_missing_dimension_records(self):
        with tempfile.TemporaryDirectory() as folder:
            assets, manifest = self.fixture(folder)
            manifest['page_sizes']=[]
            (assets/'Enemy/fixture/atlas.json').write_text(json.dumps(manifest),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'dimension count differs'):
                real_contract(assets, [])

    def test_real_smoke_checks_physical_extent_for_block_aligned_publication(self):
        with tempfile.TemporaryDirectory() as folder:
            assets, manifest = self.fixture(folder)
            manifest['page_layout']='height_dense_p0_block4_v1'
            path=assets/'Enemy/fixture/atlas.json'
            path.write_text(json.dumps(manifest),encoding='utf-8')
            contract,_=real_contract(assets,[])
            self.assertTrue(contract['textures'][0]['block_aligned'])
            page=assets/'Enemy/fixture/page_0.png'
            Image.new('RGBA',(7,11),(40,80,120,255)).save(page)
            manifest['page_sizes']=[[7,11]]
            manifest['page_sha256']['page_0.png']=hashlib.sha256(page.read_bytes()).hexdigest()
            path.write_text(json.dumps(manifest),encoding='utf-8')
            with self.assertRaisesRegex(ValueError,'not block aligned'):
                real_contract(assets,[])

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
