# UI-015_Character_JOB — 職業清單與轉職（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-015_Character_JOB.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
職業列表、已解鎖條件、轉職入口與回應。

驗收情境：符合/不符條件；轉職後技能與屬性更新。

## 原版 UI 控制規格
來源：HK/output/Interface/015_Character_JOB.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 28，取得 28。

背景資源：BG_CHARACTER_JOB.spr, BG_CHARACTER_JOB.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0036 | 2 | 2176 | 1000 | 184,1,16,16 | NN05 | — | CLOSESCREEN.spr |
| 0x0077 | 0 | 2155 | 1000 | 68,37,118,11 | NN03 | — | — |
| 0x00A4 | 2 | 2008 | 1000 | 184,86,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x00E9 | 2 | 2009 | 1000 | 184,317,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0132 | 0 | 2391 | 1000 | 10,90,167,11 | — | — | — |
| 0x015B | 0 | 2392 | 1000 | 10,103,167,11 | — | — | — |
| 0x0184 | 0 | 2393 | 1000 | 10,116,167,11 | — | — | — |
| 0x01AD | 0 | 2394 | 1000 | 10,129,167,11 | — | — | — |
| 0x01D6 | 0 | 2395 | 1000 | 10,142,167,11 | — | — | — |
| 0x01FF | 0 | 2396 | 1000 | 10,155,167,11 | — | — | — |
| 0x0228 | 0 | 2397 | 1000 | 10,168,167,11 | — | — | — |
| 0x0251 | 0 | 2398 | 1000 | 10,181,167,11 | — | — | — |
| 0x027A | 0 | 2399 | 1000 | 10,194,167,11 | — | — | — |
| 0x02A3 | 0 | 2400 | 1000 | 10,207,167,11 | — | — | — |
| 0x02CC | 0 | 2401 | 1000 | 10,220,167,11 | — | — | — |
| 0x02F5 | 0 | 2402 | 1000 | 10,233,167,11 | — | — | — |
| 0x031E | 0 | 2403 | 1000 | 10,246,167,11 | — | — | — |
| 0x0347 | 0 | 2404 | 1000 | 10,259,167,11 | — | — | — |
| 0x0370 | 0 | 2405 | 1000 | 10,272,167,11 | — | — | — |
| 0x0399 | 0 | 2406 | 1000 | 10,285,167,11 | — | — | — |
| 0x03C2 | 0 | 2407 | 1000 | 10,298,167,11 | — | — | — |
| 0x03EB | 0 | 2408 | 1000 | 10,311,167,11 | — | — | — |
| 0x0414 | 0 | 2409 | 1000 | 10,324,167,11 | — | — | — |
| 0x043D | 0 | 2410 | 1000 | 10,337,167,11 | — | — | — |
| 0x0466 | 0 | 2411 | 1000 | 68,53,118,11 | NN04 | — | — |
| 0x0493 | 1 | 2000 | 1000 | 13,53,48,12 | — | 職業狀態 | — |
| 0x04C1 | 1 | 2000 | 1000 | 13,37,48,12 | — | 現在職業 | — |
| 0x04EF | 2 | 3116 | 1000 | 168,1,16,16 | OH01 | — | B_ONLINEHELP.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_CHARACTER_JOB.spr | 1 | 205×363 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CCharInfoJobItf::MessageMap — L114711 @0x0044541C；直接具名呼叫：CCharInfoJobItf::OnJob, CCharInfoJobItf::OnScrollUp, CCharInfoJobItf::OnScrollDown, CCharInfoFullItf::MessageMap
- CCharInfoJobItf::UpdateData — L115045 @0x0044579D；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, VCSGOPlayer::?1?$CELPtr, CCharInfoFullItf::GetJobNameByID
- CCharInfoJobItf::OnJob — L115206 @0x00445986；直接具名呼叫：UsJob::?A?$CELArray, CCharInfoJobItf::GetJobIDByName, CSGOUim::SendPacket
- CCharInfoJobItf::OnUpdate — L115470 @0x00445C00；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, VCELMessageArea::?0?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, CCharInfoFullItf::GetJobNameByID, VCELMessageArea::?C?$CELPtr, CCharInfoJobItf::CalJobCounter, VCELMessageArea::?9?$CELPtr, UsJob::?A?$CELArray, VCELMessageArea::?1?$CELPtr
- CCharInfoJobItf::OnClose — L115833 @0x004460AF；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoJobItf::OnScrollUp — L115856 @0x004460C8；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoJobItf::OnScrollDown — L115893 @0x00446112；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCharInfoJobItf::Create — L115985 @0x004461CF；直接具名呼叫：CSGOInterface::Create, CSGOUim::GetPlayer, VCSGOPlayer::?4?$CELPtr, VCSGOPlayer::?1?$CELPtr, CSGOUim::SendPacket, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0036 / ID 2176（type 2，184,1,16,16，NN05，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0077 / ID 2155（type 0，68,37,118,11，NN03）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00A4 / ID 2008（type 2，184,86,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00E9 / ID 2009（type 2，184,317,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0132 / ID 2391（type 0，10,90,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x015B / ID 2392（type 0，10,103,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0184 / ID 2393（type 0，10,116,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01AD / ID 2394（type 0，10,129,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01D6 / ID 2395（type 0，10,142,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01FF / ID 2396（type 0，10,155,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0228 / ID 2397（type 0，10,168,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0251 / ID 2398（type 0，10,181,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x027A / ID 2399（type 0，10,194,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02A3 / ID 2400（type 0，10,207,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02CC / ID 2401（type 0，10,220,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02F5 / ID 2402（type 0，10,233,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x031E / ID 2403（type 0，10,246,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0347 / ID 2404（type 0，10,259,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0370 / ID 2405（type 0，10,272,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0399 / ID 2406（type 0，10,285,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C2 / ID 2407（type 0，10,298,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03EB / ID 2408（type 0，10,311,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0414 / ID 2409（type 0，10,324,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x043D / ID 2410（type 0，10,337,167,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0466 / ID 2411（type 0，68,53,118,11，NN04）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0493 / ID 2000（type 1，13,53,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C1 / ID 2000（type 1，13,37,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04EF / ID 3116（type 2，168,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：符合/不符條件；轉職後技能與屬性更新。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-020 技能學習與職業限制](../gameplay/GP-020.md)
- [GP-048 轉職與職業條件](../gameplay/GP-048.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
