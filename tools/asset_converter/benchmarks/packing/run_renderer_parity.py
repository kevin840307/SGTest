from __future__ import annotations
"""Reproducible Windows visual renderer A/B with movement, Y-sort and original offsets."""
import json
import statistics
import subprocess
import time
from pathlib import Path
import psutil

ROOT=Path(__file__).resolve().parents[4]
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=Path(r"C:\SGO\Recovered\client")
SG_PROJECT=ROOT/".cache/bptc_matrix"
REC_SCRIPT=Path(__file__).with_name("recovered_parity_native.gd")
SG_SCRIPT=Path(__file__).with_name("parity_renderer.gd")
OUTPUT=ROOT/".cache/asset_packing_benchmark/renderer_parity_v2"
OUTPUT.mkdir(parents=True,exist_ok=True)

MONSTERS=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A",
          "EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
FOUR=["EN032A","EN016A","EN081A","EN078A"]
SAMPLE_MAP={**{x:"mixed_"+x.lower() for x in MONSTERS},
            "EN032A":"enemy_small","EN016A":"enemy_medium","EN081A":"enemy_large","EN078A":"enemy_huge"}
CASES=[
  ("small_1000",["EN032A"],1000),
  ("huge_1000",["EN078A"],1000),
  ("four_1000",FOUR,1000),
  ("twelve_200",MONSTERS,200),
  ("twelve_1000",MONSTERS,1000),
  ("twelve_2000",MONSTERS,2000),
]
MODES=["recovered","sprite","fast"]

def measure(command:list[str],out:Path)->dict:
    log=out.with_suffix(".process.log")
    with log.open("w",encoding="utf8",errors="replace") as output:
        proc=subprocess.Popen(command,cwd=ROOT,stdout=output,stderr=subprocess.STDOUT)
        tracked={proc.pid}; peak_rss=0; peak_private=0; cpu_s=0.; started=time.perf_counter()
        while proc.poll() is None:
            for pid in list(tracked):
                try:
                    p=psutil.Process(pid)
                    tracked.update(c.pid for c in p.children(recursive=True))
                except (psutil.NoSuchProcess,psutil.AccessDenied):
                    pass
            rss=0; priv=0; cpu=0.
            for pid in list(tracked):
                try:
                    p=psutil.Process(pid);m=p.memory_info();t=p.cpu_times()
                    rss+=m.rss;priv+=getattr(m,"private",0);cpu+=t.user+t.system
                except (psutil.NoSuchProcess,psutil.AccessDenied):
                    pass
            peak_rss=max(peak_rss,rss);peak_private=max(peak_private,priv)
            cpu_s=max(cpu_s,cpu)
            if time.perf_counter()-started>90:
                proc.kill()
                raise TimeoutError("timeout: "+" ".join(command))
            time.sleep(.01)
        proc.wait()
        wall=time.perf_counter()-started
    if proc.returncode!=0 or not out.exists():
        tail=log.read_text(encoding="utf8",errors="replace")[-5000:]
        raise RuntimeError("renderer parity failed: "+" ".join(command)+"\n"+tail)
    row=json.loads(out.read_text(encoding="utf8"))
    row.update(peak_rss_bytes=peak_rss,peak_private_bytes=peak_private,
               cpu_seconds=cpu_s,wall_seconds=wall)
    assert row.get("world_ysort_enabled") is True, row
    if "sprite" in row.get("renderer_path","") or "fast_canvas" in row.get("renderer_path",""):
        assert row["frame_semantics_assertions"]>0,row
    else:
        assert row["frame_origins_verified"]>0,row
    return row

def cmd(mode,ids,count,out):
    common=[str(count),"150","1280","720",str(out)]
    if mode=="recovered":
        return [str(GODOT),"--rendering-method","gl_compatibility",
                "--path",str(REC_PROJECT),"--script",str(REC_SCRIPT),"--",
                ",".join(ids),*common]
    return [str(GODOT),"--rendering-method","gl_compatibility",
            "--path",str(SG_PROJECT),"--script",str(SG_SCRIPT),"--",
            mode,"height_p0",",".join(SAMPLE_MAP[x] for x in ids),*common]

def median(rows,key):
    return statistics.median(float(r[key]) for r in rows)

def main():
    all_rows=[]
    for name,ids,count in CASES:
        for rep in (1,2,3):
            modes=MODES if rep!=2 else list(reversed(MODES))
            for mode in modes:
                out=OUTPUT/f"{name}_{mode}_r{rep}.json"
                row=measure(cmd(mode,ids,count,out),out)
                row.update(scenario=name,mode=mode,repeat=rep)
                all_rows.append(row)
                print("PASS",name,mode,rep,
                      "RSS",round(row["peak_rss_bytes"]/2**20,1),
                      "Tex",round(row["texture_mem_delta"]/2**20,1),
                      "load",round(row["load_ms"],1),
                      "P95",round(row["frame_p95_ms"],3),
                      "P99",round(row["frame_p99_ms"],3),
                      "Draw",int(row["draw_calls_p95"]),
                      flush=True)
    summary=[]
    for name,ids,count in CASES:
        for mode in MODES:
            g=[r for r in all_rows if r["scenario"]==name and r["mode"]==mode]
            entry={"scenario":name,"mode":mode,"instances":count,"trials":len(g),
                   "renderer":g[0]["renderer_path"]}
            for key in ["peak_rss_bytes","peak_private_bytes","cpu_seconds",
                         "wall_seconds","load_ms","frame_p50_ms","frame_p95_ms",
                         "frame_p99_ms","draw_calls_p95","texture_mem_delta","video_mem_delta",
                         "node_delta"]:
                entry[key]=median(g,key)
            summary.append(entry)
    report={"version":2,
            "conditions":{"viewport":"1280x720","warmup":35,"frames":150,
                          "repeats":3,"same_actions":True,"move_ysort":True,
                          "recovered_act_timing":True,"sprite_offset_updated_per_frame":True,
                          "compression":"Godot VRAM compression / GL Compatibility"},
            "results":summary,"trials":all_rows}
    (OUTPUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
    print("\nSUMMARY")
    for r in summary:
        print(r["scenario"],r["mode"],"RSS",round(r["peak_rss_bytes"]/2**20,1),
              "Tex",round(r["texture_mem_delta"]/2**20,1),
              "Load",round(r["load_ms"],1),
              "P95",round(r["frame_p95_ms"],3),
              "P99",round(r["frame_p99_ms"],3),
              "Draw",round(r["draw_calls_p95"],0))
    return 0

if __name__=="__main__":
    raise SystemExit(main())
