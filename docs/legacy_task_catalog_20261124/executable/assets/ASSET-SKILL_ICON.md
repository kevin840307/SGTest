# ASSET-SKILL_ICON — SKILL_ICON.spr 動態素材可達性（獨立證據任務）

狀態：NOT VERIFIED。父任務：[GP-078-02](../gameplay/GP-078-02.md)。遵守 [AGENTS.md](../../../../AGENTS.md)。
來源：HK/output/Interface/SKILL_ICON.spr。未出現在本次 152 個 ITF 的直接控件/背景引用集合；不代表未使用，亦不得直接新增不存在的遊戲功能。

## 已取得的實際素材資訊
- SPR header 幀數：114
- 原始寬高與 origin：24×24 @(0,0) ×114 幀
- 反編譯字串定位（Old / HK，最多 30 處；可能為資料區或動態引用，須追 caller）：
- Old/ClientData/Paradise.exe.lst L36399 @0x004164C4 (.text)；所在符號 ?LoadSkillObj@CSGOUim@@IAEXXZ
- Old/ClientData/Paradise.exe.lst L125466 @0x0044C206 (.text)；所在符號 ?OnLoadHotKey@CHotKeyItf@@QAEXPAUHOTKEY_SYNC_TO_CLIENT_Data@@@Z
- Old/ClientData/Paradise.exe.lst L260323 @0x0049EAAE (.text)；所在符號 ?InitComponent@CRobotComplexInfoItf@@MAEXXZ
- Old/ClientData/Paradise.exe.lst L833722 @0x005EC410 (.data)
- Old/ClientData/Paradise.exe.lst L850865 @0x005F12A0 (.data)
- Old/ClientData/Paradise.exe.lst L901616 @0x005FEB54 (.data)
- HK/SGOClient.exe.lst L35007 @0x0041420D (.text)；所在符號 sub_414158
- HK/SGOClient.exe.lst L124237 @0x0044C174 (.text)；所在符號 sub_44BE62
- HK/SGOClient.exe.lst L306686 @0x004BFC84 (.text)；所在符號 sub_4BFA1D
- HK/SGOClient.exe.lst L839211 @0x0060E14C (.data)

## 獨立執行步驟
- [ ] 使用正式素材管線確認本素材所有 frame、origin、透明度及用途；不得提交原始素材。
- [ ] 查上述原版地址、caller、動態檔名/ITF 子目錄；取得原版實際畫面或可達性證據，記錄哪個功能、畫面、觸發/取消與狀態。
- [ ] 若可達，定位 SGTest 既有 owner、對應 UI/Gameplay 任務；補上正式素材及顯示/交互缺口，驗 frame/位置/字型（適用）、原版操作與 Before/After。
- [ ] 若僅屬舊版/未啟用/未確認，保留證據分類 variant/dormant/unknown，不建立假入口、不標功能 COMPLETE。
- [ ] 更新 GP-078-02 的證據與既有 TODO/Matrix（若有實際產品變更），Git 僅提交本切片。
