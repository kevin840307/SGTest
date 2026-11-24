# UI-008_MINIMAP_S — 小地圖（008_MINIMAP_S）

- 優先度：P1（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：Old+HK 直接引用。原版 ITF：\`HK/output/Interface/008_MINIMAP_S.itf\`（215 bytes）。
- 目標：世界座標轉換、玩家/隊友標記、大小切換。
- 依賴：先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。

## 1. 反編譯證據與行為入口
- Old/ClientData/Paradise.exe.lst L132946 @0x0045048B（ITF 引用）；鄰近 Create 符號 \`CMiniMapSItf::Create\` L132931 @0x0045047A，需確認函式邊界
- HK/SGOClient.exe.lst L130133 @0x0044FB3A（.text ITF 引用）
- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
- \`B_ONLINEHELP.spr\` → HK/output/Interface/B_ONLINEHELP.spr
- \`MINIMAP_S.spr\` → HK/output/Interface/MINIMAP_S.spr
- \`ZONESCREEN.spr\` → HK/output/Interface/ZONESCREEN.spr

### 直接載入／動態素材線索
- 此任務暫無已定位的額外動態 SPR；需檢查 Draw/資源載入器，不能假設 ITF 已包含全部。

## 3. Server / Data 對照
- Old/服務端/plugins/SGOMapService.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDMapService::GetMapCount\` L19011 @0x1000BB63
  - \`CDMapService::OnPlayerMove\` L19890 @0x1000C3BE
  - \`CDMapService::OnNPCMove\` L26326 @0x100103CE
  - \`CDMapService::GetMove\` L27492 @0x10010CAD
  - \`CDMapService2D::GetMapCount\` L59189 @0x10024597
  - \`CDMapService2D::GetMapCount_Real\` L59618 @0x100249AC
  - \`CDMapService2D::OnPlayerMove\` L90364 @0x10038104
  - \`CDMapService2D::OnPetMove\` L95950 @0x1003BAA0
- Old/服務端/plugins/CDTeam.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。
  - \`CDTeam::GetTeamSize\` L12938 @0x100090F3
  - \`CDTeamMgr::RemoveTeam\` L18444 @0x1000C2B4
  - \`CDTeamSys::OnInvite\` L36232 @0x10017291
  - \`CDTeamSys::OnInviteReply\` L37703 @0x100181AA
  - \`CDTeamSys::GetTeamMembersPtr\` L51685 @0x10020DFA
  - \`CDTeamSys::GetTeamMembers\` L52707 @0x10021747
  - \`CDTeamSys::GetTeamMembers_ByLeaderUID\` L53584 @0x10022003
  - \`CDTeamSys::GetTeamList_First\` L54461 @0x100228C0
- 相關資料：map.cfg；Dat/Map/
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：世界座標轉換、玩家/隊友標記、大小切換；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：跨圖重設、邊界與縮放正確；標記跟隨世界座標。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
