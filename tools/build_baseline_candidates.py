"""Build same-map legacy layout candidates for measurements, not selection."""
import argparse
import hashlib
import json
from pathlib import Path
from experiments.atlas_builder import convert_group,group_key,write_json

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=Path('.cache/client'))
    parser.add_argument('--output',type=Path,default=Path('reports/atlas_candidates'))
    args=parser.parse_args()
    population=json.loads(Path('client/assets/maps/13/population.json').read_text(encoding='utf-8'))
    keys={Path(d['atlas']).with_suffix('').as_posix() for d in population['definitions']}
    sources={key:[] for key in keys}
    root=args.source.resolve()
    for path in sorted(root.rglob('*.spr')):
        key=group_key(path.relative_to(root))
        if key in sources:sources[key].append(path)
    policies=[('entity','entity',[512,1024,2048,4096],'consistent'),
              *[(f'direction_{side}','direction',[side],'consistent') for side in [512,1024,2048,4096]],
              ('readable_auto','auto',[512,1024,2048,4096],'readable')]
    records=[]
    for label,mode,sides,shape in policies:
        rows=[]
        for key in sorted(keys):
            doc,_=convert_group(key,sources[key],root,args.output/label,sides,False,mode,shape)
            rows.append({'entity':key,'atlas_side':doc['atlas_side'],'pages':len(doc['pages']),
                         'page_sizes':doc['page_sizes'],
                         'rgba_bytes':sum(w*h*4 for w,h in doc['page_sizes']),'source_fingerprint':doc['sha256']})
        report={'candidate':label,'mode':mode,'requested_sides':sides,'shape':shape,'groups':rows,
                'pages':sum(row['pages'] for row in rows),'rgba_bytes':sum(row['rgba_bytes'] for row in rows),
                'population_sha256':hashlib.sha256(Path('client/assets/maps/13/population.json').read_bytes()).hexdigest(),
                'note':'preferred size with measured fallback; not a final mixed/atomic/map-aware builder'}
        write_json(args.output/label/'candidate.json',report)
        records.append(report)
        print(f'{label}: {report["pages"]} pages, {report["rgba_bytes"]} RGBA bytes',flush=True)
    write_json(Path('reports/baseline_candidates.json'),{'candidates':records})

if __name__=='__main__':main()
