"""Preserve original audio/tables/maps and convert standalone images for Godot.

Map/table bytes are preserved, not claimed to be parsed by the prototype.
"""
import argparse
import hashlib
import io
from pathlib import Path
import shutil
from PIL import Image
from build_assets import write_json
from runtime_spr import unwrap_cde


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    source, output = args.source.resolve(), args.output.resolve()
    if source == output or source in output.parents or output in source.parents:
        parser.error('source and output must be separate')
    records, errors = [], []
    for path in sorted(source.rglob('*')):
        if not path.is_file():
            continue
        extension = path.suffix.lower()
        relative = path.relative_to(source)
        if extension in ('.wav','.mp3','.ogg'):
            dest = output/'audio'/relative
        elif extension in ('.tga','.jpg','.jpeg','.bmp','.png'):
            dest = (output/'images'/relative).with_suffix('.png')
        elif extension in ('.csv','.itd','.dat','.map','.itf','.sif','.sth','.blg','.bin','.txt','.act'):
            dest = output/'data'/relative
        else:
            continue
        try:
            dest.parent.mkdir(parents=True, exist_ok=True)
            raw = path.read_bytes()
            decoded = unwrap_cde(raw) if raw.startswith(b'C.D.E.') else raw
            if extension in ('.tga','.jpg','.jpeg','.bmp','.png'):
                with Image.open(io.BytesIO(decoded)) as image:
                    image.convert('RGBA').save(dest)
            else:
                dest.write_bytes(decoded)
            records.append({'source': relative.as_posix(), 'output': dest.relative_to(output).as_posix(),
                            'sha256': hashlib.sha256(raw).hexdigest(), 'cde_unwrapped': raw != decoded})
        except (OSError, ValueError) as exc:
            errors.append({'source': relative.as_posix(), 'error': str(exc)})
    write_json(output/'support_catalog.json', {'files': records, 'errors': errors})
    print(f'support assets={len(records)} errors={len(errors)}')
    return bool(errors)


if __name__ == '__main__':
    raise SystemExit(main())
