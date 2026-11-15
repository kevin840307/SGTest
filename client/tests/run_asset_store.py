"""Run actual Godot Store lifecycle tests; engine errors invalidate script PASS.

--compare repeats the same fresh-process trace with one and two Texture jobs.
Private PCKs, raw logs and metrics remain outside Git.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import subprocess
import time

REPO = Path(__file__).resolve().parents[2]


class ProcessMetrics:
    def __init__(self):
        self.peak_rss = 0
        self.cpu_seconds = None
        if os.name != 'nt':
            return
        from ctypes import wintypes
        class Memory(ctypes.Structure):
            _fields_ = [('cb', wintypes.DWORD), ('faults', wintypes.DWORD)] + [
                (name, ctypes.c_size_t) for name in ('peak_rss', 'rss', 'pool_peak', 'pool',
                                                   'nonpaged_peak', 'nonpaged', 'pagefile', 'pagefile_peak')]
        self.memory_type = Memory
        self.filetime = wintypes.FILETIME
        self.psapi = ctypes.WinDLL('psapi', use_last_error=True)
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(Memory), wintypes.DWORD]
        self.kernel.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(self.filetime)] * 4

    def sample(self, process):
        if os.name != 'nt':
            return
        memory = self.memory_type()
        memory.cb = ctypes.sizeof(memory)
        if self.psapi.GetProcessMemoryInfo(int(process._handle), ctypes.byref(memory), memory.cb):
            self.peak_rss = max(self.peak_rss, memory.peak_rss)
        values = [self.filetime() for _ in range(4)]
        if self.kernel.GetProcessTimes(int(process._handle), *(ctypes.byref(value) for value in values)):
            ticks = sum((value.dwHighDateTime << 32) + value.dwLowDateTime for value in values[2:])
            self.cpu_seconds = ticks / 10_000_000


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('godot', type=Path)
    parser.add_argument('--pack', type=Path, default=REPO/'.cache/client-release/assets/assets.pck')
    parser.add_argument('--report', type=Path, default=REPO/'reports/client_asset_store_validation.json')
    parser.add_argument('--rounds', type=int, default=1)
    parser.add_argument('--compare', action='store_true')
    parser.add_argument('--script', default='tests/test_asset_store.gd')
    args = parser.parse_args()
    if not 1 <= args.rounds <= 10:
        parser.error('rounds must be between 1 and 10')
    args.report.parent.mkdir(parents=True, exist_ok=True)
    # The Windows console binary is a small launcher; sample the actual engine.
    godot = args.godot.resolve()
    if os.name == 'nt' and godot.name.endswith('_console.exe'):
        engine = godot.with_name(godot.name.replace('_console.exe', '.exe'))
        if engine.is_file():
            godot = engine
    source_files = [REPO/'client'/name for name in ('project.godot', 'assets_runtime/asset_store.gd', args.script)]
    source_hashes = {str(path.relative_to(REPO)):hashlib.sha256(path.read_bytes()).hexdigest() for path in source_files}
    records = []
    pack_hash = hashlib.sha256(args.pack.read_bytes()).hexdigest()
    for round_id in range(args.rounds):
        for concurrency in ([1, 2] if args.compare else [1]):
            name = f'{args.report.stem}_r{round_id+1}_c{concurrency}'
            output = args.report.parent/(name+'.json')
            log_path = args.report.parent/(name+'.log')
            output.unlink(missing_ok=True)  # Never accept a stale report after a crash.
            command = [str(godot), '--headless', '--path', str(REPO/'client'),
                       '--script', args.script, '--', str(args.pack.resolve()),
                       str(output.resolve()), str(concurrency)]
            started = time.perf_counter()
            metrics = ProcessMetrics()
            with log_path.open('w', encoding='utf-8') as log:
                process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
                try:
                    while True:
                        metrics.sample(process)
                        if time.perf_counter()-started > 180:
                            process.kill()
                            process.wait()
                            break
                        try:
                            process.wait(timeout=0.05)
                            break
                        except subprocess.TimeoutExpired:
                            pass
                    metrics.sample(process)
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.wait()
            log = log_path.read_text(encoding='utf-8', errors='replace')
            engine_errors = [line for line in log.splitlines() if 'ERROR:' in line or 'leaked at exit' in line]
            result = json.loads(output.read_text(encoding='utf-8')) if output.is_file() else {}
            unchanged = source_hashes == {str(path.relative_to(REPO)):hashlib.sha256(path.read_bytes()).hexdigest() for path in source_files}
            passed = process.returncode == 0 and not engine_errors and result.get('status') == 'PASS' and 'ASSETSTORE_RESULT ' in log and unchanged
            record = {'round':round_id+1, 'texture_jobs':concurrency, 'status':'PASS' if passed else 'FAIL',
                      'exit_code':process.returncode, 'engine_errors':engine_errors,
                      'wall_seconds':round(time.perf_counter()-started,3), 'cpu_seconds':metrics.cpu_seconds,
                      'peak_process_rss_bytes':metrics.peak_rss or None, 'script_result':result,
                      'log':str(log_path), 'command':command}
            records.append(record)
            print(json.dumps({key:record[key] for key in ('round','texture_jobs','status','wall_seconds','cpu_seconds','peak_process_rss_bytes')},ensure_ascii=False),flush=True)
    baseline = [record for record in records if record['texture_jobs'] == 1]
    report = {'status':'PASS' if all(row['status']=='PASS' for row in baseline) else 'FAIL',
              'baseline_texture_jobs':1, 'pck_sha256':pack_hash, 'source_sha256':source_hashes, 'runs':records,
              'scope':'headless fresh-process asset lifecycle; no VRAM/gameplay/network claim'}
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    return 0 if report['status']=='PASS' else 1


if __name__ == '__main__':
    raise SystemExit(main())
