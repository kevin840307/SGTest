# UI-003_SELECT_SERVER — 選擇伺服器（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-003_SELECT_SERVER.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
伺服器列表、狀態燈、捲動、選擇、返回。

驗收情境：伺服器滿載或離線不可誤進；列表更新和捲動；選擇後正確切換。

## 原版 UI 控制規格
來源：HK/output/Interface/003_SELECT_SERVER.ITF；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 28，取得 28。

背景資源：SELECT_SERVER.spr, SELECT_SERVER.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0030 | 0 | 2035 | 1000 | 259,254,300,11 | — | — | — |
| 0x0059 | 2 | 2008 | 1000 | 588,249,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x009E | 2 | 2009 | 1000 | 588,360,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x00E7 | 2 | 2010 | 1000 | 499,468,85,21 | L05 | — | YELLOW_BUTTON.spr |
| 0x012B | 1 | 2000 | 1000 | 523,473,36,12 | — | 上一頁 | — |
| 0x0157 | 0 | 2036 | 1000 | 259,268,300,11 | — | — | — |
| 0x0180 | 0 | 2037 | 1000 | 259,282,300,11 | — | — | — |
| 0x01A9 | 0 | 2038 | 1000 | 259,296,300,11 | — | — | — |
| 0x01D2 | 0 | 2039 | 1000 | 259,310,300,11 | — | — | — |
| 0x01FB | 0 | 2040 | 1000 | 259,324,300,11 | — | — | — |
| 0x0224 | 0 | 2041 | 1000 | 259,338,300,11 | — | — | — |
| 0x024D | 0 | 2042 | 1000 | 259,352,300,11 | — | — | — |
| 0x0276 | 0 | 2043 | 1000 | 259,366,300,11 | — | — | — |
| 0x029F | 0 | 2044 | 1000 | 259,380,300,11 | — | — | — |
| 0x02C8 | 0 | 2077 | 1000 | 201,253,55,13 | — | — | LOGIN_Light.spr |
| 0x0301 | 0 | 2078 | 1000 | 201,267,55,13 | — | — | LOGIN_Light.spr |
| 0x033A | 0 | 2079 | 1000 | 201,281,55,13 | — | — | LOGIN_Light.spr |
| 0x0373 | 0 | 2080 | 1000 | 201,295,55,13 | — | — | LOGIN_Light.spr |
| 0x03AC | 0 | 2081 | 1000 | 201,309,55,13 | — | — | LOGIN_Light.spr |
| 0x03E5 | 0 | 2082 | 1000 | 201,323,55,13 | — | — | LOGIN_Light.spr |
| 0x041E | 0 | 2083 | 1000 | 201,337,55,13 | — | — | LOGIN_Light.spr |
| 0x0457 | 0 | 2084 | 1000 | 201,351,55,13 | — | — | LOGIN_Light.spr |
| 0x0490 | 0 | 2085 | 1000 | 201,365,55,13 | — | — | LOGIN_Light.spr |
| 0x04C9 | 0 | 2086 | 1000 | 201,379,55,13 | — | — | LOGIN_Light.spr |
| 0x0502 | 2 | 3116 | 1000 | 577,220,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0545 | 0 | 3296 | 1000 | 201,405,378,53 | — | — | — |
| 0x056E | 2 | 2525 | 1000 | 588,437,17,25 | — | — | SCROLLDOWN_S.spr |
| 0x05AD | 2 | 2524 | 1000 | 588,402,17,25 | — | — | SCROLLUP_S.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| LOGIN_Light.spr | 2 | 24×13 @(0,0) ×2 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLDOWN_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SELECT_SERVER.spr | 1 | 800×600 @(0,0) ×1 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CSelectServerItf::MessageMap — L73325 @0x0042C8FC；直接具名呼叫：CSelectServerItf::OnSelectSlot, CSelectServerItf::OnBack, CSelectServerItf::OnScrollUp, CSelectServerItf::OnScrollDown, COperationalItf::MessageMap
- CSelectServerItf::ReadServerList — L73566 @0x0042CB59；直接具名呼叫：CELFileIO::CELFileRead
- CSelectServerItf::OnScrollUp — L73738 @0x0042CD15；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectServerItf::OnScrollDown — L73771 @0x0042CD4F；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectServerItf::OnBack — L73867 @0x0042CE1C；直接具名呼叫：CSelectServerItf::ReadServerList
- CSelectServerItf::OnUpdate — L73904 @0x0042CE69；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCSGOMessageArea::?C?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELMessageArea::?1?$CELPtr
- CSelectServerItf::OnSelectSlot — L74261 @0x0042D213；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?7?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, YA_NAAVCELString::?9, CSelectServerItf::ReadServerList
- CSelectServerItf::Create — L74538 @0x0042D5A2；直接具名呼叫：CSGOInterface::Create, CSelectServerItf::ReadServerList, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCSGOMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, CELFileIO::CELFileRead

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0030 / ID 2035（type 0，259,254,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0059 / ID 2008（type 2，588,249,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x009E / ID 2009（type 2，588,360,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00E7 / ID 2010（type 2，499,468,85,21，L05，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x012B / ID 2000（type 1，523,473,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0157 / ID 2036（type 0，259,268,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0180 / ID 2037（type 0，259,282,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01A9 / ID 2038（type 0，259,296,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01D2 / ID 2039（type 0，259,310,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01FB / ID 2040（type 0，259,324,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0224 / ID 2041（type 0，259,338,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x024D / ID 2042（type 0，259,352,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0276 / ID 2043（type 0，259,366,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x029F / ID 2044（type 0，259,380,300,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02C8 / ID 2077（type 0，201,253,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0301 / ID 2078（type 0，201,267,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x033A / ID 2079（type 0，201,281,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0373 / ID 2080（type 0，201,295,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03AC / ID 2081（type 0，201,309,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03E5 / ID 2082（type 0，201,323,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x041E / ID 2083（type 0，201,337,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0457 / ID 2084（type 0，201,351,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0490 / ID 2085（type 0，201,365,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C9 / ID 2086（type 0，201,379,55,13，LOGIN_Light.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0502 / ID 3116（type 2，577,220,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0545 / ID 3296（type 0，201,405,378,53）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x056E / ID 2525（type 2，588,437,17,25，SCROLLDOWN_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05AD / ID 2524（type 2，588,402,17,25，SCROLLUP_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：伺服器滿載或離線不可誤進；列表更新和捲動；選擇後正確切換。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-001 登入與帳號 session](../gameplay/GP-001.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
