from __future__ import annotations
import json, statistics, subprocess, time
from pathlib import Path
import psutil

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/bptc_native.gd"
PROJECTS={
 "bptc":ROOT/".cache/bptc_matrix",
 "s3tc":ROOT/".cache/s3tc_matrix",
 "basis":ROOT/".cache/basis_matrix",
}
OUT=ROOT/".cache/asset_packing_benchmark/compression_matrix"
OUT.mkdir(parents=True,exist_ok=True)
FIXED12=["mixed_"+x for x in ["en114a","en089a","en121a","en116a","en023a","en084a","en111a","en125a","en050c","en080a","en093a","en079a"]]
CASES=[
 ("enemy_huge","enemy",["enemy_huge"],1000),
 ("enemy_mix12","enemy",FIXED12,1000),
 ("weapon_mix4","weapon",["weapon_small","weapon_medium","weapon_large","weapon_huge"],300),
 ("equipment_mix4","equipment",["equipment_small","equipment_medium","equipment_large","equipment_huge"],300),
]

def execute(cmd,out):
    log=out.with_suffix(".process.log"); f=log.open("w",encoding="utf8",errors="replace")
    proc=subprocess.Popen(cmd,cwd=ROOT,stdout=f,stderr=subprocess.STDOUT,text=True,encoding="utf8",errors="replace")
    tracked={proc.pid};peak=private=cpu=0.0
    while proc.poll() is None:
        for pid in list(tracked):
            try:
                p=psutil.Process(pid); tracked.update(c.pid for c in p.children(recursive=True))
            except (psutil.NoSuchProcess,psutil.AccessDenied): pass
        rss_now=private_now=cpu_now=0.0
        for pid in list(tracked):
            try:
                p=psutil.Process(pid); mi=p.memory_info()
                rss_now+=mi.rss; private_now+=getattr(mi,"private",0) or 0
                ct=p.cpu_times(); cpu_now+=ct.user+ct.system
            except (psutil.NoSuchProcess,psutil.AccessDenied): pass
        peak=max(peak,rss_now); private=max(private,private_now); cpu=max(cpu,cpu_now)
        time.sleep(.01)
    proc.wait();f.close()
    if proc.returncode!=0 or not out.exists():
        raise RuntimeError(log.read_text(encoding="utf8",errors="replace")[-8000:])
    row=json.loads(out.read_text(encoding="utf8"))
    row.update(peak_rss_bytes=peak,peak_private_bytes=private,cpu_seconds=cpu)
    return row

rows=[]
for label,kind,samples,count in CASES:
    for rep in (1,2,3):
        order=list(PROJECTS) if rep!=2 else list(reversed(PROJECTS))
        for mode in order:
            out=OUT/f"{label}_{mode}_r{rep}.json"
            cmd=[str(GODOT),"--rendering-method","gl_compatibility","--path",str(PROJECTS[mode]),
                 "--script",str(SCRIPT),"--","res://assets/height_p0","fast",",".join(samples),
                 str(count),"120","1280","720",kind,str(out)]
            row=execute(cmd,out);row.update(label=label,mode=mode,repeat=rep);rows.append(row)
            print("PASS",label,mode,rep,"RSS",round(row["peak_rss_bytes"]/1048576,1),
                  "Tex",round(row["texture_mem_delta"]/1048576,1),"Load",round(row["load_ms"],1),
                  "P95",round(row["frame_p95_ms"],3),flush=True)
def med(g,k):return statistics.median(float(x.get(k,0)) for x in g)
summary=[]
for label in sorted({r["label"] for r in rows}):
    for mode in sorted(PROJECTS):
        g=[r for r in rows if r["label"]==label and r["mode"]==mode]
        item={"label":label,"mode":mode}
        for k in ["peak_rss_bytes","peak_private_bytes","cpu_seconds","load_ms","frame_p95_ms","frame_p99_ms","draw_calls_p95","texture_mem_delta","video_mem_delta","node_delta"]:
            item[k]=med(g,k)
        summary.append(item)
(OUT/"summary.json").write_text(json.dumps({"rows":rows,"summary":summary},indent=2),encoding="utf8")
print("\nSUMMARY")
for r in summary:
    print(r["label"],r["mode"],"RSS",round(r["peak_rss_bytes"]/1048576,1),"Tex",round(r["texture_mem_delta"]/1048576,1),
          "Load",round(r["load_ms"],1),"P95",round(r["frame_p95_ms"],3),"P99",round(r["frame_p99_ms"],3))
