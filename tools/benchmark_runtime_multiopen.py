#!/usr/bin/env python3
"""Native 1/2/4/8-client runtime isolation and resource scaling matrix."""
from __future__ import annotations

import argparse
import json
import math
import statistics
import subprocess
import tempfile
import time
from pathlib import Path

from benchmark_runtime_isolation import parse_traces, process_sample, stop, summarize
from runtime_processes import server_command


def percentile(values:list[float], fraction:float) -> float:
    if not values: return 0.0
    ordered=sorted(values)
    return ordered[min(len(ordered)-1,max(0,math.ceil(len(ordered)*fraction)-1))]


def cpu_intervals(resources:list[dict], client_index:int) -> list[float]:
    values=[]
    for previous,current in zip(resources,resources[1:]):
        elapsed=max(.001,float(current["elapsed_s"])-float(previous["elapsed_s"]))
        before=float(previous["clients"][client_index]["cpu_seconds"])
        after=float(current["clients"][client_index]["cpu_seconds"])
        values.append(max(0.0,(after-before)/elapsed*100.0))
    return values


def run_case(godot:str,count:int,seconds:int,monsters:int,drops:int) -> dict:
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    server=subprocess.Popen(server_command()+[f"--monsters={monsters}",f"--drops={drops}","--map=13"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    processes=[]; logs=[]; resources=[]
    try:
        time.sleep(1)
        for index in range(count):
            log=tempfile.TemporaryFile(mode="w+",encoding="utf-8"); logs.append(log)
            command=[godot,"--path","client","--rendering-method","gl_compatibility",
                     "--resolution","1440x900","--max-fps","60","--",
                     "--bot",f"--bot-index={index+1}","--runtime-trace","--unfocus-low-power=0"]
            processes.append(subprocess.Popen(command,stdout=log,stderr=subprocess.STDOUT))
            time.sleep(.12)
        started=time.time(); last=0.0
        while time.time()-started<seconds:
            if server.poll() is not None or any(p.poll() is not None for p in processes):
                raise RuntimeError(f"{count}-client process exited early")
            now=time.time()
            if now-last>=1.0:
                last=now
                resources.append({"elapsed_s":now-started,
                                  "clients":[process_sample(p.pid) for p in processes],
                                  "server":process_sample(server.pid)})
            time.sleep(.1)
    finally:
        for process in processes: stop(process)
        stop(server)

    texts=[]
    for log in logs:
        log.seek(0); texts.append(log.read()); log.close()
    server_log.seek(0); server_text=server_log.read(); server_log.close()

    clients=[]; passed=True
    for index,text in enumerate(texts):
        traces=parse_traces(text); summary=summarize(traces)
        cpu=cpu_intervals(resources,index)
        summary.update({
            "client_index":index+1,
            "prediction_ready":"prediction_ready" in text,
            "snapshot_received":"snapshot_received" in text,
            "error_lines":sum("ERROR:" in line or "SCRIPT ERROR" in line for line in text.splitlines()),
            "cpu_one_core_percent_median":statistics.median(cpu) if cpu else 0.0,
            "cpu_one_core_percent_p95":percentile(cpu,.95),
            "rss_mib_peak":max((row["clients"][index]["rss_bytes"] for row in resources),default=0)/1048576.0,
        })
        summary["execution_passed"]=bool(traces) and summary["prediction_ready"] and summary["snapshot_received"] and summary["error_lines"]==0
        # Under combat stress the bot may die/protect and legitimately stop move-input sends.
        # Input=30Hz is gated separately by the zero-monster same-server hash matrix;
        # multi-open stress gates reducer cadence/queue/errors/resources.
        summary["cadence_pass"]=(29.0<=float(summary.get("physics_hz_median",0))<=31.0
                                 and float(summary.get("queue_age_p99_ms_worst",999))<75.0
                                 and int(summary.get("max_incoming_depth",999))<=128)
        passed=passed and summary["execution_passed"] and summary["cadence_pass"]
        clients.append({"summary":summary,"traces":traces})

    aggregate_cpu=[]
    for i in range(max(0,len(resources)-1)):
        elapsed=max(.001,float(resources[i+1]["elapsed_s"])-float(resources[i]["elapsed_s"]))
        total=0.0
        for index in range(count):
            total+=max(0.0,(resources[i+1]["clients"][index]["cpu_seconds"]
                            -resources[i]["clients"][index]["cpu_seconds"])/elapsed*100.0)
        aggregate_cpu.append(total)
    aggregate={
        "client_count":count,
        "client_cpu_one_core_percent_sum_median":statistics.median(aggregate_cpu) if aggregate_cpu else 0.0,
        "client_cpu_one_core_percent_sum_p95":percentile(aggregate_cpu,.95),
        "client_rss_mib_sum_peak":max((sum(c["rss_bytes"] for c in row["clients"])/1048576.0
                                       for row in resources),default=0),
        "server_rss_mib_peak":max((row["server"]["rss_bytes"]/1048576.0 for row in resources),default=0),
        "texture_memory_mib_sum_median":sum(float(row["summary"].get("texture_memory_mib_median") or 0) for row in clients),
        "video_memory_mib_sum_median":sum(float(row["summary"].get("video_memory_mib_median") or 0) for row in clients),
        "physics_hz_min":min((float(row["summary"].get("physics_hz_median") or 0) for row in clients),default=0),
        "physics_hz_max":max((float(row["summary"].get("physics_hz_median") or 0) for row in clients),default=0),
        "queue_p99_ms_max":max((float(row["summary"].get("queue_age_p99_ms_worst") or 0) for row in clients),default=0),
        "incoming_depth_max":max((int(row["summary"].get("max_incoming_depth") or 0) for row in clients),default=0),
        "server_error_lines":sum("fail:" in line.lower() or "Unhandled" in line for line in server_text.splitlines()),
        "execution_passed":passed,
    }
    return {"aggregate":aggregate,"clients":clients,"resources":resources,"server_log":server_text}


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--counts",type=int,nargs="+",default=[1,2,4,8])
    parser.add_argument("--seconds",type=int,default=15)
    parser.add_argument("--monsters",type=int,default=300)
    parser.add_argument("--drops",type=int,default=500)
    parser.add_argument("--output",type=Path,default=Path("reports/runtime_multiopen.json"))
    args=parser.parse_args()
    if args.seconds<12 or any(count<1 or count>16 for count in args.counts):
        parser.error("seconds>=12 and counts1..16 required")
    godot=str(args.godot.resolve())
    cases=[]; passed=True
    for count in args.counts:
        print(f"=== native multi-open {count} clients ===",flush=True)
        case=run_case(godot,count,args.seconds,args.monsters,args.drops)
        cases.append(case)
        print(json.dumps(case["aggregate"],ensure_ascii=False,indent=2),flush=True)
        passed=passed and case["aggregate"]["execution_passed"]
    result={"version":1,"scenario":{"monsters":args.monsters,"drops":args.drops,"map":13,
                                   "seconds":args.seconds,"fps_cap":60,"resolution":[1440,900]},
            "cases":cases,"execution_passed":passed,
            "physical_driver_vram":"NOT VERIFIED; summed Godot per-process engine counters only"}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    return 0 if passed else 1


if __name__=="__main__":
    raise SystemExit(main())
