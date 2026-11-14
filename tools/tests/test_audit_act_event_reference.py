import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from audit_act_event_reference import audit

def f(addr,sym,body):
    return [f".text:{addr} {sym} proc near"]+body+[f".text:{addr} {sym} endp"]
def sample():
    lines=[]
    lines+=f("00100000","?SetEventCallback@CELxnObj@@UAEXP6AXPADPAX@Z1@Z",["call    ?SetEventCallback@xnAction@@QAEXP6AXPADPAX@Z1@Z"])
    lines+=f("00100100","?SetEventCallback@xnAction@@QAEXP6AXPADPAX@Z1@Z",["mov     [ecx+28h], eax","mov     [ecx+24h], edx"])
    lines+=f("00100200","?SetAction@xnAction@@QAEXPAD@Z",["mov     edx, [edi+10h]","mov     eax, [edx]","call    ecx","mov     eax, [esi+10h]","mov     ecx, [edi+0Ch]"])
    lines+=f("00100300","?Update@xnAction@@QAE?AW4ActionStatus@@XZ",["inc     edx","mov     ecx, [edi+10h]","call    ecx","mov     ecx, [edi+0Ch]"])
    lines+=f("00100400","?BattleEffSFXCallback@CBattleSys@@SAXPADPAX@Z",['push offset $SG161356 ; "*"','push offset $SG161357 ; "+"',"call ?SetHurtEffEvent@CSGOChar@@QAEXXZ","call ?RandomPlaySound@CSGOMap@@QAEXPAD@Z"])
    lines+=f("00100500","?AttEffAxtionCallback@CSGOCharDrawLogic@@KAXPADPAX@Z",['push offset $SG169888 ; "+"','push offset $SG169913 ; "-"','push offset $SG170003 ; "$"','push offset $SG170022 ; "#"',"call ??0CBulletObj@@QAE@XZ","call ?SetRidingMode@CSGOChar@@QAEXE@Z","call ?RandomPlaySound@CSGOMap@@QAEXPAD@Z"])
    common=["call ?SetReciveEffect@CBattleSys@@QAEHPAD00@Z","call ?AppendDamge@CBattleSys@@QAEXHHPAG@Z","call ?AppendBlock@CBattleSys@@QAEXHHPAG@Z","call ?RandomPlaySound@CSGOMap@@QAEXPAD@Z"]
    lines+=f("00100600","?EnemEffAxtionCallback@CSGOEnemDrawLogic@@KAXPADPAX@Z",['push offset $SG157980 ; "+"']+common)
    lines+=f("00100700","?PetEffAxtionCallback@CSGOPetDrawLogic@@KAXPADPAX@Z",['push offset $SG157392 ; "+"']+common)
    lines+=f("00100800","?RandomPlaySound@CSGOMap@@QAEXPAD@Z",["call ds:__imp__strtok","call ds:__imp__rand",'push offset X ; "Audio\\\\SE\\\\%s.WAV"',"call ?PlayGlobalSource@CELSound"])
    return lines

class T(unittest.TestCase):
    def test_verified(self):
        r=audit(sample()); self.assertTrue(r["verified"]); self.assertTrue(r["timing"]["frame0_callback_on_set_action"])
    def test_missing_sound_rule_fails(self):
        lines=[x for x in sample() if "__imp__rand" not in x]
        self.assertFalse(audit(lines)["verified"])
if __name__=="__main__": unittest.main()
