#!/usr/bin/env python3
"""Build independent execution specs from local licensed reference metadata.
No original art, code dumps, credentials or decrypted binary are exported.
Run from repository root: python docs/legacy_task_catalog_20261124/build_execution_specs.py
"""
from pathlib import Path
import csv, json, re, struct, sys, collections, hashlib
R=Path(__file__).resolve().parent
ROOT=R.parents[1]
sys.path.insert(0,str(ROOT))
from tools.asset_converter.source.runtime_spr import unwrap_cde
HK=Path(r"C:\SGO\Stargate Online IE HK")
OLD=Path(r"C:\SGO\Recovered\Old")
OUT=R/"executable"
D=json.loads((R/"source_inventory.json").read_text(encoding="utf-8"))
idx=list(csv.DictReader((R/"TASK_INDEX.csv").open(encoding="utf-8-sig")))
byid={x["id"]:x for x in idx}
def lp(b,p):
    n=b[p]; end=p+1+n
    if end>len(b): raise ValueError("length")
    return b[p+1:end],end
def text(s): return s.decode("cp950",errors="replace").replace("|","／").replace("\n"," ").replace("\r"," ")
def controls(b):
    a,p=lp(b,0); z,p=lp(b,p); root,scale,count=struct.unpack_from("<III",b,p); p+=12
    if not 0<count<=2000 or not 0<scale<=100000: raise ValueError("unexpected header")
    rows=[]
    # The 7 little-endian fields are confirmed on login/profile/select by existing project evidence.
    # Scan unaligned candidate starts; count and first-offset are independent structural checks.
    for i in range(p,len(b)-28):
        t,x,y,w,h,cid,s=struct.unpack_from("<7i",b,i)
        if not (0<=t<=40 and -4096<=x<=4096 and -4096<=y<=4096 and (-4096<=w<=4096 or w==-842150451) and (-4096<=h<=4096 or h==-842150451) and 1000<=cid<=100000 and (root<=s<=root+15 or s==-1)):continue
        try:
            label,q=lp(b,i+28)
            if len(label)>100 or any(k<32 for k in label):continue
        except (IndexError,ValueError):continue
        if rows and i<rows[-1]["offset"]+29:continue
        rows.append(dict(offset=i,t=t,x=x,y=y,w=w,h=h,id=cid,scale=s,label=text(label),payload=q))
    # Constrain per-control payload to following identified record, never claim frame bindings without evidence.
    for i,r in enumerate(rows):
        end=rows[i+1]["offset"] if i+1<len(rows) else len(b)
        payload=b[r.pop("payload"):end]
        r["sprites"]=sorted(set(x.decode("ascii") for x in re.findall(rb"[A-Za-z0-9_./ -]+[.]spr",payload,re.I)),key=str.casefold)
        if r["t"]==1 and payload:
            try:
                label,stop=lp(payload,0)
                r["caption"]=label.decode('cp950',errors='strict')
            except (IndexError,ValueError,UnicodeError):pass
    status="COUNT_MATCH" if len(rows)==count and rows[0]["offset"]==p else "PARTIAL_CANDIDATES"
    return dict(background=[text(a),text(z)],root=root,scale=scale,count=count,status=status,controls=rows)
# Index named Old functions and direct named call edges, without copying disassembly.
PROC=re.compile(r"^\.text:([0-9A-Fa-f]+)\s+\?([^@\s]+)@([^@\s]+)@@\S*\s+proc near")
END=re.compile(r"\s+endp\s*$")
CALL=re.compile(r"\bcall\s+\?([^@\s]+)@([^@\s]+)@@")
def symbol_index(path,owners=None,selected=None):
    found=collections.defaultdict(list); active=None
    with path.open(encoding="utf-8",errors="replace") as f:
        for n,line in enumerate(f,1):
            m=PROC.match(line)
            if m:
                addr,method,owner=m.groups()
                active=None
                if (owners is None or owner in owners) and (selected is None or method in selected):
                    active=dict(owner=owner,method=method,line=n,address=addr,calls=[])
                    found[owner].append(active)
            elif active:
                if END.search(line):active=None;continue
                c=CALL.search(line)
                if c:
                    method,owner=c.groups(); k=owner+"::"+method
                    if k not in active["calls"]:active["calls"].append(k)
    return found
def dim(v):
    return 'AUTO? (0xCDCDCDCD)' if v==-842150451 else str(v)

def rect(c):return ','.join(dim(c[x]) for x in ('x','y','w','h'))

spr_files={p.name.casefold():p for p in (HK/'output'/'Interface').iterdir() if p.suffix.casefold()=='.spr'}
spr_cache={}
def sprite_meta(name):
    key=Path(name).name.casefold()
    if key in spr_cache:return spr_cache[key]
    p=spr_files.get(key)
    if not p:return None
    try:
        b=p.read_bytes(); b=unwrap_cde(b) if b.startswith(b'C.D.E.') else b
        count=struct.unpack_from('<I',b,len(b)-4)[0]; start=len(b)-4-4*count
        if not 0<count<=100000 or start<20:raise ValueError('invalid SPR frame table')
        offs=struct.unpack_from(f'<{count}I',b,start)
        if any(a>=z for a,z in zip(offs,offs[1:])) or offs[-1]+16>start:raise ValueError('invalid SPR offsets')
        frames=[struct.unpack_from('<IIii',b,x) for x in offs]
        if any(w==0 or h==0 or w*h>64000000 for w,h,ox,oy in frames):raise ValueError('invalid geometry')
        m=dict(name=p.name,frames=count,geometry=collections.Counter(f'{w}×{h} @({ox},{oy})' for w,h,ox,oy in frames))
    except (ValueError,struct.error,IndexError):m=None
    spr_cache[key]=m;return m
def cell(s):return str(s).replace("|","／").replace("\n"," ")
def write(p,s):
    p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s.rstrip()+"\n",encoding="utf-8")
owners={o["owner"]["name"] for u in D["ui"] for o in u["old"] if o["owner"]}
client=symbol_index(OLD/"ClientData/Paradise.exe.lst",owners)
server_meta={}
for file in ("server_symbols_core.json","server_symbols_social.json"):
    server_meta.update(json.loads((R/file).read_text(encoding="utf-8")))
server={}
for name,entries in server_meta.items():
    file=OLD/"服務端"/"plugins"/(name+".dll.lst")
    if file.exists():
        methods={x["method"] for x in entries}
        if name=="SGOBattleSys":methods.update(["CalMiss_PerHit","CalWeaponDamage_PerHit","CalFinalChange","CalHP_Final","SendCharSyncPac","CalGold_Final","CalExp_Final"])
        if name=="SGOEnemyDistSys":methods.update(["CheckDead","CheckDropItem"])
        data=symbol_index(file,selected=methods)
        server[name]=[v for items in data.values() for v in items]
ui_by_name={u["itf"].casefold():u for u in D["ui"]}
ui_meta={}
def existing_base(row):return (R/row["path"]).read_text(encoding="utf-8")
def task_intro(row):
    return f"""# {row['id']} — {row['title']}（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：{row['priority']}。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../{row['path']}) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。

"""
def symbol_lines(items):
    return "\n".join(f"- {x['owner']}::{x['method']} — L{x['line']} @0x{x['address']}；直接具名呼叫："+(", ".join(x["calls"][:12]) or "未擷取（可能間接呼叫或無具名呼叫）") for x in items)
def finish(row):
    return """
## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
"""
for row in idx:
    if row["track"]!="UI":continue
    u=next(v for v in D["ui"] if Path(v["itf"]).stem.casefold()==row["id"][3:].casefold())
    raw=(HK/"output"/"Interface"/u["itf"]).read_bytes()
    try:
        b=unwrap_cde(raw) if raw.startswith(b"C.D.E.") else raw
        m=controls(b); m["unwrapped"]=raw.startswith(b"C.D.E.")
        m["plain_sha256"]=hashlib.sha256(b).hexdigest()
    except (ValueError,IndexError,struct.error) as e:
        m=dict(status="UNRESOLVED",count=0,controls=[],background=[],error=str(e),unwrapped=False)
    ui_meta[row["id"]]=m
    base=existing_base(row)
    goal=re.search(r"^- 目標：(.*)",base,re.M).group(1)
    accept=re.search(r"^- \[ \] 可玩驗收：(.*)",base,re.M).group(1)
    refs=[x for o in u["old"] if o["owner"] for x in client.get(o["owner"]["name"],[])]
    # Dedup class symbols, focus real UI handlers rather than all accessors.
    refs=list({(x["owner"],x["method"],x["address"]):x for x in refs}.values())
    refs=[x for x in refs if re.match(r"On|Create|Draw|Update|Refresh|MessageMap|Validate|MakeData|Set|Show|Hide|Process|Render|Load|Read|Reset",x["method"])]
    refs=refs[:65]
    gp=[x for x in idx if x["track"]=="Gameplay" and f"../ui/{row['id']}.md" in existing_base(x)]
    lines=[task_intro(row),f"## 玩家可見目標\n{goal}\n\n驗收情境：{accept}\n",f"## 原版 UI 控制規格\n來源：HK/output/Interface/{u['itf']}；C.D.E. 解包：{'成功' if m['unwrapped'] else '非封裝或未解'}；解析狀態：{m['status']}；宣告控制項 {m['count']}，取得 {len(m['controls'])}。\n"]
    if m["background"]: lines.append("背景資源："+", ".join(m["background"])+f"；header raw fields={m['root']}/{m['scale']}。")
    lines.append("控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。")
    lines.append("原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。")
    lines.append("| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |")
    lines.append("|---:|---:|---:|---:|---|---|---|---|")
    for c in m["controls"]:
        lines.append(f"| 0x{c['offset']:04X} | {c['t']} | {c['id']} | {c['scale']} | {rect(c)} | {cell(c['label'] or '—')} | {cell(c.get('caption') or '—')} | {cell(', '.join(c['sprites']) or '—')} |")
    if not m["controls"]:lines.append("| — | — | — | — | — | 未確認 | — | — |")
    lines.append('\n### SPR 實際幀數與原始幾何（SPR header/table）\n| SPR | 幀數 | 原始寬高及 origin（括號內） |\n|---|---:|---|')
    names=sorted(set(x for x in m['background']+u['spr']+[s for c in m['controls'] for s in c['sprites']] if x.casefold().endswith('.spr')),key=str.casefold)
    for name in names:
        sp=sprite_meta(name)
        if sp:
            geometry='；'.join(f'{k} ×{v} 幀' for k,v in sp['geometry'].items())
            lines.append(f"| {cell(name)} | {sp['frames']} | {geometry} |")
        else:lines.append(f'| {cell(name)} | 未驗 | 未定位 top-level SPR 或格式未解析 |')
    lines.append('以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。')
    family=row['id'][3:6]
    if family in ('001','004','005','013','081','082'):
        lines.append('已存在字型證據：參考 SGTest docs/FEATURE_MATRIX.md 的 HK 字型驗收記錄；固定 caption / 已確認 Profile 文字使用新細明體 12px、weight 500、baseline 10（各畫面適用範圍見原證據）；不可將尚未驗證的動態文字或 AA 一律視為完成。')
    else:lines.append('字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。')
    lines.append("\n## 原版函式與事件追蹤\n以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。")
    lines.append(symbol_lines(refs) or "- 尚未定位具名類別；依來源任務中的 HK 引用追實際 loader/handler。")
    lines.append("\n## 本任務執行順序\n- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。")
    if m["status"]!="COUNT_MATCH":lines.append("- [ ] 先補齊本 ITF 未解析記錄：以宣告數量、逐記錄邊界、loader/原版畫面交叉驗證；不可直接使用候選當正式幾何。")
    for c in m["controls"]:
        lines.append(f"- [ ] 控件 0x{c['offset']:04X} / ID {c['id']}（type {c['t']}，{rect(c)}"+(f"，{c['label']}" if c["label"] else "")+(f"，{', '.join(c['sprites'])}" if c["sprites"] else "")+"）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。")
    lines.append(f"- [ ] 執行完整玩家流程：{accept}\n- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。")
    lines.append("\n## 玩法相依（按需執行，不重複實作）\n"+("\n".join(f"- [{g['id']} {g['title']}](../gameplay/{g['id']}.md)" for g in gp) or "- 無已建立的直接玩法連結；依實際 handler 確認。"))
    lines.append(finish(row))
    write(OUT/"ui"/(row["id"]+".md"),"\n".join(lines))
# Operational acceptance scenarios: proposed implementation slices, not automatically proven legacy behavior.
CASES=r"""
001|登入成功：輸入有效帳密→確認→取得帳號 session→角色列表可用|登入拒絕：錯誤帳密、重送或斷線→原版錯誤/回復流程→沒有授權 session
002|角色列表：登入帳號→檢查槽位與角色預覽資料→只列該帳號角色|進入世界：選擇合法 UID→進圖快照→WorldReady 後立即點地移動
003|角色外觀草稿：選種族、髮型、初始裝備與配點→預覽與數值正確|建立提交：合法姓名建立→回列表、重登存在；重名/非法配點不寫入
004|刪除角色：選角色→確認/取消→合法刪除；其他 UID 拒絕|第二密碼驗證：正確/錯誤及取消→進入或返回；不得洩漏密碼|變更第二密碼：舊密碼與新密碼驗證→變更後只能用新密碼
005|初始外觀：進圖靜止觀察十秒→方向、身體、頭髮及裝備層一致|裝備外觀：裝卸後觀察自己與另一 Client→動作/方向不串層
006|移動：點不同地面→合法路徑/Server位置→觀察他人走路|停止改點：移動中停止、連續 retarget、長按→不殘留舊命令
007|合法尋路：Map13 階梯與轉角→正常抵達|不可走目標：點牆與封閉區→不穿牆、不原地跑
008|跨圖：Map15 真 NPC 傳點到 Map13→WorldReady 立即走|返程：Map13→Map15→舊 AOI、地上物與封包不污染新圖
009|AOI 進入：雙 Client 接近→雙向 Spawn、位置與外觀正確|AOI 離開重入：離開範圍再靠近→Despawn/新 baseline，不瞬移殘影
010|怪物生成：走近原始怪區→種類、位置、數量與權威 UID|怪物重生：擊殺後依已確認時序重生，兩 Client 觀察一致
011|巡邏：無玩家攻擊時怪物依原規則走動，不穿牆|追擊與脫戰：吸引怪→繞障→離開距離→回位，不無限追
012|正式新角戰鬥：用正式角色、真裝備資料進 Map13→可選怪普攻|未知 profile：缺正式來源或非法職業→明確拒絕，不用假數值
013|選怪與取消：滑鼠點怪→目標 HUD；切怪、離開 AOI 或死亡清除|近遠攻距：近戰與遠距武器分別點怪→到合法距離才攻擊，超距不結算
014|普通攻擊：合法目標及攻速→每次有效命中一次權威扣血|MISS：使用可重現的來源確認命中條件→顯示 MISS，HP 不變|非法攻擊：超距、過快、死亡目標、錯圖→拒絕且不產生傷害事件
015|傷害結算：來源確認的固定輸入→核對傷害/HP及可靠事件|受擊動畫：自己與他人看到正確受擊/攻擊，移動不覆蓋死亡
016|擊殺 EXP：最後有效攻擊→一次死亡結算與 EXP，重送不多發|升級：跨越來源確認門檻→等級/屬性/HUD同步，重登一致|金錢：確認原版擊殺金錢規則→單次入帳且不重複
017|掉落生成：指定怪擊殺→依合法掉落表生成可見 UID|掉落生命周期：兩 Client 觀察、離開再進 AOI→相同物品，不重生複製
018|撿取成功：走近合法地上物→點取→背包新增，世界移除|競拾/滿包：兩 Client 同點一物、滿包或超距→唯一成功或保留物品
019|死亡：HP 歸零→死亡畫面、停止移動攻擊及原版損失|復活：依原版合法選擇→回指定點、HP/MP依證據、可走可攻擊
020|技能學習：符合職業等級與前置→新增技能與技能 UI|技能升級：合法點數/材料→等級、消耗與重登一致
021|單體施法：技能欄/快捷鍵選目標→扣 MP、冷卻、單次效果|施法拒絕：MP不足、冷卻中、超距、非法目標→不扣不結算
022|範圍選取：施放已確認 AoE→範圍內外怪對照|範圍結算：多目標與多 Client→每目標按原版規則計次，不重複
023|Buff：施放可確認的增益→狀態圖示、數值及到期同步|Debuff：命中合法負面效果→疊加/解除/死亡規則|治療：選合法目標→HP/MP變更、不超上限、雙 Client
024|背包格位：撿物→分類與格位顯示，重登保持|堆疊與分堆：合法合併/分割→總量守恆；滿包拒絕
025|使用藥水：有消耗品、HP/MP不足→正確回復與減量|消耗品拒絕：空堆疊/不合條件/冷卻→不扣物、不虛增回復
026|裝備穿戴：合法 UID→正確槽位、屬性與外觀|卸下與替換：回背包或換裝→不複製、不錯槽|裝備限制：錯職業/等級/類型/非持有 UID→拒絕
027|屬性配點：有可用點數→配置一項→HUD與派生值更新|非法配點：超量或重送→無額外屬性，重登一致
028|快捷鍵配置：拖技能/物品到槽位、覆蓋、清除→正確圖示|快捷鍵使用：實際按鍵觸發真技能/消耗品，空槽不執行
029|NPC 開啟：靠近點 NPC→正確對話及選項|NPC 分支：選合法選項→下一頁或權威功能，取消/離圖失效
030|購買：真 NPC 選商品、數量→金錢與背包原子更新|購買拒絕：錢不足/滿包/失效價格→無半套交易
031|出售：選自有可售物→數量減少、金錢增加|出售拒絕：非持有/裝備中或非法數量→資料不變
032|存款：銀行輸入合法金額→身上減、銀行增|提款：銀行輸入合法金額→反向原子更新；不足/重送拒絕
033|倉庫存物：背包選合法 UID→倉庫格位與所有權|倉庫取物：反向轉移；滿包/滿倉與重送不複製
034|耐久消耗：來源確認戰鬥情境→耐久顯示及保存|修理：選裝備→正確費用和耐久；不足拒絕
035|強化成功：選合法裝備、材料→instance enhancement 與屬性更新|強化失敗：依原版機率/結果證據驗耗材、保留或損壞，不猜規則
036|物品合成：選已確認配方→材料減少、結果新增|合成拒絕：不足材料或滿包→原子失敗
037|物品分解：選可分解物→合法產出及扣除|分解拒絕：非法 UID、滿包或重送→不複製
038|交易邀請：兩 Client 發出/接受/拒絕→雙方 UI 一致|交易內容：放入物品與金錢→同步預覽，變更使 ready 失效|交易確認：兩方確認→一次原子交換，取消/斷線不丟物
039|開攤命名：店主設定名稱→世界招牌與 AOI|商品上架：選 UID/價格/數量→買家清單一致|關攤：依原版取消上架→不可再購買、物品解鎖
040|擺攤購買：買家選商品確認→雙方金錢與物品一致|多人競購：同最後一件→只有一人成功，其他人刷新
041|隊伍邀請：建立、邀請、接受或拒絕→雙方狀態|隊伍管理：隊長轉移/踢人/離隊→權限與列表同步
042|隊友狀態：隊友 HP/MP/位置變更→隊伍 UI 一致|共享結算：組隊擊殺→按已確認 EXP/掉落分配，離隊後不共享
043|公開聊天：兩 Client 發送繁中→確認頻道與範圍|私訊/隊伍聊天：指定對象/成員可收到，其他人不可見
044|好友：新增刪除與上下線→清單同步、重登|黑名單：封鎖/解除→依原版過濾訊息及互動
045|留言寄送：確認原版入口→發給合法收件者|留言讀取：讀取/刪除→重登與非收件者拒絕
046|表情：選表情→雙 Client 同步及到期|泡泡樣式：切換可用樣式→原圖與字型呈現，重登依證據
047|稱號：選已取得稱號→世界名稱與人物資料一致；未取得拒絕
048|轉職：滿足職業任務/等級→職業、技能、派生值更新|不符前置：拒絕且舊職業保留
049|任務接取：NPC 選項→任務清單及已接取狀態|進度：合法擊殺/收集事件→更新計數；重登與放棄依證據
050|任務完成：滿足條件→提交與獎勵一次|獎勵拒絕：條件不足、重送、滿包→依原版安全結算
051|寵物召喚：持有合法寵物→外觀、HP、雙 Client Spawn|寵物收回：正常收回→Despawn，換圖/重登依原版
052|寵物跟隨：主人移動與繞障→寵物不穿牆|寵物攻擊：選合法怪→權威攻擊與受擊、傷害歸屬
053|餵食：合法食物→飽食/相關屬性更新，扣除物品|寵物配點：合法點數→寵物派生數值及重登|寵物裝卸：合法物品→正確寵物槽位與屬性
054|寵物交易：雙 Client 預覽並確認→合法寵物所有權轉移；取消/限制拒絕
055|機甲搭乘：合法機甲→外觀、模式與能源 UI 更新|機甲解除：下車→人物模式/外觀恢復，雙 Client 同步
056|機甲裝卸：選對應合法裝備→機甲屬性、槽位一致|能源消耗：搭乘或戰鬥→能源按證據更新、耗盡退出合法狀態|能源補充：使用合法道具→耗材與能源原子更新
057|機甲修理：選機甲→費用、耐久/能源依證據更新|機甲升級：來源確認 EXP 門檻→升級、屬性與重登
058|機甲卡片：合法卡片選擇/使用→正確預覽與狀態|合體：符合成員和機甲條件→多 Client 共識外觀與能力|解除合體：退出、斷線、換圖→按規則清理各成員
059|PvP 地圖：允許/不允許區域測試選人及攻擊|PK 名稱：合法攻擊、擊殺與雙方狀態→名稱顏色與權威規則一致
060|PvP 對戰入口：先證明模式可達→雙 Client 邀請/進場|PvP 結算排行：合法勝負、離場/斷線→結算與排行一致
061|排行：多角色查閱→排序、分頁、空清單與同分規則
062|社團建立：合法名稱與條件→建立並保存|成員管理：加入、退出、職位及權限→多 Client 同步
063|結盟：雙方確認→同盟一致；解除後同步|社團公告：有權限編輯公告/介紹→成員可見，無權拒絕
064|社團金庫捐獻：合法金額→個人及金庫原子更新|社團金庫提款：權限與餘額→反向原子交易|社團資源：合法資源操作→數量與紀錄一致|社團科技開發：前置與成本→解鎖|社團科技升級：合法等級與材料→一次更新
065|髮型預覽：左右切換→預覽正確，不扣錢|髮型購買：合法確認→扣款與外觀保存；取消/不足不變
066|收藏：各分類合法物品→收藏狀態與完成度|收藏獎勵：達條件領取→一次獎勵；重送拒絕
067|Refund 原版語意：實際入口與 handler→確認是否啟用與商品類型|Refund 交易：僅在證據足夠後執行合法/非法返還與原子驗收
068|系統設定：選項切換→實際生效，重開保存|登出：回登入→世界、session、AOI 清理，再登入成功
069|公告：合法來源發布→畫面正確排隊與消失；一般玩家不能冒充
070|禮物資格：合法資格領取一次→背包/狀態保存；未達條件拒絕
071|GM 功能：隔離環境授權測試→權限檢查與審計；一般帳號拒絕
072|游標：地板/怪/玩家/物品/技能 hover→對應原圖與可點狀態
073|傷害數字：普攻及技能→自己與他人顯示正確值、一次|MISS/Guard：確認對應原版素材→未命中或防禦顯示、時序
074|武器/命中特效：合法攻擊→正確動作、方向與素材|技能特效：單體/範圍→起迄時間、方向及多 Client 清理一致
075|地圖介面：開地圖與選區域→正確背景、標記及坐標|傳送地圖：合法入口/目的地→正常傳送；無效拒絕
076|共用圖像：以正式 converter 匯入背景、控件、frame→代表視窗原圖對照|共用文字：原版字型、baseline與 clipping→代表畫面 A/B
077|五個封裝 ITF：確認現有解包器輸出、控制項總數及 sprite 引用→逐一實機驗證
078|未直接引用 ITF：逐檔確認 loader、實際入口及版本可達性|未配對 SPR：搜尋動態引用及用途→保留 active/unknown 分類
079|換圖與登出資源：重複進圖/換圖/登出→記憶體、幀率、資源數與基線比較|AFK/Unfocus：只降低渲染負荷，可靠事件與權威邏輯不降級
""".strip()
case_map={p[0]:p[1:] for p in (l.split("|") for l in CASES.splitlines())}
entries=[]
for row in idx:
    if row["track"]=="UI":
        m=ui_meta[row["id"]]
        entries.append(dict(id=row["id"],track="UI",title=row["title"],priority=row["priority"],path=f"ui/{row['id']}.md",parent="",evidence=m["status"]))
        continue
    id=row["id"]; base=existing_base(row)
    goal=re.search(r"^- 目標：(.*)",base,re.M).group(1)
    accept=re.search(r"^- \[ \] 玩家流程：(.*)",base,re.M).group(1)
    data=re.search(r"^- 相關資料定位：(.*)",base,re.M).group(1)
    plugins=re.findall(r"plugins/([^/]+?)\.dll\.lst",base)
    ui_ids=re.findall(r"\.\./ui/([^/]+?)\.md",base)
    # Start with names explicitly cited in the existing evidence. Include only real indexed entries.
    relevant=[]
    for pl in plugins:
        candidates=server.get(pl,[])
        selected=[x for x in candidates if x["method"] in base]
        for x in selected[:20]:relevant.append((pl,x))
    evidence="\n".join(f"- Old/服務端/plugins/{pl}.dll.lst：{symbol_lines([x])[2:]}" for pl,x in relevant)
    links="\n".join(f"- [{u} {byid[u]['title']}](../ui/{u}.md)" for u in ui_ids)
    scenarios=case_map[id[3:]]
    common=f"""## 原版行為與資料
{goal}
- 原始資料：{data}
- 以 Old .lst 和 HK 可觀察結果查明規則；以下靜態呼叫摘要不保證實際分支、順序或公式。
{evidence or '- 本次未建立對應具名呼叫摘要，依既有任務證據追實際 handler，不猜測。'}

## UI 呈現與操作
{links or '- 無已確認專屬 ITF；若功能透過地圖、人物動畫或世界事件呈現，記錄實際原版畫面及操作，不能虛構視窗。'}
使用玩家真實入口，UI 只呈現正式 Server 狀態；原版素材、位置、字型、圖示、動畫、錯誤與等待狀態均依相應 UI 文件。不得先假造數字、假 UI 或繞過正式驗證。
"""
    sublinks=[]
    for i,s in enumerate(scenarios,1):
        if len(scenarios)==1:break
        sid=f"{id}-{i:02d}"; title=s.split("：",1)[0]
        sr=dict(row,id=sid,title=title)
        content=task_intro(sr)+f"## 單一交付目標\n{s}。\n\n父任務：[ {id} ]({id}.md)。只處理本操作；其他分支仍由各子任務負責。\n\n"+common+f"""
## 實作與測試順序
- [ ] 使用隔離正式 Host、可見 Client、合法角色與真資料重現：{s}。
- [ ] 定位上述反編譯 entry、分支與既有資料來源，記錄與本操作相關的前置、輸入、權威狀態轉移、輸出/錯誤；未解析常數不補猜。
- [ ] 在現有 domain owner、Wire、Client reducer、操作入口補齊本操作；正確處理重入、失效、取消與依本功能適用的 transaction/epoch。
- [ ] 正常玩家操作逐步驗收：{s}；另測至少一個此操作的原版合法失敗路徑；若原版未確認先標 NOT VERIFIED。
- [ ] 核對與父任務的共享狀態及鄰近操作，不重複其他子任務的實作。
"""+finish(sr)
        write(OUT/"gameplay"/(sid+".md"),content)
        sublinks.append(f"- [ ] [{sid} {title}]({sid}.md)：{s}")
        entries.append(dict(id=sid,track="Gameplay leaf",title=title,priority=row["priority"],path=f"gameplay/{sid}.md",parent=id,evidence="Old source anchors; scenario not runtime verified"))
    content=task_intro(row)+common+"\n## 可執行操作與完成順序\n"+("\n".join(sublinks) if sublinks else f"- [ ] {scenarios[0]}\n- [ ] 補齊本操作的正式 domain、Wire、Client 輸入/呈現及適用的保存；核對原版錯誤分支與重登/多人。")
    content+=f"\n\n## 整體玩家驗收\n- [ ] {accept}\n- [ ] 若有子任務，各自驗收後再跑完整相鄰流程；本文件只作整合關閉，不重複已完成子任務。\n"+finish(row)
    write(OUT/"gameplay"/(id+".md"),content)
    entries.append(dict(id=id,track="Gameplay integration" if sublinks else "Gameplay leaf",title=row["title"],priority=row["priority"],path=f"gameplay/{id}.md",parent="",evidence="Old source anchors; scenario not runtime verified"))
# Unmapped SPRs: keep one evidence/reachability task each, not one invented feature each.
direct={Path(n).name.casefold() for m in ui_meta.values() for n in m['background']+[s for c in m['controls'] for s in c['sprites']] if n.casefold().endswith('.spr')}
unmapped={x.casefold():x for x in D['assets'] if x.casefold() not in direct}
hits=collections.defaultdict(list)
SPR_LITERAL=re.compile(r"[A-Za-z0-9_%-]+\.spr",re.I)
ADDR=re.compile(r"^(\.[A-Za-z0-9_]+):([0-9A-Fa-f]+)")
FUNC=re.compile(r"^\.text:[0-9A-Fa-f]+\s+(\S+)\s+proc near")
for label,p in [('Old/ClientData/Paradise.exe.lst',OLD/'ClientData'/'Paradise.exe.lst'),('HK/SGOClient.exe.lst',HK/'SGOClient.exe.lst')]:
    active=None
    with p.open(encoding='utf-8',errors='replace') as f:
        for line_no,line in enumerate(f,1):
            m=FUNC.match(line)
            if m:active=m.group(1)
            elif END.search(line):active=None
            if '.spr' not in line.casefold():continue
            addr=ADDR.match(line)
            if not addr:continue
            for name in {s.casefold() for s in SPR_LITERAL.findall(line)} & unmapped.keys():
                if len(hits[name])>=30:continue
                seg,address=addr.groups()
                hits[name].append(dict(source=label,line=line_no,address=address,segment=seg,func=active if seg=='.text' else None))
asset_entries=[]
for name,original in sorted(unmapped.items()):
    sid='ASSET-'+Path(original).stem
    sp=sprite_meta(original)
    refs=hits[name]
    geom='；'.join(f'{k} ×{v} 幀' for k,v in sp['geometry'].items()) if sp else '未驗'
    refs_text='\n'.join(f"- {x['source']} L{x['line']} @0x{x['address']} ({x['segment']})"+(f"；所在符號 {x['func']}" if x['func'] else '') for x in refs)
    content=f"""# {sid} — {original} 動態素材可達性（獨立證據任務）

狀態：NOT VERIFIED。父任務：[GP-078-02](../gameplay/GP-078-02.md)。遵守 [AGENTS.md](../../../../AGENTS.md)。
來源：HK/output/Interface/{original}。未出現在本次 152 個 ITF 的直接控件/背景引用集合；不代表未使用，亦不得直接新增不存在的遊戲功能。

## 已取得的實際素材資訊
- SPR header 幀數：{sp['frames'] if sp else '未驗'}
- 原始寬高與 origin：{geom}
- 反編譯字串定位（Old / HK，最多 30 處；可能為資料區或動態引用，須追 caller）：
{refs_text or '- 未找到相同完整檔名字串；需查動態格式化、共用 loader 或版本差異。'}

## 獨立執行步驟
- [ ] 使用正式素材管線確認本素材所有 frame、origin、透明度及用途；不得提交原始素材。
- [ ] 查上述原版地址、caller、動態檔名/ITF 子目錄；取得原版實際畫面或可達性證據，記錄哪個功能、畫面、觸發/取消與狀態。
- [ ] 若可達，定位 SGTest 既有 owner、對應 UI/Gameplay 任務；補上正式素材及顯示/交互缺口，驗 frame/位置/字型（適用）、原版操作與 Before/After。
- [ ] 若僅屬舊版/未啟用/未確認，保留證據分類 variant/dormant/unknown，不建立假入口、不標功能 COMPLETE。
- [ ] 更新 GP-078-02 的證據與既有 TODO/Matrix（若有實際產品變更），Git 僅提交本切片。
"""
    write(OUT/'assets'/(sid+'.md'),content)
    asset_entries.append(dict(id=sid,track='Asset evidence',title=original+' 素材可達性',priority='P3',path=f'assets/{sid}.md',parent='GP-078-02',evidence='LST_LITERAL' if refs else 'DYNAMIC_OR_UNKNOWN'))
entries.extend(asset_entries)
asset_index="\n".join(f"- [ ] [{x['id']}](../{x['path']}) — {x['evidence']}" for x in asset_entries)
asset_parent=OUT/'gameplay'/'GP-078-02.md'
write(asset_parent,asset_parent.read_text(encoding='utf-8')+"\n## 逐素材追蹤子任務（不把未知素材當已確認玩法）\n"+asset_index)
# Five wrappers are now decoded structurally; this is not a UI runtime pass.
encrypted=OUT/'gameplay'/'GP-077.md'
write(encrypted,encrypted.read_text(encoding='utf-8')+"\n## 本次已完成的靜態前置\n正式 unwrap_cde 已成功解包全部五個 ITF；各自的控制項宣告數、offset、SPR 與文字候選見對應 UI 規格。剩餘工作是確認 loader/frame-state、實際入口、原版畫面及正式 SGTest 操作，不需重新猜測加密格式。")
# Save metadata, not decrypted original files.
write(OUT/"UI_CONTROL_METADATA.json",json.dumps(ui_meta,ensure_ascii=False,indent=2))
write(OUT/"SPR_FRAME_METADATA.json",json.dumps({k:v for k,v in spr_cache.items() if v},ensure_ascii=False,indent=2))
with (OUT/"EXECUTION_INDEX.csv").open("w",encoding="utf-8-sig",newline="") as f:
    w=csv.DictWriter(f,fieldnames=list(entries[0]));w.writeheader();w.writerows(entries)
counts=collections.Counter(x["status"] for x in ui_meta.values())
tracks=collections.Counter(x["track"] for x in entries)
n=sum(len(x["controls"]) for x in ui_meta.values())
unwrapped=sum(x["unwrapped"] for x in ui_meta.values())
rows="\n".join(f"| [{x['id']}]({x['path']}) | {x['track']} | {x['title']} | {x['priority']} | {x['parent'] or '—'} |" for x in entries)
write(OUT/"README.md",f"""# SGTest 獨立實作與驗收任務庫

本目錄為原始盤點的執行版；以每個 MD 為單位進行玩家功能、UI 呈現、原版證據與驗收。不是全部完成報告。正式狀態仍由 TODO / FEATURE_MATRIX 管理。

- {len(entries)} 個任務：{tracks['UI']} 個 UI、{tracks['Gameplay leaf']} 個可執行 Gameplay leaf、{tracks['Gameplay integration']} 個 Gameplay 整合、{tracks['Asset evidence']} 個未配對 SPR 逐項證據任務（{sum(bool(hits[x]) for x in unmapped)} 個有 Old/HK 直接字串定位）。直接 ITF 配對 {len(direct)} 個素材；共 {len(spr_cache)} 個素材嘗試讀取幀幾何、{sum(v is not None for v in spr_cache.values())} 個成功。
- HK ITF 共 {len(ui_meta)}，結構掃描取得 {n} 個候選控件；宣告數量及首偏移吻合 {counts['COUNT_MATCH']}，部分候選 {counts['PARTIAL_CANDIDATES']}，未解析 {counts['UNRESOLVED']}；成功以正式解包器解 C.D.E. {unwrapped} 個。
- COUNT_MATCH 仍非 type/frame/字體/事件綁定的完整語意驗證。未取得證據保留未知，禁止自行猜測。Old 靜態具名呼叫並非完整 opcode、執行順序或公式。
- 原始檔及反編譯全文不提交，只保留 metadata/定位與任務。UI 優先 HK 原圖、Old 行為參考。
- 執行順序遵循 [AGENTS.md](../../../AGENTS.md)：先 P0，再現有可玩鏈最早缺口（尤其 GP-012→013→014），每三個非緊急交付兩玩法一遊戲內 UI。不要每輪重做登入。Gameplay leaf 優先，integration 在其子任務驗收後整合。已符合就驗收現有實作，不重寫。
- Agent 一次取一份 leaf 或 UI MD，先查現有程式與其他 Session，再執行所有適用核取方塊；沒有證據時記錄 blocker 並改做獨立任務。完成後更新唯一正式矩陣/證據及 commit，不能以文件數量當遊戲完成。
- 本次只產出任務規格，未啟動遊戲；個別功能尚未驗證。重建：在 SGTest root 執行 python docs/legacy_task_catalog_20261124/build_execution_specs.py。原始盤點留在上一層。

| Task | Track | 內容 | 優先度 | 父任務 |
|---|---|---|---|---|
{rows}
""")
# Validate the generated tree and structural claims.
assert len(ui_meta)==152 and set(case_map)=={x["id"][3:] for x in idx if x["track"]=="Gameplay"}
assert len(entries)==len({x["id"] for x in entries})
for x in entries:
    f=OUT/x["path"]; assert f.is_file()
    s=f.read_text(encoding="utf-8"); assert "- [ ]" in s and "NOT VERIFIED" in s
    for target in re.findall(r"\]\(([^)]+\.md)\)",s):
        assert (f.parent/target).is_file(), (f,target)
print("EXECUTION_SPECS_OK",len(entries),dict(tracks),"UI",dict(counts),"controls",n,"unwrapped",unwrapped)
