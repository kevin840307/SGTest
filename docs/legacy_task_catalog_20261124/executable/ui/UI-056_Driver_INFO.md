# UI-056_Driver_INFO — 機甲詳細資訊（056_Driver_INFO）（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-056_Driver_INFO.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
依 Driver INFO/ITEM 變體重現機甲資訊、裝備與能源、物品管理。

驗收情境：搭乘、卸下、能量、裝備屬性與重登一致。

## 原版 UI 控制規格
來源：HK/output/Interface/056_Driver_INFO.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 118，取得 118。

背景資源：BG_DRIVER.spr, BG_DRIVER.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0028 | 0 | 2001 | 1000 | 50,37,118,12 | I01 | — | — |
| 0x0054 | 0 | 2154 | 1000 | 50,53,118,12 | I02 | — | — |
| 0x0080 | 0 | 2155 | 1000 | 50,69,118,12 | I03 | — | — |
| 0x00AC | 1 | 2000 | 1000 | 9,37,36,12 | — | 名　稱 | — |
| 0x00D8 | 1 | 2000 | 1000 | 9,53,36,12 | — | 類　形 | — |
| 0x0104 | 1 | 2000 | 1000 | 9,69,36,12 | — | 能　源 | — |
| 0x0130 | 1 | 2000 | 1000 | 5,280,24,12 | — | 生命 | — |
| 0x015A | 1 | 2000 | 1000 | 4,296,24,12 | — | 魔力 | — |
| 0x0184 | 1 | 2000 | 1000 | 4,312,24,12 | — | 意志 | — |
| 0x01AE | 1 | 2000 | 1000 | 136,111,48,12 | — | 能源消耗 | — |
| 0x01DC | 1 | 2000 | 1000 | 10,115,36,12 | — | 攻　擊 | — |
| 0x0208 | 1 | 2000 | 1000 | 10,132,36,12 | — | 防　御 | — |
| 0x0234 | 1 | 2000 | 1000 | 10,149,36,12 | — | 魔　攻 | — |
| 0x0260 | 1 | 2000 | 1000 | 10,166,36,12 | — | 魔　防 | — |
| 0x028C | 1 | 2000 | 1000 | 10,183,36,12 | — | 命　中 | — |
| 0x02B8 | 1 | 2000 | 1000 | 10,200,36,12 | — | 回　避 | — |
| 0x02E4 | 1 | 2000 | 1000 | 10,223,36,12 | — | 步　速 | — |
| 0x0310 | 0 | 2167 | 1000 | 51,115,62,10 | I07 | — | — |
| 0x033C | 0 | 2168 | 1000 | 51,132,62,10 | I08 | — | — |
| 0x0368 | 0 | 2169 | 1000 | 51,149,62,10 | I09 | — | — |
| 0x0394 | 0 | 2170 | 1000 | 51,166,62,10 | I10 | — | — |
| 0x03C0 | 0 | 2171 | 1000 | 51,183,62,10 | I11 | — | — |
| 0x03EC | 0 | 2172 | 1000 | 51,200,62,10 | I12 | — | — |
| 0x0418 | 0 | 2173 | 1000 | 51,223,62,10 | I13 | — | — |
| 0x0444 | 2 | 2176 | 1000 | 500,1,16,16 | I23 | — | CLOSESCREEN.spr |
| 0x0484 | 0 | 2090 | 1000 | 51,94,33,12 | I24 | — | — |
| 0x04B0 | 3 | 2000 | 1000 | 31,238,0,0 | — | — | — |
| 0x04D9 | 3 | 2091 | 1000 | 30,280,92,10 | I04 | — | HP.spr |
| 0x0521 | 3 | 2092 | 1000 | 30,296,92,10 | I05 | — | MP.spr |
| 0x0569 | 3 | 2093 | 1000 | 30,312,92,10 | I06 | — | WP.spr |
| 0x05B1 | 2 | 2174 | 1000 | 179,35,84,15 | I21 | — | B_CHANGE_NAME.spr |
| 0x05F5 | 1 | 2000 | 1000 | 184,37,48,12 | — | 改變名稱 | — |
| 0x0623 | 0 | 2000 | 1000 | 76,297,0,0 | — | — | — |
| 0x064C | 0 | 2993 | 1000 | 51,240,62,10 | — | — | — |
| 0x0675 | 0 | 2994 | 1000 | 51,257,62,10 | — | — | — |
| 0x069E | 1 | 2000 | 1000 | 10,240,36,12 | — | 總攻擊 | — |
| 0x06CA | 1 | 2000 | 1000 | 10,257,36,12 | — | 總防禦 | — |
| 0x06F6 | 2 | 3116 | 1000 | 482,1,16,16 | OH01 | — | B_ONLINEHELP.spr |
| 0x0739 | 3 | 2995 | 1000 | 139,96,121,10 | — | — | EXP.spr |
| 0x0780 | 2 | 2702 | 1000 | 1,330,113,23 | — | — | B_DRIVER01.spr |
| 0x07BB | 2 | 2703 | 1000 | 1,360,113,23 | — | — | B_DRIVER02.spr |
| 0x07F6 | 1 | 2000 | 1000 | 8,366,48,12 | — | 機甲脫出 | — |
| 0x0824 | 1 | 2000 | 1000 | 9,336,24,12 | — | 駕駛 | — |
| 0x084E | 1 | 2000 | 1000 | 333,30,48,12 | — | 機甲裝備 | — |
| 0x087C | 2 | 2698 | 1000 | 495,190,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x08C1 | 2 | 2700 | 1000 | 495,265,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x090A | 0 | 2557 | 1000 | 165,308,18,18 | — | — | B_RADIO_B.spr |
| 0x0941 | 0 | 2655 | 1000 | 364,346,120,12 | — | — | — |
| 0x096A | 0 | 2656 | 1000 | 364,373,120,12 | — | — | — |
| 0x0993 | 5 | 3222 | 1000 | 136,303,90,27 | — | — | — |
| 0x09B0 | 5 | 3228 | 1000 | 333,52,155,26 | — | — | — |
| 0x09CD | 5 | 3237 | 1000 | 333,359,155,26 | — | — | — |
| 0x09EA | 4 | 2000 | 1000 | 549,179,0,0 | — | — | — |
| 0x0A07 | 1 | 2000 | 1000 | 200,312,24,12 | — | 主體 | — |
| 0x0A31 | 1 | 2000 | 1000 | 199,339,24,12 | — | 武裝 | — |
| 0x0A5B | 1 | 2000 | 1000 | 199,366,24,12 | — | 引擎 | — |
| 0x0A85 | 1 | 2000 | 1000 | 262,312,36,12 | — | 強化卡 | — |
| 0x0AB1 | 1 | 2000 | 1000 | 333,310,48,12 | — | 機甲能源 | — |
| 0x0ADF | 2 | 2699 | 1000 | 495,362,17,25 | — | — | SCROLLDOWN_S.spr |
| 0x0B1E | 2 | 2697 | 1000 | 495,330,17,25 | — | — | SCROLLUP_S.spr |
| 0x0B59 | 0 | 2125 | 1000 | 364,332,40,13 | — | — | — |
| 0x0B82 | 0 | 2126 | 1000 | 364,359,40,13 | — | — | — |
| 0x0BAB | 2 | 3216 | 1000 | 334,333,24,24 | — | — | Icon.spr |
| 0x0BDA | 2 | 3217 | 1000 | 333,360,24,24 | — | — | Icon.spr |
| 0x0C09 | 2 | 2413 | 1000 | 495,50,17,36 | — | — | SCROLLUP_BUTTON.spr |
| 0x0C4E | 2 | 2009 | 1000 | 495,125,17,36 | — | — | SCROLLDOWN_BUTTON.spr |
| 0x0C97 | 1 | 2000 | 1000 | 333,170,36,12 | — | 強化卡 | — |
| 0x0CC3 | 0 | 2558 | 1000 | 165,335,18,18 | — | — | B_RADIO_B.spr |
| 0x0CFA | 0 | 2559 | 1000 | 165,362,18,18 | — | — | B_RADIO_B.spr |
| 0x0D31 | 5 | 3223 | 1000 | 135,329,90,27 | — | — | — |
| 0x0D4E | 5 | 3224 | 1000 | 135,357,90,27 | — | — | — |
| 0x0D6B | 5 | 3225 | 1000 | 228,303,70,27 | — | — | — |
| 0x0D88 | 5 | 3226 | 1000 | 228,331,70,27 | — | — | — |
| 0x0DA5 | 5 | 3227 | 1000 | 228,359,70,27 | — | — | — |
| 0x0DC2 | 2 | 3202 | 1000 | 136,306,24,24 | — | — | Icon.spr |
| 0x0DF1 | 2 | 3203 | 1000 | 137,333,24,24 | — | — | Icon.spr |
| 0x0E20 | 2 | 3204 | 1000 | 137,360,24,24 | — | — | Icon.spr |
| 0x0E4F | 2 | 3205 | 1000 | 231,306,24,24 | — | — | Icon.spr |
| 0x0E7E | 2 | 3206 | 1000 | 231,333,24,24 | — | — | Icon.spr |
| 0x0EAD | 2 | 3207 | 1000 | 231,361,24,24 | — | — | Icon.spr |
| 0x0EDC | 0 | 2647 | 1000 | 363,66,120,12 | — | — | — |
| 0x0F05 | 2 | 3208 | 1000 | 334,53,24,24 | — | — | Icon.spr |
| 0x0F34 | 0 | 2648 | 1000 | 363,93,120,12 | — | — | — |
| 0x0F5D | 2 | 3209 | 1000 | 334,80,24,24 | — | — | Icon.spr |
| 0x0F8C | 0 | 2649 | 1000 | 363,120,120,12 | — | — | — |
| 0x0FB5 | 0 | 2650 | 1000 | 363,147,120,12 | — | — | — |
| 0x0FDE | 2 | 3210 | 1000 | 334,108,24,24 | — | — | Icon.spr |
| 0x100D | 2 | 3211 | 1000 | 334,134,24,24 | — | — | Icon.spr |
| 0x103C | 0 | 2651 | 1000 | 363,206,103,12 | — | — | — |
| 0x1065 | 0 | 2652 | 1000 | 363,233,103,12 | — | — | — |
| 0x108E | 0 | 2653 | 1000 | 363,260,103,12 | — | — | — |
| 0x10B7 | 0 | 2654 | 1000 | 363,287,103,12 | — | — | — |
| 0x10E0 | 2 | 3212 | 1000 | 334,193,24,24 | — | — | Icon.spr |
| 0x110F | 2 | 3213 | 1000 | 334,220,24,24 | — | — | Icon.spr |
| 0x113E | 2 | 3214 | 1000 | 334,248,24,24 | — | — | Icon.spr |
| 0x116D | 2 | 3215 | 1000 | 334,274,24,24 | — | — | Icon.spr |
| 0x119C | 5 | 3229 | 1000 | 333,79,155,26 | — | — | — |
| 0x11B9 | 5 | 3230 | 1000 | 333,106,155,26 | — | — | — |
| 0x11D6 | 5 | 3231 | 1000 | 333,133,155,26 | — | — | — |
| 0x11F3 | 5 | 3232 | 1000 | 333,192,137,26 | — | — | — |
| 0x1210 | 5 | 3233 | 1000 | 333,219,137,26 | — | — | — |
| 0x122D | 5 | 3234 | 1000 | 333,246,137,26 | — | — | — |
| 0x124A | 5 | 3235 | 1000 | 333,273,137,26 | — | — | — |
| 0x1267 | 5 | 3236 | 1000 | 333,332,155,26 | — | — | — |
| 0x1284 | 1 | 2000 | 1000 | 262,340,36,12 | — | 強化卡 | — |
| 0x12B0 | 1 | 2000 | 1000 | 262,366,36,12 | — | 強化卡 | — |
| 0x12DC | 2 | 2059 | 1000 | 299,308,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x132B | 2 | 2060 | 1000 | 299,335,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x137A | 2 | 2061 | 1000 | 299,362,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x13C9 | 2 | 2065 | 1000 | 472,198,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x1418 | 2 | 2067 | 1000 | 472,252,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x1467 | 2 | 2068 | 1000 | 472,279,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x14B6 | 2 | 2066 | 1000 | 472,225,19,19 | DB01 | — | DRIVER_SHOW_BUTTON.spr |
| 0x1505 | 2 | 3218 | 1000 | 231,66,24,24 | — | — | Icon.spr |
| 0x1534 | 1 | 2000 | 1000 | 280,72,36,12 | — | 守護石 | — |
| 0x1560 | 5 | 3238 | 1000 | 229,64,90,27 | — | — | — |
| 0x157D | 0 | 2560 | 1000 | 259,68,18,18 | — | — | B_RADIO_B.spr |
| 0x15B4 | 1 | 2000 | 1000 | 10,94,36,12 | — | 等　級 | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| B_CHANGE_NAME.spr | 4 | 84×15 @(0,0) ×4 幀 |
| B_DRIVER01.spr | 4 | 113×23 @(0,0) ×4 幀 |
| B_DRIVER02.spr | 4 | 113×23 @(0,0) ×4 幀 |
| B_ONLINEHELP.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| B_RADIO_B.spr | 2 | 19×19 @(0,0) ×2 幀 |
| BG_DRIVER.spr | 1 | 523×393 @(0,0) ×1 幀 |
| CLOSESCREEN.spr | 4 | 16×16 @(0,0) ×3 幀；17×17 @(0,0) ×1 幀 |
| DRIVER_SHOW_BUTTON.spr | 4 | 19×19 @(0,0) ×4 幀 |
| EXP.spr | 16 | 225×12 @(0,0) ×16 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
| Icon.spr | 500 | 24×24 @(0,0) ×500 幀 |
| MP.spr | 16 | 109×10 @(0,0) ×16 幀 |
| SCROLLDOWN_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLDOWN_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| SCROLLUP_BUTTON.spr | 4 | 17×36 @(0,0) ×4 幀 |
| SCROLLUP_S.spr | 4 | 17×25 @(0,0) ×4 幀 |
| WP.spr | 18 | 141×10 @(0,0) ×18 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CRidingInfoItf::MessageMap — L249449 @0x004976EC；直接具名呼叫：CRidingInfoItf::OnRename, CServiceItf::MessageMap
- CRidingInfoItf::UpdateFigure — L250281 @0x00498083；直接具名呼叫：CSGOCore::GetItemByID, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCSGOChar::?C?$CELPtr, CELOnlineObj::SetData, CSGOChar::InitMapRiding, CSGOChar::GetRiding, CMapRiding::SetName, CMapRiding::SetAttackType
- CRidingInfoItf::Create — L250605 @0x004983DC；直接具名呼叫：CSGOInterface::Create
- CRidingInfoItf::SetRiding — L250636 @0x00498410；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRidingInfoItf::OnUpdate — L251308 @0x00498B27；直接具名呼叫：CServiceItf::OnUpdate, VCEditBox::?9?$CELPtr, VCEditBox::?C?$CELPtr, CSGOInterface::GetData, CRidingInfoItf::DoRename, VCSGOChar::?9?$CELPtr, VCSGOChar::?C?$CELPtr
- CRidingInfoItf::UpdateButton — L251401 @0x00498C09；直接具名呼叫：VCSGOUserButton::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOUserButton::?C?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOChar::GetCharSMode, CSGOChar::GetRideUID, VCSGOUserButton::?1?$CELPtr
- CRidingInfoItf::OnAddBattery — L251617 @0x00498E24；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingBatteryBagList, PAVRidingBatterySyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::SendPacket
- CRidingInfoItf::UpdateInfo — L251708 @0x00498EFB；直接具名呼叫：VCELProgressBar::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, CSGOLibrary::GetRideLvlUpData, UPLAYERLVLUP_::?A?$CELArray, VCELProgressBar::?C?$CELPtr, VCELProgressBar::?4?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELOnlineObj::GetData, VCELProgressBar::?1?$CELPtr
- CRidingInfoItf::OnAddBatteryBut — L252052 @0x0049928C；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingBatteryBagList, PAVRidingBatterySyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::SendPacket
- CRidingInfoItf::OnEquipBut — L252143 @0x00499363；直接具名呼叫：PAVRidingEquipSyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::AddSystemMsg, CSGOUim::SendPacket
- CRidingInfoItf::OnEquip — L252243 @0x00499466；直接具名呼叫：PAVRidingEquipSyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::AddSystemMsg, CSGOUim::SendPacket
- CRidingInfoItf::OnSummon — L252343 @0x00499569；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOChar::GetCharSMode, CSGOUim::AddSystemMsg, CSGOChar::GetRideUID, CSGOChar::GetAction, CSGOPlayer::SendPacket, CSGOUim::SendPacket
- CRidingInfoItf::OnUnSummon — L252439 @0x00499678；直接具名呼叫：CSGOUim::SendPacket
- CRidingInfoItf::OnDataModified — L252477 @0x004996BA；直接具名呼叫：VCSGOMessageArea::?0?$CELPtr, VCELHelpBound::?0?$CELPtr, VCSGOUserButton::?0?$CELPtr, VCSGOUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOUserButton::?9?$CELPtr, VCSGOUserButton::?C?$CELPtr, VCSGOMessageArea::?9?$CELPtr, VCSGOMessageArea::?C?$CELPtr, VCSGOMessageArea::?4?$CELPtr, VCSGOPlayer::?9?$CELPtr, VCELProgressBar::?0?$CELPtr
- CRidingInfoItf::UpdateExtraData — L255347 @0x0049BC57；直接具名呼叫：VCSGOUserButton::?0?$CELPtr, VCELHelpBound::?0?$CELPtr, VCSGOUserButton::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCSGOUserButton::?9?$CELPtr, VCSGOUserButton::?C?$CELPtr, VCELHelpBound::?4?$CELPtr, VCELHelpBound::?1?$CELPtr, VCELHelpBound::?9?$CELPtr, CServiceItf::GenHelpContent, VCELHelpBound::?C?$CELPtr, VCELItfComp::?0?$CELPtr
- CRidingInfoItf::OnScrollUp — L255943 @0x0049C23A；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRidingInfoItf::OnScrollDown — L256033 @0x0049C31A；直接具名呼叫：VCSGOPlayer::?C?$CELPtr, CSGOPlayer::GetRidingBatteryBagList
- CRidingInfoItf::OnRename — L256139 @0x0049C43F；直接具名呼叫：CSGOUim::PlaySoundA, VCELInterface::?0?$CELPtr, VCOKBox::?C?$CELPtr, CSGOInterface::CenterScreen, VCELInterface::?1?$CELPtr, VCEditBox::?4?$CELPtr, VCEditBox::?C?$CELPtr, CEditBox::Create
- CRidingInfoItf::OnSortMode — L256416 @0x0049C70F；直接具名呼叫：VCELMessageArea::?0?$CELPtr, VCELItfComp::?1?$CELPtr, VCELMessageArea::?C?$CELPtr, VCELMessageArea::?4?$CELPtr, VCELMessageArea::?1?$CELPtr
- CRidingInfoItf::OnEquipCard — L256739 @0x0049CA53；直接具名呼叫：PAVRidingEquipSyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::AddSystemMsg, CSGOUim::SendPacket
- CRidingInfoItf::OnEquipCardBut — L256865 @0x0049CB92；直接具名呼叫：PAVRidingEquipSyncData::?A?$CELArray, CServiceItf::CanRideItemUse, CSGOUim::AddSystemMsg, CSGOUim::SendPacket
- CRidingInfoItf::Draw — L256983 @0x0049CCB4；直接具名呼叫：CSGOInterface::Draw, CSGOMap::IsMapNotReady, VCSGOChar::?9?$CELPtr, CELMap2D::MapPosY, CELMap2D::MapPosX, VCSGOChar::?C?$CELPtr, CSGOChar::SetMapPosition, CSGOChar::DrawAlpha
- CRidingInfoItf::OnCardShow — L257057 @0x0049CD74；直接具名呼叫：VCRidingCardItf::?0?$CELPtr, VCRidingCardItf::?C?$CELPtr, CRidingCardItf::SetCardInfo, CSGOInterface::CenterScreen, VCELInterface::?0?$CELPtr, VCRidingCardItf::?1?$CELPtr
- CRidingInfoItf::OnCard1Show — L257176 @0x0049CE98；直接具名呼叫：PAVRidingEquipSyncData::?A?$CELArray, VCRidingCardItf::?0?$CELPtr, VCRidingCardItf::?C?$CELPtr, CRidingCardItf::SetCardInfo, CSGOInterface::CenterScreen, VCELInterface::?0?$CELPtr, VCRidingCardItf::?1?$CELPtr
- CRidingInfoItf::OnUnEquipCardBut — L257317 @0x0049CFFD；直接具名呼叫：CSGOUim::SendPacket
- CRidingInfoItf::OnUnequipCrystal — L257372 @0x0049D071；直接具名呼叫：CSGOUim::SendPacket

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0028 / ID 2001（type 0，50,37,118,12，I01）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0054 / ID 2154（type 0，50,53,118,12，I02）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0080 / ID 2155（type 0，50,69,118,12，I03）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00AC / ID 2000（type 1，9,37,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00D8 / ID 2000（type 1，9,53,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0104 / ID 2000（type 1，9,69,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0130 / ID 2000（type 1，5,280,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x015A / ID 2000（type 1，4,296,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0184 / ID 2000（type 1，4,312,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01AE / ID 2000（type 1，136,111,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x01DC / ID 2000（type 1，10,115,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0208 / ID 2000（type 1，10,132,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0234 / ID 2000（type 1，10,149,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0260 / ID 2000（type 1，10,166,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x028C / ID 2000（type 1，10,183,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02B8 / ID 2000（type 1，10,200,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x02E4 / ID 2000（type 1，10,223,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0310 / ID 2167（type 0，51,115,62,10，I07）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x033C / ID 2168（type 0，51,132,62,10，I08）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0368 / ID 2169（type 0，51,149,62,10，I09）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0394 / ID 2170（type 0，51,166,62,10，I10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03C0 / ID 2171（type 0，51,183,62,10，I11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x03EC / ID 2172（type 0，51,200,62,10，I12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0418 / ID 2173（type 0，51,223,62,10，I13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0444 / ID 2176（type 2，500,1,16,16，I23，CLOSESCREEN.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0484 / ID 2090（type 0，51,94,33,12，I24）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04B0 / ID 2000（type 3，31,238,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x04D9 / ID 2091（type 3，30,280,92,10，I04，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0521 / ID 2092（type 3，30,296,92,10，I05，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0569 / ID 2093（type 3，30,312,92,10，I06，WP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05B1 / ID 2174（type 2，179,35,84,15，I21，B_CHANGE_NAME.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x05F5 / ID 2000（type 1，184,37,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0623 / ID 2000（type 0，76,297,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x064C / ID 2993（type 0，51,240,62,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0675 / ID 2994（type 0，51,257,62,10）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x069E / ID 2000（type 1，10,240,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06CA / ID 2000（type 1，10,257,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x06F6 / ID 3116（type 2，482,1,16,16，OH01，B_ONLINEHELP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0739 / ID 2995（type 3，139,96,121,10，EXP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0780 / ID 2702（type 2，1,330,113,23，B_DRIVER01.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07BB / ID 2703（type 2，1,360,113,23，B_DRIVER02.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x07F6 / ID 2000（type 1，8,366,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0824 / ID 2000（type 1，9,336,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x084E / ID 2000（type 1，333,30,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x087C / ID 2698（type 2，495,190,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x08C1 / ID 2700（type 2，495,265,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x090A / ID 2557（type 0，165,308,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0941 / ID 2655（type 0，364,346,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x096A / ID 2656（type 0，364,373,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0993 / ID 3222（type 5，136,303,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09B0 / ID 3228（type 5，333,52,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09CD / ID 3237（type 5，333,359,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x09EA / ID 2000（type 4，549,179,0,0）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A07 / ID 2000（type 1，200,312,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A31 / ID 2000（type 1，199,339,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A5B / ID 2000（type 1，199,366,24,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0A85 / ID 2000（type 1，262,312,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0AB1 / ID 2000（type 1，333,310,48,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0ADF / ID 2699（type 2，495,362,17,25，SCROLLDOWN_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B1E / ID 2697（type 2，495,330,17,25，SCROLLUP_S.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B59 / ID 2125（type 0，364,332,40,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0B82 / ID 2126（type 0，364,359,40,13）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BAB / ID 3216（type 2，334,333,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0BDA / ID 3217（type 2，333,360,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C09 / ID 2413（type 2，495,50,17,36，SCROLLUP_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C4E / ID 2009（type 2，495,125,17,36，SCROLLDOWN_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0C97 / ID 2000（type 1，333,170,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CC3 / ID 2558（type 0，165,335,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0CFA / ID 2559（type 0，165,362,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D31 / ID 3223（type 5，135,329,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D4E / ID 3224（type 5，135,357,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D6B / ID 3225（type 5，228,303,70,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0D88 / ID 3226（type 5，228,331,70,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0DA5 / ID 3227（type 5，228,359,70,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0DC2 / ID 3202（type 2，136,306,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0DF1 / ID 3203（type 2，137,333,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E20 / ID 3204（type 2，137,360,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E4F / ID 3205（type 2，231,306,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0E7E / ID 3206（type 2，231,333,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0EAD / ID 3207（type 2，231,361,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0EDC / ID 2647（type 0，363,66,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0F05 / ID 3208（type 2，334,53,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0F34 / ID 2648（type 0，363,93,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0F5D / ID 3209（type 2，334,80,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0F8C / ID 2649（type 0，363,120,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0FB5 / ID 2650（type 0，363,147,120,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0FDE / ID 3210（type 2，334,108,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x100D / ID 3211（type 2，334,134,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x103C / ID 2651（type 0，363,206,103,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1065 / ID 2652（type 0，363,233,103,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x108E / ID 2653（type 0，363,260,103,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x10B7 / ID 2654（type 0，363,287,103,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x10E0 / ID 3212（type 2，334,193,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x110F / ID 3213（type 2，334,220,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x113E / ID 3214（type 2，334,248,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x116D / ID 3215（type 2，334,274,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x119C / ID 3229（type 5，333,79,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x11B9 / ID 3230（type 5，333,106,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x11D6 / ID 3231（type 5，333,133,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x11F3 / ID 3232（type 5，333,192,137,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1210 / ID 3233（type 5，333,219,137,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x122D / ID 3234（type 5，333,246,137,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x124A / ID 3235（type 5，333,273,137,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1267 / ID 3236（type 5，333,332,155,26）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1284 / ID 2000（type 1，262,340,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x12B0 / ID 2000（type 1，262,366,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x12DC / ID 2059（type 2，299,308,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x132B / ID 2060（type 2，299,335,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x137A / ID 2061（type 2，299,362,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x13C9 / ID 2065（type 2，472,198,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1418 / ID 2067（type 2，472,252,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1467 / ID 2068（type 2，472,279,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x14B6 / ID 2066（type 2，472,225,19,19，DB01，DRIVER_SHOW_BUTTON.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1505 / ID 3218（type 2，231,66,24,24，Icon.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1534 / ID 2000（type 1，280,72,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x1560 / ID 3238（type 5，229,64,90,27）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x157D / ID 2560（type 0，259,68,18,18，B_RADIO_B.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x15B4 / ID 2000（type 1，10,94,36,12）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：搭乘、卸下、能量、裝備屬性與重登一致。
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
