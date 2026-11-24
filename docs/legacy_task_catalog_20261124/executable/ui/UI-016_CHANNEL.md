# UI-016_CHANNEL — 聊天小視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-016_CHANNEL.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
頻道選擇、輸入、發送、捲動、訊息歷史。

驗收情境：不同頻道只收應收內容；中文輸入與長字串；大小切換。

## 原版 UI 控制規格
來源：HK/output/Interface/016_CHANNEL.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 17，取得 17。

背景資源：CHANNEL.spr, CHANNEL.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0024 | 0 | 2205 | 1000 | 440,7,24,24 | C06 | — | ICON_CHANNEL.spr |
| 0x0061 | 0 | 2213 | 1000 | 52,74,106,12 | C09 | — | — |
| 0x008D | 2 | 2009 | 1000 | 418,45,17,25 | — | — | SCROLLDOWN_S.spr |
| 0x00CC | 2 | 2008 | 1000 | 418,20,17,25 | — | — | SCROLLUP_S.spr |
| 0x0107 | 8 | 2152 | 1000 | 179,74,253,11 | C10 | — | — |
| 0x013B | 0 | 2076 | 1000 | 53,19,360,48 | — | — | — |
| 0x0164 | 2 | 2206 | 1000 | 440,35,24,24 | C07 | — | B_EMOTETION.spr |
| 0x01A4 | 2 | 2538 | 1000 | 418,3,17,17 | C11 | — | ZONESCREEN.spr |
| 0x01E2 | 0 | 2200 | 1000 | 3,21,43,15 | C01 | — | B_CHANNEL_CA.spr |
| 0x021F | 0 | 2000 | 1000 | 31,82,0,1 | — | — | — |
| 0x0248 | 0 | 2000 | 1000 | 31,63,0,0 | — | — | — |
| 0x0271 | 0 | 2203 | 1000 | 3,55,43,15 | C04 | — | B_CHANNEL_CD.spr |
| 0x02AE | 0 | 2204 | 1000 | 3,72,43,15 | C05 | — | B_CHANNEL_CE.spr |
| 0x02EB | 0 | 2202 | 1000 | 3,38,43,15 | C03 | — | B_CHANNEL_CC.spr |
| 0x0328 | 0 | 2547 | 1000 | 440,61,24,24 | C08 | — | ICON_BUBBLE.spr |
| 0x0364 | 0 | 2684 | 1000 | 53,4,360,11 | — | — | — |
| 0x038D | 0 | 2201 | 1000 | 3,4,43,15 | C02 | — | B_CHANNEL_CB.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_CHANNEL_CA.spr | 4 | 43×15 @(0,0) ×4 幀 |
| B_CHANNEL_CB.spr | 4 | 43×15 @(0,0) ×4 幀 |
| B_CHANNEL_CC.spr | 4 | 43×15 @(0,0) ×4 幀 |
| B_CHANNEL_CD.spr | 4 | 43×15 @(0,0) ×4 幀 |
| B_CHANNEL_CE.spr | 4 | 43×15 @(0,0) ×4 幀 |
| B_EMOTETION.spr | 4 | 24×24 @(0,0) ×4 幀 |
| CHANNEL.spr | 1 | 469×90 @(0,0) ×1 幀 |
| ICON_BUBBLE.spr | 12 | 24×24 @(0,0) ×12 幀 |
| ICON_CHANNEL.spr | 4 | 24×24 @(0,0) ×4 幀 |
| SCROLLDOWN_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SCROLLUP_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| ZONESCREEN.spr | 4 | 17×17 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CChatSItf::MessageMap — L105133 @0x0043F7FC；直接具名呼叫：CChatItf::MessageMap
- CChatSItf::Create — L105273 @0x0043F8F5；直接具名呼叫：CSGOInterface::Create, CChatItf::InitComponent
- CChatSItf::OnUpdate — L105328 @0x0043F96A；直接具名呼叫：CChatItf::OnUpdate
- CChatSItf::OnStoreMsg — L105349 @0x0043F97D；直接具名呼叫：CChatItf::OnStoreMsg, VCELInterface::?9?$CELPtr, VCChatLItf::?C?$CELPtr, CChatItf::OnNewMsg, VCELInterface::?1?$CELPtr
- CChatSItf::OnSwitchPanel — L105436 @0x0043FA2D；直接具名呼叫：VCSGOInterface::?0?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCSGOInterface::?7?$CELPtr, VCSGOInterface::?4?$CELPtr, VCSGOInterface::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCChatLItf::?C?$CELPtr, CChatItf::SetHistoryIndex, CChatItf::SyncAnyChanges, VCELInterface::?9?$CELPtr, VCIMEItf::?C?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0024 / ID 2205（type 0，440,7,24,24，C06，ICON_CHANNEL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0061 / ID 2213（type 0，52,74,106,12，C09）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x008D / ID 2009（type 2，418,45,17,25，SCROLLDOWN_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00CC / ID 2008（type 2，418,20,17,25，SCROLLUP_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0107 / ID 2152（type 8，179,74,253,11，C10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x013B / ID 2076（type 0，53,19,360,48）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0164 / ID 2206（type 2，440,35,24,24，C07，B_EMOTETION.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01A4 / ID 2538（type 2，418,3,17,17，C11，ZONESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01E2 / ID 2200（type 0，3,21,43,15，C01，B_CHANNEL_CA.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x021F / ID 2000（type 0，31,82,0,1）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0248 / ID 2000（type 0，31,63,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0271 / ID 2203（type 0，3,55,43,15，C04，B_CHANNEL_CD.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02AE / ID 2204（type 0，3,72,43,15，C05，B_CHANNEL_CE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02EB / ID 2202（type 0，3,38,43,15，C03，B_CHANNEL_CC.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0328 / ID 2547（type 0，440,61,24,24，C08，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0364 / ID 2684（type 0，53,4,360,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x038D / ID 2201（type 0，3,4,43,15，C02，B_CHANNEL_CB.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：不同頻道只收應收內容；中文輸入與長字串；大小切換。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-043 聊天頻道與私訊](../gameplay/GP-043.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
