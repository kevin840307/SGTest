from __future__ import annotations
import json, statistics, subprocess
from pathlib import Path

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=Path(r"C:\SGO\Recovered\client")
SG_PROJECT=ROOT/"client"
REC_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/recovered_native.gd"
SG_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_native.gd"
VARIANTS=ROOT/".cache/asset_packing_benchmark/model1_variants"
OUT=ROOT/".cache/asset_packing_benchmark/native_matrix"
OUT.mkdir(parents=True,exist_ok=True)

FIXED12=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]

SINGLES=[
 ("enemy_small","enemy",["EN032A"],["enemy_small"],1000),
 ("enemy_medium","enemy",["EN016A"],["enemy_medium"],1000),
 ("enemy_large","enemy",["EN081A"],["enemy_large"],1000),
 ("enemy_huge","enemy",["EN078A"],["enemy_huge"],1000),
 ("weapon_small","weapon",["B-Dagger006"],["weapon_small"],300),
 ("weapon_medium","weapon",["B-Sword024"],["weapon_medium"],300),
 ("weapon_large","weapon",["B-Stick003"],["weapon_large"],300),
 ("weapon_huge","weapon",["B-Stick011"],["weapon_huge"],300),
 ("equipment_small","equipment",["G-Bonnet092"],["equipment_small"],300),
 ("equipment_medium","equipment",["A-Back023"],["equipment_medium"],300),
 ("equipment_large","equipment",["R-Back029"],["equipment_large"],300),
 ("equipment_huge","equipment",["R-Back026"],["equipment_huge"],300),
]
MIXED=[
 ("enemy_mix4","enemy",["EN032A","EN016A","EN081A","EN078A"],
   ["enemy_small","enemy_medium","enemy_large","enemy_huge"],1000,2),
 ("enemy_mix12","enemy",FIXED12,["mixed_"+x.lower() for x in FIXED12],1000,2),
 ("weapon_mix4","weapon",["B-Dagger006","B-Sword024","B-Stick003","B-Stick011"],
   ["weapon_small","weapon_medium","weapon_large","weapon_huge"],300,2),
 ("equipment_mix4","equipment",["G-Bonnet092","A-Back023","R-Back029","R-Back026"],
   ["equipment_small","equipment_medium","equipment_large","equipment_huge"],300,2),
]
SG_MODES=["height_p0","height_p1","height_p2","maxrects_p1"]

def run(cmd,out):
    p=subprocess.run(cmd,cwd=ROOT,capture_output=True,text=True,encoding="utf8",errors="replace",timeout=90)
    if p.returncode!=0 or not out.exists():
        raise RuntimeError("FAILED\n"+" ".join(map(str,cmd))+"\n"+p.stdout[-5000:]+"\n"+p.stderr[-5000:])
    return json.loads(out.read_text(encoding="utf8"))

def recovered(label,kind,names,count,rep):
    out=OUT/f"{label}_recovered_r{rep}.json"
    cmd=[str(GODOT),"--rendering-method","gl_compatibility","--path",str(REC_PROJECT),"--script",str(REC_SCRIPT),"--",
         kind,",".join(names),str(count),"100","1280","720",str(out)]
    row=run(cmd,out);row.update(label=label,mode="recovered_model1",repeat=rep)
    print("PASS",label,"recovered",rep,"load",round(row["load_ms"],2),"p95",round(row["frame_p95_ms"],3),"draw",row["draw_calls_p95"],flush=True)
    return row

def sgtest(label,kind,samples,count,mode,rep):
    out=OUT/f"{label}_{mode}_r{rep}.json"
    cmd=[str(GODOT),"--rendering-method","gl_compatibility","--path",str(SG_PROJECT),"--script",str(SG_SCRIPT),"--",
         str(VARIANTS/mode),",".join(samples),str(count),"100","1280","720",kind,str(out)]
    row=run(cmd,out);row.update(label=label,mode=mode,repeat=rep)
    print("PASS",label,mode,rep,"load",round(row["load_ms"],2),"p95",round(row["frame_p95_ms"],3),"draw",row["draw_calls_p95"],flush=True)
    return row

rows=[]
# Screen each size with real Recovered vs the geometry winner.
for label,kind,rec,sg,count in SINGLES:
    rows.append(recovered(label,kind,rec,count,1))
    rows.append(sgtest(label,kind,sg,count,"height_p0",1))

# Mixed workloads exercise every Model1 variant twice.
for label,kind,rec,sg,count,repeats in MIXED:
    for rep in range(1,repeats+1):
        rows.append(recovered(label,kind,rec,count,rep))
        for mode in SG_MODES:
            rows.append(sgtest(label,kind,sg,count,mode,rep))

# Scenery has no equivalent isolated Recovered loader; compare SGTest packing/render path only.
for rep in (1,2):
    for mode in SG_MODES:
        rows.append(sgtest("scenery_mix4","scenery",
            ["scenery_small","scenery_medium","scenery_large","scenery_huge"],600,mode,rep))

def median(group,key):
    return statistics.median(float(x[key]) for x in group)

summary=[]
for label in sorted({r["label"] for r in rows}):
    for mode in sorted({r["mode"] for r in rows if r["label"]==label}):
        g=[r for r in rows if r["label"]==label and r["mode"]==mode]
        item={"label":label,"mode":mode,"trials":len(g),"instances":g[0]["instances"],
              "renderer_path":g[0]["renderer_path"]}
        for key in ["load_ms","frame_p50_ms","frame_p95_ms","frame_p99_ms","draw_calls_p50","draw_calls_p95",
                    "texture_mem_delta","video_mem_delta","node_delta"]:
            item[key]=median(g,key)
        if "atlas_rgba_bytes" in g[0]: item["atlas_rgba_bytes"]=median(g,"atlas_rgba_bytes")
        summary.append(item)

report={"version":1,"conditions":{"viewport":"1280x720","frames":100,"warmup":30,
 "single_screen":"Recovered vs Height-P0","mixed_repeats":2,"renderers_are_native":True},
 "rows":rows,"summary":summary}
(OUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
print("\nSUMMARY")
for r in summary:
    print(r["label"],r["mode"],"trials",r["trials"],"load",round(r["load_ms"],2),
          "p95",round(r["frame_p95_ms"],3),"p99",round(r["frame_p99_ms"],3),
          "draw",round(r["draw_calls_p95"],1),"texMiB",round(r["texture_mem_delta"]/1048576,2),
          "nodes",round(r["node_delta"],1))
