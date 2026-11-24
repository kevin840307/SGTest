# UI-027_Skill — 技能視窗

- 優先度：P1（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/027_Skill.itf\`（2306 bytes）。
- 目標：技能清單、圖示、等級、技能熟練度/學習與快捷列入口。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L178384 @0x0046AE5C（ITF 引用）；鄰近 Create 符號 \`CSkillItf::Create\` L178370 @0x0046AE4D，需確認函式邊界
- HK/SGOClient.exe.lst L173173 @0x0046A6C0（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`BG_SKILL_S.spr\` → HK/output/Interface/BG_SKILL_S.spr
- \`B_ONLINEHELP.spr\` → HK/output/Interface/B_ONLINEHELP.spr
- \`CLOSESCREEN.spr\` → HK/output/Interface/CLOSESCREEN.spr
- \`EXP.spr\` → HK/output/Interface/EXP.spr
- \`ICON.spr\` → HK/output/Interface/Icon.spr
- \`SCROLLDOWN_BUTTON.spr\` → HK/output/Interface/SCROLLDOWN_BUTTON.spr
- \`SCROLLUP_BUTTON.spr\` → HK/output/Interface/SCROLLUP_BUTTON.spr

### 直接載入／動態素材線索
- Interface/SKILL_ICON.spr (L36399, 004164C4；L260323, 0049EAAE)

## 3. Server / Data 對照
- Old/服務端/plugins/SGOSkillSys.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`SGOSkill::GetSkillPlayer\` L10206 @0x100074FC
  - \`CSkill::GetSkillCount\` L12480 @0x10008AB0
  - \`SGOSkillSys::LoadSkillData\` L25992 @0x10011017
  - \`SGOSkillSys::CreateSkill\` L29831 @0x10013805
  - \`SGOSkillSys::FindSkillData\` L30723 @0x100140AF
  - \`SGOSkillSys::GetSkillHitCount\` L49959 @0x10020580
  - \`SGOSkillSys::SetSkillTimer\` L60793 @0x10027539
  - \`CDSkillSys::GetCurrentSkillInfo\` L61091 @0x100276F0
- Old/服務端/plugins/SGOCareerSys.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CCareer::FindJobID\` L2274 @0x100025B3
  - \`CCareer::SetCareer\` L5738 @0x100048DA
  - \`CCareerData::FindJobData\` L8805 @0x100064DA
  - \`SGOCareerSys::LoadCareerData\` L16698 @0x1000B015
  - \`SGOCareerSys::OnChangeJob\` L27182 @0x10011B9A
  - \`SGOCareerSys::ChangeJob\` L28199 @0x100124EA
  - \`SGOCareerSys::GetJobFlag\` L34842 @0x100167E7
  - \`SGOCareerSys::FindCareer\` L42993 @0x1001BAA4
- 相關資料：skill.cfg；Dat/Skill/；ClientData/Interface/skill.dat
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：技能清單、圖示、等級、技能熟練度/學習與快捷列入口；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：未學會不可施放；學習後顯示、拖曳及消耗一致。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
