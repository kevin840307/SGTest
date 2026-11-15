from __future__ import annotations
import json, statistics, subprocess, time
from pathlib import Path
import psutil

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=Path(r"C:\SGO\Recovered\client")
SG_PROJECT=ROOT/"client"
BPTC_PROJECT=ROOT/".cache/bptc_matrix"
REC_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/recovered_native.gd"
SG_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_native.gd"
BPTC_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/bptc_native.gd"
VARIANTS=ROOT/".cache/asset_packing_benchmark/model1_variants"
OUT=ROOT/".cache/asset_packing_benchmark/optimized_matrix"
OUT.mkdir(parents=True,exist_ok=True)

FIXED12=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
FIXED12_SG=["mixed_"+x.lower() for x in FIXED12]
BASE=["recovered_model1","raw_p0","bptc_p0_fast","bptc_pot_fast"]
MIX=["recovered_model1","raw_p0","raw_pot_p0","bptc_p0_current","bptc_p0_fast","bptc_pot_fast"]

CASES=[
 ("enemy_small","enemy",["EN032A"],["enemy_small"],1000,BASE),
 ("enemy_medium","enemy",["EN016A"],["enemy_medium"],1000,BASE),
 ("enemy_large","enemy",["EN081A"],["enemy_large"],1000,BASE),
 ("enemy_huge","enemy",["EN078A"],["enemy_huge"],1000,BASE),
 ("enemy_mix4","enemy",["EN032A","EN016A","EN081A","EN078A"],
  ["enemy_small","enemy_medium","enemy_large","enemy_huge"],1000,MIX),
 ("enemy_mix12","enemy",FIXED12,FIXED12_SG,1000,MIX),
 ("weapon_mix4","weapon",["B-Dagger006","B-Sword024","B-Stick003","B-Stick011"],
  ["weapon_small","weapon_medium","weapon_large","weapon_huge"],300,
  ["recovered_model1","raw_p0","raw_pot_p0","bptc_p0_fast","bptc_pot_fast"]),
 ("equipment_mix4","equipment",["G-Bonnet092","A-Back023","R-Back029","R-Back026"],
  ["equipment_small","equipment_medium","equipment_large","equipment_huge"],300,
  ["recovered_model1","raw_p0","raw_pot_p0","bptc_p0_fast","bptc_pot_fast"]),
 ("scenery_mix4","scenery",[],["scenery_small","scenery_medium","scenery_large","scenery_huge"],600,
  ["raw_p0","raw_pot_p0","bptc_p0_fast","bptc_pot_fast"]),
]

def execute(cmd,out):
    started=time.perf_counter()
    log_path=out.with_suffix(".process.log")
    log_file=log_path.open("w",encoding="utf8",errors="replace")
    proc=subprocess.Popen(cmd,cwd=ROOT,stdout=log_file,stderr=subprocess.STDOUT,text=True,encoding="utf8",errors="replace")
    peak_rss=peak_private=0; cpu_seconds=0.0; tracked={proc.pid}
    while proc.poll() is None:
        for pid in list(tracked):
            try:
                ps=psutil.Process(pid); tracked.update(c.pid for c in ps.children(recursive=True))
            except (psutil.NoSuchProcess,psutil.AccessDenied): pass
        rss=private=0; cpu=0.0
        for pid in list(tracked):
            try:
                ps=psutil.Process(pid); mi=ps.memory_info()
                rss+=int(mi.rss); private+=int(getattr(mi,"private",0) or 0)
                ct=ps.cpu_times(); cpu+=float(ct.user+ct.system)
            except (psutil.NoSuchProcess,psutil.AccessDenied): pass
        peak_rss=max(peak_rss,rss); peak_private=max(peak_private,private); cpu_seconds=max(cpu_seconds,cpu)
        time.sleep(.01)
    proc.wait(); log_file.close()
    wall=time.perf_counter()-started
    if proc.returncode!=0 or not out.exists():
        tail=log_path.read_text(encoding="utf8",errors="replace")[-10000:] if log_path.exists() else ""
        raise RuntimeError("FAILED\n"+" ".join(map(str,cmd))+"\n"+tail)
    row=json.loads(out.read_text(encoding="utf8"))
    row.update(peak_rss_bytes=peak_rss,peak_private_bytes=peak_private,cpu_seconds=cpu_seconds,wall_seconds=wall)
    return row

def command(label,kind,rec_names,sg_names,count,mode,out):
    common=[str(count),"120","1280","720"]
    if mode=="recovered_model1":
        return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(REC_PROJECT),"--script",str(REC_SCRIPT),"--",
                kind,",".join(rec_names),*common,str(out)]
    if mode.startswith("raw_"):
        variant="height_p0" if mode=="raw_p0" else "height_pot_p0"
        return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(SG_PROJECT),"--script",str(SG_SCRIPT),"--",
                str(VARIANTS/variant),",".join(sg_names),*common,kind,str(out)]
    variant="height_pot_p0" if "pot" in mode else "height_p0"
    renderer="current" if mode.endswith("current") else "fast"
    return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(BPTC_PROJECT),"--script",str(BPTC_SCRIPT),"--",
            f"res://assets/{variant}",renderer,",".join(sg_names),*common,kind,str(out)]

rows=[]
for label,kind,rec_names,sg_names,count,modes in CASES:
    for rep in (1,2,3):
        order=modes if rep!=2 else list(reversed(modes))
        for mode in order:
            out=OUT/f"{label}_{mode}_r{rep}.json"
            row=execute(command(label,kind,rec_names,sg_names,count,mode,out),out)
            row.update(label=label,mode=mode,repeat=rep)
            rows.append(row)
            print("PASS",label,mode,"r",rep,
                  "RSS",round(row["peak_rss_bytes"]/1048576,1),
                  "Tex",round(row["texture_mem_delta"]/1048576,1),
                  "Load",round(row["load_ms"],1),
                  "P95",round(row["frame_p95_ms"],3),
                  "Draw",round(row["draw_calls_p95"],1),flush=True)

def med(g,k): return statistics.median(float(x.get(k,0)) for x in g)
summary=[]
for label in sorted({r["label"] for r in rows}):
    for mode in sorted({r["mode"] for r in rows if r["label"]==label}):
        g=[r for r in rows if r["label"]==label and r["mode"]==mode]
        item={"label":label,"mode":mode,"trials":len(g),"instances":g[0]["instances"],"renderer_path":g[0]["renderer_path"]}
        for k in ["peak_rss_bytes","peak_private_bytes","cpu_seconds","wall_seconds","load_ms","frame_p50_ms","frame_p95_ms","frame_p99_ms","draw_calls_p50","draw_calls_p95","texture_mem_delta","video_mem_delta","node_delta"]:
            item[k]=med(g,k)
        summary.append(item)
report={"version":1,"conditions":{"viewport":"1280x720","frames":120,"warmup":30,"repeats":3,"vsync":False,
        "native_renderer_per_design":True,"bptc_import":"Godot VRAM compression high_quality no_mipmap"},
        "rows":rows,"summary":summary}
(OUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
print("\nSUMMARY")
for r in summary:
    print(r["label"],r["mode"],"RSS",round(r["peak_rss_bytes"]/1048576,1),"Tex",round(r["texture_mem_delta"]/1048576,1),
          "Load",round(r["load_ms"],1),"P95",round(r["frame_p95_ms"],3),"P99",round(r["frame_p99_ms"],3),
          "Draw",round(r["draw_calls_p95"],1),"CPU",round(r["cpu_seconds"],2))
