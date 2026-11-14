#!/usr/bin/env python3
"""Fail-closed audit of Paradise ACT event callback semantics."""
from __future__ import annotations
import argparse, json, re
from pathlib import Path

FUNC_START=re.compile(r"^\.text:([0-9A-Fa-f]{8}).*?(\?\S+) proc (?:near|far)\s*$")
FUNC_END=re.compile(r"^\.text:[0-9A-Fa-f]{8}.*?(\?\S+) endp\s*$")

def parse_functions(lines):
    rows=[]; cur=None
    for line in lines:
        m=FUNC_START.match(line.strip())
        if m:
            cur={"address":"0x"+m.group(1).upper(),"symbol":m.group(2),"lines":[line]}
            rows.append(cur); continue
        if cur is not None:
            cur["lines"].append(line)
            e=FUNC_END.match(line.strip())
            if e and e.group(1)==cur["symbol"]: cur=None
    return rows

def block(functions,symbol):
    rows=[x for x in functions if x["symbol"]==symbol]
    if len(rows)!=1: raise ValueError(f"expected one {symbol}, got {len(rows)}")
    return rows[0]

def has_all(text,*needles): return all(x in text for x in needles)

def audit(lines):
    funcs=parse_functions(lines)
    cel=block(funcs,"?SetEventCallback@CELxnObj@@UAEXP6AXPADPAX@Z1@Z")
    xn=block(funcs,"?SetEventCallback@xnAction@@QAEXP6AXPADPAX@Z1@Z")
    set_action=block(funcs,"?SetAction@xnAction@@QAEXPAD@Z")
    update=block(funcs,"?Update@xnAction@@QAE?AW4ActionStatus@@XZ")
    battle=block(funcs,"?BattleEffSFXCallback@CBattleSys@@SAXPADPAX@Z")
    char=block(funcs,"?AttEffAxtionCallback@CSGOCharDrawLogic@@KAXPADPAX@Z")
    enemy=block(funcs,"?EnemEffAxtionCallback@CSGOEnemDrawLogic@@KAXPADPAX@Z")
    pet=block(funcs,"?PetEffAxtionCallback@CSGOPetDrawLogic@@KAXPADPAX@Z")
    sound=block(funcs,"?RandomPlaySound@CSGOMap@@QAEXPAD@Z")
    T=lambda x:"\n".join(x["lines"])

    callback_store=has_all(T(cel),"?SetEventCallback@xnAction@@QAEXP6AXPADPAX@Z1@Z")
    xn_store=has_all(T(xn),"mov     [ecx+28h], eax","mov     [ecx+24h], edx")
    frame0=has_all(T(set_action),"mov     edx, [edi+10h]","mov     eax, [edx]","call    ecx",
                   "mov     eax, [esi+10h]","mov     ecx, [edi+0Ch]")
    enter_frame=has_all(T(update),"inc     edx","mov     ecx, [edi+10h]","call    ecx",
                        "mov     ecx, [edi+0Ch]")

    battle_ok=has_all(T(battle),'offset $SG161356 ; "*"','offset $SG161357 ; "+"',
                      "?SetHurtEffEvent@CSGOChar@@QAEXXZ","?RandomPlaySound@CSGOMap@@QAEXPAD@Z")
    char_ok=has_all(T(char),'offset $SG169888 ; "+"','offset $SG169913 ; "-"',
                    'offset $SG170003 ; "$"','offset $SG170022 ; "#"',
                    "??0CBulletObj@@QAE@XZ","?SetRidingMode@CSGOChar@@QAEXE@Z",
                    "?RandomPlaySound@CSGOMap@@QAEXPAD@Z")
    enemy_ok=has_all(T(enemy),'offset $SG157980 ; "+"',
                     "?SetReciveEffect@CBattleSys@@QAEHPAD00@Z",
                     "?AppendDamge@CBattleSys@@QAEXHHPAG@Z",
                     "?AppendBlock@CBattleSys@@QAEXHHPAG@Z",
                     "?RandomPlaySound@CSGOMap@@QAEXPAD@Z")
    pet_ok=has_all(T(pet),'offset $SG157392 ; "+"',
                   "?SetReciveEffect@CBattleSys@@QAEHPAD00@Z",
                   "?AppendDamge@CBattleSys@@QAEXHHPAG@Z",
                   "?AppendBlock@CBattleSys@@QAEXHHPAG@Z",
                   "?RandomPlaySound@CSGOMap@@QAEXPAD@Z")
    sound_ok=has_all(T(sound),"__imp__strtok","__imp__rand",
                     '"Audio\\\\SE\\\\%s.WAV"',"PlayGlobalSource@CELSound")

    verified=all([callback_store,xn_store,frame0,enter_frame,battle_ok,char_ok,enemy_ok,pet_ok,sound_ok])
    return {
      "version":1,"verified":verified,
      "addresses":{x["symbol"]:x["address"] for x in [cel,xn,set_action,update,battle,char,enemy,pet,sound]},
      "timing":{"frame0_callback_on_set_action":frame0,"callback_on_enter_frame":enter_frame},
      "callbacks":{
        "battle":{"tokens":["*","+"],"special_hurt_cue":battle_ok,"default_audio":battle_ok},
        "character":{"tokens":["+","-","$","#"],"plus_receive_damage_visual":char_ok,
                     "minus_projectile_visual":char_ok,"dollar_riding_control":char_ok,
                     "hash_consumed":char_ok,"default_audio":char_ok},
        "enemy":{"tokens":["+"],"plus_receive_damage_visual":enemy_ok,"default_audio":enemy_ok},
        "pet":{"tokens":["+"],"plus_receive_damage_visual":pet_ok,"default_audio":pet_ok}},
      "random_play_sound":{"comma_newline_tokenize_random_one_and_play_audio_se":sound_ok},
      "callback_storage_verified":callback_store and xn_store,
    }

def main():
    ap=argparse.ArgumentParser(description=__doc__); ap.add_argument("lst",type=Path); ap.add_argument("--output",type=Path)
    a=ap.parse_args()
    if not a.lst.is_file(): ap.error("missing lst")
    result=audit(a.lst.read_text(encoding="utf-8",errors="ignore").splitlines())
    if a.output:
        a.output.parent.mkdir(parents=True,exist_ok=True)
        a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps(result,ensure_ascii=False,indent=2))
    return 0 if result["verified"] else 1
if __name__=="__main__": raise SystemExit(main())
