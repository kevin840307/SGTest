"""Native GPU terrain comparison with an independent original-tile compositor."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
import time

from PIL import Image, ImageChops
from run_asset_store import ProcessMetrics

REPO = Path(__file__).resolve().parents[2]


def expected_view(assets, capture):
    folder = assets/f"maps/{capture['map']}"
    world = json.loads((folder/'world.json').read_text(encoding='utf-8'))
    raw = (folder/'tiles.bin').read_bytes()
    width, height = struct.unpack_from('<II', raw)
    tiles = struct.unpack_from(f'<{width*height}H', raw, 8)
    side = world['tile_side']
    columns = world['atlas_grid'][0]
    if world.get('tile_source_rule') != 'int16_plus_one_wrap':
        raise ValueError('runtime map missing original signed tile +1 contract')
    mapping = {original:index for index,original in enumerate(world['tile_ids'])}
    output = Image.new('RGB', tuple(capture['size']), 'black')
    # Raster pixel centers with nearest filtering; test fractions avoid ambiguous ties.
    ox, oy = (math.floor(value+0.5) for value in capture['camera'])
    with Image.open(folder/'terrain.png') as atlas:
        atlas = atlas.convert('RGB')
        for y in range(max(0,oy//side), min(height,math.ceil((oy+output.height)/side))):
            for x in range(max(0,ox//side), min(width,math.ceil((ox+output.width)/side))):
                original = tiles[y*width+x]
                index = mapping[original]
                sx, sy = index%columns*side, index//columns*side
                output.paste(atlas.crop((sx,sy,sx+side,sy+side)), (x*side-ox,y*side-oy))
    return output


def check_capture(assets, capture):
    expected = expected_view(assets, capture)
    with Image.open(capture['file']) as actual:
        difference = ImageChops.difference(expected, actual.convert('RGB'))
        channels = difference.split()
        mask = ImageChops.lighter(ImageChops.lighter(channels[0],channels[1]),channels[2]).point(lambda value:255 if value else 0)
        mismatches = mask.histogram()[255]
    if mismatches:
        expected.save(Path(capture['file']).with_suffix('.expected.png'))
        difference.save(Path(capture['file']).with_suffix('.diff.png'))
    return {'file':capture['file'],'mismatching_pixels':mismatches,
            'expected_rgb_sha256':hashlib.sha256(expected.tobytes()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot',type=Path)
    parser.add_argument('--pack',type=Path,default=REPO/'.cache/client-release/assets/assets.pck')
    parser.add_argument('--assets',type=Path,default=REPO/'client/assets')
    parser.add_argument('--rounds',type=int,default=3)
    parser.add_argument('--mode',choices=['draw','chunks','visible_chunks'],action='append')
    parser.add_argument('--report',type=Path,default=REPO/'reports/terrain_comparison.json')
    args = parser.parse_args()
    if not 1 <= args.rounds <= 5:
        parser.error('rounds must be 1..5')
    godot = args.godot.resolve()
    if os.name == 'nt' and godot.name.endswith('_console.exe'):
        godot = godot.with_name(godot.name.replace('_console.exe','.exe'))
    args.report.parent.mkdir(parents=True,exist_ok=True)
    sources = [REPO/'client'/name for name in ['project.godot','assets_runtime/asset_store.gd',
               'rendering/terrain_data.gd','rendering/terrain.gd','tests/terrain_visible_chunks_candidate.gd','tests/terrain_chunks_candidate.gd','tests/test_terrain.gd']]
    source_hashes = {str(path.relative_to(REPO)):hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
    runs = []
    for round_id in range(args.rounds):
        modes = args.mode or ['draw','chunks','visible_chunks']
        if round_id%2:
            modes = list(reversed(modes))
        for mode in modes:
            folder = args.report.parent/f'{args.report.stem}_r{round_id+1}_{mode}'
            folder.mkdir(exist_ok=True)
            result_file = folder/'result.json'
            result_file.unlink(missing_ok=True)
            command = [str(godot),'--path',str(REPO/'client'),'--rendering-method','gl_compatibility',
                       '--rendering-driver','opengl3','--disable-vsync','--position','24,24',
                       '--script','tests/test_terrain.gd','--',str(args.pack.resolve()),mode,str(result_file.resolve())]
            metrics = ProcessMetrics()
            started = time.perf_counter()
            with (folder/'engine.log').open('w',encoding='utf-8') as log:
                process = subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT)
                try:
                    while True:
                        metrics.sample(process)
                        if time.perf_counter()-started > 180:
                            process.kill()
                            process.wait()
                            break
                        try:
                            process.wait(timeout=0.05)
                            break
                        except subprocess.TimeoutExpired:
                            pass
                    metrics.sample(process)
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.wait()
            engine_wall = time.perf_counter()-started
            log = (folder/'engine.log').read_text(encoding='utf-8',errors='replace')
            errors = [line for line in log.splitlines() if 'ERROR:' in line or 'leaked at exit' in line]
            result = json.loads(result_file.read_text(encoding='utf-8')) if result_file.is_file() else {}
            pixels = [check_capture(args.assets,entry) for entry in result.get('captures',[])]
            unchanged = source_hashes == {str(path.relative_to(REPO)):hashlib.sha256(path.read_bytes()).hexdigest() for path in sources}
            passed = process.returncode == 0 and not errors and result.get('status')=='PASS' and 'TERRAIN_RESULT ' in log and unchanged and len(pixels)==16 and all(entry['mismatching_pixels']==0 for entry in pixels)
            record = {'round':round_id+1,'mode':mode,'status':'PASS' if passed else 'FAIL',
                      'exit_code':process.returncode,'engine_errors':errors,'source_unchanged':unchanged,
                      'wall_seconds':round(engine_wall,3),'cpu_seconds':metrics.cpu_seconds,
                      'peak_process_rss_bytes':metrics.peak_rss or None,'pixels':pixels,'result':result,'command':command}
            runs.append(record)
            print(json.dumps({key:record[key] for key in ['round','mode','status','exit_code','engine_errors','wall_seconds','cpu_seconds','peak_process_rss_bytes']}),flush=True)
            print('PIXEL_MISMATCHES', [entry['mismatching_pixels'] for entry in pixels],flush=True)
    report = {'status':'PASS' if all(row['status']=='PASS' for row in runs) else 'FAIL',
              'pck_sha256':hashlib.sha256(args.pack.read_bytes()).hexdigest(),'source_sha256':source_hashes,'runs':runs,
              'scope':'Native OpenGL terrain only; not game actors/AOI/World FPS'}
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return 0 if report['status']=='PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
