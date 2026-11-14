"""Repack existing exact frame geometry to compare orientation without PNG IO.

This planning pass does not replace the final source-SPR rebuild/pixel audit.
"""
import argparse
import json
import time
from pathlib import Path
from experiments.atlas_builder import ANIM,Box,directional_layout,write_json

def geometry(document):
    seen={}
    images=[]
    directions=[]
    for clip,indices in document['clips'].items():
        animation=ANIM.match(clip)
        direction=animation[3] if animation else 'default'
        for index in indices:
            frame=document['frames'][index]
            if frame['page']<0:continue
            key=(frame['page'],tuple(frame['rect']))
            if key not in seen:
                seen[key]=len(images)
                images.append(Box(frame['rect'][2],frame['rect'][3]))
                directions.append(direction)
    return images,directions

def plan(document,shape):
    images,directions=geometry(document)
    separate=document['layout']=='direction'
    candidates=[]
    for side in [1024,2048,4096,8192,16384]:
        try:
            _,sizes,_=directional_layout(images,directions,side,separate,shape)
        except ValueError:continue
        if not separate and len(sizes)>1:continue
        candidates.append({'side':side,'pages':len(sizes),'dimensions':sizes,
                           'rgba_bytes':sum(w*h*4 for w,h in sizes)})
        if side>=4096:break
    if not candidates:raise ValueError(f'no legal {shape} layout')
    return min(candidates,key=lambda row:row['rgba_bytes'])

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets',type=Path,default=Path('client/assets/generated'))
    parser.add_argument('--entity',action='append')
    parser.add_argument('--output',type=Path,default=Path('reports/atlas_shapes.json'))
    args=parser.parse_args()
    selected=set(args.entity or [])
    rows=[];started=time.perf_counter()
    for path in sorted(args.assets.rglob('*.json')):
        key=path.relative_to(args.assets).with_suffix('').as_posix()
        if selected and key not in selected:continue
        doc=json.loads(path.read_text(encoding='utf-8'))
        if not all(k in doc for k in ['frames','clips','page_sizes','direction_regions']):continue
        compact=plan(doc,'compact');horizontal=plan(doc,'landscape');consistent=plan(doc,'consistent')
        old=compact['rgba_bytes']
        rows.append({'entity':key,'legacy_rgba_bytes':old,'input_rgba_bytes':sum(w*h*4 for w,h in doc['page_sizes']),
                     'stored_images':len(geometry(doc)[0]),'compact':compact,'landscape':horizontal,'consistent':consistent,
                     'landscape_overhead':horizontal['rgba_bytes']/old-1 if old else 0})
        if len(rows)%100==0:print(f'planned {len(rows)} groups in {time.perf_counter()-started:.1f}s',flush=True)
    report={'geometry_only':True,'groups':len(rows),'seconds':time.perf_counter()-started,'rows':rows,
            'legacy_rgba_bytes':sum(r['legacy_rgba_bytes'] for r in rows),
            'landscape_rgba_bytes':sum(r['landscape']['rgba_bytes'] for r in rows),
            'consistent_rgba_bytes':sum(r['consistent']['rgba_bytes'] for r in rows)}
    write_json(args.output,report)
    print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2))

if __name__=='__main__':main()
