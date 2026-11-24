# UI-009_STATUS — 狀態效果列（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-009_STATUS.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
Buff/Debuff icon、剩餘時間、移除、提示。

驗收情境：增加、更新、到期、死亡清除／重登規則一致。

## 原版 UI 控制規格
來源：HK/output/Interface/009_STATUS.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 8，取得 8。

背景資源：STATUS.spr, STATUS.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0022 | 1 | 2000 | 1000 | 3,2,24,12 | — | 狀態 | — |
| 0x004C | 0 | 2139 | 1000 | 3,14,24,24 | — | — | EFFECT_ICON.spr |
| 0x0085 | 0 | 2140 | 1000 | 3,41,24,24 | — | — | EFFECT_ICON.spr |
| 0x00BE | 0 | 2142 | 1000 | 3,68,24,24 | — | — | EFFECT_ICON.spr |
| 0x00F7 | 0 | 2141 | 1000 | 3,95,24,24 | — | — | EFFECT_ICON.spr |
| 0x0130 | 0 | 2143 | 1000 | 3,122,24,24 | — | — | EFFECT_ICON.spr |
| 0x0169 | 0 | 2144 | 1000 | 3,149,24,24 | — | — | EFFECT_ICON.spr |
| 0x01A2 | 0 | 2145 | 1000 | 3,176,24,24 | — | — | EFFECT_ICON.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| EFFECT_ICON.spr | 7 | 24×24 @(0,0) ×7 幀 |
| STATUS.spr | 1 | 30×205 @(0,0) ×1 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CStatusIconBarItf::Create — L134955 @0x00451617；直接具名呼叫：CSGOInterface::Create
- CStatusIconBarItf::OnUpdate — L134979 @0x00451635；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0022 / ID 2000（type 1，3,2,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x004C / ID 2139（type 0，3,14,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0085 / ID 2140（type 0，3,41,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00BE / ID 2142（type 0，3,68,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00F7 / ID 2141（type 0，3,95,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0130 / ID 2143（type 0，3,122,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0169 / ID 2144（type 0，3,149,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01A2 / ID 2145（type 0，3,176,24,24，EFFECT_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：增加、更新、到期、死亡清除／重登規則一致。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-023 Buff/Debuff 與治療](../gameplay/GP-023.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
