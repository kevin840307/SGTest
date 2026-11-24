# UI-056_Driver_item — 機甲物品/裝備列表（056_Driver_item）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-056_Driver_item.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
選擇機甲物品、分類、裝卸或查看入口；確認 UID。

驗收情境：合法裝卸、非法類型拒絕、同步與重登。

## 原版 UI 控制規格
來源：HK/output/Interface/056_Driver_item.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 68，取得 68。

背景資源：BG_DRIVE_ITEM.spr, BG_DRIVE_ITEM.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0030 | 2 | 2176 | 1000 | 241,1,16,16 | — | — | CLOSESCREEN.spr |
| 0x006D | 2 | 2009 | 1000 | 204,200,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x00B6 | 2 | 2008 | 1000 | 204,109,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x00FB | 0 | 2714 | 1000 | 72,90,120,12 | — | — | — |
| 0x0124 | 0 | 2715 | 1000 | 72,141,120,12 | — | — | — |
| 0x014D | 0 | 2716 | 1000 | 72,168,120,12 | — | — | — |
| 0x0176 | 0 | 2717 | 1000 | 72,195,120,12 | — | — | — |
| 0x019F | 0 | 2718 | 1000 | 72,222,120,12 | — | — | — |
| 0x01C8 | 0 | 2719 | 1000 | 73,274,120,12 | — | — | — |
| 0x01F1 | 0 | 2720 | 1000 | 73,301,120,12 | — | — | — |
| 0x021A | 0 | 2721 | 1000 | 73,328,120,12 | — | — | — |
| 0x0243 | 0 | 2722 | 1000 | 73,355,120,12 | — | — | — |
| 0x026C | 0 | 2723 | 1000 | 73,382,120,12 | — | — | — |
| 0x0295 | 0 | 2724 | 1000 | 72,435,120,12 | — | — | — |
| 0x02BE | 0 | 2725 | 1000 | 72,462,120,12 | — | — | — |
| 0x02E7 | 0 | 2726 | 1000 | 72,489,120,12 | — | — | — |
| 0x0310 | 1 | 2000 | 1000 | 46,37,60,12 | — | 機甲卡空間 | — |
| 0x0340 | 0 | 2290 | 1000 | 113,37,18,10 | EQ06 | — | — |
| 0x036D | 0 | 2284 | 1000 | 0,21,35,37 | EQ01 | — | B_ITEM_ITEM.spr |
| 0x03AA | 0 | 2291 | 1000 | 0,58,35,37 | EQ02 | — | B_ITEM_EQUIP.spr |
| 0x03E8 | 0 | 2292 | 1000 | 0,95,35,37 | EQ03 | — | B_ITEM_PET.spr |
| 0x0424 | 0 | 2293 | 1000 | 0,132,35,37 | EQ04 | — | B_ITEM_RIDE.spr |
| 0x0461 | 0 | 2294 | 1000 | 0,169,35,37 | EQ05 | — | B_ITEM_ARMOR.spr |
| 0x049F | 0 | 2365 | 1000 | 72,421,30,13 | — | — | — |
| 0x04C8 | 0 | 2366 | 1000 | 72,448,30,13 | — | — | — |
| 0x04F1 | 0 | 2367 | 1000 | 72,475,30,13 | — | — | — |
| 0x051A | 2 | 2455 | 1000 | 43,261,24,24 | — | — | ICON.spr |
| 0x0549 | 2 | 2456 | 1000 | 43,288,24,24 | — | — | ICON.spr |
| 0x0578 | 2 | 2457 | 1000 | 43,316,24,24 | — | — | ICON.spr |
| 0x05A7 | 2 | 2458 | 1000 | 44,342,24,24 | — | — | ICON.spr |
| 0x05D6 | 2 | 2459 | 1000 | 44,369,24,24 | — | — | ICON.spr |
| 0x0605 | 2 | 2460 | 1000 | 43,422,24,24 | — | — | ICON.spr |
| 0x0634 | 2 | 2461 | 1000 | 43,450,24,24 | — | — | ICON.spr |
| 0x0663 | 2 | 2462 | 1000 | 43,476,24,24 | — | — | ICON.spr |
| 0x0692 | 2 | 2450 | 1000 | 43,77,24,24 | — | — | ICON.spr |
| 0x06C1 | 2 | 2451 | 1000 | 43,128,24,24 | — | — | ICON.spr |
| 0x06F0 | 2 | 2452 | 1000 | 43,156,24,24 | — | — | ICON.spr |
| 0x071F | 2 | 2453 | 1000 | 42,182,24,24 | — | — | ICON.spr |
| 0x074E | 2 | 2454 | 1000 | 42,209,24,24 | — | — | ICON.spr |
| 0x077D | 3 | 2000 | 1000 | 58,123,0,0 | — | — | — |
| 0x07A6 | 2 | 3116 | 1000 | 225,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x07E9 | 5 | 3017 | 1000 | 43,75,152,27 | — | — | — |
| 0x0806 | 5 | 3018 | 1000 | 43,127,152,27 | — | — | — |
| 0x0823 | 5 | 3019 | 1000 | 43,154,152,27 | — | — | — |
| 0x0840 | 5 | 3020 | 1000 | 43,181,152,27 | — | — | — |
| 0x085D | 5 | 3021 | 1000 | 43,208,152,27 | — | — | — |
| 0x087A | 5 | 3022 | 1000 | 43,259,152,27 | — | — | — |
| 0x0897 | 5 | 3023 | 1000 | 43,286,152,27 | — | — | — |
| 0x08B4 | 5 | 3024 | 1000 | 42,313,152,27 | — | — | — |
| 0x08D1 | 5 | 3025 | 1000 | 42,340,152,27 | — | — | — |
| 0x08EE | 5 | 3026 | 1000 | 42,367,152,27 | — | — | — |
| 0x090B | 5 | 3027 | 1000 | 41,420,152,27 | — | — | — |
| 0x0928 | 5 | 3028 | 1000 | 41,447,152,27 | — | — | — |
| 0x0945 | 5 | 3029 | 1000 | 41,474,152,27 | — | — | — |
| 0x0962 | 4 | 2000 | 1000 | 361,214,0,0 | — | — | — |
| 0x097F | 4 | 2000 | 1000 | 171,210,0,0 | — | — | — |
| 0x099C | 4 | 2000 | 1000 | 174,240,0,0 | — | — | — |
| 0x09B9 | 1 | 2000 | 1000 | 85,60,60,12 | — | 使用中機甲 | — |
| 0x09E9 | 1 | 2000 | 1000 | 86,111,36,12 | — | 機甲卡 | — |
| 0x0A15 | 1 | 2000 | 1000 | 85,243,48,12 | — | 機甲裝備 | — |
| 0x0A43 | 1 | 2000 | 1000 | 85,404,48,12 | — | 機甲能源 | — |
| 0x0A71 | 2 | 2699 | 1000 | 204,361,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0ABA | 2 | 2700 | 1000 | 204,466,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0B03 | 2 | 2333 | 1000 | 204,241,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0B48 | 2 | 2712 | 1000 | 204,402,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0B8D | 1 | 2000 | 1000 | 85,77,24,12 | — | 機甲 | — |
| 0x0BB7 | 1 | 2000 | 1000 | 141,37,84,12 | — | 裝備與能源空間 | — |
| 0x0BEB | 0 | 2154 | 1000 | 232,37,18,10 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ITEM_ARMOR.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_EQUIP.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_ITEM.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_PET.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ITEM_RIDE.spr | 4 | 36×37 @(0,0) ×4 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_DRIVE_ITEM.spr | 1 | 259×506 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| ICON.spr | 500 | 24×24 @(0,0) ×500 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CRidingEquipItf::SetDropRideCardUID — L58423 @0x00423F00；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRidingEquipItf::MessageMap — L241631 @0x0049243C；直接具名呼叫：CRidingEquipItf::OnSummonRidingInfo, CRidingEquipItf::OnRidingInfo, CRidingEquipItf::OnRidingInfoBut, CRidingEquipItf::OnAddBattery, CRidingEquipItf::OnAddBatteryBut, CItemSysItf::MessageMap
- CRidingEquipItf::Create — L242033 @0x0049288A；直接具名呼叫：CSGOInterface::Create, VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CRidingEquipItf::OnDataModified — L242718 @0x00492F2E；直接具名呼叫：VCSGOMessageArea::?0?$CELPtr, VCELHelpBound::?0?$CELPtr, VCSGOUserButton::?0?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, VCSGOUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOUserButton::?9?$CELPtr, VCSGOUserButton::?C?$CELPtr, VCSGOMessageArea::?4?$CELPtr, VCSGOMessageArea::?9?$CELPtr, VCSGOMessageArea::?C?$CELPtr
- CRidingEquipItf::OnScrollUp — L244415 @0x0049438C；直接具名呼叫：CRidingEquipItf::HandleScrollButStatus
- CRidingEquipItf::OnScrollDown — L244496 @0x00494454；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, CRidingEquipItf::HandleScrollButStatus, CSGOPlayer::GetRidingEquipBagList, CSGOPlayer::GetRidingBatteryBagList
- CRidingEquipItf::OnAddBatteryBut — L244601 @0x0049457D；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, PAVRidingCardSyncData::?A?$CELArray, CSGOPlayer::GetRidingBatteryBagList, PAVRidingBatterySyncData::?A?$CELArray, CSGOUim::SendPacket
- CRidingEquipItf::OnAddBattery — L244717 @0x00494682；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, PAVRidingCardSyncData::?A?$CELArray, CSGOPlayer::GetRidingBatteryBagList, PAVRidingBatterySyncData::?A?$CELArray, CSGOUim::SendPacket
- CRidingEquipItf::OnSummonRidingInfo — L244833 @0x00494787；直接具名呼叫：VCELInterface::?9?$CELPtr, VCELInterface::?C?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, PAVRidingCardSyncData::?A?$CELArray, VCRidingInfoItf::?0?$CELPtr, VCRidingInfoItf::?4?$CELPtr, VCRidingInfoItf::?C?$CELPtr, CSGOInterface::CenterScreen, CRidingInfoItf::SetRiding, VCELInterface::?0?$CELPtr, VCRidingInfoItf::?1?$CELPtr
- CRidingEquipItf::OnRidingInfo — L245101 @0x00494A0F；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, VCELInterface::?9?$CELPtr, VCELInterface::?C?$CELPtr, VCRidingInfoItf::?0?$CELPtr, PAVRidingCardSyncData::?A?$CELArray, VCRidingInfoItf::?4?$CELPtr, VCRidingInfoItf::?C?$CELPtr, CSGOInterface::CenterScreen, CRidingInfoItf::SetRiding, VCELInterface::?0?$CELPtr, VCRidingInfoItf::?1?$CELPtr
- CRidingEquipItf::OnRidingInfoBut — L245319 @0x00494C1A；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, VCELInterface::?9?$CELPtr, VCELInterface::?C?$CELPtr, VCRidingInfoItf::?0?$CELPtr, PAVRidingCardSyncData::?A?$CELArray, VCRidingInfoItf::?4?$CELPtr, VCRidingInfoItf::?C?$CELPtr, CSGOInterface::CenterScreen, CRidingInfoItf::SetRiding, VCELInterface::?0?$CELPtr, VCRidingInfoItf::?1?$CELPtr
- CRidingEquipItf::OnUpdate — L245537 @0x00494E25；直接具名呼叫：CItemSysItf::OnUpdate, CSGOUim::SendPacket, CSGOUim::PlaySoundA
- CRidingEquipItf::UpdateSpace — L245681 @0x00494F6C；直接具名呼叫：VCSGOMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOMessageArea::?9?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingCardList, VCSGOMessageArea::?C?$CELPtr, VCSGOMessageArea::?4?$CELPtr, CSGOPlayer::GetRidingBatteryBagList, CSGOPlayer::GetRidingEquipBagList, VCSGOMessageArea::?1?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0030 / ID 2176（type 2，241,1,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x006D / ID 2009（type 2，204,200,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B6 / ID 2008（type 2，204,109,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00FB / ID 2714（type 0，72,90,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0124 / ID 2715（type 0，72,141,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x014D / ID 2716（type 0，72,168,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0176 / ID 2717（type 0，72,195,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x019F / ID 2718（type 0，72,222,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01C8 / ID 2719（type 0，73,274,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01F1 / ID 2720（type 0，73,301,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x021A / ID 2721（type 0，73,328,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0243 / ID 2722（type 0，73,355,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x026C / ID 2723（type 0，73,382,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0295 / ID 2724（type 0，72,435,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02BE / ID 2725（type 0，72,462,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E7 / ID 2726（type 0，72,489,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0310 / ID 2000（type 1，46,37,60,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0340 / ID 2290（type 0，113,37,18,10，EQ06）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x036D / ID 2284（type 0，0,21,35,37，EQ01，B_ITEM_ITEM.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03AA / ID 2291（type 0，0,58,35,37，EQ02，B_ITEM_EQUIP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03E8 / ID 2292（type 0，0,95,35,37，EQ03，B_ITEM_PET.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0424 / ID 2293（type 0，0,132,35,37，EQ04，B_ITEM_RIDE.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0461 / ID 2294（type 0，0,169,35,37，EQ05，B_ITEM_ARMOR.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x049F / ID 2365（type 0，72,421,30,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C8 / ID 2366（type 0，72,448,30,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04F1 / ID 2367（type 0，72,475,30,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x051A / ID 2455（type 2，43,261,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0549 / ID 2456（type 2，43,288,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0578 / ID 2457（type 2，43,316,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05A7 / ID 2458（type 2，44,342,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05D6 / ID 2459（type 2，44,369,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0605 / ID 2460（type 2，43,422,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0634 / ID 2461（type 2，43,450,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0663 / ID 2462（type 2，43,476,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0692 / ID 2450（type 2，43,77,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06C1 / ID 2451（type 2，43,128,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06F0 / ID 2452（type 2，43,156,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x071F / ID 2453（type 2，42,182,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x074E / ID 2454（type 2，42,209,24,24，ICON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x077D / ID 2000（type 3，58,123,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07A6 / ID 3116（type 2，225,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07E9 / ID 3017（type 5，43,75,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0806 / ID 3018（type 5，43,127,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0823 / ID 3019（type 5，43,154,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0840 / ID 3020（type 5，43,181,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x085D / ID 3021（type 5，43,208,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x087A / ID 3022（type 5，43,259,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0897 / ID 3023（type 5，43,286,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08B4 / ID 3024（type 5，42,313,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08D1 / ID 3025（type 5，42,340,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08EE / ID 3026（type 5，42,367,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x090B / ID 3027（type 5，41,420,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0928 / ID 3028（type 5，41,447,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0945 / ID 3029（type 5，41,474,152,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0962 / ID 2000（type 4，361,214,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x097F / ID 2000（type 4，171,210,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x099C / ID 2000（type 4，174,240,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09B9 / ID 2000（type 1，85,60,60,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09E9 / ID 2000（type 1，86,111,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A15 / ID 2000（type 1，85,243,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A43 / ID 2000（type 1，85,404,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A71 / ID 2699（type 2，204,361,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0ABA / ID 2700（type 2，204,466,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B03 / ID 2333（type 2，204,241,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B48 / ID 2712（type 2，204,402,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B8D / ID 2000（type 1，85,77,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BB7 / ID 2000（type 1，141,37,84,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BEB / ID 2154（type 0，232,37,18,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：合法裝卸、非法類型拒絕、同步與重登。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-055 機甲搭乘與解除](../gameplay/GP-055.md)
- [GP-056 機甲裝備與能源](../gameplay/GP-056.md)
- [GP-057 機甲修理與升級](../gameplay/GP-057.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
