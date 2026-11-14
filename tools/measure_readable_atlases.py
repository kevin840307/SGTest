"""Plan readable source-frame geometry without decoding or changing PNGs."""
import argparse
import json
import time
from pathlib import Path
from experiments.atlas_builder import ANIM,Box,packed_layout,write_json

def source_geometry(document):
    images=[];directions=[];clips={}
    for clip,indices in document['clips'].items():
        match=ANIM.match(clip);direction=match[3] if match else 'default'
        slots=[]
        for index in indices:
            frame=document['frames'][index]
            if frame['page']<0:slots.append(None);continue
            slots.append(len(images));images.append(Box(frame['rect'][2],frame['rect'][3]));directions.append(direction)
        clips[clip]=slots
    return images,directions,clips

def plan(document,shape):
    images,directions,clips=source_geometry(document)
    separate=sum(image.width*image.height*4 for image in images)>1536*1024
    rows=[]
    for side in [1024,2048,4096,8192,16384]:
        try:
            _,sizes,_,_=packed_layout(images,directions,clips,side,separate,shape)
        except ValueError:continue
        if not separate and len(sizes)>1:continue
        rows.append({'rgba_bytes':sum(w*h*4 for w,h in sizes),'pages':len(sizes),'side':side,'dimensions':sizes})
        if side>=4096:break
    if not rows:raise ValueError('cannot lay out '+document['entity'])
    return min(rows,key=lambda row:row['rgba_bytes'])

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--assets',type=Path,default=Path('client/assets'))
    parser.add_argument('--entity',action='append')
    parser.add_argument('--output',type=Path,default=Path('reports/readable_atlas_plan.json'))
    args=parser.parse_args();rows=[];started=time.perf_counter();selected=set(args.entity or [])
    for path in sorted(args.assets.rglob('*.json')):
        key=path.relative_to(args.assets).with_suffix('').as_posix()
        if selected and key not in selected:continue
        doc=json.loads(path.read_text(encoding='utf-8'))
        if not all(field in doc for field in ['frames','clips','page_sizes','direction_regions']):continue
        readable=plan(doc,'readable')
        rows.append({'entity':key,'before_rgba_bytes':sum(w*h*4 for w,h in doc['page_sizes']),'readable':readable})
        if len(rows)%100==0:print(f'readable planning {len(rows)} groups {time.perf_counter()-started:.1f}s',flush=True)
    report={'geometry_only':True,'groups':len(rows),'seconds':time.perf_counter()-started,'rows':rows,
            'before_rgba_bytes':sum(r['before_rgba_bytes'] for r in rows),
            'readable_rgba_bytes':sum(r['readable']['rgba_bytes'] for r in rows)}
    write_json(args.output,report);print(json.dumps({k:v for k,v in report.items() if k!='rows'},indent=2))

if __name__=='__main__':main()
