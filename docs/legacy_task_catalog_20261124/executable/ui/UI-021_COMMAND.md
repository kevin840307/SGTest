# UI-021_COMMAND — 主功能工具列（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-021_COMMAND.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
主選單各按鈕實際入口、顯示/隱藏與相鄰面板互斥。

驗收情境：每個已確認入口能開關對應面板；世界操作與快捷鍵不衝突。

## 原版 UI 控制規格
來源：HK/output/Interface/021_COMMAND.itf；C.D.E. 解包：成功；解析狀態：COUNT_MATCH；宣告控制項 9，取得 9。

背景資源：COMMAND.spr, COMMAND.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0024 | 2 | 3335 | 1000 | 3,113,24,23 | B04 | — | B_BASIC_GUILD.spr |
| 0x0068 | 2 | 2284 | 1000 | 3,32,24,23 | B02 | — | B_BASIC_ITEM.spr |
| 0x00AA | 2 | 2281 | 1000 | 3,5,24,23 | B01 | — | B_BASIC_PLAYER.spr |
| 0x00F0 | 2 | 2285 | 1000 | 3,59,24,23 | B03 | — | B_BASIC_SKILL.spr |
| 0x0134 | 2 | 2287 | 1000 | 2,222,24,23 | B05 | — | B_BASIC_SYSTEM.spr |
| 0x017A | 2 | 2286 | 1000 | 2,86,24,23 | B06 | — | B_COMMAND_TEAM.spr |
| 0x01C0 | 0 | 2684 | 1000 | 2,196,25,22 | B10 | — | B_BASIC_GM.spr |
| 0x01FB | 2 | 3604 | 1000 | 4,142,21,19 | B11 | — | B_BASIC_PSHOP.spr |
| 0x023F | 0 | 3652 | 1000 | 2,169,25,22 | B12 | — | B_BASIC_QUEST.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_BASIC_GM.spr | 4 | 23×19 @(-1,-3) ×2 幀；24×22 @(0,-1) ×1 幀；23×18 @(-1,-3) ×1 幀 |
| B_BASIC_GUILD.spr | 4 | 24×23 @(0,0) ×4 幀 |
| B_BASIC_ITEM.spr | 4 | 24×23 @(0,0) ×4 幀 |
| B_BASIC_PLAYER.spr | 4 | 24×23 @(0,0) ×4 幀 |
| B_BASIC_PSHOP.spr | 4 | 21×19 @(-2,-3) ×4 幀 |
| B_BASIC_QUEST.spr | 4 | 20×20 @(-1,-2) ×3 幀；19×19 @(-1,-2) ×1 幀 |
| B_BASIC_SKILL.spr | 4 | 24×23 @(0,0) ×3 幀；24×24 @(0,0) ×1 幀 |
| B_BASIC_SYSTEM.spr | 4 | 24×23 @(0,0) ×4 幀 |
| B_COMMAND_TEAM.spr | 4 | 24×23 @(0,0) ×4 幀 |
| COMMAND.spr | 1 | 30×249 @(0,0) ×1 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CToolBarItf::MessageMap — L174414 @0x00468C5C；直接具名呼叫：CToolBarItf::OnPlayer, CToolBarItf::OnItem, CToolBarItf::OnSkill, CToolBarItf::OnTeam, CToolBarItf::OnClub, CToolBarItf::OnAlly, CToolBarItf::OnGmHelp, CToolBarItf::OnSelling, CToolBarItf::OnSystem, CToolBarItf::OnFriend
- CToolBarItf::Create — L174522 @0x00468D56；直接具名呼叫：CSGOInterface::Create
- CToolBarItf::OnUpdate — L174606 @0x00468DD6；直接具名呼叫：COperationalItf::OnUpdate, VCELUserButton::?0?$CELPtr, VCELUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELUserButton::?9?$CELPtr, VCELUserButton::?C?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELInterface::?7?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnPlayer — L175018 @0x0046920C；直接具名呼叫：VCELInterface::?7?$CELPtr, VCCharInfoFullItf::?0?$CELPtr, VCCharInfoFullItf::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCCharInfoFullItf::?1?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnItem — L175155 @0x00469340；直接具名呼叫：VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCELInterface::?7?$CELPtr, VCELInterface::?0?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?9?$CELPtr
- CToolBarItf::OnSkill — L175468 @0x0046963A；直接具名呼叫：VCELInterface::?7?$CELPtr, VCSkillItf::?0?$CELPtr, VCSkillItf::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCSkillItf::?1?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnFriend — L175605 @0x0046976E；直接具名呼叫：VCELInterface::?7?$CELPtr, VCFriendSItf::?0?$CELPtr, VCFriendSItf::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCFriendSItf::?1?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnTeam — L175742 @0x004698A2；直接具名呼叫：VCELInterface::?7?$CELPtr, VCTeamItf::?0?$CELPtr, VCTeamItf::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCTeamItf::?1?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnClub — L175879 @0x004699D3；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CToolBarItf::OnAlly — L175898 @0x004699DE；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CToolBarItf::OnSelling — L175917 @0x004699E9；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?8?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetSelling, VCELInterface::?9?$CELPtr, VSellingOpenItf::?C?$CELPtr, SellingOpenItf::ToggleHide, VCELInterface::?1?$CELPtr, CSGOUim::SendPacket
- CToolBarItf::OnSystem — L176050 @0x00469B51；直接具名呼叫：VCELInterface::?7?$CELPtr, VCSystemMenuItf::?0?$CELPtr, VCSystemMenuItf::?C?$CELPtr, VCELInterface::?0?$CELPtr, VCSystemMenuItf::?1?$CELPtr, VCELInterface::?C?$CELPtr, VCELInterface::?1?$CELPtr
- CToolBarItf::OnGmHelp — L176187 @0x00469C82；直接具名呼叫：VCELInterface::?7?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCChatItf::?C?$CELPtr, CChatItf::ToggleGMMode

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0024 / ID 3335（type 2，3,113,24,23，B04，B_BASIC_GUILD.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0068 / ID 2284（type 2，3,32,24,23，B02，B_BASIC_ITEM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AA / ID 2281（type 2，3,5,24,23，B01，B_BASIC_PLAYER.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00F0 / ID 2285（type 2，3,59,24,23，B03，B_BASIC_SKILL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0134 / ID 2287（type 2，2,222,24,23，B05，B_BASIC_SYSTEM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x017A / ID 2286（type 2，2,86,24,23，B06，B_COMMAND_TEAM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01C0 / ID 2684（type 0，2,196,25,22，B10，B_BASIC_GM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01FB / ID 3604（type 2，4,142,21,19，B11，B_BASIC_PSHOP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x023F / ID 3652（type 0，2,169,25,22，B12，B_BASIC_QUEST.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：每個已確認入口能開關對應面板；世界操作與快捷鍵不衝突。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-071 GM 管理功能](../gameplay/GP-071.md)
- [GP-076 原版 UI 素材共用載入與字型](../gameplay/GP-076.md)
- [GP-077 五個 C.D.E. ITF 解密與控件驗證](../gameplay/GP-077.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
