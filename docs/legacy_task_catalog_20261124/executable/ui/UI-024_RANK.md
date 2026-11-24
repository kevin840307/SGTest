# UI-024_RANK — 排行榜主視窗（024_RANK）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-024_RANK.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
排行清單、排序、翻頁、查看詳細資料；核對 024b 版本差異。

驗收情境：空清單、多名排行、順序與查看詳情。

## 原版 UI 控制規格
來源：HK/output/Interface/024_RANK.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 42，取得 42。

背景資源：BG_RANK03.spr, BG_RANK03.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0028 | 0 | 2412 | 1000 | 223,122,56,61 | W03 | — | — |
| 0x0054 | 2 | 2414 | 1000 | 3,151,85,21 | W01 | — | YELLOW_BUTTON.spr |
| 0x0098 | 2 | 2009 | 1000 | 335,310,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x00E1 | 2 | 2413 | 1000 | 335,178,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0126 | 1 | 2000 | 1000 | 10,156,72,12 | — | 編輯勝利字句 | — |
| 0x0158 | 0 | 2415 | 1000 | 95,156,250,11 | W02 | — | — |
| 0x0184 | 0 | 2416 | 1000 | 13,184,40,11 | — | — | — |
| 0x01AD | 0 | 2426 | 1000 | 68,184,120,11 | — | — | — |
| 0x01D6 | 0 | 2436 | 1000 | 203,184,120,11 | — | — | — |
| 0x01FF | 0 | 2417 | 1000 | 13,200,40,11 | — | — | — |
| 0x0228 | 0 | 2418 | 1000 | 13,216,40,11 | — | — | — |
| 0x0251 | 0 | 2419 | 1000 | 13,232,40,11 | — | — | — |
| 0x027A | 0 | 2420 | 1000 | 13,248,40,11 | — | — | — |
| 0x02A3 | 0 | 2421 | 1000 | 13,264,40,11 | — | — | — |
| 0x02CC | 0 | 2422 | 1000 | 13,280,40,11 | — | — | — |
| 0x02F5 | 0 | 2423 | 1000 | 13,296,40,11 | — | — | — |
| 0x031E | 0 | 2424 | 1000 | 13,312,40,11 | — | — | — |
| 0x0347 | 0 | 2425 | 1000 | 13,328,40,11 | — | — | — |
| 0x0370 | 0 | 2427 | 1000 | 68,200,120,11 | — | — | — |
| 0x0399 | 0 | 2428 | 1000 | 68,216,120,11 | — | — | — |
| 0x03C2 | 0 | 2429 | 1000 | 68,232,120,11 | — | — | — |
| 0x03EB | 0 | 2430 | 1000 | 68,248,120,11 | — | — | — |
| 0x0414 | 0 | 2431 | 1000 | 68,264,120,11 | — | — | — |
| 0x043D | 0 | 2432 | 1000 | 68,280,120,11 | — | — | — |
| 0x0466 | 0 | 2433 | 1000 | 68,296,120,11 | — | — | — |
| 0x048F | 0 | 2434 | 1000 | 68,312,120,11 | — | — | — |
| 0x04B8 | 0 | 2435 | 1000 | 68,328,120,11 | — | — | — |
| 0x04E1 | 0 | 2437 | 1000 | 203,200,120,11 | — | — | — |
| 0x050A | 0 | 2438 | 1000 | 203,216,120,11 | — | — | — |
| 0x0533 | 0 | 2439 | 1000 | 203,232,120,11 | — | — | — |
| 0x055C | 0 | 2440 | 1000 | 203,248,120,11 | — | — | — |
| 0x0585 | 0 | 2441 | 1000 | 203,264,120,11 | — | — | — |
| 0x05AE | 0 | 2442 | 1000 | 203,280,120,11 | — | — | — |
| 0x05D7 | 0 | 2443 | 1000 | 203,296,120,11 | — | — | — |
| 0x0600 | 0 | 2444 | 1000 | 203,312,120,11 | — | — | — |
| 0x0629 | 0 | 2445 | 1000 | 203,328,120,11 | — | — | — |
| 0x0652 | 0 | 2448 | 1000 | 13,374,40,11 | W05 | — | — |
| 0x067E | 0 | 2447 | 1000 | 68,374,120,11 | W06 | — | — |
| 0x06AA | 0 | 2449 | 1000 | 203,374,120,11 | W07 | — | — |
| 0x06D6 | 2 | 2176 | 1000 | 335,1,16,16 | — | — | CLOSESCREEN.spr |
| 0x0713 | 2 | 3116 | 1000 | 318,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0756 | 0 | 2001 | 1000 | 8,34,98,13 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_RANK03.spr | 1 | 356×357 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CRankMainItf::MessageMap — L136579 @0x004522EC；直接具名呼叫：CRankMainItf::OnScrollUp, CRankMainItf::OnScrollDown, CRankMainItf::OnEditWin, COperationalItf::MessageMap
- CRankMainItf::Create — L136776 @0x004524C7；直接具名呼叫：CSGOInterface::Create, VCELFont2D::?4?$CELPtr, VCELFont2D::?1?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, CSGOInterface::CenterScreen
- CRankMainItf::OnClose — L137003 @0x00452703；直接具名呼叫：CSGOUim::SendPacket
- CRankMainItf::Draw — L137031 @0x00452730；直接具名呼叫：CELInterface::Draw, VCELFont2D::?9?$CELPtr, VCELFont2D::?C?$CELPtr, VCSGOPlayer::?9?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOChar::DrawAlpha
- CRankMainItf::OnUpdate — L137179 @0x004528C2；直接具名呼叫：CRankMainItf::UpdatePhoto, CRankMainItf::UpdateScrollMsg, CRankMainItf::UpdateMyRankInfo
- CRankMainItf::UpdatePhoto — L137212 @0x00452901；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, VCSGOPlayer::?C?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOUim::GetPlayer, CELMapObj::GetWorldPosition, VCSGOPlayer::?1?$CELPtr, CELDisplay::GetDimension, CSGOChar::SetMapPosition, VCELMessageArea::?1?$CELPtr
- CRankMainItf::UpdateInfo — L137383 @0x00452A8D；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, URANK_INFO::?A?$CELArray, VCELMessageArea::?1?$CELPtr
- CRankMainItf::UpdateButton — L137639 @0x00452D28；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELUserButton::?C?$CELPtr, VCELUserButton::?1?$CELPtr
- CRankMainItf::UpdateScrollMsg — L137726 @0x00452DE6；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRankMainItf::UpdateMyRankInfo — L137762 @0x00452E2F；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CRankMainItf::OnScrollUp — L137912 @0x00452F93；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRankMainItf::OnScrollDown — L137945 @0x00452FCD；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRankMainItf::OnEditWin — L138041 @0x0045309A；直接具名呼叫：VCELInterface::?0?$CELPtr, VCELInterface::?C?$CELPtr, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCRankSayItf::?C?$CELPtr, CRankSayItf::SetWinMsg, CRankSayItf::SetRankType, VCELInterface::?1?$CELPtr
- CRankMainItf::SetData — L138167 @0x004531C9；直接具名呼叫：CRankMainItf::SetRankTypeName
- CRankMainItf::SetRankTypeName — L138242 @0x00453261；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?7?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, CSGOUim::GetLibrary, CSGOLibrary::GetRankTitleData, URANKTITLEDATA_::?A?$CELArray
- CRankMainItf::OnChampionLook — L138388 @0x004533D4；直接具名呼叫：VCSGOPlayer::?4?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::SetEnableDisplayInfo, CSGOPlayer::SetEmotion, CSGOChar::InitActDisplay
- CRankMainItf::SetData — L138602 @0x00453618；直接具名呼叫：VCELFont2D::?C?$CELPtr
- CRankMainItf::SetData — L138666 @0x0045369D；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0028 / ID 2412（type 0，223,122,56,61，W03）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0054 / ID 2414（type 2，3,151,85,21，W01，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0098 / ID 2009（type 2，335,310,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00E1 / ID 2413（type 2，335,178,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0126 / ID 2000（type 1，10,156,72,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0158 / ID 2415（type 0，95,156,250,11，W02）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0184 / ID 2416（type 0，13,184,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01AD / ID 2426（type 0，68,184,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01D6 / ID 2436（type 0，203,184,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01FF / ID 2417（type 0，13,200,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0228 / ID 2418（type 0，13,216,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0251 / ID 2419（type 0，13,232,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x027A / ID 2420（type 0，13,248,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02A3 / ID 2421（type 0，13,264,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02CC / ID 2422（type 0，13,280,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02F5 / ID 2423（type 0，13,296,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x031E / ID 2424（type 0，13,312,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0347 / ID 2425（type 0，13,328,40,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0370 / ID 2427（type 0，68,200,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0399 / ID 2428（type 0，68,216,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C2 / ID 2429（type 0，68,232,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03EB / ID 2430（type 0，68,248,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0414 / ID 2431（type 0，68,264,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x043D / ID 2432（type 0，68,280,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0466 / ID 2433（type 0，68,296,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x048F / ID 2434（type 0，68,312,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04B8 / ID 2435（type 0，68,328,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04E1 / ID 2437（type 0，203,200,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x050A / ID 2438（type 0，203,216,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0533 / ID 2439（type 0，203,232,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x055C / ID 2440（type 0，203,248,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0585 / ID 2441（type 0，203,264,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05AE / ID 2442（type 0，203,280,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05D7 / ID 2443（type 0，203,296,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0600 / ID 2444（type 0，203,312,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0629 / ID 2445（type 0，203,328,120,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0652 / ID 2448（type 0，13,374,40,11，W05）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x067E / ID 2447（type 0，68,374,120,11，W06）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06AA / ID 2449（type 0，203,374,120,11，W07）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06D6 / ID 2176（type 2，335,1,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0713 / ID 3116（type 2，318,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0756 / ID 2001（type 0，8,34,98,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：空清單、多名排行、順序與查看詳情。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-051 寵物召喚與收回](../gameplay/GP-051.md)
- [GP-061 排行榜](../gameplay/GP-061.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
