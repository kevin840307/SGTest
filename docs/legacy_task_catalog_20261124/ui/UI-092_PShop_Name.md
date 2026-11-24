# UI-092_PShop_Name — 擺攤命名

- 優先度：P2（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/092_PShop_Name.itf\`（396 bytes）。
- 目標：輸入店名、限制、確認與取消；依實際 handler 驗證。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L287339 @0x004AF233（ITF 引用）；鄰近 Create 符號 \`SellingNameItf::Create\` L287309 @0x004AF211，需確認函式邊界
- HK/SGOClient.exe.lst L317662 @0x004C69B0（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`BG_RANK_01.spr\` → HK/output/Interface/BG_RANK_01.spr
- \`B_ONLINEHELP.spr\` → HK/output/Interface/B_ONLINEHELP.spr
- \`CLOSESCREEN.spr\` → HK/output/Interface/CLOSESCREEN.spr
- \`YELLOW_BUTTON.spr\` → HK/output/Interface/YELLOW_BUTTON.spr

### 直接載入／動態素材線索
- 此任務暫無已定位的額外動態 SPR；需檢查 Draw/資源載入器，不能假設 ITF 已包含全部。

## 3. Server / Data 對照
- Old/服務端/plugins/SGOTrade.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOTradeAgent::ProcessTrade\` L18904 @0x1000CE12
  - \`SGOTradeSys::OnConfirm\` L31807 @0x10014B17
- Old/服務端/plugins/SGOShop.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDShop::OnBuy\` L12538 @0x1000895B
  - \`CDShop::OnSell\` L13446 @0x100092A0
  - \`CDShop::OnShopClose\` L14274 @0x10009AD9
  - \`CDShop::GetShoppingIndex\` L16904 @0x1000B5BE
  - \`CDShop::GetShopIndex\` L17723 @0x1000BDE7
- Old/服務端/plugins/CDItem.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDItemSys::AddItem\` L3237 @0x10002ED8
  - \`CDItemSys::RemoveItem\` L4484 @0x10003B67
  - \`CDItemSys::AddItemTest\` L10832 @0x10007B48
  - \`CDItemSys::GetItem\` L13786 @0x100098CB
  - \`CDItemSys::GetItemByIndex\` L14648 @0x1000A143
  - \`CDItemSys::RemoveItemByIndex\` L18399 @0x1000C565
  - \`CDItemSys::CheckItemByIndex\` L19647 @0x1000D1F3
- 相關資料：SellingName.cfg；資料庫/game.sql
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：輸入店名、限制、確認與取消；依實際 handler 驗證；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：雙 Client 開攤、瀏覽、買賣、取消；最後一件競購只成功一次。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
