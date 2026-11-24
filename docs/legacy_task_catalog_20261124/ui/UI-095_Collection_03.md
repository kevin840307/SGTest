# UI-095_Collection_03 — 收藏圖鑑子頁 03

- 優先度：P3（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：僅 ITF 存在；可達性待證。原版 ITF：\`HK/output/Interface/095_Collection_03.itf\`（2173 bytes）。
- 目標：依主畫面或六個子頁分類顯示收藏條件、完成度及可領取項目。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- 本次 Old 靜態掃描未定位直接 ITF 字串；不可據此判定無功能。
- HK .text 未定位直接字串；需追 ITF 共用載入器、動態名稱或版本可達性。
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`BG_COLLECTION_03.spr\` → HK/output/Interface/BG_COLLECTION_03.spr
- \`B_COLLECTION_ICON.spr\` → HK/output/Interface/B_COLLECTION_ICON.spr
- \`B_ONLINEHELP.spr\` → HK/output/Interface/B_ONLINEHELP.spr
- \`CLOSESCREEN.spr\` → HK/output/Interface/CLOSESCREEN.spr
- \`SCROLLDOWN_S.spr\` → HK/output/Interface/SCROLLDOWN_S.spr
- \`SCROLLUP_S.spr\` → HK/output/Interface/SCROLLUP_S.spr

### 直接載入／動態素材線索
- 此任務暫無已定位的額外動態 SPR；需檢查 Draw/資源載入器，不能假設 ITF 已包含全部。

## 3. Server / Data 對照
- Old/服務端/plugins/CDQuest.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
- Old/服務端/plugins/CDItem.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDItemSys::AddItem\` L3237 @0x10002ED8
  - \`CDItemSys::RemoveItem\` L4484 @0x10003B67
  - \`CDItemSys::AddItemTest\` L10832 @0x10007B48
  - \`CDItemSys::GetItem\` L13786 @0x100098CB
  - \`CDItemSys::GetItemByIndex\` L14648 @0x1000A143
  - \`CDItemSys::RemoveItemByIndex\` L18399 @0x1000C565
  - \`CDItemSys::CheckItemByIndex\` L19647 @0x1000D1F3
- 相關資料：HK Interface/Collection.itd；ItemCollectionData.dat；FlagCollectionData.dat
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：依主畫面或六個子頁分類顯示收藏條件、完成度及可領取項目；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：解鎖/未解鎖、重複收藏、獎勵只一次、重登；各頁不能混資料。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
