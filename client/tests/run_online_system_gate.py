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
    parser = argparse.ArgumentParser(description="Formal Host + fresh main scenes System-menu gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=105.0)
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
        with tempfile.TemporaryDirectory(prefix="sgtest-online-system-", ignore_cleanup_errors=True) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            pack = temp / "assets.pck"
            content = temp / "content"
            database = temp / "identity.db"
            host_dll = temp / "host-bin/SGTest.Server.Host.dll"
            observer_userdata = temp / "observer-userdata"
            actor_userdata = temp / "actor-userdata"
            for directory in (observer_userdata, actor_userdata):
                (directory / "roaming").mkdir(parents=True)
                (directory / "local").mkdir(parents=True)

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
                ("system_observer", "SystemObserver", "880"),
                ("system_actor", "SystemActor", "840"),
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

            host_seconds = max(120, int(args.timeout) + 35)
            host = subprocess.Popen(
                ["dotnet", str(host_dll), "serve", "--content", str(content),
                 "--identity", str(database), "--bind", "127.0.0.1", "--port", "0",
                 "--max-connections", "8", "--max-per-address", "4", "--seconds", str(host_seconds)],
                cwd=repo, env=env, text=True, encoding="utf-8", errors="replace",
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(target=capture, args=(host, logs["host"], host_events), daemon=True).start()
            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str, username: str, userdata: Path):
                client_env = env.copy()
                client_env["SGTEST_E2E_USERNAME"] = username
                client_env["SGTEST_E2E_PASSWORD"] = PASSWORD
                client_env["SGTEST_SYSTEM_GATE_TIMEOUT_MS"] = str(max(45_000, int((args.timeout - 10) * 1000)))
                client_env["APPDATA"] = str(userdata / "roaming")
                client_env["LOCALAPPDATA"] = str(userdata / "local")
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_system_gate.gd", "--",
                    "--pack", str(pack), "--host", "127.0.0.1", "--port", str(port),
                    "--system-role", role,
                ]
                process = subprocess.Popen(
                    command, cwd=repo, env=client_env, text=True, encoding="utf-8",
                    errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
                processes.append(process)
                logs[role] = []
                events: queue.Queue[str] = queue.Queue()
                threading.Thread(target=capture, args=(process, logs[role], events), daemon=True).start()
                return process, events

            observer, observer_events = start_client("observer", "system_observer", observer_userdata)
            wait_for(observer_events, "ONLINE_SYSTEM_GATE_READY role=observer", observer, deadline)

            settings, settings_events = start_client("settings", "system_actor", actor_userdata)
            wait_for(settings_events, "ONLINE_SYSTEM_GATE_PASS role=settings", settings, deadline)
            wait_for(observer_events, "ONLINE_SYSTEM_GATE_LOGOUT_DESPAWN role=observer", observer, deadline)
            settings.wait(timeout=max(1.0, deadline-time.monotonic()))
            if settings.returncode != 0:
                raise RuntimeError("settings client returned failure")

            exit_client, exit_events = start_client("exit", "system_actor", actor_userdata)
            wait_for(observer_events, "ONLINE_SYSTEM_GATE_RELOGIN_PRESENT role=observer", observer, deadline)
            wait_for(exit_events, "ONLINE_SYSTEM_GATE_RESTORE role=exit fresh_process=PASS", exit_client, deadline)
            wait_for(exit_events, "ONLINE_SYSTEM_GATE_EXIT_CONFIRMED role=exit session_stop=PASS", exit_client, deadline)
            exit_client.wait(timeout=max(1.0, deadline-time.monotonic()))
            if exit_client.returncode != 0:
                raise RuntimeError("exit client returned failure")

            wait_for(observer_events, "ONLINE_SYSTEM_GATE_PASS role=observer", observer, deadline)
            observer.wait(timeout=max(1.0, deadline-time.monotonic()))
            if observer.returncode != 0:
                raise RuntimeError("observer client returned failure")

            required_settings = [
                "ONLINE_SYSTEM_GATE_SETTINGS role=settings source029=PASS bgm=4 sfx=6 fps=25 battle=2 reopen=PASS",
                "ONLINE_SYSTEM_GATE_CANCEL role=settings back_to_server=PASS session_retained=PASS",
                "ONLINE_SYSTEM_GATE_LOGOUT role=settings confirm=PASS session_stop=PASS world_clear=PASS",
            ]
            required_exit = [
                "ONLINE_SYSTEM_GATE_RESTORE role=exit fresh_process=PASS bgm=4 sfx=6 fps=25 battle=2",
                "ONLINE_SYSTEM_GATE_CANCEL role=exit exit_game=PASS session_retained=PASS",
                "ONLINE_SYSTEM_GATE_EXIT_REQUESTED role=exit prompt=PASS",
                "ONLINE_SYSTEM_GATE_EXIT_CONFIRMED role=exit session_stop=PASS",
            ]
            required_observer = [
                "ONLINE_SYSTEM_GATE_PRESENT role=observer",
                "ONLINE_SYSTEM_GATE_LOGOUT_DESPAWN role=observer authority_aoi=PASS",
                "ONLINE_SYSTEM_GATE_RELOGIN_PRESENT role=observer authority_aoi=PASS",
                "ONLINE_SYSTEM_GATE_EXIT_DESPAWN role=observer authority_aoi=PASS",
            ]
            for name, required in [
                ("settings", required_settings), ("exit", required_exit), ("observer", required_observer)
            ]:
                if any(not any(token in line for line in logs[name]) for token in required):
                    raise RuntimeError(name + " missed required system milestone")

            for name in ("settings", "exit", "observer"):
                for line in logs[name]:
                    if line.startswith("ONLINE_SYSTEM_GATE_"):
                        print(line)
            print("ONLINE_SYSTEM_MAIN_SCENE_GATE_PASS "
                  "formal_host=true observer_main_scene=true fresh_actor_processes=2 "
                  "source_ui_029=true bgm=true sfx=true fps=true battle_mode_profile=true "
                  "logout_cancel_confirm=true exit_cancel_confirm=true settings_restart_persistence=true "
                  "observer_logout_despawn=true observer_exit_despawn=true "
                  f"native_visible={str(args.native).lower()} synthetic_world_content=true")
            return 0
    except Exception as error:
        print("ONLINE_SYSTEM_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
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
