"""Compare extraction inputs by bytes, without modifying either client."""
import argparse
import hashlib
import json
from pathlib import Path
from extract_client import CATEGORIES

def inputs(root):
    result = {'data1.dat': root/'Data1.dat'}
    for folder in root.iterdir():
        if folder.is_dir() and folder.name.lower() in CATEGORIES:
            for file in folder.rglob('*'):
                if file.is_file():
                    result[file.relative_to(root).as_posix().lower()] = file
    return result

def digest(path):
    value = hashlib.sha256()
    with path.open('rb') as file:
        for block in iter(lambda:file.read(1024*1024),b''):
            value.update(block)
    return value.hexdigest()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('previous',type=Path)
    parser.add_argument('original',type=Path)
    args = parser.parse_args()
    before,after = inputs(args.previous),inputs(args.original)
    changed = []
    identical = 0
    for name in sorted(before.keys() & after.keys()):
        a,b = before[name],after[name]
        if a.stat().st_size == b.stat().st_size and digest(a) == digest(b):
            identical += 1
        else:
            changed.append(name)
    report = {'previous':str(args.previous.resolve()),'original':str(args.original.resolve()),
              'identical':identical,'changed':changed,'added':sorted(after.keys()-before.keys()),
              'removed':sorted(before.keys()-after.keys()),'archive_sha256':digest(after['data1.dat'])}
    Path('reports/source_comparison.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v if not isinstance(v,list) else {'count':len(v),'first':v[:12]} for k,v in report.items()},ensure_ascii=False,indent=2))

if __name__ == '__main__':
    main()
