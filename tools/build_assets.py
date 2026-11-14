"""RESEARCH-ONLY legacy per-entity/direction atlas candidate builder.

Production runtime sprites are built by asset_pipeline.py from lifecycle bundles.
Reproducible entity atlases from a decoded original Data1 tree (plain/CDE SPR).

Never scans a client plus its extracted duplicates: pass the output directory.
One entity/part owns its actions. Each direction is contiguous and never split;
small entities share one PNG, larger entities can use one PNG per direction.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import re
import struct
import time
from dataclasses import dataclass

from PIL import Image
from runtime_spr import decode_spr

VERSION = 7
ANIM = re.compile(r"^(.+)_([A-Z]{2})([1-8])$", re.I)


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + '.tmp')
    tmp.write_text(json.dumps(data, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    tmp.replace(path)


def group_key(relative):
    match = ANIM.match(relative.stem)
    return (relative.parent / (match[1] if match else relative.stem)).as_posix().lower()


def layout(images, side):
    """Best-fit guillotine rectangles, sorted deterministically; 1 px gutter."""
    free = []
    placements = {}
    sizes = []
    for index in sorted(range(len(images)), key=lambda i: (-max(images[i].size), -images[i].width * images[i].height, i)):
        w, h = images[index].size
        if w + 2 > side or h + 2 > side:
            raise ValueError(f'frame {w}x{h} exceeds {side} with gutters')
        candidates = [(min(fw-w-2, fh-h-2), fw*fh, page, slot)
                      for page, rects in enumerate(free) for slot, (_, _, fw, fh) in enumerate(rects)
                      if w+2 <= fw and h+2 <= fh]
        if candidates:
            _, _, page, slot = min(candidates)
        else:
            page, slot = len(free), 0
            free.append([(0, 0, side, side)])
            sizes.append([0, 0])
        x, y, fw, fh = free[page].pop(slot)
        rw, rh = w+2, h+2
        if fw-rw > fh-rh:
            remaining = [(x+rw, y, fw-rw, fh), (x, y+rh, rw, fh-rh)]
        else:
            remaining = [(x+rw, y, fw-rw, rh), (x, y+rh, fw, fh-rh)]
        free[page].extend(r for r in remaining if r[2] and r[3])
        placements[index] = (page, x+1, y+1, w, h)
        sizes[page] = [max(sizes[page][0], x+rw), max(sizes[page][1], y+rh)]
    return placements, sizes


def shelf_layout(images, side, width=None):
    width = width or side
    placements, pages, sizes = {}, [], []
    for index in sorted(range(len(images)), key=lambda i: (-images[i].height, -images[i].width, i)):
        w, h = images[index].width+2, images[index].height+2
        if w > width or h > side:
            raise ValueError('frame exceeds atlas dimensions including gutter')
        fits = [(shelf[2]-h, width-shelf[0]-w, page, row)
                for page, shelves in enumerate(pages) for row, shelf in enumerate(shelves)
                if shelf[2] >= h and shelf[0]+w <= width]
        if fits:
            _, _, page, row = min(fits)
        else:
            page = next((p for p,s in enumerate(sizes) if s[1]+h <= side), len(pages))
            if page == len(pages):
                pages.append([])
                sizes.append([0, 0])
            row = len(pages[page])
            pages[page].append([0, sizes[page][1], h])
            sizes[page][1] += h
        shelf = pages[page][row]
        x, y = shelf[:2]
        placements[index] = (page, x+1, y+1, w-2, h-2)
        shelf[0] += w
        sizes[page][0] = max(sizes[page][0], shelf[0])
    return placements, sizes


@dataclass
class Box:
    width: int
    height: int


def directional_layout(images, directions, side, separate=False):
    """Each stored direction owns a contiguous rectangle in the single atlas."""
    groups = defaultdict(list)
    for index, direction in enumerate(directions):
        groups[direction].append(index)
    blocks, layouts, keys = [], [], sorted(groups)
    for direction in keys:
        indices = groups[direction]
        local = [images[i] for i in indices]
        candidates = []
        for width in sorted({min(side-2, n) for n in (128, 256, 512, 768, 1024, 1536, 2048, 3072, 4096, 8192, 16384)}):
            if width < max(i.width+2 for i in local):
                continue
            try:
                placement, sizes = shelf_layout(local, side-2, width)
            except ValueError:
                continue
            if len(sizes) == 1:
                w,h = sizes[0]
                candidates.append((w*h, abs(w-h), placement, w, h))
        if not candidates:
            raise ValueError(f'direction {direction} exceeds atlas size')
        _, _, placement, w, h = min(candidates, key=lambda c: c[:2])
        blocks.append(Box(w,h))
        layouts.append((indices, placement))
    if separate:
        block_placements = {i: (i,0,0,b.width,b.height) for i,b in enumerate(blocks)}
        sizes = [[b.width,b.height] for b in blocks]
    else:
        block_placements, sizes = shelf_layout(blocks, side)
    placements, regions = {}, {}
    for index, (indices, local) in enumerate(layouts):
        page,bx,by,bw,bh = block_placements[index]
        regions[keys[index]] = {'page': page, 'rect': [bx,by,bw,bh]}
        for local_index, (_,x,y,w,h) in local.items():
            placements[indices[local_index]] = (page,bx+x,by+y,w,h)
    return placements, sizes, regions


def act_timing(raw):
    # Original ACT records: type byte, length uint32, payload. Timelines use
    # frame-index/duration-tick pairs, including repeated indices. Preserve raw
    # ticks; playback tick rate is a runtime policy, not an extraction guess.
    result, action, at = {}, '', 0
    while at < len(raw):
        if raw[at:] == b'\xff':
            break
        if at+5 > len(raw):
            raise ValueError('truncated ACT record')
        kind, size = raw[at], struct.unpack_from('<I', raw, at+1)[0]
        at += 5
        if kind == 2:
            size *= 8
        payload = raw[at:at+size]
        if len(payload) != size:
            raise ValueError('truncated ACT payload')
        at += size
        if kind == 1:
            action = payload.rstrip(b'\0').decode('ascii')
            result[action] = {'timeline': [], 'events': []}
        elif kind == 2 and action:
            result[action]['timeline'] = [list(pair) for pair in struct.iter_unpack('<II', payload)]
        elif kind == 3 and action:
            result[action]['loop'] = bool(payload[0]) if payload else False
        elif kind == 4 and action:
            result[action]['events'].append({'frame': struct.unpack_from('<I', payload)[0],
                                            'cue': payload[4:].rstrip(b'\0').decode('ascii')})
    return result


def aliases(clips, monster=False):
    result = {}
    for name in sorted(clips):
        match = ANIM.match(name)
        if not match:
            continue
        base, action, _ = match.groups()
        for direction in range(1, 9):
            dest = f'{base}_{action}{direction}'
            if dest in clips or dest in result:
                continue
            canonical = {5: 3, 6: 2, 7: 1}.get(direction, direction)
            flip = direction in (5, 6, 7)
            if monster and action.endswith('S'):
                canonical, flip = {1:(1,False), 2:(3,False), 3:(3,False), 4:(3,True),
                                   5:(3,True), 6:(1,True), 7:(1,True), 8:(1,False)}[direction]
            source = f'{base}_{action}{canonical}'
            # Same stable adjacent/death fallbacks as Recovered. Only create
            # aliases when the corresponding actual source exists.
            if source not in clips and monster:
                source = f'{base}_{action}1'
            elif source not in clips and canonical in (4, 8):
                source = f'{base}_{action}{3 if canonical == 4 else 1}'
            if action.endswith('D') and not monster:
                source, flip = f'{base}_{action}1', False
            if source in clips:
                result[dest] = {'clip': source, 'flip_h': flip}
    return result


def convert_group(key, sources, root, output, sides, force=False, mode='entity'):
    target = output / key
    manifest = target.with_name(target.name + '.json')
    requested_mode = mode
    raw_sources = []
    for path in sources:
        raw = path.read_bytes()
        raw_sources.append((path, raw))
    act = sources[0].parent / (ANIM.match(sources[0].stem)[1] + '.ACT') if ANIM.match(sources[0].stem) else sources[0].with_suffix('.ACT')
    if not act.exists():
        act = next((p for p in sources[0].parent.glob('*') if p.name.lower() == act.name.lower()), act)
    act_raw = act.read_bytes() if act.exists() else b''
    def fingerprint_for(layout_mode):
        digest = hashlib.sha256(f'{VERSION}:{sides}:{layout_mode}'.encode())
        for path, raw in raw_sources:
            digest.update(path.relative_to(root).as_posix().encode())
            digest.update(raw)
        digest.update(act_raw)
        return digest.hexdigest()

    if mode == 'auto' and manifest.exists():
        previous = json.loads(manifest.read_text(encoding='utf-8'))
        rectangles = {(f['page'],tuple(f['rect'])) for f in previous['frames'] if f['page'] >= 0}
        area = sum(rect[2]*rect[3]*4 for _,rect in rectangles)
        mode = 'entity' if area <= 1536*1024 else 'direction'
    fingerprint = fingerprint_for(mode)
    if manifest.exists() and not force:
        old = json.loads(manifest.read_text(encoding='utf-8'))
        if old.get('sha256') == fingerprint and all((manifest.parent / p).exists() for p in old['pages']):
            return old, True
    images, directions, unique, records, clips = [], [], {}, [], {}
    for path, raw in raw_sources:
        animation = ANIM.match(path.stem)
        direction = animation[3] if animation else 'default'
        indices = []
        for frame in decode_spr(raw):
            bounds = frame.image.getbbox()
            record = {'page': -1, 'rect': [0, 0, 0, 0], 'size': list(frame.image.size),
                      'origin': [frame.origin_x, frame.origin_y], 'trim': list(bounds[:2]) if bounds else [0, 0]}
            if bounds:
                image = frame.image.crop(bounds)
                identity = (direction, image.size, hashlib.sha256(image.tobytes()).digest())
                if identity not in unique:
                    unique[identity] = len(images)
                    images.append(image)
                    directions.append(direction)
                record['image'] = unique[identity]
            indices.append(len(records))
            records.append(record)
        clips[path.stem.upper()] = indices
    trials = []
    if requested_mode == 'auto':
        # Small sprites favor one upload; above 1.5 MiB of unique trimmed RGBA,
        # measured samples favored separate, contiguous direction blocks.
        mode = 'entity' if sum(i.width*i.height*4 for i in images) <= 1536*1024 else 'direction'
        fingerprint = fingerprint_for(mode)
    for side in sorted(set(sides + [8192, 16384])):
        try:
            placements, sizes, regions = directional_layout(images, directions, side, mode == 'direction')
        except ValueError:
            continue
        trials.append({'side': side, 'pages': len(sizes), 'rgba_bytes': sum(w*h*4 for w,h in sizes)})
        if (len(sizes) <= 1 or mode == 'direction') and side >= max(sides):
            break
    single = [t for t in trials if t['pages'] <= 1 or mode == 'direction']
    if not single:
        raise ValueError('entity/direction block exceeds one 16384 texture; block splitting is forbidden')
    chosen = min(single, key=lambda t: t['rgba_bytes'])
    placements, sizes, regions = directional_layout(images, directions, chosen['side'], mode == 'direction')
    pages = [Image.new('RGBA', size) for size in sizes]
    for index, (page, x, y, w, h) in placements.items():
        pages[page].paste(images[index], (x, y))
    for record in records:
        if 'image' in record:
            page, *rect = placements[record.pop('image')]
            record.update(page=page, rect=rect)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    names = []
    for index, page in enumerate(pages):
        name = f'{target.name}_{index}.png'
        tmp = target.parent / (name + '.tmp')
        page.save(tmp, format='PNG', compress_level=3)
        tmp.replace(target.parent / name)
        names.append(name)
    document = {'version': VERSION, 'sha256': fingerprint, 'entity': key, 'pages': names,
                'layout': mode, 'page_sizes': sizes, 'atlas_side': chosen['side'], 'trials': trials, 'direction_regions': regions,
                'frames': records, 'clips': clips, 'aliases': aliases(clips, key.startswith('enemy/')),
                'act': act_timing(act_raw) if act_raw else {},
                'sources': [p.relative_to(root).as_posix() for p in sources]}
    write_json(manifest, document)
    for stale in target.parent.glob(target.name + '_*.png'):
        if stale.name not in names and re.fullmatch(re.escape(target.name) + r'_\d+\.png', stale.name):
            stale.unlink()
    return document, False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--category', action='append')
    parser.add_argument('--limit', type=int, help='limit entity groups, never individual actions')
    parser.add_argument('--sides', nargs='+', type=int, default=[1024, 2048, 4096])
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--layout', choices=('auto', 'entity', 'direction'), default='auto',
                        help='one entity PNG or one PNG per stored direction; both keep directions contiguous')
    args = parser.parse_args()
    root, output = args.source.resolve(), args.output.resolve()
    if not root.is_dir() or root == output or root in output.parents or output in root.parents:
        parser.error('source and output must be separate, non-nested directories')
    if any(s not in (1024, 2048, 4096) for s in args.sides):
        parser.error('supported sides: 1024 2048 4096')
    output.mkdir(parents=True, exist_ok=True)
    (output/'.gdignore').write_text('# Runtime PNG/JSON; avoid bulk editor imports.\n', encoding='utf-8')
    groups = defaultdict(list)
    categories = {c.lower() for c in args.category or []}
    for path in sorted(root.rglob('*.spr')):
        rel = path.relative_to(root)
        if not categories or rel.parts[0].lower() in categories:
            groups[group_key(rel)].append(path)
    started = time.perf_counter()
    results, errors, cached = [], [], 0
    for key, sources in list(sorted(groups.items()))[:args.limit]:
        try:
            doc, hit = convert_group(key, sources, root, output, sorted(set(args.sides)), args.force, args.layout)
            cached += hit
            results.append({'entity': key, 'sources': len(sources), 'frames': len(doc['frames']),
                            'pages': len(doc['pages']), 'rgba_bytes': sum(w*h*4 for w,h in doc['page_sizes']),
                            'side': doc['atlas_side'], 'aliases': len(doc['aliases'])})
        except (OSError, ValueError, struct.error) as exc:
            errors.append({'entity': key, 'error': str(exc)})
        if (len(results)+len(errors)) % 25 == 0:
            print(f'groups={len(results)} failed={len(errors)} elapsed={time.perf_counter()-started:.1f}s', flush=True)
    report = {'version': VERSION, 'seconds': time.perf_counter()-started, 'cached': cached,
              'groups': results, 'errors': errors, 'source_root': str(root)}
    # A partial run gets its own report and never replaces the complete catalog.
    name = 'catalog.json' if not categories and args.limit is None else 'partial_catalog.json'
    write_json(output / name, report)
    print(json.dumps({k:v for k,v in report.items() if k != 'groups'}, ensure_ascii=False), flush=True)
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
