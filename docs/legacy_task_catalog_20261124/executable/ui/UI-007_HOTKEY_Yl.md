# UI-007_HOTKEY_Yl — 快捷鍵列（007_HOTKEY_Yl）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-007_HOTKEY_Yl.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
物品與技能欄位、拖曳、翻頁/展開縮合、快捷使用。

驗收情境：拖入/移除後狀態同步；按鍵消耗正確；冷卻及重登保存。

## 原版 UI 控制規格
來源：HK/output/Interface/007_HOTKEY_Yl.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 63，取得 63。

背景資源：HOTKEY_LY.spr, HOTKEY_LY.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0028 | 2 | 2248 | 1000 | 3,342,24,13 | H09 | — | HOTKEY_YS_B.spr |
| 0x0068 | 0 | 2250 | 1000 | 3,4,11,11 | H07 | — | OFFON.spr |
| 0x009E | 0 | 2251 | 1000 | 16,4,11,11 | h08 | — | OFFON.spr |
| 0x00D4 | 1 | 3182 | 1000 | 4,18,12,12 | — | F1 | — |
| 0x00FC | 1 | 3183 | 1000 | 4,47,12,12 | — | F2 | — |
| 0x0124 | 1 | 3184 | 1000 | 4,72,12,12 | — | F3 | — |
| 0x014C | 1 | 3185 | 1000 | 4,99,12,12 | — | F4 | — |
| 0x0174 | 1 | 3186 | 1000 | 4,126,12,12 | — | F5 | — |
| 0x019C | 1 | 3187 | 1000 | 4,153,12,12 | — | F6 | — |
| 0x01C4 | 1 | 3188 | 1000 | 4,180,12,12 | — | F7 | — |
| 0x01EC | 1 | 3189 | 1000 | 4,207,12,12 | — | F8 | — |
| 0x0214 | 1 | 3190 | 1000 | 4,234,12,12 | — | F9 | — |
| 0x023C | 1 | 3191 | 1000 | 4,261,18,12 | — | F10 | — |
| 0x0265 | 1 | 3192 | 1000 | 4,288,18,12 | — | F11 | — |
| 0x028E | 1 | 3193 | 1000 | 4,315,18,12 | — | F12 | — |
| 0x02B7 | 0 | 2125 | 1000 | 8,32,18,13 | — | — | — |
| 0x02E0 | 0 | 2126 | 1000 | 8,58,18,13 | — | — | — |
| 0x0309 | 0 | 2127 | 1000 | 8,85,18,13 | — | — | — |
| 0x0332 | 0 | 2128 | 1000 | 8,112,18,13 | — | — | — |
| 0x035B | 0 | 2129 | 1000 | 8,139,18,13 | — | — | — |
| 0x0384 | 0 | 2130 | 1000 | 8,166,18,13 | — | — | — |
| 0x03AD | 0 | 2131 | 1000 | 8,193,18,13 | — | — | — |
| 0x03D6 | 0 | 2132 | 1000 | 8,220,18,13 | — | — | — |
| 0x03FF | 0 | 2133 | 1000 | 8,247,18,13 | — | — | — |
| 0x0428 | 0 | 2134 | 1000 | 8,274,18,13 | — | — | — |
| 0x0451 | 0 | 2135 | 1000 | 8,301,18,13 | — | — | — |
| 0x047A | 0 | 2136 | 1000 | 8,328,18,13 | — | — | — |
| 0x04A3 | 4 | 2234 | 1000 | 4,18,24,26 | — | — | — |
| 0x04C0 | 4 | 2235 | 1000 | 4,45,24,26 | — | — | — |
| 0x04DD | 4 | 2236 | 1000 | 4,72,24,26 | — | — | — |
| 0x04FA | 4 | 2237 | 1000 | 4,99,24,26 | — | — | — |
| 0x0517 | 4 | 2238 | 1000 | 4,126,24,26 | — | — | — |
| 0x0534 | 4 | 2239 | 1000 | 4,153,24,26 | — | — | — |
| 0x0551 | 4 | 2240 | 1000 | 4,180,24,26 | — | — | — |
| 0x056E | 4 | 2241 | 1000 | 4,207,24,26 | — | — | — |
| 0x058B | 4 | 2242 | 1000 | 4,234,24,26 | — | — | — |
| 0x05A8 | 4 | 2243 | 1000 | 4,261,24,26 | — | — | — |
| 0x05C5 | 4 | 2244 | 1000 | 4,288,24,26 | — | — | — |
| 0x05E2 | 4 | 2245 | 1000 | 4,315,24,26 | — | — | — |
| 0x05FF | 0 | 2113 | 1000 | 4,18,24,26 | — | — | — |
| 0x0628 | 0 | 2114 | 1000 | 4,45,24,26 | — | — | — |
| 0x0651 | 0 | 2115 | 1000 | 4,72,24,26 | — | — | — |
| 0x067A | 0 | 2117 | 1000 | 4,99,24,26 | — | — | — |
| 0x06A3 | 0 | 2116 | 1000 | 4,126,24,26 | — | — | — |
| 0x06CC | 0 | 2118 | 1000 | 4,153,24,26 | — | — | — |
| 0x06F5 | 0 | 2119 | 1000 | 4,180,24,26 | — | — | — |
| 0x071E | 0 | 2120 | 1000 | 4,207,24,26 | — | — | — |
| 0x0747 | 0 | 2121 | 1000 | 4,234,24,26 | — | — | — |
| 0x0770 | 0 | 2122 | 1000 | 4,261,24,26 | — | — | — |
| 0x0799 | 0 | 2123 | 1000 | 4,288,24,26 | — | — | — |
| 0x07C2 | 0 | 2124 | 1000 | 4,315,24,26 | — | — | — |
| 0x07EB | 3 | 2995 | 1000 | 3,18,25,26 | — | — | — |
| 0x0814 | 3 | 2996 | 1000 | 3,45,25,26 | — | — | — |
| 0x083D | 3 | 2997 | 1000 | 3,72,25,26 | — | — | — |
| 0x0866 | 3 | 2998 | 1000 | 3,99,25,26 | — | — | — |
| 0x088F | 3 | 2999 | 1000 | 3,126,25,26 | — | — | — |
| 0x08B8 | 3 | 3000 | 1000 | 3,153,25,26 | — | — | — |
| 0x08E1 | 3 | 3001 | 1000 | 3,180,25,26 | — | — | — |
| 0x090A | 3 | 3002 | 1000 | 3,207,25,26 | — | — | — |
| 0x0933 | 3 | 3003 | 1000 | 3,234,25,26 | — | — | — |
| 0x095C | 3 | 3004 | 1000 | 3,261,25,26 | — | — | — |
| 0x0985 | 3 | 3005 | 1000 | 3,288,25,26 | — | — | — |
| 0x09AE | 3 | 3006 | 1000 | 3,315,25,26 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| HOTKEY_LY.spr | 1 | 30×358 @(0,0) ×1 幀 |
| HOTKEY_YS_B.spr | 2 | 24×13 @(0,0) ×2 幀 |
| OFFON.spr | 2 | 11×11 @(0,0) ×2 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CHotKeyItf::SetLastPickUpPos — L57105 @0x00423640；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CHotKeyItf::MessageMap — L119900 @0x0044878C；直接具名呼叫：CHotKeyItf::OnUseHotButton, CHotKeyItf::OnPage1, CHotKeyItf::OnPage2, CHotKeyItf::OnToggleSize, CItemItf::MessageMap
- CHotKeyItf::Draw — L120180 @0x00448B39；直接具名呼叫：VCELFont2D::?C?$CELPtr, VCELFont2D::?1?$CELPtr, VCELItfDC::?7?$CELPtr, VCELItfDC::?C?$CELPtr, VCELDrawObj::?7?$CELPtr, VCELDrawObj::?1?$CELPtr, VCELDrawObj::?C?$CELPtr, VCELUserButton::?0?$CELPtr, VCELProgressBar::?0?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELText::?0?$CELPtr, VCELFont2D::?9?$CELPtr
- CHotKeyItf::OnUseHotButton — L121117 @0x0044946B；直接具名呼叫：CHotKeyItf::OnUseHotKey
- CHotKeyItf::OnUseHotKey — L121157 @0x004494B4；直接具名呼叫：VCELInterface::?9?$CELPtr, VCELInterface::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetSelling, CHotKeyItf::IsValidKey, CHotKeyItf::MakeDummyItem, CHotKeyItf::OnUseSkill, CSGOPlayer::SetEmotion, CSGOPlayer::IsSummoning, VCELInterface::?7?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?C?$CELPtr
- CHotKeyItf::OnSummon — L121681 @0x00449A21；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::IsSummonable, CSGOUim::SendPacket, CServiceItf::GetPetByUID, CSGOPlayer::GetSummonRemainTime, CSGOPlayer::GetPetRecRemainTime, CSGOUim::AddSystemMsg
- CHotKeyItf::OnUnSummon — L121823 @0x00449BE2；直接具名呼叫：CSGOUim::SendPacket
- CHotKeyItf::OnDataModified — L121849 @0x00449C05；直接具名呼叫：CSGOCore::GetSysTimer, CHotKeyItf::SyncHotKeyInfo, VCELUserButton::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELUserButton::?9?$CELPtr, CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetPetByUID, VCSGOPlayer::?1?$CELPtr, VCELUserButton::?C?$CELPtr, VCELDrawObj::?C?$CELPtr, CHotKeyItf::MakeDummyItem
- CHotKeyItf::OnPage1 — L122874 @0x0044A790；直接具名呼叫：VCELUserButton::?9?$CELPtr, VCELUserButton::?1?$CELPtr, CHotKeyItf::RefreshPageButton
- CHotKeyItf::OnPage2 — L122950 @0x0044A833；直接具名呼叫：VCELUserButton::?9?$CELPtr, VCELUserButton::?1?$CELPtr, CHotKeyItf::RefreshPageButton
- CHotKeyItf::RefreshPageButton — L123026 @0x0044A8D6；直接具名呼叫：VCSGOMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOMessageArea::?9?$CELPtr, VCSGOMessageArea::?C?$CELPtr, VCELUserButton::?0?$CELPtr, VCELProgressBar::?0?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELContainer::?0?$CELPtr, VCELUserButton::?4?$CELPtr, VCELUserButton::?9?$CELPtr, VCELUserButton::?C?$CELPtr, VCELProgressBar::?4?$CELPtr
- CHotKeyItf::OnToggleSize — L123506 @0x0044ADCA；直接具名呼叫：CHotKeyItf::StartToggle
- CHotKeyItf::OnUpdate — L123894 @0x0044B18C；直接具名呼叫：CHotKeyItf::UpdateHotKeyArea, CHotKeyItf::UpdateHotKeyCount, CHotKeyItf::UpdateButtons, CHotKeyItf::RefreshTimer, CHotKeyItf::StartToggle
- CHotKeyItf::UpdateButtons — L124331 @0x0044B666；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CHotKeyItf::UpdateHotKeyArea — L124350 @0x0044B671；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CHotKeyItf::UpdateHotKeyCount — L124369 @0x0044B67C；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CHotKeyItf::RefreshTimer — L124417 @0x0044B6B0；直接具名呼叫：CSGOCore::GetSysTimer, CHotKeyItf::RefreshItemSP, VCELProgressBar::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, CServiceItf::GetPetByUID, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetSummonRemainTime, CSGOPlayer::GetPetRecRemainTime, VCELUserButton::?0?$CELPtr, VCELUserButton::?9?$CELPtr, VCELUserButton::?C?$CELPtr
- CHotKeyItf::RefreshItemSP — L124912 @0x0044BCE1；直接具名呼叫：VPlayerItemData::?0?$CELArray, CHotKeyItf::MakeDummyItem, CItemSysItf::RefreshItemSP, VPlayerItemData::?1?$CELArray
- CHotKeyItf::OnLoadHotKey — L125240 @0x0044BF90；直接具名呼叫：VCSGOUserButton::?0?$CELPtr, CServiceItf::GetItemByUID, CSGOUim::GetApp, CSGOCore::GetItemByID, VCSGOUserButton::?C?$CELPtr, CSGOUserButton::SetDataType, CSGOCore::GetSkillByID, CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetPetByUID, VCSGOPlayer::?1?$CELPtr, CSGOPlayer::GetRidingCardList
- CHotKeyItf::OnSaveHotKey — L125876 @0x0044C707；直接具名呼叫：CSGOUim::SendPacket
- CHotKeyItf::Create — L126803 @0x0044CF9E；直接具名呼叫：CSGOInterface::Create, CSGOUim::SendPacket
- CHotKeyItf::OnUseSkill — L127274 @0x0044D465；直接具名呼叫：VCSGOPlayer::?0?$CELPtr, CSGOUim::GetApp, CSGOCore::GetSkillByID, VCSGOPlayer::?1?$CELPtr, CHotKeyItf::GetSkillDataByID, CSkillItf::IsSkillUsable, CSGOUim::AddSystemMsg, CSGOUim::SetPreUseSkill, CSGOUim::SendUseSkillOnTarget, CSGOUim::SetControlMode

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0028 / ID 2248（type 2，3,342,24,13，H09，HOTKEY_YS_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0068 / ID 2250（type 0，3,4,11,11，H07，OFFON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x009E / ID 2251（type 0，16,4,11,11，h08，OFFON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D4 / ID 3182（type 1，4,18,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00FC / ID 3183（type 1，4,47,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0124 / ID 3184（type 1，4,72,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x014C / ID 3185（type 1，4,99,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0174 / ID 3186（type 1，4,126,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x019C / ID 3187（type 1，4,153,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01C4 / ID 3188（type 1，4,180,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01EC / ID 3189（type 1，4,207,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0214 / ID 3190（type 1，4,234,12,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x023C / ID 3191（type 1，4,261,18,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0265 / ID 3192（type 1，4,288,18,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x028E / ID 3193（type 1，4,315,18,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02B7 / ID 2125（type 0，8,32,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E0 / ID 2126（type 0，8,58,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0309 / ID 2127（type 0，8,85,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0332 / ID 2128（type 0，8,112,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x035B / ID 2129（type 0，8,139,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0384 / ID 2130（type 0，8,166,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03AD / ID 2131（type 0，8,193,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03D6 / ID 2132（type 0，8,220,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03FF / ID 2133（type 0，8,247,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0428 / ID 2134（type 0，8,274,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0451 / ID 2135（type 0，8,301,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x047A / ID 2136（type 0，8,328,18,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04A3 / ID 2234（type 4，4,18,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C0 / ID 2235（type 4，4,45,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04DD / ID 2236（type 4，4,72,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04FA / ID 2237（type 4，4,99,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0517 / ID 2238（type 4，4,126,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0534 / ID 2239（type 4，4,153,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0551 / ID 2240（type 4，4,180,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x056E / ID 2241（type 4，4,207,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x058B / ID 2242（type 4，4,234,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05A8 / ID 2243（type 4，4,261,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05C5 / ID 2244（type 4，4,288,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05E2 / ID 2245（type 4，4,315,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05FF / ID 2113（type 0，4,18,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0628 / ID 2114（type 0，4,45,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0651 / ID 2115（type 0，4,72,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x067A / ID 2117（type 0，4,99,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06A3 / ID 2116（type 0，4,126,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06CC / ID 2118（type 0，4,153,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06F5 / ID 2119（type 0，4,180,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x071E / ID 2120（type 0，4,207,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0747 / ID 2121（type 0，4,234,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0770 / ID 2122（type 0，4,261,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0799 / ID 2123（type 0，4,288,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07C2 / ID 2124（type 0，4,315,24,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07EB / ID 2995（type 3，3,18,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0814 / ID 2996（type 3，3,45,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x083D / ID 2997（type 3，3,72,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0866 / ID 2998（type 3，3,99,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x088F / ID 2999（type 3，3,126,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08B8 / ID 3000（type 3，3,153,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08E1 / ID 3001（type 3，3,180,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x090A / ID 3002（type 3，3,207,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0933 / ID 3003（type 3，3,234,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x095C / ID 3004（type 3，3,261,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0985 / ID 3005（type 3，3,288,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09AE / ID 3006（type 3，3,315,25,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：拖入/移除後狀態同步；按鍵消耗正確；冷卻及重登保存。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-013 選怪與接近攻擊距離](../gameplay/GP-013.md)
- [GP-014 普通攻擊與命中/MISS](../gameplay/GP-014.md)
- [GP-021 單體技能與 MP/冷卻](../gameplay/GP-021.md)
- [GP-025 消耗品使用與恢復](../gameplay/GP-025.md)
- [GP-028 快捷鍵綁定及執行](../gameplay/GP-028.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
