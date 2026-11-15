"""Verify complete archive provenance/geometry without allocating decoded pages.

Pixel equality was checked by build_group and is bound to source/decoder/page
and metadata hashes. This audit does not claim to replay that pixel comparison.
"""
import hashlib
import json
from pathlib import Path
from PIL import Image

from .dense_atlas import _is_cached, _source_fingerprint, DECODER_SHA256
from .layout import select_groups
from ..source.convert_assets import sidecar_document


def audit_archive(source: Path, archive: Path) -> dict:
    source,archive=Path(source).resolve(),Path(archive).resolve()
    index_path=archive/'index.json'
    index=json.loads(index_path.read_text(encoding='utf-8'))
    if not isinstance(index,dict):raise ValueError('invalid index document type')
    expected=select_groups(source)
    rows=index.get('groups',[])
    recorded={row['group'] for row in rows}
    indexed={row['group']:row for row in rows}
    errors=[]
    if len(rows)!=len(recorded):
        errors.append({'index':'duplicate source group records'})
    if index.get('status')!='complete' or index.get('source_scope')!='all SPR' or index.get('errors'):
        errors.append({'index':'full, error-free completed index required'})
    wanted={key.as_posix() for key in expected}
    if recorded!=wanted or index.get('groups_expected')!=len(wanted):
        errors.append({'index':'source group set differs','missing':sorted(wanted-recorded),
                       'extra':sorted(recorded-wanted)})
    counts={'groups':0,'sources':0,'frames':0,'pages':0,'rgba_bytes':0,'png_bytes':0}
    for key,paths in expected.items():
        folder=archive/'sprites'/key
        try:
            data=json.loads((folder/'atlas.json').read_text(encoding='utf-8'))
            if not isinstance(data,dict):raise ValueError('invalid atlas document type')
            fingerprint,_=_source_fingerprint(source,paths)
            if not _is_cached(folder,data,fingerprint) or data.get('decoder_sha256')!=DECODER_SHA256 or not data.get('metadata_sha256'):
                raise ValueError('source/decoder/metadata/page integrity mismatch')
            relative=[path.relative_to(source).as_posix() for path in paths]
            if [row['path'] for row in data['sources']]!=relative:
                raise ValueError('source ordering differs')
            identities=[(row['path'],frame) for row in data['sources'] for frame in range(row['frames'])]
            if [(row['source'],row['source_frame']) for row in data['frames']]!=identities:
                raise ValueError('missing, duplicated or reordered source frame identities')
            if len(data['page_sizes'])!=len(data['pages']):
                raise ValueError('page dimensions missing')
            for name,size in zip(data['pages'],data['page_sizes']):
                with Image.open(folder/name) as image:
                    if list(image.size)!=size or image.format!='PNG' or max(size)>4096:
                        raise ValueError('PNG header/dimension mismatch')
            rgba=sum(width*height*4 for width,height in data['page_sizes'])
            png_bytes=sum((folder/name).stat().st_size for name in data['pages'])
            actual={'frames':len(data['frames']),'pages':len(data['pages']),
                    'rgba_bytes':rgba,'png_bytes':png_bytes}
            if any(indexed.get(key.as_posix(),{}).get(field)!=value
                   for field,value in actual.items()):
                raise ValueError('index group counts differ from verified manifest/pages')
            if data.get('group')!=key.as_posix() or data.get('png_bytes')!=png_bytes:
                raise ValueError('manifest group identity or PNG byte count differs')
            selected=next(row for row in data['candidates'] if row['side']==data['atlas_side'])
            if (not selected['valid'] or selected['pages']!=len(data['pages']) or
                selected['rgba_bytes']!=rgba or data['rgba_bytes']!=rgba):
                raise ValueError('candidate cost differs from actual page geometry')
            for frame in data['frames']:
                if frame['page']<0:
                    if frame['crop']!=[0,0,0,0]:raise ValueError('nonempty frame has no page')
                    continue
                x,y,w,h=frame['rect']
                pw,ph=data['page_sizes'][frame['page']]
                if min(x,y)<0 or min(w,h)<=0 or x+w>pw or y+h>ph:
                    raise ValueError('frame outside page')
            counts['groups']+=1
            counts['sources']+=len(paths)
            counts['frames']+=len(data['frames'])
            counts['pages']+=len(data['pages'])
            counts['rgba_bytes']+=rgba
            counts['png_bytes']+=png_bytes
        except (OSError,ValueError,KeyError,TypeError,IndexError,StopIteration) as error:
            errors.append({'group':key.as_posix(),'error':str(error)})
    sidecars={'.act':0,'.sif':0,'draw_order':0}
    for path in source.rglob('*'):
        if not path.is_file():continue
        suffix=path.suffix.casefold()
        if suffix in ('.act','.sif'):
            kind,extension=suffix,suffix+'.json'
        elif path.name.casefold().endswith('draworder.dat'):
            kind,extension='draw_order','.draw_order.json'
        else:continue
        relative=path.relative_to(source)
        target=archive/'sidecars'/relative.parent/(path.stem+extension)
        try:
            data=json.loads(target.read_text(encoding='utf-8'))
            if data!=sidecar_document(path,source,kind):
                raise ValueError('sidecar differs from decoded source metadata')
            sidecars[kind]+=1
        except (OSError,ValueError,TypeError) as error:
            errors.append({'source':relative.as_posix(),'error':str(error)})
    for kind,count in sidecars.items():
        declared=index.get('sidecars',{}).get(kind,{})
        if declared.get('found')!=count or declared.get('ok')!=count:
            errors.append({'sidecar':kind,'error':'verified count differs from index'})
    return {'status':'pass' if not errors else 'failed', 'counts':counts,'expected_groups':len(expected),
            'index_sha256':hashlib.sha256(index_path.read_bytes()).hexdigest(),
            'decoder_sha256':DECODER_SHA256,'sidecars':index.get('sidecars',{}),
            'sidecars_verified':sidecars,'errors':errors,
            'scope':'source/cache integrity + frame identities + PNG geometry + source sidecar replay; builder pixel comparison not replayed'}
