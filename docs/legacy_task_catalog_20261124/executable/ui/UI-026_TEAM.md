# UI-026_TEAM — 組隊視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-026_TEAM.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
建立/邀請/退出、成員列表、HP/MP 與隊長狀態。

驗收情境：雙 Client 邀請、接受、拒絕、退出、換圖及斷線。

## 原版 UI 控制規格
來源：HK/output/Interface/026_TEAM.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 23，取得 23。

背景資源：BG_TEAM.spr, BG_TEAM.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0024 | 2 | 2176 | 1000 | 119,1,16,16 | T16 | — | CLOSESCREEN.spr |
| 0x0064 | 2 | 2493 | 1000 | 4,1,16,16 | T13 | — | B_TEAM_BUILDUP.spr |
| 0x00AA | 2 | 2685 | 1000 | 23,1,16,16 | T14 | — | B_TEAM_GOOUT.spr |
| 0x00EC | 3 | 2481 | 1000 | 10,38,88,3 | T01 | — | HP.spr |
| 0x0134 | 3 | 2487 | 1000 | 100,38,29,3 | T07 | — | MP.spr |
| 0x017C | 3 | 2482 | 1000 | 10,58,88,3 | T02 | — | HP.spr |
| 0x01C4 | 3 | 2483 | 1000 | 10,78,88,3 | T03 | — | HP.spr |
| 0x020C | 3 | 2484 | 1000 | 10,98,88,3 | T04 | — | HP.spr |
| 0x0254 | 3 | 2485 | 1000 | 10,118,88,3 | T05 | — | HP.spr |
| 0x029C | 3 | 2486 | 1000 | 10,138,88,3 | T06 | — | HP.spr |
| 0x02E4 | 3 | 2488 | 1000 | 100,58,29,3 | T08 | — | MP.spr |
| 0x032C | 3 | 2489 | 1000 | 100,78,29,3 | T09 | — | MP.spr |
| 0x0374 | 3 | 2490 | 1000 | 100,98,29,3 | T10 | — | MP.spr |
| 0x03BC | 3 | 2491 | 1000 | 100,118,29,3 | T11 | — | MP.spr |
| 0x0404 | 3 | 2492 | 1000 | 100,138,29,3 | T12 | — | MP.spr |
| 0x044C | 0 | 2475 | 1000 | 9,24,120,12 | — | — | — |
| 0x0475 | 0 | 2476 | 1000 | 9,44,120,12 | — | — | — |
| 0x049E | 0 | 2477 | 1000 | 9,64,120,12 | — | — | — |
| 0x04C7 | 0 | 2478 | 1000 | 9,84,120,12 | — | — | — |
| 0x04F0 | 0 | 2479 | 1000 | 9,104,120,12 | — | — | — |
| 0x0519 | 0 | 2480 | 1000 | 9,124,120,12 | — | — | — |
| 0x0542 | 2 | 3116 | 1000 | 102,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0585 | 2 | 2668 | 1000 | 42,1,16,16 | — | — | B_COMPLEX_TEAM.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_COMPLEX_TEAM.spr | 4 | 16×16 @(0,0) ×4 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_TEAM_BUILDUP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_TEAM_GOOUT.spr | 4 | 16×16 @(0,0) ×4 幀 |
| BG_TEAM.spr | 1 | 139×144 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
| MP.spr | 16 | 109×10 @(0,0) ×16 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CTeamItf::MessageMap — L143610 @0x004562DC；直接具名呼叫：CTeamItf::OnBuildTeam, CTeamItf::OnQuitTeam, CTeamItf::OnOrgMenu, CTeamItf::OnComplex, CTeamItf::OnKickTeamMem, COperationalItf::MessageMap
- CTeamItf::Create — L143834 @0x0045656A；直接具名呼叫：CSGOInterface::Create, VCELProgressBar::?0?$CELPtr, VCELProgressBar::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?C?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELProgressBar::?1?$CELPtr, CTeamItf::OnStartSync
- CTeamItf::OnStartSync — L144049 @0x0045676C；直接具名呼叫：CSGOUim::SendPacket
- CTeamItf::OnClose — L144078 @0x0045679F；直接具名呼叫：CSGOUim::SendPacket
- CTeamItf::UpdateButton — L144106 @0x004567CC；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELUserButton::?9?$CELPtr, VCELUserButton::?C?$CELPtr, VCELUserButton::?1?$CELPtr, CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CSGOChar::GetCharSMode, VCSGOPlayer::?1?$CELPtr
- CTeamItf::UpdateInfo — L144474 @0x00456B81；直接具名呼叫：VCELProgressBar::?0?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?4?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELProgressBar::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELProgressBar::?C?$CELPtr, UsTeamMember::?A?$CELArray, VCELMessageArea::?1?$CELPtr, VCELProgressBar::?1?$CELPtr
- CTeamItf::OnRecvFullList — L144933 @0x00457167；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CTeamItf::OnTeamNewMember — L144997 @0x004571E4；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CTeamItf::OnQuitMember — L145037 @0x00457229；直接具名呼叫：UsTeamMember::?A?$CELArray, CSGOUim::AddSystemMsg
- CTeamItf::OnTeamSyncData — L145125 @0x004572F6；直接具名呼叫：UsTeamMember::?A?$CELArray
- CTeamItf::OnTeamExtSyncData — L145219 @0x004573E7；直接具名呼叫：UsTeamMember::?A?$CELArray
- CTeamItf::OnBuildTeam — L145292 @0x00457476；直接具名呼叫：CSGOUim::SendPacket
- CTeamItf::OnQuitTeam — L145316 @0x00457495；直接具名呼叫：VCSGOInterface::?0?$CELPtr, VCYesNoBox::?C?$CELPtr, CYesNoBox::Create, VCMsgBox::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCSGOInterface::?1?$CELPtr
- CTeamItf::OnTeamQuitLeader — L145421 @0x0045759D；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CTeamItf::OnUpdate — L145443 @0x004575B6；直接具名呼叫：COperationalItf::OnUpdate, CSGOUim::SendPacket, CSGOUim::SetHasTeam, CSGOUim::GetTeamList, CSGOUim::GetPlayer, VCSGOPlayer::?9?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::ClearTM, VCSGOPlayer::?1?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr
- CTeamItf::OnOrgMenu — L145622 @0x0045779A；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CTeamItf::OnComplex — L145641 @0x004577A5；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CSGOChar::GetRobot, VCSGOPlayer::?1?$CELPtr, VCELInterface::?1?$CELPtr, VCELInterface::?8?$CELPtr, VCELInterface::?4?$CELPtr, VCRobotComplexInfoItf::?C?$CELPtr, CRobotComplexInfoItf::SetComplex, CRobotComplexInfoItf::DisplayUsed, VCELInterface::?C?$CELPtr, CSGOInterface::CenterScreen
- CTeamItf::OnKickTeamMem — L145807 @0x0045793D；直接具名呼叫：UsTeamMember::?A?$CELArray, CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCSGOPlayer::?1?$CELPtr, VCELInterface::?9?$CELPtr, VCELInterface::?1?$CELPtr, VCELInterface::?4?$CELPtr, VCKickTeamOKBox::?C?$CELPtr, CKickTeamOKBox::SetKickTeamUid, CYesNoBox::Create, CSGOInterface::CenterScreen

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0024 / ID 2176（type 2，119,1,16,16，T16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0064 / ID 2493（type 2，4,1,16,16，T13，B_TEAM_BUILDUP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AA / ID 2685（type 2，23,1,16,16，T14，B_TEAM_GOOUT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00EC / ID 2481（type 3，10,38,88,3，T01，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0134 / ID 2487（type 3，100,38,29,3，T07，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x017C / ID 2482（type 3，10,58,88,3，T02，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01C4 / ID 2483（type 3，10,78,88,3，T03，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x020C / ID 2484（type 3，10,98,88,3，T04，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0254 / ID 2485（type 3，10,118,88,3，T05，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x029C / ID 2486（type 3，10,138,88,3，T06，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E4 / ID 2488（type 3，100,58,29,3，T08，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x032C / ID 2489（type 3，100,78,29,3，T09，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0374 / ID 2490（type 3，100,98,29,3，T10，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03BC / ID 2491（type 3，100,118,29,3，T11，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0404 / ID 2492（type 3，100,138,29,3，T12，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x044C / ID 2475（type 0，9,24,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0475 / ID 2476（type 0，9,44,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x049E / ID 2477（type 0，9,64,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C7 / ID 2478（type 0，9,84,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04F0 / ID 2479（type 0，9,104,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0519 / ID 2480（type 0，9,124,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0542 / ID 3116（type 2，102,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0585 / ID 2668（type 2，42,1,16,16，B_COMPLEX_TEAM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：雙 Client 邀請、接受、拒絕、退出、換圖及斷線。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-009 玩家 AOI 進出與同步](../gameplay/GP-009.md)
- [GP-023 Buff/Debuff 與治療](../gameplay/GP-023.md)
- [GP-041 組隊邀請與隊長](../gameplay/GP-041.md)
- [GP-042 組隊戰鬥共享與狀態](../gameplay/GP-042.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
