from __future__ import annotations
import json, statistics, subprocess, time
from pathlib import Path

ROOT=Path(r"C:\SGO\SRC\SGTest")
GODOT=Path(r"C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe")
REC_PROJECT=ROOT/".cache/recovered_renderer_project"
REC_SCRIPT=REC_PROJECT/"benchmark.gd"
SG_PROJECT=ROOT/"client"
SG_SCRIPT=ROOT/"tools/asset_converter/benchmarks/packing/sgtest_renderer.gd"
VAR_ROOT=ROOT/".cache/asset_packing_benchmark/model1_variants"
OUT=ROOT/".cache/asset_packing_benchmark/model1_renderer_trials"
OUT.mkdir(parents=True,exist_ok=True)

FIXED12=["EN114A","EN089A","EN121A","EN116A","EN023A","EN084A","EN111A","EN125A","EN050C","EN080A","EN093A","EN079A"]
SCENERY=[
"Map/OBJECT/WATER_OBJECT/WATER_OBJECT_01.spr",
"Map/OBJECT/hold_object/R015_HOLE_OBJECT_C_L.spr",
"Map/OBJECT/Plant&Tree/A008_STAR_RIVER_E_R.spr",
"Map/OBJECT/hold_object/A_BROSS_ROOM.spr",
]
SCENARIOS=[
("enemy_small","monster",["EN032A"],["enemy_small"],1000),
("enemy_medium","monster",["EN016A"],["enemy_medium"],1000),
("enemy_large","monster",["EN081A"],["enemy_large"],1000),
("enemy_huge","monster",["EN078A"],["enemy_huge"],1000),
("enemy_mix4","monster",["EN032A","EN016A","EN081A","EN078A"],["enemy_small","enemy_medium","enemy_large","enemy_huge"],1000),
("enemy_mix12","monster",FIXED12,["mixed_"+x.lower() for x in FIXED12],1000),
("weapon_small","part",["B-Dagger006"],["weapon_small"],300),
("weapon_huge","part",["B-Stick011"],["weapon_huge"],300),
("weapon_mix4","part",["B-Dagger006","B-Sword024","B-Stick003","B-Stick011"],["weapon_small","weapon_medium","weapon_large","weapon_huge"],300),
("scenery_small","scenery",[SCENERY[0]],["scenery_small"],300),
("scenery_huge","scenery",[SCENERY[3]],["scenery_huge"],300),
("scenery_mix4","scenery",SCENERY,["scenery_small","scenery_medium","scenery_large","scenery_huge"],300),
]
STRATEGIES=[
("recovered_model1","recovered"),
("height_p0","nearest"),
("height_p1","linear"),
("height_p2","linear"),
("maxrects_p1","linear"),
]

def run(cmd,out):
    p=subprocess.run(cmd,cwd=ROOT,capture_output=True,text=True,encoding="utf8",errors="replace",timeout=90)
    if p.returncode!=0 or not out.exists():
        print(p.stdout[-3000:]);print(p.stderr[-3000:])
        raise RuntimeError("benchmark failed")
    return json.loads(out.read_text())

rows=[]
for repeat in (1,2,3):
    strategies=STRATEGIES if repeat!=2 else list(reversed(STRATEGIES))
    for scenario,kind,rec_ids,cand_ids,instances in SCENARIOS:
        for strategy,filter_name in strategies:
            out=OUT/f"{scenario}_{strategy}_r{repeat}.json"
            if strategy=="recovered_model1":
                cmd=[str(GODOT),"--disable-vsync","--max-fps","0","--rendering-method","gl_compatibility",
                     "--path",str(REC_PROJECT),"--script","res://benchmark.gd","--",
                     kind,",".join(rec_ids),str(instances),"150",str(out),"40"]
            else:
                cmd=[str(GODOT),"--disable-vsync","--max-fps","0","--rendering-method","gl_compatibility",
                     "--path",str(SG_PROJECT),"--script",str(SG_SCRIPT),"--",
                     strategy,str(VAR_ROOT),",".join(cand_ids),str(instances),"150",str(out),"40",filter_name]
            t=time.perf_counter()
            d=run(cmd,out);d.update(scenario=scenario,strategy=strategy,repeat=repeat,kind=kind,wall=time.perf_counter()-t)
            rows.append(d)
            print(f"PASS r{repeat} {scenario:14} {strategy:18} load={d['load_ms']:.1f} tex={d['texture_with_instances_delta']/1048576:.2f}MiB draw={d['draw_p95']:.0f} p95={d['frame_p95_ms']:.3f} wall={d['wall']:.2f}",flush=True)

metrics=["load_ms","texture_after_load_delta","texture_with_instances_delta","video_after_load_delta","video_with_instances_delta",
         "node_count","resource_count","update_p95_ms","frame_p50_ms","frame_p95_ms","frame_p99_ms","draw_p50","draw_p95"]
summary=[]
for scenario,kind,rec_ids,cand_ids,instances in SCENARIOS:
    for strategy,_ in STRATEGIES:
        group=[r for r in rows if r["scenario"]==scenario and r["strategy"]==strategy]
        item={"scenario":scenario,"kind":kind,"strategy":strategy,"instances":instances,"trials":len(group)}
        for m in metrics:item[m]=statistics.median(float(r[m]) for r in group)
        summary.append(item)
report={"version":1,"repeats":3,"rows":summary}
(OUT/"summary.json").write_text(json.dumps(report,indent=2),encoding="utf8")
print("SUMMARY",OUT/"summary.json")
