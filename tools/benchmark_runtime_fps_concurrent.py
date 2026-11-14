#!/usr/bin/env python3
"""Concurrent same-server FPS matrix with authoritative state checkpoint hashes."""
from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import tempfile
import time
from pathlib import Path

from benchmark_runtime_isolation import parse_traces, process_sample, stop, summarize
from runtime_processes import server_command


def checkpoints(text: str) -> dict[int,str]:
    result={}
    for line in text.splitlines():
        marker="state_checkpoint "
        if marker not in line:
            continue
        try:
            row=json.loads(line.split(marker,1)[1])
            result[int(row["tick"])]=str(row["hash"])
        except (json.JSONDecodeError,KeyError,TypeError,ValueError):
            pass
    return result


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--fps",type=int,nargs="+",default=[30,45,60,75,90])
    parser.add_argument("--seconds",type=int,default=18)
    parser.add_argument("--output",type=Path,default=Path("reports/runtime_fps_concurrent.json"))
    args=parser.parse_args()
    if args.seconds<15: parser.error("--seconds must be >=15")
    godot=str(args.godot.resolve())
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    server=subprocess.Popen(server_command()+["--monsters=0","--drops=0","--map=13"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    processes=[]; logs=[]; resources=[]
    try:
        time.sleep(1)
        for index,fps in enumerate(args.fps):
            log=tempfile.TemporaryFile(mode="w+",encoding="utf-8"); logs.append(log)
            command=[godot,"--path","client","--rendering-method","gl_compatibility",
                     "--resolution","1440x900","--",
                     "--bot","--bot-index=1","--runtime-trace","--unfocus-low-power=0",
                     f"--render-fps={fps}"]
            processes.append(subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT))
            time.sleep(.15)
        started=time.time(); last_sample=0.0
        while time.time()-started<args.seconds:
            if server.poll() is not None or any(p.poll() is not None for p in processes):
                raise RuntimeError("concurrent FPS process exited early")
            now=time.time()
            if now-last_sample>=1.0:
                last_sample=now
                resources.append({"elapsed_s":now-started,
                                  "clients":[process_sample(p.pid) for p in processes],
                                  "server":process_sample(server.pid)})
            time.sleep(.1)
    finally:
        for process in processes: stop(process)
        stop(server)
    client_text=[]
    for log in logs:
        log.seek(0); client_text.append(log.read()); log.close()
    server_log.seek(0); server_text=server_log.read(); server_log.close()

    cases=[]
    checkpoint_maps=[]
    passed=True
    for fps,text in zip(args.fps,client_text):
        traces=parse_traces(text)
        summary=summarize(traces)
        summary.update({"fps_cap":fps,
                        "prediction_ready":"prediction_ready" in text,
                        "snapshot_received":"snapshot_received" in text,
                        "error_lines":sum("ERROR:" in line or "SCRIPT ERROR" in line for line in text.splitlines())})
        current_checkpoints=checkpoints(text); checkpoint_maps.append(current_checkpoints)
        summary["checkpoint_ticks"]=sorted(current_checkpoints)
        summary["execution_passed"]=bool(traces) and summary["prediction_ready"] and summary["snapshot_received"] and summary["error_lines"]==0
        passed=summary["execution_passed"] and passed
        cases.append({"summary":summary,"traces":traces,"checkpoints":current_checkpoints})

    common=set(checkpoint_maps[0]) if checkpoint_maps else set()
    for current in checkpoint_maps[1:]:
        common.intersection_update(current)
    common=sorted(common)
    comparisons=[]
    state_hash_match=True
    for tick in common:
        hashes=[current[tick] for current in checkpoint_maps]
        match=len(set(hashes))==1
        comparisons.append({"tick":tick,"match":match,"hashes":hashes})
        state_hash_match=state_hash_match and match
    stable_common=[row for row in comparisons if row["tick"]>=150]
    if not stable_common:
        state_hash_match=False
    passed=passed and state_hash_match

    physics=[row["summary"].get("physics_hz_median") for row in cases]
    tx=[row["summary"].get("tx_hz_median") for row in cases]
    inputs=[row["summary"].get("input_hz_median") for row in cases]
    cadence_pass=all(value is not None and 29.0<=value<=31.0 for value in physics)
    cadence_pass=cadence_pass and all(value is not None and 29.0<=value<=31.0 for value in inputs)
    if tx and all(value is not None for value in tx):
        cadence_pass=cadence_pass and max(tx)-min(tx)<=1.0
    else:
        cadence_pass=False
    passed=passed and cadence_pass

    aggregate_resources=[]
    for sample in resources:
        aggregate_resources.append({
            "elapsed_s":sample["elapsed_s"],
            "client_rss_mib_sum":sum(row["rss_bytes"] for row in sample["clients"])/1048576.0,
            "server_rss_mib":sample["server"]["rss_bytes"]/1048576.0,
        })

    result={"version":1,"fps":args.fps,"seconds":args.seconds,
            "same_server":True,"same_deterministic_input":True,
            "cases":cases,"checkpoint_comparisons":comparisons,
            "state_hash_match":state_hash_match,"cadence_pass":cadence_pass,
            "aggregate_resources":aggregate_resources,
            "server_error_lines":sum("fail:" in line.lower() or "Unhandled" in line for line in server_text.splitlines()),
            "execution_passed":passed}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({
        "execution_passed":passed,
        "state_hash_match":state_hash_match,
        "common_ticks":common,
        "cadence_pass":cadence_pass,
        "physics_hz":physics,
        "input_hz":inputs,
        "tx_hz":tx,
        "queue_p99":[row["summary"].get("queue_age_p99_ms_worst") for row in cases],
        "rss_sum_peak_mib":max((row["client_rss_mib_sum"] for row in aggregate_resources),default=0),
    },indent=2))
    return 0 if passed else 1


if __name__=="__main__":
    raise SystemExit(main())
