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
MARKER = "ONLINE_CLAN_GATE_PASS role="

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
    parser = argparse.ArgumentParser(description="Formal Host + two main scenes clan vertical gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=125.0)
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
        with tempfile.TemporaryDirectory(prefix="sgtest-online-clan-", ignore_cleanup_errors=True) as temp_text:
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
                ("clan_observer", "ClanObserver", "880"),
                ("clan_actor", "ClanActor", "840"),
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
                 "--max-connections", "8", "--max-per-address", "4", "--seconds", "120"],
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

            def start_client(role: str, username: str):
                client_env = env.copy()
                client_env["SGTEST_E2E_USERNAME"] = username
                client_env["SGTEST_E2E_PASSWORD"] = PASSWORD
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_clan_gate.gd", "--",
                    "--pack", str(pack), "--host", "127.0.0.1", "--port", str(port),
                    "--clan-role", role,
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

            observer, observer_events = start_client("observer", "clan_observer")
            wait_for(observer_events, "ONLINE_CLAN_GATE_READY role=observer", observer, deadline)
            actor, actor_events = start_client("actor", "clan_actor")

            wait_for(actor_events, MARKER + "actor", actor, deadline)
            wait_for(observer_events, MARKER + "observer", observer, deadline)
            actor.wait(timeout=max(1.0, deadline - time.monotonic()))
            observer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if actor.returncode != 0 or observer.returncode != 0:
                raise RuntimeError("clan gate client returned failure")

            required_actor = [
                "ONLINE_CLAN_GATE_CREATE role=actor ui061=PASS ui057=PASS sqlite=PASS",
                "ONLINE_CLAN_GATE_INVITE role=actor rightbox=PASS server=PASS",
                "ONLINE_CLAN_GATE_REINVITE role=actor reject_cleared_pending=PASS",
                "ONLINE_CLAN_GATE_JOIN_SYNC role=actor members=2 online=PASS ui060=PASS",
                "ONLINE_CLAN_GATE_RELOGIN role=actor sqlite_membership=PASS online_return=PASS",
                "ONLINE_CLAN_GATE_ANNOUNCEMENT role=actor ui070=PASS server=PASS sync=PASS",
                "ONLINE_CLAN_GATE_TRANSFER role=actor authority=PASS sync=PASS",
                "ONLINE_CLAN_GATE_KICKED role=actor clear_snapshot=PASS",
            ]
            required_observer = [
                "ONLINE_CLAN_GATE_REJECT role=observer question=PASS server=PASS",
                "ONLINE_CLAN_GATE_ACCEPT role=observer invite=PASS authority_join=PASS members=2",
                "ONLINE_CLAN_GATE_PRESENCE role=observer offline=PASS",
                "ONLINE_CLAN_GATE_PRESENCE role=observer relog_online=PASS",
                "ONLINE_CLAN_GATE_ANNOUNCEMENT role=observer read_only=PASS synced=PASS",
                "ONLINE_CLAN_GATE_TRANSFER role=observer became_leader=PASS",
                "ONLINE_CLAN_GATE_KICK role=observer authority=PASS bilateral_sync=PASS",
                "ONLINE_CLAN_GATE_RELOGIN role=observer sqlite_transfer_kick=PASS",
                "ONLINE_CLAN_GATE_LEAVE role=observer authority=PASS clear_snapshot=PASS",
            ]
            if any(not any(token in line for line in logs["actor"]) for token in required_actor):
                raise RuntimeError("actor missed a required clan milestone")
            if any(not any(token in line for line in logs["observer"]) for token in required_observer):
                raise RuntimeError("observer missed a required clan milestone")

            for name in ("actor", "observer"):
                for line in logs[name]:
                    if line.startswith("ONLINE_CLAN_GATE_"):
                        print(line)
            print("ONLINE_CLAN_MAIN_SCENE_GATE_PASS "
                  "formal_host=true main_scenes=2 create=true invite_reject_accept=true "
                  "member_online_offline=true announcement=true transfer=true kick=true leave=true "
                  "sqlite_relogin=true source_ui_057_060_061_070=true "
                  f"native_visible={str(args.native).lower()} synthetic_world_content=true")
            return 0
    except Exception as error:
        print("ONLINE_CLAN_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
        for name, lines in logs.items():
            for line in lines[-55:]:
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
