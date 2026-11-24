# UI-044_QUESTION_BOX — 問答/確認框（044_QUESTION_BOX）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-044_QUESTION_BOX.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
選項、確認、取消、模態輸入；分辨 A/B 變體。

驗收情境：正確呼叫者、重複點擊、Esc 與選項回傳。

## 原版 UI 控制規格
來源：HK/output/Interface/044_QUESTION_BOX.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 4，取得 4。

背景資源：BG_QUESTION_BOX.spr, BG_QUESTION_BOX.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0034 | 2 | 2176 | 1000 | 444,1,16,16 | — | — | CLOSESCREEN.spr |
| 0x0071 | 2 | 2074 | 1000 | 424,1,16,16 | — | — | B_OK_BUTTON.spr |
| 0x00AE | 0 | 2495 | 1000 | 9,3,382,12 | — | — | — |
| 0x00D7 | 0 | 2943 | 1000 | 393,3,17,12 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_OK_BUTTON.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_QUESTION_BOX.spr | 1 | 469×19 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CReplierItf::SetStartime — L104993 @0x0043F720；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CReplierItf::MessageMap — L108073 @0x0044107C；直接具名呼叫：CYesNoBox::MessageMap
- CReplierItf::Create — L108188 @0x00441178；直接具名呼叫：CSGOInterface::Create, CSGOCore::GetSysTimer, CReplierItf::AdjustPosition, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CReplierItf::OnUpdate — L108467 @0x004413E1；直接具名呼叫：CMsgBox::OnUpdate, CSGOCore::GetSysTimer, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CReplierItf::SetQuestionerUID — L109090 @0x0044197D；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CReplierItf::SetQuestion — L109236 @0x00441AAE；直接具名呼叫：CReplierItf::HandleBusyReply
- CReplierItf::SetMessage — L109292 @0x00441B1F；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCSGOMessageArea::?C?$CELPtr, CSGOCore::GetSysTimer, VCELMessageArea::?1?$CELPtr
- CReplierItf::OnYes — L109455 @0x00441C9B；直接具名呼叫：CReplierItf::HandleReply
- CReplierItf::OnNOReply — L109511 @0x00441D25；直接具名呼叫：CSGOUim::SendPacket
- CReplierItf::OnNo — L109626 @0x00441E4C；直接具名呼叫：CReplierItf::HandleReply
- CReplierItf::SetReturnCmd — L109682 @0x00441ED6；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CReplierItf::SetCallBackNOREPLY — L109718 @0x00441F15；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CReplierItf::SetCallBackBUSY — L109749 @0x00441F46；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0034 / ID 2176（type 2，444,1,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0071 / ID 2074（type 2，424,1,16,16，B_OK_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AE / ID 2495（type 0，9,3,382,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D7 / ID 2943（type 0，393,3,17,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：正確呼叫者、重複點擊、Esc 與選項回傳。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-019 玩家死亡與復活](../gameplay/GP-019.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
