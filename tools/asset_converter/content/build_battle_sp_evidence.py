"""Publish Old battle.cfg SP combat variation evidence, not attack cadence."""

from __future__ import annotations

from pathlib import Path

from asset_converter.json_files import write_json
from asset_converter.source.old_battle_cfg import read_old_battle_cfg

KIND = "old_battle_sp_source_evidence"
VERSION = 1


def compile_battle_sp_evidence(
    source: Path, *, current_fps: int | None = None,
    default_fps: int | None = None,
) -> dict:
    timing, audit = read_old_battle_cfg(Path(source))
    if (current_fps is None) != (default_fps is None):
        raise ValueError("Current/default server FPS must be supplied together")
    scaled = (timing.scaled_for_fps(current_fps, default_fps)
              if current_fps is not None and default_fps is not None else None)
    return {
        "kind": KIND,
        "version": VERSION,
        "status": "partial_source_only",
        "source_reference": (
            "Old SGOBattleSys::LoadCfg/WillDamage_ForSP/HPLostChange_ForSP "
            "+ Dat/battle.cfg; player attack cadence NOT VERIFIED"
        ),
        "sp_multiple": timing.multiple,
        "sp_values_at_source_fps": list(timing.sp),
        "race_delta_values_at_source_fps": list(timing.race_delta),
        "fps_scaled_loader_values": scaled,
        "observed_consumers": [
            "SGOBattleSys::WillDamage_ForSP",
            "SGOBattleSys::HPLostChange_ForSP",
        ],
        "combat_profile_verified": False,
        "combat_cooldown_verified": False,
        "weapon_binding_verified": False,
        "audit": audit,
    }


def publish_battle_sp_evidence(
    source: Path, output: Path, *, current_fps: int | None = None,
    default_fps: int | None = None,
) -> dict:
    legacy_source = Path(source).resolve()
    destination = Path(output).resolve()
    if destination == legacy_source or destination.is_relative_to(legacy_source.parent):
        raise ValueError("Do not write into the read-only Old source tree")
    result = compile_battle_sp_evidence(
        legacy_source, current_fps=current_fps, default_fps=default_fps)
    write_json(destination, result)
    return {**result, "output": str(destination)}


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser(
        description="Audit read-only Old battle.cfg SP; never enable combat.")
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--current-fps", type=int)
    parser.add_argument("--default-fps", type=int)
    args = parser.parse_args()
    result = publish_battle_sp_evidence(
        args.source, args.output,
        current_fps=args.current_fps, default_fps=args.default_fps)
    print("OLD_BATTLE_CFG_SOURCE_PASS",
          result["audit"]["source_sha256"],
          f"SP={len(result['sp_values_at_source_fps'])}",
          f"Race={len(result['race_delta_values_at_source_fps'])}",
          "COMBAT_NOT_VERIFIED")
