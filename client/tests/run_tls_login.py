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
    parser.add_argument("--crossmap", action="store_true",
                        help="run two-client Map15 -> Map13 -> Map15 trusted transfer TLS E2E")
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
    if args.crossmap:
        return run_crossmap(args, repo, fixture, env)
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



def run_crossmap(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    """Two actual Godot processes on one TLS World; test-only trusted transfer signals.

    Python waits until the observing Godot process has *actually seen* the
    first Delta before asking the C# fixture to transfer the other actor.
    No time-based teleport and no client-supplied arbitrary MapId.
    """
    def start_reader(process, lines: list[str], events: queue.Queue[str]) -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value = line.rstrip()
            lines.append(value)
            events.put(value)

    def until(events: queue.Queue[str], token: str, deadline: float,
              process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"{token}: process exited {process.returncode}")
            try:
                value = events.get(timeout=0.15)
            except queue.Empty:
                continue
            if token in value:
                return value
        raise TimeoutError(f"{token}: timed out")

    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--crossmap"],
        cwd=repo, env=env, text=True, stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_events: queue.Queue[str] = queue.Queue()
    threading.Thread(target=start_reader, args=(server, server_lines,
                                                server_events), daemon=True).start()
    observer = None
    actor = None
    observer_lines: list[str] = []
    actor_lines: list[str] = []
    observer_events: queue.Queue[str] = queue.Queue()
    actor_events: queue.Queue[str] = queue.Queue()
    deadline = time.monotonic() + args.timeout
    try:
        ready = until(server_events, "CROSSMAP_FIXTURE_READY", deadline, server)
        match = re.search(
            r"CROSSMAP_FIXTURE_READY port=(\d+) cert=(.+?) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("invalid cross-map fixture readiness: " + ready)
        port, certificate = match.group(1), match.group(2)
        command = [args.godot, "--headless", "--path", str(repo / "client"),
                   "--script", "res://tests/test_client_multiplayer_tls.gd", "--",
                   port, certificate]
        observer = subprocess.Popen(
            [*command, "observer", "crossmap"], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=start_reader,
                         args=(observer, observer_lines, observer_events),
                         daemon=True).start()
        until(observer_events, "CLIENT_CROSSMAP_READY role=observer",
              deadline, observer)

        actor = subprocess.Popen(
            [*command, "actor", "crossmap"], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=start_reader, args=(actor, actor_lines, actor_events),
                         daemon=True).start()

        # Exact cross-process milestones eliminate an arbitrary time delay
        # between client movement and the server-authorized transfer.
        until(observer_events, "CLIENT_CROSSMAP_DELTA_SEEN", deadline, observer)
        assert server.stdin is not None
        server.stdin.write("TO13\n")
        server.stdin.flush()
        until(actor_events, "CLIENT_CROSSMAP_MAP13_MOVE_ACK", deadline, actor)
        server.stdin.write("TO15\n")
        server.stdin.flush()

        until(observer_events, "CLIENT_CROSSMAP_PASS role=observer",
              deadline, observer)
        until(actor_events, "CLIENT_CROSSMAP_PASS role=actor", deadline, actor)

        observer.wait(timeout=max(1.0, deadline - time.monotonic()))
        actor.wait(timeout=max(1.0, deadline - time.monotonic()))
        server.wait(timeout=max(1.0, deadline - time.monotonic()))

        if any(p.returncode != 0 for p in (observer, actor, server)) or not any(
            "CROSSMAP_FIXTURE_PASS" in line for line in server_lines
        ) or not any("CROSSMAP_FIXTURE_TRANSFER map=13" in line
                     for line in server_lines) or not any(
            "CROSSMAP_FIXTURE_TRANSFER map=15" in line for line in server_lines):
            raise RuntimeError("cross-map server/client process failed")

        for lines in (observer_lines, actor_lines, server_lines):
            for line in lines:
                if line.startswith(("CLIENT_CROSSMAP_", "CROSSMAP_FIXTURE_")):
                    print(line)
        print("GODOT_CSHARP_TWO_CLIENT_CROSSMAP_E2E_PASS "
              "tls=true map15_to_map13_to_map15=true "
              "world_ready=true old_map_clear=true "
              "despawn=true return_spawn=true "
              "same_authoritative_30hz_world=true")
        return 0
    except Exception as error:
        print("TLS_CROSSMAP_RUNNER_FAIL", error, file=sys.stderr)
        for name, lines in (("server", server_lines), ("observer", observer_lines),
                            ("actor", actor_lines)):
            for line in lines[-18:]:
                print(name, line, file=sys.stderr)
        return 1
    finally:
        for process in (observer, actor, server):
            if process is None or process.poll() is not None:
                continue
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()


if __name__ == "__main__":
    raise SystemExit(main())
