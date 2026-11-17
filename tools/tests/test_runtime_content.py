"""Runtime publication contracts using synthetic maps/NPCs, no game media."""
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import patch

import test_npc_content as npc_fixture
from asset_converter import cli
from asset_converter.content.build_population import compile_population
from asset_converter.content.export_assets import PLAYER_PART_ASSETS


class RuntimeContentTests(unittest.TestCase):
    def fixture(self, folder):
        source, server, _ = npc_fixture.NpcContentTests().fixture(folder)
        (server/'Enemy').mkdir()
        (server/'Enemy/Enemy_Data.csv').write_bytes(b'')
        (server/'Map/Map01/Enemy.dat').write_bytes(bytes(4))
        assets = Path(folder)/'assets'
        for relative in ['catalog/assets.json', 'npcs/npc001/atlas.json',
                         *(asset+'/atlas.json' for asset in PLAYER_PART_ASSETS)]:
            path = assets/relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('{}', encoding='utf-8')
        return source, server, assets

    def test_runtime_publishes_npcs_with_source_mapping_and_unverified_population(self):
        with tempfile.TemporaryDirectory() as folder:
            source, server, assets = self.fixture(folder)
            with patch.object(cli, 'REPO', Path(folder)):
                result = cli.build_runtime_assets(source, assets, SimpleNamespace(server_dat=server, map=1))
            self.assertEqual(result['files_published'], 6)
            self.assertEqual(result['validation']['npcs'], 1)
            npc = json.loads((assets/'maps/1/npcs.json').read_text(encoding='utf-8'))
            self.assertEqual(npc['npcs'][0]['visual']['atlas'], 'npcs/npc001/atlas.json')
            population = json.loads((assets/'maps/1/population.json').read_text(encoding='utf-8'))
            self.assertEqual(population['version'], 3)
            self.assertFalse(population['stats_verified'])
            self.assertFalse(population['gameplay_ready'])

    def test_missing_npc_atlas_rejects_before_replacing_prior_map_content(self):
        with tempfile.TemporaryDirectory() as folder:
            source, server, assets = self.fixture(folder)
            prior = assets/'maps/1/npcs.json'
            prior.parent.mkdir(parents=True)
            prior.write_text('previous published data', encoding='utf-8')
            (assets/'npcs/npc001/atlas.json').unlink()
            before = {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()}
            with patch.object(cli, 'REPO', Path(folder)), self.assertRaisesRegex(ValueError, 'missing independent NPC'):
                cli.build_runtime_assets(source, assets, SimpleNamespace(server_dat=server, map=1))
            self.assertEqual(before, {p.relative_to(assets):p.read_bytes() for p in assets.rglob('*') if p.is_file()})

    def test_population_preserves_original_spawns_without_inventing_stats(self):
        with tempfile.TemporaryDirectory() as folder:
            assets = Path(folder)
            manifest = assets/'Enemy/en001a/atlas.json'
            manifest.parent.mkdir(parents=True)
            clips = {f'EN001A_{action}{facing}':[] for action in ('NS','NW','FA','NH','ND') for facing in range(1,9)}
            manifest.write_text(json.dumps({'clips':clips}), encoding='utf-8')
            spawns = [{'enemy_id':1,'x':100,'y':200,'source_index':7,'flags':9}]
            result = compile_population(
                spawns,
                {1:{'sprite':'EN001A','level':7,'max_hp':456,'stats':{'hp':999}}},
                assets, 13)
            self.assertEqual(result['definitions'][0]['source_combat_evidence'], {
                'level':7,'max_hp':456,'source_columns':{'level':3,'max_hp':9}})
            self.assertIsNone(result['definitions'][0]['stats'])
            self.assertFalse(result['definitions'][0]['stats_verified'])
            self.assertEqual(result['spawns'], [{'definition':1,'p':[100,200],'source_index':7,'source_flags':9}])
            self.assertFalse(result['gameplay_ready'])


if __name__ == '__main__':
    unittest.main()
