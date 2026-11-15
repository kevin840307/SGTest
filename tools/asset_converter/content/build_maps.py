"""Build compact original terrain atlases, object references and collision grids.

Map INI selects PIC (uint32 width/height, RGB565 strip), MapBlock (uint32
width/height then uint16 tile IDs; 65535 empty), Blocking (uint32 width/height
then one byte/cell), and ObjectList (total count including children, name + XY
and a child count on top-level records only).
Sprite object images remain shared with build_assets output; no recovered scenes.
"""
import argparse
import configparser
import hashlib
import json
import math
from pathlib import Path, PureWindowsPath
import struct
import time
from PIL import Image
from ..json_files import write_json
from ..source.runtime_spr import decode_spr, unwrap_cde
from ..source.scenery import Reader, parse_blg, parse_sif_contract, compile_scenery_order
from .asset_paths import source_sprite_manifest


def server_map_directory(server_dat: Path, map_id: int) -> Path:
    """Resolve legacy Map07 and Map13 without guessing between duplicates."""
    if not 1 <= map_id <= 65535:
        raise ValueError('invalid original map id')
    base = server_dat.resolve()
    candidates = {base/'Map'/f'Map{map_id}', base/'Map'/f'Map{map_id:02d}'}
    found = [path.resolve() for path in candidates if path.is_dir()]
    if len(found) != 1 or not found[0].is_relative_to(base):
        raise ValueError(f'missing/ambiguous original Map{map_id} directory')
    return found[0]


def plain(path):
    raw = path.read_bytes()
    return unwrap_cde(raw) if raw.startswith(b'C.D.E.') else raw


def resolve(root, name, parent=None):
    candidate = PureWindowsPath(name)
    if candidate.drive or candidate.root:
        raise ValueError(f'absolute original resource path forbidden: {name}')
    path = (parent or root).joinpath(*[p.lower() for p in candidate.parts]).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError(f'resource path leaves original tree: {name}')
    return path


def dimensions(raw, stride):
    if len(raw) < 8:
        raise ValueError('short grid header')
    w,h = struct.unpack_from('<II',raw)
    if not w or not h or w*h > 128_000_000 or len(raw) != 8+w*h*stride:
        raise ValueError(f'invalid grid size {w}x{h}, bytes={len(raw)}')
    return w,h


def object_records(raw):
    """Decode the hierarchy, not a guessed XYZ layout.

    DIRECT: Paradise.exe CELMap2D::MapObjListConvert, 0x005291B5–0x005297AB.
    The header includes children; only roots have a following int32 child count.
    Children are name+XY, have no own count, and retain absolute world XY.
    Both root and child SetWorldPosition calls explicitly pass zero Z.
    """
    if len(raw) < 4:
        raise ValueError('short ObjectList header')
    count = struct.unpack_from('<I', raw)[0]
    if count > 1_000_000:
        raise ValueError('ObjectList count outside supported bounds')
    at, records = 4, []
    parent_index, children_left = None, 0
    for index in range(count):
        if at >= len(raw):
            raise ValueError(f'truncated ObjectList record {index}')
        length = raw[at]
        at += 1
        if not length or at + length + 8 > len(raw):
            raise ValueError(f'invalid ObjectList path length at record {index}')
        try:
            name = raw[at:at+length].decode('cp950')
        except UnicodeDecodeError as error:
            raise ValueError(f'invalid ObjectList name encoding at record {index}') from error
        at += length
        x, y = struct.unpack_from('<ii', raw, at)
        at += 8
        if children_left:
            child_count = 0
            children_left -= 1
            parent = parent_index
        else:
            if at + 4 > len(raw):
                raise ValueError(f'truncated ObjectList child count at record {index}')
            child_count = struct.unpack_from('<i', raw, at)[0]
            at += 4
            if child_count < 0 or child_count > count-index-1:
                raise ValueError(f'ObjectList child count outside remaining records at {index}')
            if child_count and not name.lower().endswith('.blg'):
                raise ValueError(f'ObjectList non-building parent at record {index}')
            parent, parent_index, children_left = None, index, child_count
        records.append({'name':name, 'p':[x,y], 'parent_index':parent,
                        'child_count':child_count})
    if raw[at:] not in (b'', b'\0\0\0\0'):
        raise ValueError('unexpected ObjectList trailer or record alignment')
    return records


def scenery_definition(source, resource):
    """Preserve two building appearances, SIF mode and original front geometry."""
    building = resource.suffix.lower() == '.blg'
    definition = {'sorting_mode':0 if building else 1}
    if building:
        definition.update(parse_blg(plain(resource)))
        refs = [definition.pop('primary'), definition.pop('alternate')]
    else:
        refs = [resource.name]
    hashes = {resource.relative_to(source).as_posix():hashlib.sha256(plain(resource)).hexdigest()}
    appearances = []
    for reference in refs:
        if not reference:
            appearances.append(None)
            continue
        path = resolve(source,reference,resource.parent)
        sprite = object_sprite(source,path)
        sif_path = path.with_suffix('.sif')
        sif = parse_sif_contract(plain(sif_path))
        if resolve(source,sif['spr_name'],sif_path.parent) != sprite:
            raise ValueError(f'SIF/appearance SPR mismatch: {path}')
        for dependency in (path,sif_path,sprite):
            hashes[dependency.relative_to(source).as_posix()] = hashlib.sha256(plain(dependency)).hexdigest()
        key = sprite.relative_to(source).as_posix()
        appearances.append({'source':key,'atlas':source_sprite_manifest(key),
                            'clip':sprite.stem.upper(), **{k:v for k,v in sif.items() if k != 'spr_name'}})
    primary = appearances[0]
    # Original ctor gets mode from primary, but geometry from the last nonempty
    # SIF (alternate overwrites center/size). Preserve per-appearance values too.
    geometry = next(row for row in reversed(appearances) if row is not None)
    definition.update(appearances=appearances, draw_group=primary['mode'] if primary['mode'] in (0,1,2) else 1,
                      center=geometry['center'], size=geometry['size'], source_hashes=hashes)
    return definition


def objects(raw, source, definitions=None):
    result = []
    decoded_cache = {}
    definitions = {} if definitions is None else definitions
    for record in object_records(raw):
        name = record['name']
        x, y = record['p']
        resource = resolve(source,'map/'+name)
        definition_id = resource.relative_to(source).as_posix()
        if definition_id not in definitions:
            definition = scenery_definition(source,resource)
            for appearance in definition['appearances']:
                if appearance is None:
                    continue
                key = appearance['source']
                if key not in decoded_cache:
                    decoded = decode_spr((source/key).read_bytes())
                    boxes = []
                    for frame in decoded:
                        crop = frame.image.getbbox()
                        if crop:
                            l,t,r,b = crop
                            boxes.append((l-frame.origin_x,t-frame.origin_y,r-frame.origin_x,b-frame.origin_y))
                    decoded_cache[key] = (boxes,len(decoded))
                boxes, count = decoded_cache[key]
                appearance['frames'] = count
                appearance['local_bounds'] = [min((b[0] for b in boxes),default=0),
                                              min((b[1] for b in boxes),default=0),
                                              max((b[2] for b in boxes),default=0),
                                              max((b[3] for b in boxes),default=0)]
            definitions[definition_id] = definition
        definition = definitions[definition_id]
        primary = definition['appearances'][0]
        key, clip, frame_count = primary['source'], primary['clip'], primary['frames']
        local_boxes = [a['local_bounds'] for a in definition['appearances'] if a is not None]
        left = min((x+b[0] for b in local_boxes), default=x)
        top = min((y+b[1] for b in local_boxes), default=y)
        right = max((x+b[2] for b in local_boxes), default=x)
        bottom = max((y+b[3] for b in local_boxes), default=y)
        result.append({'atlas':source_sprite_manifest(key),'clip':clip,'p':[x,y], 'z':0,
                       'definition':definition_id,
                       'parent_index':record['parent_index'], 'child_count':record['child_count'],
                       'bounds':[left,top,right-left,bottom-top],'frames':frame_count,
                       'source':key})
    return result


def object_sprite(source, resource):
    # BLG/STH store authoring absolute paths followed by the runtime relative
    # reference. Follow the latter: differently named BLGs can share a sprite.
    for _ in range(4):
        extension = resource.suffix.lower()
        if extension == '.spr':
            return resource
        reader, names = Reader(plain(resource)), []
        if extension not in ('.sif','.sth','.blg'):
            raise ValueError('unsupported scenery reference extension: '+extension)
        for _ in range(1 if extension == '.sif' else 2):
            names.append(reader.name())
        if not names[-1]:
            raise ValueError('empty scenery SPR reference')
        resource = resolve(source,names[-1],resource.parent)
    raise ValueError('cyclic/unsupported building sprite reference')


def scenery_asset_paths(world):
    """Single reference traversal for scoped publication and runtime validation."""
    if world.get('scenery_schema') != 'sgtest.scenery/v1':
        raise ValueError('scenery v1 required; rebuild map presentation metadata')
    definitions = world.get('scenery_definitions', {})
    for row in world.get('objects', []):
        definition = definitions.get(row.get('definition'))
        if not definition or row.get('atlas') != definition['appearances'][0]['atlas']:
            raise ValueError('missing/mismatched scenery definition')
    return {appearance['atlas'] for definition in definitions.values()
            for appearance in definition['appearances'] if appearance is not None}


def build(source, output, map_id):
    started = time.perf_counter()
    source, output = source.resolve(), output.resolve()
    config_path = source/f'map/map{map_id}.map'
    ini = configparser.ConfigParser(interpolation=None)
    ini.read_string(plain(config_path).decode('cp950'))
    settings = ini['Map']
    pic_path = resolve(source,settings['Tiles'])
    tile_path = resolve(source,settings['Array'])
    blocking_path = resolve(source,settings['Blocking'])
    pic, tile_bytes, blocking = plain(pic_path), plain(tile_path), plain(blocking_path)
    side,height = dimensions(pic,2)
    if side > 256 or height%side:
        raise ValueError('PIC is not a tile strip')
    w,h = dimensions(tile_bytes,2)
    bw,bh = dimensions(blocking,1)
    if (bw*16,bh*16) != (w*side,h*side):
        raise ValueError('terrain and collision world dimensions disagree')
    tile_ids = struct.unpack_from(f'<{w*h}H',tile_bytes,8)
    used = sorted(set(tile_ids)-{65535})
    if used and used[-1] >= height//side:
        raise ValueError('tile ID outside original PIC')
    columns = max(1,math.ceil(math.sqrt(len(used))))
    rows = max(1,math.ceil(len(used)/columns))
    atlas = Image.new('RGBA',(columns*side,rows*side))
    strip = Image.frombytes('RGB',(side,height),pic[8:],'raw','BGR;16')
    for index,tile in enumerate(used):
        atlas.paste(strip.crop((0,tile*side,side,(tile+1)*side)),(index%columns*side,index//columns*side))
    dest = output/f'maps/{map_id}'
    dest.mkdir(parents=True,exist_ok=True)
    atlas.save(dest/'terrain.png',compress_level=3)
    (dest/'tiles.bin').write_bytes(tile_bytes)
    (dest/'blocking.dat').write_bytes(blocking)
    object_path = resolve(source,settings['List'])
    definitions = {}
    placed = objects(plain(object_path),source,definitions)
    hashes = {p.relative_to(source).as_posix():hashlib.sha256(plain(p)).hexdigest()
              for p in [config_path,pic_path,tile_path,blocking_path,object_path]}
    document = {'version':3,'map_id':map_id,'name':settings.get('Name',''), 'tile_side':side,
                'tile_width':w,'tile_height':h,'collision_cell':16,'size':[w*side,h*side],
                'atlas_grid':[columns,rows],'tile_ids':used,'objects':placed,
                'scenery_schema':'sgtest.scenery/v1','scenery_definitions':definitions,
                'scenery_order':compile_scenery_order(placed,definitions),
                'source_hashes':hashes,
                'tiles_sha256':hashlib.sha256(tile_bytes).hexdigest(),
                'collision_sha256':hashlib.sha256(blocking).hexdigest(),
                'terrain_sha256':hashlib.sha256((dest/'terrain.png').read_bytes()).hexdigest()}
    fingerprint = hashlib.sha256(json.dumps(document,sort_keys=True,ensure_ascii=False).encode()).hexdigest()
    document['fingerprint'] = fingerprint
    write_json(dest/'world.json',document)
    # Decode the actual emitted PNG and compare every referenced tile to PIC.
    checked = Image.open(dest/'terrain.png').convert('RGB')
    for index,tile in enumerate(used):
        rect = (index%columns*side,index//columns*side,(index%columns+1)*side,(index//columns+1)*side)
        if checked.crop(rect).tobytes() != strip.crop((0,tile*side,side,(tile+1)*side)).tobytes():
            raise ValueError(f'tile round-trip mismatch: {tile}')
    return {'map_id':map_id,'tiles':len(used),'tile_references':len(tile_ids),'objects':len(placed),
            'animated_objects':sum(o['frames'] > 1 for o in placed),
            'terrain_rgba_bytes':atlas.width*atlas.height*4,'size':document['size'],
            'pixel_round_trip':True,'fingerprint':fingerprint,'seconds':time.perf_counter()-started}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path)
    parser.add_argument('output',type=Path)
    parser.add_argument('--map',action='append',type=int,dest='maps')
    args = parser.parse_args()
    if (args.source.resolve() == args.output.resolve() or args.source.resolve() in args.output.resolve().parents
            or args.output.resolve() in args.source.resolve().parents):
        parser.error('output must be outside original source')
    results = [build(args.source,args.output,map_id) for map_id in args.maps or [13]]
    write_json(Path('reports/map_validation.json'),{'maps':results,'errors':[]})
    print(json.dumps(results,ensure_ascii=False,indent=2))


if __name__ == '__main__':
    main()
