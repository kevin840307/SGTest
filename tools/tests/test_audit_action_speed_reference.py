import sys, unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from audit_action_speed_reference import audit

SAMPLE="""
.text:00401000 ?SetSpeed@CSGOChar@@QAEXH@Z proc near
.text:00401001 mov eax, [ebp+var_4]
.text:00401002 mov ecx, [ebp+arg_0]
.text:00401003 mov [eax+17Ch], ecx
.text:00401004 ?SetSpeed@CSGOChar@@QAEXH@Z endp
.text:00402000 ?SetActionSpeed@CELxnObj@@UAEXH@Z proc near
.text:00402001 call ?SetSpeed@xnAction@@QAEXH@Z
.text:00402002 ?SetActionSpeed@CELxnObj@@UAEXH@Z endp
.text:00403000 ?Effect@CTest@@QAEXXZ proc near
.text:00403001 call dword ptr [eax+24h]
.text:00403002 push 32h
.text:00403003 mov edx, [eax]
.text:00403004 call dword ptr [edx+2Ch]
.text:00403005 ?Effect@CTest@@QAEXXZ endp
.rdata:00500000 ??_7CDDxDrawObj@@6B@ dd offset ?IsKindOf@CDDxDrawObj@@UAE_NPBD@Z
.rdata:0050002C dd offset ?SetActionSpeed@CELxnObj@@UAEXH@Z
.rdata:00500100 ??_7CELxnObj@@6B@ dd offset ?IsKindOf@CELxnObj@@UAE_NPBD@Z
.rdata:0050012C dd offset ?SetActionSpeed@CELxnObj@@UAEXH@Z
""".strip().splitlines()

class T(unittest.TestCase):
    def test_audit(self):
        r=audit(SAMPLE); self.assertTrue(r["movement_action_speed_separation_verified"])
        self.assertEqual(r["vtable_slots"]["CDDxDrawObj"]["slot_offset"],0x2C)
        self.assertEqual(r["action_speed_immediate_call_count"],1)
        self.assertEqual(r["observed_immediate_speeds"],[50])
        self.assertEqual(r["action_speed_immediate_calls"][0]["speed"],50)
    def test_coupled_rejected(self):
        changed=[x.replace("mov [eax+17Ch], ecx","mov [eax+17Ch], ecx call ?SetSpeed@xnAction@@QAEXH@Z") for x in SAMPLE]
        r=audit(changed); self.assertFalse(r["movement_action_speed_separation_verified"])
if __name__=="__main__": unittest.main()
