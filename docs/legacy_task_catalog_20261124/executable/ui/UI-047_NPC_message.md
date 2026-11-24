# UI-047_NPC_message — NPC 對話主框（047_NPC_message）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-047_NPC_message.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
NPC 文字、頭像、選項、下一步/關閉；依變體驗證。

驗收情境：點 NPC→對話→選項→Server 回應；距離/重複/換圖安全。

## 原版 UI 控制規格
來源：HK/output/Interface/047_NPC_message.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 5，取得 5。

背景資源：BG_NPC_MESSAGE.spr, BG_NPC_MESSAGE.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0032 | 0 | 2076 | 1000 | 25,10,345,80 | — | — | — |
| 0x005B | 0 | 3070 | 1000 | 26,108,345,13 | — | — | — |
| 0x0084 | 0 | 3071 | 1000 | 26,123,345,13 | — | — | — |
| 0x00AD | 0 | 3072 | 1000 | 26,138,345,13 | — | — | — |
| 0x00D6 | 0 | 3073 | 1000 | 26,153,345,13 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| BG_NPC_MESSAGE.spr | 1 | 399×172 @(0,0) ×1 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CQuestItf::MessageMap — L219935 @0x00484CCC；直接具名呼叫：CQuestItf::OnChoice, COperationalItf::MessageMap
- CQuestItf::OnUpdate — L220141 @0x00484ECF；直接具名呼叫：COperationalItf::OnUpdate, VCELMessageArea::?0?$CELPtr, CQuestItf::OnClickMessage, UNPCMessage::?A?$CELArray, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCSGOMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CQuestItf::Create — L221100 @0x00485856；直接具名呼叫：CSGOUim::GetNPCMessage, CSGOInterface::Create, CQuestItf::OpenBalloon, CQuestItf::DefaultChoiceBalloon, CQuestItf::DisplayChoice, CQuestItf::InitBigNPC, CQuestItf::DefaultBalloon, CQuestItf::DisplayMessageNewBie, CQuestItf::DisplayMessage
- CQuestItf::OnClickMessage — L221736 @0x00485F29；直接具名呼叫：VCSGOInterface::?0?$CELPtr, UNPCMessage::?A?$CELArray, VCQuestItf::?C?$CELPtr, CQuestItf::Create, VCELInterface::?0?$CELPtr, VCSGOInterface::?C?$CELPtr, VCSGOInterface::?9?$CELPtr, CELDisplay::GetDimension, VCSGOInterface::?1?$CELPtr, CSGOUim::SendPacket
- CQuestItf::OnChoice — L222020 @0x00486221；直接具名呼叫：UNPCMessage::?A?$CELArray, CSGOUim::SendPacket
- CQuestItf::OnMouseClick — L222100 @0x004862BF；直接具名呼叫：VCELInterface::?9?$CELPtr, VCELInterface::?1?$CELPtr, CSGOInterface::OnMouseClick, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, CQuestItf::OnClickMessage, VCELMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0032 / ID 2076（type 0，25,10,345,80）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x005B / ID 3070（type 0，26,108,345,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0084 / ID 3071（type 0，26,123,345,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AD / ID 3072（type 0，26,138,345,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D6 / ID 3073（type 0，26,153,345,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：點 NPC→對話→選項→Server 回應；距離/重複/換圖安全。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-029 NPC 互動與腳本分支](../gameplay/GP-029.md)
- [GP-049 任務接取與進度](../gameplay/GP-049.md)
- [GP-050 任務完成與獎勵](../gameplay/GP-050.md)
- [GP-070 禮物/邀請獎勵](../gameplay/GP-070.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
