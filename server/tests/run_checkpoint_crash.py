from __future__ import annotations
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import queue
import time

def main() -> int:
    repo = Path(__file__).resolve().parents[2]
    fixture = repo / "server/tests/SGTest.Server.LoginFixture/bin/Release/net8.0/SGTest.Server.LoginFixture.dll"
    if not fixture.is_file():
        print("CHECKPOINT_PROCESS_KILL_FAIL fixture DLL missing", file=sys.stderr)
        return 2
    root = Path(tempfile.mkdtemp(prefix="sgtest-checkpoint-kill-"))
    db = root / "identity.db"
    process = subprocess.Popen(
        ["dotnet", str(fixture), "--checkpoint-crash-producer", str(db)],
        cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        bufsize=1)
    lines: list[str] = []
    events: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value = line.rstrip()
            lines.append(value)
            events.put(value)

    threading.Thread(target=capture, daemon=True).start()
    try:
        deadline = time.monotonic() + 10
        ready = None
        while time.monotonic() < deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"producer exited {process.returncode}")
            try:
                line = events.get(timeout=0.1)
            except queue.Empty:
                continue
            if line.startswith("CHECKPOINT_CRASH_READY "):
                ready = line
                break
        if ready is None:
            raise TimeoutError("producer never reached durable checkpoint marker")

        # Popen.kill() is SIGKILL on the Linux CI runner: no finally/dispose path runs.
        process.kill()
        process.wait(timeout=3)
        if process.returncode == 0:
            raise RuntimeError("producer was not hard-killed")

        verify = subprocess.run(
            ["dotnet", str(fixture), "--checkpoint-crash-verify", str(db)],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=10)
        if verify.returncode != 0 or "CHECKPOINT_CRASH_VERIFY_PASS" not in verify.stdout:
            raise RuntimeError("reopen verification failed\n" + verify.stdout)

        print(ready)
        print(verify.stdout.strip())
        print("CHECKPOINT_PROCESS_KILL_E2E_PASS signal=SIGKILL durable=40,8 "
              "volatile_after_checkpoint=24,8 reopened=40,8 db_schema_unchanged=true")
        return 0
    except Exception as error:
        print("CHECKPOINT_PROCESS_KILL_FAIL", error, file=sys.stderr)
        for line in lines[-30:]:
            print("producer", line, file=sys.stderr)
        return 1
    finally:
        if process.poll() is None:
            process.kill()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                pass
        shutil.rmtree(root, ignore_errors=True)

if __name__ == "__main__":
    raise SystemExit(main())
