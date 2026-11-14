"""Rebuild real SPR samples and measure shape, pixels and native Godot upload."""
import argparse
import json
import statistics
import subprocess
import time
from pathlib import Path
from PIL import Image
from experiments.atlas_builder import convert_group,group_key,write_json
from runtime_spr import decode_spr

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path)
    parser.add_argument('--godot',type=Path)
    parser.add_argument('--entity',action='append')
    parser.add_argument('--shape',action='append',choices=['compact','consistent','readable','grid'])
    parser.add_argument('--output',type=Path,default=Path('reports/atlas_shape_benchmark.json'))
    args=parser.parse_args()
    root=args.source.resolve()
    keys=args.entity or ['enemy/en010a','enemy/en050a','enemy/en064a','player/b/standard/b-body',
                         'interface/ground','interface/icon','interface/opening/story01_text',
                         'map/object/big_wall/m004_develop_d']
    selected={key:[] for key in keys}
    for path in sorted(root.rglob('*.spr')):
        key=group_key(path.relative_to(root))
        if key in selected:selected[key].append(path)
    rows=[]
    for key in keys:
        if not selected[key]:raise ValueError(f'unknown entity {key}')
        for shape in args.shape or ['compact','landscape','consistent']:
            output=Path('reports/atlas_shapes_benchmark')/shape
            doc,_=convert_group(key,selected[key],root,output,[1024,2048,4096],True,'auto',shape)
            files=[(output/key).parent/name for name in doc['pages']]
            pages=[Image.open(path).convert('RGBA') for path in files]
            checked=0
            for source in selected[key]:
                frames=decode_spr(source.read_bytes())
                indices=doc['clips'][source.stem.upper()]
                if len(indices)!=len(frames):raise ValueError(f'frame count mismatch {source}')
                for original,index in zip(frames,indices):
                    record=doc['frames'][index]
                    rebuilt=Image.new('RGBA',original.image.size)
                    if record['page']>=0:
                        x,y,w,h=record['rect']
                        rebuilt.paste(pages[record['page']].crop((x,y,x+w,y+h)),tuple(record['trim']))
                    expected=Image.new('RGBA',original.image.size)
                    bounds=original.image.getbbox()
                    if bounds:expected.paste(original.image.crop(bounds),bounds[:2])
                    if rebuilt.tobytes()!=expected.tobytes():raise ValueError(f'pixel mismatch {source}:{index}')
                    checked+=1
            for page in pages:page.close()
            durations=[]
            for _ in range(15):
                start=time.perf_counter()
                for path in files:
                    with Image.open(path) as image:image.load()
                durations.append((time.perf_counter()-start)*1000)
            row={'entity':key,'shape':shape,'pages':len(files),'dimensions':doc['page_sizes'],
                 'rgba_bytes':sum(w*h*4 for w,h in doc['page_sizes']),'png_bytes':sum(p.stat().st_size for p in files),
                 'pixel_frames_checked':checked,'decode_ms_p50':statistics.median(durations)}
            if args.godot:
                native=subprocess.run([str(args.godot.resolve()),'--path','client','--rendering-method','gl_compatibility',
                                       '--script','tools/benchmark_atlas.gd','--',str((output/(key+'.json')).resolve())],
                                      capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=30)
                if native.returncode or 'ERROR:' in native.stdout+native.stderr:raise RuntimeError(native.stdout+native.stderr)
                row['native']=json.loads(next(line for line in native.stdout.splitlines() if line.startswith('{')))
            rows.append(row)
            print(f'{key} {shape}: {row["rgba_bytes"]} bytes, {checked} frames verified',flush=True)
    write_json(args.output,{'cache':'warm OS cache','rows':rows})

if __name__=='__main__':main()
