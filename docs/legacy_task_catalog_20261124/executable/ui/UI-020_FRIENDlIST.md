# UI-020_FRIENDlIST — 好友小視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-020_FRIENDlIST.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
好友、黑名單、新增/刪除、聯絡入口。

驗收情境：好友線上狀態、雙方互動、黑名單過濾、重登。

## 原版 UI 控制規格
來源：HK/output/Interface/020_FRIENDlIST.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 17，取得 17。

背景資源：BG_FRIENDLIST_SMALL.spr, BG_FRIENDLIST_SMALL.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x003C | 2 | 2009 | 1000 | 180,61,17,25 | — | — | SCROLLDOWN_S.spr |
| 0x007B | 2 | 2008 | 1000 | 180,36,17,25 | — | — | SCROLLUP_S.spr |
| 0x00B6 | 0 | 2857 | 1000 | 9,36,150,15 | — | — | — |
| 0x00DF | 0 | 2900 | 1000 | 163,36,14,15 | M07 | — | B_GET_MAIL.spr |
| 0x011A | 0 | 2858 | 1000 | 9,53,150,15 | — | — | — |
| 0x0143 | 0 | 2859 | 1000 | 9,70,150,15 | — | — | — |
| 0x016C | 0 | 2000 | 1000 | 9,90,119,11 | — | — | — |
| 0x0195 | 0 | 2901 | 1000 | 163,53,14,15 | M07 | — | B_GET_MAIL.spr |
| 0x01D0 | 0 | 2902 | 1000 | 163,70,14,15 | M07 | — | B_GET_MAIL.spr |
| 0x020B | 0 | 2000 | 1000 | 132,90,14,11 | — | — | — |
| 0x0234 | 2 | 2538 | 1000 | 183,6,17,17 | — | — | ZONESCREEN.spr |
| 0x026F | 0 | 2267 | 1000 | 5,7,24,24 | M01 | — | B_LIST_ADD.spr |
| 0x02AA | 0 | 2268 | 1000 | 71,7,24,24 | M02 | — | B_LIST_DEL.spr |
| 0x02E5 | 0 | 2269 | 1000 | 97,7,24,24 | M03 | — | B_LIST_EDIT.spr |
| 0x0321 | 0 | 2812 | 1000 | 31,7,24,24 | M04 | — | B_LIST_TO_INGORE.spr |
| 0x0362 | 0 | 2271 | 1000 | 128,7,24,24 | M05 | — | B_LIST_FRIENDLIST.spr |
| 0x03A4 | 0 | 2834 | 1000 | 154,7,24,24 | M06 | — | B_LIST_IGNORELIST.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_GET_MAIL.spr | 1 | 13×11 @(0,-2) ×1 幀 |
| B_LIST_ADD.spr | 4 | 24×24 @(0,0) ×4 幀 |
| B_LIST_DEL.spr | 4 | 24×24 @(0,0) ×4 幀 |
| B_LIST_EDIT.spr | 4 | 24×24 @(0,0) ×4 幀 |
| B_LIST_FRIENDLIST.spr | 2 | 24×24 @(0,0) ×2 幀 |
| B_LIST_IGNORELIST.spr | 2 | 24×24 @(0,0) ×2 幀 |
| B_LIST_TO_INGORE.spr | 4 | 24×24 @(0,0) ×4 幀 |
| BG_FRIENDLIST_SMALL.spr | 1 | 203×90 @(0,0) ×1 幀 |
| SCROLLDOWN_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SCROLLUP_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| ZONESCREEN.spr | 4 | 17×17 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CFriendSItf::Create — L173721 @0x00468696；直接具名呼叫：CSGOInterface::Create, CFriendItf::OnFriendListOnly, CFriendItf::InitComponent, PAVCContactEntry::?A?$CELArray, CFriendItf::RetrieveP2PHistory
- CFriendSItf::OnSwitchPanel — L173874 @0x00468838；直接具名呼叫：VCELInterface::?0?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCELInterface::?7?$CELPtr, VCELInterface::?C?$CELPtr
- CFriendSItf::OnUpdate — L174013 @0x0046897D；直接具名呼叫：CFriendItf::OnUpdate
- CFriendSItf::MessageMap — L174034 @0x00468990；直接具名呼叫：CFriendItf::OnPlayer, CFriendItf::OnIndication, CFriendItf::MessageMap

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x003C / ID 2009（type 2，180,61,17,25，SCROLLDOWN_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x007B / ID 2008（type 2，180,36,17,25，SCROLLUP_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B6 / ID 2857（type 0，9,36,150,15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00DF / ID 2900（type 0，163,36,14,15，M07，B_GET_MAIL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x011A / ID 2858（type 0，9,53,150,15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0143 / ID 2859（type 0，9,70,150,15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x016C / ID 2000（type 0，9,90,119,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0195 / ID 2901（type 0，163,53,14,15，M07，B_GET_MAIL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01D0 / ID 2902（type 0，163,70,14,15，M07，B_GET_MAIL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x020B / ID 2000（type 0，132,90,14,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0234 / ID 2538（type 2，183,6,17,17，ZONESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x026F / ID 2267（type 0，5,7,24,24，M01，B_LIST_ADD.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02AA / ID 2268（type 0，71,7,24,24，M02，B_LIST_DEL.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E5 / ID 2269（type 0，97,7,24,24，M03，B_LIST_EDIT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0321 / ID 2812（type 0，31,7,24,24，M04，B_LIST_TO_INGORE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0362 / ID 2271（type 0，128,7,24,24，M05，B_LIST_FRIENDLIST.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03A4 / ID 2834（type 0，154,7,24,24，M06，B_LIST_IGNORELIST.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：好友線上狀態、雙方互動、黑名單過濾、重登。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-044 好友與黑名單](../gameplay/GP-044.md)
- [GP-045 郵件與留言](../gameplay/GP-045.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
