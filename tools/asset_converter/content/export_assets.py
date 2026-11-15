"""Publish the complete private source catalog into Godot's assets tree.

Physical pages stay unchanged. The v9 adapter exposes source clips to the one
existing AssetStore; verified enemy semantics reuse the normalized compiler.
Unresolved legacy semantics remain explicit instead of invented animations.
"""
from collections import Counter
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil

from ..atlas.archive_audit import audit_archive
from ..source.runtime_spr import unwrap_cde
from .animation_definition import compile_definition
from .runtime_animation_semantics import logical_views
from .asset_paths import sprite_asset_id


def digest(path):
    hasher = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024*1024), b''):
            hasher.update(block)
    return hasher.hexdigest()


def safe_relative(value):
    path = PurePosixPath(value)
    if not value or '\\' in value or ':' in value or path.is_absolute() or any(part in ('.','..') for part in value.split('/')):
        raise ValueError('unsafe asset catalog path: '+value)
    return path


def publish(source: Path, archive: Path, assets: Path) -> dict:
    source, archive, assets = source.resolve(), archive.resolve(), assets.resolve()
    if any(assets == root or assets.is_relative_to(root) or root.is_relative_to(assets) for root in (source,archive)):
        raise ValueError('asset output overlaps source/archive')
    audit = audit_archive(source,archive)
    if audit['status'] != 'pass':
        raise ValueError('complete archive audit failed: '+json.dumps(audit['errors'][:3]))
    index = json.loads((archive/'index.json').read_text(encoding='utf-8'))
    assets.mkdir(parents=True,exist_ok=True)
    owned_path = assets/'catalog/_export_files.json'
    previous = json.loads(owned_path.read_text(encoding='utf-8')).get('files',{}) if owned_path.is_file() else {}
    files, entries, coverage, semantic_counts = {}, {}, {}, Counter()
    categories = {key:{} for key in ['maps','monsters','npcs','players','equipment','pets','mecha','items','effects','ui','audio','data']}

    def target(relative):
        result = assets.joinpath(*safe_relative(relative).parts).resolve()
        if not result.is_relative_to(assets):raise ValueError('asset path escapes output')
        result.parent.mkdir(parents=True,exist_ok=True)
        return result

    def write(relative, raw):
        path = target(relative)
        expected = hashlib.sha256(raw).hexdigest()
        if not path.is_file() or digest(path) != expected:
            temporary = path.with_name(path.name+'.tmp')
            temporary.write_bytes(raw)
            temporary.replace(path)
        files[relative] = expected

    def document(relative, data):
        write(relative,(json.dumps(data,ensure_ascii=False,separators=(',',':'))+'\n').encode())

    def copy(relative, original, expected):
        path = target(relative)
        if not path.is_file() or digest(path) != expected:
            temporary = path.with_name(path.name+'.tmp')
            shutil.copyfile(original,temporary)
            if digest(temporary) != expected:
                temporary.unlink()
                raise ValueError('asset copy/hash mismatch: '+relative)
            temporary.replace(path)
        files[relative] = expected

    def register(asset_id, kind, relative, **metadata):
        if asset_id in entries:raise ValueError('logical asset id collision: '+asset_id)
        entry = {'kind':kind,'file':relative,'ownership':'on_demand',**metadata}
        entries[asset_id] = entry
        categories[asset_id.split('/')[0]][asset_id] = entry

    for row in index['groups']:
        group = row['group']
        original_folder = archive/'sprites'/group
        raw = json.loads((original_folder/'atlas.json').read_text(encoding='utf-8'))
        asset_id, classification = sprite_asset_id(group)
        frames, clips = [], {}
        for number, frame in enumerate(raw['frames']):
            frames.append({'page':frame['page'],'rect':frame['rect'],'size':frame['size'],
                           'origin':frame['origin'],'trim':frame['crop'][:2],
                           'source':frame['source'],'source_frame':frame['source_frame']})
            clip = PurePosixPath(frame['source']).stem.upper()
            clips.setdefault(clip,[]).append(number)
        semantics = {'status':'source_clips_only','timing_verified':False,'direction_mapping_verified':False}
        views = {}
        if group.startswith('enemy/'):
            act = source/(group+'.act')
            if act.is_file():
                try:
                    definition = compile_definition(asset_id,[source/item['path'] for item in raw['sources']],source,'monster',act)
                    views = logical_views(definition)
                    document(asset_id+'/definition.json',definition)
                    semantics = {'status':'normalized_enemy','definition':asset_id+'/definition.json',
                                 'timing_verified':True,'direction_mapping_verified':True}
                except (ValueError,KeyError) as error:
                    semantics['reason'] = str(error)
        elif group.startswith('player/b/standard/'):
            try:
                paths = [source/item['path'] for item in raw['sources']]
                if group.endswith('/b-death'):
                    definition = compile_definition(
                        asset_id,paths,source,'single_view',
                        source/'player/b/b-death.act',act_bindings={'ND':'Effect'})
                else:
                    definition = compile_definition(
                        asset_id,paths,source,'character_part',
                        source/'player/b/boy.act',
                        source/'player/charactionname.dat',
                        source/'player/chardraworder.dat')
                views = logical_views(definition)
                document(asset_id+'/definition.json',definition)
                semantics = {'status':'normalized_player_b_part','definition':asset_id+'/definition.json',
                             'timing_verified':True,'direction_mapping_verified':True}
            except (ValueError,KeyError) as error:
                semantics['reason'] = str(error)
        semantic_counts[semantics['status']] += 1
        manifest = {'version':9,'asset_id':asset_id,'ownership':'on_demand',
                    'source_group':group,'classification':classification,'source_digest':raw['source_digest'],
                    'pages':raw['pages'],'page_sizes':raw['page_sizes'],'page_sha256':raw['page_sha256'],
                    'frames':frames,'clips':clips,'aliases':{},'act':{},'logical_views':views,
                    'sources':raw['sources'],'animation_semantics':semantics}
        for name in raw['pages']:
            copy(asset_id+'/'+name,original_folder/name,raw['page_sha256'][name])
        document(asset_id+'/atlas.json',manifest)
        register(asset_id,'sprite_atlas',asset_id+'/atlas.json',classification=classification)
        for item in raw['sources']:coverage[item['path']] = asset_id

    audio, sidecars, opaque, errors = 0,0,0,[]
    for original in sorted(source.rglob('*')):
        if not original.is_file() or len(original.relative_to(source).parts) < 2:continue
        relative = original.relative_to(source).as_posix()
        if relative in coverage:continue
        suffix = original.suffix.lower()
        if suffix == '.spr':raise ValueError('SPR missing from verified archive: '+relative)
        sidecar_suffix = '.draw_order.json' if original.name.lower().endswith('draworder.dat') else suffix+'.json'
        sidecar = archive/'sidecars'/original.relative_to(source).parent/(original.stem+sidecar_suffix)
        if suffix in ('.act','.sif') or original.name.lower().endswith('draworder.dat'):
            destination = 'data/animations/'+relative+'.json'
            copy(destination,sidecar,digest(sidecar))
            register(destination[:-5],'legacy_definition',destination)
            coverage[relative] = destination[:-5]
            sidecars += 1
            continue
        raw = original.read_bytes()
        source_hash = hashlib.sha256(raw).hexdigest()
        wrapped = raw.startswith(b'C.D.E.')
        unwrapped = False
        try:
            payload = unwrap_cde(raw) if wrapped else raw
            unwrapped = wrapped
        except ValueError as error:
            payload = raw
            errors.append({'source':relative,'error':str(error)})
        if suffix in ('.mp3','.wav') and relative.startswith('audio/'):
            valid = (payload.startswith(b'ID3') or len(payload)>1 and payload[0]==255 and payload[1]&224==224) if suffix=='.mp3' else (len(payload)>=12 and payload[:4]==b'RIFF' and payload[8:12]==b'WAVE')
            if not valid:errors.append({'source':relative,'error':'unsupported audio container header'})
            parts = original.relative_to(source).parts
            folder = 'bgm' if len(parts)>2 and parts[1].lower()=='bgm' else 'sfx'
            destination = 'audio/'+folder+'/'+('/'.join(parts[2:]) if len(parts)>2 else parts[-1])
            write(destination,payload)
            register(destination,'audio',destination,source_sha256=source_hash,
                     cde_unwrapped=unwrapped,validation='container_header_only' if valid else 'unverified')
            asset_id = destination
            audio += 1
        else:
            destination = 'data/source_files/'+relative+'.bin'
            write(destination,payload)
            register(destination[:-4],'source_payload',destination,source_sha256=source_hash,
                     cde_unwrapped=unwrapped,format_status='uninterpreted')
            asset_id = destination[:-4]
            opaque += 1
        coverage[relative] = asset_id

    for category, values in categories.items():
        document('catalog/'+category+'.json',{'schema':'sgtest.asset_catalog/v1','assets':values})
    source_files = {p.relative_to(source).as_posix() for p in source.rglob('*') if p.is_file() and len(p.relative_to(source).parts)>1}
    if source_files != set(coverage):raise ValueError('incomplete asset source coverage')
    catalog = {'schema':'sgtest.asset_catalog_root/v1','loading':'lazy_category_or_direct_id',
               'categories':{key:{'index':'catalog/'+key+'.json','assets':len(value)} for key,value in categories.items()},
               'source_files':len(coverage),'sprite_groups':len(index['groups']),
               'animation_semantics':dict(semantic_counts),'runtime_path_migration_complete':False,
               'opaque_payloads':opaque,'errors':errors}
    document('catalog/assets.json',catalog)
    retained = []
    for relative, expected in previous.items():
        if relative in files:continue
        path = target(relative)
        if path.is_file():
            if digest(path) == expected:path.unlink()
            else:retained.append(relative)
    document('catalog/_source_coverage.json',{'schema':1,'sources':coverage})
    tracking = target('catalog/_export_files.json')
    temporary = tracking.with_name(tracking.name+'.tmp')
    temporary.write_text(json.dumps({'version':1,'files':files},separators=(',',':'))+'\n',encoding='utf-8')
    temporary.replace(tracking)
    return {'status':'pass' if not errors else 'partial','scope':'complete disk export; semantic/runtime migration still partial',
            'source_files':len(coverage),'sprite_groups':len(index['groups']),'sprite_frames':sum(row['frames'] for row in index['groups']),
            'sprite_pages':sum(row['pages'] for row in index['groups']),'audio_files':audio,'sidecars':sidecars,
            'opaque_payloads':opaque,'animation_semantics':dict(semantic_counts),'categories':catalog['categories'],
            'managed_files':len(files),'bytes':sum(target(name).stat().st_size for name in files),
            'modified_stale_files_retained':retained,'errors':errors,'archive_index_sha256':audit['index_sha256'],
            'complete_asset_conversion':False,'runtime_path_migration_complete':False,
            'audio_playback_verified':False,'all_map_semantics_verified':False}
