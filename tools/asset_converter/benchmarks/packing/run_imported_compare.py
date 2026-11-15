from __future__ import annotations
import json, statistics, subprocess, time
from pathlib import Path
import psutil

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=Path(r"C:\SGO\Recovered\client")
SG_PROJECT=ROOT/"client"
REC_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/recovered_native.gd"
RAW_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_native.gd"
IMP_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_imported_modes.gd"
RAW_ROOT=ROOT/".cache/asset_packing_benchmark/model1_variants"
OUT=ROOT/".cache/asset_packing_benchmark/imported_compare"
OUT.mkdir(parents=True,exist_ok=True)

FIXED12=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
CASES=[
 ("enemy_small",["EN032A"],["enemy_small"],1000),
 ("enemy_huge",["EN078A"],["enemy_huge"],1000),
 ("enemy_mix4",["EN032A","EN016A","EN081A","EN078A"],["enemy_small","enemy_medium","enemy_large","enemy_huge"],1000),
 ("enemy_mix12",FIXED12,["mixed_"+x.lower() for x in FIXED12],1000),
]
MODES=[
 "recovered_model1",
 "raw_direct_p0","raw_direct_pot",
 "bptc_direct_p0","bptc_direct_pot",
 "bptc_fast_p0","bptc_fast_pot",
 "bptc_sprite_p0","bptc_sprite_pot",
]

def execute(cmd,out):
    log=out.with_suffix(".process.log")
    with log.open("w",encoding="utf8",errors="replace") as lf:
        proc=subprocess.Popen(cmd,cwd=ROOT,stdout=lf,stderr=subprocess.STDOUT,text=True,encoding="utf8",errors="replace")
        tracked={proc.pid}; peak_rss=peak_private=0; cpu_seconds=0.0
        started=time.perf_counter()
        while proc.poll() is None:
            for pid in list(tracked):
                try:
                    ps=psutil.Process(pid)
                    tracked.update(c.pid for c in ps.children(recursive=True))
                except (psutil.NoSuchProcess,psutil.AccessDenied):
                    pass
            rss=private=0;cpu=0.0
            for pid in list(tracked):
                try:
                    ps=psutil.Process(pid);mi=ps.memory_info()
                    rss+=int(mi.rss);private+=int(getattr(mi,"private",0) or 0)
                    ct=ps.cpu_times();cpu+=float(ct.user+ct.system)
                except (psutil.NoSuchProcess,psutil.AccessDenied):
                    pass
            peak_rss=max(peak_rss,rss);peak_private=max(peak_private,private);cpu_seconds=max(cpu_seconds,cpu)
            time.sleep(.01)
        proc.wait(); wall=time.perf_counter()-started
    if proc.returncode!=0 or not out.exists():
        tail=log.read_text(encoding="utf8",errors="replace")[-8000:] if log.exists() else ""
        raise RuntimeError("FAILED\n"+" ".join(map(str,cmd))+"\n"+tail)
    row=json.loads(out.read_text(encoding="utf8"))
    row.update(peak_rss_bytes=peak_rss,peak_private_bytes=peak_private,cpu_seconds=cpu_seconds,wall_seconds=wall)
    return row

def command(label,rec_names,sg_names,mode,out):
    if mode=="recovered_model1":
        return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(REC_PROJECT),"--script",str(REC_SCRIPT),"--",
                "enemy",",".join(rec_names),"1000","120","1280","720",str(out)]
    if mode.startswith("raw_direct_"):
        strategy="height_p0" if mode.endswith("_p0") else "height_pot_p0"
        return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(SG_PROJECT),"--script",str(RAW_SCRIPT),"--",
                str(RAW_ROOT/strategy),",".join(sg_names),"1000","120","1280","720","enemy",str(out)]
    parts=mode.split("_")
    renderer=parts[1]
    strategy="height_p0" if mode.endswith("_p0") else "height_pot_p0"
    return [str(GODOT),"--rendering-method","gl_compatibility","--path",str(SG_PROJECT),"--script",str(IMP_SCRIPT),"--",
            renderer,strategy,",".join(sg_names),"1000","120","1280","720",str(out)]

rows=[]
for label,rec_names,sg_names,count in CASES:
    for rep in (1,2,3):
        order=MODES if rep!=2 else list(reversed(MODES))
        for mode in order:
            out=OUT/f"{label}_{mode}_r{rep}.json"
            row=execute(command(label,rec_names,sg_names,mode,out),out)
            row.update(label=label,mode=mode,repeat=rep)
            rows.append(row)
            print("PASS",label,mode,"r",rep,
                  "rss",round(row["peak_rss_bytes"]/1048576,1),
                  "tex",round(row["texture_mem_delta"]/1048576,1),
                  "load",round(row["load_ms"],2),
                  "p95",round(row["frame_p95_ms"],3),
                  "draw",round(row["draw_calls_p95"],1),flush=True)

def med(g,key): return statistics.median(float(x.get(key,0)) for x in g)
summary=[]
for label in sorted({r["label"] for r in rows}):
    for mode in MODES:
        g=[r for r in rows if r["label"]==label and r["mode"]==mode]
        if not g: continue
        item={"label":label,"mode":mode,"trials":len(g),"renderer_path":g[0]["renderer_path"]}
        for key in ["peak_rss_bytes","peak_private_bytes","cpu_seconds","wall_seconds","load_ms",
                    "frame_p50_ms","frame_p95_ms","frame_p99_ms","draw_calls_p50","draw_calls_p95",
                    "texture_mem_delta","video_mem_delta","node_delta"]:
            item[key]=med(g,key)
        summary.append(item)

report={"version":1,"conditions":{"viewport":"1280x720","instances":1000,"frames":120,"warmup":30,
        "repeats":3,"vsync":False,"bptc":True,"native_renderer_per_mode":True},
        "rows":rows,"summary":summary}
(OUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
print("\nSUMMARY")
for r in summary:
    print(r["label"],r["mode"],
          "RSS",round(r["peak_rss_bytes"]/1048576,1),
          "Tex",round(r["texture_mem_delta"]/1048576,1),
          "Load",round(r["load_ms"],2),
          "P95",round(r["frame_p95_ms"],3),
          "P99",round(r["frame_p99_ms"],3),
          "Draw",round(r["draw_calls_p95"],1),
          "CPU",round(r["cpu_seconds"],2))
