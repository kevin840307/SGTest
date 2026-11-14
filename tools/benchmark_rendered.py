"""Measure selected single/entity and direction layouts in real Godot OpenGL."""
import argparse
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot', type=Path)
    args = parser.parse_args()
    root = Path('reports/atlas_benchmark')
    rows = json.loads((root/'results.json').read_text(encoding='utf-8'))['rows']
    results = []
    for entity in sorted({r['entity'] for r in rows}):
        for mode in ('entity','direction'):
            best = min((r for r in rows if r['entity']==entity and r['layout']==mode),key=lambda r:r['rgba_bytes'])
            manifest = (root/f"{best['requested_side']}_{mode}"/(entity+'.json')).resolve()
            probe = subprocess.run([str(args.godot.resolve()),'--path','.','--rendering-method','gl_compatibility',
                                    '--script','tools/benchmark_atlas.gd','--',str(manifest)],
                                   capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=20)
            if probe.returncode:
                raise RuntimeError(probe.stdout+probe.stderr)
            measured = json.loads(next(line for line in probe.stdout.splitlines() if line.startswith('{')))
            results.append({'entity':entity,'layout':mode,'rgba_bytes':best['rgba_bytes'],
                            'decode_upload_ms':measured['decode_upload_ms'],'pages':measured['pages'],
                            'renderer':measured['renderer']})
    (root/'rendered.json').write_text(json.dumps({'cache':'warm OS file cache; process startup excluded',
                                               'rows':results},indent=2),encoding='utf-8')
    print(json.dumps(results,indent=2))


if __name__ == '__main__':
    main()
