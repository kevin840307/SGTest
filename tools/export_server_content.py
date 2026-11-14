"""Export only collision and map contract; server never receives textures."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,default=Path('client/assets/generated'))
    parser.add_argument('--output',type=Path,default=Path('server/content'))
    args=parser.parse_args()
    for manifest in sorted((args.source/'maps').glob('*/world.json')):
        data=json.loads(manifest.read_text(encoding='utf-8'))
        collision=manifest.with_name('blocking.bin')
        if hashlib.sha256(collision.read_bytes()).hexdigest()!=data['collision_sha256']:
            raise ValueError(f'collision hash mismatch: {manifest}')
        dest=args.output/'maps'/manifest.parent.name
        dest.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(collision,dest/'blocking.bin')
        # This minimal contract matches the client's full manifest fingerprint.
        (dest/'world.json').write_text(json.dumps({k:data[k] for k in ['version','fingerprint','collision_sha256']},indent=2),encoding='utf-8')
        print(f'exported server collision/map contract: {dest}')

if __name__=='__main__': main()
