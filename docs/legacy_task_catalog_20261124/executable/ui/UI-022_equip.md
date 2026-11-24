# UI-022_equip — 裝備視窗（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P1。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-022_equip.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
裝備槽、欄位分類、裝卸、裝備預覽與屬性。

驗收情境：合法裝卸成功；職業等級不符拒絕；UID、能力值、重登一致。

## 原版 UI 控制規格
來源：HK/output/Interface/022_equip.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 78，取得 78。

背景資源：BG_Equip.spr, BG_Equip.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0026 | 2 | 2176 | 1000 | 241,1,16,16 | — | — | CLOSESCREEN.spr |
| 0x0063 | 2 | 2009 | 1000 | 204,466,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x00AC | 2 | 2008 | 1000 | 204,310,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x00F1 | 0 | 2311 | 1000 | 72,326,120,12 | — | — | — |
| 0x011A | 0 | 2312 | 1000 | 72,353,120,12 | — | — | — |
| 0x0143 | 0 | 2313 | 1000 | 72,380,120,12 | — | — | — |
| 0x016C | 0 | 2314 | 1000 | 72,407,120,12 | — | — | — |
| 0x0195 | 0 | 2315 | 1000 | 72,434,120,12 | — | — | — |
| 0x01BE | 0 | 2316 | 1000 | 72,461,120,12 | — | — | — |
| 0x01E7 | 0 | 2317 | 1000 | 72,488,120,12 | — | — | — |
| 0x0210 | 1 | 2000 | 1000 | 44,37,48,12 | — | 餘下空間 | — |
| 0x023E | 0 | 2290 | 1000 | 102,37,34,10 | EQ06 | — | — |
| 0x026B | 0 | 2284 | 1000 | 0,21,35,37 | EQ01 | — | B_ITEM_ITEM.spr |
| 0x02A8 | 0 | 2291 | 1000 | 0,58,35,37 | EQ02 | — | B_ITEM_EQUIP.spr |
| 0x02E6 | 0 | 2292 | 1000 | 0,95,35,37 | EQ03 | — | B_ITEM_PET.spr |
| 0x0322 | 0 | 2293 | 1000 | 0,132,35,37 | EQ04 | — | B_ITEM_RIDE.spr |
| 0x035F | 0 | 2294 | 1000 | 0,169,35,37 | EQ05 | — | B_ITEM_ARMOR.spr |
| 0x039D | 4 | 2335 | 1000 | 39,310,157,190 | — | — | — |
| 0x03BA | 4 | 2948 | 1000 | 44,63,90,24 | — | — | — |
| 0x03D7 | 4 | 2955 | 1000 | 43,252,90,24 | — | — | — |
| 0x03F4 | 4 | 2949 | 1000 | 44,90,90,24 | — | — | — |
| 0x0411 | 4 | 2950 | 1000 | 44,117,90,24 | — | — | — |
| 0x042E | 4 | 2951 | 1000 | 44,144,90,24 | — | — | — |
| 0x044B | 4 | 2952 | 1000 | 44,171,90,24 | — | — | — |
| 0x0468 | 4 | 2953 | 1000 | 44,198,90,24 | — | — | — |
| 0x0485 | 4 | 2954 | 1000 | 43,224,90,24 | — | — | — |
| 0x04A2 | 0 | 2983 | 1000 | 71,66,18,18 | — | — | B_RADIO_B.spr |
| 0x04D9 | 0 | 2986 | 1000 | 71,93,18,18 | — | — | B_RADIO_B.spr |
| 0x0510 | 0 | 2984 | 1000 | 71,147,18,18 | — | — | B_RADIO_B.spr |
| 0x0547 | 0 | 2985 | 1000 | 71,120,18,18 | — | — | B_RADIO_B.spr |
| 0x057E | 0 | 2987 | 1000 | 71,174,18,18 | — | — | B_RADIO_B.spr |
| 0x05B5 | 0 | 2988 | 1000 | 71,201,18,18 | — | — | B_RADIO_B.spr |
| 0x05EC | 0 | 2989 | 1000 | 71,228,18,18 | — | — | B_RADIO_B.spr |
| 0x0623 | 0 | 2990 | 1000 | 71,255,18,18 | — | — | B_RADIO_B.spr |
| 0x065A | 1 | 2000 | 1000 | 105,205,24,12 | — | 武器 | — |
| 0x0684 | 1 | 2000 | 1000 | 105,232,24,12 | — | 盾牌 | — |
| 0x06AE | 1 | 2000 | 1000 | 105,70,24,12 | — | 頭部 | — |
| 0x06D8 | 1 | 2000 | 1000 | 105,97,24,12 | — | 左手 | — |
| 0x0702 | 1 | 2000 | 1000 | 106,124,24,12 | — | 右手 | — |
| 0x072C | 1 | 2000 | 1000 | 105,151,24,12 | — | 身體 | — |
| 0x0756 | 1 | 2000 | 1000 | 105,178,24,12 | — | 腳部 | — |
| 0x0780 | 1 | 2000 | 1000 | 105,259,24,12 | — | 背部 | — |
| 0x07AA | 2 | 2957 | 1004 | 43,63,24,24 | — | — | ICON.spr |
| 0x07D9 | 2 | 2958 | 1004 | 43,89,24,24 | — | — | ICON.spr |
| 0x0808 | 2 | 2959 | 1004 | 43,116,24,24 | — | — | ICON.spr |
| 0x0837 | 2 | 2960 | 1004 | 43,144,24,24 | — | — | ICON.spr |
| 0x0866 | 2 | 2961 | 1004 | 43,173,24,24 | — | — | ICON.spr |
| 0x0895 | 2 | 2962 | 1004 | 43,197,24,24 | — | — | ICON.spr |
| 0x08C4 | 2 | 2963 | 1004 | 43,225,24,24 | — | — | ICON.spr |
| 0x08F3 | 2 | 2964 | 1004 | 43,252,24,24 | — | — | ICON.spr |
| 0x0922 | 2 | 2450 | 1004 | 43,313,24,24 | — | — | ICON.spr |
| 0x0951 | 2 | 2451 | 1004 | 43,340,24,24 | — | — | ICON.spr |
| 0x0980 | 2 | 2452 | 1004 | 43,367,24,24 | — | — | ICON.spr |
| 0x09AF | 2 | 2453 | 1004 | 43,394,24,24 | — | — | ICON.spr |
| 0x09DE | 2 | 2454 | 1004 | 43,421,24,24 | — | — | ICON.spr |
| 0x0A0D | 2 | 2455 | 1004 | 43,449,24,24 | — | — | ICON.spr |
| 0x0A3C | 2 | 2456 | 1004 | 43,476,24,24 | — | — | ICON.spr |
| 0x0A6B | 2 | 2965 | 1004 | 43,278,24,24 | — | — | ICON.spr |
| 0x0A9A | 4 | 2956 | 1000 | 43,278,90,26 | — | — | — |
| 0x0AB7 | 0 | 2991 | 1000 | 71,282,18,18 | — | — | B_RADIO_B.spr |
| 0x0AEE | 1 | 2000 | 1000 | 105,286,24,12 | — | 護符 | — |
| 0x0B18 | 2 | 3116 | 1000 | 224,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0B5B | 5 | 3017 | 1000 | 43,61,90,27 | — | — | — |
| 0x0B78 | 5 | 3018 | 1000 | 43,88,90,27 | — | — | — |
| 0x0B95 | 5 | 3019 | 1000 | 43,115,90,27 | — | — | — |
| 0x0BB2 | 5 | 3020 | 1000 | 43,142,90,27 | — | — | — |
| 0x0BCF | 5 | 3021 | 1000 | 43,169,90,27 | — | — | — |
| 0x0BEC | 5 | 3022 | 1000 | 43,196,90,27 | — | — | — |
| 0x0C09 | 5 | 3023 | 1000 | 42,223,90,27 | — | — | — |
| 0x0C26 | 5 | 3024 | 1000 | 42,250,90,27 | — | — | — |
| 0x0C43 | 5 | 3025 | 1000 | 42,277,90,27 | — | — | — |
| 0x0C60 | 5 | 3162 | 1000 | 43,312,152,27 | — | — | — |
| 0x0C7D | 5 | 3163 | 1000 | 43,339,152,27 | — | — | — |
| 0x0C9A | 5 | 3164 | 1000 | 43,366,152,27 | — | — | — |
| 0x0CB7 | 5 | 3165 | 1000 | 43,393,152,27 | — | — | — |
| 0x0CD4 | 5 | 3166 | 1000 | 43,420,152,27 | — | — | — |
| 0x0CF1 | 5 | 3167 | 1000 | 43,447,152,27 | — | — | — |
| 0x0D0E | 5 | 3168 | 1000 | 43,474,152,27 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ITEM_ARMOR.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_EQUIP.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_ITEM.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_PET.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_RIDE.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_RADIO_B.spr | 2 | 19×19 @(0,0) ×2 幀 |
| BG_Equip.spr | 1 | 259×506 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| ICON.spr | 500 | 24×24 @(0,0) ×500 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CEquipItf::MessageMap — L146691 @0x00457FCC；直接具名呼叫：CEquipItf::OnShowHelm, CEquipItf::OnShowHandL, CEquipItf::OnShowHandR, CEquipItf::OnShowBody, CEquipItf::OnShowLeg, CEquipItf::OnShowWeapon, CEquipItf::OnShowShield, CEquipItf::OnShowBack, CEquipItf::OnShowDieItem, CEquipItf::OnEquipOn, CEquipItf::OnEquipButOn, CEquipItf::OnEquipButOff
- CEquipItf::Create — L147312 @0x004586F3；直接具名呼叫：CSGOInterface::Create, VCELHelpBound::?0?$CELPtr, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, CEquipItf::ClearItemList, CEquipItf::ClearEquipList, VCELHelpBound::?4?$CELPtr, VCELHelpBound::?1?$CELPtr, VCELHelpBound::?9?$CELPtr, VCELItfComp::?0?$CELPtr
- CEquipItf::ShowEquip — L148044 @0x00458E58；直接具名呼叫：VPlayerItemData::?A?$CELArray, CSGOLibrary::GetItemData
- CEquipItf::UpdateSpace — L148236 @0x00459078；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VPlayerItemData::?A?$CELArray, CSGOLibrary::GetItemData, VCELMessageArea::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetPetSyncList, PAVPetItemSyncData::?A?$CELArray, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr
- CEquipItf::UpdateInfo — L148666 @0x00459499；直接具名呼叫：CItemSysItf::UpdateInfo, CEquipItf::UpdateDescription, VCSGOChar::?9?$CELPtr, VCSGOChar::?C?$CELPtr
- CEquipItf::UpdateDescription — L149795 @0x0045A02D；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELHelpBound::?0?$CELPtr, VCSGOUserButton::?0?$CELPtr, CEquipItf::ClearEquipList, VPlayerItemData::?A?$CELArray, CSGOLibrary::GetItemData, VCSGOUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELHelpBound::?4?$CELPtr, VCELHelpBound::?1?$CELPtr, VCSGOUserButton::?9?$CELPtr, VCELHelpBound::?9?$CELPtr
- CEquipItf::UpdateButton — L150937 @0x0045AFDF；直接具名呼叫：CItemSysItf::UpdateButton
- CEquipItf::SetUpdateList — L151091 @0x0045B13C；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CEquipItf::OnShowProcess — L151469 @0x0045B516；直接具名呼叫：CEquipItf::ClearChoiceBut, CEquipItf::DisplayAllEquip, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, CEquipItf::ShowEquip, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CEquipItf::OnShowShield — L151587 @0x0045B645；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowWeapon — L151611 @0x0045B661；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowHelm — L151635 @0x0045B67D；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowHandL — L151659 @0x0045B699；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowHandR — L151683 @0x0045B6B5；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowBody — L151707 @0x0045B6D1；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowLeg — L151731 @0x0045B6ED；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowBack — L151755 @0x0045B709；直接具名呼叫：CEquipItf::OnShowProcess
- CEquipItf::OnShowDieItem — L151779 @0x0045B728；直接具名呼叫：CEquipItf::ClearChoiceBut, CEquipItf::ProcessShowDieItem
- CEquipItf::ProcessShowDieItem — L151808 @0x0045B75F；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VPlayerItemData::?A?$CELArray, CSGOLibrary::GetItemData, VCELMessageArea::?1?$CELPtr
- CEquipItf::OnEquipButOn — L152135 @0x0045BB54；直接具名呼叫：CEquipItf::OnEquipOn
- CEquipItf::OnEquipOn — L152165 @0x0045BB84；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr, VPlayerItemData::?A?$CELArray, CSGOUim::GetApp, CSGOCore::GetItemByID, CServiceItf::IsItemUsable, CSGOUim::SendPacket
- CEquipItf::OnEquipButOff — L152440 @0x0045BE00；直接具名呼叫：CEquipItf::OnEquipOff
- CEquipItf::OnEquipOff — L152467 @0x0045BE24；直接具名呼叫：CSGOUim::SendPacket, CEquipItf::AutoSetFilter
- CEquipItf::OnScrollUp — L152625 @0x0045BF7C；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?1?$CELPtr
- CEquipItf::OnScrollDown — L152706 @0x0045C055；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?1?$CELPtr
- CEquipItf::SetDiscount — L152856 @0x0045C1C2；直接具名呼叫：CEquipItf::SetUpdateList
- CEquipItf::Draw — L152886 @0x0045C1F2；直接具名呼叫：CSGOInterface::Draw, CSGOMap::IsMapNotReady, VCSGOChar::?9?$CELPtr, CELMap2D::MapPosY, CELMap2D::MapPosX, VCSGOChar::?C?$CELPtr, CSGOChar::SetMapPosition, CSGOChar::DrawAlpha
- CEquipItf::UpdateFigure1 — L152963 @0x0045C2BB；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCSGOChar::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::ActionStandAdj, CSGOChar::SetAction

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0026 / ID 2176（type 2，241,1,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0063 / ID 2009（type 2，204,466,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AC / ID 2008（type 2，204,310,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00F1 / ID 2311（type 0，72,326,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x011A / ID 2312（type 0，72,353,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0143 / ID 2313（type 0，72,380,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x016C / ID 2314（type 0，72,407,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0195 / ID 2315（type 0，72,434,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01BE / ID 2316（type 0，72,461,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01E7 / ID 2317（type 0，72,488,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0210 / ID 2000（type 1，44,37,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x023E / ID 2290（type 0，102,37,34,10，EQ06）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x026B / ID 2284（type 0，0,21,35,37，EQ01，B_ITEM_ITEM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02A8 / ID 2291（type 0，0,58,35,37，EQ02，B_ITEM_EQUIP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E6 / ID 2292（type 0，0,95,35,37，EQ03，B_ITEM_PET.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0322 / ID 2293（type 0，0,132,35,37，EQ04，B_ITEM_RIDE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x035F / ID 2294（type 0，0,169,35,37，EQ05，B_ITEM_ARMOR.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x039D / ID 2335（type 4，39,310,157,190）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03BA / ID 2948（type 4，44,63,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03D7 / ID 2955（type 4，43,252,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03F4 / ID 2949（type 4，44,90,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0411 / ID 2950（type 4，44,117,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x042E / ID 2951（type 4，44,144,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x044B / ID 2952（type 4，44,171,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0468 / ID 2953（type 4，44,198,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0485 / ID 2954（type 4，43,224,90,24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04A2 / ID 2983（type 0，71,66,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04D9 / ID 2986（type 0，71,93,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0510 / ID 2984（type 0，71,147,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0547 / ID 2985（type 0，71,120,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x057E / ID 2987（type 0，71,174,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05B5 / ID 2988（type 0，71,201,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05EC / ID 2989（type 0，71,228,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0623 / ID 2990（type 0，71,255,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x065A / ID 2000（type 1，105,205,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0684 / ID 2000（type 1，105,232,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06AE / ID 2000（type 1，105,70,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06D8 / ID 2000（type 1，105,97,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0702 / ID 2000（type 1，106,124,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x072C / ID 2000（type 1，105,151,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0756 / ID 2000（type 1，105,178,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0780 / ID 2000（type 1，105,259,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07AA / ID 2957（type 2，43,63,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07D9 / ID 2958（type 2，43,89,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0808 / ID 2959（type 2，43,116,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0837 / ID 2960（type 2，43,144,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0866 / ID 2961（type 2，43,173,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0895 / ID 2962（type 2，43,197,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08C4 / ID 2963（type 2，43,225,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08F3 / ID 2964（type 2，43,252,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0922 / ID 2450（type 2，43,313,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0951 / ID 2451（type 2，43,340,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0980 / ID 2452（type 2，43,367,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09AF / ID 2453（type 2，43,394,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09DE / ID 2454（type 2，43,421,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A0D / ID 2455（type 2，43,449,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A3C / ID 2456（type 2，43,476,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A6B / ID 2965（type 2，43,278,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A9A / ID 2956（type 4，43,278,90,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0AB7 / ID 2991（type 0，71,282,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0AEE / ID 2000（type 1，105,286,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B18 / ID 3116（type 2，224,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B5B / ID 3017（type 5，43,61,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B78 / ID 3018（type 5，43,88,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B95 / ID 3019（type 5，43,115,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BB2 / ID 3020（type 5，43,142,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BCF / ID 3021（type 5，43,169,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BEC / ID 3022（type 5，43,196,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C09 / ID 3023（type 5，42,223,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C26 / ID 3024（type 5，42,250,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C43 / ID 3025（type 5，42,277,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C60 / ID 3162（type 5，43,312,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C7D / ID 3163（type 5，43,339,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C9A / ID 3164（type 5，43,366,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CB7 / ID 3165（type 5，43,393,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CD4 / ID 3166（type 5，43,420,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CF1 / ID 3167（type 5，43,447,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D0E / ID 3168（type 5，43,474,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：合法裝卸成功；職業等級不符拒絕；UID、能力值、重登一致。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-005 人物初始外觀與 paper doll](../gameplay/GP-005.md)
- [GP-012 正式角色戰鬥屬性解鎖](../gameplay/GP-012.md)
- [GP-026 裝備裝卸與屬性](../gameplay/GP-026.md)
- [GP-034 裝備耐久與修理](../gameplay/GP-034.md)
- [GP-035 裝備強化](../gameplay/GP-035.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
