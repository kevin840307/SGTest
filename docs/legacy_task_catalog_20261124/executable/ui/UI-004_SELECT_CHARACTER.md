# UI-004_SELECT_CHARACTER — 選擇角色（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-004_SELECT_CHARACTER.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
角色槽、角色預覽、進入遊戲、空槽創角、刪除及密碼入口。

驗收情境：空槽與已有角色均可操作；選中的角色 UID 正確；返回／創角／刪除分支不串錯。

## 原版 UI 控制規格
來源：HK/output/Interface/004_SELECT_CHARACTER.ITF；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 36，取得 36。

背景資源：SELECT_CHARACTER.spr, SELECT_CHARACTER.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0036 | 2 | 2835 | 1000 | 168,420,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0077 | 2 | 2836 | 1000 | 358,420,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x00B8 | 2 | 2837 | 1000 | 544,420,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x00F9 | 2 | 2010 | 1000 | 544,499,85,21 | L07 | — | YELLOW_BUTTON.spr |
| 0x013D | 0 | 2018 | 1000 | 150,260,124,16 | — | — | — |
| 0x0166 | 0 | 2019 | 1000 | 338,260,124,16 | — | — | — |
| 0x018F | 0 | 2020 | 1000 | 525,260,124,16 | — | — | — |
| 0x01B8 | 0 | 2021 | 1000 | 213,391,74,10 | — | — | — |
| 0x01E1 | 2 | 2015 | 1000 | 168,468,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0222 | 0 | 2023 | 1000 | 589,391,74,10 | — | — | — |
| 0x024B | 1 | 2000 | 1000 | 187,472,48,12 | — | 刪除角色 | — |
| 0x0279 | 0 | 2022 | 1000 | 402,391,74,10 | — | — | — |
| 0x02A2 | 2 | 2016 | 1000 | 358,468,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x02E3 | 2 | 2017 | 1000 | 544,468,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0324 | 1 | 2000 | 1000 | 377,472,48,12 | — | 刪除角色 | — |
| 0x0352 | 1 | 2000 | 1000 | 563,472,48,12 | — | 刪除角色 | — |
| 0x0380 | 1 | 2000 | 1000 | 569,504,36,12 | — | 上一頁 | — |
| 0x03AC | 0 | 2679 | 1000 | 169,284,84,130 | — | — | NEW_CHARACTER.spr |
| 0x03E7 | 0 | 2680 | 1000 | 358,284,84,130 | — | — | NEW_CHARACTER.spr |
| 0x0422 | 0 | 2681 | 1000 | 544,285,84,130 | — | — | NEW_CHARACTER.spr |
| 0x045D | 0 | 2496 | 1000 | 358,284,84,130 | — | — | — |
| 0x0486 | 0 | 2495 | 1000 | 169,284,84,130 | — | — | — |
| 0x04AF | 0 | 2497 | 1000 | 544,285,84,130 | — | — | — |
| 0x04D8 | 0 | 3095 | 1000 | 358,284,84,130 | — | — | PLAY_GAME.spr |
| 0x050F | 0 | 3094 | 1000 | 169,284,84,130 | — | — | PLAY_GAME.spr |
| 0x0546 | 0 | 3096 | 1000 | 544,284,84,130 | — | — | PLAY_GAME.spr |
| 0x057D | 2 | 3116 | 1000 | 644,220,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x05C0 | 2 | 2679 | 1000 | 168,444,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0601 | 2 | 2680 | 1000 | 358,444,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0642 | 2 | 2681 | 1000 | 544,444,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x0683 | 1 | 2000 | 1000 | 187,424,48,12 | — | 密碼設定 | — |
| 0x06B1 | 1 | 2000 | 1000 | 377,424,48,12 | — | 密碼設定 | — |
| 0x06DF | 1 | 2000 | 1000 | 563,424,48,12 | — | 密碼設定 | — |
| 0x070D | 1 | 2000 | 1000 | 187,448,48,12 | — | 進入遊戲 | — |
| 0x073B | 1 | 2000 | 1000 | 377,448,48,12 | — | 進入遊戲 | — |
| 0x0769 | 1 | 2000 | 1000 | 563,448,48,12 | — | 進入遊戲 | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| NEW_CHARACTER.spr | 4 | 85×132 @(0,0) ×4 幀 |
| PLAY_GAME.spr | 1 | 67×23 @(-8,-101) ×1 幀 |
| SELECT_CHARACTER.spr | 1 | 800×600 @(0,0) ×1 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
已存在字型證據：參考 SGTest docs/FEATURE_MATRIX.md 的 HK 字型驗收記錄；固定 caption / 已確認 Profile 文字使用新細明體 12px、weight 500、baseline 10（各畫面適用範圍見原證據）；不可將尚未驗證的動態文字或 AA 一律視為完成。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CSelectCharacterItf::MessageMap — L72026 @0x0042BD4C；直接具名呼叫：CSelectCharacterItf::OnSelectChar, CSelectCharacterItf::OnDeleteChar, CSelectCharacterItf::OnChangePwd, CSelectCharacterItf::OnBack, COperationalItf::MessageMap
- CSelectCharacterItf::OnDeleteChar — L72278 @0x0042BFF2；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectCharacterItf::OnChangePwd — L72332 @0x0042C06B；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectCharacterItf::OnSelectChar — L72386 @0x0042C0E4；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectCharacterItf::OnBack — L72442 @0x0042C167；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CSelectCharacterItf::OnUpdate — L72467 @0x0042C18E；直接具名呼叫：CSGOCore::GetLoginChar, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOChar::SetMapPosition, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?1?$CELPtr
- CSelectCharacterItf::Draw — L72840 @0x0042C52D；直接具名呼叫：CSGOInterface::Draw, CSGOChar::DrawAlpha, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CSelectCharacterItf::Create — L72983 @0x0042C669；直接具名呼叫：CSGOInterface::Create, VCELMessageArea::?0?$CELPtr, CSGOCore::GetLoginChar, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOChar::SetMapPosition, VCSGOMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0036 / ID 2835（type 2，168,420,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0077 / ID 2836（type 2，358,420,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B8 / ID 2837（type 2，544,420,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00F9 / ID 2010（type 2，544,499,85,21，L07，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x013D / ID 2018（type 0，150,260,124,16）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0166 / ID 2019（type 0，338,260,124,16）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x018F / ID 2020（type 0，525,260,124,16）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01B8 / ID 2021（type 0，213,391,74,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01E1 / ID 2015（type 2，168,468,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0222 / ID 2023（type 0，589,391,74,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x024B / ID 2000（type 1，187,472,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0279 / ID 2022（type 0，402,391,74,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02A2 / ID 2016（type 2，358,468,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E3 / ID 2017（type 2，544,468,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0324 / ID 2000（type 1，377,472,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0352 / ID 2000（type 1，563,472,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0380 / ID 2000（type 1，569,504,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03AC / ID 2679（type 0，169,284,84,130，NEW_CHARACTER.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03E7 / ID 2680（type 0，358,284,84,130，NEW_CHARACTER.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0422 / ID 2681（type 0，544,285,84,130，NEW_CHARACTER.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x045D / ID 2496（type 0，358,284,84,130）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0486 / ID 2495（type 0，169,284,84,130）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04AF / ID 2497（type 0，544,285,84,130）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04D8 / ID 3095（type 0，358,284,84,130，PLAY_GAME.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x050F / ID 3094（type 0，169,284,84,130，PLAY_GAME.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0546 / ID 3096（type 0，544,284,84,130，PLAY_GAME.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x057D / ID 3116（type 2，644,220,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05C0 / ID 2679（type 2，168,444,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0601 / ID 2680（type 2，358,444,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0642 / ID 2681（type 2，544,444,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0683 / ID 2000（type 1，187,424,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06B1 / ID 2000（type 1，377,424,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06DF / ID 2000（type 1，563,424,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x070D / ID 2000（type 1，187,448,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x073B / ID 2000（type 1，377,448,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0769 / ID 2000（type 1，563,448,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：空槽與已有角色均可操作；選中的角色 UID 正確；返回／創角／刪除分支不串錯。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-002 角色列表與進入世界](../gameplay/GP-002.md)
- [GP-004 角色刪除與第二密碼](../gameplay/GP-004.md)
- [GP-005 人物初始外觀與 paper doll](../gameplay/GP-005.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
