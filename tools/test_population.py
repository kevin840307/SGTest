import json
import tempfile
import unittest
from pathlib import Path
from build_population import compile_population

class PopulationTest(unittest.TestCase):
    def test_source_spawn_identity_and_shared_visual(self):
        with tempfile.TemporaryDirectory() as folder:
            assets=Path(folder)
            (assets/'enemy').mkdir()
            clips={f'EN001A_{a}{d}':[0] for a in ['NS','NW','FA','NH','ND'] for d in range(1,9)}
            path=assets/'enemy/en001a.json'
            path.write_text(json.dumps({'clips':clips,'aliases':{}}))
            rows=[{'enemy_id':id,'x':x,'y':30,'source_index':i,'flags':9}
                  for i,(id,x) in enumerate([(12,100),(7,200),(12,300)])]
            catalog={id:{'sprite':'EN001A'} for id in [7,12]}
            result=compile_population(rows,catalog,assets,13)
            self.assertEqual([d['id'] for d in result['definitions']],[7,12])
            self.assertEqual([r['definition'] for r in result['spawns']],[12,7,12])
            self.assertEqual(result['version'],2)
            self.assertEqual(result['definitions'][0]['stats']['hp'],30)
            self.assertEqual(result['definitions'][0]['stats']['move_speed'],65.0)
            self.assertEqual(result['spawns'][2]['p'],[300,30])
            self.assertEqual(result['spawns'][2]['source_index'],2)
            del catalog[12]
            with self.assertRaises(ValueError): compile_population(rows,catalog,assets,13)
            catalog[12]={'sprite':'EN001A'}
            del clips['EN001A_ND8']
            path.write_text(json.dumps({'clips':clips,'aliases':{}}))
            with self.assertRaises(ValueError): compile_population(rows,catalog,assets,13)

if __name__=='__main__': unittest.main()
