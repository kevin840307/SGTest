#!/usr/bin/env python3
"""Same-server low-power presentation modes vs normal 30Hz authoritative state."""
from __future__ import annotations

import argparse
import json
import subprocess
import tempfile
import time
from pathlib import Path

from benchmark_runtime_fps_concurrent import checkpoints
from benchmark_runtime_isolation import parse_traces, stop, summarize
from runtime_processes import server_command


MODES = [
    ("normal30", []),
    ("visual5", ["--visual-fps=5"]),
    ("visual10", ["--visual-fps=10"]),
    ("visual15", ["--visual-fps=15"]),
    ("no_world", ["--no-world-render"]),
]


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--seconds",type=int,default=18)
    parser.add_argument("--output",type=Path,default=Path("reports/runtime_lowpower_concurrent.json"))
    args=parser.parse_args()
    if args.seconds<15: parser.error("--seconds must be >=15")
    godot=str(args.godot.resolve())
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    server=subprocess.Popen(server_command()+["--monsters=0","--drops=0","--map=13"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    processes=[]; logs=[]
    try:
        time.sleep(1)
        for label,extra in MODES:
            log=tempfile.TemporaryFile(mode="w+",encoding="utf-8"); logs.append(log)
            command=[godot,"--path","client","--rendering-method","gl_compatibility",
                     "--resolution","1440x900","--max-fps","30","--",
                     "--bot","--bot-index=1","--runtime-trace","--unfocus-low-power=0",*extra]
            processes.append(subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT))
            time.sleep(.15)
        started=time.time()
        while time.time()-started<args.seconds:
            if server.poll() is not None or any(p.poll() is not None for p in processes):
                raise RuntimeError("low-power concurrent process exited early")
            time.sleep(.1)
    finally:
        for process in processes: stop(process)
        stop(server)

    texts=[]
    for log in logs:
        log.seek(0); texts.append(log.read()); log.close()
    server_log.seek(0); server_text=server_log.read(); server_log.close()

    cases=[]; maps=[]; passed=True
    for (label,_),text in zip(MODES,texts):
        traces=parse_traces(text); summary=summarize(traces)
        cp=checkpoints(text); maps.append(cp)
        summary.update({
            "mode":label,
            "checkpoint_ticks":sorted(cp),
            "prediction_ready":"prediction_ready" in text,
            "snapshot_received":"snapshot_received" in text,
            "error_lines":sum("ERROR:" in line or "SCRIPT ERROR" in line for line in text.splitlines()),
        })
        summary["execution_passed"]=bool(traces) and summary["prediction_ready"] and summary["snapshot_received"] and summary["error_lines"]==0
        passed=passed and summary["execution_passed"]
        cases.append({"summary":summary,"traces":traces,"checkpoints":cp})

    common=set(maps[0]) if maps else set()
    for current in maps[1:]: common.intersection_update(current)
    common=sorted(common)
    comparisons=[]; hashes_pass=bool(common)
    for tick in common:
        hashes=[current[tick] for current in maps]
        match=len(set(hashes))==1
        comparisons.append({"tick":tick,"match":match,"hashes":hashes})
        hashes_pass=hashes_pass and match
    stable=[row for row in comparisons if row["tick"]>=150]
    hashes_pass=hashes_pass and bool(stable)

    cadence_pass=True
    for case in cases:
        s=case["summary"]
        cadence_pass=cadence_pass and 29.0<=float(s.get("physics_hz_median",0))<=31.0
        cadence_pass=cadence_pass and 20.0<=float(s.get("rx_hz_median",0))<=22.0
        cadence_pass=cadence_pass and 29.0<=float(s.get("input_hz_median",0))<=31.0
        cadence_pass=cadence_pass and 30.0<=float(s.get("tx_hz_median",0))<=32.0
        cadence_pass=cadence_pass and float(s.get("queue_age_p99_ms_worst",999))<50.0
    visual_expectations={
        "normal30":(29.0,31.0),
        "visual5":(4.0,6.0),
        "visual10":(9.0,11.0),
        "visual15":(14.0,16.0),
        "no_world":(-.1,.1),
    }
    for case in cases:
        low,high=visual_expectations[case["summary"]["mode"]]
        value=float(case["summary"].get("visual_update_hz_median",-999))
        cadence_pass=cadence_pass and low<=value<=high

    passed=passed and hashes_pass and cadence_pass
    result={
        "version":1,
        "same_server":True,
        "same_deterministic_input":True,
        "seconds":args.seconds,
        "cases":cases,
        "common_ticks":common,
        "checkpoint_comparisons":comparisons,
        "state_hash_match":hashes_pass,
        "cadence_pass":cadence_pass,
        "server_error_lines":sum("fail:" in line.lower() or "Unhandled" in line for line in server_text.splitlines()),
        "execution_passed":passed,
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({
        "execution_passed":passed,
        "state_hash_match":hashes_pass,
        "common_ticks":common,
        "cadence_pass":cadence_pass,
        "modes":[{
            "mode":case["summary"]["mode"],
            "physics":case["summary"].get("physics_hz_median"),
            "visual":case["summary"].get("visual_update_hz_median"),
            "rx":case["summary"].get("rx_hz_median"),
            "input":case["summary"].get("input_hz_median"),
            "tx":case["summary"].get("tx_hz_median"),
            "queue_p99":case["summary"].get("queue_age_p99_ms_worst"),
        } for case in cases]
    },indent=2))
    return 0 if passed else 1


if __name__=="__main__":
    raise SystemExit(main())
