"""Publish a complete or scoped private catalog into Godot's assets tree.

Frame placement stays unchanged. Runtime pages receive transparent right/bottom
alignment to 4x4 codec blocks without resampling. The v9 adapter exposes clips to the one
future AssetStore; verified enemy semantics reuse the normalized compiler.
Unresolved legacy semantics remain explicit instead of invented animations.
"""
from collections import Counter
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import tempfile

from PIL import Image

from ..atlas.archive_audit import audit_archive
from ..source.runtime_spr import unwrap_cde
from ..source.scenery import compile_scenery_action
from .animation_definition import compile_definition
from .runtime_animation_semantics import logical_views
from .asset_paths import sprite_asset_id, source_sprite_manifest


PLAYER_PART_ASSETS = (
    'players/b/base/body', 'players/b/base/leg', 'players/b/base/head',
    'players/b/hair/hair00', 'players/b/base/left_arm',
    'players/b/base/right_arm', 'players/b/base/death',
)

PAGE_LAYOUT = 'height_dense_p0_block4_v1'


def runtime_page(payload: bytes, expected_size: list[int]) -> tuple[bytes, list[int]]:
    """Keep every source texel/rect; only extend the bottom/right page boundary.

    Godot's BC7/ASTC importers otherwise resize a non-block-aligned image. A
    FrameTable refers to original pixel coordinates, so that resize is unsafe.
    This does not extrude frames or change the verified source Archive.
    """
    with Image.open(io.BytesIO(payload)) as source:
        if list(source.size) != expected_size:
            raise ValueError('runtime page source dimension mismatch')
        size = [(side+3)//4*4 for side in source.size]
        if size == expected_size:
            return payload, size
        page = Image.new('RGBA',tuple(size))
        page.paste(source.convert('RGBA'),(0,0))
        stream = io.BytesIO()
        page.save(stream,format='PNG',compress_level=3)
    return stream.getvalue(), size


def map_asset_ids(source: Path, server_dat: Path, map_ids: list[int]) -> set[str]:
    """Follow original map references using the existing content compilers.

    This selects visual data only; it does not invent NPC timing or combat rules.
    """
    from .build_maps import build as build_map, scenery_asset_paths
    from .npc_content import build as build_npcs
    from .build_population import compile_spawn_evidence

    result = set(PLAYER_PART_ASSETS)
    with tempfile.TemporaryDirectory(prefix='sgtest-asset-selection-') as scratch:
        stage = Path(scratch)
        for map_id in sorted(map_ids):
            build_map(source, stage, map_id)
            folder = stage / f'maps/{map_id}'
            world = json.loads((folder/'world.json').read_text(encoding='utf-8'))
            result.update(path.removesuffix('/atlas.json') for path in scenery_asset_paths(world))
            build_npcs(source, server_dat, map_id, folder/'npcs.json')
            npcs = json.loads((folder/'npcs.json').read_text(encoding='utf-8'))
            for row in npcs['npcs']:
                visual = row['visual']
                if visual['resolved']:
                    result.add(source_sprite_manifest(visual['source']).removesuffix('/atlas.json'))
                elif visual['reason'] != 'empty_primary_slot':
                    raise ValueError(f'missing NPC visual for map {map_id}: {visual}')
            population = compile_spawn_evidence(source, server_dat, map_id)
            result.update(row['atlas'].removesuffix('/atlas.json') for row in population['definitions'])
    return result


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


def publish(source: Path, archive: Path, assets: Path, asset_ids: set[str] | None = None) -> dict:
    source, archive, assets = source.resolve(), archive.resolve(), assets.resolve()
    if any(assets == root or assets.is_relative_to(root) or root.is_relative_to(assets) for root in (source,archive)):
        raise ValueError('asset output overlaps source/archive')
    audit = audit_archive(source,archive)
    if audit['status'] != 'pass':
        raise ValueError('complete archive audit failed: '+json.dumps(audit['errors'][:3]))
    index = json.loads((archive/'index.json').read_text(encoding='utf-8'))
    groups = index['groups']
    if asset_ids is not None:
        available = {sprite_asset_id(row['group'])[0] for row in groups}
        if not asset_ids or asset_ids - available:
            raise ValueError('empty or unknown selected asset IDs: '+str(sorted(asset_ids - available)))
        groups = [row for row in groups if sprite_asset_id(row['group'])[0] in asset_ids]
    assets.mkdir(parents=True,exist_ok=True)
    owned_path = assets/'catalog/_export_files.json'
    previous = json.loads(owned_path.read_text(encoding='utf-8')).get('files',{}) if owned_path.is_file() else {}
    files, entries, coverage, semantic_counts = {}, {}, {}, Counter()
    categories = {key:{} for key in ['maps','Enemy','npcs','players','equipment','pets','mecha','items','effects','ui','audio','data']}

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

    for row in groups:
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
        views, act_timelines = {}, {}
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
        elif group.startswith('map/'):
            act_path = source/(group+'.act')
            if act_path.is_file():
                if len(clips) != 1:
                    raise ValueError('scenery ACT requires one source clip: '+group)
                clip = next(iter(clips))
                payload = act_path.read_bytes()
                payload = unwrap_cde(payload) if payload.startswith(b'C.D.E.') else payload
                timeline = compile_scenery_action(payload,len(clips[clip]))
                timeline['source'] = act_path.relative_to(source).as_posix()
                timeline['source_sha256'] = hashlib.sha256(act_path.read_bytes()).hexdigest()
                act_timelines[clip] = timeline
                semantics = {'status':'normalized_scenery_act','timing_verified':True,
                             'direction_mapping_verified':False,'timing_scope':'default_25_only'}
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
        page_sizes, page_hashes = [], {}
        for name, size in zip(raw['pages'],raw['page_sizes']):
            payload = (original_folder/name).read_bytes()
            if hashlib.sha256(payload).hexdigest() != raw['page_sha256'][name]:
                raise ValueError('archive page changed during publication: '+group+'/'+name)
            payload, aligned_size = runtime_page(payload,size)
            write(asset_id+'/'+name,payload)
            page_sizes.append(aligned_size)
            page_hashes[name] = hashlib.sha256(payload).hexdigest()
        manifest = {'version':9,'asset_id':asset_id,'ownership':'on_demand',
                    'source_group':group,'classification':classification,'source_digest':raw['source_digest'],
                    'pages':raw['pages'],'page_sizes':page_sizes,'page_sha256':page_hashes,
                    'page_layout':PAGE_LAYOUT,'archive_page_sizes':raw['page_sizes'],
                    'archive_page_sha256':raw['page_sha256'],
                    'frames':frames,'clips':clips,'aliases':{},'act':act_timelines,'logical_views':views,
                    'sources':raw['sources'],'animation_semantics':semantics}
        document(asset_id+'/atlas.json',manifest)
        register(asset_id,'sprite_atlas',asset_id+'/atlas.json',classification=classification)
        for item in raw['sources']:coverage[item['path']] = asset_id

    audio, sidecars, opaque, errors = 0,0,0,[]
    for original in sorted(source.rglob('*')):
        if not original.is_file() or len(original.relative_to(source).parts) < 2:continue
        relative = original.relative_to(source).as_posix()
        if relative in coverage:continue
        if asset_ids is not None:continue  # Scoped visuals include only selected SPR and compiled definitions.
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
    source_files = ({item['path'] for row in groups
                     for item in json.loads((archive/'sprites'/row['group']/'atlas.json').read_text(encoding='utf-8'))['sources']}
                    if asset_ids is not None else
                    {p.relative_to(source).as_posix() for p in source.rglob('*') if p.is_file() and len(p.relative_to(source).parts)>1})
    if source_files != set(coverage):raise ValueError('incomplete asset source coverage')
    catalog = {'schema':'sgtest.asset_catalog_root/v1','loading':'lazy_category_or_direct_id',
               'categories':{key:{'index':'catalog/'+key+'.json','assets':len(value)} for key,value in categories.items()},
               'source_scope':'selected_sprite_assets' if asset_ids is not None else 'all_source_files',
               'source_files':len(coverage),'sprite_groups':len(groups),
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
    return {'status':'pass' if not errors else 'partial','scope':catalog['source_scope']+'; semantic/runtime migration still partial',
            'source_files':len(coverage),'sprite_groups':len(groups),'sprite_frames':sum(row['frames'] for row in groups),
            'sprite_pages':sum(row['pages'] for row in groups),'audio_files':audio,'sidecars':sidecars,
            'opaque_payloads':opaque,'animation_semantics':dict(semantic_counts),'categories':catalog['categories'],
            'managed_files':len(files),'bytes':sum(target(name).stat().st_size for name in files),
            'modified_stale_files_retained':retained,'errors':errors,'archive_index_sha256':audit['index_sha256'],
            'complete_asset_conversion':False,'runtime_path_migration_complete':False,
            'audio_playback_verified':False,'all_map_semantics_verified':False}
