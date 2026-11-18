"""Compile fail-closed evidence from the original Old server Dat/Skill tree.

This module records source facts only.  It does not translate legacy formulas into
live SGTest combat rules and never guesses missing SkillIds, resource ownership,
monster DP/DR, MP state, or visual semantics.
"""
from __future__ import annotations

import csv
import hashlib
import io
import re
from pathlib import Path


_SCRIPT = re.compile(r"SKC([0-9]{1,3})J([0-9]{3})\.txt", re.IGNORECASE)


def _decode(path: Path) -> str:
    raw = path.read_bytes()
    for encoding in ("utf-8-sig", "cp950"):
        try:
            return raw.decode(encoding)
        except UnicodeDecodeError:
            pass
    raise ValueError(f"unsupported legacy text encoding: {path.name}")


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _one_assignment(text: str, name: str, quoted: bool = False):
    pattern = (
        rf'(?m)^\s*{re.escape(name)}\s*=\s*"([^"]+)"\s*$'
        if quoted else
        rf'(?m)^\s*{re.escape(name)}\s*=\s*(-?[0-9]+)\s*$'
    )
    values = re.findall(pattern, text)
    unique = list(dict.fromkeys(values))
    if len(unique) > 1:
        return {"ambiguous": unique}
    if not unique:
        return None
    return unique[0] if quoted else int(unique[0])


def _function_body(text: str, name: str) -> str | None:
    match = re.search(
        rf'(?ms)^\s*function\s+{re.escape(name)}\s*\([^)]*\)\s*(.*?)'
        rf'(?=^\s*function\s+[A-Za-z_]|^\s*#include|\Z)',
        text,
    )
    return match.group(1) if match else None


def _level_returns(text: str, name: str):
    body = _function_body(text, name)
    if body is None:
        return None
    values = {}
    for level, value in re.findall(
        r'(?s)(?:if|elseif)\s+lv\s*==\s*([0-9]+)\s+then\s*.*?'
        r'\breturn\s+(-?[0-9]+)',
        body,
    ):
        values[str(int(level))] = int(value)
    tail = re.search(r'(?s)\belse\s+return\s+(-?[0-9]+)', body)
    if tail:
        values["else"] = int(tail.group(1))
    return values or None


def _attack_calls(text: str):
    calls = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("--") or line.startswith("function "):
            continue
        match = re.match(
            r'(Attack(?:_[A-Za-z0-9_]+)?)\s*\(\s*([0-9]+)?\s*\)',
            line,
        )
        if match:
            calls.append({
                "function": match.group(1),
                "argument": int(match.group(2)) if match.group(2) else None,
            })
    return calls


def _script_facts(path: Path) -> dict:
    text = _decode(path)
    return {
        "resource": _one_assignment(text, "consume", quoted=True),
        "other_resource": _one_assignment(text, "other_consume", quoted=True),
        "ap_source": _one_assignment(text, "APcount", quoted=True),
        "dp_source": _one_assignment(text, "DPcount", quoted=True),
        "vary_delta_percent": _one_assignment(text, "varyDelta"),
        "min_hit": _one_assignment(text, "MinHit"),
        "min_hit_delta": _one_assignment(text, "MinHitDelta"),
        "attack_calls": _attack_calls(text),
        "effect_by_level": _level_returns(text, "GetPara"),
        "consume_by_level": _level_returns(text, "GetConTotal"),
        "includes_skillcommon": bool(re.search(
            r'(?mi)^\s*#include\s+"skillcommon\.txt"\s*$', text)),
    }


def _hit_formula(common: str) -> dict:
    checks = {
        "hr_minus_dr": bool(re.search(
            r'HRDiff\s*=\s*GetData\(\s*"HR"\s*\)\s*-\s*'
            r'GetTargetData\(\s*defender\s*,\s*"DR"\s*\)', common)),
        "level_delta_x2": bool(re.search(
            r'LvDiff\s*=\s*\(\s*GetData\(\s*"Level"\s*\)\s*-\s*'
            r'GetTargetData\(\s*defender\s*,\s*"Level"\s*\)\s*\)\s*\*\s*2',
            common)),
        "base_60": bool(re.search(
            r'HRFinal\s*=\s*HRDiff\s*\+\s*LvDiff\s*\+\s*60', common)),
        "minimum_30": bool(re.search(
            r'if\s+HRFinal\s*<\s*30\s+then\s*HRFinal\s*=\s*30', common,
            re.DOTALL)),
        "roll_1_to_100": bool(re.search(
            r'if\s+random\(\s*100\s*\)\s*<=\s*HRFinal\s+then', common)),
        "miss_path": "AttackDefender_Miss()" in common,
    }
    verified = all(checks.values())
    return {
        "verified": verified,
        "checks": checks,
        "formula": (
            "HRFinal=max(30,attacker.HR-defender.DR+"
            "(attacker.Level-defender.Level)*2+60); hit=random(100)<=HRFinal"
            if verified else None
        ),
        "attacker_stat": "HR" if verified else None,
        "defender_stat": "DR" if verified else None,
        "level_multiplier": 2 if verified else None,
        "base": 60 if verified else None,
        "minimum": 30 if verified else None,
    }


def _damage_formula(common: str) -> dict:
    checks = {
        "level_difference": bool(re.search(
            r'DefLv\s*=\s*GetTargetData\(\s*defender\s*,\s*"Level"\s*\)'
            r'.*?AttLv\s*=\s*GetData\(\s*"Level"\s*\)'
            r'.*?LvDif\s*=\s*DefLv\s*-\s*AttLv', common, re.DOTALL)),
        "over_9_levels_forces_min": bool(re.search(
            r'if\s+LvDif\s*>\s*9\s+then\s*damage\s*=\s*GetMinDamage\(\)',
            common, re.DOTALL)),
        "ap_plus_effect": bool(re.search(
            r'attackerAP\s*=\s*GetData\(\s*APcount\s*\)\s*\+\s*effect',
            common)),
        "pre_defense_variance": bool(re.search(
            r'attackerAP\s*=\s*attackerAP\s*\+\s*attackerAP\s*\*\s*'
            r'\(\s*varyDelta\s*-\s*random\(\s*varyDelta\s*\*\s*2\s*\)\s*\)'
            r'\s*/\s*100', common)),
        "defender_dp": bool(re.search(
            r'defenderDP\s*=\s*GetTargetData\(\s*defender\s*,\s*DPcount\s*\)',
            common)),
        "subtract_dp": bool(re.search(
            r'damage\s*=\s*attackerAP\s*-\s*defenderDP', common)),
        "minimum_damage_fallback": bool(re.search(
            r'if\s+damage\s*<\s*MinHit\s+then\s*damage\s*=\s*GetMinDamage\(\)',
            common, re.DOTALL)),
        "positive_level_gap_scale": bool(re.search(
            r'elseif\s+LvDif\s*>\s*0\s+then\s*'
            r'damage\s*=\s*damage\s*\*\s*\(\s*10\s*-\s*LvDif\s*\)\s*/\s*10',
            common, re.DOTALL)),
        "divide_by_hit_count": bool(re.search(
            r'damage_perhit\s*=\s*GetTotalDamage\(\s*defender\s*\)'
            r'.*?damage_perhit\s*=\s*damage_perhit\s*/\s*GetSkillHitCount\(\)',
            common, re.DOTALL)),
        "per_hit_hr_check": bool(re.search(
            r'function\s+DoHit_PerHitCount.*?if\s+checkHR\(\s*defender\s*\)\s*~=\s*0',
            common, re.DOTALL)),
        "second_variance_per_hit": bool(re.search(
            r'totaldamage\s*=\s*totaldamage\s*\+\s*totaldamage\s*\*\s*'
            r'\(\s*varyDelta\s*-\s*random\(\s*varyDelta\s*\*\s*2\s*\)\s*\)'
            r'\s*/\s*100', common)),
        "attack_defender": bool(re.search(
            r'AttackDefender\(\s*totaldamage\s*,\s*0\s*\)', common)),
        "miss_path": bool(re.search(r'AttackDefender_Miss\(\s*\)', common)),
        "lethal_clamps_to_current_hp": bool(re.search(
            r'if\s+hp\s*<=\s*totaldamage\s+then\s*'
            r'totaldamage\s*=\s*hp', common, re.DOTALL)),
    }
    verified = all(checks.values())
    return {
        "verified": verified,
        "checks": checks,
        "summary": (
            "total=(AP+effect) with varyDelta, minus defender DP, level-gap/minimum "
            "rules, divided by skill hit count; each hit re-applies varyDelta and "
            "HR/DR hit check; lethal damage clamps to current HP"
            if verified else None
        ),
        "random_calls_per_damage_path": (
            ["GetMinDamage/random(MinHitDelta) when minimum path is used",
             "pre-defense random(varyDelta*2)",
             "per-hit random(varyDelta*2)",
             "per-hit random(100) for HR/DR"]
            if verified else None
        ),
    }


def _range_semantics(common: str) -> dict:
    body = _function_body(common, "Attack_Range")
    if body is None:
        return {"verified": False, "checks": {}}
    checks = {
        "resource_guard": bool(re.search(
            r'GetData\(\s*consume\s*\)\s*>=\s*conTotal', body)),
        "visible_enemy_first": bool(re.search(
            r'GetVisEnemyList_First\(\s*AttackRange\s*\)', body)),
        "consume_only_when_any_target": bool(re.search(
            r'if\s+hasdefender\s*~=\s*0\s+then\s*'
            r'ModifyData\(\s*consume\s*,\s*-conTotal\s*\)',
            body, re.DOTALL)),
        "iterate_visible_list": bool(re.search(
            r'while\s+hasdefender\s*~=\s*0\s+do.*?'
            r'DoHit\(\s*defender\s*\).*?'
            r'GetVisList_Next\(\s*\)', body, re.DOTALL)),
    }
    verified = all(checks.values())
    return {
        "verified": verified,
        "checks": checks,
        "summary": (
            "Attack_Range uses the script argument as visible-enemy search range, "
            "charges the configured resource only when at least one defender exists, "
            "then applies DoHit to each visible enemy"
            if verified else None
        ),
    }


def _read_numeric_csv(path: Path) -> list[list[int]]:
    rows = []
    for index, row in enumerate(csv.reader(io.StringIO(_decode(path))), 1):
        if not row:
            continue
        try:
            rows.append([int(value.strip()) for value in row])
        except ValueError as error:
            raise ValueError(
                f"{path.name} row {index} contains non-integer attribute data"
            ) from error
    return rows


def _enemy_attribute_structure(server_dat: Path) -> dict:
    enemy_dir = server_dat / "Enemy"
    enemy_path = enemy_dir / "Enemy_Data.csv"
    table_paths = sorted(enemy_dir.glob("EN_Att[0-9][0-9].csv"))
    if not enemy_path.is_file() or not table_paths:
        return {
            "available": False,
            "status": "not_present",
            "semantic_names_verified": False,
            "runtime_ready": False,
        }

    table_levels = {}
    table_meta = {}
    source_hashes = {"Enemy/Enemy_Data.csv": _sha(enemy_path)}
    for path in table_paths:
        match = re.fullmatch(r"EN_Att([0-9]{2})\.csv", path.name, re.IGNORECASE)
        if match is None:
            continue
        table_id = int(match.group(1))
        rows = _read_numeric_csv(path)
        if not rows:
            continue
        widths = sorted({len(row) for row in rows})
        levels = {row[0] for row in rows if row}
        table_levels[table_id] = levels
        table_meta[str(table_id)] = {
            "rows": len(rows),
            "widths": widths,
            "min_level": min(levels),
            "max_level": max(levels),
        }
        source_hashes[f"Enemy/{path.name}"] = _sha(path)

    raw_enemy = list(csv.reader(io.StringIO(_decode(enemy_path))))
    enemy_rows = [row for row in raw_enemy if row]
    widths = sorted({len(row) for row in enemy_rows})
    max_width = max(widths, default=0)

    selector_columns = []
    for column in range(max_width):
        values = []
        valid = True
        for row in enemy_rows:
            if column >= len(row) or not row[column].strip():
                valid = False
                break
            try:
                values.append(int(row[column].strip()))
            except ValueError:
                valid = False
                break
        unique = sorted(set(values))
        if (valid and len(unique) >= 2 and
                all(value in table_levels for value in unique)):
            selector_columns.append((column, unique))

    candidates = []
    for selector_column, selector_values in selector_columns:
        for level_column in range(max_width):
            if level_column == selector_column:
                continue
            matched = 0
            unmatched = []
            considered = 0
            valid_numeric = True
            for row_index, row in enumerate(enemy_rows, 1):
                try:
                    selector = int(row[selector_column].strip())
                    level = int(row[level_column].strip())
                except (ValueError, IndexError):
                    valid_numeric = False
                    break
                if selector not in table_levels:
                    continue
                considered += 1
                if level in table_levels[selector]:
                    matched += 1
                elif len(unmatched) < 16:
                    unmatched.append({
                        "row": row_index,
                        "enemy_id": row[0] if row else "",
                        "selector": selector,
                        "level_candidate": level,
                    })
            if not valid_numeric or considered == 0:
                continue
            ratio = matched / considered
            if ratio >= 0.90:
                candidates.append({
                    "selector_column": selector_column,
                    "level_column": level_column,
                    "selector_values": selector_values,
                    "matched_rows": matched,
                    "considered_rows": considered,
                    "match_ratio": round(ratio, 6),
                    "unmatched_examples": unmatched,
                })

    candidates.sort(
        key=lambda item: (
            -item["match_ratio"],
            -len(item["selector_values"]),
            item["selector_column"],
            item["level_column"],
        )
    )
    return {
        "available": True,
        "status": "candidate_only",
        "enemy_rows": len(enemy_rows),
        "enemy_widths": widths,
        "attribute_tables": table_meta,
        "candidate_pairs": candidates[:8],
        "semantic_names_verified": False,
        "runtime_ready": False,
        "sources_sha256": dict(sorted(source_hashes.items())),
        "notes": [
            "Candidate pairs are structural correlations only; they do not prove column semantics.",
            "The eight EN_Att payload columns remain unnamed until loader/schema evidence is found.",
            "Sentinel or unmatched rows are retained as evidence and must not be silently coerced.",
        ],
    }


def _read_table(path: Path):
    text = _decode(path)
    rows = {}
    for index, row in enumerate(csv.reader(io.StringIO(text)), 1):
        if not row or not row[0].strip():
            continue
        try:
            skill_id = int(row[0].strip())
        except ValueError as error:
            raise ValueError(f"skill.csv row {index} has invalid SkillId") from error
        if skill_id <= 0 or len(row) < 3:
            raise ValueError(f"skill.csv row {index} is malformed")
        rows.setdefault(skill_id, []).append({
            "row": index,
            "name": row[1],
            "description": row[2],
            # Unknown numeric columns are deliberately preserved by index rather
            # than assigned semantics without source proof.
            "columns": {str(i): value for i, value in enumerate(row[3:], 3)},
        })
    return rows


def compile_skill_evidence(server_dat: Path, only_ids: set[int] | None = None) -> dict:
    server_dat = Path(server_dat).resolve()
    skill_dir = server_dat / "Skill"
    table_path = skill_dir / "skill.csv"
    common_path = skill_dir / "skillcommon.txt"
    if not table_path.is_file() or not common_path.is_file():
        raise ValueError("original Skill/skill.csv and Skill/skillcommon.txt are required")
    if only_ids is not None and (
        not only_ids or any(not isinstance(value, int) or value <= 0 for value in only_ids)
    ):
        raise ValueError("requested SkillIds must be positive integers")

    table = _read_table(table_path)
    scripts = {}
    for path in sorted(skill_dir.iterdir(), key=lambda value: value.name.casefold()):
        match = _SCRIPT.fullmatch(path.name)
        if not path.is_file() or match is None:
            continue
        skill_id = int(match.group(1)) * 1000 + int(match.group(2))
        if skill_id in scripts:
            raise ValueError(f"duplicate SKC script identity for SkillId {skill_id}")
        scripts[skill_id] = path

    available = set(table) | set(scripts)
    selected = sorted(available if only_ids is None else available & only_ids)
    missing = [] if only_ids is None else sorted(only_ids - available)
    common = _decode(common_path)

    definitions = []
    source_hashes = {
        "Skill/skill.csv": _sha(table_path),
        "Skill/skillcommon.txt": _sha(common_path),
    }
    for skill_id in selected:
        script = scripts.get(skill_id)
        if script is not None:
            source_hashes[f"Skill/{script.name}"] = _sha(script)
        definitions.append({
            "skill_id": skill_id,
            "table_rows": table.get(skill_id, []),
            "script": script.name if script else None,
            "script_facts": _script_facts(script) if script else None,
            "source_status": (
                "table_and_script" if skill_id in table and script is not None
                else "table_only" if skill_id in table else "script_only"
            ),
        })

    return {
        "version": 1,
        "kind": "old_skill_source_evidence_not_runtime",
        "status": "partial" if missing else "pass",
        "requested_skill_ids": sorted(only_ids) if only_ids is not None else None,
        "missing_requested_skill_ids": missing,
        "definitions": definitions,
        "hit_formula": _hit_formula(common),
        "damage_formula": _damage_formula(common),
        "range_semantics": _range_semantics(common),
        "enemy_attribute_structure": _enemy_attribute_structure(server_dat),
        "sources_sha256": dict(sorted(source_hashes.items())),
        "runtime_rules_generated": False,
        "combat_ready": False,
        "notes": [
            "SkillId is joined to SKCxxJyyy by xx*1000+yyy only when that source file exists.",
            "Unknown skill.csv columns remain indexed raw evidence; semantics are not guessed.",
            "DP/DR semantic column mapping and durable MP/Will ownership must be verified before runtime activation.",
            "Enemy/EN_Att relationships are emitted as structural candidates only and never promoted to runtime rules.",
        ],
    }
