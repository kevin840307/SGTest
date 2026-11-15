"""Compile private NPC/transfer evidence without executing legacy Lua.

Only the audited TS_Common template and an exact static entry shape become a
confirmed transfer. Other scripts remain explicit script_required records.
"""
import configparser
import hashlib
import json
from pathlib import Path, PureWindowsPath
import re

from ..source.legacy_npc import npc_placements, npc_visual_table, plain
from ..source.runtime_spr import decode_spr
from .build_maps import dimensions, resolve, server_map_directory as map_directory

TRANSFER_TEMPLATE_SHA256 = '8c972667f520ef00441f2b12c5a16fa1475a2e0a5ce1dd20e16bb79e37697562'
_SHORT_STRING = r'"(?:[^"\\\r\n]|\\[^\r\n])*"'
_LONG_COMMENT = re.compile(r'--\[(=*)\[')
_STATIC = re.compile(
    r'\s*function\s+start\s*\(\s*\)\s*(?P<entry>CanMove|CannnotMove)\s*\(\s*\)\s*end\s*'
    r'function\s+GetLocName\s*\(\s*\)\s*return\s+'+_SHORT_STRING+r'\s*end\s*'
    r'function\s+GetLoc\s*\(\s*\)\s*return\s+(?P<x>-?\d{1,10})\s*,\s*(?P<y>-?\d{1,10})\s*,\s*(?P<map>\d{1,5})\s*end\s*'
    r'#include\s+"TS_Common\.txt"\s*', re.ASCII)


def _basename(value: str) -> str:
    path = PureWindowsPath(value)
    if not value or path.drive or path.root or len(path.parts) != 1 or path.name in ('.', '..') or ':' in value or '/' in value or '\\' in value:
        raise ValueError('unsafe original NPC script reference')
    return value if value.lower().endswith('.txt') else value+'.txt'


def _without_comments(text: str) -> str:
    """Small lexical scan: comments cannot create functions from quoted text."""
    result, at, quote = [], 0, ''
    while at < len(text):
        ch = text[at]
        if quote:
            result.append(ch)
            if ch == '\\' and at+1 < len(text):
                at += 1
                result.append(text[at])
            elif ch == quote:
                quote = ''
            at += 1
        elif ch in ('"', "'"):
            quote = ch
            result.append(ch)
            at += 1
        elif match := _LONG_COMMENT.match(text, at):
            closing = ']'+match[1]+']'
            end = text.find(closing, match.end())
            if end < 0:
                raise ValueError('unterminated Lua comment')
            result.append(' ')
            at = end+len(closing)
        elif text.startswith('--', at):
            end = text.find('\n', at)
            result.append('\n')
            at = len(text) if end < 0 else end+1
        else:
            result.append(ch)
            at += 1
    return ''.join(result)


def _script_text(raw: bytes) -> str:
    raw = plain(raw)
    for encoding in ('utf-8-sig', 'cp950'):
        try:
            return raw.decode(encoding).replace('\r\n', '\n').replace('\r', '\n')
        except UnicodeDecodeError:
            pass
    raise ValueError('unsupported NPC script encoding')


def build(source: Path, server_dat: Path, map_id: int, output: Path) -> dict:
    source, server_dat, output = source.resolve(), server_dat.resolve(), output.resolve()
    for original in (source, server_dat):
        if output == original or output.is_relative_to(original):
            raise ValueError('NPC output must not overwrite original source')
    inputs, cache = {}, {}

    def read(path: Path, root: Path, prefix: str) -> bytes:
        resolved = path.resolve()
        if not resolved.is_relative_to(root):
            raise ValueError('NPC resource leaves original source tree')
        raw = resolved.read_bytes()
        inputs[prefix+'/'+resolved.relative_to(root).as_posix()] = hashlib.sha256(raw).hexdigest()
        return raw

    rows = npc_placements(read(map_directory(server_dat, map_id)/'NPC.dat', server_dat, 'server'))
    table = npc_visual_table(read(source/'npc/npc_table.dat', source, 'client'))
    quest = server_dat/'Quest'
    scripts = {}
    if quest.is_dir():
        for path in quest.iterdir():
            if path.is_file():
                key = path.name.casefold()
                if key in scripts:
                    raise ValueError('ambiguous case-insensitive quest file')
                scripts[key] = path

    def location(target_map, point):
        key = int(target_map)
        if key not in cache:
            try:
                ini = configparser.ConfigParser(interpolation=None)
                ini.read_string(plain(read(source/f'map/map{key}.map', source, 'client')).decode('cp950'))
                grid = plain(read(resolve(source, ini['Map']['Blocking']), source, 'client'))
                width, height = dimensions(grid, 1)
                cache[key] = (width, height, grid)
            except FileNotFoundError:
                cache[key] = None
        data = cache[key]
        if data is None:
            return 'map_resource_missing'
        width, height, grid = data
        x, y = point
        if not (0 <= x < width*16 and 0 <= y < height*16):
            return 'out_of_bounds'
        return 'walkable' if grid[8+(y//16)*width+x//16] & 7 == 0 else 'blocked'

    def interaction(script):
        if not script:
            return {'mode': 'none'}
        name = _basename(script)
        path = scripts.get(name.casefold())
        if path is None:
            return {'mode': 'unavailable', 'reason': 'script_missing'}
        raw = read(path, server_dat, 'server')
        try:
            match = _STATIC.fullmatch(_without_comments(_script_text(raw)))
        except ValueError:
            match = None
        if not match:
            return {'mode': 'script_required', 'reason': 'not_a_static_TS_entry', 'dependencies_complete': False}
        common = scripts.get('ts_common.txt')
        if common is None or hashlib.sha256(read(common, server_dat, 'server')).hexdigest() != TRANSFER_TEMPLATE_SHA256:
            return {'mode': 'script_required', 'reason': 'unverified_TS_template', 'dependencies_complete': False}
        if match['entry'] != 'CanMove':
            return {'mode': 'blocked', 'reason': 'disabled_script_entry', 'dependencies_complete': True}
        x, y, target_map = (int(match[key]) for key in ('x', 'y', 'map'))
        if not 1 <= target_map <= 65535 or not (0 <= x <= 2147483647 and 0 <= y <= 2147483647):
            return {'mode': 'unavailable', 'reason': 'invalid_destination', 'dependencies_complete': True}
        status = location(target_map, [x, y])
        return {'mode': 'confirm_transfer' if status == 'walkable' else 'transfer_unavailable',
                'destination': {'map_id': target_map, 'p': [x, y]},
                'destination_status': status, 'choice': 0, 'dependencies_complete': True,
                'adapter': 'audited_TS_Common_v1'}

    visuals = {}
    for row in rows:
        row['spawn_id'] = row['source_index']+1
        row['position_status'] = location(map_id, row['p'])
        type_id = row['type_id']
        if type_id not in visuals:
            visual = {'resolved': False, 'reason': 'type_not_in_table'}
            if type_id < len(table):
                fields = table[type_id]
                visual = {'resolved': False, 'reason': 'empty_primary_slot', 'source_slots': fields}
                if fields[0]:
                    path = resolve(source, fields[0])
                    if path.suffix.lower() != '.spr':
                        raise ValueError('NPC visual is not an SPR reference')
                    if path.is_file():
                        frames = decode_spr(read(path, source, 'client'))
                        visual = {'resolved': True, 'source': path.relative_to(source).as_posix(),
                                  'frames': len(frames), 'source_slots': fields,
                                  'timing_verified': False, 'direction_mapping_verified': False}
                    else:
                        visual['reason'] = 'sprite_missing'
            visuals[type_id] = visual
        row['visual'] = visuals[type_id]
        row['interaction'] = interaction(row['script'])
    document = {'schema': 'sgtest.npcs/v1', 'map_id': map_id, 'coordinate_space': 'world_pixels',
                'spawn_id_policy': 'map-scoped source_index+1; fenced by content hash',
                'visual_table_rows': len(table), 'npcs': rows, 'source_sha256': dict(sorted(inputs.items()))}
    document['fingerprint'] = hashlib.sha256(json.dumps(document, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name+'.tmp')
    temporary.write_text(json.dumps(document, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    temporary.replace(output)
    modes = {}
    for row in rows:
        mode = row['interaction']['mode']
        modes[mode] = modes.get(mode, 0)+1
    return {'status': 'pass', 'map_id': map_id, 'npcs': len(rows), 'interactions': modes,
            'unresolved_visuals': sum(not row['visual']['resolved'] for row in rows),
            'fingerprint': document['fingerprint'], 'source_sha256': document['source_sha256'],
            'output': str(output), 'runtime_transfer_implemented': False}
