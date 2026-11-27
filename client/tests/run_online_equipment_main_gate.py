from __future__ import annotations

from pathlib import Path
import argparse
import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
import time

ACTOR_PASS = "ONLINE_EQUIPMENT_MAIN_GATE_ACTOR_PASS"
OBSERVER_PASS = "ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_PASS"


def run_checked(command: list[str], cwd: Path, env: dict[str, str], timeout: float) -> str:
    result = subprocess.run(
        command, cwd=cwd, env=env, text=True, encoding="utf-8", errors="replace",
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError("command failed: " + " ".join(command[:6]) + "\n" + result.stdout)
    return result.stdout


def capture(process: subprocess.Popen[str], lines: list[str], notices: queue.Queue[str]) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        clean = line.rstrip()
        lines.append(clean)
        notices.put(clean)
        if clean.startswith("ONLINE_EQUIPMENT_MAIN_GATE_"):
            print(clean, flush=True)


def wait_for(
    notices: queue.Queue[str], token: str, process: subprocess.Popen[str], deadline: float
) -> str:
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


def write_json(path: Path, payload: dict) -> None:
    path.write_text(
        json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Formal two-client character/equipment/dynamic paper-doll vertical gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=120.0)
    parser.add_argument("--native", action="store_true")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    fixture_project = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    appearance_builder = repo / "tools/ui_art/build_recovered2_character_rules.py"
    recovered2 = Path(r"C:\SGO\NewVersion\Recovered2")
    items_root = recovered2 / "Items"
    hk_item_itd = Path(r"C:\SGO\Stargate Online IE HK2\Item\item.itd")
    old_weapon_itd = Path(r"C:\SGO\Recovered\Old\服務端\Dat\Item\Weapon.itd")
    for required in (items_root / "equip/18501.tres", hk_item_itd, old_weapon_itd):
        if not required.is_file():
            parser.error(f"required source evidence missing: {required}")

    env = os.environ.copy()
    env["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    env["SGTEST_AES_KEY_HEX"] = (
        "000102030405060708090a0b0c0d0e0f"
        "101112131415161718191a1b1c1d1e1f")
    env["SGTEST_AES_IV_HEX"] = "000102030405060708090a0b0c0d0e0f"

    processes: list[subprocess.Popen[str]] = []
    logs: dict[str, list[str]] = {}
    try:
        with tempfile.TemporaryDirectory(
            prefix="sgtest-online-equipment-main-", ignore_cleanup_errors=True
        ) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            content = temp / "content"
            pack = temp / "assets.pck"
            database = temp / "identity.db"
            appearance_rules = temp / "character-appearance.json"
            equipment_rules = temp / "equipment-rules.json"
            equipment_combat_rules = temp / "equipment-combat-rules.json"
            host_dir = temp / "host-bin"
            fixture_dir = temp / "fixture-bin"
            host_dll = host_dir / "SGTest.Server.Host.dll"
            fixture_dll = fixture_dir / "SGTest.Server.LoginFixture.dll"

            source_out = run_checked(
                [args.godot, "--headless", "--path", str(repo / "client"),
                 "--script", "res://tests/build_online_gate_pack.gd", "--",
                 str(source_assets), str(content)],
                repo, env, args.timeout)
            if "ONLINE_GATE_SOURCE_READY" not in source_out:
                raise RuntimeError("same-origin source/content builder did not confirm readiness")

            sys.path.insert(0, str(repo / "tools"))
            from asset_converter.runtime_pack import build_runtime_pack
            from asset_converter.content.build_item_catalog import compile_item_catalog
            from asset_converter.content.build_equipment_combat_rules import (
                compile_equipment_combat_rules,
            )
            report = build_runtime_pack(
                Path(args.godot), source_assets, pack, stage=temp / "pack-stage")
            if not pack.is_file() or int(report.get("source_files", 0)) < 1:
                raise RuntimeError("same-origin runtime pack was not produced")

            appearance_out = run_checked(
                [sys.executable, str(appearance_builder), "--out", str(appearance_rules)],
                repo, env, args.timeout)
            if "CHARACTER_APPEARANCE_RULES_READY" not in appearance_out:
                raise RuntimeError("verified appearance rules were not generated")

            run_checked(
                ["dotnet", "build", str(host_project), "-c", "Release", "--nologo",
                 "-o", str(host_dir)],
                repo, env, args.timeout)
            run_checked(
                ["dotnet", "build", str(fixture_project), "-c", "Release", "--nologo",
                 "-o", str(fixture_dir)],
                repo, env, args.timeout)

            seed_out = run_checked(
                ["dotnet", str(fixture_dll), "--equipment-main-seed",
                 str(database), str(appearance_rules)],
                repo, env, args.timeout)
            if "EQUIPMENT_MAIN_SEED_PASS" not in seed_out:
                raise RuntimeError("formal equipment seed did not complete")

            catalog = compile_item_catalog(
                items_root,
                source_reference="Recovered2 Items/*.tres",
                hk_item_itd=hk_item_itd,
                hk_source_reference="HK2 Item/item.itd")
            item = next(
                (entry for entry in catalog["items"]
                 if int(entry.get("item_id", 0)) == 18501), None)
            if item is None:
                raise RuntimeError("source item 18501 missing from recovered catalog")
            presentation = item.get("presentation", {})
            if (
                item.get("category") != "equip"
                or int(presentation.get("equip_slot", 0)) != 3
                or int(presentation.get("hk_original_item_id", 0)) != 570
                or int(presentation.get("def", -1)) != 6
                or int(presentation.get("hk_defense", -1)) != 6
            ):
                raise RuntimeError("18501 source evidence no longer verifies body slot / DEF+6")
            write_json(equipment_rules, {
                "kind": "verified_equipment_item_rules",
                "version": 1,
                "rules_verified": True,
                "gameplay_ready": True,
                "source_reference": (
                    "Recovered2 Items/equip/18501.tres + HK2 Item/item.itd "
                    "exact bridge: original 570, slot 3"),
                "rules": [{"item_definition_id": 18501, "slot_id": 3}],
            })

            combat = compile_equipment_combat_rules(
                items_root, hk_item_itd, old_weapon_itd,
                item_source_reference="Recovered2 Items/*.tres",
                hk_source_reference="HK2 Item/item.itd",
                old_equip_source_reference=(
                    "Old SGOEquip::EquipItem + SGOPlayer::UpdateParam decompilation"),
                old_weapon_source_reference=(
                    "Old SGOEquip::ReadWeaponData/SearchIndex + Dat/Item/Weapon.itd"))
            combat_18501 = next(
                (rule for rule in combat["rules"]
                 if int(rule.get("item_definition_id", 0)) == 18501), None)
            if combat_18501 is None or int(combat_18501.get("defense_bonus", -1)) != 6:
                raise RuntimeError("18501 did not survive fail-closed equipment combat audit")
            equipment_combat_rules.write_text(
                json.dumps(combat, ensure_ascii=False), encoding="utf-8")

            host_command = [
                "dotnet", str(host_dll), "serve",
                "--content", str(content),
                "--identity", str(database),
                "--bind", "127.0.0.1", "--port", "0",
                "--max-connections", "8", "--max-per-address", "4",
                "--seconds", "180",
                "--equipment-rules", str(equipment_rules),
                "--equipment-combat-rules", str(equipment_combat_rules),
            ]
            host = subprocess.Popen(
                host_command, cwd=repo, env=env, text=True, encoding="utf-8",
                errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(
                target=capture, args=(host, logs["host"], host_events), daemon=True
            ).start()

            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            if int(ready.get("verified_equipment_rule_count", 0)) != 1:
                raise RuntimeError("Host did not load verified equipment rule")
            if int(ready.get("verified_equipment_combat_rule_count", 0)) < 1:
                raise RuntimeError("Host did not load verified equipment combat authority")
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str) -> tuple[subprocess.Popen[str], queue.Queue[str]]:
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_equipment_main_gate.gd", "--",
                    "--pack", str(pack), "--role", role, "--port", str(port),
                ]
                process = subprocess.Popen(
                    command, cwd=repo, env=env, text=True, encoding="utf-8",
                    errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    bufsize=1)
                processes.append(process)
                logs[role] = []
                events: queue.Queue[str] = queue.Queue()
                threading.Thread(
                    target=capture, args=(process, logs[role], events), daemon=True
                ).start()
                return process, events

            observer, observer_events = start_client("observer")
            actor, actor_events = start_client("actor")
            wait_for(actor_events, ACTOR_PASS, actor, deadline)
            wait_for(observer_events, OBSERVER_PASS, observer, deadline)

            actor.wait(timeout=max(1.0, deadline - time.monotonic()))
            observer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if actor.returncode != 0 or observer.returncode != 0:
                raise RuntimeError(
                    f"client exit actor={actor.returncode} observer={observer.returncode}")

            actor_required = [
                "ONLINE_EQUIPMENT_MAIN_GATE_EQUIP_PASS",
                "ONLINE_EQUIPMENT_MAIN_GATE_UNEQUIP_PASS",
                "ONLINE_EQUIPMENT_MAIN_GATE_ACTOR_PASS",
            ]
            observer_required = [
                "ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_EQUIP_PASS",
                "ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_DESPAWN_PASS",
                "ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_RELOG_PASS",
                "ONLINE_EQUIPMENT_MAIN_GATE_OBSERVER_PASS",
            ]
            for marker in actor_required:
                if not any(marker in line for line in logs["actor"]):
                    raise RuntimeError("actor missed milestone " + marker)
            for marker in observer_required:
                if not any(marker in line for line in logs["observer"]):
                    raise RuntimeError("observer missed milestone " + marker)

            print(
                "ONLINE_EQUIPMENT_MAIN_SCENE_GATE_PASS "
                "formal_main=true clients=2 source_022=true "
                "character_authority=true defense=3_to_9_to_3 "
                "server_authority=true sqlite_relog=true "
                "remote_equip_relog_clear=true verified_equipment_rules=1 "
                f"verified_equipment_combat_rules={int(ready.get('verified_equipment_combat_rule_count', 0))} "
                "same_origin_runtime_pack=true source_backed_item_18501=true "
                f"native_visible={str(args.native).lower()} port={port}")
            return 0
    except Exception as error:
        print("ONLINE_EQUIPMENT_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
        for name, lines in logs.items():
            for line in lines[-80:]:
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


if __name__ == "__main__":
    raise SystemExit(main())
