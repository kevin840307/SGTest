"""Compare offline atlas planning CPU cost; never a Runtime FPS benchmark."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import statistics
import time

from asset_converter.atlas import build_atlas_groups
from asset_converter.source.runtime_spr import decode_spr


def compare(frames,ranges,method):
    costs=[]
    for side in [512,1024,2048,4096]:
        try:
            if method=='paint':
                pages,_=build_atlas_groups.pack_source_blocks(frames,ranges,side)
                costs.append({'pages':len(pages),'rgba_bytes':sum(page.width*page.height*4 for page in pages)})
            else:
                data=build_atlas_groups.measure_source_blocks(frames,ranges,side)
                costs.append({key:data[key] for key in ['pages','rgba_bytes']})
        except ValueError:
            costs.append(None)
    return costs


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=Path('.cache/asset_converter/source'))
    parser.add_argument('--group',action='append')
    parser.add_argument('--repeats',type=int,default=3)
    parser.add_argument('--geometry-batch',type=int,default=100,
                        help='amortize Windows process CPU timer quantization')
    parser.add_argument('--output',type=Path,default=Path('reports/converter_planning_geometry.json'))
    args=parser.parse_args()
    if args.repeats<1 or args.geometry_batch<1:parser.error('repeats/batch must be positive')
    groups={key.as_posix().casefold():paths for key,paths in build_atlas_groups.select_groups(args.source).items()}
    records=[]
    for key in args.group or ['enemy/en010a','player/b/standard/b-body','player/b/standard/b-hair00']:
        frames,ranges,inputs=[],[],{}
        for path in groups[key.casefold()]:
            raw=path.read_bytes()
            inputs[path.relative_to(args.source).as_posix()]=hashlib.sha256(raw).hexdigest()
            decoded=decode_spr(raw)
            ranges.append((len(frames),len(decoded)))
            frames.extend(decoded)
        measured={'paint':[],'geometry':[]}
        expected=None
        for repeat in range(args.repeats):
            # Alternate order to avoid always favoring one method's cache state.
            for method in (['paint','geometry'] if repeat%2==0 else ['geometry','paint']):
                start=time.process_time()
                batch=args.geometry_batch if method=='geometry' else 1
                for _ in range(batch):
                    costs=compare(frames,ranges,method)
                measured[method].append((time.process_time()-start)*1000/batch)
                if expected is None:expected=costs
                if costs!=expected:raise AssertionError(f'geometry changed for {key}')
        records.append({'group':key,'frames':len(frames),'source_sha256':inputs,
                        'same_geometry':True,'candidates':expected,
                        'samples_cpu_ms':measured,
                        'median_cpu_ms':{method:statistics.median(values) for method,values in measured.items()}})
        print(json.dumps({name:value for name,value in records[-1].items() if name in ['group','frames','median_cpu_ms','same_geometry']}),flush=True)
    module=Path(build_atlas_groups.__file__)
    report={'python':platform.python_version(),'os':platform.platform(),'repeats':args.repeats,
            'geometry_batch':args.geometry_batch,
            'module_sha256':hashlib.sha256(module.read_bytes()).hexdigest(),'groups':records,
            'scope':'offline candidate planning CPU process_time; no Runtime FPS/RAM/VRAM inference'}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')


if __name__=='__main__':
    main()
