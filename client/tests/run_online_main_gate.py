from __future__ import annotations
from pathlib import Path
import argparse
import json
import os
import queue
import signal
import subprocess
import sys
import tempfile
import threading
import time

PASSWORD = "Online-Gate-Password-123!"
PFX_PASSWORD = "Online-Gate-PFX-123!"
MARKER = "ONLINE_MAIN_GATE_PASS role="

def run_checked(command: list[str], cwd: Path, env: dict[str, str], timeout: float) -> str:
    result = subprocess.run(command, cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError("command failed: " + " ".join(command[:4]) + "\n" + result.stdout)
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
    parser = argparse.ArgumentParser(description="Formal Host + two actual Godot main scenes online gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=55.0)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    host_dll = repo / "server/host/SGTest.Server.Host/bin/Release/net8.0/SGTest.Server.Host.dll"
    base_env = os.environ.copy()
    base_env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"

    processes: list[subprocess.Popen[str]] = []
    logs: dict[str, list[str]] = {}
    try:
        with tempfile.TemporaryDirectory(prefix="sgtest-online-main-") as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            pack = temp / "assets.pck"
            content = temp / "content"
            database = temp / "identity.db"
            cert = temp / "cert.pem"
            key = temp / "key.pem"
            pfx = temp / "cert.pfx"

            build_source = run_checked(
                [args.godot, "--headless", "--path", str(repo / "client"),
                 "--script", "res://tests/build_online_gate_pack.gd", "--",
                 str(source_assets), str(content)],
                repo, base_env, args.timeout)
            if "ONLINE_GATE_SOURCE_READY" not in build_source:
                raise RuntimeError("synthetic source/content builder did not confirm readiness")

            sys.path.insert(0, str(repo / "tools"))
            from asset_converter.runtime_pack import build_runtime_pack
            pack_report = build_runtime_pack(
                Path(args.godot), source_assets, pack, stage=temp / "pack-stage")
            if not pack.is_file() or pack_report.get("source_files", 0) < 1:
                raise RuntimeError("formal runtime pack pipeline produced no external PCK")

            run_checked(["dotnet", "build", str(host_project), "-c", "Release", "--nologo"],
                        repo, base_env, args.timeout)

            run_checked(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-sha256",
                         "-nodes", "-keyout", str(key), "-out", str(cert), "-days", "1",
                         "-subj", "/CN=localhost",
                         "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1",
                         "-addext", "extendedKeyUsage=serverAuth"],
                        repo, base_env, args.timeout)
            run_checked(["openssl", "pkcs12", "-export", "-out", str(pfx), "-inkey",
                         str(key), "-in", str(cert), "-passout", "pass:" + PFX_PASSWORD],
                        repo, base_env, args.timeout)

            provision_env = base_env.copy()
            provision_env["SGTEST_ACCOUNT_PASSWORD"] = PASSWORD
            provision_env["SGTEST_TLS_PASSWORD"] = PFX_PASSWORD
            for username, character, x, y in [
                ("gate_observer", "GateObserver", "40", "40"),
                ("gate_actor", "GateActor", "24", "24"),
            ]:
                out = run_checked(
                    ["dotnet", str(host_dll), "account-create", "--identity", str(database),
                     "--username", username], repo, provision_env, args.timeout)
                if "ACCOUNT_CREATED" not in out:
                    raise RuntimeError("account provisioning did not create " + username)
                out = run_checked(
                    ["dotnet", str(host_dll), "character-create", "--identity", str(database),
                     "--username", username, "--content", str(content), "--name", character,
                     "--slot", "0", "--map", "15", "--x", x, "--y", y],
                    repo, provision_env, args.timeout)
                if "CHARACTER_CREATED" not in out:
                    raise RuntimeError("character provisioning did not create " + username)

            host_env = base_env.copy()
            host_env["SGTEST_TLS_PASSWORD"] = PFX_PASSWORD
            host = subprocess.Popen(
                ["dotnet", str(host_dll), "serve", "--content", str(content),
                 "--identity", str(database), "--certificate", str(pfx),
                 "--bind", "127.0.0.1", "--port", "0", "--max-connections", "8",
                 "--max-per-address", "4", "--seconds", "30"],
                cwd=repo, env=host_env, text=True, stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT, bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(target=capture, args=(host, logs["host"], host_events),
                             daemon=True).start()
            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"TLS_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            if ready.get("result") != "TLS_HOST_READY":
                raise RuntimeError("formal Host readiness payload invalid")
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str, username: str):
                env = base_env.copy()
                env["SGTEST_E2E_USERNAME"] = username
                env["SGTEST_E2E_PASSWORD"] = PASSWORD
                command = [
                    args.godot, "--headless", "--path", str(repo / "client"), "--",
                    "--pack", str(pack), "--host", "127.0.0.1", "--port", str(port),
                    "--tls-name", "localhost", "--trusted-cert", str(cert),
                    "--e2e-role", role,
                ]
                process = subprocess.Popen(command, cwd=repo, env=env, text=True,
                                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                           bufsize=1)
                processes.append(process)
                logs[role] = []
                events: queue.Queue[str] = queue.Queue()
                threading.Thread(target=capture, args=(process, logs[role], events),
                                 daemon=True).start()
                return process, events

            observer, observer_events = start_client("observer", "gate_observer")
            wait_for(observer_events, "ONLINE_MAIN_GATE_READY role=observer", observer, deadline)
            actor, actor_events = start_client("actor", "gate_actor")

            wait_for(actor_events, MARKER + "actor", actor, deadline)
            wait_for(observer_events, MARKER + "observer", observer, deadline)
            actor.wait(timeout=max(1.0, deadline - time.monotonic()))
            observer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if actor.returncode != 0 or observer.returncode != 0:
                raise RuntimeError("main-scene client process returned failure")

            if os.name != "nt" and host.poll() is None:
                host.send_signal(signal.SIGINT)
            host.wait(timeout=max(1.0, deadline - time.monotonic()))
            if host.returncode != 0 or not any('"TLS_HOST_STOPPED"' in line for line in logs["host"]):
                raise RuntimeError("formal Host did not stop cleanly")

            required_actor = [
                "ONLINE_MAIN_GATE_MOVE_CAMERA role=actor",
                "ONLINE_MAIN_GATE_MAP13 role=actor",
                "ONLINE_MAIN_GATE_RETURN_MAP15 role=actor",
            ]
            required_observer = [
                "ONLINE_MAIN_GATE_SPAWN role=observer",
                "ONLINE_MAIN_GATE_DELTA role=observer",
                "ONLINE_MAIN_GATE_DESPAWN role=observer",
                "ONLINE_MAIN_GATE_RETURN_SPAWN role=observer",
            ]
            if any(not any(token in line for line in logs["actor"]) for token in required_actor):
                raise RuntimeError("actor main scene missed a required milestone")
            if any(not any(token in line for line in logs["observer"]) for token in required_observer):
                raise RuntimeError("observer main scene missed a required milestone")

            for name in ("actor", "observer"):
                for line in logs[name]:
                    if line.startswith("ONLINE_MAIN_GATE_"):
                        print(line)
            for line in logs["host"]:
                if '"TLS_HOST_' in line:
                    print(line)
            print("ONLINE_MAIN_SCENE_FORMAL_HOST_GATE_PASS "
                  "formal_host=true main_scenes=2 tls=true provision_cli=true "
                  "move=true aoi=true camera_follow=true map15_map13_return=true "
                  "resource_release=true synthetic_content=true private_original_content=false")
            return 0
    except Exception as error:
        print("ONLINE_MAIN_SCENE_FORMAL_HOST_GATE_FAIL", error, file=sys.stderr)
        for name, lines in logs.items():
            for line in lines[-35:]:
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

if __name__ == "__main__":
    raise SystemExit(main())
