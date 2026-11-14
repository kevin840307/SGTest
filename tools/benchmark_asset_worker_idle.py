"""Measure idle decoder cost separately from rendering/network/world CPU."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import tempfile
import time

import psutil

REPO = Path(__file__).resolve().parents[1]


def cpu_time(group):
    counters = [process.cpu_times() for process in group]
    return sum(value.user+value.system for value in counters)


def run(godot, count, seconds):
    with tempfile.TemporaryFile(mode='w+', encoding='utf-8') as log:
        process = subprocess.Popen([str(godot), '--headless', '--path', str(REPO/'client'),
                                    '--max-fps', '10', '--script', 'tools/benchmark_asset_worker_idle.gd',
                                    '--', f'--stores={count}'], cwd=REPO, stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic()+10
            while True:
                log.seek(0)
                if 'asset_worker_idle_ready' in log.read():
                    break
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError('idle probe did not become ready')
                time.sleep(.05)
            meter = psutil.Process(process.pid)
            # Windows *_console.exe is a small launcher; include the actual
            # engine child, just as the multiplayer baseline does.
            group = [meter, *meter.children(recursive=True)]
            before = cpu_time(group)
            started = time.monotonic()
            time.sleep(seconds)
            after = cpu_time(group)
            elapsed = time.monotonic()-started
            return {'stores': count, 'seconds': elapsed,
                    'processes': [{'pid': p.pid, 'name': p.name()} for p in group],
                    'cpu_seconds': after-before,
                    'cpu_one_core_percent': 100*(after-before)/elapsed,
                    'rss_bytes': sum(p.memory_info().rss for p in group)}
        finally:
            if process.poll() is None:
                children = psutil.Process(process.pid).children(recursive=True)
                for child in children:
                    child.terminate()
                process.terminate()
                psutil.wait_procs(children, timeout=5)
            process.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot', type=Path)
    parser.add_argument('--seconds', type=float, default=8)
    parser.add_argument('--output', type=Path, default=REPO/'reports/perf003_idle.json')
    args = parser.parse_args()
    if not 5 <= args.seconds <= 15:
        parser.error('measurement must last 5..15 seconds after warmup')
    godot = args.godot.resolve()
    report = {'scope': 'one headless process, 10 FPS, no world/network/assets; 0/1/8 idle decoder threads, not eight clients',
              'cpu_counter_note': 'Short-window process CPU counters may quantize small values to zero; zero does not prove zero CPU or an improvement.',
              'godot_version': subprocess.check_output([str(godot), '--version'], text=True).strip(),
              'os': platform.platform(), 'store_sha256': hashlib.sha256((REPO/'client/scripts/runtime_asset_store.gd').read_bytes()).hexdigest(),
              'samples': [run(godot, count, args.seconds) for count in [0, 1, 8]]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
