# UI-009_STATUS — 狀態效果列

- 優先度：P1（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old 直接引用。原版 ITF：\`HK/output/Interface/009_STATUS.itf\`（475 bytes）。
- 目標：Buff/Debuff icon、剩餘時間、移除、提示。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L134965 @0x00451622（ITF 引用）；鄰近 Create 符號 \`CStatusIconBarItf::Create\` L134955 @0x00451617，需確認函式邊界
- HK .text 未定位直接字串；需追 ITF 共用載入器、動態名稱或版本可達性。
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`EFFECT_ICON.spr\` → HK/output/Interface/EFFECT_ICON.spr
- \`STATUS.spr\` → HK/output/Interface/STATUS.spr

### 直接載入／動態素材線索
- 此任務暫無已定位的額外動態 SPR；需檢查 Draw/資源載入器，不能假設 ITF 已包含全部。

## 3. Server / Data 對照
- Old/服務端/plugins/SGOStatus.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOStatusMgr::SetStatus\` L16318 @0x1000AF06
  - \`SGOStatusMgr::GetStatus\` L19940 @0x1000D341
  - \`SGOStatusMgr::SendPlayerStatus\` L36098 @0x100176E4
  - \`CDMapEntity::SetStatus\` L112395 @0x10045045
  - \`CDMapEntity::GetStatus\` L113200 @0x1004583C
  - \`CDEntity::GetScriptBuffer\` L130096 @0x1004FBF6
- Old/服務端/plugins/SGOSkillSys.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOSkill::GetSkillPlayer\` L10206 @0x100074FC
  - \`CSkill::GetSkillCount\` L12480 @0x10008AB0
  - \`SGOSkillSys::LoadSkillData\` L25992 @0x10011017
  - \`SGOSkillSys::CreateSkill\` L29831 @0x10013805
  - \`SGOSkillSys::FindSkillData\` L30723 @0x100140AF
  - \`SGOSkillSys::GetSkillHitCount\` L49959 @0x10020580
  - \`SGOSkillSys::SetSkillTimer\` L60793 @0x10027539
  - \`CDSkillSys::GetCurrentSkillInfo\` L61091 @0x100276F0
- 相關資料：status.cfg；skill.cfg
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：Buff/Debuff icon、剩餘時間、移除、提示；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：增加、更新、到期、死亡清除／重登規則一致。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
