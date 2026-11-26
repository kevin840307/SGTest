from pathlib import Path
import argparse
import os
import queue
import re
import subprocess
import sys
import threading
import time

READY = re.compile(r"LOGIN_FIXTURE_READY port=(\d+) username=")


def main() -> int:
    parser = argparse.ArgumentParser(description="Run real Godot -> C# AES login/CharacterChoose/Move E2E")
    parser.add_argument("godot", help="Godot 4.7.2 console executable")
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--app-ui", action="store_true",
                        help="exercise real main-scene login/choose/logout UI over AES (no private textures)")
    parser.add_argument("--ui-screenshot", type=Path,
                        help="with --app-ui, render the UI natively and save a private screenshot")
    parser.add_argument("--multi", action="store_true",
                        help="run two concurrent real Godot AES clients against the same 30Hz World")
    parser.add_argument("--multi-reconnect", action="store_true",
                        help="run observer + actor disconnect + same-character rejoin AOI lifecycle E2E")
    parser.add_argument("--crossmap", action="store_true",
                        help="run two-client Map15 -> Map13 -> Map15 trusted transfer AES E2E")
    parser.add_argument("--portal", action="store_true",
                        help="run two-client audited NPC interaction AES transfer E2E")
    parser.add_argument("--reconnect", action="store_true",
                        help="force one AES socket drop and require automatic same-character reconnect")
    parser.add_argument("--combat", action="store_true",
                        help="run real AES ClientSession normal-attack authority E2E")
    parser.add_argument("--target-combat", action="store_true",
                        help="run v6 source-backed close retaliation without EXP manifest")
    parser.add_argument("--target-combat-miss", action="store_true",
                        help="run source-backed retaliation MISS through AES and WorldView")
    parser.add_argument("--combat-observer", action="store_true",
                        help="run two real AES clients and verify remote player attack visuals")
    parser.add_argument("--skill-aoe", action="store_true",
                        help="run real AES self-centered AoE skill against three monsters")
    parser.add_argument("--skill-aoe-observer", action="store_true",
                        help="run two real AES clients and verify multi-target AoE visuals")
    parser.add_argument("--auto-attack", action="store_true",
                        help="run real AES out-of-range attack -> Server A* approach -> retry E2E")
    parser.add_argument("--discard", action="store_true",
                        help="run real AES ClientSession durable inventory discard E2E")
    parser.add_argument("--recovery-hotkey", action="store_true",
                        help="run original hotbar F1 -> authoritative recovery -> HUD/inventory -> relog E2E")
    parser.add_argument("--storage", action="store_true",
                        help="run real AES ClientSession personal storage deposit/withdraw E2E")
    parser.add_argument("--pet", action="store_true",
                        help="run real AES Pet summon, reconnect restoration and recall E2E")
    parser.add_argument("--mecha", action="store_true",
                        help="run real AES Mecha mount, reconnect restoration and dismount E2E")
    parser.add_argument("--equipment", action="store_true",
                        help="run real AES ClientSession durable equipment equip/unequip E2E")
    parser.add_argument("--bank", action="store_true",
                        help="run real AES ClientSession scoped bank deposit/withdraw E2E")
    parser.add_argument("--repair", action="store_true",
                        help="run real AES ClientSession server-authoritative repair E2E")
    parser.add_argument("--enhancement", action="store_true",
                        help="run real AES ClientSession server-authoritative equipment enhancement E2E")
    parser.add_argument("--quest", action="store_true",
                        help="run real AES verified quest progression and relogin E2E")
    parser.add_argument("--trade", action="store_true",
                        help="run two real AES clients through durable player trade E2E")
    parser.add_argument("--player-shop", action="store_true",
                        help="run two real AES clients through authoritative player-shop E2E")
    parser.add_argument("--pvp", action="store_true",
                        help="run two real AES clients through authoritative player PvP E2E")
    parser.add_argument("--register", action="store_true",
                        help="create a public account then authenticate on the same AES connection")
    parser.add_argument("--appearance", action="store_true",
                        help="create with authoritative appearance choices and verify durable relogin")
    args = parser.parse_args()
    if args.app_ui and (args.multi or args.multi_reconnect or args.crossmap or args.portal or args.reconnect or args.combat or args.target_combat or args.target_combat_miss or args.combat_observer or args.skill_aoe or args.skill_aoe_observer or args.discard or args.recovery_hotkey or args.storage or args.pet or args.mecha or args.equipment or args.bank or args.repair or args.enhancement or args.quest or args.trade or args.player_shop or args.pvp or args.auto_attack or args.register or args.appearance):
        parser.error("--app-ui uses the single-client login fixture")
    if args.ui_screenshot and not args.app_ui:
        parser.error("--ui-screenshot requires --app-ui")
    repo = Path(__file__).resolve().parents[2]
    fixture = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    env["SGTEST_AES_KEY_HEX"] = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f"
    env["SGTEST_AES_IV_HEX"] = "000102030405060708090a0b0c0d0e0f"
    # The Godot fixture subprocesses inherit these test-only keys.
    os.environ["SGTEST_AES_KEY_HEX"] = env["SGTEST_AES_KEY_HEX"]
    os.environ["SGTEST_AES_IV_HEX"] = env["SGTEST_AES_IV_HEX"]
    # Build time is environment/setup work, not an E2E gameplay deadline.
    # GitHub-hosted runners can occasionally cross the historical 30s default
    # on a cold NuGet/MSBuild path; keep gameplay timeouts unchanged.
    build = subprocess.run(
        ["dotnet", "build", str(fixture), "-c", "Release", "--nologo"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        timeout=max(90.0, args.timeout))
    if build.returncode != 0:
        print(build.stdout)
        return 2
    if args.combat_observer:
        return run_combat_observer(args, repo, fixture, env)
    if args.skill_aoe_observer:
        return run_skill_aoe_observer(args, repo, fixture, env)
    if args.skill_aoe:
        return run_skill_aoe(args, repo, fixture, env)
    if args.auto_attack:
        return run_auto_attack(args, repo, fixture, env)
    if args.discard:
        return run_discard(args, repo, fixture, env)
    if args.recovery_hotkey:
        return run_recovery_hotkey(args, repo, fixture, env)
    if args.storage:
        return run_storage(args, repo, fixture, env)
    if args.pet:
        return run_pet(args, repo, fixture, env)
    if args.mecha:
        return run_mecha(args, repo, fixture, env)
    if args.equipment:
        return run_equipment(args, repo, fixture, env)
    if args.bank:
        return run_bank(args, repo, fixture, env)
    if args.repair:
        return run_repair(args, repo, fixture, env)
    if args.enhancement:
        return run_enhancement(args, repo, fixture, env)
    if args.quest:
        return run_quest(args, repo, fixture, env)
    if args.trade:
        return run_trade(args, repo, fixture, env)
    if args.player_shop:
        return run_player_shop(args, repo, fixture, env)
    if args.pvp:
        return run_pvp(args, repo, fixture, env)
    if args.register:
        return run_identity_creation(args, repo, fixture, env)
    if args.appearance:
        return run_identity_creation(args, repo, fixture, env, appearance=True)
    if args.target_combat_miss:
        return run_target_combat(args, repo, fixture, env, force_miss=True)
    if args.target_combat:
        return run_target_combat(args, repo, fixture, env)
    if args.combat:
        return run_combat(args, repo, fixture, env)
    if args.reconnect:
        return run_reconnect(args, repo, fixture, env)
    if args.crossmap:
        return run_crossmap(args, repo, fixture, env)
    if args.portal:
        return run_multiplayer(args, repo, fixture, env, portal=True)
    if args.multi_reconnect:
        return run_multiplayer(args, repo, fixture, env, reconnect=True)
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
        port = match.group(1)
        client = subprocess.run(
            [args.godot, *([] if args.ui_screenshot else ["--headless"]), "--path", str(repo / "client"),
             "--script", "res://tests/test_session_ui_aes.gd" if args.app_ui else "res://tests/test_client_transport_aes.gd", "--", port,
             *([str(args.ui_screenshot.resolve())] if args.ui_screenshot else [])],
            cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=args.timeout)
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_AES_LOGIN_PASS" not in client.stdout or "receipt=PASS" not in client.stdout or "world_ready=PASS" not in client.stdout:
            raise RuntimeError("Godot AES login test failed")
        remaining = max(1.0, deadline - time.monotonic())
        server.wait(timeout=remaining)
        reader.join(timeout=2)
        output = "\n".join(lines)
        if server.returncode != 0 or "LOGIN_FIXTURE_MOVE_PASS" not in output or "LOGIN_FIXTURE_PASS" not in output or "LOGIN_FIXTURE_RECEIPT_ROUTED" not in output or "LOGIN_FIXTURE_WORLD_READY_ROUTED" not in output:
            raise RuntimeError("C# fixture did not confirm authenticated Move")
        for line in lines:
            if line.startswith("LOGIN_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_LOGIN_E2E_PASS aes_cbc=true authenticated_move=true authoritative_receipt_returned=true world_ready_delivered=true transport_thread=true reducer_thread=true")
        return 0
    except subprocess.TimeoutExpired as error:
        print("AES_LOGIN_RUNNER_FAIL", error, file=sys.stderr)
        partial = error.stdout or ""
        if isinstance(partial, bytes):
            partial = partial.decode("utf-8", errors="replace")
        if partial:
            print("AES_LOGIN_GODOT_PARTIAL_BEGIN", file=sys.stderr)
            print(partial, file=sys.stderr, end="" if partial.endswith("\n") else "\n")
            print("AES_LOGIN_GODOT_PARTIAL_END", file=sys.stderr)
        for line in lines[-30:]:
            print(line, file=sys.stderr)
        return 1
    except Exception as error:
        print("AES_LOGIN_RUNNER_FAIL", error, file=sys.stderr)
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





def run_identity_creation(args, repo: Path, fixture: Path, env: dict[str, str], *, appearance=False) -> int:
    prefix = "APPEARANCE_FIXTURE" if appearance else "REGISTER_FIXTURE"
    client_marker = "CLIENT_CHARACTER_APPEARANCE_AES_PASS" if appearance else "CLIENT_REGISTRATION_AES_PASS"
    ready_pattern = re.compile(prefix + r"_READY port=(\d+) username=")
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--appearance" if appearance else "--register"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.2)
            except queue.Empty:
                continue
            match = ready_pattern.search(line)
            if match:
                ready = match
                break
        if ready is None:
            raise RuntimeError(prefix+" never became ready")
        port = ready.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_character_appearance_aes.gd" if appearance else "res://tests/test_client_registration_aes.gd",
             "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, timeout=args.timeout)
        print(client.stdout, end="")
        if client.returncode != 0 or client_marker not in client.stdout:
            raise RuntimeError("Godot identity ClientSession failed")
        remaining = max(1.0, deadline - time.monotonic())
        server.wait(timeout=remaining)
        output = "\n".join(lines)
        if server.returncode != 0 or prefix+"_PASS" not in output:
            raise RuntimeError("C# identity fixture did not confirm durable state")
        for line in lines:
            if line.startswith(prefix+"_"):
                print(line)
        if appearance:
            print("GODOT_CSHARP_AES_APPEARANCE_E2E_PASS server_choices=true created=true durable_relogin=true choose_world=true")
        else:
            print("GODOT_CSHARP_AES_REGISTRATION_E2E_PASS created=true explicit_auth=true same_aes_cbc=true character_list_empty=true gameplay_authority=false")
        return 0
    except Exception as error:
        print("AES_APPEARANCE_RUNNER_FAIL" if appearance else "AES_REGISTRATION_RUNNER_FAIL", error, file=sys.stderr)
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



def run_auto_attack(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--auto-attack"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("AUTO_ATTACK_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("auto-attack fixture never became ready")
        match = re.search(r"AUTO_ATTACK_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed auto-attack fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_auto_approach_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_AUTO_APPROACH_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot auto-approach AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "AUTO_ATTACK_FIXTURE_PASS" not in output:
            raise RuntimeError("C# auto-attack fixture did not confirm A* approach settlement")
        for line in lines:
            if line.startswith("AUTO_ATTACK_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_AUTO_APPROACH_ATTACK_E2E_PASS aes_cbc=true first_attack_out_of_range=true server_astar_move=true self_state_stop=true second_attack_applied=true client_range_guess=false")
        return 0
    except Exception as error:
        print("AES_AUTO_ATTACK_RUNNER_FAIL", error, file=sys.stderr)
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


def run_recovery_hotkey(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--recovery-hotkey"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("RECOVERY_HOTKEY_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("recovery-hotkey fixture never became ready")
        match = re.search(r"RECOVERY_HOTKEY_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed recovery-hotkey fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_recovery_hotkey_aes.gd", "--", port],
            cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_RECOVERY_HOTKEY_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot recovery-hotkey AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "RECOVERY_HOTKEY_FIXTURE_PASS" not in output:
            raise RuntimeError("C# recovery-hotkey fixture did not confirm persistence")
        for line in lines:
            if line.startswith("RECOVERY_HOTKEY_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_RECOVERY_HOTKEY_E2E_PASS "
              "aes_cbc=true input_event_f1=true authoritative_hotkey=true "
              "recovery_receipt=true hud_inventory=true relog_persistence=true")
        return 0
    except Exception as error:
        print("AES_RECOVERY_HOTKEY_RUNNER_FAIL", error, file=sys.stderr)
        for line in lines[-40:]:
            print(line, file=sys.stderr)
        return 1
    finally:
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()

def run_discard(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--discard"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("DISCARD_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("discard fixture never became ready")
        match = re.search(r"DISCARD_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed discard fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_discard_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_DISCARD_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot discard AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "DISCARD_FIXTURE_PASS" not in output:
            raise RuntimeError("C# discard fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("DISCARD_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_DISCARD_E2E_PASS aes_cbc=true durable_inventory=true request_identity_server_bound=true receipt_before_local_mutation=true final_delete_persisted=true")
        return 0
    except Exception as error:
        print("AES_DISCARD_RUNNER_FAIL", error, file=sys.stderr)
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


def run_storage(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--storage"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("STORAGE_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("storage fixture never became ready")
        match = re.search(r"STORAGE_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed storage fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_storage_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_STORAGE_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot storage AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "STORAGE_FIXTURE_PASS" not in output:
            raise RuntimeError("C# storage fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("STORAGE_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_STORAGE_E2E_PASS aes_cbc=true "
              "identity_server_bound=true whole_uid_stack=true "
              "deposit=true withdraw=true durable_uid_and_provenance=true")
        return 0
    except Exception as error:
        print("AES_STORAGE_RUNNER_FAIL", error, file=sys.stderr)
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


def run_pet(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--pet"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("PET_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("pet fixture never became ready")
        match = re.search(r"PET_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed pet fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_pet_aes.gd", "--", port],
            cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_PET_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot pet AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if (
            server.returncode != 0
            or "PET_FIXTURE_SUMMON_PERSISTED" not in output
            or "PET_FIXTURE_PASS" not in output
        ):
            raise RuntimeError("C# pet fixture did not confirm durable lifecycle")
        for line in lines:
            if line.startswith("PET_FIXTURE_"):
                print(line)
        print(
            "GODOT_CSHARP_AES_PET_E2E_PASS aes_cbc=true "
            "summon_persisted=true reconnect_restored=true recall_persisted=true "
            "definition_evolution_identity=true aoi_spawn_despawn=true")
        return 0
    except Exception as error:
        print("AES_PET_RUNNER_FAIL", error, file=sys.stderr)
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


def run_mecha(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--mecha"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("MECHA_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("mecha fixture never became ready")
        match = re.search(r"MECHA_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed mecha fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_mecha_aes.gd", "--", port],
            cwd=repo, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_MECHA_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot mecha AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if (
            server.returncode != 0
            or "MECHA_FIXTURE_MOUNT_PERSISTED" not in output
            or "MECHA_FIXTURE_PASS" not in output
        ):
            raise RuntimeError("C# mecha fixture did not confirm durable lifecycle")
        for line in lines:
            if line.startswith("MECHA_FIXTURE_"):
                print(line)
        print(
            "GODOT_CSHARP_AES_MECHA_E2E_PASS aes_cbc=true "
            "mount_persisted=true reconnect_restored=true dismount_persisted=true "
            "server_owned_roster=true")
        return 0
    except Exception as error:
        print("AES_MECHA_RUNNER_FAIL", error, file=sys.stderr)
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


def run_equipment(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--equipment"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("EQUIPMENT_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("equipment fixture never became ready")
        match = re.search(r"EQUIPMENT_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed equipment fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_equipment_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_EQUIPMENT_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot equipment AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "EQUIPMENT_FIXTURE_PASS" not in output:
            raise RuntimeError("C# equipment fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("EQUIPMENT_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_EQUIPMENT_E2E_PASS aes_cbc=true "
              "identity_server_bound=true slot_server_rule=true "
              "equip=true unequip=true durable_uid_and_provenance=true")
        return 0
    except Exception as error:
        print("AES_EQUIPMENT_RUNNER_FAIL", error, file=sys.stderr)
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

def run_bank(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--bank"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("BANK_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("bank fixture never became ready")
        match = re.search(r"BANK_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed bank fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_bank_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_BANK_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot bank AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "BANK_FIXTURE_PASS" not in output:
            raise RuntimeError("C# bank fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("BANK_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_BANK_E2E_PASS aes_cbc=true "
              "identity_server_bound=true scope_server_rule=true "
              "deposit=true withdraw=true durable_balances=true")
        return 0
    except Exception as error:
        print("AES_BANK_RUNNER_FAIL", error, file=sys.stderr)
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



def run_repair(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--repair"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("REPAIR_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("repair fixture never became ready")
        match = re.search(r"REPAIR_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed repair fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_repair_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_REPAIR_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot repair AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "REPAIR_FIXTURE_PASS" not in output:
            raise RuntimeError("C# repair fixture did not confirm durable settlement")
        for line in lines:
            if line.startswith("REPAIR_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_REPAIR_E2E_PASS aes_cbc=true "
              "identity_server_bound=true npc_rate_server_rule=true "
              "durability_server_auth=true cost_server_auth=true "
              "wallet_debit_once=true equipment_uid_preserved=true")
        return 0
    except Exception as error:
        print("AES_REPAIR_RUNNER_FAIL", error, file=sys.stderr)
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



def run_enhancement(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--enhancement"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("ENHANCEMENT_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("enhancement fixture never became ready")
        match = re.search(
            r"ENHANCEMENT_FIXTURE_READY port=(\d+) username=",
            ready)
        if match is None:
            raise RuntimeError(
                "malformed enhancement fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_enhancement_aes.gd", "--",
             port],
            cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or                 "CLIENT_ENHANCEMENT_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot enhancement AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "ENHANCEMENT_FIXTURE_PASS" not in output:
            raise RuntimeError(
                "C# enhancement fixture did not confirm durable settlement")
        for line in lines:
            if line.startswith("ENHANCEMENT_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_ENHANCEMENT_E2E_PASS aes_cbc=true "
              "identity_server_bound=true probability_server_auth=true "
              "cost_server_auth=true wallet_debit_once=true "
              "enhancement_level_durable=true equipment_uid_preserved=true")
        return 0
    except Exception as error:
        print("AES_ENHANCEMENT_RUNNER_FAIL", error, file=sys.stderr)
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


def run_player_shop(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--player-shop"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_notices: queue.Queue[str] = queue.Queue()
    seller_lines: list[str] = []
    seller_notices: queue.Queue[str] = queue.Queue()
    seller = None

    def capture(pipe, storage: list[str], notices: queue.Queue[str]) -> None:
        for line in pipe:
            cleaned = line.rstrip()
            storage.append(cleaned)
            notices.put(cleaned)

    assert server.stdout is not None
    threading.Thread(
        target=capture,args=(server.stdout,server_lines,server_notices),
        daemon=True).start()

    def until(notices: queue.Queue[str], expected: str, deadline: float,
              process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and notices.empty():
                raise RuntimeError(
                    f"process exited before {expected} (code={process.returncode})")
            try:
                line=notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if expected in line:
                return line
        raise TimeoutError(f"timeout waiting for {expected}")

    deadline=time.monotonic()+args.timeout
    try:
        ready=until(server_notices,"PLAYER_SHOP_FIXTURE_READY",deadline,server)
        match=re.search(
            r"PLAYER_SHOP_FIXTURE_READY port=(\d+) seller=(\d+) "
            r"buyer=(\d+) item_uid=(\d+) drop_id=(\d+)",ready)
        if match is None:
            raise RuntimeError("malformed player-shop fixture readiness: "+ready)
        port, seller_id,buyer_id,item_uid,drop_id=match.groups()
        base=[args.godot,"--headless","--path",str(repo/"client"),
              "--script","res://tests/test_client_player_shop_aes.gd","--",
              port]
        tail=[seller_id,buyer_id,item_uid,drop_id]

        seller=subprocess.Popen(
            [*base,"seller",*tail],cwd=repo,text=True,
            stdout=subprocess.PIPE,stderr=subprocess.STDOUT,bufsize=1)
        assert seller.stdout is not None
        threading.Thread(
            target=capture,args=(seller.stdout,seller_lines,seller_notices),
            daemon=True).start()
        until(seller_notices,"CLIENT_PLAYER_SHOP_READY role=seller",deadline,seller)

        buyer=subprocess.run(
            [*base,"buyer",*tail],cwd=repo,text=True,
            stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
            timeout=max(1.0,deadline-time.monotonic()))
        if buyer.returncode!=0 or "CLIENT_PLAYER_SHOP_PASS role=buyer" not in buyer.stdout:
            raise RuntimeError("player-shop buyer failed:\n"+buyer.stdout)

        seller.wait(timeout=max(1.0,deadline-time.monotonic()))
        if seller.returncode!=0 or not any(
                "CLIENT_PLAYER_SHOP_PASS role=seller" in line
                for line in seller_lines):
            raise RuntimeError(
                "player-shop seller failed:\n"+"\n".join(seller_lines))

        server.wait(timeout=max(1.0,deadline-time.monotonic()))
        if server.returncode!=0 or not any(
                line.startswith("PLAYER_SHOP_FIXTURE_PASS")
                for line in server_lines):
            raise RuntimeError(
                "player-shop fixture failed:\n"+"\n".join(server_lines))

        print(buyer.stdout,end="")
        for line in seller_lines:
            if line.startswith("CLIENT_PLAYER_SHOP_"):
                print(line)
        for line in server_lines:
            if line.startswith("PLAYER_SHOP_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_PLAYER_SHOP_E2E_PASS "
              "aes_cbc=true concurrent_clients=2 online_only=true "
              "aoi_seller=true inspect=true atomic_uid=true "
              "quantity=2 source_drop=true durability=80 "
              "seller_wallet=100_to_400 buyer_wallet=1000_to_700 "
              "seller_sold_push=true client_price_math=false")
        return 0
    except Exception as error:
        print("AES_PLAYER_SHOP_RUNNER_FAIL",error,file=sys.stderr)
        for name,lines in (("server",server_lines),("seller",seller_lines)):
            for line in lines[-30:]:
                print(name,line,file=sys.stderr)
        return 1
    finally:
        if seller is not None and seller.poll() is None:
            seller.terminate()
            try:
                seller.wait(timeout=2)
            except subprocess.TimeoutExpired:
                seller.kill()
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()


def run_pvp(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--pvp"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_events: queue.Queue[str] = queue.Queue()
    victim_lines: list[str] = []
    victim_events: queue.Queue[str] = queue.Queue()
    victim = None
    deadline = time.monotonic() + args.timeout

    def capture(process, lines: list[str], events: queue.Queue[str]) -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value = line.rstrip()
            lines.append(value)
            events.put(value)

    def until(events: queue.Queue[str], token: str, process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"{token}: process exited {process.returncode}")
            try:
                line = events.get(timeout=0.15)
            except queue.Empty:
                continue
            if token in line:
                return line
        raise TimeoutError(token)

    threading.Thread(
        target=capture, args=(server, server_lines, server_events), daemon=True).start()
    try:
        ready = until(server_events, "PVP_FIXTURE_READY", server)
        match = re.search(
            r"PVP_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed PvP fixture readiness: " + ready)
        port, actor_a, actor_b = match.groups()
        base = [args.godot, "--headless", "--path", str(repo / "client"),
                "--script", "res://tests/test_client_pvp_aes.gd", "--",
                port]

        victim = subprocess.Popen(
            [*base, "victim", actor_a, actor_b],
            cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(
            target=capture, args=(victim, victim_lines, victim_events), daemon=True).start()
        until(victim_events, "CLIENT_PVP_READY role=victim", victim)

        actor = subprocess.run(
            [*base, "actor", actor_a, actor_b],
            cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(actor.stdout, end="")
        if actor.returncode != 0 or "CLIENT_PVP_PASS role=actor" not in actor.stdout:
            raise RuntimeError("PvP actor failed")

        victim.wait(timeout=max(1.0, deadline - time.monotonic()))
        if victim.returncode != 0 or not any(
                "CLIENT_PVP_PASS role=victim" in line for line in victim_lines):
            raise RuntimeError("PvP victim failed")

        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        if server.returncode != 0 or not any(
                line.startswith("PVP_FIXTURE_PASS") for line in server_lines):
            raise RuntimeError("PvP server fixture failed")

        for line in victim_lines:
            if line.startswith("CLIENT_PVP_"):
                print(line)
        for line in server_lines:
            if line.startswith("PVP_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_PVP_E2E_PASS "
              "aes_cbc=true concurrent_clients=2 map13=true white_opt_in=true "
              "server_range=true server_damage=true shared_hp_sequence=true "
              "normal_hit=58 player_skill_fixed_hit=42 skill_id=99003 "
              "skill_mp=2_to_1 victim_dead=true attacker_red=true "
              "gm_capability=true gm_audit=true victim_gm_blue=true "
              "client_damage_estimate=false")
        return 0
    except Exception as error:
        print("AES_PVP_RUNNER_FAIL", error, file=sys.stderr)
        for name, lines in (("server", server_lines), ("victim", victim_lines)):
            for line in lines[-30:]:
                print(name, line, file=sys.stderr)
        return 1
    finally:
        if victim is not None and victim.poll() is None:
            victim.terminate()
            try:
                victim.wait(timeout=2)
            except subprocess.TimeoutExpired:
                victim.kill()
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()


def run_combat_observer(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--combat-observer"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_events: queue.Queue[str] = queue.Queue()
    observer_lines: list[str] = []
    observer_events: queue.Queue[str] = queue.Queue()
    observer = None
    deadline = time.monotonic() + args.timeout

    def capture(process, lines: list[str], events: queue.Queue[str]) -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value = line.rstrip()
            lines.append(value)
            events.put(value)

    def until(events: queue.Queue[str], token: str, process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"{token}: process exited {process.returncode}")
            try:
                line = events.get(timeout=0.15)
            except queue.Empty:
                continue
            if token in line:
                return line
        raise TimeoutError(token)

    threading.Thread(target=capture,args=(server,server_lines,server_events),
                     daemon=True).start()
    try:
        ready=until(server_events,"COMBAT_OBSERVER_FIXTURE_READY",server)
        match=re.search(
            r"COMBAT_OBSERVER_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed combat-observer readiness: "+ready)
        port = match.group(1)
        base=[args.godot,"--headless","--path",str(repo/"client"),
              "--script","res://tests/test_client_combat_observer_aes.gd","--",
              port]
        observer=subprocess.Popen(
            [*base,"observer"],cwd=repo,text=True,stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,bufsize=1)
        threading.Thread(target=capture,args=(observer,observer_lines,observer_events),
                         daemon=True).start()
        until(observer_events,"CLIENT_COMBAT_OBSERVER_READY role=observer",observer)

        actor=subprocess.run(
            [*base,"actor"],cwd=repo,text=True,stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0,deadline-time.monotonic()))
        print(actor.stdout,end="")
        if actor.returncode!=0 or "CLIENT_COMBAT_OBSERVER_PASS role=actor" not in actor.stdout:
            raise RuntimeError("combat actor failed")

        observer.wait(timeout=max(1.0,deadline-time.monotonic()))
        if observer.returncode!=0 or not any(
            "CLIENT_COMBAT_OBSERVER_PASS role=observer" in line
            for line in observer_lines):
            raise RuntimeError("combat observer failed")

        server.wait(timeout=max(1.0,deadline-time.monotonic()))
        if server.returncode!=0 or not any(
            line.startswith("COMBAT_OBSERVER_FIXTURE_PASS")
            for line in server_lines):
            raise RuntimeError("combat observer server fixture failed")

        for line in observer_lines:
            if line.startswith("CLIENT_COMBAT_OBSERVER_"):
                print(line)
        for line in server_lines:
            if line.startswith("COMBAT_OBSERVER_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_ATTACK_VISUAL_E2E_PASS "
              "aes_cbc=true concurrent_clients=2 attacker_visual=true "
              "observer_visual=true damage=60 monster_delta_hp=40 "
              "actor_hit_visual=true observer_hit_visual=true monster_damage=18 "
              "observer_remote_hp_authority=false same_reliable_aoi=true")
        return 0
    except Exception as error:
        print("AES_COMBAT_OBSERVER_RUNNER_FAIL",error,file=sys.stderr)
        for name,lines in (("server",server_lines),("observer",observer_lines)):
            for line in lines[-30:]:
                print(name,line,file=sys.stderr)
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



def run_skill_aoe_observer(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server=subprocess.Popen(
        ["dotnet","run","--project",str(fixture),"-c","Release",
         "--no-build","--","--skill-aoe-observer"],
        cwd=repo,env=env,text=True,stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,bufsize=1)
    server_lines: list[str]=[]
    server_events: queue.Queue[str]=queue.Queue()
    observer_lines: list[str]=[]
    observer_events: queue.Queue[str]=queue.Queue()
    observer=None
    deadline=time.monotonic()+args.timeout

    def capture(process, lines: list[str], events: queue.Queue[str]) -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value=line.rstrip()
            lines.append(value)
            events.put(value)

    def until(events: queue.Queue[str], token: str, process: subprocess.Popen) -> str:
        while time.monotonic()<deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"{token}: process exited {process.returncode}")
            try:
                line=events.get(timeout=0.15)
            except queue.Empty:
                continue
            if token in line:
                return line
        raise TimeoutError(token)

    threading.Thread(target=capture,args=(server,server_lines,server_events),
                     daemon=True).start()
    try:
        ready=until(server_events,"SKILL_AOE_OBSERVER_FIXTURE_READY",server)
        match=re.search(
            r"SKILL_AOE_OBSERVER_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed skill AoE observer readiness: "+ready)
        port = match.group(1)
        base=[args.godot,"--headless","--path",str(repo/"client"),
              "--script","res://tests/test_client_skill_aoe_observer_aes.gd",
              "--",port]
        observer=subprocess.Popen(
            [*base,"observer"],cwd=repo,text=True,stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,bufsize=1)
        threading.Thread(target=capture,args=(observer,observer_lines,observer_events),
                         daemon=True).start()
        until(observer_events,"CLIENT_SKILL_AOE_OBSERVER_READY role=observer",observer)

        actor=subprocess.run(
            [*base,"actor"],cwd=repo,text=True,stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0,deadline-time.monotonic()))
        print(actor.stdout,end="")
        if actor.returncode!=0 or "CLIENT_SKILL_AOE_OBSERVER_PASS role=actor" not in actor.stdout:
            raise RuntimeError("skill AoE actor failed")

        observer.wait(timeout=max(1.0,deadline-time.monotonic()))
        if observer.returncode!=0 or not any(
            "CLIENT_SKILL_AOE_OBSERVER_PASS role=observer" in line
            for line in observer_lines):
            raise RuntimeError("skill AoE observer failed")

        server.wait(timeout=max(1.0,deadline-time.monotonic()))
        if server.returncode!=0 or not any(
            line.startswith("SKILL_AOE_OBSERVER_FIXTURE_PASS")
            for line in server_lines):
            raise RuntimeError("skill AoE observer server fixture failed")

        for line in observer_lines:
            if line.startswith("CLIENT_SKILL_AOE_OBSERVER_"):
                print(line)
        for line in server_lines:
            if line.startswith("SKILL_AOE_OBSERVER_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_SKILL_AOE_E2E_PASS "
              "aes_cbc=true concurrent_clients=2 target_zero=true hits=2 "
              "same_sequence_multi_target=true observer_target_memory=2 "
              "mp_owner_actor_only=true monster_delta_authority=true")
        return 0
    except Exception as error:
        print("AES_SKILL_AOE_OBSERVER_RUNNER_FAIL",error,file=sys.stderr)
        for name,lines in (("server",server_lines),("observer",observer_lines)):
            for line in lines[-30:]:
                print(name,line,file=sys.stderr)
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


def run_skill_aoe(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet","run","--project",str(fixture),"-c","Release",
         "--no-build","--","--skill-aoe"],
        cwd=repo,env=env,text=True,stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,bufsize=1)
    lines: list[str]=[]
    notices: queue.Queue[str]=queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value=line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture,daemon=True).start()
    deadline=time.monotonic()+args.timeout
    try:
        ready=None
        while time.monotonic()<deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line=notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("SKILL_AOE_FIXTURE_READY "):
                ready=line
                break
        if ready is None:
            raise RuntimeError("skill AoE fixture never became ready")
        match=re.search(r"SKILL_AOE_FIXTURE_READY port=(\d+) username=",ready)
        if match is None:
            raise RuntimeError("malformed skill AoE readiness: "+ready)
        port = match.group(1)
        client=subprocess.run(
            [args.godot,"--headless","--path",str(repo/"client"),
             "--script","res://tests/test_client_skill_aoe_aes.gd","--",port],
            cwd=repo,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
            timeout=max(1.0,deadline-time.monotonic()))
        print(client.stdout,end="")
        if client.returncode!=0 or "CLIENT_SKILL_AOE_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot skill AoE AES test failed")
        server.wait(timeout=max(1.0,deadline-time.monotonic()))
        output="\n".join(lines)
        if server.returncode!=0 or "SKILL_AOE_FIXTURE_PASS" not in output:
            raise RuntimeError("C# skill AoE fixture did not confirm settlement")
        for line in lines:
            if line.startswith("SKILL_AOE_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_SKILL_AOE_E2E_PASS aes_cbc=true target_zero=true hits=2 mp_debit_once=true third_target_untouched=true per_target_visual=true monster_delta_authority=true")
        return 0
    except Exception as error:
        print("AES_SKILL_AOE_RUNNER_FAIL",error,file=sys.stderr)
        for line in lines[-30:]:
            print(line,file=sys.stderr)
        return 1
    finally:
        if server.poll() is None:
            server.terminate()
            try:
                server.wait(timeout=3)
            except subprocess.TimeoutExpired:
                server.kill()


def run_target_combat(args, repo: Path, fixture: Path, env: dict[str, str],
                      force_miss: bool = False) -> int:
    fixture_mode = "--target-combat-miss" if force_miss else "--target-combat"
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", fixture_mode],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("TARGET_COMBAT_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("target-combat fixture never became ready")
        match = re.search(r"TARGET_COMBAT_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed target-combat fixture readiness: " + ready)
        port = match.group(1)
        client_args = [
            args.godot, "--headless", "--path", str(repo / "client"),
            "--script", "res://tests/test_client_target_combat_aes.gd", "--", port]
        if force_miss:
            client_args.append("miss")
        client = subprocess.run(
            client_args,
            cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_TARGET_COMBAT_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot target-combat AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "TARGET_COMBAT_FIXTURE_PASS" not in output:
            raise RuntimeError("C# target-combat fixture did not confirm authority settlement")
        for line in lines:
            if line.startswith("TARGET_COMBAT_FIXTURE_"):
                print(line)
        if force_miss:
            print("GODOT_CSHARP_AES_TARGET_COMBAT_MISS_E2E_PASS "
                  "aes_cbc=true v7_retaliation_chase=true trusted_input=true "
                  "authoritative_damage=40 authoritative_monster_hp=60 "
                  "retaliation=MISS authoritative_player_hp=100 durable_hp_unchanged=true "
                  "player_damage_visual=true world_view_miss=true original_miss_overlay=true "
                  "chase=true proactive_aggro=false")
        else:
            print("GODOT_CSHARP_AES_TARGET_COMBAT_E2E_PASS "
                  "aes_cbc=true v7_retaliation_chase=true experience_manifest=false "
                  "trusted_input=true authoritative_damage=40 authoritative_monster_hp=60 "
                  "retaliation_damage=8 authoritative_player_hp=92 player_damage_visual=true "
                  "monster_delta=true own_attack_visual=true ai=false chase=true "
                  "proactive_aggro=false")
        return 0
    except Exception as error:
        print("AES_TARGET_COMBAT_RUNNER_FAIL", error, file=sys.stderr)
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


def run_combat(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--combat"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                line = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if line.startswith("COMBAT_FIXTURE_READY "):
                ready = line
                break
        if ready is None:
            raise RuntimeError("combat fixture never became ready")
        match = re.search(r"COMBAT_FIXTURE_READY port=(\d+) username=", ready)
        if match is None:
            raise RuntimeError("malformed combat fixture readiness: " + ready)
        port = match.group(1)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_combat_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_COMBAT_AES_PASS" not in client.stdout:
            raise RuntimeError("Godot combat AES test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "COMBAT_FIXTURE_PASS" not in output:
            raise RuntimeError("C# combat fixture did not confirm authority settlement")
        for line in lines:
            if line.startswith("COMBAT_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_AES_COMBAT_E2E_PASS aes_cbc=true synthetic_profile_explicit=true client_damage_absent=true authoritative_monster_hp=true shared_monster_delta=true monster_hit_committed_before_player_damage=true authoritative_player_hp=true lethal_world_stop_before_delivery=true dead_intents_blocked=true")
        return 0
    except Exception as error:
        print("AES_COMBAT_RUNNER_FAIL", error, file=sys.stderr)
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


def run_reconnect(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--reconnect"],
        cwd=repo, env=env, text=True, stdin=subprocess.PIPE,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
    server_lines: list[str] = []
    server_events: queue.Queue[str] = queue.Queue()
    client_lines: list[str] = []
    client_events: queue.Queue[str] = queue.Queue()
    client = None
    deadline = time.monotonic() + args.timeout

    def capture(process, lines: list[str], events: queue.Queue[str]) -> None:
        assert process.stdout is not None
        for line in process.stdout:
            value = line.rstrip()
            lines.append(value)
            events.put(value)

    def until(events: queue.Queue[str], token: str, process: subprocess.Popen) -> str:
        while time.monotonic() < deadline:
            if process.poll() is not None and events.empty():
                raise RuntimeError(f"{token}: process exited {process.returncode}")
            try:
                line = events.get(timeout=0.15)
            except queue.Empty:
                continue
            if token in line:
                return line
        raise TimeoutError(token)

    threading.Thread(target=capture, args=(server, server_lines, server_events),
                     daemon=True).start()
    try:
        ready = until(server_events, "RECONNECT_FIXTURE_READY", server)
        match = re.search(
            r"RECONNECT_FIXTURE_READY port=(\d+) username=.*? character=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed reconnect readiness: " + ready)
        port = match.group(1)
        client = subprocess.Popen(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_reconnect_aes.gd", "--", port],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            bufsize=1)
        threading.Thread(target=capture, args=(client, client_lines, client_events),
                         daemon=True).start()

        until(client_events, "CLIENT_RECONNECT_FIRST_MOVE_DONE", client)
        assert server.stdin is not None
        server.stdin.write("DROP\n")
        server.stdin.flush()
        until(server_events, "RECONNECT_FIXTURE_DROPPED", server)
        until(client_events, "CLIENT_RECONNECT_PASS", client)

        client.wait(timeout=max(1.0, deadline - time.monotonic()))
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        if client.returncode != 0 or server.returncode != 0 or not any(
                line.startswith("RECONNECT_FIXTURE_PASS") for line in server_lines):
            raise RuntimeError("reconnect fixture/client failed")

        for lines in (client_lines, server_lines):
            for line in lines:
                if line.startswith(("CLIENT_RECONNECT_", "RECONNECT_FIXTURE_")):
                    print(line)
        print("GODOT_CSHARP_AES_RECONNECT_E2E_PASS forced_drop=true auto_reauth=true "
              "same_character=true new_generation=true persisted_position=true "
              "world_ready_reack=true old_intents_not_replayed=true "
              "chat_generation_reset=true bounded_attempts=3")
        return 0
    except Exception as error:
        print("AES_RECONNECT_RUNNER_FAIL", error, file=sys.stderr)
        for name, lines in (("server", server_lines), ("client", client_lines)):
            for line in lines[-25:]:
                print(name, line, file=sys.stderr)
        return 1
    finally:
        for process in (client, server):
            if process is None or process.poll() is not None:
                continue
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()


def run_trade(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--trade"],
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
        ready = until(server_notices, "TRADE_FIXTURE_READY", deadline, server)
        match = re.search(
            r"TRADE_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+) item_a=(\d+) item_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed trade fixture readiness: " + ready)
        port, actor_a, actor_b, item_a, item_b = match.groups()
        base = [args.godot, "--headless", "--path", str(repo / "client"),
                "--script", "res://tests/test_client_trade_aes.gd", "--",
                port]
        tail = [actor_a, actor_b, item_a, item_b]
        observer = subprocess.Popen(
            [*base, "observer", *tail], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=capture, args=(observer.stdout, observer_lines,
                                               observer_notices), daemon=True).start()
        until(observer_notices, "CLIENT_TRADE_READY role=observer", deadline, observer)

        actor = subprocess.run(
            [*base, "actor", *tail], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline-time.monotonic()))
        if actor.returncode != 0 or "CLIENT_TRADE_PASS role=actor" not in actor.stdout:
            raise RuntimeError("active trade client failed:\n" + actor.stdout)
        observer.wait(timeout=max(1.0, deadline-time.monotonic()))
        if observer.returncode != 0 or not any(
                "CLIENT_TRADE_PASS role=observer" in line for line in observer_lines):
            raise RuntimeError("observer trade client failed:\n" + "\n".join(observer_lines))
        server.wait(timeout=max(1.0, deadline-time.monotonic()))
        if server.returncode != 0 or not any(
                line.startswith("TRADE_FIXTURE_PASS") for line in server_lines):
            raise RuntimeError("trade fixture failed:\n" + "\n".join(server_lines))

        print(actor.stdout, end="")
        for line in observer_lines:
            if line.startswith("CLIENT_TRADE_"):
                print(line)
        for line in server_lines:
            if line.startswith("TRADE_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TWO_CLIENT_TRADE_E2E_PASS aes_cbc=true concurrent_clients=2 "
              "invite=true offers=true money=true dual_lock=true dual_confirm=true "
              "sqlite_atomic_swap=true uid_provenance=true")
        return 0
    except Exception as error:
        print("AES_TRADE_RUNNER_FAIL", error, file=sys.stderr)
        for line in server_lines[-25:]:
            print(line, file=sys.stderr)
        for line in observer_lines[-25:]:
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


def run_multiplayer(args, repo: Path, fixture: Path, env: dict[str, str],
                    portal: bool = False, reconnect: bool = False) -> int:
    """Real AES AOI with optional audited portal or same-character reconnect lifecycle."""
    label = "MULTI_RECONNECT" if reconnect else "PORTAL" if portal else "MULTI"
    fixture_mode = "--multi-reconnect" if reconnect else "--portal" if portal else "--multi"
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", fixture_mode],
        cwd=repo, env=env, text=True, encoding="utf-8", errors="replace",
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
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
        ready = until(server_notices, label + "_FIXTURE_READY", deadline, server)
        match = re.search(label + r"_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+)", ready)
        if match is None:
            raise RuntimeError(f"malformed multiplayer fixture readiness {ready}")
        port = match.group(1)
        base = [args.godot, "--headless", "--path", str(repo / "client"),
                "--script", "res://tests/test_client_multiplayer_aes.gd", "--",
                port]

        extra = ["reconnect"] if reconnect else ["portal"] if portal else []
        observer = subprocess.Popen(
            [*base, "observer", *extra], cwd=repo, text=True,
            encoding="utf-8", errors="replace", stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=capture, args=(observer.stdout, observer_lines,
                                               observer_notices), daemon=True).start()
        until(observer_notices, "CLIENT_" + label + "_READY role=observer", deadline, observer)

        remaining = max(1.0, deadline - time.monotonic())
        actor = subprocess.run([*base, "actor", *extra], cwd=repo, text=True,
                               encoding="utf-8", errors="replace",
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               timeout=remaining)
        actor_expected = "CLIENT_MULTI_PASS role=actor" if reconnect else "CLIENT_" + label + "_PASS role=actor"
        if actor.returncode != 0 or actor_expected not in actor.stdout:
            raise RuntimeError("active Godot client failed:\n" + actor.stdout)

        rejoin = None
        if reconnect:
            remaining = max(1.0, deadline - time.monotonic())
            rejoin = subprocess.run([*base, "rejoin", "reconnect"], cwd=repo,
                                    text=True, encoding="utf-8", errors="replace",
                                    stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                    timeout=remaining)
            if rejoin.returncode != 0 or "CLIENT_MULTI_RECONNECT_PASS role=rejoin" not in rejoin.stdout:
                raise RuntimeError("rejoined Godot client failed:\n" + rejoin.stdout)

        observer.wait(timeout=max(1.0, deadline - time.monotonic()))
        if observer.returncode != 0 or not any(
                "CLIENT_" + label + "_PASS role=observer" in line for line in observer_lines):
            raise RuntimeError("observer Godot client failed:\n" +
                               "\n".join(observer_lines))

        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        if server.returncode != 0 or not any(
                line.startswith(label + "_FIXTURE_PASS") for line in server_lines):
            raise RuntimeError("shared World fixture failed:\n" +
                               "\n".join(server_lines))

        print(actor.stdout, end="")
        if reconnect and rejoin is not None:
            print(rejoin.stdout, end="")
        for line in observer_lines:
            if line.startswith("CLIENT_" + label + "_"):
                print(line)
        for line in server_lines:
            if line.startswith(label + "_FIXTURE_"):
                print(line)
        if reconnect:
            print("GODOT_CSHARP_THREE_SESSION_AOI_RECONNECT_E2E_PASS "
                  "aes_cbc=true observer_alive=true actor_disconnect=true "
                  "despawn=true same_actor_rejoin=true new_connection_epoch=true "
                  "persisted_position=true post_rejoin_delta=true zero_ghost_actors=true")
        elif portal:
            print("GODOT_CSHARP_TWO_CLIENT_NPC_PORTAL_E2E_PASS "
                  "aes_cbc=true concurrent_clients=2 audited_clicks=true "
                  "unknown_npc_denied=true map15_to_map13_to_map15=true "
                  "world_ready=true delta=true despawn=true return_spawn=true "
                  "zero_ghost_actors=true")
        else:
            print("GODOT_CSHARP_TWO_CLIENT_AOI_E2E_PASS "
                  "aes_cbc=true concurrent_clients=2 world_ready=true "
                  "spawn=true authoritative_delta=true despawn=true "
                  "same_30hz_world=true")
        return 0
    except Exception as error:
        print("AES_MULTI_RECONNECT_RUNNER_FAIL" if reconnect else
              "AES_NPC_PORTAL_RUNNER_FAIL" if portal else
              "AES_MULTIPLAYER_RUNNER_FAIL", error, file=sys.stderr)
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



def run_quest(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--quest"],
        cwd=repo, env=env, text=True, stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT, bufsize=1)
    lines: list[str] = []
    notices: queue.Queue[str] = queue.Queue()

    def capture() -> None:
        assert server.stdout is not None
        for line in server.stdout:
            value = line.rstrip()
            lines.append(value)
            notices.put(value)

    threading.Thread(target=capture, daemon=True).start()
    deadline = time.monotonic() + args.timeout
    try:
        ready = None
        while time.monotonic() < deadline:
            if server.poll() is not None and notices.empty():
                break
            try:
                value = notices.get(timeout=0.15)
            except queue.Empty:
                continue
            if value.startswith("QUEST_FIXTURE_READY "):
                ready = value
                break
        if ready is None:
            raise RuntimeError("quest fixture never became ready")
        match = re.search(
            r"QUEST_FIXTURE_READY port=(\d+) username=.*? "
            r"character=(\d+) npc=(\d+) quest=(\d+)", ready)
        if match is None:
            raise RuntimeError("malformed quest readiness: " + ready)
        port, character, npc, quest = match.groups()
        base = [
            args.godot, "--headless", "--path", str(repo / "client"),
            "--script", "res://tests/test_client_quest_aes.gd", "--",
            port]
        tail = [character, npc, quest]

        mutate = subprocess.run(
            [*base, "mutate", *tail], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline-time.monotonic()))
        print(mutate.stdout, end="")
        if mutate.returncode != 0 or "CLIENT_QUEST_AES_PASS role=mutate" not in mutate.stdout:
            raise RuntimeError("quest mutate Godot client failed")

        relogin = subprocess.run(
            [*base, "relogin", *tail], cwd=repo, text=True,
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline-time.monotonic()))
        print(relogin.stdout, end="")
        if relogin.returncode != 0 or "CLIENT_QUEST_AES_PASS role=relogin" not in relogin.stdout:
            raise RuntimeError("quest relogin Godot client failed")

        server.wait(timeout=max(1.0, deadline-time.monotonic()))
        output = "\n".join(lines)
        if (
            server.returncode != 0 or
            "QUEST_FIXTURE_MUTATE_PASS" not in output or
            "QUEST_FIXTURE_RELOGIN_PASS" not in output or
            "QUEST_FIXTURE_PASS" not in output
        ):
            raise RuntimeError("quest C# fixture did not confirm durable progression")
        for line in lines:
            if line.startswith("QUEST_FIXTURE_"):
                print(line)
        print(
            "GODOT_CSHARP_AES_QUEST_E2E_PASS aes_cbc=true "
            "verified_rule=true npc_distance_server_auth=true "
            "choice_ids_server_view=true durable_progress=true "
            "relogin_completed=true client_stage_math=false")
        return 0
    except Exception as error:
        print("AES_QUEST_RUNNER_FAIL", error, file=sys.stderr)
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


def run_crossmap(args, repo: Path, fixture: Path, env: dict[str, str]) -> int:
    """Two actual Godot processes on one AES World; test-only trusted transfer signals.

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
            r"CROSSMAP_FIXTURE_READY port=(\d+) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("invalid cross-map fixture readiness: " + ready)
        port = match.group(1)
        command = [args.godot, "--headless", "--path", str(repo / "client"),
                   "--script", "res://tests/test_client_multiplayer_aes.gd", "--",
                   port]
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
              "aes_cbc=true map15_to_map13_to_map15=true "
              "world_ready=true old_map_clear=true "
              "despawn=true return_spawn=true "
              "nearby_chat_isolation=true "
              "party_membership_crossmap=true party_chat_crossmap=true "
              "whisper_crossmap=true target_identity_server_auth=true "
              "whisper_unavailable=true no_optimistic_echo=true "
              "same_authoritative_30hz_world=true")
        return 0
    except Exception as error:
        print("AES_CROSSMAP_RUNNER_FAIL", error, file=sys.stderr)
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
