"""Tests for fail-closed Old Dat/Skill evidence compilation."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.skill_evidence import compile_skill_evidence


COMMON = r'''
function checkHR(defender)
    HRDiff = GetData("HR") - GetTargetData(defender, "DR")
    LvDiff = ( GetData("Level") - GetTargetData(defender, "Level") ) * 2
    HRFinal = HRDiff + LvDiff + 60
    if HRFinal < 30 then
        HRFinal = 30
    end
    if random(100) <= HRFinal then
        return 1
    else
        AttackDefender_Miss()
        return 0
    end
end
'''

SKILL_23102 = r'''
function start()
    MinHit=2
    MinHitDelta=3
    consume = "Will"
    APcount = "AP"
    DPcount = "DP"
    varyDelta = 20
    effect = GetPara(skillLv)
    conTotal = GetConTotal(skillLv)
    Attack()
end
function GetPara(lv)
    if lv == 1 then
        return 25
    elseif lv == 2 then
        return 37
    else
        return 85
    end
end
function GetConTotal(lv)
    if lv == 1 then
        return 14
    elseif lv == 2 then
        return 17
    else
        return 26
    end
end
#include "skillcommon.txt"
'''

SKILL_23103 = r'''
function start()
    consume = "MP"
    APcount = "AP"
    DPcount = "DP"
    varyDelta = 20
    effect = GetPara(skillLv)
    conTotal = GetConTotal(skillLv)
    Attack_Range(125)
end
function GetPara(lv)
    if lv == 1 then
        return 12
    else
        return 33
    end
end
function GetConTotal(lv)
    if lv == 1 then
        return 63
    else
        return 168
    end
end
#include "skillcommon.txt"
'''


class SkillSourceEvidenceTests(unittest.TestCase):
    def fixture(self, root: Path) -> Path:
        dat = root/'Dat'
        skill = dat/'Skill'
        skill.mkdir(parents=True)
        rows = [
            '23102,Skill A,Single target,150,0,0,28,0,2,0,0,1,0,8,2,Use,Hit,,1,Effect',
            '23103,Skill B,Area target,150,0,63,0,3,2,0,0,1,0,9,1,Use,Hit,,1,Effect',
        ]
        (skill/'skill.csv').write_bytes(('\n'.join(rows)+'\n').encode('cp950'))
        (skill/'skillcommon.txt').write_bytes(COMMON.encode('ascii'))
        (skill/'SKC23J102.txt').write_bytes(SKILL_23102.encode('ascii'))
        (skill/'SKC23J103.txt').write_bytes(SKILL_23103.encode('ascii'))
        return dat

    def test_script_join_resource_range_levels_and_direct_hit_formula(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            result = compile_skill_evidence(dat,{23102,23103})
            self.assertEqual(result['status'],'pass')
            self.assertTrue(result['hit_formula']['verified'])
            self.assertEqual(
                result['hit_formula']['formula'],
                'HRFinal=max(30,attacker.HR-defender.DR+'
                '(attacker.Level-defender.Level)*2+60); hit=random(100)<=HRFinal')
            rows={entry['skill_id']:entry for entry in result['definitions']}
            single=rows[23102]['script_facts']
            area=rows[23103]['script_facts']
            self.assertEqual(rows[23102]['script'],'SKC23J102.txt')
            self.assertEqual(single['resource'],'Will')
            self.assertEqual(single['ap_source'],'AP')
            self.assertEqual(single['dp_source'],'DP')
            self.assertEqual(single['attack_calls'],[{'function':'Attack','argument':None}])
            self.assertEqual(single['effect_by_level'],{'1':25,'2':37,'else':85})
            self.assertEqual(single['consume_by_level'],{'1':14,'2':17,'else':26})
            self.assertEqual(area['resource'],'MP')
            self.assertEqual(area['attack_calls'],
                             [{'function':'Attack_Range','argument':125}])
            self.assertEqual(area['consume_by_level'],{'1':63,'else':168})
            self.assertFalse(result['runtime_rules_generated'])
            self.assertFalse(result['combat_ready'])

    def test_requested_missing_skill_id_is_explicit_partial_not_nearest_match(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            result = compile_skill_evidence(dat,{99001})
            self.assertEqual(result['status'],'partial')
            self.assertEqual(result['requested_skill_ids'],[99001])
            self.assertEqual(result['missing_requested_skill_ids'],[99001])
            self.assertEqual(result['definitions'],[])

    def test_unknown_common_formula_is_not_promoted_to_verified(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            (dat/'Skill/skillcommon.txt').write_text(
                'function checkHR(defender) return 1 end\n',encoding='ascii')
            result = compile_skill_evidence(dat,{23102})
            self.assertFalse(result['hit_formula']['verified'])
            self.assertIsNone(result['hit_formula']['formula'])

    def test_deterministic_output_and_source_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            first=compile_skill_evidence(dat,None)
            second=compile_skill_evidence(dat,None)
            self.assertEqual(first,second)
            self.assertEqual([x['skill_id'] for x in first['definitions']],[23102,23103])
            self.assertIn('Skill/skill.csv',first['sources_sha256'])
            self.assertIn('Skill/SKC23J103.txt',first['sources_sha256'])


if __name__=='__main__':
    unittest.main()
