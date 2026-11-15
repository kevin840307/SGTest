from __future__ import annotations
import json, statistics, subprocess, time, threading
from pathlib import Path
import psutil

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=Path(r"C:\SGO\Recovered\client")
SG_PROJECT=ROOT/"client"
REC_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/recovered_native.gd"
SG_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_native.gd"
VARIANTS=ROOT/".cache/asset_packing_benchmark/model1_variants"
OUT=ROOT/".cache/asset_packing_benchmark/runtime_resources"
OUT.mkdir(parents=True,exist_ok=True)

FIXED12=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]

CASES=[
 ("enemy_small","enemy",["EN032A"],["enemy_small"],1000,["recovered_model1","height_p0"]),
 ("enemy_medium","enemy",["EN016A"],["enemy_medium"],1000,["recovered_model1","height_p0"]),
 ("enemy_large","enemy",["EN081A"],["enemy_large"],1000,["recovered_model1","height_p0"]),
 ("enemy_huge","enemy",["EN078A"],["enemy_huge"],1000,["recovered_model1","height_p0"]),
 ("enemy_mix4","enemy",["EN032A","EN016A","EN081A","EN078A"],
  ["enemy_small","enemy_medium","enemy_large","enemy_huge"],1000,
  ["recovered_model1","height_p0","height_p1","height_p2","maxrects_p1"]),
 ("enemy_mix12","enemy",FIXED12,["mixed_"+x.lower() for x in FIXED12],1000,
  ["recovered_model1","height_p0","height_p1","height_p2","maxrects_p1"]),
 ("weapon_small","weapon",["B-Dagger006"],["weapon_small"],300,["recovered_model1","height_p0"]),
 ("weapon_medium","weapon",["B-Sword024"],["weapon_medium"],300,["recovered_model1","height_p0"]),
 ("weapon_large","weapon",["B-Stick003"],["weapon_large"],300,["recovered_model1","height_p0"]),
 ("weapon_huge","weapon",["B-Stick011"],["weapon_huge"],300,["recovered_model1","height_p0"]),
 ("weapon_mix4","weapon",["B-Dagger006","B-Sword024","B-Stick003","B-Stick011"],
  ["weapon_small","weapon_medium","weapon_large","weapon_huge"],300,
  ["recovered_model1","height_p0","height_p1","height_p2","maxrects_p1"]),
 ("equipment_small","equipment",["G-Bonnet092"],["equipment_small"],300,["recovered_model1","height_p0"]),
 ("equipment_medium","equipment",["A-Back023"],["equipment_medium"],300,["recovered_model1","height_p0"]),
 ("equipment_large","equipment",["R-Back029"],["equipment_large"],300,["recovered_model1","height_p0"]),
 ("equipment_huge","equipment",["R-Back026"],["equipment_huge"],300,["recovered_model1","height_p0"]),
 ("equipment_mix4","equipment",["G-Bonnet092","A-Back023","R-Back029","R-Back026"],
  ["equipment_small","equipment_medium","equipment_large","equipment_huge"],300,
  ["recovered_model1","height_p0","height_p1","height_p2","maxrects_p1"]),
 ("scenery_mix4","scenery",[],["scenery_small","scenery_medium","scenery_large","scenery_huge"],600,
  ["height_p0","height_p1","height_p2","maxrects_p1"]),
]

def monitor_process(proc: subprocess.Popen):
    peak_rss=0
    peak_private=0
    latest_cpu=0.0
    sample_count=0
    ps=None
    try:
        ps=psutil.Process(proc.pid)
    except Exception:
        pass
    while proc.poll() is None:
        if ps is not None:
            try:
                mi=ps.memory_info()
                peak_rss=max(peak_rss,int(mi.rss))
                peak_private=max(peak_private,int(getattr(mi,"private",0) or 0))
                ct=ps.cpu_times()
                latest_cpu=float(ct.user+ct.system)
                sample_count+=1
            except (psutil.NoSuchProcess,psutil.AccessDenied):
                pass
        time.sleep(0.01)
    if ps is not None:
        try:
            mi=ps.memory_info()
            peak_rss=max(peak_rss,int(mi.rss))
            peak_private=max(peak_private,int(getattr(mi,"private",0) or 0))
            ct=ps.cpu_times()
            latest_cpu=float(ct.user+ct.system)
        except Exception:
            pass
    return peak_rss,peak_private,latest_cpu,sample_count

def execute(cmd,out):
    started=time.perf_counter()
    log_path=out.with_suffix(".process.log")
    log_file=log_path.open("w",encoding="utf8",errors="replace")
    proc=subprocess.Popen(cmd,cwd=ROOT,stdout=log_file,stderr=subprocess.STDOUT,
                          text=True,encoding="utf8",errors="replace")
    peak_rss=peak_private=0;cpu_seconds=0.0;samples=0
    tracked={proc.pid}
    while proc.poll() is None:
        # Godot console spawns the real Windows renderer process. Measure the
        # whole process tree so the value matches Task Manager much more closely.
        for pid in list(tracked):
            try:
                ps=psutil.Process(pid)
                tracked.update(child.pid for child in ps.children(recursive=True))
            except (psutil.NoSuchProcess,psutil.AccessDenied):
                pass
        rss_now=private_now=0; cpu_now=0.0
        for pid in list(tracked):
            try:
                ps=psutil.Process(pid)
                mi=ps.memory_info()
                rss_now+=int(mi.rss)
                private_now+=int(getattr(mi,"private",0) or 0)
                ct=ps.cpu_times(); cpu_now+=float(ct.user+ct.system)
            except (psutil.NoSuchProcess,psutil.AccessDenied):
                pass
        peak_rss=max(peak_rss,rss_now)
        peak_private=max(peak_private,private_now)
        cpu_seconds=max(cpu_seconds,cpu_now)
        samples+=1
        time.sleep(.01)
    proc.wait()
    log_file.close()
    wall=time.perf_counter()-started
    if proc.returncode!=0 or not out.exists():
        tail=log_path.read_text(encoding="utf8",errors="replace")[-8000:] if log_path.exists() else ""
        raise RuntimeError("FAILED\n"+" ".join(map(str,cmd))+"\n"+tail)
    row=json.loads(out.read_text(encoding="utf8"))
    row.update(peak_rss_bytes=peak_rss,peak_private_bytes=peak_private,
               cpu_seconds=cpu_seconds,wall_seconds=wall,monitor_samples=samples)
    return row

def run_case(label,kind,rec_names,sg_names,count,mode,rep):
    out=OUT/f"{label}_{mode}_r{rep}.json"
    if mode=="recovered_model1":
        cmd=[str(GODOT),"--rendering-method","gl_compatibility","--path",str(REC_PROJECT),
             "--script",str(REC_SCRIPT),"--",kind,",".join(rec_names),str(count),"120","1280","720",str(out)]
    else:
        cmd=[str(GODOT),"--rendering-method","gl_compatibility","--path",str(SG_PROJECT),
             "--script",str(SG_SCRIPT),"--",str(VARIANTS/mode),",".join(sg_names),str(count),
             "120","1280","720",kind,str(out)]
    row=execute(cmd,out)
    row.update(label=label,mode=mode,repeat=rep)
    print("PASS",label,mode,"r",rep,
          "rssMiB",round(row["peak_rss_bytes"]/1048576,1),
          "texMiB",round(row["texture_mem_delta"]/1048576,1),
          "load",round(row["load_ms"],1),
          "p95",round(row["frame_p95_ms"],3),
          "draw",round(row["draw_calls_p95"],1),flush=True)
    return row

rows=[]
for label,kind,rec_names,sg_names,count,modes in CASES:
    for rep in (1,2,3):
        order=modes if rep!=2 else list(reversed(modes))
        for mode in order:
            rows.append(run_case(label,kind,rec_names,sg_names,count,mode,rep))

def med(group,key):
    return statistics.median(float(x.get(key,0)) for x in group)

summary=[]
for label in sorted({r["label"] for r in rows}):
    for mode in sorted({r["mode"] for r in rows if r["label"]==label}):
        g=[r for r in rows if r["label"]==label and r["mode"]==mode]
        item={"label":label,"mode":mode,"trials":len(g),"instances":g[0]["instances"],
              "renderer_path":g[0]["renderer_path"]}
        for key in ["peak_rss_bytes","peak_private_bytes","cpu_seconds","wall_seconds","load_ms",
                    "frame_p50_ms","frame_p95_ms","frame_p99_ms","draw_calls_p50","draw_calls_p95",
                    "texture_mem_delta","video_mem_delta","node_delta"]:
            item[key]=med(g,key)
        summary.append(item)

report={"version":1,"metric_priority":["frame_p95_ms","peak_rss_bytes","texture_mem_delta","load_ms","draw_calls_p95"],
        "conditions":{"viewport":"1280x720","frames":120,"warmup":30,"repeats":3,"vsync":False,
        "native_renderer_per_design":True},"rows":rows,"summary":summary}
(OUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
print("\nSUMMARY")
for r in summary:
    print(r["label"],r["mode"],
          "RSS",round(r["peak_rss_bytes"]/1048576,1),
          "Tex",round(r["texture_mem_delta"]/1048576,1),
          "Load",round(r["load_ms"],1),
          "P95",round(r["frame_p95_ms"],3),
          "P99",round(r["frame_p99_ms"],3),
          "Draw",round(r["draw_calls_p95"],1),
          "CPU",round(r["cpu_seconds"],2))
