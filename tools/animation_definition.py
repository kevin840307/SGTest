"""Compile source SPR/ACT into atlas-independent animation definitions.

No image output, atlas placement, network timing or gameplay semantics here.
Facing policies reuse the audited reference mappings; unresolved inputs fail.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re

if __package__:
    from .inspect_act import parse_act
    from .legacy_direction import map_character_direction, map_enemy_direction
    from .runtime_spr import decode_spr
else:
    from inspect_act import parse_act
    from legacy_direction import map_character_direction, map_enemy_direction
    from runtime_spr import decode_spr

SCHEMA = 1
CLIP_NAME = re.compile(r'^(.+)_([A-Z]{2})([1-8])$', re.I)
ENEMY_ACTIONS = {'NS': ('S', 0), 'NW': ('W', 1), 'ND': ('D', 2), 'FA': ('A', 3), 'NH': ('H', 4)}


def content_hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest()


def compile_definition(entity, sources, root, kind, act_path, character_actions=None,
                       draw_order_path=None, requested_behaviors=None, act_bindings=None):
    root = Path(root).resolve()
    act_path = Path(act_path).resolve()
    inputs = {}

    def read(path):
        path = Path(path).resolve()
        relative = path.relative_to(root).as_posix()
        raw = path.read_bytes()
        inputs[relative] = hashlib.sha256(raw).hexdigest()
        return relative, raw

    act_relative, act_raw = read(act_path)
    actions = {}
    for action in parse_act(act_raw):
        if action['key'] in actions:
            raise ValueError(f'duplicate ACT action: {action["key"]}')
        actions[action['key']] = action
    names = []
    if kind == 'character_part':
        if character_actions is None or draw_order_path is None:
            raise ValueError('character parts require original action names and draw-order table')
        _, raw = read(character_actions)
        names = [name.strip().upper() for name in raw.decode('ascii').split(',') if name.strip()]
        if len(names) < 13 or len(set(names[:13])) != 13:
            raise ValueError('expected 13 distinct primary character action names')
    elif kind not in ('monster', 'single_view'):
        raise ValueError(f'unknown definition kind: {kind}')
    draw_order = {}
    if draw_order_path is not None:
        _, raw = read(draw_order_path)
        for line in raw.decode('ascii').splitlines():
            if not line.strip():
                continue
            key, value = line.split(':', 1)
            key = key.strip().upper()
            if key in draw_order:
                raise ValueError(f'duplicate draw-order key: {key}')
            draw_order[key] = [token.strip() for token in value.split('>')]

    frames, clips, prefixes, seen = [], {}, set(), set()
    for path in sorted(map(Path, sources), key=lambda path: path.as_posix().casefold()):
        relative, raw = read(path)
        name = path.stem.upper()
        match = CLIP_NAME.fullmatch(name)
        if match is None or name in seen:
            raise ValueError(f'invalid or duplicate source clip: {name}')
        seen.add(name)
        prefixes.add(match[1])
        indices = []
        for index, frame in enumerate(decode_spr(raw)):
            bounds = frame.image.getbbox()
            pixels = frame.image.crop(bounds) if bounds else None
            indices.append(len(frames))
            frames.append({'id':f'{relative}#{index}', 'source':relative, 'source_index':index,
                           'size':list(frame.image.size), 'pivot':[frame.origin_x,frame.origin_y],
                           'trim':list(bounds) if bounds else [0,0,0,0],
                           'pixel_sha256':hashlib.sha256(pixels.tobytes()).hexdigest() if pixels else None})
        if not indices:
            raise ValueError(f'empty SPR clip: {name}')
        clips[name] = {'source':relative, 'frames':indices}
    if len(prefixes) != 1:
        raise ValueError('an entity definition must contain exactly one source prefix')
    prefix = next(iter(prefixes))
    available = sorted({CLIP_NAME.fullmatch(name)[2] for name in clips})
    if requested_behaviors is not None:
        selected = sorted(set(requested_behaviors))
        if set(selected) - set(available):
            raise ValueError('requested behavior has no SPR source')
    elif kind == 'character_part':
        selected = sorted(set(available).intersection(names[:13]))
    else:
        selected = available
    if not selected:
        raise ValueError('no resolvable behavior')
    act_bindings = act_bindings or {}
    if set(act_bindings)-set(selected):
        raise ValueError('ACT binding refers to an unselected behavior')

    behaviors = {}
    used_clips = set()
    for code in selected:
        if kind == 'monster':
            if code not in ENEMY_ACTIONS:
                raise ValueError(f'unknown monster action: {code}')
            act_key, action_id = ENEMY_ACTIONS[code]
            mapping = lambda facing: map_enemy_direction(facing, action_id)
        elif kind == 'character_part':
            if code not in names[:13]:
                raise ValueError(f'unverified character action mapping: {code}')
            act_key, action_id = code, names.index(code)
            mapping = lambda facing: map_character_direction(facing, action_id)
        else:
            act_key, action_id = code[-1], None
            mapping = lambda facing: (1, False)
        act_key = act_bindings.get(code, act_key)
        if act_key not in actions:
            raise ValueError(f'missing ACT timing for {code}: {act_key}')
        action = actions[act_key]
        timeline = [{'frame_index':step['index'], 'raw_time':step['raw_time'],
                     'default_ticks':max(1,step['raw_time']*40//25)} for step in action['frames']]
        if not timeline:
            raise ValueError(f'empty ACT timeline: {act_key}')
        facings = {}
        for facing in range(1,9):
            source_direction, flip = mapping(facing)
            source_clip = f'{prefix}_{code}{source_direction}'
            if source_clip not in clips:
                raise ValueError(f'{entity}/{code}/{facing}: required source {source_clip} is missing')
            if any(step['frame_index'] >= len(clips[source_clip]['frames']) for step in timeline):
                raise ValueError(f'ACT frame index outside source clip: {source_clip}')
            draw_key = f'{code}{source_direction}'
            if kind == 'character_part' and draw_key not in draw_order:
                raise ValueError(f'missing original draw-order key: {draw_key}')
            facings[str(facing)] = {'clip':source_clip, 'source_facing':source_direction,
                                   'flip_h':flip, 'draw_order_key':draw_key if draw_order else None}
            used_clips.add(source_clip)
        # Keep source events losslessly, without depending on uncommitted audit extensions.
        events = [{'step_index':event['at_frame_raw'], 'token':event['token'],
                   'raw_payload_hex':event['raw_payload_hex']} for event in action['events']]
        behaviors[code] = {'legacy_action_id':action_id, 'act_key':act_key,
                           'raw_loop_flag':action['raw_loop_flag'], 'loop':action['loops'],
                           'timeline':timeline, 'events':events, 'facings':facings}
    document = {'schema':'sgtest.animation', 'version':SCHEMA, 'entity':entity, 'kind':kind,
                'timing':{'fixed_hz':50, 'default_speed':25, 'numerator':40,
                          'independent_of_movement_and_render_fps':True},
                'mapping_evidence':'reference_decompilation; HK2 visual parity NOT VERIFIED',
                'act_source':act_relative, 'source_sha256':dict(sorted(inputs.items())),
                'frames':frames, 'clips':clips, 'behaviors':behaviors, 'draw_order':draw_order,
                'unreferenced_source_clips':sorted(set(clips)-used_clips),
                'unused_act_actions':sorted(set(actions)-{row['act_key'] for row in behaviors.values()})}
    document['content_sha256'] = content_hash(document)
    validate_definition(document)
    return document


def validate_definition(data):
    if data.get('schema') != 'sgtest.animation' or data.get('version') != SCHEMA:
        raise ValueError('unsupported animation definition schema')
    for code, behavior in data['behaviors'].items():
        if set(behavior['facings']) != {str(index) for index in range(1,9)}:
            raise ValueError(f'{code}: expected exactly eight logical facings')
        for view in behavior['facings'].values():
            clip = data['clips'][view['clip']]
            if not clip['frames'] or any(index < 0 or index >= len(data['frames']) for index in clip['frames']):
                raise ValueError('invalid frame reference')
            if any(step['frame_index'] < 0 or step['frame_index'] >= len(clip['frames']) or step['default_ticks'] < 1
                   for step in behavior['timeline']):
                raise ValueError('invalid timeline')
        if any(event['step_index'] < 0 or event['step_index'] >= len(behavior['timeline']) for event in behavior['events']):
            raise ValueError('invalid event step')
    expected = content_hash({key:value for key,value in data.items() if key != 'content_sha256'})
    if expected != data.get('content_sha256'):
        raise ValueError('definition content hash mismatch')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--entity', required=True)
    parser.add_argument('--kind', required=True, choices=['monster','character_part','single_view'])
    parser.add_argument('--act', required=True, help='path relative to source root')
    parser.add_argument('--spr', required=True, action='append', help='source-relative glob; repeatable')
    parser.add_argument('--character-actions')
    parser.add_argument('--draw-order')
    parser.add_argument('--behavior', action='append')
    parser.add_argument('--act-binding', action='append', default=[], help='explicit behavior=ACT-key, e.g. ND=Effect')
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    root = args.source.resolve()
    destination = args.output.resolve()
    if destination.is_relative_to(root):
        parser.error('output must be outside read-only source tree')
    sources = sorted({path for pattern in args.spr for path in root.glob(pattern)})
    bindings = {}
    for item in args.act_binding:
        code, separator, key = item.partition('=')
        if not separator or not key or code in bindings:
            parser.error('ACT bindings must be distinct behavior=ACT-key pairs')
        bindings[code] = key
    data = compile_definition(args.entity, sources, root, args.kind, root/args.act,
        root/args.character_actions if args.character_actions else None,
        root/args.draw_order if args.draw_order else None, args.behavior, bindings)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')), encoding='utf-8')
    print(json.dumps({'entity':data['entity'],'behaviors':len(data['behaviors']),
                      'frames':len(data['frames']),'content_sha256':data['content_sha256']}))


if __name__ == '__main__':
    main()
