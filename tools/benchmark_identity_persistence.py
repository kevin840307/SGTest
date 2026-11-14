#!/usr/bin/env python3
"""F001 SQLite persistence across a full server restart."""
from __future__ import annotations

import argparse
import json
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path

from runtime_processes import server_command

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

def wait_health(port:int,timeout:float=5.0) -> dict:
    deadline=time.time()+timeout
    while time.time()<deadline:
        try:
            return health(port)
        except OSError:
            time.sleep(.1)
    raise RuntimeError("server health timeout")

def wait_result(path:Path,process:subprocess.Popen,timeout:float=10.0) -> dict:
    deadline=time.time()+timeout
    while time.time()<deadline:
        if path.is_file() and path.stat().st_size:
            return json.loads(path.read_text(encoding="utf-8"))
        if process.poll() is not None and process.returncode!=0:
            raise RuntimeError(f"identity client exited {process.returncode}")
        time.sleep(.1)
    raise RuntimeError("identity client result timeout")

def run_server(port:int,database:Path,log):
    process=subprocess.Popen(
        server_command(accounts="sqlite",db=database)+
        [f"--port={port}","--map=13","--monsters=0","--drops=0"],
        stdout=log,stderr=subprocess.STDOUT)
    state=wait_health(port)
    if state.get("accounts")!="sqlite":
        stop_process(process)
        raise RuntimeError(f"server did not use sqlite: {state}")
    return process,state

def run_client(godot:str,port:int,index:int,result:Path) -> dict:
    process=subprocess.Popen(
        [godot,"--headless","--path","client",
         "--script","res://tests/identity_lifecycle_client.gd","--",
         f"--port={port}",f"--index={index}",f"--result={result}","--mode=once","--timeout=15"],
        stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
    try:
        return wait_result(result,process)
    finally:
        stop_process(process)

def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--port",type=int,default=27933)
    parser.add_argument("--output",type=Path,default=Path("reports/f001_sqlite_restart.json"))
    args=parser.parse_args()
    godot=str(args.godot.resolve())
    with tempfile.TemporaryDirectory(prefix="sgtest-sqlite-restart-") as temp:
        root=Path(temp)
        database=root/"identity.db"
        result1=root/"first.json"
        result2=root/"second.json"
        log1=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
        server1,state1=run_server(args.port,database,log1)
        try:
            first=run_client(godot,args.port,88,result1)
        finally:
            stop_process(server1)
        log1.seek(0); server1_text=log1.read(); log1.close()

        time.sleep(.4)
        log2=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
        server2,state2=run_server(args.port,database,log2)
        try:
            second=run_client(godot,args.port,88,result2)
        finally:
            stop_process(server2)
        log2.seek(0); server2_text=log2.read(); log2.close()

        db_size=database.stat().st_size if database.exists() else 0
        wal_or_db=database.exists()

    first_codes=[event[2] for event in first.get("events",[]) if event and event[0]=="auth"]
    second_codes=[event[2] for event in second.get("events",[]) if event and event[0]=="auth"]
    passed=(
        first.get("success") is True and first.get("reason")=="entered"
        and second.get("success") is True and second.get("reason")=="entered"
        and "registered" in first_codes
        and second_codes==["authenticated"]
        and int(first.get("account_id",0))==int(second.get("account_id",0))>0
        and int(first.get("character_id",0))==int(second.get("character_id",0))>0
        and state1.get("accounts")=="sqlite" and state2.get("accounts")=="sqlite"
        and wal_or_db and db_size>0
        and "Unhandled" not in server1_text+server2_text
    )
    report={
        "version":1,
        "first":first,
        "second":second,
        "first_health":state1,
        "second_health":state2,
        "database_bytes":db_size,
        "execution_passed":passed,
        "server_logs":[server1_text,server2_text],
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k!="server_logs"},ensure_ascii=False,indent=2))
    return 0 if passed else 1

if __name__=="__main__":
    raise SystemExit(main())
