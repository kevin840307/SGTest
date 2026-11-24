# UI-018_EMOTION_BIG — 表情選單

- 優先度：P2（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/018_EMOTION_BIG.itf\`（912 bytes）。
- 目標：表情分頁、選擇與顯示事件。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L135537 @0x00451A51（ITF 引用）；鄰近 Create 符號 \`CEmotionMenuItf::Create\` L135509 @0x00451A2F，需確認函式邊界
- HK/SGOClient.exe.lst L132584 @0x00451219（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`BG_EMOTION.spr\` → HK/output/Interface/BG_EMOTION.spr
- \`B_BACK.spr\` → HK/output/Interface/B_BACK.spr
- \`B_NEXT.spr\` → HK/output/Interface/B_NEXT.spr
- \`CLOSESCREEN.spr\` → HK/output/Interface/CLOSESCREEN.spr
- \`EMOTIONAL_ICON.spr\` → HK/output/Interface/EMOTIONAL_ICON.spr

### 直接載入／動態素材線索
- Interface/EMOTIONAL_ICON.spr (L36500, 0041659F)

## 3. Server / Data 對照
- Old/服務端/plugins/CDEmotion.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDEmotion::OnSetEmotion\` L2501 @0x1000253E
  - \`CDEmotion::OnSetPKEmotion\` L3860 @0x100032DA
  - \`CDEmotion::SendEmotionPacket\` L5203 @0x10004057
  - \`CDEmotion::SendPKEmotionPacket\` L6071 @0x10004900
  - \`CDEmotion::GetEmotion\` L7803 @0x100059FF
- 相關資料：chat.cfg
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：表情分頁、選擇與顯示事件；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：合法表情發送、其他玩家可見、重複與冷卻規則核對。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
