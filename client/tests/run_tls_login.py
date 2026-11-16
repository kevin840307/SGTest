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
        if client.returncode != 0 or "CLIENT_TLS_LOGIN_PASS" not in client.stdout or "receipt=PASS" not in client.stdout:
            raise RuntimeError("Godot TLS login test failed")
        remaining = max(1.0, deadline - time.monotonic())
        server.wait(timeout=remaining)
        reader.join(timeout=2)
        output = "\n".join(lines)
        if server.returncode != 0 or "LOGIN_FIXTURE_MOVE_PASS" not in output or "LOGIN_FIXTURE_PASS" not in output or "LOGIN_FIXTURE_RECEIPT_ROUTED" not in output:
            raise RuntimeError("C# fixture did not confirm authenticated Move")
        for line in lines:
            if line.startswith("LOGIN_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_LOGIN_E2E_PASS secure_tls=true authenticated_move=true authoritative_receipt_returned=true transport_thread=true reducer_thread=true")
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


if __name__ == "__main__":
    raise SystemExit(main())
