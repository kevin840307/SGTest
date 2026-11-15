"""Single-entry SGTest legacy asset converter. Run from any working directory."""

from __future__ import annotations
import argparse, hashlib, json, os, re, shutil, sys, tempfile, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.atlas.dense_atlas import build_group, POLICY
from asset_converter.atlas.layout import select_groups
from asset_converter.atlas.archive_audit import audit_archive
from asset_converter.operation_lock import converter_operation, ConverterBusy
from asset_converter.json_files import write_json
from asset_converter.source.convert_assets import (
    convert_act,
    convert_draw_order,
    convert_sif,
)
from asset_converter.source.extract_client import extract, audit_sprite_coverage
from asset_converter.content.build_maps import build as build_map
from asset_converter.content.npc_content import build as build_npcs
from asset_converter.content.export_assets import publish as export_assets, map_asset_ids, PLAYER_PART_ASSETS
from asset_converter.content.server_source import resolve_server_dat
from asset_converter.content.build_maps import server_map_directory
from asset_converter.content.build_enemy_map_palette import read_enemy_catalog, read_enemy_dat
from asset_converter.content.build_population import compile_population, compile_spawn_evidence
from asset_converter.content.asset_paths import source_sprite_manifest


def resolve_source(args) -> Path:
    if args.extracted_source:
        result = args.extracted_source.resolve()
        if not result.is_dir():
            raise ValueError("missing extracted source " + str(result))
        return result
    original = args.original.resolve()
    if not original.is_dir():
        raise ValueError("original game directory missing: " + str(original))
    if (original / "Data1.dat").is_file():
        cache = (REPO / ".cache" / "asset_converter" / "source").resolve()
        if cache == original or original in cache.parents:
            raise ValueError("extraction cache overlaps original directory")
        print("[EXTRACT] Data1.dat plus loose overlays", flush=True)
        extract(original, cache)
        return cache
    if list(original.rglob("*.spr"))[:1]:
        print("[SOURCE] already-extracted SPR tree", flush=True)
        return original
    raise ValueError("input does not contain Data1.dat or extracted SPR files")


def archive_all(source: Path, archive: Path, args) -> dict:
    if archive == source or archive in source.parents or source in archive.parents:
        raise ValueError("archive output must not overlap original source")
    keys = select_groups(source)
    if args.category:
        wanted = {x.casefold() for x in args.category}
        keys = {
            key: files
            for key, files in keys.items()
            if key.parts[0].casefold() in wanted
        }
    prefix = getattr(args, "prefix", None)
    if prefix:
        keys = {
            key: files
            for key, files in keys.items()
            if key.as_posix().casefold().startswith(prefix.casefold())
        }
    if args.preview_groups:
        keys = dict(list(keys.items())[: args.preview_groups])
    summary = {
        "packing_policy": POLICY,
        "source": str(source),
        "groups_expected": len(keys),
        "groups": [],
        "errors": [],
        "source_scope": "filtered"
        if (args.preview_groups or args.category or prefix)
        else "all SPR",
        "status":"running",
    }
    total = len(keys)
    write_json(archive / 'checkpoint.json',summary)
    for n, (key, files) in enumerate(keys.items(), 1):
        try:
            row = build_group(source, archive / "sprites", key, files, args.force)
            summary["groups"].append(row)
        except Exception as exc:
            summary["errors"].append({"group": key.as_posix(), "error": str(exc)})
        if n % 50 == 0 or n == total:
            print(
                f"[HEIGHT DENSE] {n}/{total} groups; failures={len(summary['errors'])}",
                flush=True,
            )
            write_json(
                archive / "progress.json",
                {
                    "done": n,
                    "total": total,
                    "failures": len(summary["errors"]),
                },
            )
            write_json(archive / 'checkpoint.json',summary)
    if (
        not args.preview_groups
        and not args.category
        and not prefix
        and not args.skip_sidecars
    ):
        counters = {}
        for extension, func in (
            (".act", convert_act),
            (".sif", convert_sif),
        ):
            sources = sorted(
                p
                for p in source.rglob("*")
                if p.is_file() and p.suffix.lower() == extension
            )
            done = 0
            for path in sources:
                try:
                    func(path, source, archive / "sidecars", args.force)
                    done += 1
                except Exception as exc:
                    summary["errors"].append(
                        {
                            "source": path.relative_to(source).as_posix(),
                            "error": str(exc),
                        }
                    )
            counters[extension] = {"found": len(sources), "ok": done}
        order = sorted(
            p
            for p in source.rglob("*")
            if p.is_file() and p.name.lower().endswith("draworder.dat")
        )
        done = 0
        for p in order:
            try:
                convert_draw_order(p, source, archive / "sidecars", args.force)
                done += 1
            except Exception as exc:
                summary["errors"].append(
                    {"source": p.relative_to(source).as_posix(), "error": str(exc)}
                )
        counters["draw_order"] = {"found": len(order), "ok": done}
        summary["sidecars"] = counters
    summary["total_pages"] = sum(row["pages"] for row in summary["groups"])
    summary["total_frames"] = sum(row["frames"] for row in summary["groups"])
    summary["rgba_bytes"] = sum(row["rgba_bytes"] for row in summary["groups"])
    summary["png_bytes"] = sum(row["png_bytes"] for row in summary["groups"])
    summary["cached_groups"] = sum(
        row["status"] == "cached" for row in summary["groups"]
    )
    summary['revalidated_groups'] = sum(row['status']=='revalidated' for row in summary['groups'])
    summary['status'] = 'partial' if summary['errors'] else 'complete'
    write_json(archive / 'checkpoint.json',summary)
    write_json(archive / "index.json", summary)
    return summary


def check_runtime(stage: Path, assets: Path, map_id: int) -> dict:
    world_path = stage / f"maps/{map_id}/world.json"
    population_path = stage / f"maps/{map_id}/population.json"
    world = json.loads(world_path.read_text(encoding="utf-8"))
    if world.get('version') != 3 or world.get('map_id') != map_id:
        raise ValueError('world v3 required; rebuild v2 scenery hierarchy from original ObjectList')
    population = json.loads(population_path.read_text(encoding="utf-8"))
    npc_document = json.loads((stage/f'maps/{map_id}/npcs.json').read_text(encoding='utf-8'))
    if npc_document.get('schema') != 'sgtest.npcs/v1' or npc_document.get('map_id') != map_id:
        raise ValueError('NPC content schema/map identity differs')
    if (population.get('version') != 3 or population.get('map_id') != map_id
            or population.get('kind') != 'visual_spawn_content'
            or population.get('stats_verified') is not False or population.get('gameplay_ready') is not False
            or any(row.get('stats', 'missing') is not None or row.get('stats_verified') is not False
                   for row in population.get('definitions', []))):
        raise ValueError('population v3 must explicitly withhold unverified gameplay stats; rebuild v2 content')
    npc_paths = set()
    for row in npc_document['npcs']:
        visual = row['visual']
        if not visual['resolved']:
            if visual.get('reason') == 'empty_primary_slot':
                continue
            raise ValueError(f'unresolved NPC visual: {visual}')
        relative = source_sprite_manifest(visual['source'])
        if visual.get('atlas') != relative:
            raise ValueError('NPC atlas differs from original source mapping')
        if not (assets/relative).is_file():
            raise ValueError(f'missing independent NPC asset: {relative}')
        npc_paths.add(relative)
    if "sprite_bundle" in world:
        raise ValueError("map contract still contains a cross-asset sprite bundle")
    collision = stage / f"maps/{map_id}/blocking.dat"
    if hashlib.sha256(collision.read_bytes()).hexdigest() != world["collision_sha256"]:
        raise ValueError("map collision is not the verified original byte grid")
    object_paths = sorted({str(row.get("atlas","")) for row in world.get("objects",[])})
    for relative in object_paths:
        if not relative.startswith("maps/visuals/") or not relative.endswith("/atlas.json"):
            raise ValueError(f"map object is not an independent scenery asset: {relative}")
        if not (assets / relative).is_file():
            raise ValueError(f"missing independent map object asset: {relative}")
    monster_paths = sorted({str(row.get("atlas","")) for row in population.get("definitions",[])})
    for relative in monster_paths:
        if not relative.startswith("Enemy/") or not relative.endswith("/atlas.json"):
            raise ValueError(f"monster is not an independent asset: {relative}")
        if not (assets / relative).is_file():
            raise ValueError(f"missing independent monster asset: {relative}")
    player_paths = [asset_id+'/atlas.json' for asset_id in PLAYER_PART_ASSETS]
    for relative in player_paths:
        if not (assets / relative).is_file():
            raise ValueError(f"missing independent player part asset: {relative}")
    return {
        "map_objects": len(world.get("objects",[])),
        "map_visual_assets": len(object_paths),
        "monster_definitions": len(population.get("definitions",[])),
        "monster_visual_assets": len(monster_paths),
        "npcs": len(npc_document['npcs']),
        "npc_visual_assets": len(npc_paths),
        "combat_stats_verified": False,
        "gameplay_ready": False,
        "player_part_assets": len(player_paths),
        "collision_format": "original_blocking_byte_grid",
        "cross_asset_bundle": False,
    }


def build_runtime_assets(source: Path, output: Path, args) -> dict:
    try:
        server_dat = resolve_server_dat(args.server_dat)
    except ValueError as exc:
        return {"status": "not_built", "reason": str(exc)}
    if output == source or output in source.parents or source in output.parents:
        raise ValueError("runtime output must not overlap original source")
    catalog = output / "catalog/assets.json"
    if not catalog.is_file():
        raise ValueError("independent asset catalog missing; publish assets before runtime map contracts")
    stage_parent = REPO / ".cache" / "asset_converter"
    stage_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="stage-", dir=stage_parent) as scratch:
        stage = Path(scratch)
        print(f"[RUNTIME] map {args.map} contract; scenery/monster/player assets stay independent", flush=True)
        map_result = build_map(source, stage, args.map)
        npc_result = build_npcs(source, server_dat, args.map, stage/f'maps/{args.map}/npcs.json')
        map_dir = server_map_directory(server_dat,args.map)
        enemy_dat = map_dir / "Enemy.dat"
        enemy_catalog = server_dat / "Enemy" / "Enemy_Data.csv"
        population = compile_population(
            read_enemy_dat(enemy_dat),read_enemy_catalog(enemy_catalog),output,args.map
        )
        population["sources"] = {
            enemy_dat.relative_to(server_dat).as_posix(): hashlib.sha256(enemy_dat.read_bytes()).hexdigest(),
            enemy_catalog.relative_to(server_dat).as_posix(): hashlib.sha256(enemy_catalog.read_bytes()).hexdigest(),
        }
        write_json(stage / f"maps/{args.map}/population.json",population)
        validated = check_runtime(stage,output,args.map)
        files = [p for p in stage.rglob("*") if p.is_file()]
        files.sort(key=lambda p: (p.suffix.lower() == ".json", p.as_posix()))
        for p in files:
            relative = p.relative_to(stage)
            dest = output / relative
            dest.parent.mkdir(parents=True, exist_ok=True)
            temporary = dest.with_name(dest.name + ".asset_converter.tmp")
            shutil.copyfile(p, temporary)
            temporary.replace(dest)
    npc_result['output'] = str(output/f'maps/{args.map}/npcs.json')
    return {
        "status": "built",
        "map_id": args.map,
        "map": map_result,
        "npc_content": npc_result,
        "validation": validated,
        "definitions": len(population["definitions"]),
        "spawns": len(population["spawns"]),
        "files_published": len(files),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="command", required=True)
    p = sub.add_parser(
        "build",
        help="extract original source, build per-asset Height-Dense Atlas for SPR, then runtime contracts",
    )
    p.add_argument(
        "original", type=Path, help="original HK2 client folder containing Data1.dat"
    )
    p.add_argument(
        "--extracted-source", type=Path, help="reuse an already extracted source tree"
    )
    p.add_argument(
        "--archive-output",
        type=Path,
        default=None,
    )
    p.add_argument("--runtime-output", type=Path, default=REPO / "client" / "assets")
    p.add_argument(
        "--server-dat",
        type=Path,
        help="original server Dat required for authoritative map population",
    )
    p.add_argument("--map", type=int, action="append", help="repeat for more maps; defaults to Map13")
    p.add_argument(
        "--runtime-only",
        action="store_true",
        help="only rebuild map/world/population contracts from already-published independent assets",
    )
    p.add_argument(
        "--archive-only", action="store_true", help="do not touch live client assets"
    )
    p.add_argument("--preview-groups", type=int, help="trial only: first N groups")
    p.add_argument(
        "--category", action="append", help="filter first-level asset category"
    )
    p.add_argument(
        "--prefix",
        help="filter exact group path prefix, e.g. player/a/weapon/a-sword014",
    )
    p.add_argument("--skip-sidecars", action="store_true")
    p.add_argument("--force", action="store_true")
    verify = sub.add_parser('verify',help='audit the complete private archive against the original SPR source set')
    verify.add_argument('original',type=Path)
    verify.add_argument('--extracted-source',type=Path,default=REPO/'.cache/asset_converter/source')
    verify.add_argument('--archive-output',type=Path,default=REPO/'local-assets/asset_converter/complete')
    verify.add_argument('--report',type=Path,default=REPO/'reports/converter_archive_verification.json')
    world = sub.add_parser('world-content',help='extract original client NPC visuals and compile private NPC/transfer evidence from original server Dat')
    world.add_argument('original',type=Path)
    world.add_argument('--extracted-source',type=Path)
    world.add_argument('--server-dat',type=Path)
    maps = world.add_mutually_exclusive_group()
    maps.add_argument('--map',type=int,action='append',help='repeatable map ID; default Map13')
    maps.add_argument('--all-maps',action='store_true',help='compile all NPC.dat files present in server Dat; absent files are reported, not guessed empty')
    destinations = world.add_mutually_exclusive_group()
    destinations.add_argument('--output',type=Path,help='single-map JSON destination')
    destinations.add_argument('--output-root',type=Path,help='private catalog root; output maps/<id>/npcs.json')
    world.add_argument('--report',type=Path)
    world.add_argument('--include-map',action='store_true',
                       help='also compile original terrain, objects and blocking per map into output-root')
    world.add_argument('--include-population',action='store_true',
                       help='audit Enemy.dat spawn records against original SPR/ACT, without guessed runtime stats')
    publication = sub.add_parser('export-assets',help='audit complete archive and publish all private source assets with a logical Godot catalog')
    publication.add_argument('original',type=Path)
    publication.add_argument('--extracted-source',type=Path)
    publication.add_argument('--archive-output',type=Path,default=REPO/'local-assets/asset_converter/complete')
    publication.add_argument('--assets',type=Path,default=REPO/'client/assets')
    publication.add_argument('--report',type=Path,default=REPO/'reports/asset_export.json')
    publication.add_argument('--map',type=int,action='append',help='publish only original map visuals, enemies, NPCs and standard player parts')
    publication.add_argument('--server-dat',type=Path,help='original server Dat for map-scoped asset selection')
    args = ap.parse_args()
    if args.command == 'build' and args.archive_only and args.runtime_only:
        ap.error('archive-only and runtime-only conflict')
    if args.command in ('build', 'world-content', 'export-assets'):
        selected_maps = args.map or [13]
        if len(set(selected_maps)) != len(selected_maps) or any(not 1 <= n <= 65535 for n in selected_maps):
            ap.error('map IDs must be unique integers between 1 and 65535')
        args.map_ids = sorted(selected_maps)
    if args.command == 'world-content' and args.output and (args.all_maps or len(args.map_ids) > 1 or args.include_map or args.include_population):
        ap.error('multiple maps, include-map and include-population use output-root, not a single output file')
    try:
        with converter_operation(REPO/'.cache/asset_converter/operation.lock'):
            return _execute(args)
    except ConverterBusy as error:
        print(json.dumps({'status':'busy','error':str(error)},ensure_ascii=False),flush=True)
        return 2


def _execute(args):
    if args.command == 'export-assets':
        report = {'status':'failed'}
        started = time.perf_counter()
        try:
            output = args.assets.resolve()
            original = args.original.resolve()
            if output == original or output.is_relative_to(original):
                raise ValueError('asset output must not overwrite original game')
            source = resolve_source(args)
            selected = map_asset_ids(source,resolve_server_dat(args.server_dat),args.map_ids) if args.map else None
            report = export_assets(source,args.archive_output,output,asset_ids=selected)
            if selected is not None:
                report['requested_maps'] = args.map_ids
                report['selected_assets'] = sorted(selected)
        except (OSError,ValueError,KeyError,TypeError) as error:
            report = {'status':'failed','error':str(error),'complete_asset_conversion':False}
        report['elapsed_seconds'] = round(time.perf_counter()-started,3)
        write_json(args.report,report)
        print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)
        return 0 if report['status']=='pass' else 1
    if args.command == 'world-content':
        report = {'status':'failed'}
        destination = (args.output_root or REPO/'local-assets/asset_converter/world').resolve()
        report_path = args.report or REPO/('reports/world_content_all.json' if args.all_maps else
                                         'reports/world_content_maps.json' if len(args.map_ids) > 1 else
                                         f'reports/world_content_map{args.map_ids[0]}.json')
        started = time.perf_counter()
        module_paths = ['tools/asset_converter/'+name for name in
                        ['cli.py','content/npc_content.py','content/build_maps.py',
                         'content/build_population.py','content/animation_definition.py',
                         'content/build_enemy_map_palette.py','content/asset_paths.py',
                         'content/audit_character_layers.py','source/inspect_act.py',
                         'source/legacy_direction.py','source/legacy_npc.py',
                         'source/runtime_spr.py']]
        modules = {name:hashlib.sha256((REPO/name).read_bytes()).hexdigest() for name in module_paths}
        try:
            original = args.original.resolve()
            source = resolve_source(args)
            server_dat = resolve_server_dat(args.server_dat)
            output = (args.output or destination/f'maps/{args.map_ids[0]}/npcs.json').resolve()
            for root in (original,source,server_dat):
                if output == root or output.is_relative_to(root):
                    raise ValueError('world content output must not overwrite original sources')
            def compile_one(map_id, npc_output):
                row = build_npcs(source, server_dat, map_id, npc_output)
                if args.include_map:
                    row['terrain'] = build_map(source, destination, map_id)
                if args.include_population:
                    evidence = compile_spawn_evidence(source, server_dat, map_id)
                    write_json(destination/f'maps/{map_id}/population_evidence.json', evidence)
                    row['enemy_source'] = {
                        'spawns': len(evidence['spawns']),
                        'definitions': len(evidence['definitions']),
                        'runtime_implemented': False,
                        'stats_verified': False,
                    }
                return row
            if args.all_maps:
                map_ids, absent = [], []
                for folder in sorted((server_dat/'Map').iterdir()):
                    match = re.fullmatch(r'Map([0-9]+)',folder.name)
                    if match and folder.is_dir():
                        (map_ids if (folder/'NPC.dat').is_file() else absent).append(int(match[1]))
                if not map_ids or len(map_ids) != len(set(map_ids)):
                    raise ValueError('missing/ambiguous NPC map source set')
                rows = []
                for map_id in sorted(map_ids):
                    try:
                        rows.append(compile_one(map_id,destination/f'maps/{map_id}/npcs.json'))
                    except (OSError,ValueError,KeyError,TypeError) as error:
                        rows.append({'status':'failed','map_id':map_id,'error':str(error)})
                report = {'status':'pass' if all(row['status']=='pass' for row in rows) else 'partial',
                          'scope':'NPC.dat files present in supplied server Dat; missing files are not assumed empty',
                          'maps':rows,'map_files':len(map_ids),'maps_without_npc_file':sorted(absent),
                          'npcs':sum(row.get('npcs',0) for row in rows),'runtime_transfer_implemented':False}
            elif len(args.map_ids) > 1:
                rows = []
                for map_id in args.map_ids:
                    try:
                        rows.append(compile_one(map_id,
                                                destination/f'maps/{map_id}/npcs.json'))
                    except (OSError, ValueError, KeyError, TypeError) as error:
                        rows.append({'status':'failed','map_id':map_id,'error':str(error)})
                report = {'status':'pass' if all(row['status']=='pass' for row in rows) else 'partial',
                          'maps':rows, 'map_files':len(rows),
                          'npcs':sum(row.get('npcs',0) for row in rows),
                          'runtime_transfer_implemented':False}
            else:
                report = compile_one(args.map_ids[0],output)
        except (OSError,ValueError,KeyError,TypeError) as error:
            report = {'status':'failed','error':str(error),'runtime_transfer_implemented':False}
        report['elapsed_seconds'] = round(time.perf_counter()-started,3)
        report['converter_sha256'] = modules
        report['converter_files_unchanged'] = modules == {name:hashlib.sha256((REPO/name).read_bytes()).hexdigest() for name in module_paths}
        if not report['converter_files_unchanged']:
            report.update(status='failed',error='converter files changed during execution; rerun before acceptance')
        write_json(report_path,report)
        print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)
        return 0 if report['status']=='pass' else 1
    if args.command=='verify':
        report={'status':'failed'}
        try:
            source=resolve_source(args)
            coverage=audit_sprite_coverage(args.original,source)
            report=audit_archive(source,args.archive_output)
            report['source_coverage']=coverage
            if coverage['missing'] or coverage['extra']:
                report['status']='failed'
                report['errors'].append({'source':'original SPR presence mismatch'})
        except (OSError,ValueError,KeyError,TypeError) as error:
            report={'status':'failed','error':str(error)}
        write_json(args.report,report)
        print(json.dumps(report,ensure_ascii=False,indent=2),flush=True)
        return 0 if report['status']=='pass' else 1
    started = time.perf_counter()
    report = {"version": 1, "atlas_policy": POLICY, "status": "failed"}
    try:
        source = resolve_source(args)
        report["source"] = str(source)
        if (args.original.resolve()/'Data1.dat').is_file():
            coverage = audit_sprite_coverage(args.original,source)
            report['source_coverage'] = coverage
            if (coverage['missing'] or coverage['extra']) and not (args.category or args.prefix or args.preview_groups):
                raise ValueError(f'extracted SPR source is incomplete or contains extras: '
                                 f"missing={len(coverage['missing'])}, extra={len(coverage['extra'])}; rebuild from original")
        if not args.runtime_only:
            archive_root = args.archive_output or (
                REPO / "local-assets" / "asset_converter"
                / ("preview" if (args.category or args.prefix or args.preview_groups) else "complete")
            )
            report["archive"] = archive_all(source, archive_root.resolve(), args)
        full_build = not (args.preview_groups or args.category or args.prefix)
        if not args.archive_only and not args.runtime_only and full_build:
            report["asset_export"] = export_assets(
                source,
                args.archive_output or REPO/"local-assets/asset_converter/complete",
                args.runtime_output,
            )
        if not args.archive_only and full_build:
            runtime_rows = []
            for map_id in args.map_ids:
                map_args = argparse.Namespace(**vars(args))
                map_args.map = map_id
                row = build_runtime_assets(source, args.runtime_output.resolve(), map_args)
                runtime_rows.append(row)
                if row.get('status') != 'built':
                    break  # fail closed; do not report a partial multi-map build as complete
            report['runtime'] = (runtime_rows[0] if len(args.map_ids) == 1 else
                                 {'status':'built' if len(runtime_rows) == len(args.map_ids) and
                                  all(row.get('status') == 'built' for row in runtime_rows) else 'partial',
                                  'requested_maps':args.map_ids,'maps':runtime_rows})
        else:
            report["runtime"] = {"status": "not_requested"}
        report["status"] = (
            "pass"
            if (
                not report.get("archive", {}).get("errors")
                and report.get('asset_export',{}).get('status','pass') == 'pass'
                and report.get("runtime", {}).get("status")
                in ("built", "not_requested")
            )
            else "partial"
        )
        report["elapsed_seconds"] = round(time.perf_counter() - started, 2)
    except Exception as exc:
        report["error"] = str(exc)
        report["elapsed_seconds"] = round(time.perf_counter() - started, 2)
    path = REPO / "local-assets" / "asset_converter" / "build_report.json"
    write_json(path, report)
    print(
        json.dumps(
            {k: v for k, v in report.items() if k != "archive"},
            ensure_ascii=False,
            indent=2,
        ),
        flush=True,
    )
    return 0 if report["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
