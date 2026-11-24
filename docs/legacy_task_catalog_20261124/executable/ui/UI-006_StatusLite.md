# UI-006_StatusLite — 角色簡易狀態 HUD（006_StatusLite）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-006_StatusLite.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
HP/MP/EXP/WP 即時顯示；數值與進度裁切不錯位。

驗收情境：受擊、恢復、升級、換圖、重登後正確；0/MAX 邊界。

## 原版 UI 控制規格
來源：HK/output/Interface/006_StatusLite.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 7，取得 7。

背景資源：HPBAR.spr, HPBAR.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0020 | 3 | 2091 | 1000 | 24,20,92,10 | s01 | — | HP.spr |
| 0x0068 | 3 | 2092 | 1000 | 24,34,92,10 | S02 | — | MP.spr |
| 0x00B0 | 3 | 2093 | 1000 | 24,48,92,10 | s03 | — | WP.spr |
| 0x00F8 | 3 | 2095 | 1000 | 24,80,92,4 | s05 | — | EXP.spr |
| 0x0142 | 0 | 2090 | 1000 | 42,5,25,10 | S01 | — | — |
| 0x016E | 0 | 2094 | 1000 | 27,64,87,10 | s04 | — | — |
| 0x019A | 0 | 2592 | 1000 | 76,5,25,10 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| EXP.spr | 16 | 225×12 @(0,0) ×16 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
| HPBAR.spr | 1 | 128×90 @(0,0) ×1 幀 |
| MP.spr | 16 | 109×10 @(0,0) ×16 幀 |
| WP.spr | 18 | 141×10 @(0,0) ×18 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CCharInfoLiteItf::Create — L117035 @0x00446B14；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?4?$CELPtr, VCSGOPlayer::?1?$CELPtr, CSGOInterface::Create, VCELProgressBar::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, VCELProgressBar::?C?$CELPtr, VCELProgressBar::?1?$CELPtr
- CCharInfoLiteItf::UpdateInfo — L117158 @0x00446C3A；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, CCharInfoLiteItf::CalEXPBar
- CCharInfoLiteItf::UpdateButton — L117390 @0x00446EB6；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELProgressBar::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELProgressBar::?4?$CELPtr, VCELProgressBar::?C?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCELProgressBar::?1?$CELPtr, VCELMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0020 / ID 2091（type 3，24,20,92,10，s01，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0068 / ID 2092（type 3，24,34,92,10，S02，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B0 / ID 2093（type 3，24,48,92,10，s03，WP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00F8 / ID 2095（type 3，24,80,92,4，s05，EXP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0142 / ID 2090（type 0，42,5,25,10，S01）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x016E / ID 2094（type 0，27,64,87,10，s04）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x019A / ID 2592（type 0，76,5,25,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：受擊、恢復、升級、換圖、重登後正確；0/MAX 邊界。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-012 正式角色戰鬥屬性解鎖](../gameplay/GP-012.md)
- [GP-014 普通攻擊與命中/MISS](../gameplay/GP-014.md)
- [GP-015 傷害結算、受擊與動畫](../gameplay/GP-015.md)
- [GP-016 擊殺、EXP、金錢與升級](../gameplay/GP-016.md)
- [GP-019 玩家死亡與復活](../gameplay/GP-019.md)
- [GP-023 Buff/Debuff 與治療](../gameplay/GP-023.md)
- [GP-025 消耗品使用與恢復](../gameplay/GP-025.md)
- [GP-026 裝備裝卸與屬性](../gameplay/GP-026.md)
- [GP-027 屬性配點與重算](../gameplay/GP-027.md)
- [GP-042 組隊戰鬥共享與狀態](../gameplay/GP-042.md)
- [GP-073 戰鬥數字與 MISS/防禦特效](../gameplay/GP-073.md)
- [GP-077 五個 C.D.E. ITF 解密與控件驗證](../gameplay/GP-077.md)
- [GP-079 遊戲內資源生命週期與效能](../gameplay/GP-079.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
