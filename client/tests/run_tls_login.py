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
    parser.add_argument("--app-ui", action="store_true",
                        help="exercise real main-scene login/choose/logout UI over TLS (no private textures)")
    parser.add_argument("--ui-screenshot", type=Path,
                        help="with --app-ui, render the UI natively and save a private screenshot")
    parser.add_argument("--multi", action="store_true",
                        help="run two concurrent real Godot TLS clients against the same 30Hz World")
    parser.add_argument("--crossmap", action="store_true",
                        help="run two-client Map15 -> Map13 -> Map15 trusted transfer TLS E2E")
    parser.add_argument("--portal", action="store_true",
                        help="run two-client audited NPC interaction TLS transfer E2E")
    parser.add_argument("--reconnect", action="store_true",
                        help="force one TLS socket drop and require automatic same-character reconnect")
    parser.add_argument("--combat", action="store_true",
                        help="run real TLS ClientSession normal-attack authority E2E")
    parser.add_argument("--combat-observer", action="store_true",
                        help="run two real TLS clients and verify remote player attack visuals")
    parser.add_argument("--skill-aoe", action="store_true",
                        help="run real TLS self-centered AoE skill against three monsters")
    parser.add_argument("--skill-aoe-observer", action="store_true",
                        help="run two real TLS clients and verify multi-target AoE visuals")
    parser.add_argument("--auto-attack", action="store_true",
                        help="run real TLS out-of-range attack -> Server A* approach -> retry E2E")
    parser.add_argument("--discard", action="store_true",
                        help="run real TLS ClientSession durable inventory discard E2E")
    parser.add_argument("--storage", action="store_true",
                        help="run real TLS ClientSession personal storage deposit/withdraw E2E")
    parser.add_argument("--equipment", action="store_true",
                        help="run real TLS ClientSession durable equipment equip/unequip E2E")
    parser.add_argument("--bank", action="store_true",
                        help="run real TLS ClientSession scoped bank deposit/withdraw E2E")
    args = parser.parse_args()
    if args.app_ui and (args.multi or args.crossmap or args.portal or args.reconnect or args.combat or args.combat_observer or args.skill_aoe or args.skill_aoe_observer or args.discard or args.storage or args.equipment or args.bank or args.auto_attack):
        parser.error("--app-ui uses the single-client login fixture")
    if args.ui_screenshot and not args.app_ui:
        parser.error("--ui-screenshot requires --app-ui")
    repo = Path(__file__).resolve().parents[2]
    fixture = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
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
    if args.storage:
        return run_storage(args, repo, fixture, env)
    if args.equipment:
        return run_equipment(args, repo, fixture, env)
    if args.bank:
        return run_bank(args, repo, fixture, env)
    if args.combat:
        return run_combat(args, repo, fixture, env)
    if args.reconnect:
        return run_reconnect(args, repo, fixture, env)
    if args.crossmap:
        return run_crossmap(args, repo, fixture, env)
    if args.portal:
        return run_multiplayer(args, repo, fixture, env, portal=True)
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
            [args.godot, *([] if args.ui_screenshot else ["--headless"]), "--path", str(repo / "client"),
             "--script", "res://tests/test_session_ui_tls.gd" if args.app_ui else "res://tests/test_client_transport_tls.gd", "--", port, cert,
             *([str(args.ui_screenshot.resolve())] if args.ui_screenshot else [])],
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
        match = re.search(r"AUTO_ATTACK_FIXTURE_READY port=(\d+) cert=(.+?) username=", ready)
        if match is None:
            raise RuntimeError("malformed auto-attack fixture readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_auto_approach_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_AUTO_APPROACH_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot auto-approach TLS test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "AUTO_ATTACK_FIXTURE_PASS" not in output:
            raise RuntimeError("C# auto-attack fixture did not confirm A* approach settlement")
        for line in lines:
            if line.startswith("AUTO_ATTACK_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_AUTO_APPROACH_ATTACK_E2E_PASS secure_tls=true first_attack_out_of_range=true server_astar_move=true self_state_stop=true second_attack_applied=true client_range_guess=false")
        return 0
    except Exception as error:
        print("TLS_AUTO_ATTACK_RUNNER_FAIL", error, file=sys.stderr)
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
        match = re.search(r"DISCARD_FIXTURE_READY port=(\d+) cert=(.+?) username=", ready)
        if match is None:
            raise RuntimeError("malformed discard fixture readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_discard_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_DISCARD_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot discard TLS test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "DISCARD_FIXTURE_PASS" not in output:
            raise RuntimeError("C# discard fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("DISCARD_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_DISCARD_E2E_PASS secure_tls=true durable_inventory=true request_identity_server_bound=true receipt_before_local_mutation=true final_delete_persisted=true")
        return 0
    except Exception as error:
        print("TLS_DISCARD_RUNNER_FAIL", error, file=sys.stderr)
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
        match = re.search(r"STORAGE_FIXTURE_READY port=(\d+) cert=(.+?) username=", ready)
        if match is None:
            raise RuntimeError("malformed storage fixture readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_storage_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_STORAGE_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot storage TLS test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "STORAGE_FIXTURE_PASS" not in output:
            raise RuntimeError("C# storage fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("STORAGE_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_STORAGE_E2E_PASS secure_tls=true "
              "identity_server_bound=true whole_uid_stack=true "
              "deposit=true withdraw=true durable_uid_and_provenance=true")
        return 0
    except Exception as error:
        print("TLS_STORAGE_RUNNER_FAIL", error, file=sys.stderr)
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
        match = re.search(r"EQUIPMENT_FIXTURE_READY port=(\d+) cert=(.+?) username=", ready)
        if match is None:
            raise RuntimeError("malformed equipment fixture readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_equipment_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_EQUIPMENT_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot equipment TLS test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "EQUIPMENT_FIXTURE_PASS" not in output:
            raise RuntimeError("C# equipment fixture did not confirm persistent settlement")
        for line in lines:
            if line.startswith("EQUIPMENT_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_EQUIPMENT_E2E_PASS secure_tls=true "
              "identity_server_bound=true slot_server_rule=true "
              "equip=true unequip=true durable_uid_and_provenance=true")
        return 0
    except Exception as error:
        print("TLS_EQUIPMENT_RUNNER_FAIL", error, file=sys.stderr)
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
            r"COMBAT_OBSERVER_FIXTURE_READY port=(\d+) cert=(.+?) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed combat-observer readiness: "+ready)
        port,cert=match.group(1),match.group(2)
        base=[args.godot,"--headless","--path",str(repo/"client"),
              "--script","res://tests/test_client_combat_observer_tls.gd","--",
              port,cert]
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
              "tls=true concurrent_clients=2 attacker_visual=true "
              "observer_visual=true damage=60 monster_delta_hp=40 "
              "actor_hit_visual=true observer_hit_visual=true monster_damage=18 "
              "observer_remote_hp_authority=false same_reliable_aoi=true")
        return 0
    except Exception as error:
        print("TLS_COMBAT_OBSERVER_RUNNER_FAIL",error,file=sys.stderr)
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
            r"SKILL_AOE_OBSERVER_FIXTURE_READY port=(\d+) cert=(.+?) actor_a=(\d+) actor_b=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed skill AoE observer readiness: "+ready)
        port,cert=match.group(1),match.group(2)
        base=[args.godot,"--headless","--path",str(repo/"client"),
              "--script","res://tests/test_client_skill_aoe_observer_tls.gd",
              "--",port,cert]
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
              "tls=true concurrent_clients=2 target_zero=true hits=2 "
              "same_sequence_multi_target=true observer_target_memory=2 "
              "mp_owner_actor_only=true monster_delta_authority=true")
        return 0
    except Exception as error:
        print("TLS_SKILL_AOE_OBSERVER_RUNNER_FAIL",error,file=sys.stderr)
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
        match=re.search(r"SKILL_AOE_FIXTURE_READY port=(\d+) cert=(.+?) username=",ready)
        if match is None:
            raise RuntimeError("malformed skill AoE readiness: "+ready)
        port,cert=match.group(1),match.group(2)
        client=subprocess.run(
            [args.godot,"--headless","--path",str(repo/"client"),
             "--script","res://tests/test_client_skill_aoe_tls.gd","--",port,cert],
            cwd=repo,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
            timeout=max(1.0,deadline-time.monotonic()))
        print(client.stdout,end="")
        if client.returncode!=0 or "CLIENT_SKILL_AOE_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot skill AoE TLS test failed")
        server.wait(timeout=max(1.0,deadline-time.monotonic()))
        output="\n".join(lines)
        if server.returncode!=0 or "SKILL_AOE_FIXTURE_PASS" not in output:
            raise RuntimeError("C# skill AoE fixture did not confirm settlement")
        for line in lines:
            if line.startswith("SKILL_AOE_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_SKILL_AOE_E2E_PASS secure_tls=true target_zero=true hits=2 mp_debit_once=true third_target_untouched=true per_target_visual=true monster_delta_authority=true")
        return 0
    except Exception as error:
        print("TLS_SKILL_AOE_RUNNER_FAIL",error,file=sys.stderr)
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
        match = re.search(r"COMBAT_FIXTURE_READY port=(\d+) cert=(.+?) username=", ready)
        if match is None:
            raise RuntimeError("malformed combat fixture readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.run(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_combat_tls.gd", "--", port, cert],
            cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            timeout=max(1.0, deadline - time.monotonic()))
        print(client.stdout, end="")
        if client.returncode != 0 or "CLIENT_COMBAT_TLS_PASS" not in client.stdout:
            raise RuntimeError("Godot combat TLS test failed")
        server.wait(timeout=max(1.0, deadline - time.monotonic()))
        output = "\n".join(lines)
        if server.returncode != 0 or "COMBAT_FIXTURE_PASS" not in output:
            raise RuntimeError("C# combat fixture did not confirm authority settlement")
        for line in lines:
            if line.startswith("COMBAT_FIXTURE_"):
                print(line)
        print("GODOT_CSHARP_TLS_COMBAT_E2E_PASS secure_tls=true synthetic_profile_explicit=true client_damage_absent=true authoritative_monster_hp=true shared_monster_delta=true monster_hit_committed_before_player_damage=true authoritative_player_hp=true lethal_world_stop_before_delivery=true dead_intents_blocked=true")
        return 0
    except Exception as error:
        print("TLS_COMBAT_RUNNER_FAIL", error, file=sys.stderr)
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
            r"RECONNECT_FIXTURE_READY port=(\d+) cert=(.+?) username=.*? character=(\d+)",
            ready)
        if match is None:
            raise RuntimeError("malformed reconnect readiness: " + ready)
        port, cert = match.group(1), match.group(2)
        client = subprocess.Popen(
            [args.godot, "--headless", "--path", str(repo / "client"),
             "--script", "res://tests/test_client_reconnect_tls.gd", "--", port, cert],
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
        print("GODOT_CSHARP_TLS_RECONNECT_E2E_PASS forced_drop=true auto_reauth=true "
              "same_character=true new_generation=true persisted_position=true "
              "world_ready_reack=true old_intents_not_replayed=true bounded_attempts=3")
        return 0
    except Exception as error:
        print("TLS_RECONNECT_RUNNER_FAIL", error, file=sys.stderr)
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


def run_multiplayer(args, repo: Path, fixture: Path, env: dict[str, str],
                    portal: bool = False) -> int:
    """Real TLS AOI with optional audited NPC action, no test-only teleport RPC."""
    label = "PORTAL" if portal else "MULTI"
    server = subprocess.Popen(
        ["dotnet", "run", "--project", str(fixture), "-c", "Release",
         "--no-build", "--", "--portal" if portal else "--multi"],
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
        ready = until(server_notices, label + "_FIXTURE_READY", deadline, server)
        match = re.search(label + r"_FIXTURE_READY port=(\d+) cert=(.+?) actor_a=(\d+) actor_b=(\d+)", ready)
        if match is None:
            raise RuntimeError(f"malformed multiplayer fixture readiness {ready}")
        port, cert = match.group(1), match.group(2)
        base = [args.godot, "--headless", "--path", str(repo / "client"),
                "--script", "res://tests/test_client_multiplayer_tls.gd", "--",
                port, cert]

        extra = ["portal"] if portal else []
        observer = subprocess.Popen(
            [*base, "observer", *extra], cwd=repo, text=True, stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT, bufsize=1)
        threading.Thread(target=capture, args=(observer.stdout, observer_lines,
                                               observer_notices), daemon=True).start()
        until(observer_notices, "CLIENT_" + label + "_READY role=observer", deadline, observer)

        remaining = max(1.0, deadline - time.monotonic())
        actor = subprocess.run([*base, "actor", *extra], cwd=repo, text=True,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               timeout=remaining)
        if actor.returncode != 0 or "CLIENT_" + label + "_PASS role=actor" not in actor.stdout:
            raise RuntimeError("active Godot client failed:\n" + actor.stdout)

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
        for line in observer_lines:
            if line.startswith("CLIENT_" + label + "_"):
                print(line)
        for line in server_lines:
            if line.startswith(label + "_FIXTURE_"):
                print(line)
        if portal:
            print("GODOT_CSHARP_TWO_CLIENT_NPC_PORTAL_E2E_PASS "
                  "tls=true concurrent_clients=2 audited_clicks=true "
                  "unknown_npc_denied=true map15_to_map13_to_map15=true "
                  "world_ready=true delta=true despawn=true return_spawn=true "
                  "zero_ghost_actors=true")
        else:
            print("GODOT_CSHARP_TWO_CLIENT_AOI_E2E_PASS "
                  "tls=true concurrent_clients=2 world_ready=true "
                  "spawn=true authoritative_delta=true despawn=true "
                  "same_30hz_world=true")
        return 0
    except Exception as error:
        print("TLS_NPC_PORTAL_RUNNER_FAIL" if portal else
              "TLS_MULTIPLAYER_RUNNER_FAIL", error, file=sys.stderr)
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
