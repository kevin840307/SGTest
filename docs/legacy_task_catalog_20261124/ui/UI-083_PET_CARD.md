# UI-083_PET_CARD — 機甲/寵物卡片預覽

- 優先度：P2（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/083_PET_CARD.itf\`（120 bytes）。
- 目標：卡片與對應實體圖示；以 handler 確認卡片類型及使用入口。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L258108 @0x0049D54A（ITF 引用）；鄰近 Create 符號 \`CRidingCardItf::Create\` L258097 @0x0049D53D，需確認函式邊界
- HK/SGOClient.exe.lst L304308 @0x004BE669（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`CARD_004.spr\` → HK/output/Interface/CARD_004.spr

### 直接載入／動態素材線索
- MS/PetCard/CARD_%03d.spr (L258231, 0049D639)
- MS/PetCard/PET_%03d.spr (L258321, 0049D799)

## 3. Server / Data 對照
- Old/服務端/plugins/SGOPetSys.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CSGOPet::GetPetObj\` L12428 @0x10008B67
  - \`CSGOPet::SetPetObj\` L13188 @0x100092BA
  - \`CSGOPet::GetPetData\` L14002 @0x10009ABB
  - \`CSGOPet::SetPetData\` L14760 @0x1000A20E
  - \`CSGOPetObj::SetPet\` L68601 @0x1002B645
  - \`CSGOPetObj::UpdatePet\` L69562 @0x1002BFCD
  - \`CSGOPetObj::GetPet\` L70435 @0x1002C86A
  - \`CSGOPetObj::SetPetSys\` L75143 @0x1002F7E5
- Old/服務端/plugins/SGORobotComplex.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
- 相關資料：Dat/Complex/RobotComplexCard.csv
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：卡片與對應實體圖示；以 handler 確認卡片類型及使用入口；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：正確卡片 ID、不同卡片及取消；不可錯用/重複使用。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
