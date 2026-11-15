from __future__ import annotations
"""Run a controlled Map13 scenery+AOI visual stress matrix on Windows.

Independent enemy assets stay independent; actual Map13 object placements
are read from the unmodified SGTest client/assets folder. Player/drops are
placeholder geometry, NOT actual player paper-doll or network/server workload.
"""
import argparse
import json
import os
import statistics
import subprocess
import time
from pathlib import Path
import psutil

ROOT=Path(__file__).resolve().parents[4]
GODOT=Path(os.environ.get("SGTEST_GODOT",r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"))
RECOVERED=Path(os.environ.get("SGTEST_RECOVERED_CLIENT",r"C:\SGO\Recovered\client"))
PACK_PROJECT=Path(os.environ.get("SGTEST_COMPRESSED_PROJECT",str(ROOT/".cache/bptc_matrix")))
ASSETS=Path(os.environ.get("SGTEST_ASSETS_ROOT",str(ROOT/"client/assets")))
SCRIPT=Path(__file__).with_name("mixed_map13_renderer.gd")
OUT=ROOT/".cache/asset_packing_benchmark/map13_mix_v1"
OUT.mkdir(parents=True,exist_ok=True)
SCENARIOS=[("300",300),("1000",1000),("2000",2000)]
MODES=["recovered","sprite"]
FRAMES=150

def benchmark(name:str,count:int,mode:str,rep:int)->dict:
    out=OUT/f"{name}_{mode}_r{rep}.json"
    log=out.with_suffix(".log")
    # Fail closed: do not accept output left over from a previous run.
    if out.exists():
        out.unlink()
    cmd=[str(GODOT),"--rendering-method","gl_compatibility",
         "--path",str(RECOVERED if mode=="recovered" else PACK_PROJECT),
         "--script",str(SCRIPT),"--",mode,str(ASSETS),str(count),
         str(FRAMES),str(out),"1280"]
    started=time.perf_counter()
    with log.open("w",encoding="utf8",errors="replace") as handle:
        proc=subprocess.Popen(cmd,cwd=ROOT,stdout=handle,stderr=subprocess.STDOUT)
        children={proc.pid}
        peak_rss=0;peak_private=0;cpu=0.0
        while proc.poll() is None:
            for pid in list(children):
                try:
                    ps=psutil.Process(pid)
                    children.update(child.pid for child in ps.children(recursive=True))
                except (psutil.AccessDenied,psutil.NoSuchProcess):
                    continue
            rss=private=0;last_cpu=0.0
            for pid in list(children):
                try:
                    ps=psutil.Process(pid)
                    mem=ps.memory_info();times=ps.cpu_times()
                    rss+=mem.rss;private+=getattr(mem,"private",0)
                    last_cpu+=times.user+times.system
                except (psutil.AccessDenied,psutil.NoSuchProcess):
                    continue
            peak_rss=max(peak_rss,rss);peak_private=max(peak_private,private)
            cpu=max(cpu,last_cpu)
            if time.perf_counter()-started>120:
                proc.kill()
                raise TimeoutError("Godot renderer test exceeded 120s: "+name+" "+mode)
            time.sleep(.01)
        proc.wait()
    if proc.returncode!=0 or not out.exists():
        raise RuntimeError(f"FAIL {name} {mode} exit={proc.returncode}\n"+
                           log.read_text(encoding="utf8",errors="replace")[-6000:])
    raw=log.read_text(encoding="utf8",errors="replace")
    if "SCRIPT ERROR:" in raw or "ERROR:" in raw:
        raise RuntimeError("Godot logged errors: "+raw[-4000:])
    data=json.loads(out.read_text(encoding="utf8"))
    assert data["enemies"]==count and data["mode"]==mode
    assert data["scenery"]>0 and data["scenery_types"]>=10
    assert data["spawn"]==data["despawn"] and data["spawn"]>0
    assert data["action_switches"]>0 and data["world_ysort_enabled"]
    assert data["action_views_per_asset"]==14
    assert data["draw_calls_p95"]>0 and data["texture_delta"]>0
    data.update(scenario=name,repeat=rep,peak_rss_bytes=peak_rss,
                peak_private_bytes=peak_private,cpu_seconds=cpu,
                wall_seconds=time.perf_counter()-started)
    print("PASS",name,mode,rep,"RSS",round(peak_rss/2**20,1),
          "tex",round(data["texture_delta"]/2**20,1),
          "load",round(data["load_ms"],1),
          "p95",round(data["p95_ms"],3),"p99",round(data["p99_ms"],3),
          "gap",data["gaps_over_16_7_ms"],flush=True)
    return data

def main():
    global FRAMES, OUT
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--counts",nargs="+",type=int,default=[300,1000,2000])
    parser.add_argument("--repeats",type=int,default=3)
    parser.add_argument("--frames",type=int,default=150)
    parser.add_argument("--output-dir",type=Path,default=OUT)
    options=parser.parse_args()
    if options.repeats<1 or options.frames<60 or any(n<10 for n in options.counts):
        parser.error("counts >=10, repeats >=1, frames >=60 are required")
    FRAMES=options.frames
    OUT=options.output_dir.resolve()
    OUT.mkdir(parents=True,exist_ok=True)
    for p in (GODOT,RECOVERED,PACK_PROJECT,ASSETS/"maps/13/world.json"):
        if not p.exists():
            parser.error("missing required file/directory: "+str(p))
    scenarios=[(str(count),count) for count in options.counts]
    results=[]
    for name,count in scenarios:
        for rep in range(1,options.repeats+1):
            for mode in (MODES if rep!=2 else list(reversed(MODES))):
                results.append(benchmark(name,count,mode,rep))
    summary=[]
    for name,count in scenarios:
        for mode in MODES:
            rows=[x for x in results if x["scenario"]==name and x["mode"]==mode]
            entry={"scenario":name,"mode":mode,"trials":len(rows),"enemies":count}
            for key in ["peak_rss_bytes","peak_private_bytes","cpu_seconds","wall_seconds",
                        "load_ms","scenery_load_ms","p50_ms","p95_ms","p99_ms",
                        "draw_calls_p95","texture_delta","node_delta","gaps_over_16_7_ms"]:
                entry[key]=statistics.median(float(x[key]) for x in rows)
            entry["scenery"]=rows[0]["scenery"]
            entry["scenery_types"]=rows[0]["scenery_types"]
            entry["players_placeholder"]=rows[0]["players_placeholder"]
            entry["drops_markers"]=rows[0]["drops_markers"]
            summary.append(entry)
    (OUT/"summary.json").write_text(json.dumps({"schema":1,"scenario":"Map13 real independent scenery, 20 player placeholders, 500 drop markers, 12 Enemy types, AOI churn", "warmup":35,"frames":FRAMES,"repeats":options.repeats,"summary":summary,"trials":results},indent=2),encoding="utf8")
    print("SUMMARY")
    for r in summary:
        print(r["scenario"],r["mode"],"RSS",round(r["peak_rss_bytes"]/2**20,1),
              "Tex",round(r["texture_delta"]/2**20,1),
              "Load",round(r["load_ms"],1),"P95",round(r["p95_ms"],3),
              "P99",round(r["p99_ms"],3),"Draw",round(r["draw_calls_p95"],0),
              "gaps",round(r["gaps_over_16_7_ms"],0))

if __name__=="__main__":main()
