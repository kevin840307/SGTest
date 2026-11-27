from __future__ import annotations

from pathlib import Path
import argparse
import json
import os
import queue
import shutil
import subprocess
import sys
import tempfile
import threading
import time

ACTOR_PASS = "ONLINE_MECHA_MAIN_GATE_ACTOR_PASS"
OBSERVER_PASS = "ONLINE_MECHA_MAIN_GATE_OBSERVER_PASS"
MECHA_DEFINITION = 50018
WEAPON_DEFINITION = 50703
ENGINE_DEFINITION = 50702
BATTERY_DEFINITION = 50103
CAPACITY = 25200
MECHA_BASES = [
    "mecha/ms/standard/b-msbody09",
    "mecha/ms/standard/b-msleg09",
    "mecha/ms/standard/b-l-msarm09",
    "mecha/ms/standard/b-r-msarm09",
    "mecha/ms/head/b-head",
    "mecha/ms/head/b-hair00",
]


def run_checked(command: list[str], cwd: Path, env: dict[str, str], timeout: float) -> str:
    result = subprocess.run(
        command, cwd=cwd, env=env, text=True, encoding="utf-8", errors="replace",
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=timeout)
    if result.returncode != 0:
        raise RuntimeError("command failed: " + " ".join(command[:7]) + "\n" + result.stdout)
    return result.stdout


def capture(process: subprocess.Popen[str], lines: list[str], notices: queue.Queue[str]) -> None:
    assert process.stdout is not None
    for line in process.stdout:
        clean = line.rstrip()
        lines.append(clean)
        notices.put(clean)
        if clean.startswith("ONLINE_MECHA_MAIN_GATE_"):
            print(clean, flush=True)


def wait_for(notices: queue.Queue[str], token: str,
             process: subprocess.Popen[str], deadline: float) -> str:
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


def publish_private_mecha_assets(
    repo: Path, private_assets: Path, scoped_root: Path
) -> set[str]:
    from asset_converter.atlas.dense_atlas import POLICY, build_group
    from asset_converter.atlas.layout import select_groups
    from asset_converter.content.asset_paths import sprite_asset_id
    from asset_converter.content.export_assets import publish

    source = repo / ".cache/asset_converter/source"
    scoped_archive = scoped_root / "archive"
    groups = select_groups(source, category="ms")
    selected_groups: list[tuple[Path, list[Path], str]] = []
    wanted = set(MECHA_BASES)
    for key, paths in groups.items():
        asset_id = sprite_asset_id(key.as_posix())[0]
        if asset_id in wanted:
            selected_groups.append((key, paths, asset_id))
    found = {asset_id for _, _, asset_id in selected_groups}
    if found != wanted:
        raise RuntimeError(
            "current original MS source is missing required grouped families: "
            + str(sorted(wanted - found)))

    rows: list[dict] = []
    for key, paths, _ in selected_groups:
        rows.append(build_group(
            source, scoped_archive / "sprites", key, paths, force=False))
    write_json(scoped_archive / "index.json", {
        "packing_policy": POLICY,
        "source": str(source),
        "groups_expected": len(rows),
        "groups": rows,
        "errors": [],
        "source_scope": "filtered",
        "status": "complete",
    })

    result = publish(source, scoped_archive, private_assets, wanted)
    if result.get("status") != "pass":
        raise RuntimeError("isolated MS publish failed: " + json.dumps(result, ensure_ascii=False))
    return wanted


def add_private_mecha_assets(
    source_assets: Path, private_assets: Path, selected: set[str]
) -> int:
    catalog_dir = source_assets / "catalog"
    catalog_dir.mkdir(parents=True, exist_ok=True)
    entries: dict[str, dict[str, str]] = {}
    for asset_id in sorted(selected):
        source = private_assets / Path(asset_id)
        manifest = source / "atlas.json"
        if not manifest.is_file():
            raise RuntimeError(f"source-backed Mecha asset missing: {asset_id}")
        target = source_assets / Path(asset_id)
        shutil.copytree(source, target)
        entries[asset_id] = {
            "kind": "sprite_atlas",
            "file": asset_id + "/atlas.json",
        }
    if not entries:
        raise RuntimeError("no source-backed Mecha assets selected")
    write_json(catalog_dir / "mecha.json", {"assets": entries})
    root_path = catalog_dir / "assets.json"
    root = json.loads(root_path.read_text(encoding="utf-8"))
    root.setdefault("categories", {})["mecha"] = {"index": "catalog/mecha.json"}
    write_json(root_path, root)
    return len(entries)


def item_by_id(catalog: dict, item_id: int) -> dict:
    item = next((x for x in catalog["items"] if int(x.get("item_id", 0)) == item_id), None)
    if item is None:
        raise RuntimeError(f"source item {item_id} missing")
    return item


def verify_source_catalog(catalog: dict) -> None:
    mecha = item_by_id(catalog, MECHA_DEFINITION)
    p = mecha.get("presentation", {})
    if (
        mecha.get("category") != "horse"
        or p.get("horse_type") != "MS"
        or int(p.get("body_id", 0)) != 50701
        or int(p.get("weapon_id", 0)) != WEAPON_DEFINITION
        or int(p.get("engine_id", 0)) != ENGINE_DEFINITION
        or int(p.get("max_battery", 0)) != CAPACITY
        or int(p.get("req_level", 0)) != 65
    ):
        raise RuntimeError("50018 source metadata no longer matches verified MS definition")
    for definition, slot in ((WEAPON_DEFINITION, 1), (ENGINE_DEFINITION, 2)):
        item = item_by_id(catalog, definition)
        ip = item.get("presentation", {})
        if (
            item.get("category") != "horseequip"
            or ip.get("horse_type") != "MS"
            or int(ip.get("equip_slot", 0)) != slot
            or int(ip.get("req_level", 0)) != 65
            or int(ip.get("spr_id", 0)) != 9
        ):
            raise RuntimeError(f"{definition} source metadata no longer verifies Mecha slot {slot}")
    battery = item_by_id(catalog, BATTERY_DEFINITION)
    bp = battery.get("presentation", {})
    if battery.get("category") != "horsebattery" or int(bp.get("battery", 0)) != CAPACITY:
        raise RuntimeError("50103 source metadata no longer verifies 25200 battery")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Formal two-client original 056 Mecha vertical gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=150.0)
    parser.add_argument("--native", action="store_true")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    fixture_project = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    appearance_builder = repo / "tools/ui_art/build_recovered2_character_rules.py"
    recovered2 = Path(r"C:\SGO\NewVersion\Recovered2")
    items_root = recovered2 / "Items"

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
            prefix="sgtest-online-mecha-main-", ignore_cleanup_errors=True
        ) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            private_assets = temp / "mecha_assets"
            content = temp / "content"
            pack = temp / "assets.pck"
            database = temp / "identity.db"
            appearance_rules = temp / "character-appearance.json"
            mecha_equipment_rules = temp / "mecha-equipment-rules.json"
            mecha_battery_rules = temp / "mecha-battery-rules.json"
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
            selected_mecha_assets = publish_private_mecha_assets(
                repo, private_assets, temp / "scoped_mecha_source")
            copied_mecha_assets = add_private_mecha_assets(
                source_assets, private_assets, selected_mecha_assets)
            report = build_runtime_pack(
                Path(args.godot), source_assets, pack, stage=temp / "pack-stage")
            if (
                not pack.is_file()
                or copied_mecha_assets <= 0
                or int(report.get("source_files", 0)) < copied_mecha_assets
            ):
                raise RuntimeError("same-origin runtime pack did not include Mecha source assets")

            catalog = compile_item_catalog(
                items_root,
                source_reference="Recovered2 Items/*.tres",
                only_ids={
                    MECHA_DEFINITION, WEAPON_DEFINITION,
                    ENGINE_DEFINITION, BATTERY_DEFINITION, 50701,
                })
            verify_source_catalog(catalog)
            write_json(mecha_equipment_rules, {
                "kind": "verified_mecha_equipment_rules",
                "version": 1,
                "rules_verified": True,
                "gameplay_ready": True,
                "source_reference": (
                    "Recovered2 Items: 50018 MS + 50703 weapon slot1 + 50702 engine slot2"),
                "rules": [
                    {"item_definition_id": WEAPON_DEFINITION,
                     "equipment_class": 1, "required_level": 65},
                    {"item_definition_id": ENGINE_DEFINITION,
                     "equipment_class": 2, "required_level": 65},
                ],
            })
            write_json(mecha_battery_rules, {
                "kind": "verified_mecha_battery_rules",
                "version": 1,
                "rules_verified": True,
                "gameplay_ready": True,
                "source_reference": (
                    "Recovered2 Items 50018 max_battery=25200 + 50103 battery=25200"),
                "rules": [{
                    "mecha_definition_id": MECHA_DEFINITION,
                    "battery_item_definition_id": BATTERY_DEFINITION,
                    "capacity": CAPACITY,
                    "charge_amount": CAPACITY,
                    "allow_while_mounted": False,
                }],
            })

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
                ["dotnet", str(fixture_dll), "--mecha-main-seed",
                 str(database), str(appearance_rules)],
                repo, env, args.timeout)
            if "MECHA_MAIN_SEED_PASS" not in seed_out:
                raise RuntimeError("formal Mecha seed did not complete")

            host_command = [
                "dotnet", str(host_dll), "serve",
                "--content", str(content),
                "--identity", str(database),
                "--bind", "127.0.0.1", "--port", "0",
                "--max-connections", "8", "--max-per-address", "4",
                "--seconds", "210",
                "--mecha-equipment-rules", str(mecha_equipment_rules),
                "--mecha-battery-rules", str(mecha_battery_rules),
            ]
            host = subprocess.Popen(
                host_command, cwd=repo, env=env, text=True, encoding="utf-8",
                errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(
                target=capture, args=(host, logs["host"], host_events), daemon=True).start()

            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            if int(ready.get("verified_mecha_equipment_rule_count", 0)) != 2:
                raise RuntimeError("Host did not load 2 verified Mecha equipment rules")
            if int(ready.get("verified_mecha_battery_rule_count", 0)) != 1:
                raise RuntimeError("Host did not load verified Mecha battery rule")
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str) -> tuple[subprocess.Popen[str], queue.Queue[str]]:
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_mecha_main_gate.gd", "--",
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
                    target=capture, args=(process, logs[role], events), daemon=True).start()
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

            for marker in (
                "ONLINE_MECHA_MAIN_GATE_MOUNT_PASS",
                "ONLINE_MECHA_MAIN_GATE_DISMOUNT_PASS",
                ACTOR_PASS,
            ):
                if not any(marker in line for line in logs["actor"]):
                    raise RuntimeError("actor missed milestone " + marker)
            for marker in (
                "ONLINE_MECHA_MAIN_GATE_OBSERVER_MOUNT_PASS",
                "ONLINE_MECHA_MAIN_GATE_OBSERVER_RELOG_PASS",
                OBSERVER_PASS,
            ):
                if not any(marker in line for line in logs["observer"]):
                    raise RuntimeError("observer missed milestone " + marker)

            print(
                "ONLINE_MECHA_MAIN_SCENE_GATE_PASS "
                "formal_main=true clients=2 source_056=true "
                "server_authority=true battery_consume=true equipment_slots=1,2 "
                "sqlite_relog=true remote_mount_relog_dismount=true "
                "verified_mecha_equipment_rules=2 verified_mecha_battery_rules=1 "
                "source_backed_mecha_50018=true source_backed_world_visual=true "
                f"native_visible={str(args.native).lower()} port={port}")
            return 0
    except Exception as error:
        print("ONLINE_MECHA_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
        for name, lines in logs.items():
            for line in lines[-100:]:
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
