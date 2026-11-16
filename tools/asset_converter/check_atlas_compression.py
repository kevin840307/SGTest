"""Private codec/layout comparison; reuses the sole Height-Dense geometry and PCK.

Original P0, block-aligned P0, and 1px extruded frames are temporary candidates,
not alternative publishers. Metrics compare source-over RGB at native pixel
centers on black/white backgrounds, not invisible RGB under zero alpha. The
readback is decoded device-selected texture data, not a gameplay benchmark.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import time

from PIL import Image, ImageOps

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools"))
from asset_converter.atlas.dense_atlas import _single_page_geometry, _pack_paged
from asset_converter.source.runtime_spr import Frame
from asset_converter.runtime_pack import build_runtime_pack


def aligned_page(image: Image.Image) -> Image.Image:
    size = tuple((side + 3) // 4 * 4 for side in image.size)
    result = Image.new("RGBA", size)
    result.paste(image, (0, 0))
    return result


def extruded_page(image: Image.Image) -> Image.Image:
    result = ImageOps.expand(image, 1)
    result.paste(image.crop((0, 0, 1, image.height)), (0, 1))
    result.paste(image.crop((image.width-1, 0, image.width, image.height)), (image.width+1, 1))
    result.paste(result.crop((0, 1, result.width, 2)), (0, 0))
    result.paste(result.crop((0, result.height-2, result.width, result.height-1)), (0, result.height-1))
    return result


def histogram_metrics(histogram):
    count = sum(histogram)
    def percentile(p):
        target, total = math.ceil(count*p), 0
        for value, frequency in enumerate(histogram):
            total += frequency
            if total >= target:
                return value
        return 0
    return {"samples":count,"mae":sum(i*n for i,n in enumerate(histogram))/max(1,count),
            "p95":percentile(.95),"p99":percentile(.99),
            "max":max((i for i,n in enumerate(histogram) if n),default=0),
            "over_16":sum(histogram[17:])}


def composite_difference(expected, actual):
    from PIL import ImageChops
    histogram = [0]*256
    for background in (0,255):
        base = Image.new("RGBA",expected.size,(background,background,background,255))
        a = Image.alpha_composite(base,expected).convert("RGB")
        b = Image.alpha_composite(base,actual).convert("RGB")
        h = ImageChops.difference(a,b).histogram()
        for channel in range(3):
            histogram = [n+m for n,m in zip(histogram,h[channel*256:(channel+1)*256])]
    return histogram


def run(args):
    started = time.perf_counter()
    code = [Path(__file__),Path(__file__).with_name("probe_atlas_compression.gd"),
            REPO/"tools/asset_converter/atlas/dense_atlas.py",REPO/"tools/asset_converter/runtime_pack.py"]
    code_hashes = {str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in code}
    assets, work = args.assets.resolve(),args.work.resolve()
    # Probe work must never erase source assets or a shared build stage.
    if work == assets or work.is_relative_to(assets) or assets.is_relative_to(work):
        raise ValueError("probe workspace overlaps source assets")
    work.mkdir(parents=True,exist_ok=False)
    source = work/"source"
    source.mkdir()
    output = work/"readback"
    output.mkdir()
    contract, cases = [],[]
    before = {}
    for manifest_path in sorted(assets.rglob("atlas.json")):
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        before[str(manifest_path)] = hashlib.sha256(manifest_path.read_bytes()).hexdigest()
        page_folder = manifest_path.parent
        if args.archive:
            # Reproduce the pre-fix P0 baseline even after runtime publication
            # has aligned pages. Source frame geometry must still be identical.
            page_folder = args.archive.resolve()/"sprites"/manifest["source_group"]
            archived = json.loads((page_folder/"atlas.json").read_text(encoding="utf-8"))
            if len(archived["frames"]) != len(manifest["frames"]) or any(
                any(a[k]!=b[k] for k in ("page","rect","origin","size","source","source_frame"))
                for a,b in zip(archived["frames"],manifest["frames"])):
                raise ValueError("published frame geometry differs from source Archive")
            before[str(page_folder/"atlas.json")] = hashlib.sha256((page_folder/"atlas.json").read_bytes()).hexdigest()
        for page_id,name in enumerate(manifest["pages"]):
            page_path = page_folder/name
            before[str(page_path)] = hashlib.sha256(page_path.read_bytes()).hexdigest()
            with Image.open(page_path) as loaded:
                original = loaded.convert("RGBA")
            records = [(i,row["rect"]) for i,row in enumerate(manifest["frames"]) if row["page"]==page_id]
            enlarged = []
            for _,(x,y,w,h) in records:
                enlarged.append(Frame(extruded_page(original.crop((x,y,x+w,y+h))),0,0))
            rows = [(i,(0,0,f.image.width,f.image.height),f.image.width,f.image.height) for i,f in enumerate(enlarged)]
            rows.sort(key=lambda row:(-row[3],-row[2],row[0]))
            geometry = _single_page_geometry(rows)
            if geometry is None:
                p1_pages,p1_records = _pack_paged(enlarged,rows)
            else:
                from asset_converter.atlas.dense_atlas import _pack_single
                p1_pages,p1_records = _pack_single(enlarged,rows,geometry)
            variants = {"p0":([original],[r for _,r in records],[0]*len(records)),
                        "block4":([aligned_page(original)],[r for _,r in records],[0]*len(records)),
                        "extrude1":([aligned_page(p) for p in p1_pages],
                          [[r["rect"][0]+1,r["rect"][1]+1,r["rect"][2]-2,r["rect"][3]-2] for r in p1_records],
                          [r["page"] for r in p1_records])}
            for variant,(pages,rects,page_ids) in variants.items():
                case_id = len(cases)
                folder = source/f"Enemy/probe_{case_id}"
                folder.mkdir(parents=True)
                for n,page in enumerate(pages):
                    page.save(folder/f"page_{n}.png")
                    contract.append({"path":f"res://assets/Enemy/probe_{case_id}/page_{n}.png"})
                (folder/"atlas.json").write_text(json.dumps({"pages":[f"page_{n}.png" for n in range(len(pages))]}))
                cases.append({"asset":manifest["asset_id"],"source_page":str(page_path),"page_id":page_id,
                              "variant":variant,"id":case_id,"rects":rects,"page_ids":page_ids,
                              "source_rects":[r for _,r in records]})
    # Colored 51x102 chart catches whole-image resampling without codec ambiguity.
    chart = Image.new("RGBA",(51,102))
    for y in range(102):
        for x in range(51):
            chart.putpixel((x,y),((255 if x%2 else 0),(255 if y%2 else 0),0,255))
    chart.save(work/"chart.png")
    for variant,page in (("p0",chart),("block4",aligned_page(chart))):
        case_id = len(cases)
        folder = source/f"Enemy/probe_{case_id}"
        folder.mkdir(parents=True)
        page.save(folder/"page_0.png")
        (folder/"atlas.json").write_text('{"pages":["page_0.png"]}')
        contract.append({"path":f"res://assets/Enemy/probe_{case_id}/page_0.png"})
        cases.append({"asset":"synthetic_chart","source_page":str(work/"chart.png"),"page_id":0,
                      "variant":variant,"id":case_id,"rects":[[0,0,51,102]],"page_ids":[0],"source_rects":[[0,0,51,102]]})
    contract_path = work/"contract.json"
    contract_path.write_text(json.dumps(contract),encoding="utf-8")
    pack = build_runtime_pack(args.godot,source,work/"probe.pck",work/"import")
    godot = args.godot.resolve()
    actual = godot.with_name(godot.name.replace("_console",""))
    if actual.is_file():
        godot = actual
    command = [str(godot),"--path",str(REPO/"client"),"--resolution","320x180",
               "--script",str(Path(__file__).with_name("probe_atlas_compression.gd")),"--",
               pack["path"],str(contract_path),str(output)]
    result = subprocess.run(command,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=600)
    (work/"native.log").write_text(result.stdout+result.stderr,encoding="utf-8")
    if result.returncode or "ATLAS_COMPRESSION_PROBE_PASS" not in result.stdout or "ERROR:" in result.stderr:
        raise RuntimeError("native probe failed; see "+str(work/"native.log"))
    readback = json.loads((output/"readback.json").read_text())
    decoded = {row["path"]:row for row in readback["rows"]}
    totals = {v:[0]*256 for v in ("p0","block4","extrude1")}
    metrics = []
    for case in cases:
        histogram = [0]*256
        with Image.open(case["source_page"]) as loaded:
            original = loaded.convert("RGBA")
        pages,extent_mismatches,compressed_bytes = {},0,0
        for page_id in set(case["page_ids"]):
            row = decoded[f'res://assets/Enemy/probe_{case["id"]}/page_{page_id}.png']
            extent_mismatches += row["logical"] != row["physical"]
            compressed_bytes += row["compressed_bytes"]
            with Image.open(row["file"]) as loaded:
                physical = loaded.convert("RGBA")
            # Texture coordinates are normalized by logical size by Godot.
            # Resize back with nearest to evaluate original pixel centers.
            pages[page_id] = physical.resize(tuple(row["logical"]),Image.Resampling.NEAREST)
        for rect,source_rect,page_id in zip(case["rects"],case["source_rects"],case["page_ids"]):
            x,y,w,h = source_rect
            expected = original.crop((x,y,x+w,y+h))
            x,y,w,h = rect
            actual = pages[page_id].crop((x,y,x+w,y+h))
            histogram = [n+m for n,m in zip(histogram,composite_difference(expected,actual))]
        metrics.append({"asset":case["asset"],"page_id":case["page_id"],"variant":case["variant"],
                        "extent_mismatches":extent_mismatches,"compressed_bytes":compressed_bytes,
                        **histogram_metrics(histogram)})
        if case["asset"] != "synthetic_chart":
            totals[case["variant"]] = [n+m for n,m in zip(totals[case["variant"]],histogram)]
    summary = {variant:{**histogram_metrics(histogram),
               "extent_mismatches":sum(r["extent_mismatches"] for r in metrics if r["variant"]==variant and r["asset"]!="synthetic_chart"),
               "compressed_bytes":sum(r["compressed_bytes"] for r in metrics if r["variant"]==variant and r["asset"]!="synthetic_chart")}
               for variant,histogram in totals.items()}
    unchanged = all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in before.items()) and all(
        hashlib.sha256((REPO/p).read_bytes()).hexdigest()==h for p,h in code_hashes.items())
    report = {"status":"PASS" if unchanged and summary["block4"]["extent_mismatches"]==0 else "FAIL",
              "quality_gate":"NOT VERIFIED; comparison completion does not approve every codec artifact",
              "scope":"device-selected codec readback at pixel centers; no game performance claim",
              "sources_unchanged":unchanged,"source_sha256":before,"device":readback["device"],
              "source_code_sha256":code_hashes,
              "godot":readback["godot"],"summary":summary,"metrics":metrics,"pack":pack,
              "seconds":time.perf_counter()-started}
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,indent=2),encoding="utf-8")
    print(json.dumps({"status":report["status"],"summary":summary,"seconds":report["seconds"]},indent=2))
    return 0 if report["status"]=="PASS" else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--assets",type=Path,default=REPO/"client/assets")
    parser.add_argument("--archive",type=Path,help="Read original P0 pages from a verified Archive; keep selected published frame geometry")
    parser.add_argument("--work",type=Path,required=True,help="New private directory; never reuse an existing probe")
    parser.add_argument("--report",type=Path,required=True)
    raise SystemExit(run(parser.parse_args()))
