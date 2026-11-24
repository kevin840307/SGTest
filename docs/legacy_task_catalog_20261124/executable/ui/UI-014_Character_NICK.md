# UI-014_Character_NICK — 稱號/暱稱清單（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-014_Character_NICK.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
已取得稱號、選擇、變更與原版名稱顯示。

驗收情境：未解鎖不可選；更換後自己與他人可見；重登保存。

## 原版 UI 控制規格
來源：HK/output/Interface/014_Character_NICK.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 28，取得 28。

背景資源：BG_CHARACTER_NICK.spr, BG_CHARACTER_NICK.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0038 | 2 | 2176 | 1000 | 184,1,16,16 | NN02 | — | CLOSESCREEN.spr |
| 0x0079 | 1 | 2000 | 1000 | 13,37,48,12 | — | 現在稱號 | — |
| 0x00A7 | 0 | 2154 | 1000 | 71,37,118,11 | NN01 | — | — |
| 0x00D4 | 2 | 2008 | 1000 | 184,60,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0119 | 2 | 2009 | 1000 | 184,317,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0162 | 0 | 2368 | 1000 | 10,64,167,11 | — | — | — |
| 0x018B | 0 | 2369 | 1000 | 10,77,167,11 | — | — | — |
| 0x01B4 | 0 | 2370 | 1000 | 10,90,167,11 | — | — | — |
| 0x01DD | 0 | 2371 | 1000 | 10,103,167,11 | — | — | — |
| 0x0206 | 0 | 2372 | 1000 | 10,116,167,11 | — | — | — |
| 0x022F | 0 | 2373 | 1000 | 10,129,167,11 | — | — | — |
| 0x0258 | 0 | 2374 | 1000 | 10,142,167,11 | — | — | — |
| 0x0281 | 0 | 2375 | 1000 | 10,155,167,11 | — | — | — |
| 0x02AA | 0 | 2376 | 1000 | 10,168,167,11 | — | — | — |
| 0x02D3 | 0 | 2377 | 1000 | 10,181,167,11 | — | — | — |
| 0x02FC | 0 | 2378 | 1000 | 10,194,167,11 | — | — | — |
| 0x0325 | 0 | 2379 | 1000 | 10,207,167,11 | — | — | — |
| 0x034E | 0 | 2380 | 1000 | 10,220,167,11 | — | — | — |
| 0x0377 | 0 | 2381 | 1000 | 10,233,167,11 | — | — | — |
| 0x03A0 | 0 | 2382 | 1000 | 10,246,167,11 | — | — | — |
| 0x03C9 | 0 | 2383 | 1000 | 10,259,167,11 | — | — | — |
| 0x03F2 | 0 | 2384 | 1000 | 10,272,167,11 | — | — | — |
| 0x041B | 0 | 2385 | 1000 | 10,285,167,11 | — | — | — |
| 0x0444 | 0 | 2386 | 1000 | 10,298,167,11 | — | — | — |
| 0x046D | 0 | 2387 | 1000 | 10,311,167,11 | — | — | — |
| 0x0496 | 0 | 2388 | 1000 | 10,324,167,11 | — | — | — |
| 0x04BF | 0 | 2389 | 1000 | 10,337,167,11 | — | — | — |
| 0x04E8 | 2 | 3116 | 1000 | 168,1,16,16 | OH01 | — | B_ONLINEHELP.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_CHARACTER_NICK.spr | 1 | 205×363 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CCharInfoTitleItf::MessageMap — L118017 @0x004475CC；直接具名呼叫：CCharInfoTitleItf::OnTitle, CCharInfoTitleItf::OnScrollUp, CCharInfoTitleItf::OnScrollDown, CCharInfoFullItf::MessageMap
- CCharInfoTitleItf::OnUpdate — L118366 @0x00447985；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, VCELMessageArea::?0?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, CSGOUim::GetTitleNameByID, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?9?$CELPtr, UsTitle::?A?$CELArray, VCELMessageArea::?1?$CELPtr
- CCharInfoTitleItf::UpdateData — L118555 @0x00447BC8；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOUim::GetTitleNameByID, VCELMessageArea::?1?$CELPtr
- CCharInfoTitleItf::OnTitle — L118769 @0x00447E28；直接具名呼叫：UsTitle::?A?$CELArray, CSGOUim::SendPacket
- CCharInfoTitleItf::OnScrollUp — L118825 @0x00447E9A；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoTitleItf::OnScrollDown — L118862 @0x00447EE4；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoTitleItf::OnClose — L118954 @0x00447FA1；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoTitleItf::Create — L118977 @0x00447FBA；直接具名呼叫：CSGOInterface::Create, CSGOUim::GetPlayer, VCSGOPlayer::?4?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, CSGOUim::SendPacket

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0038 / ID 2176（type 2，184,1,16,16，NN02，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0079 / ID 2000（type 1，13,37,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00A7 / ID 2154（type 0，71,37,118,11，NN01）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D4 / ID 2008（type 2，184,60,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0119 / ID 2009（type 2，184,317,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0162 / ID 2368（type 0，10,64,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x018B / ID 2369（type 0，10,77,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01B4 / ID 2370（type 0，10,90,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01DD / ID 2371（type 0，10,103,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0206 / ID 2372（type 0，10,116,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x022F / ID 2373（type 0，10,129,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0258 / ID 2374（type 0，10,142,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0281 / ID 2375（type 0，10,155,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02AA / ID 2376（type 0，10,168,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02D3 / ID 2377（type 0，10,181,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02FC / ID 2378（type 0，10,194,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0325 / ID 2379（type 0，10,207,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x034E / ID 2380（type 0，10,220,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0377 / ID 2381（type 0，10,233,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03A0 / ID 2382（type 0，10,246,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C9 / ID 2383（type 0，10,259,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03F2 / ID 2384（type 0，10,272,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x041B / ID 2385（type 0，10,285,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0444 / ID 2386（type 0，10,298,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x046D / ID 2387（type 0，10,311,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0496 / ID 2388（type 0，10,324,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04BF / ID 2389（type 0，10,337,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04E8 / ID 3116（type 2，168,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：未解鎖不可選；更換後自己與他人可見；重登保存。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-047 稱號變更](../gameplay/GP-047.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
