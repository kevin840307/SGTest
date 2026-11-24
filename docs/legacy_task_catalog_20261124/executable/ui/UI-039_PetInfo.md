# UI-039_PetInfo — 寵物詳細資訊（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-039_PetInfo.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
寵物數值、配點、餵食、改名、召喚與收回入口。

驗收情境：召喚/收回、HP/MP、選擇寵物與重登狀態一致。

## 原版 UI 控制規格
來源：HK/output/Interface/039_PetInfo.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 76，取得 76。

背景資源：BG_ELFIN_INFO.spr, BG_ELFIN_INFO.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0030 | 0 | 2001 | 1000 | 51,37,118,12 | I01 | — | — |
| 0x005C | 0 | 3091 | 1000 | 51,53,118,12 | I02 | — | — |
| 0x0088 | 0 | 3090 | 1000 | 51,69,118,12 | I03 | — | — |
| 0x00B4 | 1 | 2000 | 1000 | 8,37,36,12 | — | 名　字 | — |
| 0x00E0 | 1 | 2000 | 1000 | 8,53,36,12 | — | 品　種 | — |
| 0x010C | 1 | 2000 | 1000 | 8,69,36,12 | — | 形　態 | — |
| 0x0138 | 1 | 2000 | 1000 | 13,102,24,12 | — | 力量 | — |
| 0x0162 | 1 | 2000 | 1000 | 12,165,24,12 | — | 智慧 | — |
| 0x018C | 1 | 2000 | 1000 | 12,123,24,12 | — | 速度 | — |
| 0x01B6 | 1 | 2000 | 1000 | 4,231,24,12 | — | 生命 | — |
| 0x01E0 | 1 | 2000 | 1000 | 12,144,24,12 | — | 體質 | — |
| 0x020A | 1 | 2000 | 1000 | 4,247,24,12 | — | 魔力 | — |
| 0x0234 | 1 | 2000 | 1000 | 146,102,36,12 | — | 攻　擊 | — |
| 0x0260 | 1 | 2000 | 1000 | 146,119,36,12 | — | 防　御 | — |
| 0x028C | 1 | 2000 | 1000 | 146,136,36,12 | — | 魔　攻 | — |
| 0x02B8 | 1 | 2000 | 1000 | 146,153,36,12 | — | 魔　防 | — |
| 0x02E4 | 1 | 2000 | 1000 | 146,170,36,12 | — | 命　中 | — |
| 0x0310 | 1 | 2000 | 1000 | 146,187,36,12 | — | 回　避 | — |
| 0x033C | 0 | 2045 | 1000 | 39,102,47,10 | I14 | — | — |
| 0x0368 | 0 | 2047 | 1000 | 39,123,47,10 | I15 | — | — |
| 0x0394 | 0 | 2046 | 1000 | 39,144,47,10 | I16 | — | — |
| 0x03C0 | 0 | 2048 | 1000 | 39,165,47,10 | I17 | — | — |
| 0x03EC | 0 | 2167 | 1000 | 189,102,62,10 | I07 | — | — |
| 0x0418 | 0 | 2168 | 1000 | 189,119,62,10 | I08 | — | — |
| 0x0444 | 0 | 2169 | 1000 | 190,136,62,10 | I09 | — | — |
| 0x0470 | 0 | 2170 | 1000 | 189,153,62,10 | I10 | — | — |
| 0x049C | 0 | 2171 | 1000 | 189,170,62,10 | I11 | — | — |
| 0x04C8 | 0 | 2172 | 1000 | 189,187,62,10 | I12 | — | — |
| 0x04F4 | 2 | 2176 | 1000 | 458,1,16,16 | I23 | — | CLOSESCREEN.spr |
| 0x0534 | 2 | 2163 | 1000 | 93,99,16,16 | I25 | — | ADD_POINT.spr |
| 0x0570 | 2 | 2164 | 1000 | 93,120,16,16 | I26 | — | ADD_POINT.spr |
| 0x05AC | 2 | 2165 | 1000 | 93,141,16,16 | I27 | — | ADD_POINT.spr |
| 0x05E8 | 2 | 2166 | 1000 | 93,162,16,16 | I28 | — | ADD_POINT.spr |
| 0x0624 | 2 | 2009 | 1000 | 242,328,17,25 | — | — | SCROLLDOWN_S.spr |
| 0x0663 | 2 | 2008 | 1000 | 242,283,17,25 | — | — | SCROLLUP_S.spr |
| 0x069E | 1 | 2000 | 1000 | 197,69,24,12 | — | 等級 | — |
| 0x06C8 | 0 | 2090 | 1000 | 222,69,33,10 | I24 | — | — |
| 0x06F4 | 3 | 2000 | 1000 | 31,238,0,0 | — | — | — |
| 0x071D | 3 | 2995 | 1000 | 30,231,92,10 | I04 | — | HP.spr |
| 0x0765 | 3 | 2996 | 1000 | 30,248,92,10 | I05 | — | MP.spr |
| 0x07AD | 3 | 2997 | 1000 | 30,265,92,10 | I19 | — | EXP.spr |
| 0x07F7 | 0 | 2000 | 1000 | 76,297,0,0 | — | — | — |
| 0x0820 | 0 | 2158 | 1000 | 77,285,160,65 | — | — | — |
| 0x0849 | 1 | 2000 | 1000 | 4,265,24,12 | — | 經驗 | — |
| 0x0873 | 2 | 3092 | 1000 | 178,35,84,15 | — | — | B_ELFIN_NAME.spr |
| 0x08B2 | 1 | 2000 | 1000 | 209,37,48,12 | — | 修改名稱 | — |
| 0x08E0 | 0 | 2714 | 1000 | 285,51,160,11 | — | — | — |
| 0x0909 | 0 | 2715 | 1000 | 285,67,160,11 | — | — | — |
| 0x0932 | 0 | 2716 | 1000 | 285,83,160,11 | — | — | — |
| 0x095B | 0 | 2717 | 1000 | 285,99,160,11 | — | — | — |
| 0x0984 | 0 | 2718 | 1000 | 285,115,160,11 | — | — | — |
| 0x09AD | 1 | 2000 | 1000 | 281,28,72,12 | — | 戰鬥精靈技能 | — |
| 0x09DF | 2 | 2699 | 1000 | 456,175,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0A28 | 2 | 2697 | 1000 | 456,46,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0A6D | 2 | 2701 | 1000 | 281,216,195,34 | P01 | — | B_ELFIN_FEED.spr |
| 0x0AAF | 2 | 2291 | 1000 | 281,253,195,34 | P02 | — | B_ELFIN_EQUIP.spr |
| 0x0AF3 | 2 | 2702 | 1000 | 281,290,195,34 | P03 | — | B_ELFIN_SUMMON.spr |
| 0x0B39 | 2 | 2703 | 1000 | 281,327,195,34 | P04 | — | B_ELFIN_UNSUMMON.spr |
| 0x0B83 | 0 | 2993 | 1000 | 189,209,62,10 | — | — | — |
| 0x0BAC | 0 | 2994 | 1000 | 189,226,62,10 | — | — | — |
| 0x0BD5 | 1 | 2000 | 1000 | 146,209,36,12 | — | 總攻擊 | — |
| 0x0C01 | 1 | 2000 | 1000 | 146,226,36,12 | — | 總防禦 | — |
| 0x0C2D | 1 | 2000 | 1000 | 412,222,24,12 | — | 飼養 | — |
| 0x0C57 | 1 | 2000 | 1000 | 413,259,24,12 | — | 裝備 | — |
| 0x0C81 | 1 | 2000 | 1000 | 413,296,24,12 | — | 召喚 | — |
| 0x0CAB | 1 | 2000 | 1000 | 407,333,36,12 | — | 反召喚 | — |
| 0x0CD7 | 0 | 2327 | 1000 | 11,288,58,58 | — | — | — |
| 0x0D00 | 0 | 2719 | 1000 | 285,131,160,11 | — | — | — |
| 0x0D29 | 0 | 2720 | 1000 | 285,147,160,11 | — | — | — |
| 0x0D52 | 0 | 2721 | 1000 | 285,163,160,11 | — | — | — |
| 0x0D7B | 0 | 2722 | 1000 | 285,179,160,11 | — | — | — |
| 0x0DA4 | 0 | 2723 | 1000 | 285,195,160,11 | — | — | — |
| 0x0DCD | 2 | 3116 | 1000 | 440,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0E10 | 0 | 3313 | 1000 | 189,262,62,10 | — | — | — |
| 0x0E39 | 1 | 2000 | 1000 | 146,262,36,12 | — | 壽　命 | — |
| 0x0E65 | 5 | 2000 | 1000 | 266,290,0,0 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| ADD_POINT.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_ELFIN_EQUIP.spr | 4 | 172×26 @(-23,0) ×4 幀 |
| B_ELFIN_FEED.spr | 4 | 195×26 @(0,0) ×4 幀 |
| B_ELFIN_NAME.spr | 4 | 84×15 @(0,0) ×4 幀 |
| B_ELFIN_SUMMON.spr | 4 | 149×26 @(-46,0) ×4 幀 |
| B_ELFIN_UNSUMMON.spr | 4 | 126×26 @(-69,0) ×3 幀；126×27 @(-69,0) ×1 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_ELFIN_INFO.spr | 1 | 477×363 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| EXP.spr | 16 | 225×12 @(0,0) ×16 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
| MP.spr | 16 | 109×10 @(0,0) ×16 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLDOWN_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CPetInfoItf::MessageMap — L226569 @0x00488E2C；直接具名呼叫：CServiceItf::MessageMap
- CPetInfoItf::SetPetUID — L226893 @0x004891A2；直接具名呼叫：PAVPetItemSyncData::?A?$CELArray, VCELInterface::?9?$CELPtr, VCPetItemItf::?C?$CELPtr, CPetItemItf::SetPetCard, VCELInterface::?4?$CELPtr, VCELInterface::?1?$CELPtr, VCPetEquipItf::?C?$CELPtr, CPetEquipItf::SetPetCard, CSGOUim::GetMap, CSGOMap::GetMapPetData, VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr
- CPetInfoItf::OnUpdate — L227569 @0x004899E9；直接具名呼叫：COperationalItf::OnUpdate, CPetInfoItf::HandleSummonable, VCEditBox::?9?$CELPtr, VCEditBox::?C?$CELPtr, CSGOInterface::GetData, CPetInfoItf::DoRenamePet
- CPetInfoItf::UpdateButton — L227751 @0x00489B94；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::IsSummoning, CSGOPlayer::IsSummonable, CSGOChar::GetCharSMode, CSGOChar::GetRidingType, VCELUserButton::?C?$CELPtr, VCELUserButton::?4?$CELPtr, CPetInfoItf::IsSummoning, CPetInfoItf::UpdateAttButton, VCELUserButton::?1?$CELPtr
- CPetInfoItf::UpdateAttButton — L228123 @0x00489F7C；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELUserButton::?7?$CELPtr, VCELUserButton::?1?$CELPtr, CPetInfoItf::CheckExpToAtt, VCELUserButton::?C?$CELPtr, CSGOLibrary::GetPlayerAttUpData, UPLAYERATTUP_::?A?$CELArray
- CPetInfoItf::UpdateInfo — L228295 @0x0048A10E；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CPetInfoItf::UpdateMainInfo — L228354 @0x0048A189；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CPetInfoItf::UpdateAttrib — L228614 @0x0048A417；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?1?$CELPtr
- CPetInfoItf::UpdatePBArea — L229186 @0x0048AA98；直接具名呼叫：VCELProgressBar::?0?$CELPtr, VCELProgressBar::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?C?$CELPtr, CPetInfoItf::CalPetExpLv2Lv, VCELProgressBar::?1?$CELPtr
- CPetInfoItf::UpdateGenDesc — L229484 @0x0048AD93；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?7?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELMessageArea::?C?$CELPtr
- CPetInfoItf::OnScrollUp — L229576 @0x0048AE69；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?7?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELMessageArea::?C?$CELPtr
- CPetInfoItf::OnScrollDown — L229662 @0x0048AF2A；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?7?$CELPtr, VCELMessageArea::?1?$CELPtr, VCELMessageArea::?C?$CELPtr
- CPetInfoItf::OnAddCON — L229862 @0x0048B0D1；直接具名呼叫：CPetInfoItf::CheckExpToAtt, CPetInfoItf::SendUpgradePacket
- CPetInfoItf::OnAddSTR — L229898 @0x0048B10F；直接具名呼叫：CPetInfoItf::CheckExpToAtt, CPetInfoItf::SendUpgradePacket
- CPetInfoItf::OnAddDEX — L229934 @0x0048B14D；直接具名呼叫：CPetInfoItf::CheckExpToAtt, CPetInfoItf::SendUpgradePacket
- CPetInfoItf::OnAddWIS — L229970 @0x0048B18B；直接具名呼叫：CPetInfoItf::CheckExpToAtt, CPetInfoItf::SendUpgradePacket
- CPetInfoItf::UpdateSkillList — L230068 @0x0048B23C；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?9?$CELPtr, VCELMessageArea::?C?$CELPtr, VPetSkillData::?A?$CELArray, CPetInfoItf::GetSkillDataByID, VCELMessageArea::?1?$CELPtr
- CPetInfoItf::OnDataModified — L231298 @0x0048C047；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetPetByUID, VCSGOPlayer::?1?$CELPtr
- CPetInfoItf::OnScrollUp02 — L231440 @0x0048C188；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?1?$CELPtr
- CPetInfoItf::OnScrollDown02 — L231521 @0x0048C261；直接具名呼叫：VCELUserButton::?0?$CELPtr, VCELUserButton::?1?$CELPtr
- CPetInfoItf::OnFeed — L231608 @0x0048C343；直接具名呼叫：VCELInterface::?7?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?C?$CELPtr, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCELInterface::?0?$CELPtr, VCELInterface::?1?$CELPtr
- CPetInfoItf::OnEquip — L231722 @0x0048C450；直接具名呼叫：VCELInterface::?7?$CELPtr, VCELInterface::?4?$CELPtr, VCELInterface::?C?$CELPtr, VCSGOInterface::?C?$CELPtr, CSGOInterface::CenterScreen, VCELInterface::?0?$CELPtr, VCELInterface::?1?$CELPtr
- CPetInfoItf::OnSummon — L231836 @0x0048C55D；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::IsSummonable, CSGOUim::SendPacket
- CPetInfoItf::OnUnSummon — L231889 @0x0048C5CD；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::IsSummoning, CSGOUim::SendPacket
- CPetInfoItf::Create — L231947 @0x0048C643；直接具名呼叫：CSGOInterface::Create
- CPetInfoItf::OnRename — L231980 @0x0048C681；直接具名呼叫：CSGOUim::PlaySoundA, VCELInterface::?0?$CELPtr, VCOKBox::?C?$CELPtr, CSGOInterface::CenterScreen, VCELInterface::?1?$CELPtr, VCEditBox::?4?$CELPtr, VCEditBox::?C?$CELPtr, CEditBox::Create

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0030 / ID 2001（type 0，51,37,118,12，I01）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x005C / ID 3091（type 0，51,53,118,12，I02）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0088 / ID 3090（type 0，51,69,118,12，I03）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00B4 / ID 2000（type 1，8,37,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00E0 / ID 2000（type 1，8,53,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x010C / ID 2000（type 1，8,69,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0138 / ID 2000（type 1，13,102,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0162 / ID 2000（type 1，12,165,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x018C / ID 2000（type 1，12,123,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01B6 / ID 2000（type 1，4,231,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01E0 / ID 2000（type 1，12,144,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x020A / ID 2000（type 1，4,247,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0234 / ID 2000（type 1，146,102,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0260 / ID 2000（type 1，146,119,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x028C / ID 2000（type 1，146,136,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02B8 / ID 2000（type 1，146,153,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E4 / ID 2000（type 1，146,170,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0310 / ID 2000（type 1，146,187,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x033C / ID 2045（type 0，39,102,47,10，I14）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0368 / ID 2047（type 0，39,123,47,10，I15）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0394 / ID 2046（type 0，39,144,47,10，I16）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C0 / ID 2048（type 0，39,165,47,10，I17）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03EC / ID 2167（type 0，189,102,62,10，I07）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0418 / ID 2168（type 0，189,119,62,10，I08）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0444 / ID 2169（type 0，190,136,62,10，I09）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0470 / ID 2170（type 0，189,153,62,10，I10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x049C / ID 2171（type 0，189,170,62,10，I11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04C8 / ID 2172（type 0，189,187,62,10，I12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04F4 / ID 2176（type 2，458,1,16,16，I23，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0534 / ID 2163（type 2，93,99,16,16，I25，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0570 / ID 2164（type 2，93,120,16,16，I26，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05AC / ID 2165（type 2，93,141,16,16，I27，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05E8 / ID 2166（type 2，93,162,16,16，I28，ADD_POINT.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0624 / ID 2009（type 2，242,328,17,25，SCROLLDOWN_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0663 / ID 2008（type 2，242,283,17,25，SCROLLUP_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x069E / ID 2000（type 1，197,69,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06C8 / ID 2090（type 0，222,69,33,10，I24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06F4 / ID 2000（type 3，31,238,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x071D / ID 2995（type 3，30,231,92,10，I04，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0765 / ID 2996（type 3，30,248,92,10，I05，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07AD / ID 2997（type 3，30,265,92,10，I19，EXP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07F7 / ID 2000（type 0，76,297,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0820 / ID 2158（type 0，77,285,160,65）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0849 / ID 2000（type 1，4,265,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0873 / ID 3092（type 2，178,35,84,15，B_ELFIN_NAME.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08B2 / ID 2000（type 1，209,37,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08E0 / ID 2714（type 0，285,51,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0909 / ID 2715（type 0，285,67,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0932 / ID 2716（type 0，285,83,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x095B / ID 2717（type 0，285,99,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0984 / ID 2718（type 0，285,115,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09AD / ID 2000（type 1，281,28,72,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09DF / ID 2699（type 2，456,175,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A28 / ID 2697（type 2，456,46,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A6D / ID 2701（type 2，281,216,195,34，P01，B_ELFIN_FEED.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0AAF / ID 2291（type 2，281,253,195,34，P02，B_ELFIN_EQUIP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0AF3 / ID 2702（type 2，281,290,195,34，P03，B_ELFIN_SUMMON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B39 / ID 2703（type 2，281,327,195,34，P04，B_ELFIN_UNSUMMON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B83 / ID 2993（type 0，189,209,62,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BAC / ID 2994（type 0，189,226,62,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BD5 / ID 2000（type 1，146,209,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C01 / ID 2000（type 1，146,226,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C2D / ID 2000（type 1，412,222,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C57 / ID 2000（type 1，413,259,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C81 / ID 2000（type 1，413,296,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CAB / ID 2000（type 1，407,333,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CD7 / ID 2327（type 0，11,288,58,58）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D00 / ID 2719（type 0，285,131,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D29 / ID 2720（type 0，285,147,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D52 / ID 2721（type 0，285,163,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D7B / ID 2722（type 0，285,179,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0DA4 / ID 2723（type 0，285,195,160,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0DCD / ID 3116（type 2，440,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E10 / ID 3313（type 0，189,262,62,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E39 / ID 2000（type 1，146,262,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E65 / ID 2000（type 5，266,290,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：召喚/收回、HP/MP、選擇寵物與重登狀態一致。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-051 寵物召喚與收回](../gameplay/GP-051.md)
- [GP-052 寵物 AI 與戰鬥](../gameplay/GP-052.md)
- [GP-053 寵物餵食、配點與裝備](../gameplay/GP-053.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
