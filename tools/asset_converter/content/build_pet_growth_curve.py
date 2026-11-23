"""Publish a private audited Pet growth manifest from Old Server source data."""

from __future__ import annotations

from pathlib import Path

from asset_converter.json_files import write_json
from asset_converter.source.old_pet_level_curve import (
    MAX_LEVEL,
    read_old_pet_level_curve,
)

SCHEMA = "sgtest.pet_growth_curve/v1"


def compile_pet_growth_curve(
    source_csv: Path,
    *,
    source_reference: str = "Old Server Dat/Player/Player_Up_Lv.csv",
) -> dict:
    source_label = source_reference.strip()
    if not source_label or len(source_label) > 256:
        raise ValueError("pet growth source reference is invalid")
    thresholds, audit = read_old_pet_level_curve(Path(source_csv))
    return {
        "schema": SCHEMA,
        "status": "pass",
        "source": source_label,
        "source_sha256": audit["sha256"],
        "source_bytes": audit["bytes"],
        "max_level": MAX_LEVEL,
        "level_up_total_experience": thresholds,
        "semantics": (
            "Old CSGOPetSys::ReadLevelUpTable cumulative first 100 rows; "
            "CSGOPet::AddExp advances repeatedly while total EXP meets current threshold"
        ),
    }


def publish_pet_growth_curve(
    source_csv: Path,
    output: Path,
    *,
    source_reference: str = "Old Server Dat/Player/Player_Up_Lv.csv",
) -> dict:
    result = compile_pet_growth_curve(
        source_csv, source_reference=source_reference)
    destination = Path(output).resolve()
    source = Path(source_csv).resolve()
    if destination == source:
        raise ValueError("pet growth output must not overwrite original source")
    write_json(destination, result)
    return {**result, "output": str(destination)}
