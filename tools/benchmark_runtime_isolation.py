#!/usr/bin/env python3
"""Windows/Godot runtime-isolation matrix for render FPS vs network/state cadence."""
from __future__ import annotations

import argparse
import ctypes
import json
import math
import statistics
import subprocess
import sys
import tempfile
import time
from pathlib import Path

import psutil

from runtime_processes import server_command


def stop(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    try:
        subprocess.run(["taskkill","/PID",str(process.pid),"/T","/F"],
                       capture_output=True,text=True,timeout=10)
    except (OSError,subprocess.TimeoutExpired):
        process.kill()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()


def process_sample(pid: int) -> dict:
    try:
        root=psutil.Process(pid)
        processes=[root,*root.children(recursive=True)]
    except psutil.NoSuchProcess:
        return {"cpu_seconds":0.0,"rss_bytes":0}
    cpu=0.0; rss=0
    for process in processes:
        try:
            times=process.cpu_times()
            cpu+=times.user+times.system
            rss+=process.memory_info().rss
        except psutil.NoSuchProcess:
            pass
    return {"cpu_seconds":cpu,"rss_bytes":rss}


def find_window(pid: int):
    if sys.platform != "win32":
        return None
    pids={pid}
    try:
        pids.update(child.pid for child in psutil.Process(pid).children(recursive=True))
    except psutil.NoSuchProcess:
        pass
    user32=ctypes.windll.user32
    found=[]
    callback_type=ctypes.WINFUNCTYPE(ctypes.c_bool,ctypes.c_void_p,ctypes.c_void_p)
    @callback_type
    def callback(hwnd,_lparam):
        process_id=ctypes.c_ulong()
        user32.GetWindowThreadProcessId(hwnd,ctypes.byref(process_id))
        if process_id.value in pids and user32.IsWindowVisible(hwnd):
            found.append(hwnd)
            return False
        return True
    user32.EnumWindows(callback,0)
    return found[0] if found else None


def window_action(pid: int, action: str) -> bool:
    hwnd=find_window(pid)
    if hwnd is None or sys.platform != "win32":
        return False
    user32=ctypes.windll.user32
    if action=="minimize":
        return bool(user32.ShowWindow(hwnd,6) or True)
    if action=="restore":
        user32.ShowWindow(hwnd,9)
        user32.SetForegroundWindow(hwnd)
        return True
    if action=="unfocus":
        shell=user32.GetShellWindow()
        return bool(shell and user32.SetForegroundWindow(shell))
    raise ValueError(action)


def parse_traces(text: str) -> list[dict]:
    rows=[]
    for line in text.splitlines():
        marker="runtime_trace "
        if marker in line:
            try:
                rows.append(json.loads(line.split(marker,1)[1]))
            except json.JSONDecodeError:
                pass
    return rows


def summarize(rows: list[dict], minimum_elapsed: float = 9.0) -> dict:
    stable=[row for row in rows if float(row.get("elapsed_s",0)) >= minimum_elapsed]
    if not stable:
        stable=rows
    def med(key):
        values=[float(row[key]) for row in stable if key in row]
        return statistics.median(values) if values else None
    def worst(key):
        values=[float(row[key]) for row in stable if key in row]
        return max(values) if values else None
    rx_rates=[float(row["rx_packets"])/float(row["window_s"]) for row in stable if row.get("window_s")]
    tx_rates=[float(row["tx_packets"])/float(row["window_s"]) for row in stable if row.get("window_s")]
    return {
        "trace_windows":len(stable),
        "physics_hz_median":med("physics_hz"),
        "engine_frame_hz_median":med("engine_frame_hz"),
        "visual_update_hz_median":med("visual_update_hz"),
        "rx_hz_median":statistics.median(rx_rates) if rx_rates else None,
        "tx_hz_median":statistics.median(tx_rates) if tx_rates else None,
        "queue_age_p95_ms_worst":worst("queue_age_p95_ms"),
        "queue_age_p99_ms_worst":worst("queue_age_p99_ms"),
        "queue_age_max_ms_worst":worst("queue_age_max_ms"),
        "max_incoming_depth":max((int(row.get("max_incoming_depth",0)) for row in stable),default=0),
        "snapshots_per_window_median":med("snapshots"),
        "owner_states_per_window_median":med("owner_states"),
        "actions_per_window_median":med("actions"),
        "input_hz_median":med("input_hz"),
        "texture_memory_mib_median":(med("texture_memory_bytes")/1048576.0 if med("texture_memory_bytes") is not None else None),
        "video_memory_mib_median":(med("video_memory_bytes")/1048576.0 if med("video_memory_bytes") is not None else None),
        "static_memory_mib_median":(med("static_memory_bytes")/1048576.0 if med("static_memory_bytes") is not None else None),
        "network_failed":any(bool(row.get("network_failed")) for row in stable),
    }


def run_case(godot: str, fps: int, seconds: int, monsters: int, drops: int,
             visual: bool = False, no_world: bool = False, transition: str = "none") -> dict:
    server_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    client_log=tempfile.TemporaryFile(mode="w+",encoding="utf-8")
    server=subprocess.Popen(server_command()+[f"--monsters={monsters}",f"--drops={drops}","--map=13"],
                            stdout=server_log,stderr=subprocess.STDOUT)
    client=None
    started=time.time()
    try:
        time.sleep(1)
        engine_fps=30 if visual else fps
        client_executable=godot
        if transition!="none" and sys.platform=="win32" and godot.lower().endswith("_console.exe"):
            candidate=godot[:-12]+".exe"
            if Path(candidate).is_file():
                client_executable=candidate
        command=[client_executable,"--path","client","--rendering-method","gl_compatibility",
                 "--resolution","1440x900","--max-fps",str(engine_fps),"--",
                 "--bot","--bot-index=1","--runtime-trace"]
        if transition=="none":
            command.append("--unfocus-low-power=0")
        if visual:
            command.append(f"--visual-fps={fps}")
        if no_world:
            command.append("--no-world-render")
        client=subprocess.Popen(command,stdout=client_log,stderr=subprocess.STDOUT)
        client_started=time.time()
        resources=[]; transition_events=[]; last_resource=0.0
        transitioned=False; restored=False
        deadline=client_started+seconds
        while time.time()<deadline:
            if server.poll() is not None:
                raise RuntimeError(f"server exited early at fps={fps}")
            if client.poll() is not None:
                raise RuntimeError(f"client exited early at fps={fps}")
            now=time.time(); elapsed=now-client_started
            if transition!="none" and not transitioned and elapsed>=5.5:
                transitioned=True
                ok=window_action(client.pid,transition)
                transition_events.append({"elapsed_s":elapsed,"action":transition,"success":ok})
            if transition!="none" and transitioned and not restored and elapsed>=11.0:
                restored=True
                ok=window_action(client.pid,"restore")
                transition_events.append({"elapsed_s":elapsed,"action":"restore","success":ok})
            if now-last_resource>=1.0:
                last_resource=now
                resources.append({"elapsed_s":elapsed,"client":process_sample(client.pid),
                                  "server":process_sample(server.pid)})
            time.sleep(.1)
    finally:
        if client is not None: stop(client)
        stop(server)
    server_log.seek(0); server_text=server_log.read(); server_log.close()
    client_log.seek(0); client_text=client_log.read(); client_log.close()
    rows=parse_traces(client_text)
    summary=summarize(rows)
    if len(resources)>=2:
        client_cpu=[]; server_cpu=[]
        for previous,current in zip(resources,resources[1:]):
            elapsed=max(.001,float(current["elapsed_s"])-float(previous["elapsed_s"]))
            client_cpu.append(max(0.0,(current["client"]["cpu_seconds"]-previous["client"]["cpu_seconds"])/elapsed*100.0))
            server_cpu.append(max(0.0,(current["server"]["cpu_seconds"]-previous["server"]["cpu_seconds"])/elapsed*100.0))
        summary.update({
            "client_cpu_one_core_percent_median":statistics.median(client_cpu) if client_cpu else 0.0,
            "client_cpu_one_core_percent_p95":sorted(client_cpu)[min(len(client_cpu)-1,max(0,math.ceil(len(client_cpu)*.95)-1))] if client_cpu else 0.0,
            "client_rss_mib_peak":max(row["client"]["rss_bytes"] for row in resources)/1048576.0,
            "server_cpu_one_core_percent_median":statistics.median(server_cpu) if server_cpu else 0.0,
            "server_rss_mib_peak":max(row["server"]["rss_bytes"] for row in resources)/1048576.0,
        })
    summary.update({
        "fps_cap":fps,
        "mode":"visual" if visual else ("no-world" if no_world else "engine"),
        "engine_cap":30 if visual else fps,
        "seconds":seconds,
        "execution_passed":bool(rows) and "ERROR:" not in client_text and "SCRIPT ERROR" not in client_text
            and "prediction_ready" in client_text and "snapshot_received" in client_text,
        "prediction_ready":"prediction_ready" in client_text,
        "snapshot_received":"snapshot_received" in client_text,
        "client_error_lines":sum("ERROR:" in line or "SCRIPT ERROR" in line for line in client_text.splitlines()),
        "server_error_lines":sum("fail:" in line.lower() or "Unhandled" in line for line in server_text.splitlines()),
        "runtime_seconds":time.time()-started,
    })
    return {"summary":summary,"traces":rows,"resources":resources,
            "transition_events":transition_events,"client_log":client_text,"server_log":server_text}


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("godot",type=Path)
    parser.add_argument("--fps",type=int,nargs="+",default=[5,10,15,30,45,60,75,90])
    parser.add_argument("--seconds",type=int,default=16)
    parser.add_argument("--monsters",type=int,default=300)
    parser.add_argument("--drops",type=int,default=500)
    parser.add_argument("--visual",action="store_true",
                        help="treat --fps as low-power visual rates while engine remains 30Hz")
    parser.add_argument("--no-world",action="store_true",
                        help="skip world visual projection/redraw while preserving logic/network")
    parser.add_argument("--window-transition",choices=["none","minimize","unfocus"],default="none",
                        help="at 5.5s apply a real Windows state change and restore at 11s")
    parser.add_argument("--output",type=Path,default=Path("reports/runtime_isolation_matrix.json"))
    args=parser.parse_args()
    if args.seconds<12: parser.error("--seconds must be >=12")
    godot=str(args.godot.resolve())
    cases=[]
    passed=True
    if args.visual and any(fps not in (5,10,15) for fps in args.fps):
        parser.error("--visual supports only 5/10/15")
    for fps in args.fps:
        mode="visual" if args.visual else ("no-world" if args.no_world else "engine")
        print(f"=== runtime isolation {mode} {fps} FPS ===",flush=True)
        row=run_case(godot,fps,args.seconds,args.monsters,args.drops,args.visual,args.no_world,args.window_transition)
        cases.append(row)
        print(json.dumps(row["summary"],ensure_ascii=False,indent=2),flush=True)
        passed=row["summary"]["execution_passed"] and passed
    output={"version":1,"godot":subprocess.check_output([godot,"--version"],text=True).strip(),
            "scenario":{"monsters":args.monsters,"drops":args.drops,"map":13,"seconds":args.seconds,
                        "resolution":[1440,900],"visual_throttle":args.visual,"no_world":args.no_world,
                        "window_transition":args.window_transition},
            "cases":cases}
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(output,ensure_ascii=False,indent=2),encoding="utf-8")
    return 0 if passed else 1

if __name__=="__main__":
    raise SystemExit(main())
