"""Join original spawn records to HK2 visual manifests; emit private map content."""
import argparse
import hashlib
import json
from pathlib import Path
from .build_enemy_map_palette import read_enemy_catalog, read_enemy_dat
from .asset_paths import monster_manifest
from .build_maps import server_map_directory
from .animation_definition import compile_definition

def compile_spawn_evidence(source: Path, server_dat: Path, map_id: int) -> dict:
    """Original Enemy.dat + ACT evidence, without invented combat stats or live assets.

    This is separate from compile_population(), which requires published Atlas
    manifests and may use provisional runtime stats. Source evidence must never
    publish either those defaults or placeholder atlas files as verified facts.
    """
    server_dat = Path(server_dat).resolve()
    source = Path(source).resolve()
    enemy_dat = server_map_directory(server_dat, map_id) / 'Enemy.dat'
    catalog_path = server_dat / 'Enemy' / 'Enemy_Data.csv'
    spawns = read_enemy_dat(enemy_dat)
    catalog = read_enemy_catalog(catalog_path)
    definitions = []
    for enemy_id in sorted({row['enemy_id'] for row in spawns}):
        if enemy_id not in catalog:
            raise ValueError(f'unresolved original Enemy_Data.csv definition {enemy_id}')
        entry = catalog[enemy_id]
        sprite = entry['sprite'].lower()
        if not sprite.isascii() or not sprite.isalnum():
            raise ValueError(f'unsafe Enemy sprite name for {enemy_id}')
        sprite_dir = source / 'enemy'
        act = sprite_dir / f'{sprite}.act'
        files = sorted(sprite_dir.glob(f'{sprite}_*.spr'))
        if not act.is_file() or not files:
            raise ValueError(f'missing original ACT/SPR for Enemy {enemy_id}: {sprite}')
        animation = compile_definition('Enemy/'+sprite, files, source, 'monster', act)
        definitions.append({
            'enemy_id': enemy_id, 'name': entry['name'], 'level': entry['level'],
            'sprite': entry['sprite'], 'atlas': monster_manifest(entry['sprite']),
            'animation_content_sha256': animation['content_sha256'],
            'animation_behaviors': sorted(animation['behaviors']),
            'logical_facings': 8, 'frame_count': len(animation['frames']),
            'source_files_sha256': animation['source_sha256'],
            'combat_stats_verified': False,
        })
    return {
        'version': 1, 'kind': 'original_spawn_evidence_not_runtime',
        'map_id': map_id,
        'definitions': definitions,
        'spawns': [{'source_index': row['source_index'], 'definition': row['enemy_id'],
                    'p': [row['x'], row['y']], 'source_flags': row['flags']}
                   for row in spawns],
        'sources': {
            p.relative_to(server_dat).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in (enemy_dat, catalog_path)
        },
        'stats_verified': False, 'runtime_implemented': False,
    }


DEFAULT_STATS = {
    'hp': 30,
    'damage': 10,
    'move_speed': 65.0,
    'aggro_range': 500.0,
    'attack_range': 70.0,
    'chase_stop_range': 45.0,
    'attack_interval_seconds': 1.0,
    'respawn_seconds': 2.0,
}

def validate_stats(value):
    stats = {**DEFAULT_STATS, **(value or {})}
    numeric = all(isinstance(stats[key], (int, float)) and not isinstance(stats[key], bool)
                  for key in DEFAULT_STATS)
    if not numeric:
        raise ValueError('monster stats must be numeric')
    if not (1 <= int(stats['hp']) <= 1_000_000 and
            0 <= int(stats['damage']) <= 1_000_000 and
            0 <= float(stats['move_speed']) <= 2000 and
            0 <= float(stats['aggro_range']) <= 5000 and
            0 <= float(stats['attack_range']) <= 1000 and
            0 <= float(stats['chase_stop_range']) <= 1000 and
            0.05 <= float(stats['attack_interval_seconds']) <= 60 and
            0 <= float(stats['respawn_seconds']) <= 3600 and
            float(stats['attack_range']) <= float(stats['aggro_range']) and
            float(stats['chase_stop_range']) <= float(stats['aggro_range'])):
        raise ValueError('monster stats outside supported bounds')
    stats['hp'] = int(stats['hp'])
    stats['damage'] = int(stats['damage'])
    for key in DEFAULT_STATS:
        if key not in ('hp', 'damage'):
            stats[key] = float(stats[key])
    return stats

def compile_population(spawns, catalog, assets, map_id):
    definitions=[]
    for enemy_id in sorted({row['enemy_id'] for row in spawns}):
        if enemy_id not in catalog:
            raise ValueError(f'unresolved enemy definition {enemy_id}')
        definition=catalog[enemy_id]
        sprite=definition['sprite'].upper()
        if not sprite.isascii() or not sprite.isalnum():
            raise ValueError(f'invalid sprite identity {sprite}')
        relative=monster_manifest(sprite)
        path=assets/relative
        manifest=json.loads(path.read_text(encoding='utf-8'))
        for action in ['NS','NW','FA','NH','ND']:
            for facing in range(1,9):
                clip=f'{sprite}_{action}{facing}'
                if (clip not in manifest.get('logical_views',{})
                        and clip not in manifest.get('clips',{})
                        and clip not in manifest.get('aliases',{})):
                    raise ValueError(f'missing logical action/direction {clip}')
        definitions.append({'id':enemy_id,'sprite':sprite,'atlas':relative,
                            'stats':validate_stats(definition.get('stats'))})
    return {'version':2,'map_id':map_id,'definitions':definitions,
            'spawns':[{'definition':r['enemy_id'],'p':[r['x'],r['y']],
                       'source_index':r['source_index'],'source_flags':r['flags']} for r in spawns],
            'policy':'original spawn positions; each monster visual is an independent runtime asset selected by the server definition'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('server_dat',type=Path)
    parser.add_argument('--assets',type=Path,default=Path('client/assets'))
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
