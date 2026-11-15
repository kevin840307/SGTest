from __future__ import annotations

import json, math, statistics
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
ARCHIVE = ROOT / "local-assets" / "asset_converter" / "complete" / "sprites"
OUTPUT = ROOT / ".cache" / "asset_packing_benchmark" / "geometry_all.json"
PADDING = 2
MAX_SIDE = 4096
WIDTHS = tuple(range(128, MAX_SIDE + 1, 32))

def action(source: str) -> str:
    stem = Path(source).stem.casefold()
    tail = stem.rsplit("_", 1)[-1]
    letters = "".join(ch for ch in tail if ch.isalpha())
    return letters or tail

def rows_from(doc: dict) -> list[dict]:
    rows=[]
    for i,f in enumerate(doc.get("frames",[])):
        if int(f.get("page",-1)) < 0:
            continue
        w,h=int(f["rect"][2]),int(f["rect"][3])
        if w<=0 or h<=0: continue
        source=str(f.get("source",""))
        rows.append({"id":i,"w":w,"h":h,"area":w*h,"max":max(w,h),
                     "source":source,"action":action(source)})
    return rows

def ordered(rows: list[dict], strategy: str) -> list[dict]:
    if strategy=="height":
        return sorted(rows,key=lambda r:(-r["h"],-r["w"],r["id"]))
    if strategy=="width":
        return sorted(rows,key=lambda r:(-r["w"],-r["h"],r["id"]))
    if strategy=="area":
        return sorted(rows,key=lambda r:(-r["area"],-r["h"],-r["w"],r["id"]))
    if strategy=="max_side":
        return sorted(rows,key=lambda r:(-r["max"],-r["area"],r["id"]))
    if strategy=="height_band_32":
        return sorted(rows,key=lambda r:(-math.ceil(r["h"]/32),-r["w"],-r["h"],r["id"]))
    if strategy=="height_band_64":
        return sorted(rows,key=lambda r:(-math.ceil(r["h"]/64),-r["w"],-r["h"],r["id"]))
    if strategy=="aspect_bucket":
        def key(r):
            ratio=r["w"]/max(1,r["h"])
            bucket=0 if ratio>=1.6 else (1 if ratio>=0.625 else 2)
            return (bucket,-r["max"],-r["area"],r["id"])
        return sorted(rows,key=key)
    if strategy=="size_bucket":
        def bucket(r):
            m=r["max"]
            return 0 if m>512 else 1 if m>256 else 2 if m>128 else 3 if m>64 else 4
        return sorted(rows,key=lambda r:(bucket(r),-r["h"],-r["w"],r["id"]))
    if strategy=="action_bucket":
        return sorted(rows,key=lambda r:(r["action"],-r["h"],-r["w"],r["id"]))
    if strategy=="source_bucket":
        return sorted(rows,key=lambda r:(r["source"],-r["h"],-r["w"],r["id"]))
    raise KeyError(strategy)

def shelf(rows: list[dict], width: int, force_break=None):
    x=y=shelf_h=0; used_w=0; current=None
    for r in rows:
        if force_break is not None:
            token=force_break(r)
            if current is not None and token!=current and x:
                y += shelf_h; x=0; shelf_h=0
            current=token
        cw,ch=r["w"]+PADDING*2,r["h"]+PADDING*2
        if cw>width: return None
        if x and x+cw>width:
            y += shelf_h; x=0; shelf_h=0
        x += cw; shelf_h=max(shelf_h,ch); used_w=max(used_w,x)
    used_h=y+shelf_h
    if used_h>MAX_SIDE: return None
    return used_w,used_h

def best_shelf(rows, strategy, forced=None):
    seq=ordered(rows,strategy)
    best=None
    min_width=max(r["w"]+PADDING*2 for r in seq)
    for width in WIDTHS:
        if width<min_width: continue
        g=shelf(seq,width,forced)
        if g is None: continue
        w,h=g; area=w*h
        row=(area,max(w,h),w,h)
        if best is None or row<best: best=row
    return best

STRATEGIES={
    "height_global":("height",None),
    "width_global":("width",None),
    "area_global":("area",None),
    "max_side_global":("max_side",None),
    "height_band_32":("height_band_32",None),
    "height_band_64":("height_band_64",None),
    "size_bucket_soft":("size_bucket",None),
    "aspect_bucket_soft":("aspect_bucket",None),
    "action_bucket_soft":("action_bucket",None),
    "source_bucket_soft":("source_bucket",None),
    "size_bucket_hard":("size_bucket",lambda r: 0 if r["max"]>512 else 1 if r["max"]>256 else 2 if r["max"]>128 else 3 if r["max"]>64 else 4),
    "action_bucket_hard":("action_bucket",lambda r:r["action"]),
    "source_bucket_hard":("source_bucket",lambda r:r["source"]),
}

assets=[]
for p in ARCHIVE.rglob("atlas.json"):
    doc=json.loads(p.read_text(encoding="utf8"))
    group=str(doc.get("group",""))
    if not group or "/" not in group: continue
    rows=rows_from(doc)
    if not rows: continue
    category=group.split("/",1)[0].lower()
    subtype=""
    gl=group.lower()
    if category=="player" and "/weapon/" in gl: subtype="weapon"
    elif category=="player" and "/cloth/" in gl: subtype="equipment"
    elif category=="map": subtype="scenery"
    elif category=="enemy": subtype="enemy"
    pixel=sum(r["area"] for r in rows)
    results={}
    for name,(order,forced) in STRATEGIES.items():
        g=best_shelf(rows,order,forced)
        if g is None:
            results[name]=None
        else:
            area,side,w,h=g
            results[name]={"area":area,"w":w,"h":h,"waste":(area-pixel)/max(1,pixel)}
    if subtype not in {"enemy","weapon","equipment","scenery"}:
        continue
    assets.append({"group":group,"category":category,"subtype":subtype,
                   "frames":len(rows),"pixel_area":pixel,"results":results})

summary={}
for name in STRATEGIES:
    valid=[a for a in assets if a["results"][name] is not None]
    areas=[a["results"][name]["area"] for a in valid]
    waste=[a["results"][name]["waste"] for a in valid]
    summary[name]={
        "fit":len(valid),"total_area":sum(areas),
        "median_waste":statistics.median(waste),
        "p90_waste":sorted(waste)[int((len(waste)-1)*.9)],
        "p99_waste":sorted(waste)[int((len(waste)-1)*.99)],
        "mean_waste":statistics.fmean(waste),
    }

subtypes={}
for subtype in ("enemy","weapon","equipment","scenery"):
    subset=[a for a in assets if a["subtype"]==subtype]
    subtypes[subtype]={}
    for name in STRATEGIES:
        valid=[a for a in subset if a["results"][name] is not None]
        if not valid: continue
        subtypes[subtype][name]={
            "assets":len(valid),
            "total_area":sum(a["results"][name]["area"] for a in valid),
            "median_waste":statistics.median(a["results"][name]["waste"] for a in valid),
        }

samples={}
for subtype in ("enemy","weapon","equipment","scenery"):
    subset=sorted([a for a in assets if a["subtype"]==subtype],key=lambda a:a["pixel_area"])
    if not subset: continue
    qs=[("small",.1),("medium",.5),("large",.9),("huge",1.0)]
    samples[subtype]=[]
    for label,q in qs:
        item=subset[round((len(subset)-1)*q)]
        samples[subtype].append({"label":label,"group":item["group"],"frames":item["frames"],"pixel_area":item["pixel_area"]})

report={"version":1,"assets":len(assets),"strategies":list(STRATEGIES),"summary":summary,
        "subtypes":subtypes,"samples":samples}
OUTPUT.parent.mkdir(parents=True,exist_ok=True)
OUTPUT.write_text(json.dumps(report,indent=2),encoding="utf8")
print("ASSETS",len(assets))
for name,row in sorted(summary.items(),key=lambda kv:kv[1]["total_area"]):
    print(name,"fit",row["fit"],"totalGiB",round(row["total_area"]*4/2**30,3),
          "medianWaste%",round(row["median_waste"]*100,2),"p90%",round(row["p90_waste"]*100,2))
print("\nSUBTYPES")
for subtype,table in subtypes.items():
    print("\n",subtype)
    for name,row in sorted(table.items(),key=lambda kv:kv[1]["total_area"])[:8]:
        print(name,"assets",row["assets"],"MiB",round(row["total_area"]*4/2**20,2),
              "medianWaste%",round(row["median_waste"]*100,2))
print("\nSAMPLES",json.dumps(samples,indent=2))
