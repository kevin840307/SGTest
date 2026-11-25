# UI-018_EMOTION_BIG — 表情選單（獨立執行規格）

狀態：TARGETED PASS（2026-11-26 headless + 真 C# AES 雙 Client authority 已驗；native visible HK A/B / OS hitbox 與原版 action-permit 連點節流仍 NOT VERIFIED）；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-018_EMOTION_BIG.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
表情分頁、選擇與顯示事件。

驗收情境：合法表情發送、其他玩家可見、重複與冷卻規則核對。

## 原版 UI 控制規格
來源：HK/output/Interface/018_EMOTION_BIG.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 17，取得 17。

背景資源：BG_EMOTION.spr, BG_EMOTION.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x002A | 2 | 2176 | 1000 | 222,6,16,16 | — | — | CLOSESCREEN.spr |
| 0x0067 | 2 | 2259 | 1000 | 18,3,14,24 | — | — | B_NEXT.spr |
| 0x009A | 2 | 2010 | 1000 | 3,3,14,24 | — | — | B_BACK.spr |
| 0x00CD | 4 | 2113 | 1000 | 33,3,24,24 | — | — | — |
| 0x00EA | 4 | 2114 | 1000 | 60,3,24,24 | — | — | — |
| 0x0107 | 4 | 2115 | 1000 | 87,3,24,24 | — | — | — |
| 0x0124 | 4 | 2117 | 1000 | 114,3,24,24 | — | — | — |
| 0x0141 | 4 | 2116 | 1000 | 141,3,24,24 | — | — | — |
| 0x015E | 4 | 2118 | 1000 | 168,3,24,24 | — | — | — |
| 0x017B | 4 | 2119 | 1000 | 195,3,24,24 | — | — | — |
| 0x0198 | 2 | 2450 | 1000 | 33,3,24,24 | EMT01 | — | EMOTIONAL_ICON.spr |
| 0x01E0 | 2 | 2451 | 1000 | 60,3,24,24 | EMT02 | — | EMOTIONAL_ICON.spr |
| 0x0228 | 2 | 2452 | 1000 | 87,3,24,24 | EMT03 | — | EMOTIONAL_ICON.spr |
| 0x0270 | 2 | 2453 | 1000 | 114,3,24,24 | EMT04 | — | EMOTIONAL_ICON.spr |
| 0x02B8 | 2 | 2454 | 1000 | 141,3,24,24 | EMT05 | — | EMOTIONAL_ICON.spr |
| 0x0300 | 2 | 2455 | 1000 | 168,3,24,24 | EMT06 | — | EMOTIONAL_ICON.spr |
| 0x0348 | 2 | 2456 | 1000 | 195,3,24,24 | EMT07 | — | EMOTIONAL_ICON.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_BACK.spr | 3 | 14×24 @(0,0) ×3 幀 |
| B_NEXT.spr | 3 | 14×24 @(0,0) ×3 幀 |
| BG_EMOTION.spr | 1 | 242×30 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| EMOTIONAL_ICON.spr | 12 | 24×24 @(0,0) ×12 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CEmotionMenuItf::MessageMap — L135267 @0x004517EC；直接具名呼叫：CEmotionMenuItf::OnNext, CEmotionMenuItf::OnBack, CEmotionMenuItf::OnSetEmotion, COperationalItf::MessageMap
- CEmotionMenuItf::Create — L135509 @0x00451A2F；直接具名呼叫：CSGOInterface::Create, CSGOUim::GetLibrary, CSGOLibrary::GetEmtCmdData, CEmotionMenuItf::RefreshButton
- CEmotionMenuItf::RefreshButton — L135612 @0x00451B14；直接具名呼叫：CSGOUim::GetLibrary, CSGOLibrary::GetEmtCmdData, VCSGOUserButton::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOUserButton::?9?$CELPtr, PAVEmotionData::?A?$CELArray, UEMTCMDDATA_::?A?$CELArray, VCSGOUserButton::?C?$CELPtr, VCSGOUserButton::?1?$CELPtr
- CEmotionMenuItf::OnSetEmotion — L135800 @0x00451D02；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?7?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::SetEmotion
- CEmotionMenuItf::OnUpdate — L135892 @0x00451DD3；直接具名呼叫：COperationalItf::OnUpdate
- CEmotionMenuItf::OnNext — L135917 @0x00451DF4；直接具名呼叫：CEmotionMenuItf::RefreshButton
- CEmotionMenuItf::OnBack — L135952 @0x00451E38；直接具名呼叫：CEmotionMenuItf::RefreshButton

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x002A / ID 2176（type 2，222,6,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0067 / ID 2259（type 2，18,3,14,24，B_NEXT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x009A / ID 2010（type 2，3,3,14,24，B_BACK.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00CD / ID 2113（type 4，33,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00EA / ID 2114（type 4，60,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0107 / ID 2115（type 4，87,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0124 / ID 2117（type 4，114,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0141 / ID 2116（type 4，141,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x015E / ID 2118（type 4，168,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x017B / ID 2119（type 4，195,3,24,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0198 / ID 2450（type 2，33,3,24,24，EMT01，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01E0 / ID 2451（type 2，60,3,24,24，EMT02，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0228 / ID 2452（type 2，87,3,24,24，EMT03，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0270 / ID 2453（type 2，114,3,24,24，EMT04，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02B8 / ID 2454（type 2，141,3,24,24，EMT05，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0300 / ID 2455（type 2，168,3,24,24，EMT06，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0348 / ID 2456（type 2，195,3,24,24，EMT07，EMOTIONAL_ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：合法表情發送、其他玩家可見、重複與冷卻規則核對。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-046 表情與泡泡樣式](../gameplay/GP-046.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
