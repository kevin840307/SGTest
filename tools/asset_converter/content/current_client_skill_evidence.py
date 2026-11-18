"""Compile fail-closed evidence from the current Godot client SkillData tree.

This source family is intentionally separate from Old server Dat/Skill.  It records
what the converted/current client says; it never silently merges IDs or formulas
across versions and never emits live combat rules.
"""
from __future__ import annotations

import csv
import hashlib
import io
import re
from pathlib import Path


_INT_FIELDS = (
    "level", "cast_range", "aoe_radius", "cost_hp", "cost_mp", "cost_sp",
    "base_damage", "damage_multiplier", "cooldown", "duration", "tick_count",
    "max_hp_bonus", "max_mp_bonus", "max_sp_bonus", "atk_bonus", "def_bonus",
    "matk_bonus", "mdef_bonus", "hit_bonus", "flee_bonus", "luk_bonus",
    "aspd_bonus", "move_speed_bonus", "crit_rate_bonus", "crit_damage_bonus",
)
_RESOURCE_INT_FIELDS = ("id", "skill_type", "damage_type", "icon_id")
_RESOURCE_TEXT_FIELDS = (
    "skill_name", "description", "fx_use", "fx_hit", "fx_extra", "special_skill",
)


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _decode(path: Path) -> str:
    raw=path.read_bytes()
    for encoding in ("utf-8-sig","cp950"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            pass
    raise ValueError(f"unsupported current-client text encoding: {path.name}")


def _int(text: str, name: str, default: int = 0) -> int:
    matches=re.findall(
        rf'(?m)^\s*{re.escape(name)}\s*=\s*(-?[0-9]+)\s*$',text)
    if not matches:
        return default
    values=list(dict.fromkeys(int(value) for value in matches))
    if len(values)!=1:
        raise ValueError(f"ambiguous {name}")
    return values[0]


def _quoted(text: str, name: str) -> str:
    matches=re.findall(
        rf'(?m)^\s*{re.escape(name)}\s*=\s*"((?:[^"\\]|\\.)*)"\s*$',text)
    if not matches:
        return ""
    values=list(dict.fromkeys(matches))
    if len(values)!=1:
        raise ValueError(f"ambiguous {name}")
    # Godot strings in this evidence subset use ordinary escaped quotes/backslashes.
    return bytes(values[0],"utf-8").decode("unicode_escape") if "\\" in values[0] else values[0]


def _blocks(text: str) -> list[str]:
    return [
        match.group(1)
        for match in re.finditer(
            r'(?ms)^\[sub_resource[^\]]*\]\s*(.*?)'
            r'(?=^\[sub_resource|^\[resource\]|\Z)',
            text,
        )
    ]


def _resource_body(text: str) -> str:
    match=re.search(r'(?ms)^\[resource\]\s*(.*)\Z',text)
    if match is None:
        raise ValueError("SkillData .tres missing [resource]")
    return match.group(1)


def _parse_skill(path: Path) -> dict:
    text=_decode(path)
    resource=_resource_body(text)
    skill_id=_int(resource,"id")
    if skill_id<=0:
        try:
            skill_id=int(path.stem)
        except ValueError as error:
            raise ValueError(f"{path.name} missing positive id") from error
    levels=[]
    seen=set()
    for block in _blocks(text):
        # Current SkillData files may contain only SkillLevelData subresources.
        # Ignore an unrelated subresource only when it has no level/runtime fields.
        if not re.search(r'(?m)^\s*(?:level|cast_range|aoe_radius|cost_mp|base_damage|tick_count)\s*=',block):
            continue
        row={field:_int(block,field) for field in _INT_FIELDS}
        if row["level"]<=0:
            row["level"]=1
        if row["level"] in seen:
            raise ValueError(f"{path.name} has duplicate level {row['level']}")
        seen.add(row["level"])
        if any(value<0 for key,value in row.items() if key.startswith("cost_")):
            raise ValueError(f"{path.name} has negative resource cost")
        if row["cast_range"]<0 or row["aoe_radius"]<0 or row["cooldown"]<0:
            raise ValueError(f"{path.name} has negative range/cooldown")
        levels.append(row)
    if not levels:
        raise ValueError(f"{path.name} contains no SkillLevelData")
    result={
        "skill_id":skill_id,
        "source_file":f"Skills/{path.name}",
        "source_sha256":_sha(path),
        **{field:_int(resource,field) for field in _RESOURCE_INT_FIELDS},
        **{field:_quoted(resource,field) for field in _RESOURCE_TEXT_FIELDS},
        "levels":sorted(levels,key=lambda row:row["level"]),
    }
    if result["id"] not in (0,skill_id):
        raise ValueError(f"{path.name} filename/resource SkillId mismatch")
    result["id"]=skill_id
    return result


def _percentage_rows(path: Path | None, selected: set[int]) -> tuple[list[dict],dict]:
    if path is None or not path.is_file():
        return [],{}
    text=_decode(path)
    rows=list(csv.reader(io.StringIO(text)))
    if not rows:
        return [],{"Assets/GameInfo/Skill.csv":_sha(path)}
    header=[value.strip() for value in rows[0]]
    try:
        id_index=header.index("技能編號")
    except ValueError:
        return [],{"Assets/GameInfo/Skill.csv":_sha(path)}
    output=[]
    for index,row in enumerate(rows[1:],2):
        if id_index>=len(row):
            continue
        try:
            skill_id=int(row[id_index].strip())
        except ValueError:
            continue
        if skill_id not in selected:
            continue
        output.append({
            "row":index,
            "skill_id":skill_id,
            "columns":{
                header[column] if column<len(header) and header[column] else str(column):value
                for column,value in enumerate(row)
            },
        })
    return output,{"Assets/GameInfo/Skill.csv":_sha(path)}


def compile_current_client_skill_evidence(
    client_root: Path, only_ids: set[int] | None = None
) -> dict:
    client_root=Path(client_root).resolve()
    skill_dir=client_root/"Skills"
    if not skill_dir.is_dir():
        raise ValueError("current client Skills directory required")
    if only_ids is not None and (
        not only_ids or any(not isinstance(value,int) or value<=0 for value in only_ids)
    ):
        raise ValueError("requested SkillIds must be positive integers")

    found={}
    for path in sorted(skill_dir.glob("*.tres"),key=lambda value:value.name.casefold()):
        parsed=_parse_skill(path)
        skill_id=parsed["skill_id"]
        if skill_id in found:
            raise ValueError(f"duplicate current-client SkillId {skill_id}")
        found[skill_id]=parsed

    available=set(found)
    selected_ids=sorted(available if only_ids is None else available & only_ids)
    missing=[] if only_ids is None else sorted(only_ids-available)
    definitions=[found[skill_id] for skill_id in selected_ids]
    percentage_rows,percentage_hashes=_percentage_rows(
        client_root/"Assets"/"GameInfo"/"Skill.csv",set(selected_ids))

    blockers=set()
    for definition in definitions:
        for level in definition["levels"]:
            if level["cost_mp"]>0:
                blockers.add("durable_mp_owner_required")
            if level["cost_sp"]>0:
                blockers.add("durable_sp_owner_required")
            if level["cost_hp"]>0:
                blockers.add("durable_hp_skill_cost_owner_required")
            if level["aoe_radius"]>0:
                blockers.add("authoritative_aoe_targeting_required")
            if level["base_damage"]!=0 or level["damage_multiplier"]!=0:
                blockers.add("damage_formula_runtime_mapping_required")

    hashes={definition["source_file"]:definition["source_sha256"] for definition in definitions}
    hashes.update(percentage_hashes)
    return {
        "version":1,
        "kind":"current_client_skilldata_evidence_not_runtime",
        "source_family":"godot_current_client_skilldata",
        "status":"partial" if missing else "pass",
        "requested_skill_ids":sorted(only_ids) if only_ids is not None else None,
        "missing_requested_skill_ids":missing,
        "definitions":definitions,
        "percentage_rows":percentage_rows,
        "sources_sha256":dict(sorted(hashes.items())),
        "activation_blockers":sorted(blockers),
        "runtime_rules_generated":False,
        "combat_ready":False,
        "notes":[
            "This evidence family is separate from Old server Dat/Skill and IDs are never nearest-matched.",
            "SkillLevelData fields are source facts; their runtime formulas are not inferred here.",
            "Private source files are hashed/read locally and are not copied into the public repository.",
        ],
    }
