"""Synthetic SIF/BLG and shared map presentation dependencies."""
import copy
import hashlib
import struct
import tempfile
from pathlib import Path
import unittest

from test_asset_converter import tiny_spr
from test_map_objects import record
from asset_converter.source.scenery import parse_blg, parse_sif_contract
from asset_converter.content.build_maps import objects, scenery_asset_paths


def name(value):
    raw = value.encode('cp950')
    return bytes([len(raw)])+raw


def sif(sprite='a.spr', mode=1, center=(3,4), size=(8,9)):
    return name(sprite)+(b'' if mode is None else bytes([mode]))+struct.pack('<4i',*center,*size)


def blg(primary='a.sth', alternate='b.sth', front=(-20,5,30,-7), groups=(((1,2),),((3,4),))):
    return (name('X:\\synthetic\\a.sth')+name(primary)
            +name('X:\\synthetic\\b.sth')+name(alternate)
            +struct.pack('<4i',*front)+b''.join(struct.pack('<I',len(g))+b''.join(
                struct.pack('<2i',*p) for p in g) for g in groups))


class SceneryTests(unittest.TestCase):
    def test_sif_legacy_default_and_explicit_mode_centers_extents(self):
        for mode in (None,0,1,2,255):
            result = parse_sif_contract(sif(mode=mode,center=(-5,6)))
            self.assertEqual(result['mode'],1 if mode is None else mode)
            self.assertEqual(result['mode_source_present'],mode is not None)
            self.assertEqual(result['center'],[-5,6])
            self.assertEqual(result['size'],[8,9])
        # A 17-byte tail truncated to 16 is structurally indistinguishable from
        # legacy SIF; integrity hashes, not a guessed numeric limit, detect that.
        for bad in (b'',sif()[:-2],sif()+b'0',sif(size=(0,9)),sif('a.sth'),bytes([128])+bytes(128)):
            with self.subTest(bad=bad[:20]), self.assertRaises(ValueError):
                parse_sif_contract(bad)

    def test_blg_front_clamp_and_entrance_sets(self):
        result = parse_blg(blg())
        self.assertEqual(result['front_raw'],[-20,5,30,-7])
        self.assertEqual(result['front'],[-20,-10,30,-7])
        self.assertEqual(result['inside_cells'],[[1,2]])
        self.assertEqual(result['outside_cells'],[[3,4]])
        self.assertEqual(parse_blg(blg(alternate='',groups=((),())))['alternate'],'')
        valid = blg()
        for bad in (valid[:-1],valid+b'0',blg(primary=''),
                    valid[:-12]+struct.pack('<I',0xffffffff)+bytes(8),name('bad\0path')):
            with self.subTest(bad=bad[:20]), self.assertRaises(ValueError):
                parse_blg(bad)

    def fixture(self, temp):
        source = Path(temp)
        folder = source/'map'
        folder.mkdir()
        (folder/'room.blg').write_bytes(blg())
        for stem,mode,center in [('a',0,(3,4)),('b',2,(5,6))]:
            (folder/f'{stem}.sth').write_bytes(name('X:\\synthetic.spr')+name(f'{stem}.spr'))
            (folder/f'{stem}.sif').write_bytes(sif(f'{stem}.spr',mode,center))
            (folder/f'{stem}.spr').write_bytes(tiny_spr())
        return source,folder

    def test_dual_appearance_metadata_deduplicates_and_includes_all_dependencies(self):
        with tempfile.TemporaryDirectory() as temp:
            source,folder = self.fixture(temp)
            raw = struct.pack('<I',2)+record('room.blg',100,200,0)+record('room.blg',300,400,0)
            definitions = {}
            rows = objects(raw,source,definitions)
            self.assertEqual(len(definitions),1)
            definition = definitions['map/room.blg']
            self.assertEqual(definition['sorting_mode'],0)
            self.assertEqual(definition['draw_group'],0)  # Primary's mode, not alternate's.
            self.assertEqual(definition['center'],[5,6])  # Last SIF overwrites geometry.
            self.assertEqual([a['mode'] for a in definition['appearances']],[0,2])
            self.assertEqual(len(definition['source_hashes']),7)
            self.assertEqual(definition['source_hashes']['map/room.blg'],hashlib.sha256(blg()).hexdigest())
            world = {'scenery_schema':'sgtest.scenery/v1','scenery_definitions':definitions,'objects':rows}
            self.assertEqual(scenery_asset_paths(world),{'maps/visuals/a/atlas.json','maps/visuals/b/atlas.json'})
            again = {}
            self.assertEqual(objects(raw,source,again),rows)
            self.assertEqual(again,definitions)
            corrupt = copy.deepcopy(world)
            corrupt['objects'][0]['definition'] = 'missing'
            with self.assertRaises(ValueError):
                scenery_asset_paths(corrupt)
            (folder/'b.sif').write_bytes(sif('a.spr'))
            with self.assertRaisesRegex(ValueError,'SPR mismatch'):
                objects(raw,source)

    def test_absolute_runtime_reference_is_rejected_while_authoring_path_is_ignored(self):
        with tempfile.TemporaryDirectory() as temp:
            source,folder = self.fixture(temp)
            (folder/'room.blg').write_bytes(blg(alternate='X:\\outside.sth'))
            with self.assertRaisesRegex(ValueError,'absolute original resource path'):
                objects(struct.pack('<I',1)+record('room.blg',1,2,0),source)


if __name__ == '__main__':
    unittest.main()
