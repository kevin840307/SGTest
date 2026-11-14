"""Rebuild current-map monster/core-character definitions and report differences.

The old runtime atlas is a comparison input, never the source of truth.
Private normalized frame metadata stays under reports/normalized.
"""
import argparse
import json
from pathlib import Path

from animation_definition import compile_definition, validate_definition


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path('.cache/client'))
    parser.add_argument('--runtime-assets', type=Path, default=Path('client/assets/generated'))
    parser.add_argument('--output', type=Path, default=Path('reports/normalized'))
    parser.add_argument('--report', type=Path, default=Path('reports/normalized_definition_audit.json'))
    args = parser.parse_args()
    source = args.source.resolve()
    if args.output.resolve().is_relative_to(source) or args.report.resolve().is_relative_to(source):
        parser.error('output must be outside read-only source tree')
    population = json.loads((args.runtime_assets/'maps/13/population.json').read_text(encoding='utf-8'))
    recipes = []
    for definition in population['definitions']:
        key = Path(definition['atlas']).with_suffix('').as_posix()
        recipes.append({'entity':key, 'kind':'monster', 'act':f'{key}.act',
                        'sources':list(source.glob(key+'_*.spr'))})
    for part in ['body','leg','head','hair00','l-arm','r-arm']:
        key = f'player/b/standard/b-{part}'
        recipes.append({'entity':key, 'kind':'character_part', 'act':'player/b/boy.act',
                        'sources':list(source.glob(key+'_*.spr')),
                        'character_actions':source/'player/charactionname.dat',
                        'draw_order_path':source/'player/chardraworder.dat'})
    key = 'player/b/standard/b-death'
    recipes.append({'entity':key, 'kind':'single_view', 'act':'player/b/b-death.act',
                    'sources':list(source.glob(key+'_*.spr')), 'act_bindings':{'ND':'Effect'}})
    records, differences, failures = [], [], []
    for recipe in recipes:
        key = recipe['entity']
        try:
            parameters = {key:value for key,value in recipe.items() if key not in ('act','entity')}
            data = compile_definition(key, root=source, act_path=source/recipe['act'], **parameters)
            validate_definition(data)
            target = args.output/(key+'.json')
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
            baseline = json.loads((args.runtime_assets/(key+'.json')).read_text(encoding='utf-8'))
            mismatch = []
            for code, behavior in data['behaviors'].items():
                prefix = next(iter(data['clips'])).rsplit('_',1)[0]
                for facing, view in behavior['facings'].items():
                    requested = f'{prefix}_{code}{facing}'
                    previous = ({'clip':requested,'flip_h':False} if requested in baseline['clips']
                                else baseline['aliases'].get(requested))
                    expected = {'clip':view['clip'],'flip_h':view['flip_h']}
                    if previous != expected:
                        mismatch.append({'behavior':code,'facing':int(facing),'old':previous,'normalized':expected})
            draw_views = [view for behavior in data['behaviors'].values()
                          for view in behavior['facings'].values()
                          if view.get('draw_order_key') is not None]
            records.append({'entity':key, 'schema_version':data['version'],
                            'content_sha256':data['content_sha256'], 'frames':len(data['frames']),
                            'behaviors':len(data['behaviors']), 'logical_facings':len(data['behaviors'])*8,
                            'source_files':len(data['source_sha256']),
                            'draw_order_views':len(draw_views),
                            'normalized_z_order_verified':all(view.get('draw_layers') and view.get('draw_slots')
                                                              for view in draw_views),
                            'unused_act_actions':data['unused_act_actions'],
                            'unreferenced_source_clips':data['unreferenced_source_clips'],
                            'old_atlas_has_no_act_timing':not bool(baseline.get('act')),
                            'direction_differences':len(mismatch)})
            differences.extend({'entity':key,**item} for item in mismatch)
        except (OSError,ValueError,KeyError) as error:
            failures.append({'entity':key,'error':str(error)})
    report = {'scope':'current Map13 nine monsters + Boy six core parts + explicit single-view death',
              'source':'original SPR/ACT; reference facing helpers; HK2 visual parity NOT VERIFIED',
              'schema_version':2,
              'schema_validation_passed':not failures and len(records)==len(recipes),
              'character_draw_order_views':sum(row['draw_order_views'] for row in records),
              'normalized_z_order_verified':all(row['normalized_z_order_verified'] for row in records),
              'entities':records,'failures':failures,'old_runtime_direction_differences':differences,
              'runtime_migration':'NOT IMPLEMENTED; differences are reported, not silently accepted as visual parity',
              'source_pixel_comparison':'fixture PASS; full normalized reconstruction pending mixed builder'}
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'passed':report['schema_validation_passed'], 'entities':len(records),
                      'logical_facings':sum(row['logical_facings'] for row in records),
                      'character_draw_order_views':report['character_draw_order_views'],
                      'normalized_z_order_verified':report['normalized_z_order_verified'],
                      'old_runtime_direction_differences':len(differences),'failures':failures},ensure_ascii=False))
    return 0 if report['schema_validation_passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
