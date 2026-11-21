"""Native scenery comparison; independent layout, original SPR and raster checks."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import time
from PIL import Image, ImageChops
from run_asset_store import ProcessMetrics
from run_terrain import expected_view

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(REPO/'tools'))
from asset_converter.source.scenery import swap_required
from asset_converter.source.runtime_spr import decode_spr


def intersects(a,b):
    return a[0] < b[0]+b[2] and a[0]+a[2] > b[0] and a[1] < b[1]+b[3] and a[1]+a[3] > b[1]


def visible_roots(world,view):
    placements = world['objects']
    visible = set()
    for index,row in enumerate(placements):
        if row['parent_index'] is not None:
            continue
        boxes = [p['bounds'] for p in placements[index:index+1+row['child_count']]]
        left,top = min(b[0] for b in boxes),min(b[1] for b in boxes)
        right,bottom = max(b[0]+b[2] for b in boxes),max(b[1]+b[3] for b in boxes)
        if intersects([left,top,right-left,bottom-top],view):
            visible.add(index)
    return visible


def expected_focus(world,capture):
    main = world['scenery_order']['groups'][1]
    buildings = {}
    for i in main:
        row = world['objects'][i]
        definition = world['scenery_definitions'][row['definition']]
        if definition['sorting_mode']==0 and definition['inside_cells'] and definition['outside_cells']:
            buildings[i] = (row['p'],definition['front'])
    states = {i:{'inside':False,'counter':0} for i in buildings}
    selected,last = -1,-1
    for step in capture['focus_inputs']:
        if step['update'] != last+1:
            raise ValueError('focus input trace has a gap')
        visible = visible_roots(world,[*step['camera'],*capture['size']])
        candidates = [i for i in main if i in visible]
        chosen = -1
        for i in candidates:
            if i not in buildings:
                continue
            (px,py),(lx,ly,rx,ry) = buildings[i]
            x,y = step['focus'][0]-px,step['focus'][1]-py
            # Independent isometric rectangle formulation, rather than the
            # four branch expressions in the production GDScript.
            u,v = x+2*y,x-2*y
            inside = abs(x)+abs(y)<=1000 and lx+2*ly<u<=rx+2*ry and lx-2*ly<=v<rx-2*ry
            states[i]['inside'] = inside
            if inside:
                chosen = i
        if chosen != selected:
            if chosen >= 0:
                states[chosen]['counter'] = 256
            elif selected >= 0:
                states[selected] = {'inside':False,'counter':256}
        selected = chosen
        for i in candidates:
            if i in states:
                states[i]['counter'] = max(0,states[i]['counter']-16)
        last = step['update']
    return {'active':True,'epoch':1 if capture['map']==15 else 2,'update':last,'selected':selected,
            'transitions':{str(i):{'inside':s['inside'],'updates':(256-s['counter'])//16} for i,s in states.items()}}


def expected_players(assets, states):
    # Independent reconstruction from normalized Converter metadata, not runtime
    # command data: selects logical facing before resolving physical source art.
    profiles = {12:('players/b/base/leg','B-LEG'),7:('players/b/base/right_arm','B-R-ARM'),
                9:('players/b/base/body','B-BODY'),5:('players/b/base/left_arm','B-L-ARM'),
                0:('players/b/base/head','B-HEAD'),1:('players/b/hair/hair00','B-HAIR00')}
    manifests={slot:json.loads((assets/p[0]/'atlas.json').read_text(encoding='utf-8')) for slot,p in profiles.items()}
    output=[]
    for state in sorted(states,key=lambda x:x['id']):
        action='NR' if state['moving'] else 'NS'
        requested=action+str(state['facing'])
        order=manifests[9]['logical_views']['B-BODY_'+requested]['draw_slots']
        parts=[]
        for slot in order:
            if slot not in profiles:continue
            asset,prefix=profiles[slot]
            view=manifests[slot]['logical_views'][prefix+'_'+requested]
            total=sum(row[1] for row in view['timeline_ticks'])
            tick=max(0,state['elapsed_updates'])
            tick=tick%total if view['loop'] else min(tick,total-1)
            for index,duration in view['timeline_ticks']:
                if tick<duration:break
                tick-=duration
            parts.append({'asset':asset,'clip':view['clip'],'frame':index,'flip_h':view['flip_h'],'part_slot':slot})
        output.append({'id':str(state['id']),'p':state['p'],'parts':parts})
    return output


def expected_commands(assets, capture):
    world = json.loads((assets/f"maps/{capture['map']}/world.json").read_text(encoding='utf-8'))
    placements, definitions = world['objects'],world['scenery_definitions']
    view = [*capture['camera'],*capture['size']]
    visible = visible_roots(world,view)
    focus = None
    if capture.get('focus_mode'):
        focus = expected_focus(world,capture)
        if focus != capture['focus_state']:
            raise ValueError('focus state differs from projected-coordinate replay')
    groups = [[{'index':i,'p':placements[i]['p'],**definitions[placements[i]['definition']]}
               for i in group if i in visible] for group in world['scenery_order']['groups']]
    overlay = []
    if focus is not None and focus['selected'] >= 0:
        selected = focus['selected']
        overlay = [{'index':i,'p':placements[i]['p'],**definitions[placements[i]['definition']]}
                   for i in range(selected+1,selected+1+placements[selected]['child_count']) if intersects(placements[i]['bounds'],view)]
    actors = capture['actors']
    if 'player_states' in capture:
        expected_actors = expected_players(assets,capture['player_states'])
        if expected_actors != actors:
            raise ValueError('player logical view/timing/order differs from source metadata')
    for actor in actors:
        record = {**actor,'index':-1,'sorting_mode':1}
        inside = False
        if focus is not None and focus['selected'] >= 0:
            building = placements[focus['selected']]
            lx,ly,rx,ry = definitions[building['definition']]['front']
            x,y = actor['p'][0]-building['p'][0],actor['p'][1]-building['p'][1]
            u,v = x+2*y,x-2*y
            inside = abs(x)+abs(y)<=1000 and lx+2*ly<u<=rx+2*ry and lx-2*ly<=v<rx-2*ry
        target = overlay if inside else groups[1]
        at = next((i for i,row in enumerate(target) if swap_required(row,record)),len(target))
        target.insert(at,record)
    output = []

    def append(asset_id,clip,frame,p,index,slot,actor_id,alpha=255,flip=False):
        if alpha <= 0:
            return
        manifest = json.loads((assets/asset_id/'atlas.json').read_text(encoding='utf-8'))
        row = manifest['frames'][manifest['clips'][clip][frame]]
        if row['page'] == -1:
            return
        destination = [p[0]+row['trim'][0]-row['origin'][0],p[1]+row['trim'][1]-row['origin'][1],*row['rect'][2:]]
        if flip:
            destination[0] = 2*p[0]-destination[0]
            destination[2] = -destination[2]
        bounds = [destination[0]+min(0,destination[2]),destination[1],abs(destination[2]),destination[3]]
        if intersects(bounds,view):
            output.append({'index':index,'slot':slot,'frame':frame,'actor_id':actor_id,'destination':destination,'alpha':alpha,
                           'uv':row['rect'],'texture_source':[asset_id,row['page']], 'source':row,'flip':flip})

    def placement(index,slot,alpha):
        row = placements[index]
        appearances = definitions[row['definition']]['appearances']
        a = appearances[slot]
        asset_id = a['atlas'].removesuffix('/atlas.json')
        frame = 0
        if (capture.get('timing') or capture.get('focus_mode')) and definitions[row['definition']]['sorting_mode'] == 1:
            manifest = json.loads((assets/asset_id/'atlas.json').read_text(encoding='utf-8'))
            timeline = manifest.get('act',{}).get(a['clip'])
            if timeline:
                durations = [max(1,step['raw_time']*40//25) for step in timeline['frames']]
                tick = max(0,capture['elapsed_updates'])
                tick = tick % sum(durations) if timeline['loops'] else min(tick,sum(durations)-1)
                for step,duration in zip(timeline['frames'],durations):
                    if tick < duration:
                        frame = step['index']
                        break
                    tick -= duration
        elif not capture.get('timing') and not capture.get('focus_mode') and capture['inside'] and row['frames'] > 1:
            frame = 1
        append(asset_id,a['clip'],frame,row['p'],index,slot,'',alpha)

    def append_children(index):
        for child in range(index+1,index+1+placements[index]['child_count']):
            definition = definitions[placements[child]['definition']]
            appearances = definition['appearances']
            dual = len(appearances)>1 and appearances[1] is not None
            if definition['sorting_mode'] != 0 or dual:
                placement(child,1 if dual else 0,255)

    for group in groups:
        for row in group:
            if row['index'] == -1:
                for part in row.get('parts',[row]):
                    append(part['asset'],part['clip'],part['frame'],row['p'],-1,part.get('part_slot',0),row['id'],flip=part.get('flip_h',False))
            else:
                index = row['index']
                item = placements[index]
                definition = definitions[item['definition']]
                appearances = definition['appearances']
                dual = len(appearances)>1 and appearances[1] is not None
                inside = capture['inside'] and item['child_count']>0
                primary,alternate,children = (255,0,True) if inside else (0,255,False)
                if focus is not None:
                    transition = focus['transitions'].get(str(index),{'inside':False,'updates':16})
                    remaining = 256-16*min(16,max(0,transition['updates']))
                    inside = transition['inside']
                    primary = 255 if inside else min(255,remaining)
                    alternate = min(255,remaining if inside else 256-remaining)
                    children = inside or remaining>0
                if capture.get('timing') and dual and appearances[0]['source'] != appearances[1]['source']:
                    remaining = 256-16*min(16,max(0,capture['fade_updates']))
                    inside = capture['inside']
                    primary = 255 if inside else min(255,remaining)
                    alternate = min(255,remaining if inside else 256-remaining)
                    children = inside or remaining>0
                placement(index,0,primary if definition['sorting_mode']==0 else 255)
                if children:
                    # The boolean above intentionally remains separate from the
                    # selected-room overlay after the alternate appearance.
                    append_children(index)
                if dual:
                    placement(index,1,alternate)
                if focus is not None and focus['selected']==index:
                    for entry in overlay:
                        if entry['index'] == -1:
                            append(entry['asset'],entry['clip'],entry['frame'],entry['p'],-1,0,entry['id'])
                        else:
                            child_definition = definitions[placements[entry['index']]['definition']]
                            child_appearances = child_definition['appearances']
                            child_dual = len(child_appearances)>1 and child_appearances[1] is not None
                            if child_definition['sorting_mode'] != 0 or child_dual:
                                placement(entry['index'],1 if child_dual else 0,255)
    return output


def validate_capture(assets, source, capture, originals):
    expected = expected_commands(assets,capture)
    actual_commands = capture['commands']
    if len(expected) != len(actual_commands):
        raise ValueError(f"command count differs: {len(expected)} != {len(actual_commands)}")
    output = expected_view(assets,capture).convert('RGBA')
    lower,upper = output.copy(),output.copy()
    for command,actual in zip(expected,actual_commands):
        for key in ('index','slot','frame','actor_id','destination','uv','texture_source','alpha'):
            if command[key] != actual[key]:
                raise ValueError(f"wrong {key}: {command[key]} != {actual[key]}")
        frame = command['source']
        identity = (frame['source'],frame['source_frame'])
        if identity not in originals:
            decoded = decode_spr((source/frame['source']).read_bytes())[frame['source_frame']]
            if [decoded.origin_x,decoded.origin_y] != frame['origin']:
                raise ValueError('original SPR origin differs from manifest')
            w,h = frame['rect'][2:]
            x,y = frame['trim']
            original = decoded.image.crop((x,y,x+w,y+h))
            folder = assets/command['texture_source'][0]
            manifest = json.loads((folder/'atlas.json').read_text(encoding='utf-8'))
            with Image.open(folder/manifest['pages'][frame['page']]) as atlas:
                ax,ay,aw,ah = frame['rect']
                packed = atlas.convert('RGBA').crop((ax,ay,ax+aw,ay+ah))
                if packed.tobytes() != original.tobytes():
                    raise ValueError('packed crop differs from original SPR')
            originals.add(identity)
        with Image.open(actual['page']) as page:
            x,y,w,h = command['uv']
            # Godot compressed image storage may be block-aligned/resized while
            # Texture2D retains logical dimensions. Reproduce normalized nearest
            # sampling instead of assuming get_image() is the logical atlas.
            tw,th = actual['texture_size']
            pw,ph = page.size
            page = page.convert('RGBA')
            sx,sy = pw/tw,ph/th
            # Exact texel-boundary ties can land on either adjacent texel after
            # GPU float interpolation. Only 1e-5 texel uncertainty is admitted;
            # it is not a geometric pixel shift or a broad image tolerance.
            sprites = [page.transform((int(w),int(h)),Image.Transform.AFFINE,
                       (sx,0,x*sx+ex,0,sy,y*sy+ey),Image.Resampling.NEAREST)
                       for ex,ey in ((-1e-5,-1e-5),(-1e-5,1e-5),(1e-5,-1e-5),(1e-5,1e-5))]
            if command['alpha'] < 255:
                variants = []
                for sprite in sprites:
                    for rounding in (math.floor,math.ceil):
                        variant = sprite.copy()
                        variant.putalpha(sprite.getchannel('A').point(lambda a:rounding(a*command['alpha']/255)))
                        variants.append(variant)
                sprites = variants
        dx,dy,dw,_ = command['destination']
        if command.get('flip'):
            sprites = [sprite.transpose(Image.Transpose.FLIP_LEFT_RIGHT) for sprite in sprites]
            dx += dw
        dx,dy = int(dx-capture['camera'][0]),int(dy-capture['camera'][1])
        output.alpha_composite(sprites[-1],(dx,dy))
        rect = (max(0,dx),max(0,dy),min(output.width,dx+int(w)),min(output.height,dy+int(h)))
        source_rect = (rect[0]-dx,rect[1]-dy,rect[2]-dx,rect[3]-dy)
        low_base,high_base = lower.crop(rect),upper.crop(rect)
        low,high = None,None
        for sprite in sprites:
            cropped = sprite.crop(source_rect)
            lo = Image.alpha_composite(low_base,cropped)
            hi = Image.alpha_composite(high_base,cropped)
            low = lo if low is None else ImageChops.darker(low,lo)
            high = hi if high is None else ImageChops.lighter(high,hi)
        lower.paste(low,rect[:2])
        upper.paste(high,rect[:2])
    with Image.open(capture['file']) as actual:
        diff = ImageChops.difference(output.convert('RGB'),actual.convert('RGB'))
        outside = ImageChops.lighter(ImageChops.subtract(lower.convert('RGB'),actual.convert('RGB')),
                                    ImageChops.subtract(actual.convert('RGB'),upper.convert('RGB')))
    maximum = max(high for low,high in diff.getextrema())
    channels = outside.split()
    mask = ImageChops.lighter(ImageChops.lighter(channels[0],channels[1]),channels[2])
    bad = sum(mask.histogram()[3:])  # At most two levels of GPU blend/decode rounding.
    if bad:
        output.save(Path(capture['file']).with_suffix('.expected.png'))
        diff.save(Path(capture['file']).with_suffix('.diff.png'))
    return {'file':capture['file'],'commands':len(expected),'max_channel_difference':maximum,
            'max_outside_sampler_envelope':max(high for low,high in outside.getextrema()),'pixels_over_tolerance':bad}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot',type=Path)
    parser.add_argument('--rounds',type=int,default=1)
    parser.add_argument('--mode',choices=['canvas','sprites','all'],default='all')
    parser.add_argument('--timing',action='store_true',help='ACT default clock and intermediate building alpha snapshots')
    parser.add_argument('--focus',action='store_true',help='Fixed-update building focus state with epoch and replay checks')
    parser.add_argument('--pack',type=Path,default=REPO/'.cache/client-release/assets/assets.pck')
    parser.add_argument('--source',type=Path,default=REPO/'.cache/asset_converter/source')
    parser.add_argument('--report',type=Path,default=REPO/'reports/world_render.json')
    args = parser.parse_args()
    if args.focus and args.timing:
        parser.error('choose --focus or --timing')
    if not 1 <= args.rounds <= 5:
        parser.error('rounds must be 1..5')
    godot = args.godot.resolve()
    engine = godot.with_name(godot.name.replace('_console.exe','.exe'))
    if engine.is_file():
        godot = engine
    runs, originals = [],set()
    modes = ['canvas','sprites'] if args.mode == 'all' else [args.mode]
    files = [*sorted((p for folder in ('world','map','animation','movement','player','monster','pvp') for p in (REPO/'client'/folder).glob('*.gd'))),REPO/'client/tests/test_world.gd',
             REPO/'client/tests/world_sprites_candidate.gd',Path(__file__).resolve(),
             REPO/'client/tests/building_visibility_fixture.gd',
             REPO/'tools/asset_converter/source/scenery.py',REPO/'tools/asset_converter/content/build_maps.py']
    hashes = {str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    for round_number in range(1,args.rounds+1):
        for mode in modes:
            folder = args.report.with_suffix('').with_name(args.report.stem+f'_r{round_number}_{mode}')
            folder.mkdir(parents=True,exist_ok=True)
            log = folder/'engine.log'
            metrics = ProcessMetrics()
            begin = time.monotonic()
            with log.open('w',encoding='utf-8') as stream:
                command = [str(godot),'--path',str(REPO/'client'),'--rendering-method','gl_compatibility','--disable-vsync','--script','res://tests/test_world.gd','--',str(args.pack.resolve()),mode,str(folder.resolve())]
                if args.timing:
                    command.append('timing')
                elif args.focus:
                    command.append('focus')
                process = subprocess.Popen(command,cwd=REPO,stdout=stream,stderr=subprocess.STDOUT)
                while process.poll() is None and time.monotonic()-begin < 180:
                    metrics.sample(process)
                    time.sleep(0.05)
                if process.poll() is None:
                    process.kill()
                    process.wait()
            raw = log.read_text(encoding='utf-8',errors='replace')
            errors = [line for line in raw.splitlines() if any(tag in line for tag in ('ERROR:','SCRIPT ERROR:','leaked','still in use'))]
            result_path = folder/'result.json'
            result = json.loads(result_path.read_text(encoding='utf-8')) if result_path.is_file() else {}
            checks,validation_errors = [],[]
            if process.returncode == 0 and not errors:
                for capture in result.get('captures',[]):
                    try:
                        checks.append(validate_capture(REPO/'client/assets',args.source,capture,originals))
                    except Exception as error:
                        validation_errors.append(str(error))
            passed = process.returncode == 0 and not errors and not result.get('failures') and len(checks)==(84 if args.timing or args.focus else 24) and not validation_errors and all(c['pixels_over_tolerance']==0 for c in checks)
            run = {'round':round_number,'mode':mode,'status':'PASS' if passed else 'FAIL','exit_code':process.returncode,
                   'engine_errors':errors,'validation_errors':validation_errors,'checks':checks,'result':result,
                   'wall_seconds':round(time.monotonic()-begin,3),'cpu_seconds':metrics.cpu_seconds,'peak_rss_bytes':metrics.peak_rss}
            runs.append(run)
            print(json.dumps({k:v for k,v in run.items() if k not in ('result','checks') }),flush=True)
    unchanged = hashes == {str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
    report = {'status':'PASS' if unchanged and all(r['status']=='PASS' for r in runs) else 'FAIL','runs':runs,
              'sources_unchanged':unchanged,
              'original_spr_frames_checked':len(originals),'pck_sha256':hashlib.sha256(args.pack.read_bytes()).hexdigest(),'source_sha256':hashes}
    args.report.write_text(json.dumps(report,indent=2),encoding='utf-8')
    return 0 if report['status']=='PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
