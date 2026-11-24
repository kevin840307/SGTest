# UI-029_SYSTEM — 系統設定視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-029_SYSTEM.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
音效/畫面/操作與退出等原版設定項目；不擅自新增原版未有控件。

驗收情境：修改/取消/恢復設定；重新開啟與重登一致；不影響網路 tick。

## 原版 UI 控制規格
來源：HK/output/Interface/029_SYSTEM.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 22，取得 22。

背景資源：BG_SYSTEM.spr, BG_SYSTEM.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0028 | 2 | 2176 | 1000 | 270,1,16,16 | SS01 | — | CLOSESCREEN.spr |
| 0x0069 | 1 | 2000 | 1000 | 37,71,72,12 | — | 背景音樂音量 | — |
| 0x009B | 1 | 2000 | 1000 | 35,105,48,12 | — | 音效音量 | — |
| 0x00C9 | 2 | 2545 | 1000 | 121,69,17,17 | — | — | MINISCREEN.spr |
| 0x0104 | 2 | 2544 | 1000 | 175,69,16,16 | — | — | ADD_POINT.spr |
| 0x013D | 2 | 2546 | 1000 | 94,102,17,17 | — | — | MINISCREEN.spr |
| 0x0178 | 2 | 2543 | 1000 | 148,102,16,16 | — | — | ADD_POINT.spr |
| 0x01B1 | 2 | 2539 | 1000 | 27,168,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x01F2 | 2 | 2540 | 1000 | 125,168,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0233 | 1 | 2000 | 1000 | 32,172,72,12 | — | 回選擇伺服器 | — |
| 0x0265 | 1 | 2000 | 1000 | 141,172,48,12 | — | 離開遊戲 | — |
| 0x0293 | 0 | 2541 | 1000 | 145,71,22,11 | — | — | — |
| 0x02BC | 0 | 2542 | 1000 | 118,105,22,11 | — | — | — |
| 0x02E5 | 2 | 3116 | 1000 | 253,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0328 | 2 | 2598 | 1000 | 144,34,17,17 | — | — | MINISCREEN.spr |
| 0x0363 | 2 | 2597 | 1000 | 198,34,16,16 | — | — | ADD_POINT.spr |
| 0x039C | 0 | 3065 | 1000 | 168,37,22,11 | — | — | — |
| 0x03C5 | 1 | 2000 | 1000 | 61,37,72,12 | — | 高畫質流暢度 | — |
| 0x03F7 | 2 | 2600 | 1000 | 69,136,AUTO? (0xCDCDCDCD),AUTO? (0xCDCDCDCD) | — | — | B_PVP_LEFT.spr |
| 0x0432 | 2 | 2599 | 1000 | 177,137,AUTO? (0xCDCDCDCD),AUTO? (0xCDCDCDCD) | — | — | B_PVP_RIGHT.spr |
| 0x046F | 0 | 2592 | 1000 | 91,139,79,11 | — | — | — |
| 0x0498 | 1 | 2000 | 1000 | 12,139,48,12 | — | 戰鬥模式 | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| ADD_POINT.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_PVP_LEFT.spr | 4 | 17×17 @(0,0) ×2 幀；15×15 @(0,0) ×2 幀 |
| B_PVP_RIGHT.spr | 4 | 15×15 @(0,0) ×4 幀 |
| BG_SYSTEM.spr | 1 | 291×207 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| MINISCREEN.spr | 4 | 17×17 @(0,0) ×4 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CSystemMenuItf::MessageMap — L140059 @0x004541BC；直接具名呼叫：CSystemMenuItf::OnBGMUp, CSystemMenuItf::OnBGMDown, CSystemMenuItf::OnSFXUp, CSystemMenuItf::OnSFXDown, CSystemMenuItf::OnSelectServer, CSystemMenuItf::OnExit, CSystemMenuItf::On50FPS, CSystemMenuItf::On25FPS, CSystemMenuItf::OnPKUP, CSystemMenuItf::OnPKDOWN, COperationalItf::MessageMap
- CSystemMenuItf::Create — L140235 @0x00454356；直接具名呼叫：CSGOInterface::Create, CSGOUim::GetSound, CELSound::GetGlobalSourceVolume
- CSystemMenuItf::OnBGMUp — L140302 @0x004543F0；直接具名呼叫：CSGOUim::GetSound, CELSound::SetGlobalSourceVolume
- CSystemMenuItf::OnBGMDown — L140374 @0x004544AA；直接具名呼叫：CSGOUim::GetSound, CELSound::SetGlobalSourceVolume
- CSystemMenuItf::OnSFXUp — L140722 @0x00454813；直接具名呼叫：CSGOUim::GetSound, CELSound::SetGlobalSourceVolume
- CSystemMenuItf::OnSFXDown — L140794 @0x004548CD；直接具名呼叫：CSGOUim::GetSound, CELSound::SetGlobalSourceVolume
- CSystemMenuItf::OnUpdate — L140866 @0x00454984；直接具名呼叫：CSGOUim::GetHasTeam, CSGOUim::SendPacket, CSGOUim::GetGameManager, CELGameManager::GameExit, CSGOUim::SaveProfile, CELApplication::GetGameMgr, CELGameManager::GetComponent, CSGOMiscDraw::ClearExpireWarningStr, CSystemMenuItf::CloseMsgBox, CSystemMenuItf::ICQLogout, CSGOUim::GetPlayer, VCSGOPlayer::?9?$CELPtr
- CSystemMenuItf::OnExit — L141329 @0x00454EB4；直接具名呼叫：VCSGOInterface::?0?$CELPtr, VCYesNoBox::?C?$CELPtr, CYesNoBox::Create, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCMsgBox::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCSGOInterface::?1?$CELPtr
- CSystemMenuItf::OnSelectServer — L141443 @0x00454FD7；直接具名呼叫：VCSGOInterface::?0?$CELPtr, VCYesNoBox::?C?$CELPtr, CYesNoBox::Create, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCMsgBox::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCSGOInterface::?1?$CELPtr
- CSystemMenuItf::On50FPS — L141557 @0x004550FA；直接具名呼叫：CSGOUim::GetGameManager, CELGameManager::GetDisplayMode, CELGameManager::SetUpDisplayMode, CSGOCore::SaveDisplayFPSDat
- CSystemMenuItf::On25FPS — L141595 @0x0045513E；直接具名呼叫：CSGOUim::GetGameManager, CELGameManager::GetDisplayMode, CELGameManager::SetUpDisplayMode, CSGOCore::SaveDisplayFPSDat
- CSystemMenuItf::OnPKUP — L141633 @0x00455181；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSystemMenuItf::OnPKDOWN — L141668 @0x004551CC；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0028 / ID 2176（type 2，270,1,16,16，SS01，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0069 / ID 2000（type 1，37,71,72,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x009B / ID 2000（type 1，35,105,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00C9 / ID 2545（type 2，121,69,17,17，MINISCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0104 / ID 2544（type 2，175,69,16,16，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x013D / ID 2546（type 2，94,102,17,17，MINISCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0178 / ID 2543（type 2，148,102,16,16，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01B1 / ID 2539（type 2，27,168,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01F2 / ID 2540（type 2，125,168,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0233 / ID 2000（type 1，32,172,72,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0265 / ID 2000（type 1，141,172,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0293 / ID 2541（type 0，145,71,22,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02BC / ID 2542（type 0，118,105,22,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E5 / ID 3116（type 2，253,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0328 / ID 2598（type 2，144,34,17,17，MINISCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0363 / ID 2597（type 2，198,34,16,16，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x039C / ID 3065（type 0，168,37,22,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C5 / ID 2000（type 1，61,37,72,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03F7 / ID 2600（type 2，69,136,AUTO? (0xCDCDCDCD),AUTO? (0xCDCDCDCD)，B_PVP_LEFT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0432 / ID 2599（type 2，177,137,AUTO? (0xCDCDCDCD),AUTO? (0xCDCDCDCD)，B_PVP_RIGHT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x046F / ID 2592（type 0，91,139,79,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0498 / ID 2000（type 1，12,139,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：修改/取消/恢復設定；重新開啟與重登一致；不影響網路 tick。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-068 系統設定與登出](../gameplay/GP-068.md)
- [GP-079 遊戲內資源生命週期與效能](../gameplay/GP-079.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
