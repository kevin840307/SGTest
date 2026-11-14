#!/usr/bin/env python3
"""F001 20-client first-login PBKDF2 storm vs 30Hz world tick acceptance."""
from __future__ import annotations

import argparse
import json
import re
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path

from runtime_processes import server_command

METRIC=re.compile(r"server_metrics tick=(\d+) tick_p95_ms=([0-9.]+) tick_p99_ms=([0-9.]+) players=(\d+)")

def stop_process(process:subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    try:
        subprocess.run(["taskkill","/PID",str(process.pid),"/T","/F"],
                       stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,check=False)
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=5)
    except OSError:
        process.kill()
        process.wait(timeout=5)

def health(port:int) -> dict:
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/health",timeout=2) as response:
        return json.loads(response.read().decode("utf-8"))

def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--clients",type=int,default=20)
    parser.add_argument("--port",type=int,default=27932)
    parser.add_argument("--output",type=Path,default=Path("reports/f001_auth_storm.json"))
    args=parser.parse_args()
    if args.clients<1 or args.clients>64:
        parser.error("clients must be 1..64")
    godot=str(args.godot.resolve())
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    database_temp=tempfile.TemporaryDirectory(prefix="sgtest-auth-storm-")
    database=Path(database_temp.name)/"identity.db"
    server=subprocess.Popen(server_command(accounts="sqlite",db=database)+
                            [f"--port={args.port}","--map=13","--monsters=0","--drops=0"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    clients=[]
    started=time.time()
    selected_at=None
    health_snapshot={}
    early_exits=[]
    try:
        deadline=time.time()+5
        while time.time()<deadline:
            try:
                health(args.port)
                break
            except OSError:
                time.sleep(.1)
        for index in range(1,args.clients+1):
            command=[godot,"--headless","--path","client",
                     "--script","res://tests/identity_lifecycle_client.gd","--",
                     f"--port={args.port}",f"--index={index}","--mode=hold","--timeout=30"]
            clients.append(subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT))
            time.sleep(.02)
        deadline=time.time()+15
        while time.time()<deadline:
            health_snapshot=health(args.port)
            if int(health_snapshot.get("players",-1))==args.clients:
                selected_at=time.time()
                break
            if any(p.poll() is not None for p in clients):
                break
            time.sleep(.1)
        # Keep the server alive through the 300-tick (10s) metrics boundary.
        remaining=11.5-(time.time()-started)
        if remaining>0:
            time.sleep(remaining)
        early_exits=[p.returncode for p in clients if p.poll() is not None]
    finally:
        for process in clients:
            stop_process(process)
        stop_process(server)
    server_log.seek(0)
    server_text=server_log.read()
    server_log.close()
    database_temp.cleanup()
    metrics=[{"tick":int(m.group(1)),"p95_ms":float(m.group(2)),"p99_ms":float(m.group(3)),
              "players":int(m.group(4))} for m in METRIC.finditer(server_text)]
    stable=[row for row in metrics if row["tick"]>=300]
    worst_p99=max((row["p99_ms"] for row in stable),default=999.0)
    selected_seconds=(selected_at-started) if selected_at is not None else None
    passed=(
        selected_at is not None
        and int(health_snapshot.get("players",-1))==args.clients
        and not early_exits
        and bool(stable)
        and worst_p99<25.0
        and "Unhandled" not in server_text
    )
    report={
        "version":1,
        "clients":args.clients,
        "selected_seconds":selected_seconds,
        "health":health_snapshot,
        "server_metrics":metrics,
        "stable_tick_p99_ms_worst":worst_p99,
        "early_client_exits":early_exits,
        "execution_passed":passed,
        "server_log":server_text,
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k!="server_log"},ensure_ascii=False,indent=2))
    return 0 if passed else 1

if __name__=="__main__":
    raise SystemExit(main())
