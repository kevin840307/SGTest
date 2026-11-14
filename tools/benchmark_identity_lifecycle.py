#!/usr/bin/env python3
"""F001 account/character/reconnect/logout protocol lifecycle acceptance probe."""
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

def wait_until(predicate,timeout:float,description:str):
    deadline=time.time()+timeout
    last=None
    while time.time()<deadline:
        try:
            last=predicate()
            if last:
                return last
        except (OSError,ValueError):
            pass
        time.sleep(.1)
    raise RuntimeError(f"timeout waiting for {description}; last={last!r}")

def result_if_ready(path:Path):
    if not path.is_file() or path.stat().st_size==0:
        return None
    return json.loads(path.read_text(encoding="utf-8"))

def health_when(port:int,players:int):
    value=health(port)
    return value if int(value.get("players",-1))==players else None

def client_command(godot:str,port:int,result:Path,mode:str) -> list[str]:
    return [godot,"--headless","--path","client",
            "--script","res://tests/identity_lifecycle_client.gd","--",
            f"--port={port}",f"--result={result}",f"--mode={mode}"]

def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--port",type=int,default=27931)
    parser.add_argument("--output",type=Path,default=Path("reports/f001_identity_lifecycle.json"))
    args=parser.parse_args()
    godot=str(args.godot.resolve())
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    clients=[]
    snapshots={}
    server=None
    with tempfile.TemporaryDirectory(prefix="sgtest-identity-") as temp:
        database=Path(temp)/"identity.db"
        result_paths=[Path(temp)/f"client-{index}.json" for index in range(3)]
        server=subprocess.Popen(server_command(accounts="sqlite",db=database)+
                                [f"--port={args.port}","--map=13","--monsters=20","--drops=20"],
                                stdout=server_log,stderr=subprocess.STDOUT)
        try:
            wait_until(lambda: health(args.port),5,"server health")
            clients.append(subprocess.Popen(client_command(godot,args.port,result_paths[0],"hold"),
                                            stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT))
            snapshots["after_first"]=wait_until(
                lambda: health_when(args.port,1),8,"first selected player")

            clients.append(subprocess.Popen(client_command(godot,args.port,result_paths[1],"hold"),
                                            stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT))
            first=wait_until(lambda: result_if_ready(result_paths[0]),8,"first session replacement")
            snapshots["after_takeover"]=wait_until(
                lambda: health_when(args.port,1),8,"replacement selected player")

            clients.append(subprocess.Popen(client_command(godot,args.port,result_paths[2],"logout"),
                                            stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT))
            second=wait_until(lambda: result_if_ready(result_paths[1]),8,"second session replacement")
            third=wait_until(lambda: result_if_ready(result_paths[2]),10,"third session logout")
            snapshots["after_logout"]=wait_until(
                lambda: health_when(args.port,0),5,"world player cleanup after logout")
        finally:
            for process in clients:
                stop_process(process)
            if server is not None:
                stop_process(server)
        results=[result_if_ready(path) for path in result_paths]

    server_log.seek(0)
    server_text=server_log.read()
    server_log.close()
    complete=all(isinstance(row,dict) for row in results)
    character_ids=[int(row["character_id"]) for row in results if isinstance(row,dict)]
    account_ids=[int(row["account_id"]) for row in results if isinstance(row,dict)]
    session_epochs=[int(row.get("session_epoch",0)) for row in results if isinstance(row,dict)]
    passed=(
        complete
        and len(set(character_ids))==1 and character_ids[0]>0
        and len(set(account_ids))==1 and account_ids[0]>0
        and len(session_epochs)==3 and all(epoch>0 for epoch in session_epochs) and len(set(session_epochs))==3
        and results[0]["reason"]=="session_replaced"
        and results[1]["reason"]=="session_replaced"
        and results[2]["reason"]=="logged_out"
        and all(bool(row["entered"]) for row in results)
        and int(snapshots["after_first"].get("players",-1))==1
        and int(snapshots["after_takeover"].get("players",-1))==1
        and int(snapshots["after_logout"].get("players",-1))==0
        and server_text.count("replaced peer")>=2
        and "Unhandled" not in server_text
    )
    report={
        "version":1,
        "scenario":"same account: register/create/select -> takeover -> takeover+logout",
        "clients":results,
        "health":snapshots,
        "server_replacements":server_text.count("replaced peer"),
        "execution_passed":passed,
        "server_log":server_text,
    }
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps({k:v for k,v in report.items() if k!="server_log"},ensure_ascii=False,indent=2))
    return 0 if passed else 1

if __name__=="__main__":
    raise SystemExit(main())
