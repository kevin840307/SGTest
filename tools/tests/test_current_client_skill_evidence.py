"""Tests for fail-closed current-client SkillData evidence."""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_converter.content.current_client_skill_evidence import (
    compile_current_client_skill_evidence,
)


SKILL_99001='''[gd_resource type="Resource" script_class="SkillData" format=3]

[sub_resource type="Resource" id="Resource_global_aoe"]
level = 1
cast_range = 0
aoe_radius = 900
cost_mp = 1
base_damage = 1000
damage_multiplier = 100
cooldown = 0
tick_count = 1

[resource]
id = 99001
skill_name = "Stellar Shock"
description = "self centered screen area"
skill_type = 7
damage_type = 1
icon_id = 42
fx_use = "Skill_Use_100"
fx_hit = "Skill_Hit_100"
fx_extra = ""
special_skill = "screen-area"
'''


class CurrentClientSkillEvidenceTests(unittest.TestCase):
    def fixture(self,root:Path)->Path:
        client=root/"client"
        skills=client/"Skills"
        skills.mkdir(parents=True)
        (skills/"99001.tres").write_text(SKILL_99001,encoding="utf-8")
        game=client/"Assets"/"GameInfo"
        game.mkdir(parents=True)
        (game/"Skill.csv").write_text(
            "技能編號,加最大HP比例,加命中比例\n"
            "23102,0,5\n",
            encoding="utf-8",
        )
        return client

    def test_99001_fields_and_activation_blockers_are_source_facts(self):
        with tempfile.TemporaryDirectory() as directory:
            client=self.fixture(Path(directory))
            result=compile_current_client_skill_evidence(client,{99001})
            self.assertEqual(result["status"],"pass")
            self.assertEqual(result["source_family"],"godot_current_client_skilldata")
            self.assertEqual(result["percentage_rows"],[])
            self.assertEqual(len(result["definitions"]),1)
            skill=result["definitions"][0]
            self.assertEqual(skill["skill_id"],99001)
            self.assertEqual(skill["skill_name"],"Stellar Shock")
            self.assertEqual(skill["skill_type"],7)
            self.assertEqual(skill["damage_type"],1)
            self.assertEqual(skill["special_skill"],"screen-area")
            self.assertEqual(skill["levels"],[{
                "level":1,"cast_range":0,"aoe_radius":900,
                "cost_hp":0,"cost_mp":1,"cost_sp":0,
                "base_damage":1000,"damage_multiplier":100,
                "cooldown":0,"duration":0,"tick_count":1,
                "max_hp_bonus":0,"max_mp_bonus":0,"max_sp_bonus":0,
                "atk_bonus":0,"def_bonus":0,"matk_bonus":0,"mdef_bonus":0,
                "hit_bonus":0,"flee_bonus":0,"luk_bonus":0,"aspd_bonus":0,
                "move_speed_bonus":0,"crit_rate_bonus":0,"crit_damage_bonus":0,
            }])
            self.assertEqual(result["activation_blockers"],[
                "authoritative_aoe_targeting_required",
                "damage_formula_runtime_mapping_required",
                "durable_mp_owner_required",
            ])
            self.assertFalse(result["runtime_rules_generated"])
            self.assertFalse(result["combat_ready"])
            self.assertIn("Skills/99001.tres",result["sources_sha256"])
            self.assertIn("Assets/GameInfo/Skill.csv",result["sources_sha256"])

    def test_missing_id_is_partial_and_never_nearest_matches(self):
        with tempfile.TemporaryDirectory() as directory:
            client=self.fixture(Path(directory))
            result=compile_current_client_skill_evidence(client,{9900})
            self.assertEqual(result["status"],"partial")
            self.assertEqual(result["missing_requested_skill_ids"],[9900])
            self.assertEqual(result["definitions"],[])

    def test_duplicate_levels_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            client=self.fixture(Path(directory))
            path=client/"Skills"/"99001.tres"
            path.write_text(
                SKILL_99001.replace(
                    "[resource]",
                    "[sub_resource type=\"Resource\" id=\"duplicate\"]\n"
                    "level = 1\nbase_damage = 1\n\n[resource]",
                ),
                encoding="utf-8",
            )
            with self.assertRaises(ValueError):
                compile_current_client_skill_evidence(client,{99001})

    def test_percentage_rows_are_kept_in_same_source_family_without_inference(self):
        with tempfile.TemporaryDirectory() as directory:
            client=self.fixture(Path(directory))
            csv_path=client/"Assets"/"GameInfo"/"Skill.csv"
            csv_path.write_text(
                "技能編號,加最大HP比例,加命中比例\n"
                "99001,0,12\n",
                encoding="utf-8",
            )
            result=compile_current_client_skill_evidence(client,{99001})
            self.assertEqual(result["percentage_rows"][0]["skill_id"],99001)
            self.assertEqual(
                result["percentage_rows"][0]["columns"]["加命中比例"],"12")


if __name__=="__main__":
    unittest.main()
