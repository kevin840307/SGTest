# UI-088_COMPLEX_HP — 合體 HP/MP/EXP 條（獨立執行規格）

狀態：NOT VERIFIED；建議優先度：P2。這是可直接指派的實作任務，不代表 SGTest 現況缺少全部功能。
正式工作目錄：C:/SGO/SRC/SGTest；main。先遵守 [AGENTS.md](../../../../AGENTS.md)，讀 [既有任務證據](../../ui/UI-088_COMPLEX_HP.md) 及相關 TODO/FEATURE_MATRIX。只改本任務的最小垂直切片。


## 玩家可見目標
合體實體 HP、MP、EXP 及可見性。

驗收情境：戰鬥、解體、升級、斷線正確。

## 原版 UI 控制規格
來源：HK/output/Interface/088_COMPLEX_HP.itf；C.D.E. 解包：非封裝或未解；解析狀態：COUNT_MATCH；宣告控制項 4，取得 4。

背景資源：BG_COMPLEX_HP.spr, BG_COMPLEX_HP.spr；header raw fields=1000/1000。
控制項為二進位結構掃描結果；COUNT_MATCH 表示數量及首偏移吻合，不等同每種 type 語意已驗證。位置為 ITF local rect（非螢幕全域）；同 ID 可能是多個不同文字/控件，不得以 ID 去重。SPR 欄為此候選記錄內的字串，未指定幀/normal/hover 的順序須由 HK loader 或實機查證。
原始 group 欄位照錄，語意待驗；負數尺寸不自動轉正，0xCDCDCDCD 是原檔未初始化樣式的 sentinel 候選，不是像素大小，須使用原版 loader/SPR 解析結果。
| offset | type | control ID | group raw | x,y,w,h | tag | 固定文字候選 | SPR |
|---:|---:|---:|---:|---|---|---|---|
| 0x0030 | 3 | 2092 | 1000 | 53,7,20,2 | — | — | MP.spr |
| 0x0075 | 3 | 2091 | 1000 | 8,7,42,2 | — | — | HP.spr |
| 0x00BA | 3 | 2853 | 1000 | 8,11,64,2 | — | — | EXP.spr |
| 0x0101 | 0 | 2283 | 1000 | 75,7,28,11 | — | — | — |

### SPR 實際幀數與原始幾何（SPR header/table）
| SPR | 幀數 | 原始寬高及 origin（括號內） |
|---|---:|---|
| BG_COMPLEX_HP.spr | 1 | 107×24 @(0,0) ×1 幀 |
| EXP.spr | 16 | 225×12 @(0,0) ×16 幀 |
| HP.spr | 16 | 107×10 @(0,0) ×16 幀 |
| MP.spr | 16 | 109×10 @(0,0) ×16 幀 |
以上為素材 header 幾何，不代表 UI 拉伸方式、狀態對應 frame 或 alpha/pivot 已驗；依正式 converter 與原版 draw/實機確認。
字型、動態文字 baseline/顏色、截斷與實際 frame-state：尚無本畫面完整證據，逐控件按 HK Draw 路徑及同狀態 A/B 補驗。

## 原版函式與事件追蹤
以下為 Old .lst 鄰近 Create 類別的具名函式及靜態直接呼叫摘要（不是 HK 相同行為的證明）；控件→handler 的實際綁定仍須追 MessageMap。
- CRobotComplexTPItf::Create — L262985 @0x004A0881；直接具名呼叫：CSGOInterface::Create
- CRobotComplexTPItf::OnDataModified — L263304 @0x004A0B62；直接具名呼叫：未擷取（可能間接呼叫或無具名呼叫）
- CRobotComplexTPItf::OnUpdate — L263327 @0x004A0B7B；直接具名呼叫：CSGOUim::GetPlayer, VCSGOPlayer::?8?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CELMapObj::GetWorldPosition, CSGOChar::GetNameStrlen, CELMap2D::MapPosX, CELMap2D::MapPosY
- CRobotComplexTPItf::UpdateInfo — L263426 @0x004A0C67；直接具名呼叫：VCELProgressBar::?0?$CELPtr, CSGOUim::GetPlayer, VCSGOPlayer::?8?$CELPtr, VCSGOPlayer::?1?$CELPtr, VCELProgressBar::?1?$CELPtr, VCSGOPlayer::?C?$CELPtr, CSGOChar::GetRobot, CRobotComplex::GetRobotDataInfo, VCELProgressBar::?4?$CELPtr, VCELItfComp::?1?$CELPtr, VCELProgressBar::?9?$CELPtr, VCELProgressBar::?C?$CELPtr

## 本任務執行順序
- [ ] 打開對應原版畫面，截圖記錄入口、初始狀態、所有頁籤與退出；依上表逐控件校正 local rect、原 SPR、frame/state、字型與 baseline。
- [ ] 控件 0x0030 / ID 2092（type 3，53,7,20,2，MP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0075 / ID 2091（type 3，8,7,42,2，HP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x00BA / ID 2853（type 3，8,11,64,2，EXP.spr）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 控件 0x0101 / ID 2283（type 0，75,7,28,11）：確認顯示/輸入角色、對應 handler 或資料來源、狀態切換；若純裝飾證明不可點擊，若互動驗證命中及結果。
- [ ] 執行完整玩家流程：戰鬥、解體、升級、斷線正確。
- [ ] 實作/保留正式事件入口，不以假資料或整張截圖替代；取消、重新開啟、長短字與錯誤狀態必須驗證。

## 玩法相依（按需執行，不重複實作）
- [GP-058 機甲卡片與合體](../gameplay/GP-058.md)

## 交付與關閉（逐項勾選）
- [ ] 對照目前正式實作；列出既有功能、缺口及本輪變更的實際檔案/測試。
- [ ] 完成本任務所有適用操作、錯誤/取消與重開；涉及權威、持久化、多人須驗正式 Server、重登、雙可見 Client。
- [ ] 視覺變更取得同狀態 HK Reference / Before / After 800×600、同 DPI、ROI overlay/diff；確認原圖、文字 baseline/字型與真實 hitbox。未知或未取得的原版證據不得標 COMPLETE。
- [ ] 更新既有 TODO / FEATURE_MATRIX；記錄實機證據、尚未驗項、依賴與 commit；遵守 Git 契約，僅提交本輪改動。若已完整符合，提供現有實機證據，不為修改而修改。
