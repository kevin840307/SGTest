#!/usr/bin/env python3
"""Compare foreground baseline vs real Windows background transition state hashes."""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from benchmark_runtime_fps_concurrent import checkpoints
from benchmark_runtime_isolation import parse_traces, stop, summarize, window_action
from runtime_processes import server_command


def gui_executable(godot:str) -> str:
    if sys.platform=="win32" and godot.lower().endswith("_console.exe"):
        candidate=godot[:-12]+".exe"
        if Path(candidate).is_file():
            return candidate
    return godot


def run(godot:str,transition:str,seconds:int,disable_unfocus:bool=False) -> dict:
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    base_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    bg_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    server=subprocess.Popen(server_command()+["--monsters=0","--drops=0","--map=13"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    baseline=background=None
    events=[]
    try:
        time.sleep(1)
        baseline=subprocess.Popen(
            [godot,"--path","client","--rendering-method","gl_compatibility","--resolution","1440x900",
             "--max-fps","30","--","--bot","--bot-index=1","--runtime-trace","--unfocus-low-power=0"],
            stdout=base_log,stderr=subprocess.STDOUT)
        time.sleep(.15)
        background_args=[gui_executable(godot),"--path","client","--rendering-method","gl_compatibility",
                         "--resolution","1440x900","--max-fps","30","--","--bot","--bot-index=1","--runtime-trace"]
        if disable_unfocus:
            background_args.append("--unfocus-low-power=0")
        background=subprocess.Popen(background_args,stdout=bg_log,stderr=subprocess.STDOUT)
        started=time.time(); changed=restored=False
        while time.time()-started<seconds:
            if server.poll() is not None or baseline.poll() is not None or background.poll() is not None:
                raise RuntimeError("background comparison process exited early")
            elapsed=time.time()-started
            if not changed and elapsed>=5.5:
                changed=True
                events.append({"elapsed_s":elapsed,"action":transition,
                               "success":window_action(background.pid,transition)})
            if changed and not restored and elapsed>=11.0:
                restored=True
                events.append({"elapsed_s":elapsed,"action":"restore",
                               "success":window_action(background.pid,"restore")})
            time.sleep(.1)
    finally:
        for process in (baseline,background):
            if process is not None: stop(process)
        stop(server)
    texts=[]
    for log in (base_log,bg_log):
        log.seek(0); texts.append(log.read()); log.close()
    server_log.seek(0); server_text=server_log.read(); server_log.close()

    rows=[]
    maps=[]
    for label,text in zip(("foreground","background"),texts):
        traces=parse_traces(text); cp=checkpoints(text); maps.append(cp)
        summary=summarize(traces)
        summary.update({
            "label":label,
            "checkpoint_ticks":sorted(cp),
            "prediction_ready":"prediction_ready" in text,
            "snapshot_received":"snapshot_received" in text,
            "error_lines":sum("ERROR:" in line or "SCRIPT ERROR" in line for line in text.splitlines()),
        })
        rows.append({"summary":summary,"traces":traces,"checkpoints":cp})

    common=sorted(set(maps[0]).intersection(maps[1]))
    comparisons=[]
    hash_pass=bool(common)
    for tick in common:
        match=maps[0][tick]==maps[1][tick]
        comparisons.append({"tick":tick,"match":match,"hashes":[maps[0][tick],maps[1][tick]]})
        hash_pass=hash_pass and match
    stable=[row for row in comparisons if row["tick"]>=150]
    hash_pass=hash_pass and bool(stable)
    transition_pass=(len(events)==2 and all(event["success"] for event in events))
    cadence_pass=True
    for row in rows:
        s=row["summary"]
        cadence_pass=(cadence_pass and 29.0<=float(s.get("physics_hz_median",0))<=31.0
                      and 29.0<=float(s.get("input_hz_median",0))<=31.0
                      and float(s.get("queue_age_p99_ms_worst",999))<50.0)
    background_traces=rows[1]["traces"]
    state_observed=False
    if transition=="minimize":
        state_observed=any(int(row.get("window_mode",-1))==1 for row in background_traces)
        visual_policy=any(int(row.get("window_mode",-1))==1 and float(row.get("visual_update_hz",999))<1.0
                          for row in background_traces)
    else:
        state_observed=any(not bool(row.get("window_focused",True)) for row in background_traces)
        if disable_unfocus:
            visual_policy=any(not bool(row.get("window_focused",True))
                              and 28.0<=float(row.get("visual_update_hz",-1))<=31.0
                              for row in background_traces)
        else:
            visual_policy=any(not bool(row.get("window_focused",True))
                              and 13.0<=float(row.get("visual_update_hz",-1))<=17.0
                              for row in background_traces)
    restored=any(bool(row.get("window_focused",False)) and int(row.get("window_mode",-1))==0
                 and float(row.get("elapsed_s",0))>11 for row in background_traces)
    passed=(hash_pass and transition_pass and cadence_pass and state_observed and visual_policy and restored
            and all(row["summary"]["prediction_ready"] and row["summary"]["snapshot_received"]
                    and row["summary"]["error_lines"]==0 for row in rows)
            and not any("fail:" in line.lower() or "Unhandled" in line for line in server_text.splitlines()))
    return {
        "transition":transition,"seconds":seconds,"disable_unfocus":disable_unfocus,
        "events":events,"clients":rows,
        "common_ticks":common,"checkpoint_comparisons":comparisons,
        "state_hash_match":hash_pass,"transition_pass":transition_pass,
        "cadence_pass":cadence_pass,"background_state_observed":state_observed,
        "visual_policy_pass":visual_policy,"restore_observed":restored,
        "execution_passed":passed,
    }


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--transitions",nargs="+",choices=["minimize","unfocus"],default=["minimize","unfocus"])
    parser.add_argument("--seconds",type=int,default=18)
    parser.add_argument("--disable-unfocus",action="store_true",
                        help="disable only unfocus low-power; minimize must still stop world render")
    parser.add_argument("--output",type=Path,default=Path("reports/runtime_background_concurrent.json"))
    args=parser.parse_args()
    if args.seconds<16: parser.error("--seconds must be >=16")
    godot=str(args.godot.resolve())
    cases=[]; passed=True
    for transition in args.transitions:
        print(f"=== background state hash {transition} ===",flush=True)
        case=run(godot,transition,args.seconds,args.disable_unfocus)
        cases.append(case); passed=passed and case["execution_passed"]
        print(json.dumps({
            "transition":transition,
            "execution_passed":case["execution_passed"],
            "state_hash_match":case["state_hash_match"],
            "common_ticks":case["common_ticks"],
            "transition_pass":case["transition_pass"],
            "background_state_observed":case["background_state_observed"],
            "visual_policy_pass":case["visual_policy_pass"],
            "restore_observed":case["restore_observed"],
            "queue_p99":[row["summary"].get("queue_age_p99_ms_worst") for row in case["clients"]],
        },indent=2),flush=True)
    result={"version":1,"cases":cases,"execution_passed":passed}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    return 0 if passed else 1


if __name__=="__main__":
    raise SystemExit(main())
