"""Join original spawn records to HK2 visual manifests; emit private map content."""
import argparse
import hashlib
import json
from pathlib import Path
from build_enemy_map_palette import read_enemy_catalog, read_enemy_dat

def compile_population(spawns, catalog, assets, map_id):
    definitions=[]
    for enemy_id in sorted({row['enemy_id'] for row in spawns}):
        if enemy_id not in catalog:
            raise ValueError(f'unresolved enemy definition {enemy_id}')
        definition=catalog[enemy_id]
        sprite=definition['sprite'].upper()
        if not sprite.isascii() or not sprite.isalnum():
            raise ValueError(f'invalid sprite identity {sprite}')
        path=assets/'enemy'/(sprite.lower()+'.json')
        manifest=json.loads(path.read_text(encoding='utf-8'))
        for action in ['NS','NW','FA','NH','ND']:
            for facing in range(1,9):
                clip=f'{sprite}_{action}{facing}'
                if clip not in manifest['clips'] and clip not in manifest['aliases']:
                    raise ValueError(f'missing action/direction {clip}')
        definitions.append({'id':enemy_id,'sprite':sprite,'atlas':f'enemy/{sprite.lower()}.json'})
    return {'version':1,'map_id':map_id,'definitions':definitions,
            'spawns':[{'definition':r['enemy_id'],'p':[r['x'],r['y']],
                       'source_index':r['source_index'],'source_flags':r['flags']} for r in spawns],
            'policy':'original spawn positions and visuals; prototype combat stats remain explicit defaults'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('server_dat',type=Path)
    parser.add_argument('--assets',type=Path,default=Path('client/assets/generated'))
    parser.add_argument('--map',type=int,default=13)
    args=parser.parse_args()
    source=args.server_dat/'Map'/f'Map{args.map}'/'Enemy.dat'
    catalog=args.server_dat/'Enemy'/'Enemy_Data.csv'
    document=compile_population(read_enemy_dat(source),read_enemy_catalog(catalog),args.assets,args.map)
    document['sources']={p.relative_to(args.server_dat).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in [source,catalog]}
    raw=(json.dumps(document,ensure_ascii=False,separators=(',',':'))+'\n').encode()
    dest=args.assets/'maps'/str(args.map)/'population.json'
    dest.write_bytes(raw)
    report={'map_id':args.map,'definitions':len(document['definitions']),'spawns':len(document['spawns']),
            'sha256':hashlib.sha256(raw).hexdigest(),'sources':document['sources']}
    Path('reports/population_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2))

if __name__=='__main__': main()
