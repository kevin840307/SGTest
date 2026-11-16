from pathlib import Path
import argparse
import os
import queue
import re
import subprocess
import sys
import threading
import time

READY = re.compile(r"LOGIN_FIXTURE_READY port=(\d+) cert=(.+?) username=")


def main() -> int:
    parser = argparse.ArgumentParser(description="Run real Godot -> C# TLS login/CharacterChoose/Move E2E")
    parser.add_argument("godot", help="Godot 4.7.2 console executable")
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--multi", action="store_true",
                        help="run two concurrent real Godot TLS clients against the same 30Hz World")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    fixture = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    build = subprocess.run(
        ["dotnet", "build", str(fixture), "-c", "Release", "--nologo"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        timeout=args.timeout)
    if build.returncode != 0:
        print(build.stdout)
        return 2
    if args.multi:
        return run_multiplayer(args, repo, fixture, env)

    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release", "--no-build"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def read_server() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            clean = line.rstrip()
            lines.append(clean)
            notices.put(clean)

    reader = threading.Thread(target=read_server, daemon=True)
    reader.start()
    deadline = time.monotonic() + args.timeout
    match = None
    try:
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.2)
            except queue.Empty:
                continue
            found = READY.search(line)
            if found:
                match = found
                break
        if match is None:
            raise RuntimeError("fixture never became ready")
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_transport_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=args.timeout)
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_TLS_LOGIN_PASS" not in client.stdout or "receipt=PASS" not in client.stdout or "world_ready=PASS" not in client.stdout:
            raise RuntimeError("Godot TLS login test failed")
        remaining = max(1.0, deadline - time.monotonic())
        server.wait(timeout=remaining)
        reader.join(timeout=2)
        output = "\n".join(lines)
        if server.returncode != 0 or "LOGIN_FIXTURE_MOVE_PASS" not in output or "LOGIN_FIXTURE_PASS" not in output or "LOGIN_FIXTURE_RECEIPT_ROUTED" not in output or "LOGIN_FIXTURE_WORLD_READY_ROUTED" not in output:
            raise RuntimeError("C# fixture did not confirm authenticated Move")
        for line in lines:
            if line.startswith("LOGIN_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_LOGIN_E2E_PASS secure_tls=true authenticated_move=true authoritative_receipt_returned=true world_ready_delivered=true transport_thread=true reducer_thread=true")
        return 0
    except Exception as error:
        print("TLS_LOGIN_RUNNER_FAIL", error, file=sys.stderr)
        for line in lines[-30:]:
            print(line, file=sys.stderr)
        return 1
    finally:
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()



def run_multiplayer(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    """Real TLS peer-to-peer AOI visibility through one C# authoritative World."""
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--multi"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_notices: queue.Queue[str] = queue.Queue()
    observer_lines: list[str] = []
    observer_notices: queue.Queue[str] = queue.Queue()
    observer = None

    def capture(pipe, storage: list[str], notices: queue.Queue[str]) -> None:
        for line in pipe:
            cleaned = line.rstrip()
            storage.append(cleaned)
            notices.put(cleaned)

    threading.Thread(target=capture, args=(server.stdout, server_lines,
                                           server_notices), daemon=True).start()

    def until(notices: queue.Queue[str], expected: str, deadline: float,
              process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and notices.empty():
                raise RuntimeError(f"process exited before {expected} (code={process.returncode})")
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if expected in line:
                return line
        raise TimeoutError(f"timeout waiting for {expected}")

    deadline = time.monotonic() + args.timeout
    try:
        ready = until(server_notices, "MULTI_FIXTURE_READY", deadline, server)
        match = re.search(r"MULTI_FIXTURE_READY port=(\d+) cert=(.+?) actor_a=(\d+) actor_b=(\d+)", ready)
        if match is None:
            raise RuntimeError(f"malformed multiplayer fixture readiness {ready}")
        port, cert = match.group(1), match.group(2)
        base = [args.godot, "--headless", "--path", str(repo / "client"),
                "--script", "res://tests/test_client_multiplayer_tls.gd", "--",
                port, cert]

        observer = subprocess.Popen(
            [*base, "observer"], cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=capture, args=(observer.stdout, observer_lines,
                                               observer_notices), daemon=True).start()
        until(observer_notices, "CLIENT_MULTI_READY role=observer", deadline, observer)

        remaining = max(1.0, deadline - time.monotonic())
        actor = subprocess.run([*base, "actor"], cwd=repo, text=True,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               timeout=remaining)
        if actor.returncode != 0 or "CLIENT_MULTI_PASS role=actor" not in actor.stdout:
            raise RuntimeError("active Godot client failed:\n" + actor.stdout)

        observer.wait(timeout=max(1.0, deadline - time.monotonic()))
        if observer.returncode != 0 or not any(
                "CLIENT_MULTI_PASS role=observer" in line for line in observer_lines):
            raise RuntimeError("observer Godot client failed:\n" +
                               "\n".join(observer_lines))

        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        if server.returncode != 0 or not any(
                line.startswith("MULTI_FIXTURE_PASS") for line in server_lines):
            raise RuntimeError("shared World fixture failed:\n" +
                               "\n".join(server_lines))

        print(actor.stdout, end="")
        for line in observer_lines:
            if line.startswith("CLIENT_MULTI_"):
                print(line)
        for line in server_lines:
            if line.startswith("MULTI_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_AOI_E2E_PASS "
              "tls=true concurrent_clients=2 world_ready=true "
              "spawn=true authoritative_delta=true despawn=true "
              "same_30hz_world=true")
        return 0
    except Exception as error:
        print("TLS_MULTIPLAYER_RUNNER_FAIL", error, file=sys.stderr)
        for line in server_lines[-20:]:
            print(line, file=sys.stderr)
        for line in observer_lines[-20:]:
            print(line, file=sys.stderr)
        return 1
    finally:
        if observer is not None and observer.poll() is None:
            observer.terminate()
            try:
                observer.wait(timeout=2)
            except subprocess.TimeoutExpired:
                observer.kill()
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()


if __name__ == "__main__":
    raise SystemExit(main())
