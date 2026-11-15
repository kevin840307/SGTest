"""Single-entry SGTest legacy asset converter. Run from any working directory."""

from __future__ import annotations
import argparse, hashlib, json, os, re, shutil, sys, tempfile, time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))

from asset_converter.atlas.adaptive_atlas import build_group, POLICY
from asset_converter.atlas.build_atlas_groups import select_groups
from asset_converter.atlas.archive_audit import audit_archive
from asset_converter.operation_lock import converter_operation, ConverterBusy
from asset_converter.source.convert_assets import (
    convert_act,
    convert_draw_order,
    convert_sif,
)
from asset_converter.source.extract_client import extract, audit_sprite_coverage
from asset_converter.content.build_maps import build as build_map
from asset_converter.content.npc_content import build as build_npcs
from asset_converter.content.asset_pipeline import (
    build_all as build_runtime,
    resolve_server_dat,
)


def write_json(path: Path, obj: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(
        json.dumps(obj, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    tmp.replace(path)


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
                f"[ADAPTIVE ATLAS] {n}/{total} groups; failures={len(summary['errors'])}",
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


def check_runtime(stage: Path, map_id: int) -> dict:
    result = {}
    for name, relative in (
        ("map", f"bundles/map/{map_id}/sprites.json"),
        ("session", "bundles/session/player-b-base.json"),
    ):
        path = stage / relative
        doc = json.loads(path.read_text(encoding="utf-8"))
        pages = doc["pages"]
        if not pages or len(doc["page_sizes"]) != len(pages):
            raise ValueError(f"{name}: no valid pages")
        for page_name, size in zip(pages, doc["page_sizes"]):
            if (
                Path(page_name).name != page_name
                or not (path.parent / page_name).is_file()
            ):
                raise ValueError(f"{name}: missing or unsafe page {page_name}")
            if max(size) > 4096:
                raise ValueError(f"{name}: unsafe maximum texture dimension")
        for frame in doc["frames"]:
            pg = frame["page"]
            if pg < 0:
                continue
            if pg >= len(pages):
                raise ValueError(f"{name}: frame page invalid")
            x, y, w, h = frame["rect"]
            pw, ph = doc["page_sizes"][pg]
            if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > pw or y + h > ph:
                raise ValueError(f"{name}: frame region outside atlas")
        result[name] = {
            "pages": len(pages),
            "frames": len(doc["frames"]),
            "scope": doc["bundle_scope"],
            "packing_policy": doc["packing_policy"],
        }
    return result


def build_runtime_assets(source: Path, output: Path, args) -> dict:
    try:
        server_dat = resolve_server_dat(args.server_dat)
    except ValueError as exc:
        return {"status": "not_built", "reason": str(exc)}
    if output == source or output in source.parents or source in output.parents:
        raise ValueError("runtime output must not overlap original source")
    stage_parent = REPO / ".cache" / "asset_converter"
    stage_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="stage-", dir=stage_parent) as scratch:
        stage = Path(scratch)
        print(f"[RUNTIME] map {args.map} + session, staged before publish", flush=True)
        map_result = build_map(source, stage, args.map)
        report = build_runtime(source, stage, server_dat, args.map, prune=False)
        validated = check_runtime(stage, args.map)
        # Copy only verified generated resources; leave unrelated live assets alone.
        files = [p for p in stage.rglob("*") if p.is_file()]
        files.sort(key=lambda p: (p.suffix.lower() == ".json", p.as_posix()))
        for p in files:
            relative = p.relative_to(stage)
            dest = output / relative
            dest.parent.mkdir(parents=True, exist_ok=True)
            temporary = dest.with_name(dest.name + ".asset_converter.tmp")
            shutil.copyfile(p, temporary)
            temporary.replace(dest)
    return {
        "status": "built",
        "map_id": args.map,
        "map": map_result,
        "validation": validated,
        "definitions": report["definitions"],
        "spawns": report["spawns"],
        "files_published": len(files),
    }


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="command", required=True)
    p = sub.add_parser(
        "build",
        help="extract original source, build Adaptive Atomic Atlas for SPR, then runtime bundles",
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
    p.add_argument("--map", type=int, default=13)
    p.add_argument(
        "--runtime-only",
        action="store_true",
        help="only build wired map/session bundle",
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
    maps.add_argument('--map',type=int,default=13)
    maps.add_argument('--all-maps',action='store_true',help='compile all NPC.dat files present in server Dat; absent files are reported, not guessed empty')
    destinations = world.add_mutually_exclusive_group()
    destinations.add_argument('--output',type=Path,help='single-map JSON destination')
    destinations.add_argument('--output-root',type=Path,help='private catalog root; output maps/<id>/npcs.json')
    world.add_argument('--report',type=Path)
    args = ap.parse_args()
    if args.command == 'build' and args.archive_only and args.runtime_only:
        ap.error('archive-only and runtime-only conflict')
    if args.command == 'world-content' and args.all_maps and args.output:
        ap.error('all-maps uses output-root, not a single output file')
    try:
        with converter_operation(REPO/'.cache/asset_converter/operation.lock'):
            return _execute(args)
    except ConverterBusy as error:
        print(json.dumps({'status':'busy','error':str(error)},ensure_ascii=False),flush=True)
        return 2


def _execute(args):
    if args.command == 'world-content':
        report = {'status':'failed'}
        destination = (args.output_root or REPO/'local-assets/asset_converter/world').resolve()
        report_path = args.report or REPO/('reports/world_content_all.json' if args.all_maps else f'reports/world_content_map{args.map}.json')
        started = time.perf_counter()
        module_paths = ['tools/asset_converter/'+name for name in
                        ['cli.py','content/npc_content.py','content/build_maps.py','source/legacy_npc.py','source/runtime_spr.py']]
        modules = {name:hashlib.sha256((REPO/name).read_bytes()).hexdigest() for name in module_paths}
        try:
            original = args.original.resolve()
            source = resolve_source(args)
            server_dat = resolve_server_dat(args.server_dat)
            output = (args.output or destination/f'maps/{args.map}/npcs.json').resolve()
            for root in (original,source,server_dat):
                if output == root or output.is_relative_to(root):
                    raise ValueError('world content output must not overwrite original sources')
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
                        rows.append(build_npcs(source,server_dat,map_id,destination/f'maps/{map_id}/npcs.json'))
                    except (OSError,ValueError,KeyError,TypeError) as error:
                        rows.append({'status':'failed','map_id':map_id,'error':str(error)})
                report = {'status':'pass' if all(row['status']=='pass' for row in rows) else 'partial',
                          'scope':'NPC.dat files present in supplied server Dat; missing files are not assumed empty',
                          'maps':rows,'map_files':len(map_ids),'maps_without_npc_file':sorted(absent),
                          'npcs':sum(row.get('npcs',0) for row in rows),'runtime_transfer_implemented':False}
            else:
                report = build_npcs(source,server_dat,args.map,output)
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
        if (
            not args.archive_only
            and not args.preview_groups
            and not args.category
            and not args.prefix
        ):
            report["runtime"] = build_runtime_assets(
                source, args.runtime_output.resolve(), args
            )
        else:
            report["runtime"] = {"status": "not_requested"}
        report["status"] = (
            "pass"
            if (
                not report.get("archive", {}).get("errors")
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
