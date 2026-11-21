"""Tests for fail-closed Old Dat/Skill evidence compilation."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_converter.content.skill_evidence import compile_skill_evidence


COMMON = r'''
function DoHit_PerDefender(defender)
    damage_perhit = GetTotalDamage(defender)
    damage_perhit = damage_perhit / GetSkillHitCount()
end
function GetMinDamage()
    return random(MinHitDelta)
end
function GetTotalDamage(defender)
    damage = 0
    DefLv = GetTargetData(defender, "Level")
    AttLv = GetData("Level")
    LvDif = DefLv - AttLv
    if LvDif > 9 then
        damage = GetMinDamage()
    else
        attackerAP=GetData(APcount) + effect
        attackerAP= attackerAP+ attackerAP * (varyDelta - random(varyDelta*2)) / 100
        defenderDP=GetTargetData(defender,DPcount)
        damage = attackerAP-defenderDP
        if damage < MinHit then
            damage = GetMinDamage()
        elseif LvDif > 0 then
            damage = damage * (10 - LvDif) / 10
        end
    end
    return damage
end
function DoHit_PerHitCount(defender, damage_perhit)
    totaldamage = damage_perhit
    if checkHR(defender) ~= 0 then
        totaldamage = totaldamage + totaldamage * (varyDelta - random(varyDelta*2)) / 100
        if totaldamage < MinHit then
            totaldamage = GetMinDamage()
        end
        AttackDefender(totaldamage, 0)
    else
        AttackDefender_Miss()
    end
    return totaldamage
end
function calhp_final(defender, totaldamage)
    hp=GetTargetData(defender,"HP")
    if hp>0 then
        if hp<=totaldamage then
            totaldamage = hp
        end
    end
end
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
function Attack_Range(AttackRange)
    if GetData(consume) >= conTotal then
        hasdefender, defender = GetVisEnemyList_First(AttackRange)
        if hasdefender ~= 0 then
            ModifyData(consume,-conTotal)
        end
        while hasdefender ~= 0 do
            DoHit(defender)
            hasdefender, defender = GetVisList_Next()
        end
    end
end
'''


CURE_COMMON = r'''
function Cure()
    if GetData(consume) >= conTotal then
        ModifyData(consume,-conTotal)
        Cure_value = effect + effect * (varyDelta - random(varyDelta*2)) / 100
        ModifyTargetData(defender,boost,Cure_value)
        SetSkillDefender(defender)
    end
    QuestEnd()
end
function Cure_Range(CureRange)
    if GetData(consume) >= conTotal then
        ModifyData(consume,-conTotal)
        Cure_value = effect + effect * (varyDelta - random(varyDelta*2)) / 100
        ModifyTargetData(defender,boost,Cure_value)
        SetSkillDefender(defender)
        x, y = GetTargetXY(defender)
        mpid = GetData("MapID")
        hasdefender, teammember = GetTeamList_First()
        while hasdefender ~= 0 do
            if IsTargetCloseTo(teammember, x, y, mpid, CureRange)==0 then
                Cure_value = effect + effect * (varyDelta - random(varyDelta*2)) / 100
                ModifyTargetData(teammember,boost,Cure_value)
                SetSkillDefender(teammember)
            end
            hasdefender, teammember = GetTeamList_Next()
        end
    end
    QuestEnd()
end
'''

COMM_TEAM = r'''
function IsTargetCloseTo(object, posx, posy, mapid, range)
    if GetTargetData(object, "MapID")~=mapid then
        return 0
    else
        x, y = GetTargetXY(object)
        disx = x-posx
        if disx<0 then
            disx = 0-disx
        end
        disy = y-posy
        if disy<0 then
            disy = 0-disy
        end
        if disx > range or disy>range then
            return 0
        else
            return 1
        end
    end
end
'''

SKILL_28101 = r'''
function start()
    consume = "MP"
    boost = "HP"
    varyDelta = 10
    defender = GetTargetPtr()
    if defender == 0 then
        defender = GetCallerPtr()
    end
    effect = GetPara(skillLv)
    conTotal = GetConTotal(skillLv)
    Cure()
end
function GetPara(lv)
    if lv == 1 then
        return 11
    elseif lv == 2 then
        return 15
    elseif lv == 3 then
        return 21
    elseif lv == 4 then
        return 28
    else
        return 36
    end
end
function GetConTotal(lv)
    if lv == 1 then
        return 11
    elseif lv == 2 then
        return 15
    elseif lv == 3 then
        return 21
    elseif lv == 4 then
        return 28
    else
        return 36
    end
end
#include "curecommon.txt"
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
            '28101,Heal,Target or self heal,250,0,16,0,0,3,0,0,2,0,45,1,Use,Heal,,0,0',
        ]
        (skill/'skill.csv').write_bytes(('\n'.join(rows)+'\n').encode('cp950'))
        (skill/'skillcommon.txt').write_bytes(COMMON.encode('ascii'))
        (skill/'curecommon.txt').write_bytes(CURE_COMMON.encode('ascii'))
        (skill/'CommTeam.txt').write_bytes(COMM_TEAM.encode('ascii'))
        (skill/'SKC23J102.txt').write_bytes(SKILL_23102.encode('ascii'))
        (skill/'SKC23J103.txt').write_bytes(SKILL_23103.encode('ascii'))
        (skill/'SKC28J101.txt').write_bytes(SKILL_28101.encode('ascii'))
        enemy = dat/'Enemy'
        enemy.mkdir()
        # Columns 3/4 are deliberately shaped like the real Old candidate:
        # level candidate + EN_Att selector.  Semantics remain unverified.
        enemy_rows = [
            '1,1,EnemyA,1,1,X',
            '2,2,EnemyB,2,1,X',
            '3,3,EnemyC,3,1,X',
            '4,4,EnemyD,4,1,X',
            '5,5,EnemyE,5,1,X',
            '6,6,EnemyF,1,2,X',
            '7,7,EnemyG,2,2,X',
            '8,8,EnemyH,3,2,X',
            '9,9,EnemyI,4,2,X',
            '10,10,EnemyJ,5,2,X',
            '11,11,EnemySentinel,65535,2,X',
        ]
        (enemy/'Enemy_Data.csv').write_text('\n'.join(enemy_rows)+'\n',encoding='ascii')
        (enemy/'EN_Att01.csv').write_text(
            '1,51,5,0,5,0,22,6,8\n'
            '2,136,9,0,9,0,29,12,32\n'
            '3,222,13,0,13,0,36,18,41\n'
            '4,310,18,0,18,0,43,24,64\n'
            '5,420,29,1,29,1,50,30,79\n',
            encoding='ascii')
        (enemy/'EN_Att02.csv').write_text(
            '1,77,5,0,5,0,22,6,10\n'
            '2,204,9,0,9,0,29,12,39\n'
            '3,333,13,0,13,0,36,18,50\n'
            '4,465,18,0,18,0,43,24,77\n'
            '5,630,29,1,29,1,50,30,95\n',
            encoding='ascii')
        return dat

    def test_script_join_resource_range_levels_and_direct_hit_formula(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            result = compile_skill_evidence(dat,{23102,23103,28101})
            self.assertEqual(result['status'],'pass')
            self.assertTrue(result['hit_formula']['verified'])
            self.assertTrue(result['damage_formula']['verified'])
            self.assertTrue(result['range_semantics']['verified'])
            self.assertIn('AP+effect',result['damage_formula']['summary'])
            self.assertIn('visible-enemy',result['range_semantics']['summary'])
            structure=result['enemy_attribute_structure']
            self.assertEqual(structure['status'],'candidate_only')
            self.assertFalse(structure['semantic_names_verified'])
            self.assertFalse(structure['runtime_ready'])
            self.assertTrue(structure['candidate_pairs'])
            best=structure['candidate_pairs'][0]
            self.assertEqual(best['selector_column'],4)
            self.assertEqual(best['level_column'],3)
            self.assertEqual(best['selector_values'],[1,2])
            self.assertEqual(best['matched_rows'],10)
            self.assertEqual(best['considered_rows'],11)
            self.assertGreaterEqual(best['match_ratio'],0.90)
            self.assertEqual(best['unmatched_examples'][0]['level_candidate'],65535)
            self.assertEqual(
                result['hit_formula']['formula'],
                'HRFinal=max(30,attacker.HR-defender.DR+'
                '(attacker.Level-defender.Level)*2+60); hit=random(100)<=HRFinal')
            rows={entry['skill_id']:entry for entry in result['definitions']}
            single=rows[23102]['script_facts']
            area=rows[23103]['script_facts']
            heal=rows[28101]['script_facts']
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
            self.assertEqual(heal['resource'],'MP')
            self.assertEqual(heal['boost'],'HP')
            self.assertEqual(heal['vary_delta_percent'],10)
            self.assertEqual(heal['support_calls'],
                             [{'function':'Cure','argument':None}])
            self.assertEqual(
                heal['effect_by_level'],
                {'1':11,'2':15,'3':21,'4':28,'else':36})
            self.assertEqual(
                heal['consume_by_level'],
                {'1':11,'2':15,'3':21,'4':28,'else':36})
            self.assertTrue(heal['includes_curecommon'])
            cure=result['cure_formula']
            self.assertTrue(cure['direct_verified'])
            self.assertEqual(
                cure['direct_formula'],
                'heal=effect + effect*(varyDelta-random(varyDelta*2))/100')
            self.assertTrue(cure['range_verified'])
            self.assertFalse(cure['range_runtime_ready'])
            self.assertIn('contradiction',cure['range_note'])
            self.assertIn('Skill/curecommon.txt',result['sources_sha256'])
            self.assertIn('Skill/CommTeam.txt',result['sources_sha256'])
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
            self.assertFalse(result['damage_formula']['verified'])
            self.assertFalse(result['range_semantics']['verified'])

    def test_deterministic_output_and_source_hashes(self):
        with tempfile.TemporaryDirectory() as directory:
            dat = self.fixture(Path(directory))
            first=compile_skill_evidence(dat,None)
            second=compile_skill_evidence(dat,None)
            self.assertEqual(first,second)
            self.assertEqual(
                [x['skill_id'] for x in first['definitions']],
                [23102,23103,28101])
            self.assertIn('Skill/skill.csv',first['sources_sha256'])
            self.assertIn('Skill/SKC23J103.txt',first['sources_sha256'])


if __name__=='__main__':
    unittest.main()
