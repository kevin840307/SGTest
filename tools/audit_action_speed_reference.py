#!/usr/bin/env python3
"""Audit Paradise.exe.lst for movement-speed vs ACT action-speed separation."""
from __future__ import annotations
import argparse, json, re
from pathlib import Path

FUNC_START = re.compile(r"^\.text:([0-9A-Fa-f]{8}).*?(\?\S+) proc (?:near|far)\s*$")
FUNC_END = re.compile(r"^\.text:[0-9A-Fa-f]{8}.*?(\?\S+) endp\s*$")
ADDR = re.compile(r"^\.text:([0-9A-Fa-f]{8})")
PUSH_IMMEDIATE = re.compile(r"\bpush\s+([0-9A-F]+)h\b", re.I)
CALL_VT_24 = re.compile(r"\bcall\s+dword ptr \[[^\]]+\+24h\]", re.I)
CALL_VT_2C = re.compile(r"\bcall\s+dword ptr \[[^\]]+\+2Ch\]", re.I)

def parse_functions(lines):
    result=[]; current=None
    for index,line in enumerate(lines):
        start=FUNC_START.match(line.strip())
        if start:
            current={"address":int(start.group(1),16),"address_hex":"0x"+start.group(1).upper(),
                     "symbol":start.group(2),"start_line":index,"lines":[line]}
            result.append(current); continue
        if current is not None:
            current["lines"].append(line)
            end=FUNC_END.match(line.strip())
            if end and end.group(1)==current["symbol"]:
                current["end_line"]=index; current=None
    return result

def find_function(functions,symbol):
    rows=[r for r in functions if r["symbol"]==symbol]
    if len(rows)!=1: raise ValueError(f"expected exactly one function {symbol}, got {len(rows)}")
    return rows[0]

def audit_movement_setter(functions):
    row=find_function(functions,"?SetSpeed@CSGOChar@@QAEXH@Z")
    text="\n".join(row["lines"])
    stores=bool(re.search(r"mov\s+\[eax\+17Ch\],\s*ecx",text,re.I))
    calls=("SetActionSpeed" in text or "?SetSpeed@xnAction@@" in text)
    return {"address":row["address_hex"],"stores_reference_field_0x17c":stores,
            "calls_action_speed":calls,"movement_and_action_speed_separate":stores and not calls}

def audit_action_speed_wrapper(functions):
    row=find_function(functions,"?SetActionSpeed@CELxnObj@@UAEXH@Z")
    text="\n".join(row["lines"])
    return {"address":row["address_hex"],
            "calls_xn_action_set_speed":"?SetSpeed@xnAction@@QAEXH@Z" in text}

def vtable_slot(lines,vtable_symbol,method_symbol):
    base=method=None
    marker=f"{vtable_symbol} dd offset"
    for line in lines:
        s=line.strip()
        if marker in s:
            m=re.match(r"^\.rdata:([0-9A-Fa-f]{8})",s)
            if m: base=int(m.group(1),16)
        if base is not None and f"dd offset {method_symbol}" in s:
            m=re.match(r"^\.rdata:([0-9A-Fa-f]{8})",s)
            if m: method=int(m.group(1),16); break
    if base is None or method is None: raise ValueError("vtable evidence not found")
    return {"vtable_address":f"0x{base:08X}","method_address":f"0x{method:08X}",
            "slot_offset":method-base,"slot_offset_hex":f"0x{method-base:X}"}

def action_speed_immediate_calls(functions):
    """Find InitAction(+0x24) -> push imm -> SetActionSpeed(+0x2C) sequences."""
    out=[]
    for f in functions:
        lines=f["lines"]
        for i,line in enumerate(lines):
            immediate=PUSH_IMMEDIATE.search(line)
            if not immediate: continue
            # Keep the virtual-layout evidence local to the same short sequence,
            # not merely somewhere else in the function.
            has_init=any(CALL_VT_24.search(lines[k]) for k in range(max(0,i-18),i))
            if not has_init: continue
            call_i=None
            for j in range(i+1,min(i+12,len(lines))):
                if CALL_VT_2C.search(lines[j]): call_i=j; break
            if call_i is None: continue
            a=ADDR.match(line.strip()); c=ADDR.match(lines[call_i].strip())
            out.append({"function":f["symbol"],"function_address":f["address_hex"],
                        "speed":int(immediate.group(1),16),
                        "push_speed_address":"0x"+a.group(1).upper() if a else None,
                        "virtual_2c_call_address":"0x"+c.group(1).upper() if c else None,
                        "nearby_init_action_virtual_24":True})
    seen={}
    for row in out:
        seen[(row["function"],row["push_speed_address"],row["virtual_2c_call_address"])]=row
    return list(seen.values())

def audit(lines):
    funcs=parse_functions(lines)
    movement=audit_movement_setter(funcs)
    wrapper=audit_action_speed_wrapper(funcs)
    cddx=vtable_slot(lines,"??_7CDDxDrawObj@@6B@","?SetActionSpeed@CELxnObj@@UAEXH@Z")
    xn=vtable_slot(lines,"??_7CELxnObj@@6B@","?SetActionSpeed@CELxnObj@@UAEXH@Z")
    calls=action_speed_immediate_calls(funcs)
    speeds=sorted({row["speed"] for row in calls})
    verified=(movement["movement_and_action_speed_separate"] and wrapper["calls_xn_action_set_speed"]
              and cddx["slot_offset"]==0x2C and xn["slot_offset"]==0x2C and bool(calls))
    return {"version":2,"movement_speed":movement,"action_speed_wrapper":wrapper,
            "vtable_slots":{"CDDxDrawObj":cddx,"CELxnObj":xn},
            "action_speed_immediate_calls":calls,"action_speed_immediate_call_count":len(calls),
            "observed_immediate_speeds":speeds,
            "movement_action_speed_separation_verified":verified}

def main():
    ap=argparse.ArgumentParser(description=__doc__); ap.add_argument("lst",type=Path); ap.add_argument("--output",type=Path)
    a=ap.parse_args()
    if not a.lst.is_file(): ap.error(f"missing lst: {a.lst}")
    result=audit(a.lst.read_text(encoding="utf-8",errors="ignore").splitlines())
    if a.output:
        a.output.parent.mkdir(parents=True,exist_ok=True)
        a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"verified":result["movement_action_speed_separation_verified"],
        "movement_speed":result["movement_speed"],"action_speed_wrapper":result["action_speed_wrapper"],
        "vtable_slots":result["vtable_slots"],
        "action_speed_immediate_call_count":result["action_speed_immediate_call_count"],
        "observed_immediate_speeds":result["observed_immediate_speeds"],
        "functions":sorted({x["function"] for x in result["action_speed_immediate_calls"]})},
        ensure_ascii=False,indent=2))
    return 0 if result["movement_action_speed_separation_verified"] else 1
if __name__=="__main__": raise SystemExit(main())
