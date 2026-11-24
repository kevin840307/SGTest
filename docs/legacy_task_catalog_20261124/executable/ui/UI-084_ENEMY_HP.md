# UI-084_ENEMY_HP — 怪物目標 HP 視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-084_ENEMY_HP.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
目標名、HP、選取/取消與距離、死亡清除。

驗收情境：切換怪、攻擊扣血、MISS 不扣血、離開 AOI 清除。

## 原版 UI 控制規格
來源：HK/output/Interface/084_ENEMY_HP.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 2，取得 2。

背景資源：BG_ENEMY_HP.spr, BG_ENEMY_HP.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x002C | 3 | 2147 | 1000 | 20,5,49,5 | — | — | HP.spr |
| 0x006E | 0 | 2146 | 1000 | 5,3,14,14 | — | — | B_ENEMY_HP.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ENEMY_HP.spr | 5 | 14×14 @(0,0) ×5 幀 |
| BG_ENEMY_HP.spr | 1 | 75×21 @(0,0) ×1 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CSGOEnemHPItf::OnUpdate — L259308 @0x0049E092；直接具名呼叫：CELMapObj::GetWorldPosition, CSGOEnem::GetDrawObjSize, CELMap2D::MapPosX, CELMap2D::MapPosY, CSGOEnem::GetNameStrlen, VCELProgressBar::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, CELOnlineObj::GetData, VCELProgressBar::?C?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?9?$CELPtr
- CSGOEnemHPItf::Create — L259624 @0x0049E3C1；直接具名呼叫：CSGOInterface::Create, VCELProgressBar::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, VCELProgressBar::?C?$CELPtr, VCELProgressBar::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x002C / ID 2147（type 3，20,5,49,5，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x006E / ID 2146（type 0，5,3,14,14，B_ENEMY_HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：切換怪、攻擊扣血、MISS 不扣血、離開 AOI 清除。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-010 怪物生成與重生](../gameplay/GP-010.md)
- [GP-011 怪物巡邏與追擊/回位](../gameplay/GP-011.md)
- [GP-013 選怪與接近攻擊距離](../gameplay/GP-013.md)
- [GP-014 普通攻擊與命中/MISS](../gameplay/GP-014.md)
- [GP-015 傷害結算、受擊與動畫](../gameplay/GP-015.md)
- [GP-017 怪物死亡與掉落生成](../gameplay/GP-017.md)
- [GP-021 單體技能與 MP/冷卻](../gameplay/GP-021.md)
- [GP-022 範圍技能與多目標](../gameplay/GP-022.md)
- [GP-052 寵物 AI 與戰鬥](../gameplay/GP-052.md)
- [GP-059 PvP 地圖與名稱狀態](../gameplay/GP-059.md)
- [GP-072 原版游標與地面選取](../gameplay/GP-072.md)
- [GP-073 戰鬥數字與 MISS/防禦特效](../gameplay/GP-073.md)
- [GP-074 技能與武器特效](../gameplay/GP-074.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
