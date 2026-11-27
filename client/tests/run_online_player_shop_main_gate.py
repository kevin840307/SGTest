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

SELLER_PASS = "ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_PASS"
BUYER_PASS = "ONLINE_PLAYER_SHOP_MAIN_GATE_BUYER_PASS"
DEFINITION = 1001


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
        if clean.startswith("ONLINE_PLAYER_SHOP_MAIN_GATE_"):
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
        description="Formal two-client original 092 player-shop vertical gate")
    parser.add_argument("godot")
    parser.add_argument("--timeout", type=float, default=150.0)
    parser.add_argument("--native", action="store_true")
    args = parser.parse_args()

    repo = Path(__file__).resolve().parents[2]
    host_project = repo / "server/host/SGTest.Server.Host/SGTest.Server.Host.csproj"
    fixture_project = repo / "server/tests/SGTest.Server.LoginFixture/SGTest.Server.LoginFixture.csproj"
    appearance_builder = repo / "tools/ui_art/build_recovered2_character_rules.py"
    items_root = Path(r"C:\SGO\NewVersion\Recovered2\Items")
    source_item = items_root / "recovery/1001.tres"
    if not source_item.is_file():
        parser.error(f"required source-backed trade item missing: {source_item}")

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
            prefix="sgtest-online-player-shop-main-", ignore_cleanup_errors=True
        ) as temp_text:
            temp = Path(temp_text)
            source_assets = temp / "source_assets"
            content = temp / "content"
            pack = temp / "assets.pck"
            database = temp / "identity.db"
            appearance_rules = temp / "character-appearance.json"
            trade_rules = temp / "trade-item-rules.json"
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
            report = build_runtime_pack(
                Path(args.godot), source_assets, pack, stage=temp / "pack-stage")
            if not pack.is_file() or int(report.get("source_files", 0)) < 1:
                raise RuntimeError("same-origin runtime pack was not produced")

            item_catalog = compile_item_catalog(
                items_root,
                source_reference="Recovered2 Items/*.tres",
                only_ids={DEFINITION})
            item = item_catalog["items"][0] if item_catalog.get("items") else None
            if (
                item is None
                or int(item.get("item_id", 0)) != DEFINITION
                or item.get("category") != "recovery"
                or int(item.get("presentation", {}).get("can_trade", 0)) != 1
            ):
                raise RuntimeError("Recovered2 recovery/1001.tres no longer verifies can_trade=1")
            write_json(trade_rules, {
                "kind": "verified_trade_item_rules",
                "version": 1,
                "rules_verified": True,
                "gameplay_ready": True,
                "source_reference": (
                    "Recovered2 Items/recovery/1001.tres: can_trade=1; "
                    f"sha256={item['source_sha256']}"),
                "rules": [{"item_definition_id": DEFINITION, "can_trade": True}],
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
                ["dotnet", str(fixture_dll), "--player-shop-main-seed",
                 str(database), str(appearance_rules)],
                repo, env, args.timeout)
            if "PLAYER_SHOP_MAIN_SEED_PASS" not in seed_out:
                raise RuntimeError("formal player-shop seed did not complete")

            host = subprocess.Popen(
                ["dotnet", str(host_dll), "serve",
                 "--content", str(content),
                 "--identity", str(database),
                 "--bind", "127.0.0.1", "--port", "0",
                 "--max-connections", "8", "--max-per-address", "4",
                 "--seconds", "210",
                 "--trade-item-rules", str(trade_rules)],
                cwd=repo, env=env, text=True, encoding="utf-8", errors="replace",
                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1)
            processes.append(host)
            logs["host"] = []
            host_events: queue.Queue[str] = queue.Queue()
            threading.Thread(
                target=capture, args=(host, logs["host"], host_events), daemon=True
            ).start()

            deadline = time.monotonic() + args.timeout
            ready_line = wait_for(host_events, '"AES_HOST_READY"', host, deadline)
            ready = json.loads(ready_line)
            if int(ready.get("verified_trade_item_rule_count", 0)) != 1:
                raise RuntimeError("Host did not load one verified trade item rule")
            if not bool(ready.get("player_shop_enabled", False)):
                raise RuntimeError("Host reported player_shop_enabled=false")
            port = int(str(ready["endpoint"]).rsplit(":", 1)[1])

            def start_client(role: str) -> tuple[subprocess.Popen[str], queue.Queue[str]]:
                command = [args.godot]
                if not args.native:
                    command.append("--headless")
                command += [
                    "--path", str(repo / "client"),
                    "--script", "res://tests/test_online_player_shop_main_gate.gd", "--",
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

            # Buyer enters first; seller waits until the buyer is in AOI before opening 092.
            buyer, buyer_events = start_client("buyer")
            time.sleep(0.35)
            seller, seller_events = start_client("seller")
            wait_for(seller_events, SELLER_PASS, seller, deadline)
            wait_for(buyer_events, BUYER_PASS, buyer, deadline)

            seller.wait(timeout=max(1.0, deadline - time.monotonic()))
            buyer.wait(timeout=max(1.0, deadline - time.monotonic()))
            if seller.returncode != 0 or buyer.returncode != 0:
                raise RuntimeError(
                    f"client exit seller={seller.returncode} buyer={buyer.returncode}")

            seller_required = [
                "ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_LIST_PASS",
                "ONLINE_PLAYER_SHOP_MAIN_GATE_SELLER_SOLD_PASS",
                "ONLINE_PLAYER_SHOP_MAIN_GATE_CLOSE_ALL_PASS",
                SELLER_PASS,
            ]
            buyer_required = [
                "ONLINE_PLAYER_SHOP_MAIN_GATE_BUY_PASS",
                BUYER_PASS,
            ]
            for marker in seller_required:
                if not any(marker in line for line in logs["seller"]):
                    raise RuntimeError("seller missed milestone " + marker)
            for marker in buyer_required:
                if not any(marker in line for line in logs["buyer"]):
                    raise RuntimeError("buyer missed milestone " + marker)

            print(
                "ONLINE_PLAYER_SHOP_MAIN_SCENE_GATE_PASS "
                "formal_main=true clients=2 source_092=true "
                "name_price_quantity=true world_sign_click=true browse_buy=true "
                "server_authority=true partial_stack=true sold_push=true "
                "close_all_confirm=true unsold_restore=true sqlite_relog_both=true "
                "verified_trade_item_rules=1 source_backed_item_1001=true "
                "same_origin_runtime_pack=true "
                f"native_visible={str(args.native).lower()} port={port}")
            return 0
    except Exception as error:
        print("ONLINE_PLAYER_SHOP_MAIN_SCENE_GATE_FAIL", error, file=sys.stderr)
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
