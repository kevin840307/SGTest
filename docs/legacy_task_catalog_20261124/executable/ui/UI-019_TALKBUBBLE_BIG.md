# UI-019_TALKBUBBLE_BIG — 對話泡泡樣式選單（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-019_TALKBUBBLE_BIG.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
樣式選擇及預覽，套用至聊天泡泡。

驗收情境：每種可用樣式可選；聊天泡泡正確顯示；取消不改設定。

## 原版 UI 控制規格
來源：HK/output/Interface/019_TALKBUBBLE_BIG.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 9，取得 9。

背景資源：BG_TALKBUBBLE.spr, BG_TALKBUBBLE.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0030 | 2 | 2176 | 1000 | 195,6,16,16 | — | — | CLOSESCREEN.spr |
| 0x006D | 2 | 2259 | 1000 | 18,3,14,24 | — | — | B_NEXT.spr |
| 0x00A0 | 2 | 2010 | 1000 | 3,3,14,24 | — | — | B_BACK.spr |
| 0x00D3 | 0 | 2260 | 1000 | 34,3,24,24 | — | — | ICON_BUBBLE.spr |
| 0x010C | 0 | 2261 | 1000 | 61,3,24,24 | — | — | ICON_BUBBLE.spr |
| 0x0145 | 0 | 2262 | 1000 | 88,3,24,24 | — | — | ICON_BUBBLE.spr |
| 0x017E | 0 | 2263 | 1000 | 115,3,24,24 | — | — | ICON_BUBBLE.spr |
| 0x01B7 | 0 | 2264 | 1000 | 142,3,24,24 | — | — | ICON_BUBBLE.spr |
| 0x01F0 | 0 | 2265 | 1000 | 169,3,24,24 | — | — | ICON_BUBBLE.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_BACK.spr | 3 | 14×24 @(0,0) ×3 幀 |
| B_NEXT.spr | 3 | 14×24 @(0,0) ×3 幀 |
| BG_TALKBUBBLE.spr | 1 | 216×30 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| ICON_BUBBLE.spr | 12 | 24×24 @(0,0) ×12 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CBalloonMenuItf::MessageMap — L142025 @0x0045545C；直接具名呼叫：CBalloonMenuItf::OnNext, CBalloonMenuItf::OnBack, CBalloonMenuItf::OnSetBalloon, COperationalItf::MessageMap
- CBalloonMenuItf::Create — L142219 @0x004556ED；直接具名呼叫：CSGOInterface::Create, CSGOUim::GetLibrary, CSGOLibrary::GetBalloonData
- CBalloonMenuItf::OnSetBalloon — L142258 @0x00455743；直接具名呼叫：VCELInterface::?0?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCELInterface::?9?$CELPtr, VCChatItf::?C?$CELPtr, CChatItf::SetBBType
- CBalloonMenuItf::OnClose — L142390 @0x0045587C；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CBalloonMenuItf::UpdateButton — L142413 @0x00455895；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELUserButton::?1?$CELPtr
- CBalloonMenuItf::OnNext — L142560 @0x004559ED；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CBalloonMenuItf::OnBack — L142596 @0x00455A32；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0030 / ID 2176（type 2，195,6,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x006D / ID 2259（type 2，18,3,14,24，B_NEXT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00A0 / ID 2010（type 2，3,3,14,24，B_BACK.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D3 / ID 2260（type 0，34,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x010C / ID 2261（type 0，61,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0145 / ID 2262（type 0，88,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x017E / ID 2263（type 0，115,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01B7 / ID 2264（type 0，142,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01F0 / ID 2265（type 0，169,3,24,24，ICON_BUBBLE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：每種可用樣式可選；聊天泡泡正確顯示；取消不改設定。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-046 表情與泡泡樣式](../gameplay/GP-046.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
