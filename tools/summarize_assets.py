"""Produce inventory evidence from actual converter outputs, never hand counts."""
import argparse
import hashlib
import json
from pathlib import Path
from build_assets import write_json

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=Path('.cache/client'))
    parser.add_argument('--assets',type=Path,default=Path('client/assets/generated'))
    args=parser.parse_args()
    catalog=json.loads((args.assets/'catalog.json').read_text(encoding='utf-8'))
    support=json.loads((args.assets/'support_catalog.json').read_text(encoding='utf-8'))
    rows=catalog['groups']
    orientations={}
    policies={}
    for row in rows:
        document=json.loads((args.assets/(row['entity']+'.json')).read_text(encoding='utf-8'))
        orientation=document.get('orientation','legacy')
        orientations[orientation]=orientations.get(orientation,0)+1
        policy=document.get('shape','legacy')
        policies[policy]=policies.get(policy,0)+1
    report={'source_sprites':sum(row['sources'] for row in rows),'entities_or_parts':len(rows),
            'frames':sum(row['frames'] for row in rows),'textures':sum(row['pages'] for row in rows),
            'direction_aliases':sum(row['aliases'] for row in rows),'support_assets':len(support['files']),
            'rgba_bytes':sum(row['rgba_bytes'] for row in rows),'manifest_version':catalog['version'],
            'catalog_sha256':hashlib.sha256((args.assets/'catalog.json').read_bytes()).hexdigest(),
            'orientations':orientations,'shape_policies':policies,
            'errors':catalog['errors']+support['errors'],
            'archive':json.loads((args.source/'extraction.json').read_text(encoding='utf-8'))}
    write_json(Path('reports/asset_inventory.json'),report)
    print(json.dumps({k:v for k,v in report.items() if k!='archive'},indent=2))
    return bool(report['errors'])

if __name__=='__main__':raise SystemExit(main())
