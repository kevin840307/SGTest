"""Lightweight OS process telemetry for reproducible headless E2E baselines.

Samples actual Godot and dotnet child processes, not the Python test runner.
CPU seconds are per-process and RSS is OS resident memory; neither is GPU VRAM.
A process can exit between samples, so a missing sample is reported, not zero.
"""
from __future__ import annotations

import ctypes
import os
import threading
import time
from typing import Any


def _linux_process(pid: int) -> tuple[int, int, float] | None:
    """Return (peak RSS bytes, current RSS bytes, user+system CPU seconds)."""
    try:
        with open(f"/proc/{pid}/stat", encoding="utf-8") as handle:
            # comm can contain spaces and parentheses: fields start after its final ')'.
            fields = handle.read().rsplit(")", 1)[1].split()
        cpu = (int(fields[11]) + int(fields[12])) / os.sysconf("SC_CLK_TCK")
        rss = int(fields[21]) * os.sysconf("SC_PAGE_SIZE")
        peak = rss
        with open(f"/proc/{pid}/status", encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("VmHWM:"):
                    peak = max(peak, int(line.split()[1]) * 1024)
                    break
        return peak, rss, cpu
    except (OSError, IndexError, ValueError):
        return None


def _windows_process(process: Any) -> tuple[int, int, float] | None:
    from ctypes import wintypes

    class Memory(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD), ("faults", wintypes.DWORD)] + [
            (name, ctypes.c_size_t) for name in (
                "peak_rss", "rss", "pool_peak", "pool", "nonpaged_peak",
                "nonpaged", "pagefile", "pagefile_peak",
            )
        ]

    try:
        memory = Memory()
        memory.cb = ctypes.sizeof(memory)
        psapi = ctypes.WinDLL("psapi", use_last_error=True)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        psapi.GetProcessMemoryInfo.argtypes = [
            wintypes.HANDLE, ctypes.POINTER(Memory), wintypes.DWORD,
        ]
        kernel.GetProcessTimes.argtypes = [
            wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4),
        ]
        if not psapi.GetProcessMemoryInfo(int(process._handle), ctypes.byref(memory), memory.cb):
            return None
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(
            int(process._handle), *(ctypes.byref(value) for value in times)
        ):
            return None
        ticks = sum(
            (value.dwHighDateTime << 32) | value.dwLowDateTime
            for value in times[2:]
        )
        return int(memory.peak_rss), int(memory.rss), ticks / 10_000_000
    except (AttributeError, OSError, ValueError):
        return None


def read_process(process: Any) -> tuple[int, int, float] | None:
    if os.name == "nt":
        return _windows_process(process)
    if os.path.isdir("/proc"):
        return _linux_process(process.pid)
    return None


class ProcessTelemetry:
    """Threaded sampler that never blocks the network/render test loop."""

    def __init__(self, interval_seconds: float = 0.05):
        if interval_seconds <= 0:
            raise ValueError("sample interval must be positive")
        self.interval_seconds = interval_seconds
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._entries: dict[str, tuple[Any, dict[str, Any]]] = {}
        self._thread: threading.Thread | None = None

    def register(self, role: str, process: Any) -> None:
        now = time.monotonic()
        with self._lock:
            if role in self._entries:
                raise ValueError("duplicate telemetry role")
            self._entries[role] = (process, {
                "pid": process.pid,
                "samples": 0,
                "peak_process_rss_bytes": None,
                "first_process_rss_bytes": None,
                "last_process_rss_bytes": None,
                "cpu_seconds": None,
                "started_at": now,
                "ended_at": None,
            })
        self.sample()
        if self._thread is None:
            self._thread = threading.Thread(
                target=self._run, name="sgtest-e2e-process-telemetry", daemon=True
            )
            self._thread.start()

    def _run(self) -> None:
        while not self._stop.wait(self.interval_seconds):
            self.sample()

    def sample(self) -> None:
        with self._lock:
            for process, record in self._entries.values():
                if record["ended_at"] is not None or process.poll() is not None:
                    continue
                current = read_process(process)
                if current is None:
                    continue
                peak, resident, cpu = current
                record["samples"] += 1
                if record["peak_process_rss_bytes"] is None:
                    record["peak_process_rss_bytes"] = peak
                    record["first_process_rss_bytes"] = resident
                else:
                    record["peak_process_rss_bytes"] = max(
                        record["peak_process_rss_bytes"], peak
                    )
                record["last_process_rss_bytes"] = resident
                record["cpu_seconds"] = round(cpu, 4)

    def finish(self, role: str) -> None:
        self.sample()
        with self._lock:
            self._entries[role][1]["ended_at"] = time.monotonic()

    def close(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=2)
        self.sample()

    def report(self) -> dict[str, Any]:
        with self._lock:
            now = time.monotonic()
            return {
                role: {
                    "sample_count": rec["samples"],
                    "peak_process_rss_bytes": rec["peak_process_rss_bytes"],
                    "first_process_rss_bytes": rec["first_process_rss_bytes"],
                    "last_process_rss_bytes": rec["last_process_rss_bytes"],
                    "cpu_seconds": rec["cpu_seconds"],
                    "observed_wall_seconds": round(
                        (rec["ended_at"] or now) - rec["started_at"], 3
                    ),
                }
                for role, (_, rec) in self._entries.items()
            }
