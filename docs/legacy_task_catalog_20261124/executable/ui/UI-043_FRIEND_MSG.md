# UI-043_FRIEND_MSG — 好友訊息視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-043_FRIEND_MSG.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
私訊或好友訊息讀取/回覆；確認原版收發入口。

驗收情境：雙 Client 正確對象、離線和重登規則、長訊息捲動。

## 原版 UI 控制規格
來源：HK/output/Interface/043_FRIEND_MSG.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 11，取得 11。

背景資源：BG_FRIENDMSG_BOX.spr, BG_FRIENDMSG_BOX.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0036 | 2 | 2176 | 1000 | 233,1,16,16 | — | — | CLOSESCREEN.spr |
| 0x0073 | 2 | 2008 | 1000 | 233,24,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x00B8 | 2 | 2009 | 1000 | 233,111,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0101 | 0 | 3477 | 1000 | 12,28,218,13 | — | — | — |
| 0x012A | 8 | 2152 | 1000 | 9,155,215,15 | — | — | — |
| 0x015B | 0 | 2943 | 1000 | 227,155,18,15 | — | — | — |
| 0x0184 | 0 | 2154 | 1000 | 12,4,200,13 | — | — | — |
| 0x01AD | 0 | 2684 | 1000 | 129,28,100,13 | — | — | — |
| 0x01D6 | 2 | 3116 | 1000 | 216,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0219 | 0 | 2001 | 1000 | 12,50,218,13 | — | — | — |
| 0x0242 | 0 | 2076 | 1000 | 12,65,218,70 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_FRIENDMSG_BOX.spr | 1 | 256×175 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CFriendMsgItf::MessageMap — L170885 @0x00466C60；直接具名呼叫：CFriendMsgItf::OnScrollUp, CFriendMsgItf::OnScrollDown, COperationalItf::MessageMap
- CFriendMsgItf::Create — L171128 @0x00466DFF；直接具名呼叫：CSGOInterface::Create, VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?9?$CELPtr, VCELInputArea::?C?$CELPtr, VCELItfComp::?0?$CELPtr, VCSGOInputArea::?C?$CELPtr, CSGOInputArea::SetHScroll, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?4?$CELPtr
- CFriendMsgItf::SetContactEntry — L171516 @0x004671FB；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CFriendMsgItf::OnNewMessage — L171559 @0x0046725A；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CFriendMsgItf::OnScrollUp — L171596 @0x0046729F；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CFriendMsgItf::OnScrollDown — L171631 @0x004672E3；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CFriendMsgItf::ValidateData — L171745 @0x004673E3；直接具名呼叫：VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?7?$CELPtr, VCELInputArea::?1?$CELPtr, VCELInputArea::?C?$CELPtr, YA_NAAVCELString::?8
- CFriendMsgItf::OnSend — L171869 @0x004674FF；直接具名呼叫：VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?C?$CELPtr, CELString::GetSize, VCFriendItf::?0?$CELPtr, VCELInterface::?1?$CELPtr, VCFriendItf::?9?$CELPtr, VCFriendItf::?C?$CELPtr, CFriendItf::UpdateP2PHistory, CFriendItf::ArchiveP2PHistory, CFriendMsgItf::OnNewMessage, CFriendMsgItf::SendMessagePacket
- CFriendMsgItf::OnUpdate — L172129 @0x00467743；直接具名呼叫：COperationalItf::OnUpdate, UsInstantMsg::?A?$CELArray, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOUim::GetPlayer, VCSGOPlayer::?9?$CELPtr, VCSGOPlayer::?C?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCELMessageArea::?4?$CELPtr, YA_NAAVCELString::?9
- CFriendMsgItf::SetAsDefaultFocus — L172692 @0x00467DB6；直接具名呼叫：CELInterface::GetITFUID
- CFriendMsgItf::UpdateInfo — L172742 @0x00467DFD；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELInputArea::?0?$CELPtr, VCELInputArea::?9?$CELPtr, VCELInputArea::?C?$CELPtr, VCELInputArea::?1?$CELPtr, VCELMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0036 / ID 2176（type 2，233,1,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0073 / ID 2008（type 2，233,24,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B8 / ID 2009（type 2，233,111,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0101 / ID 3477（type 0，12,28,218,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x012A / ID 2152（type 8，9,155,215,15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x015B / ID 2943（type 0，227,155,18,15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0184 / ID 2154（type 0，12,4,200,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01AD / ID 2684（type 0，129,28,100,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01D6 / ID 3116（type 2，216,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0219 / ID 2001（type 0，12,50,218,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0242 / ID 2076（type 0，12,65,218,70）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：雙 Client 正確對象、離線和重登規則、長訊息捲動。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-043 聊天頻道與私訊](../gameplay/GP-043.md)
- [GP-044 好友與黑名單](../gameplay/GP-044.md)
- [GP-045 郵件與留言](../gameplay/GP-045.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
