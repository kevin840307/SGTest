"""Compare entity PNG layouts, with real Godot decode/upload measured separately.

CPU decode figures are warm OS cache (not claims about cold disk performance).
"""
import argparse
import json
import itertools
from pathlib import Path
import statistics
import time
from PIL import Image
from build_assets import convert_group, group_key, write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path, default=Path('reports/atlas_benchmark'))
    parser.add_argument('--entity', action='append', default=[])
    args = parser.parse_args()
    root = args.source.resolve()
    keys = args.entity or ['enemy/en004a', 'enemy/en023a', 'enemy/en050b']
    sources = [p for p in root.rglob('*.spr') if group_key(p.relative_to(root)) in keys]
    rows = []
    for key in keys:
        selected = [p for p in sources if group_key(p.relative_to(root)) == key]
        if not selected:
            continue
        for side, mode in itertools.product((1024, 2048, 4096), ('entity', 'direction')):
            dest = args.output / f'{side}_{mode}'
            doc, _ = convert_group(key, selected, root, dest, [side], True, mode)
            files = [(dest/key).parent / name for name in doc['pages']]
            durations = []
            for _ in range(15):
                start = time.perf_counter()
                for path in files:
                    with Image.open(path) as image:
                        image.load()
                durations.append((time.perf_counter()-start)*1000)
            rows.append({'entity': key, 'layout': mode, 'requested_side': side, 'actual_side': doc['atlas_side'],
                         'pages': len(files), 'dimensions': doc['page_sizes'],
                         'png_bytes': sum(p.stat().st_size for p in files),
                         'rgba_bytes': sum(w*h*4 for w,h in doc['page_sizes']),
                         'decode_ms_p50': statistics.median(durations),
                         'decode_ms_p95': sorted(durations)[14], 'trials': doc['trials']})
    write_json(args.output/'results.json', {'cache': 'warm OS file cache', 'rows': rows})
    print(json.dumps(rows, indent=2))


if __name__ == '__main__':
    main()
