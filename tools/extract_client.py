"""Read original Data1.dat and overlay loose client patches; no Recovered dependency.

Format: EOF uint32 index offset; index header raw/packed uint32 sizes; LZSS
directory records. LZSS: 4K space-filled ring, write=0xfee, LSB flags,
12-bit offset and 4-bit length+3. Archive paths normalize to lowercase.
"""
import argparse
import hashlib
import json
from pathlib import Path, PureWindowsPath
import shutil
import struct
from build_assets import write_json

LIMIT = 512 * 1024 * 1024
CATEGORIES = {'audio', 'effect', 'enemy', 'interface', 'item', 'ma', 'map', 'ms', 'npc', 'player', 'sr'}


def lzss(data, size):
    if size < 0 or size > LIMIT:
        raise ValueError('invalid LZSS output size')
    ring, output = bytearray(b' '*4096), bytearray()
    at, write, flags = 0, 0xfee, 0
    while len(output) < size:
        flags >>= 1
        if not flags & 0x100:
            if at >= len(data):
                raise ValueError('truncated LZSS flags')
            flags, at = data[at] | 0xff00, at+1
        if flags & 1:
            if at >= len(data):
                raise ValueError('truncated LZSS literal')
            value, at = data[at], at+1
            output.append(value)
            ring[write], write = value, (write+1)&4095
        else:
            if at+2 > len(data):
                raise ValueError('truncated LZSS match')
            lo, hi = data[at:at+2]
            at += 2
            offset, count = lo | ((hi&0xf0)<<4), (hi&15)+3
            for i in range(min(count, size-len(output))):
                value = ring[(offset+i)&4095]
                output.append(value)
                ring[write], write = value, (write+1)&4095
    return bytes(output)


def safe_relative(name):
    path = PureWindowsPath(name)
    if path.root or path.drive or any(p in ('..', '') or ':' in p for p in path.parts):
        raise ValueError(f'unsafe archive path: {name}')
    return Path(*[p.lower() for p in path.parts])


def read_index(handle):
    handle.seek(0, 2)
    size = handle.tell()
    if size < 12:
        raise ValueError('short DAT archive')
    handle.seek(size-4)
    offset = struct.unpack('<I', handle.read(4))[0]
    if offset+8 > size-4:
        raise ValueError('invalid DAT index offset')
    handle.seek(offset)
    raw_size, packed_size = struct.unpack('<II', handle.read(8))
    if offset+8+packed_size > size-4:
        raise ValueError('DAT index extends past archive')
    index = lzss(handle.read(packed_size), raw_size)
    at = 0

    def integer():
        nonlocal at
        value = struct.unpack_from('<I', index, at)[0]
        at += 4
        return value

    def string():
        nonlocal at
        end = index.index(b'\0', at)
        raw = index[at:end]
        at = end+1
        return raw.decode('cp950', errors='strict')

    result = []
    for _ in range(integer()):
        count, folder = integer(), string()
        for _ in range(count):
            original, compressed, start = integer(), integer(), integer()
            path = safe_relative(str(PureWindowsPath(folder) / string()))
            if original > LIMIT or start+(compressed or original) > offset:
                raise ValueError(f'invalid payload range: {path}')
            result.append((path, original, compressed, start))
    if at != len(index):
        raise ValueError('unexpected trailing DAT directory data')
    return result


def extract(client, output):
    client, output = client.resolve(), output.resolve()
    if client == output or client in output.parents or output in client.parents:
        raise ValueError('output must be separate from original client')
    archive = client/'Data1.dat'
    output.mkdir(parents=True, exist_ok=True)
    checkpoint = output/'extraction.json'
    old = json.loads(checkpoint.read_text(encoding='utf-8')) if checkpoint.exists() else {}
    digest = hashlib.sha256()
    with archive.open('rb') as archive_handle:
        for block in iter(lambda: archive_handle.read(1024*1024), b''):
            digest.update(block)
    archive_sha = digest.hexdigest()
    cached = old.get('archive_sha256') == archive_sha
    count = 0
    with archive.open('rb') as handle:
        entries = read_index(handle)
        for path, original, compressed, start in entries:
            target = output/path
            if cached and target.is_file() and target.stat().st_size == original:
                continue
            handle.seek(start)
            data = handle.read(compressed or original)
            if compressed:
                data = lzss(data, original)
            if len(data) != original:
                raise ValueError(f'short DAT payload: {path}')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            count += 1
            if count % 2500 == 0:
                print(f'extracted={count}/{len(entries)}', flush=True)
    overlays = 0
    for directory in client.iterdir():
        if directory.is_dir() and directory.name.lower() in CATEGORIES:
            for path in directory.rglob('*'):
                if path.is_file() and path.suffix.lower() not in ('.exe', '.dll', '.py', '.pyc'):
                    target = output/safe_relative(str(path.relative_to(client)))
                    target.parent.mkdir(parents=True, exist_ok=True)
                    if not target.exists() or target.read_bytes() != path.read_bytes():
                        shutil.copyfile(path, target)
                    overlays += 1
                    if overlays % 10000 == 0:
                        print(f'loose_overlays={overlays}', flush=True)
    for path in client.iterdir():
        if path.is_file() and path.suffix.lower() in ('.spr', '.act'):
            shutil.copyfile(path, output/path.name.lower())
    report = {'archive_sha256': archive_sha, 'archive_entries': len(entries),
              'extracted': count, 'loose_overlays': overlays, 'client': str(client)}
    write_json(checkpoint, report)
    print(json.dumps(report, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('client', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    extract(args.client, args.output)
