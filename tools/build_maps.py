"""Build compact original terrain atlases, object references and collision grids.

Map INI selects PIC (uint32 width/height, RGB565 strip), MapBlock (uint32
width/height then uint16 tile IDs; 65535 empty), Blocking (uint32 width/height
then one byte/cell), and ObjectList (uint32 count, byte-length name + 3 int32).
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
from build_assets import write_json
from runtime_spr import decode_spr, unwrap_cde


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


def objects(raw, source, bundle_relative):
    count = struct.unpack_from('<I',raw)[0]
    at, result = 4, []
    decoded_cache = {}
    for _ in range(count):
        length = raw[at]
        at += 1
        name = raw[at:at+length].decode('cp950')
        at += length
        x,y,z = struct.unpack_from('<iii',raw,at)
        at += 12
        resource = resolve(source,'map/'+name)
        sprite = object_sprite(source,resource)
        relative = sprite.relative_to(source)
        key = relative.as_posix()
        cached = decoded_cache.get(key)
        if cached is None:
            decoded = decode_spr(sprite.read_bytes())
            local_boxes = []
            for frame in decoded:
                crop = frame.image.getbbox()
                if crop:
                    left, top, right, bottom = crop
                    local_boxes.append((left-frame.origin_x, top-frame.origin_y,
                                        right-frame.origin_x, bottom-frame.origin_y))
            cached = (sprite.stem.upper(), local_boxes, len(decoded))
            decoded_cache[key] = cached
        clip, local_boxes, frame_count = cached
        left = min((x+b[0] for b in local_boxes), default=x)
        top = min((y+b[1] for b in local_boxes), default=y)
        right = max((x+b[2] for b in local_boxes), default=x)
        bottom = max((y+b[3] for b in local_boxes), default=y)
        result.append({'atlas':bundle_relative,'clip':clip,'p':[x,y], 'z':z,
                       'bounds':[left,top,right-left,bottom-top],'frames':frame_count,
                       'source':key})
    if raw[at:] not in (b'',b'\0\0\0\0'):
        raise ValueError('unexpected ObjectList trailer')
    return result


def object_sprite(source, resource):
    # BLG/STH store authoring absolute paths followed by the runtime relative
    # reference. Follow the latter: differently named BLGs can share a sprite.
    for _ in range(4):
        extension = resource.suffix.lower()
        if extension == '.spr':
            return resource
        data, at, names = plain(resource), 0, []
        for _ in range(1 if extension == '.sif' else 2):
            size = data[at]
            at += 1
            names.append(data[at:at+size].decode('cp950'))
            at += size
        resource = resolve(source,names[-1],resource.parent)
    raise ValueError('cyclic/unsupported building sprite reference')


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
    (dest/'blocking.bin').write_bytes(blocking)
    object_path = resolve(source,settings['List'])
    bundle_relative = f'bundles/map/{map_id}/sprites.json'
    placed = objects(plain(object_path),source,bundle_relative)
    hashes = {p.relative_to(source).as_posix():hashlib.sha256(plain(p)).hexdigest()
              for p in [config_path,pic_path,tile_path,blocking_path,object_path]}
    document = {'version':2,'map_id':map_id,'name':settings.get('Name',''), 'tile_side':side,
                'tile_width':w,'tile_height':h,'collision_cell':16,'size':[w*side,h*side],
                'atlas_grid':[columns,rows],'tile_ids':used,'objects':placed,
                'sprite_bundle':bundle_relative,'source_hashes':hashes,
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
