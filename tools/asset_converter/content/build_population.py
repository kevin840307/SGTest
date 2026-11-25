"""Join original spawn records to HK2 visual manifests; emit private map content."""
import argparse
import hashlib
import json
from pathlib import Path
from .build_enemy_map_palette import read_enemy_catalog, read_enemy_dat
from .asset_paths import monster_manifest
from .build_maps import server_map_directory
from .animation_definition import compile_definition
from ..source.old_enemy_rewards import read_old_enemy_combat_catalog

def compile_spawn_evidence(source: Path, server_dat: Path, map_id: int) -> dict:
    """Original Enemy.dat + ACT evidence, without invented combat stats or live assets.

    This is separate from compile_population(), which requires published Atlas
    manifests but does not supply unverified combat stats. Source evidence must never
    publish either those defaults or placeholder atlas files as verified facts.
    """
    server_dat = Path(server_dat).resolve()
    source = Path(source).resolve()
    enemy_dat = server_map_directory(server_dat, map_id) / 'Enemy.dat'
    catalog_path = server_dat / 'Enemy' / 'Enemy_Data.csv'
    spawns = read_enemy_dat(enemy_dat)
    catalog = read_enemy_catalog(catalog_path)
    enemy_ids = sorted({row['enemy_id'] for row in spawns})
    definitions = []
    for enemy_id in enemy_ids:
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
            'source_combat_evidence': {
                'level': entry.get('level'),
                'source_columns': {'level': 3},
            },
            'combat_stats_verified': False,
        })
    sources = {
        p.relative_to(server_dat).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in (enemy_dat, catalog_path)
    }
    if enemy_ids:
        combat_by_enemy, audit = read_old_enemy_combat_catalog(
            catalog_path, required_enemy_ids=set(enemy_ids))
        if sources['Enemy/Enemy_Data.csv'] != audit['enemy_data_sha256']:
            raise ValueError('Enemy_Data.csv changed during combat evidence compilation')
        for table in audit['attribute_tables']:
            sources[f"Enemy/EN_Att{table['table']:02d}.csv"] = table['sha256']
        excluded = {row['enemy_id']: row['reason']
                    for row in audit['excluded_no_attributes']}
        for definition in definitions:
            enemy_id = definition['enemy_id']
            evidence = definition['source_combat_evidence']
            if enemy_id in combat_by_enemy:
                row = combat_by_enemy[enemy_id]
                columns = {
                    'max_hp': 1, 'attack_power': 2, 'defense_power': 3,
                    'magic_attack_power': 4, 'magic_defense_power': 5,
                    'hit_rate': 6, 'dodge_rate': 7, 'r_exp': 8,
                }
                evidence.update(
                    attribute_table=row['attribute_table'],
                    attribute_source=f"Enemy/EN_Att{row['attribute_table']:02d}.csv")
                evidence['source_columns'] = {'level': 3}
                for field, column in columns.items():
                    if field in row:
                        evidence[field] = row[field]
                        evidence['source_columns'][field] = column
                if row.get('ambiguous_fields'):
                    evidence['ambiguous_fields'] = row['ambiguous_fields']
                if row.get('definition_behavior'):
                    behavior = row['definition_behavior']
                    definition['source_behavior_evidence'] = {
                        **behavior,
                        'source': 'Enemy/Enemy_Data.csv',
                        'source_columns': {
                            field: column for field, column in
                            (('attack_length', 10), ('reborn_time_ms', 19))
                            if field in behavior
                        },
                    }
            else:
                evidence['level_key'] = evidence.pop('level')
                evidence['source_columns'] = {'level_key': 3}
                evidence['combat_attributes_unavailable'] = excluded[enemy_id]
    return {
        'version': 1, 'kind': 'original_spawn_evidence_not_runtime',
        'map_id': map_id,
        'definitions': definitions,
        'spawns': [{'source_index': row['source_index'], 'definition': row['enemy_id'],
                    'p': [row['x'], row['y']], 'source_flags': row['flags']}
                   for row in spawns],
        'sources': sources,
        'stats_verified': False, 'runtime_implemented': False,
    }


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
        definitions.append({
            'id':enemy_id,'sprite':sprite,'atlas':relative,
            'source_combat_evidence': {
                'level': definition.get('level'),
                'source_columns': {'level': 3},
            },
            'stats':None,'stats_verified':False})
    return {'version':3,'map_id':map_id,'definitions':definitions,
            'kind':'visual_spawn_content','stats_verified':False,'gameplay_ready':False,
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
