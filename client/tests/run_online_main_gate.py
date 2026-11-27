from __future__ import annotations
from pathlib import Path
import argparse
import json
import hashlib
import os
import platform
import queue
import signal
import subprocess
import sys
import tempfile
import threading
import time

from process_telemetry import ProcessTelemetry

PASSWORD = "Online-Gate-Password-123!"
MARKER = "ONLINE_MAIN_GATE_PASS role="

def write_synthetic_player_rules(progression: Path, appearance: Path) -> None:
    """Portable CI rules. Deliberately synthetic; source-backed runs pass explicit manifests."""
    levels = [
        {
            "level": level,
            "required_experience": 10 if level == 1 else 1000 + level,
            "attribute_points_awarded": 2,
            "base_hp": 150,
            "base_mp": 120,
            "base_attack": 20,
            "base_defense": 10,
            "base_magic_attack": 10,
            "base_magic_defense": 10,
            "base_hit": 10,
            "base_dodge": 5,
            "base_will": 100,
        }
        for level in range(1, 101)
    ]
    zero = [0] * 200
    race = {
        "strength_attack": zero,
        "constitution_hp": zero,
        "dexterity_hit": zero,
        "dexterity_dodge": zero,
        "wisdom_mp": zero,
        "wisdom_magic_attack": zero,
    }
    progression.write_text(json.dumps({
        "schema": "sgtest.character_progression_rules/v1",
        "status": "pass",
        "rules_verified": True,
        "gameplay_ready": True,
        "source": "synthetic online-main CI player rules",
        "max_level": 100,
        "max_attribute": 200,
        "levels": levels,
        "races": {"boy": race, "girl": race, "migu": race, "robot": race},
    }, separators=(",", ":")), encoding="utf-8")
    appearance.write_text(json.dumps({
        "kind": "verified_character_appearance_rules",
        "version": 1,
        "rules_verified": True,
        "source_reference": "synthetic online-main CI appearance",
        "choices": [{
            "race": 0, "hair_index": 0, "hair_color": 0,
            "body_index": 0, "leg_index": 0, "sleeve_color": 0,
        }],
    }, separators=(",", ":")), encoding="utf-8")

def run_checked(command: list[str], cwd: Path, env: dict[str, str], timeout: float) -> str:
    result = subprocess.run(command, cwd=cwd, env=env, text=True,
                            encoding="utf-8", errors="replace",
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError("command failed: " + " ".join(command[:4]) + "\n" + result.stdout)
    return result.stdout

def capture(process: subprocess.Popen[str], lines: list[str], notices: queue.Queue[str], echo: bool = False) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        clean = line.rstrip()
        lines.append(clean)
        notices.put(clean)
        if echo and clean.startswith("ONLINE_MAIN_GATE_"):
            print(clean, flush=True)

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
    parser.add_argument("--native", action="store_true",
                        help="run the two main-scene clients as visible Windows renderer processes")
    parser.add_argument("--manual-input", action="store_true",
                        help="in native mode require an OS mouse-driven observer movement before continuing")
    parser.add_argument("--metrics-report", type=Path,
                        help="write opt-in headless Host/2-Godot CPU and RSS telemetry JSON")
    parser.add_argument("--character-progression-rules", type=Path,
                        help="verified progression manifest; when set the native HUD gate also requires EXP authority")
    parser.add_argument("--character-appearance-rules", type=Path,
                        help="verified character appearance allowlist used by player-created formal HUD gates")
    parser.add_argument("--real-transfer", action="store_true",
                        help="use private original Map15/Map13 runtime and audited NPC transfers instead of synthetic gate content")
    parser.add_argument("--content-root", type=Path,
                        help="private Converter runtime root for --real-transfer")
    parser.add_argument("--pack", type=Path,
                        help="matching private external assets.pck for --real-transfer")
    args = parser.parse_args()
    if args.character_progression_rules:
        args.character_progression_rules = args.character_progression_rules.resolve()
        if not args.character_progression_rules.is_file():
            parser.error("character progression manifest not found")
    if args.character_appearance_rules:
        args.character_appearance_rules = args.character_appearance_rules.resolve()
        if not args.character_appearance_rules.is_file():
            parser.error("character appearance manifest not found")
    if bool(args.character_progression_rules) != bool(args.character_appearance_rules):
        parser.error("formal HUD authority requires both progression and appearance manifests")
    if args.manual_input and not args.native:
        parser.error("--manual-input requires --native")
    if args.real_transfer:
        if not args.content_root or not args.pack:
            parser.error("--real-transfer requires --content-root and --pack")
        args.content_root = args.content_root.resolve()
        args.pack = args.pack.resolve()
        if not args.content_root.is_dir():
            parser.error("real-transfer content root not found")
        if not args.pack.is_file():
            parser.error("real-transfer pack not found")
        if not args.character_progression_rules or not args.character_appearance_rules:
            parser.error("--real-transfer requires source-backed progression and appearance manifests")
        if args.manual_input:
            parser.error("--manual-input is not part of the deterministic real-transfer stress gate")
    elif args.content_root or args.pack:
        parser.error("--content-root/--pack are only valid with --real-transfer")
    source_backed_player_rules = bool(args.character_progression_rules)
    if args.metrics_report and os.name == "nt" and args.godot.lower().endswith("_console.exe"):
        # Godot console executable launches a sibling real engine on Windows.
        # Never report the lightweight wrapper as the measured game process.
        engine = Path(args.godot[:-len("_console.exe")] + ".exe")
        if not engine.is_file():
            parser.error("actual Godot engine executable missing beside console wrapper")
        args.godot = str(engine)

    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    base_env = os.environ.copy()
    base_env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    base_env["SGTEST_AES_KEY_HEX"] = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
    base_env["SGTEST_AES_IV_HEX"] = "000102030405060708090a0b0c0d0e0f"

    processes: list[subprocess.Popen[str]] = []
    logs: dict[str, list[str]] = {}
    telemetry = ProcessTelemetry()
    successful = False
    all_started = time.monotonic()
    synthetic_pack_info: dict[str, str | int] = {}
    try:
        # On Windows an exception can occur while the private dotnet Host still
        # has published DLLs open. Do not let TemporaryDirectory cleanup mask
        # the real gate failure; the outer finally terminates every process.
        with tempfile.TemporaryDirectory(prefix="sgtest-online-main-", ignore_cleanup_errors=True) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            pack = args.pack if args.real_transfer else temp / "assets.pck"
            content = args.content_root if args.real_transfer else temp / "content"
            database = temp / "identity.db"
            if not source_backed_player_rules:
                synthetic_progression = temp / "character-progression-synthetic.json"
                synthetic_appearance = temp / "character-appearance-synthetic.json"
                write_synthetic_player_rules(synthetic_progression, synthetic_appearance)
                args.character_progression_rules = synthetic_progression
                args.character_appearance_rules = synthetic_appearance
            # Running local game servers may lock the default Release binary;
            # E2E must build/run its own private Host without touching them.
            host_dll = temp / "host-bin/SGTest.Server.Host.dll"

            if not args.real_transfer:
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
                if args.metrics_report:
                    synthetic_pack_info = {
                        "sha256": hashlib.sha256(pack.read_bytes()).hexdigest(),
                        "size_bytes": pack.stat().st_size,
                    }

            run_checked(["dotnet", "build", str(host_project), "-c", "Release", "--nologo",
                         "-o", str(host_dll.parent)],
                        repo, base_env, args.timeout)

            provision_env = base_env.copy()
            provision_env["SGTEST_ACCOUNT_PASSWORD"] = PASSWORD
            actor_spawn_x = "840" if args.manual_input else "760"
            for username, character, x, y in [
                ("gate_observer", "GateObserver", "880", "800"),
                ("gate_actor", "GateActor", actor_spawn_x, "800"),
            ]:
                out = run_checked(
                    ["dotnet", str(host_dll), "account-create", "--identity", str(database),
                     "--username", username], repo, provision_env, args.timeout)
                if "ACCOUNT_CREATED" not in out:
                    raise RuntimeError("account provisioning did not create " + username)
                if not args.character_progression_rules:
                    out = run_checked(
                        ["dotnet", str(host_dll), "character-create", "--identity", str(database),
                         "--username", username, "--content", str(content), "--name", character,
                         "--slot", "0", "--map", "15", "--x", x, "--y", y],
                        repo, provision_env, args.timeout)
                    if "CHARACTER_CREATED" not in out:
                        raise RuntimeError("character provisioning did not create " + username)

            host_env = base_env.copy()
            host_seconds = 70 if args.real_transfer else (45 if args.manual_input else 30)
            host_command = ["dotnet", str(host_dll), "serve", "--content", str(content),
                            "--identity", str(database),
                            "--bind", "127.0.0.1", "--port", "0", "--max-connections", "8",
                            "--max-per-address", "4", "--seconds", str(host_seconds)]
            if args.character_progression_rules:
                spawn_x, spawn_y = ("7872", "11296") if args.real_transfer else ("840", "800")
                host_command += ["--character-progression-rules", str(args.character_progression_rules),
                                 "--character-appearance-rules", str(args.character_appearance_rules),
                                 "--character-spawn-map", "15", "--character-spawn-x", spawn_x,
                                 "--character-spawn-y", spawn_y]
            host = subprocess.Popen(
                host_command,
                cwd=repo, env=host_env, text=True, encoding="utf-8", errors="replace",
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT, bufsize=1)
            processes.append(host)
            if args.metrics_report:
                telemetry.register("host", host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(target=capture, args=(host, logs["host"], host_events),
                             daemon=True).start()
            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            if ready.get("result") != "AES_HOST_READY":
                raise RuntimeError("formal Host readiness payload invalid")
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str, username: str):
                env = base_env.copy()
                env["SGTEST_E2E_USERNAME"] = username
                env["SGTEST_E2E_PASSWORD"] = PASSWORD
                env["SGTEST_E2E_WHISPER_TARGET_NAME"] = "GateObserver" if role == "actor" else "GateActor"
                if args.character_progression_rules:
                    env["SGTEST_E2E_REQUIRE_HUD_EXP"] = "1"
                    env["SGTEST_E2E_PLAYER_CREATE_NAME"] = "GateActor" if role == "actor" else "GateObserver"
                if args.real_transfer:
                    env["SGTEST_E2E_REAL_TRANSFER"] = "1"
                if args.manual_input and role == "observer":
                    env["SGTEST_E2E_REQUIRE_MANUAL_INPUT"] = "1"
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_main_gate.gd", "--",
                    "--pack", str(pack), "--host", "127.0.0.1", "--port", str(port),
                    "--e2e-role", role,
                ]
                process = subprocess.Popen(command, cwd=repo, env=env, text=True,
                                           encoding="utf-8", errors="replace",
                                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                           bufsize=1)
                processes.append(process)
                if args.metrics_report:
                    telemetry.register(role, process)
                logs[role] = []
                events: queue.Queue[str] = queue.Queue()
                threading.Thread(target=capture,
                                 args=(process, logs[role], events, args.manual_input and role == "observer"),
                                 daemon=True).start()
                return process, events

            observer, observer_events = start_client("observer", "gate_observer")
            observer_ready = ("ONLINE_MAIN_GATE_REAL_READY role=observer"
                              if args.real_transfer else "ONLINE_MAIN_GATE_READY role=observer")
            wait_for(observer_events, observer_ready, observer, deadline)
            if args.manual_input:
                wait_for(observer_events, "ONLINE_MAIN_GATE_MANUAL_INPUT_PASS role=observer", observer, deadline)
            actor, actor_events = start_client("actor", "gate_actor")

            wait_for(actor_events, MARKER + "actor", actor, deadline)
            wait_for(observer_events, MARKER + "observer", observer, deadline)
            actor.wait(timeout=max(1.0, deadline - time.monotonic()))
            if args.metrics_report:
                telemetry.finish("actor")
            observer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if args.metrics_report:
                telemetry.finish("observer")
            if actor.returncode != 0 or observer.returncode != 0:
                raise RuntimeError("main-scene client process returned failure")

            if os.name != "nt" and host.poll() is None:
                host.send_signal(signal.SIGINT)
            host.wait(timeout=max(1.0, deadline - time.monotonic()))
            if args.metrics_report:
                telemetry.finish("host")
            if host.returncode != 0 or not any('"AES_HOST_STOPPED"' in line for line in logs["host"]):
                raise RuntimeError("formal Host did not stop cleanly")

            if args.real_transfer:
                required_actor = [
                    "ONLINE_MAIN_GATE_HUD_AUTHORITY role=actor",
                    "ONLINE_MAIN_GATE_REAL_READY role=actor",
                    "ONLINE_MAIN_GATE_REAL_MAP13_MOVE_ACK role=actor cycle=10",
                    "ONLINE_MAIN_GATE_REAL_MAP15_MOVE_ACK role=actor cycle=10",
                    "ONLINE_MAIN_GATE_REAL_TRANSFER_STRESS_PASS role=actor cycles=10",
                ]
                required_observer = [
                    "ONLINE_MAIN_GATE_HUD_AUTHORITY role=observer",
                    "ONLINE_MAIN_GATE_REAL_READY role=observer",
                    "ONLINE_MAIN_GATE_REAL_AOI_STRESS_PASS role=observer cycles=10",
                ]
            else:
                required_actor = [
                    "ONLINE_MAIN_GATE_HUD_AUTHORITY role=actor",
                    "ONLINE_MAIN_GATE_MONSTER role=actor",
                    "ONLINE_MAIN_GATE_OBSTACLE_ROUTE role=actor",
                    "ONLINE_MAIN_GATE_MOVE_CAMERA role=actor",
                    "ONLINE_MAIN_GATE_CHAT_UI_SUBMIT role=actor input=PASS",
                    "ONLINE_MAIN_GATE_CHAT_UI_ECHO role=actor authority=PASS history=PASS bubble=PASS",
                    "ONLINE_MAIN_GATE_WHISPER_UI_SUBMIT role=actor input=PASS",
                    "ONLINE_MAIN_GATE_WHISPER_UI_ECHO role=actor authority=PASS history=PASS",
                    "ONLINE_MAIN_GATE_MOTION_SMOOTH role=actor",
                    "ONLINE_MAIN_GATE_LONG_HOLD_PASS role=actor",
                    "ONLINE_MAIN_GATE_MAP13 role=actor",
                    "ONLINE_MAIN_GATE_MAP13_MOVE_ACK role=actor cycle=10",
                    "ONLINE_MAIN_GATE_RETURN_MAP15 role=actor",
                    "ONLINE_MAIN_GATE_RETURN_MOVE_ACK role=actor cycle=10",
                    "ONLINE_MAIN_GATE_TRANSFER_STRESS_PASS role=actor cycles=10",
                ]
                required_observer = [
                    "ONLINE_MAIN_GATE_HUD_AUTHORITY role=observer",
                    "ONLINE_MAIN_GATE_MONSTER role=observer",
                    "ONLINE_MAIN_GATE_SPAWN role=observer",
                    "ONLINE_MAIN_GATE_DELTA role=observer",
                    "ONLINE_MAIN_GATE_CHAT_UI_DELIVERY role=observer history=PASS bubble=PASS",
                    "ONLINE_MAIN_GATE_WHISPER_UI_DELIVERY role=observer authority=PASS history=PASS",
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
                if '"AES_HOST_' in line:
                    print(line)
            if args.real_transfer:
                print("ONLINE_MAIN_SCENE_FORMAL_HOST_GATE_PASS "
                      "formal_host=true main_scenes=2 aes_cbc=true provision_cli=true "
                      "move=true aoi=true hud_authority=true map15_map13_return=true transfer_cycles=10 "
                      "source_backed_portals=true resource_release=true synthetic_content=false private_original_content=true "
                      f"native_visible={str(args.native).lower()} manual_input=false")
            else:
                print("ONLINE_MAIN_SCENE_FORMAL_HOST_GATE_PASS "
                      "formal_host=true main_scenes=2 aes_cbc=true provision_cli=true "
                      "move=true aoi=true monster_state=true camera_follow=true hud_authority=true chat_ui_authority=true whisper_ui_authority=true chat_bubble=true map15_map13_return=true transfer_cycles=10 "
                      "resource_release=true synthetic_content=true private_original_content=false "
                      f"native_visible={str(args.native).lower()} manual_input={str(args.manual_input).lower()}")
            successful = True
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
                process.wait()
        telemetry.close()
        if args.metrics_report:
            # Never include credentials, private content paths or AES key material.
            report = {
                "kind": "sgtest_formal_host_main_headless_process_baseline",
                "version": 1,
                "status": "PASS" if successful else "FAIL",
                "platform": platform.platform(),
                "cpu_count": os.cpu_count(),
                "sample_interval_ms": int(telemetry.interval_seconds * 1000),
                "run_wall_seconds": round(time.monotonic() - all_started, 3),
                "scenario": {
                    "server": "formal dotnet Host",
                    "clients": 2,
                    "client_kind": "actual Godot main-scene processes",
                    "maps": [15, 13, 15],
                    "asset_source": "synthetic test-only external PCK",
                    "headless": True,
                    "windows_gpu_render": False,
                    "fps_p95_p99": None,
                    "gpu_vram_bytes": None,
                    "server_tick_p95_p99": None,
                    "network_bytes_per_second": None,
                },
                "source_sha256": {
                    "runner": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    "process_telemetry": hashlib.sha256(
                        Path(__file__).with_name("process_telemetry.py").read_bytes()
                    ).hexdigest(),
                },
                "synthetic_pack": synthetic_pack_info,
                "processes": telemetry.report(),
            }
            args.metrics_report.parent.mkdir(parents=True, exist_ok=True)
            args.metrics_report.write_text(
                json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
            )
            print("ONLINE_MAIN_PROCESS_METRICS " + json.dumps({
                role: {"peak_rss_bytes": row["peak_process_rss_bytes"],
                       "cpu_seconds": row["cpu_seconds"], "samples": row["sample_count"]}
                for role, row in report["processes"].items()
            }, sort_keys=True), flush=True)

if __name__ == "__main__":
    raise SystemExit(main())
