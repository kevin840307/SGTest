# UI-046_Rename — 改名對話框（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-046_Rename.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
共用命名輸入；辨認角色/寵物/機甲呼叫者及限制。

驗收情境：非法與重名拒絕；正確 entity UID；取消不寫入。

## 原版 UI 控制規格
來源：HK/output/Interface/046_Rename.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 6，取得 6。

背景資源：BG_RANK_01.spr, BG_RANK_01.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x002A | 2 | 2006 | 1000 | 235,4,16,16 | — | — | CLOSESCREEN.spr |
| 0x0067 | 8 | 2152 | 1000 | 14,53,236,12 | — | — | — |
| 0x0098 | 1 | 2000 | 1000 | 10,29,96,12 | — | 請輸入最新的名字 | — |
| 0x00CE | 2 | 2005 | 1000 | 163,73,85,21 | — | — | YELLOW_BUTTON.spr |
| 0x010F | 1 | 2000 | 1000 | 194,77,24,12 | — | 確定 | — |
| 0x0139 | 2 | 3116 | 1000 | 217,4,16,16 | OH01 | — | B_ONLINEHELP.spr |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| BG_RANK_01.spr | 1 | 256×100 @(-4,-2) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| YELLOW_BUTTON.spr | 4 | 85×21 @(0,0) ×4 幀 |
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
- [ ] 控件 0x002A / ID 2006（type 2，235,4,16,16，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0067 / ID 2152（type 8，14,53,236,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0098 / ID 2000（type 1，10,29,96,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00CE / ID 2005（type 2，163,73,85,21，YELLOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x010F / ID 2000（type 1，194,77,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0139 / ID 3116（type 2，217,4,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：非法與重名拒絕；正確 entity UID；取消不寫入。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- 無已建立的直接玩法連結；依實際 handler 確認。

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
