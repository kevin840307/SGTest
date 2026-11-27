from __future__ import annotations
from pathlib import Path
import argparse
import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
import time

PASSWORD = "Online-Gate-Password-123!"
MARKER = "ONLINE_FRIEND_GATE_PASS role="

def run_checked(command: list[str], cwd: Path, env: dict[str, str], timeout: float) -> str:
    result = subprocess.run(command, cwd=cwd, env=env, text=True, encoding="utf-8",
                            errors="replace", stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError("command failed: " + " ".join(command[:5]) + "\n" + result.stdout)
    return result.stdout

def capture(process: subprocess.Popen[str], lines: list[str], notices: queue.Queue[str]) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        clean = line.rstrip()
        lines.append(clean)
        notices.put(clean)

def wait_for(notices: queue.Queue[str], token: str, process: subprocess.Popen[str],
             deadline: float) -> str:
    while time.monotonic() < deadline:
        if process.poll() is not None and notices.empty():
            raise RuntimeError(f"{token}: process exited {process.returncode}")
        try:
            line = notices.get(timeout=0.15)
        except queue.Empty:
            continue
        if token in line:
            return line
    raise TimeoutError(token)

def main() -> int:
    parser = argparse.ArgumentParser(description="Formal Host + two main scenes friend vertical gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=95.0)
    parser.add_argument("--native", action="store_true")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    env["SGTEST_AES_KEY_HEX"] = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
    env["SGTEST_AES_IV_HEX"] = "000102030405060708090a0b0c0d0e0f"

    processes: list[subprocess.Popen[str]] = []
    logs: dict[str, list[str]] = {}
    try:
        with tempfile.TemporaryDirectory(prefix="sgtest-online-friend-", ignore_cleanup_errors=True) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            pack = temp / "assets.pck"
            content = temp / "content"
            database = temp / "identity.db"
            host_dll = temp / "host-bin/SGTest.Server.Host.dll"

            out = run_checked(
                [args.godot, "--headless", "--path", str(repo / "client"),
                 "--script", "res://tests/build_online_gate_pack.gd", "--",
                 str(source_assets), str(content)],
                repo, env, args.timeout)
            if "ONLINE_GATE_SOURCE_READY" not in out:
                raise RuntimeError("online source builder did not confirm readiness")
            sys.path.insert(0, str(repo / "tools"))
            from asset_converter.runtime_pack import build_runtime_pack
            report = build_runtime_pack(Path(args.godot), source_assets, pack,
                                        stage=temp / "pack-stage")
            if not pack.is_file() or int(report.get("source_files", 0)) < 1:
                raise RuntimeError("runtime pack missing")

            run_checked(["dotnet", "build", str(host_project), "-c", "Release", "--nologo",
                         "-o", str(host_dll.parent)], repo, env, args.timeout)

            provision_env = env.copy()
            provision_env["SGTEST_ACCOUNT_PASSWORD"] = PASSWORD
            for username, character, x in [
                ("friend_observer", "FriendObserver", "880"),
                ("friend_actor", "FriendActor", "840"),
            ]:
                out = run_checked(
                    ["dotnet", str(host_dll), "account-create", "--identity", str(database),
                     "--username", username], repo, provision_env, args.timeout)
                if "ACCOUNT_CREATED" not in out:
                    raise RuntimeError("account provisioning failed for " + username)
                out = run_checked(
                    ["dotnet", str(host_dll), "character-create", "--identity", str(database),
                     "--username", username, "--content", str(content), "--name", character,
                     "--slot", "0", "--map", "15", "--x", x, "--y", "800"],
                    repo, provision_env, args.timeout)
                if "CHARACTER_CREATED" not in out:
                    raise RuntimeError("character provisioning failed for " + username)

            host = subprocess.Popen(
                ["dotnet", str(host_dll), "serve", "--content", str(content),
                 "--identity", str(database), "--bind", "127.0.0.1", "--port", "0",
                 "--max-connections", "8", "--max-per-address", "4", "--seconds", "90"],
                cwd=repo, env=env, text=True, encoding="utf-8", errors="replace",
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(target=capture, args=(host, logs["host"], host_events),
                             daemon=True).start()
            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            run_token = temp.name[-12:]

            def start_client(role: str, username: str):
                client_env = env.copy()
                client_env["SGTEST_E2E_USERNAME"] = username
                client_env["SGTEST_E2E_PASSWORD"] = PASSWORD
                client_env["SGTEST_FRIEND_E2E_TOKEN"] = run_token
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_friend_gate.gd", "--",
                    "--pack", str(pack), "--host", "127.0.0.1", "--port", str(port),
                    "--friend-role", role,
                ]
                process = subprocess.Popen(
                    command, cwd=repo, env=client_env, text=True, encoding="utf-8",
                    errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    bufsize=1)
                processes.append(process)
                logs[role] = []
                events: queue.Queue[str] = queue.Queue()
                threading.Thread(target=capture, args=(process, logs[role], events),
                                 daemon=True).start()
                return process, events

            observer, observer_events = start_client("observer", "friend_observer")
            wait_for(observer_events, "ONLINE_FRIEND_GATE_READY role=observer", observer, deadline)
            actor, actor_events = start_client("actor", "friend_actor")
            wait_for(actor_events, MARKER + "actor", actor, deadline)
            wait_for(observer_events, MARKER + "observer", observer, deadline)
            actor.wait(timeout=max(1.0, deadline - time.monotonic()))
            observer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if actor.returncode != 0 or observer.returncode != 0:
                raise RuntimeError("friend gate client returned failure")

            required_actor = [
                "ONLINE_FRIEND_GATE_INVITE_UI role=actor rightbox=PASS",
                "ONLINE_FRIEND_GATE_REJECT_SEEN role=actor authority=PASS",
                "ONLINE_FRIEND_GATE_ACCEPT role=actor authority=PASS online=PASS big_window=PASS",
                "ONLINE_FRIEND_GATE_MESSAGE role=actor send=PASS receive=PASS history=PASS",
                "ONLINE_FRIEND_GATE_RELOGIN role=actor sqlite_friendship=PASS",
                "ONLINE_FRIEND_GATE_DELETE role=actor authority=PASS",
            ]
            required_observer = [
                "ONLINE_FRIEND_GATE_REJECT role=observer question044=PASS",
                "ONLINE_FRIEND_GATE_IGNORE_RESTORE role=observer authority=PASS ui=PASS",
                "ONLINE_FRIEND_GATE_PRESENCE role=observer offline=PASS",
                "ONLINE_FRIEND_GATE_PRESENCE role=observer relog_online=PASS sqlite_friendship=PASS",
                "ONLINE_FRIEND_GATE_DELETE_SYNC role=observer bilateral=PASS",
            ]
            if any(not any(token in line for line in logs["actor"]) for token in required_actor):
                raise RuntimeError("actor missed a required friend milestone")
            if any(not any(token in line for line in logs["observer"]) for token in required_observer):
                raise RuntimeError("observer missed a required friend milestone")

            for name in ("actor", "observer"):
                for line in logs[name]:
                    if line.startswith("ONLINE_FRIEND_GATE_"):
                        print(line)
            print("ONLINE_FRIEND_MAIN_SCENE_GATE_PASS "
                  "formal_host=true main_scenes=2 invite_reject_accept=true "
                  "friend_ui=true realtime_message=true ignore_restore=true "
                  "presence_offline_relog=true sqlite_persistence=true delete_bilateral=true "
                  f"native_visible={str(args.native).lower()} synthetic_world_content=true")
            return 0
    except Exception as error:
        print("ONLINE_FRIEND_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
        for name, lines in logs.items():
            for line in lines[-45:]:
                print(name, line, file=sys.stderr)
        return 1
    finally:
        for process in reversed(processes):
            if process.poll() is not None:
                continue
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()

if __name__ == "__main__":
    raise SystemExit(main())
