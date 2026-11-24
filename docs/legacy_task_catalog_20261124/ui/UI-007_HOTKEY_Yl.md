# UI-007_HOTKEY_Yl — 快捷鍵列（007_HOTKEY_Yl）

- 優先度：P1（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/007_HOTKEY_Yl.itf\`（2519 bytes）。
- 目標：物品與技能欄位、拖曳、翻頁/展開縮合、快捷使用。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L126826 @0x0044CFC7（ITF 引用）；鄰近 Create 符號 \`CHotKeyItf::Create\` L126803 @0x0044CF9E，需確認函式邊界
- HK/SGOClient.exe.lst L125757 @0x0044D0E8（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`HOTKEY_LY.spr\` → HK/output/Interface/HOTKEY_LY.spr
- \`HOTKEY_YS_B.spr\` → HK/output/Interface/HOTKEY_YS_B.spr
- \`OFFON.spr\` → HK/output/Interface/OFFON.spr

### 直接載入／動態素材線索
- HOTKEY_LY.spr (L123639, 0044AF00)
- HOTKEY_SY.spr (L123671, 0044AF63)
- HOTKEY_YL_B.spr (L123834, 0044B0FB)
- HOTKEY_YS_B.spr (L123848, 0044B123)
- Interface/SKILL_ICON.spr (L125466, 0044C206)
- Interface/EMOTIONAL_ICON.spr (L125519, 0044C2FE)

## 3. Server / Data 對照
- Old/服務端/plugins/CDHotKey.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
- Old/服務端/plugins/SGOSkillSys.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOSkill::GetSkillPlayer\` L10206 @0x100074FC
  - \`CSkill::GetSkillCount\` L12480 @0x10008AB0
  - \`SGOSkillSys::LoadSkillData\` L25992 @0x10011017
  - \`SGOSkillSys::CreateSkill\` L29831 @0x10013805
  - \`SGOSkillSys::FindSkillData\` L30723 @0x100140AF
  - \`SGOSkillSys::GetSkillHitCount\` L49959 @0x10020580
  - \`SGOSkillSys::SetSkillTimer\` L60793 @0x10027539
  - \`CDSkillSys::GetCurrentSkillInfo\` L61091 @0x100276F0
- Old/服務端/plugins/SGOGeneralItem.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOCountItemMgr::CreateItem\` L29206 @0x1001334A
  - \`SGOCountItemMgr::SavePlayerItemList\` L30180 @0x10013CB5
  - \`SGOCountItemMgr::GetFullPlayerSaveItemList\` L31509 @0x10014A56
  - \`SGOCountItemMgr::AddItem\` L36997 @0x100181F5
  - \`SGOCountItemMgr::AddItemTest\` L39060 @0x10019679
  - \`SGOCountItemMgr::RemoveItem\` L39977 @0x10019FC7
  - \`SGOCountItemMgr::RemoveItemByIndex\` L41868 @0x1001B344
  - \`SGOCountItemMgr::OnUseItem\` L51513 @0x10021563
- 相關資料：ClientData/Interface/skill.dat
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：物品與技能欄位、拖曳、翻頁/展開縮合、快捷使用；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：拖入/移除後狀態同步；按鍵消耗正確；冷卻及重登保存。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
