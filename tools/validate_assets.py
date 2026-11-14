"""Validate complete catalog, frame bounds/aliases and sampled pixel round trips.

--all-pixels checks every original frame, rather than deterministic one/group.
"""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image
from convert_assets import decode_spr
from build_assets import write_json


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--all-pixels', action='store_true')
    args = parser.parse_args()
    catalog = json.loads((args.output/'catalog.json').read_text(encoding='utf-8'))
    assert not catalog['errors'], catalog['errors']
    represented, checked, errors = set(), 0, []
    for entry in catalog['groups']:
        try:
            path = args.output/(entry['entity']+'.json')
            doc = json.loads(path.read_text(encoding='utf-8'))
            if doc.get('layout') == 'entity':
                assert len(doc['pages']) <= 1, 'entity layout must be one PNG'
            for name,indices in doc['clips'].items():
                match = __import__('re').match(r'^.+_[A-Z]{2}([1-8])$', name)
                if not match:
                    continue
                region = doc['direction_regions'][match[1]]
                rx,ry,rw,rh = region['rect']
                for index in indices:
                    f = doc['frames'][index]
                    if f['page'] >= 0:
                        x,y,w,h = f['rect']
                        assert f['page'] == region['page'] and rx <= x and ry <= y and x+w <= rx+rw and y+h <= ry+rh, 'direction block violated'
            for alias in doc['aliases'].values():
                assert alias['clip'] in doc['clips'], 'alias target missing'
            pages = [Image.open(path.parent/p).convert('RGBA') for p in doc['pages']]
            for frame in doc['frames']:
                if frame['page'] < 0:
                    continue
                x,y,w,h = frame['rect']
                image = pages[frame['page']]
                assert x >= 1 and y >= 1 and x+w < image.width and y+h < image.height
            sources = doc['sources']
            represented.update(s.lower() for s in sources)
            selected = sources if args.all_pixels else [sources[int(hashlib.sha256(entry['entity'].encode()).hexdigest()[:8],16) % len(sources)]]
            for source in selected:
                frames = decode_spr((args.source/source).read_bytes())
                indices = doc['clips'][Path(source).stem.upper()]
                assert len(indices) == len(frames), 'frame count mismatch'
                for original, index in zip(frames, indices):
                    frame = doc['frames'][index]
                    assert frame['origin'] == [original.origin_x, original.origin_y]
                    reconstructed = Image.new('RGBA', original.image.size)
                    if frame['page'] >= 0:
                        x,y,w,h = frame['rect']
                        reconstructed.paste(pages[frame['page']].crop((x,y,x+w,y+h)), tuple(frame['trim']))
                    # Transparent pixels can have hidden RGB; only visible RGBA matters.
                    expected = Image.new('RGBA', original.image.size)
                    bounds = original.image.getbbox()
                    if bounds:
                        expected.paste(original.image.crop(bounds), bounds[:2])
                    assert reconstructed.tobytes() == expected.tobytes(), f'pixel mismatch: {source}:{index}'
                    checked += 1
            for image in pages:
                image.close()
        except (AssertionError, OSError, ValueError, KeyError) as exc:
            errors.append({'entity': entry['entity'], 'error': str(exc)})
    actual = {p.relative_to(args.source).as_posix().lower() for p in args.source.rglob('*.spr')}
    if actual != represented:
        errors.append({'source_coverage': {'missing': sorted(actual-represented), 'extra': sorted(represented-actual)}})
    report = {'entities': len(catalog['groups']), 'source_sprites': len(actual), 'pixel_frames_checked': checked,
              'all_pixels': args.all_pixels, 'errors': errors}
    write_json(Path('reports/asset_validation.json'), report)
    print(json.dumps(report, ensure_ascii=False))
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
