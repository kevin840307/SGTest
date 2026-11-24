# UI-005_CREATE_CHARACTER — 建立角色（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-005_CREATE_CHARACTER.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
姓名、種族/性別及外觀選項、預覽、確認與返回；欄位以原版核對。

驗收情境：合法角色建立後可選；重名／非法名稱拒絕；切換外觀與預覽一致。

## 原版 UI 控制規格
來源：HK/output/Interface/005_CREATE_CHARACTER.ITF；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 50，取得 50。

背景資源：CREATE_CHARACTER.spr, CREATE_CHARACTER.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0036 | 1 | 2000 | 1000 | 169,261,24,12 | — | 名稱 | — |
| 0x0060 | 1 | 2000 | 1000 | 169,285,24,12 | — | 種族 | — |
| 0x008A | 1 | 2000 | 1000 | 157,357,48,12 | — | 上身顏色 | — |
| 0x00B8 | 1 | 2000 | 1000 | 157,381,48,12 | — | 手袖顏色 | — |
| 0x00E6 | 1 | 2000 | 1000 | 157,405,48,12 | — | 下身顏色 | — |
| 0x0114 | 1 | 2000 | 1000 | 498,357,48,12 | — | 剩餘點數 | — |
| 0x0142 | 1 | 2000 | 1000 | 510,261,24,12 | — | 力量 | — |
| 0x016C | 1 | 2000 | 1000 | 510,309,24,12 | — | 體質 | — |
| 0x0196 | 1 | 2000 | 1000 | 510,285,24,12 | — | 速度 | — |
| 0x01C0 | 1 | 2000 | 1000 | 510,333,24,12 | — | 智慧 | — |
| 0x01EA | 0 | 2045 | 1000 | 596,261,43,16 | L12 | — | — |
| 0x0216 | 0 | 2046 | 1000 | 596,309,43,16 | L13 | — | — |
| 0x0242 | 0 | 2047 | 1000 | 596,285,43,16 | L14 | — | — |
| 0x026E | 0 | 2048 | 1000 | 596,333,43,16 | L15 | — | — |
| 0x029A | 2 | 2050 | 1000 | 228,281,24,21 | — | — | LEFT_BUTTON.spr |
| 0x02D7 | 2 | 2051 | 1000 | 228,304,24,21 | — | — | LEFT_BUTTON.spr |
| 0x0314 | 2 | 2052 | 1000 | 229,328,24,21 | — | — | LEFT_BUTTON.spr |
| 0x0351 | 2 | 2054 | 1000 | 228,352,24,21 | — | — | LEFT_BUTTON.spr |
| 0x038E | 2 | 2053 | 1000 | 228,376,24,21 | — | — | LEFT_BUTTON.spr |
| 0x03CB | 2 | 2055 | 1000 | 228,400,24,21 | — | — | LEFT_BUTTON.spr |
| 0x0408 | 2 | 2059 | 1000 | 257,281,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0447 | 2 | 2060 | 1000 | 257,304,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0486 | 2 | 2061 | 1000 | 257,328,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x04C5 | 2 | 2062 | 1000 | 257,352,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0504 | 2 | 2063 | 1000 | 257,376,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0543 | 2 | 2089 | 1000 | 257,400,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0582 | 2 | 2065 | 1000 | 566,256,24,21 | — | — | LEFT_BUTTON.spr |
| 0x05BF | 2 | 2069 | 1000 | 646,256,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x05FE | 2 | 2066 | 1000 | 566,304,24,21 | — | — | LEFT_BUTTON.spr |
| 0x063B | 2 | 2067 | 1000 | 566,280,24,21 | — | — | LEFT_BUTTON.spr |
| 0x0678 | 2 | 2068 | 1000 | 566,328,24,21 | — | — | LEFT_BUTTON.spr |
| 0x06B5 | 2 | 2070 | 1000 | 646,304,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x06F4 | 2 | 2071 | 1000 | 646,280,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0733 | 2 | 2072 | 1000 | 646,328,24,21 | — | — | RIGHT_BUTTON.spr |
| 0x0772 | 2 | 2010 | 1000 | 583,471,85,21 | L17 | — | YELLOW_BUTTON.spr |
| 0x07B6 | 2 | 2005 | 1000 | 478,471,85,21 | L16 | — | YELLOW_BUTTON.spr |
| 0x07FA | 1 | 2000 | 1000 | 497,475,48,12 | — | 建立角色 | — |
| 0x0828 | 1 | 2000 | 1000 | 608,475,36,12 | — | 上一頁 | — |
| 0x0854 | 0 | 2282 | 1000 | 381,395,59,69 | — | — | — |
| 0x087D | 8 | 2001 | 1000 | 233,260,125,15 | L11 | — | — |
| 0x08B1 | 0 | 2283 | 1000 | 596,357,43,16 | — | — | — |
| 0x08DA | 1 | 2000 | 1000 | 169,309,24,12 | — | 頭部 | — |
| 0x0904 | 1 | 2000 | 1000 | 157,333,48,12 | — | 頭部顏色 | — |
| 0x0932 | 2 | 3116 | 1000 | 645,219,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0975 | 1 | 2000 | 1000 | 498,391,48,12 | — | 角色密碼 | — |
| 0x09A3 | 1 | 2000 | 1000 | 498,415,48,12 | — | 確認密碼 | — |
| 0x09D1 | 8 | 2002 | 1000 | 596,391,43,16 | L18 | — | — |
| 0x0A05 | 8 | 3361 | 1000 | 596,415,43,16 | L19 | — | — |
| 0x0A39 | 0 | 2000 | 1000 | 140,434,277,55 | — | — | — |
| 0x0A62 | 0 | 2000 | 1000 | 344,403,70,12 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| CREATE_CHARACTER.spr | 1 | 800×600 @(0,0) ×1 幀 |
| LEFT_BUTTON.spr | 4 | 24×21 @(0,0) ×4 幀 |
| RIGHT_BUTTON.spr | 4 | 24×21 @(0,0) ×4 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
已存在字型證據：參考 SGTest docs/FEATURE_MATRIX.md 的 HK 字型驗收記錄；固定 caption / 已確認 Profile 文字使用新細明體 12px、weight 500、baseline 10（各畫面適用範圍見原證據）；不可將尚未驗證的動態文字或 AA 一律視為完成。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CCreateCharacterItf::MessageMap — L62135 @0x00425E8C；直接具名呼叫：CCreateCharacterItf::OnL01, CCreateCharacterItf::OnL02, CCreateCharacterItf::OnL03, CCreateCharacterItf::OnL04, CCreateCharacterItf::OnL05, CCreateCharacterItf::OnL06, CCreateCharacterItf::OnR01, CCreateCharacterItf::OnR02, CCreateCharacterItf::OnR03, CCreateCharacterItf::OnR04, CCreateCharacterItf::OnR05, CCreateCharacterItf::OnR06
- CCreateCharacterItf::OnUpdate — L62465 @0x0042621C；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, CCreateCharacterItf::UpdateChar, VCELMessageArea::?1?$CELPtr
- CCreateCharacterItf::RefreshHead — L62777 @0x0042656C；直接具名呼叫：CSGOLibrary::GetCreateCharPara, UCREATECHAR_PARA_LIST_::?A?$CELArray, H::?A?$CELArray, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::InitActDisplay
- CCreateCharacterItf::RefreshHand — L62949 @0x00426761；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CSGOLibrary::GetCreateCharPara, UCREATECHAR_PARA_LIST_::?A?$CELArray, H::?A?$CELArray, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::InitActDisplay
- CCreateCharacterItf::RefreshArmor — L63074 @0x004268C5；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CSGOLibrary::GetCreateCharPara, UCREATECHAR_PARA_LIST_::?A?$CELArray, H::?A?$CELArray, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::InitActDisplay
- CCreateCharacterItf::UpdateChar — L63199 @0x00426A29；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData
- CCreateCharacterItf::ReadOptionConfig — L63284 @0x00426B09；直接具名呼叫：CSGOLibrary::GetCreateCharPara, UCREATECHAR_PARA_LIST_::?A?$CELArray, H::?A?$CELArray
- CCreateCharacterItf::Draw — L63407 @0x00426CAE；直接具名呼叫：CSGOInterface::Draw, VCSGOPlayer::?9?$CELPtr, VCSGOPlayer::?C?$CELPtr
- CCreateCharacterItf::Create — L63455 @0x00426D06；直接具名呼叫：CSGOInterface::Create, VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?9?$CELPtr, VCELInputArea::?C?$CELPtr, VCELItfComp::?0?$CELPtr, VCELInputArea::?4?$CELPtr, VCSGOPlayer::?4?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCSGOPlayer::?C?$CELPtr
- CCreateCharacterItf::OnOK — L64013 @0x00427338；直接具名呼叫：VCELInterface::?0?$CELPtr, VCELInterface::?C?$CELPtr, CELDisplay::GetDimension, VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?C?$CELPtr, YA_NAAVCELString::?8, VCMsgBox::?C?$CELPtr, VCELInputArea::?7?$CELPtr, CELString::GetSize, YA_NAAVCELString::?9, VCELInputArea::?1?$CELPtr
- CCreateCharacterItf::OnBack — L64626 @0x00427A70；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CCreateCharacterItf::MakeData — L64651 @0x00427A97；直接具名呼叫：VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?7?$CELPtr, VCELInputArea::?1?$CELPtr, VCELInputArea::?C?$CELPtr, CELString::GetSize
- CCreateCharacterItf::ValidateData — L65033 @0x00427EE4；直接具名呼叫：VCELInputArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELInputArea::?C?$CELPtr, YA_NAAVCELString::?8, VCELInputArea::?1?$CELPtr, VCELInputArea::?7?$CELPtr, CELString::GetSize, YA_NAAVCELString::?9
- CCreateCharacterItf::ResetRace — L65511 @0x00428425；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CELOnlineObj::SetData, CSGOLibrary::GetCreateCharPara, UCREATECHAR_PARA_LIST_::?A?$CELArray, H::?A?$CELArray, CSGOChar::InitActDisplay
- CCreateCharacterItf::OnR01 — L65858 @0x0042885E；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::ResetRace
- CCreateCharacterItf::OnL01 — L65907 @0x004288BA；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::ResetRace
- CCreateCharacterItf::OnR02 — L65956 @0x00428915；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHead
- CCreateCharacterItf::OnL02 — L66009 @0x00428986；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHead
- CCreateCharacterItf::OnR03 — L66062 @0x004289F5；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHead
- CCreateCharacterItf::OnL03 — L66115 @0x00428A66；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHead
- CCreateCharacterItf::OnR04 — L66168 @0x00428AD5；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshArmor
- CCreateCharacterItf::OnL04 — L66221 @0x00428B46；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshArmor
- CCreateCharacterItf::OnR05 — L66274 @0x00428BB5；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHand
- CCreateCharacterItf::OnL05 — L66327 @0x00428C26；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshHand
- CCreateCharacterItf::OnR06 — L66380 @0x00428C95；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshArmor
- CCreateCharacterItf::OnL06 — L66433 @0x00428D06；直接具名呼叫：VCSGOPlayer::?7?$CELPtr, CCreateCharacterItf::RefreshArmor
- CCreateCharacterItf::OnRA01 — L66486 @0x00428D75；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnLA01 — L66547 @0x00428E03；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnRA02 — L66610 @0x00428E90；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnLA02 — L66671 @0x00428F1E；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnRA03 — L66734 @0x00428FAB；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnLA03 — L66795 @0x00429039；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnRA04 — L66858 @0x004290C6；直接具名呼叫：VCSGOPlayer::?7?$CELPtr
- CCreateCharacterItf::OnLA04 — L66919 @0x00429154；直接具名呼叫：VCSGOPlayer::?7?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0036 / ID 2000（type 1，169,261,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0060 / ID 2000（type 1，169,285,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x008A / ID 2000（type 1，157,357,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B8 / ID 2000（type 1，157,381,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00E6 / ID 2000（type 1，157,405,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0114 / ID 2000（type 1，498,357,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0142 / ID 2000（type 1，510,261,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x016C / ID 2000（type 1，510,309,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0196 / ID 2000（type 1，510,285,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01C0 / ID 2000（type 1，510,333,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01EA / ID 2045（type 0，596,261,43,16，L12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0216 / ID 2046（type 0，596,309,43,16，L13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0242 / ID 2047（type 0，596,285,43,16，L14）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x026E / ID 2048（type 0，596,333,43,16，L15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x029A / ID 2050（type 2，228,281,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02D7 / ID 2051（type 2，228,304,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0314 / ID 2052（type 2，229,328,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0351 / ID 2054（type 2，228,352,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x038E / ID 2053（type 2，228,376,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03CB / ID 2055（type 2，228,400,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0408 / ID 2059（type 2，257,281,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0447 / ID 2060（type 2，257,304,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0486 / ID 2061（type 2，257,328,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C5 / ID 2062（type 2，257,352,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0504 / ID 2063（type 2，257,376,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0543 / ID 2089（type 2，257,400,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0582 / ID 2065（type 2，566,256,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05BF / ID 2069（type 2，646,256,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05FE / ID 2066（type 2，566,304,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x063B / ID 2067（type 2，566,280,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0678 / ID 2068（type 2，566,328,24,21，LEFT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06B5 / ID 2070（type 2，646,304,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06F4 / ID 2071（type 2，646,280,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0733 / ID 2072（type 2，646,328,24,21，RIGHT_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0772 / ID 2010（type 2，583,471,85,21，L17，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07B6 / ID 2005（type 2，478,471,85,21，L16，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07FA / ID 2000（type 1，497,475,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0828 / ID 2000（type 1，608,475,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0854 / ID 2282（type 0，381,395,59,69）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x087D / ID 2001（type 8，233,260,125,15，L11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08B1 / ID 2283（type 0，596,357,43,16）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08DA / ID 2000（type 1，169,309,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0904 / ID 2000（type 1，157,333,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0932 / ID 3116（type 2，645,219,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0975 / ID 2000（type 1，498,391,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09A3 / ID 2000（type 1，498,415,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09D1 / ID 2002（type 8，596,391,43,16，L18）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A05 / ID 3361（type 8，596,415,43,16，L19）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A39 / ID 2000（type 0，140,434,277,55）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A62 / ID 2000（type 0，344,403,70,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：合法角色建立後可選；重名／非法名稱拒絕；切換外觀與預覽一致。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-003 角色建立](../gameplay/GP-003.md)
- [GP-005 人物初始外觀與 paper doll](../gameplay/GP-005.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
