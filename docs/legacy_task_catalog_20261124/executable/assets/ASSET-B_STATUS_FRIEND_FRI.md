# ASSET-B_STATUS_FRIEND_FRI — B_STATUS_FRIEND_FRI.spr 動態素材可達性（獨立證據任務）

狀態：NOT VERIFIED。父任務：[GP-078-02](../gameplay/GP-078-02.md)。遵守 [AGENTS.md](../../../../AGENTS.md)。
來源：HK/output/Interface/B_STATUS_FRIEND_FRI.spr。未出現在本次 152 個 ITF 的直接控件/背景引用集合；不代表未使用，亦不得直接新增不存在的遊戲功能。

## 已取得的實際素材資訊
- SPR header 幀數：4
- 原始寬高與 origin：19×15 @(0,0) ×4 幀
- 反編譯字串定位（Old / HK，最多 30 處；可能為資料區或動態引用，須追 caller）：
- 未找到相同完整檔名字串；需查動態格式化、共用 loader 或版本差異。

## 獨立執行步驟
- [ ] 使用正式素材管線確認本素材所有 frame、origin、透明度及用途；不得提交原始素材。
- [ ] 查上述原版地址、caller、動態檔名/ITF 子目錄；取得原版實際畫面或可達性證據，記錄哪個功能、畫面、觸發/取消與狀態。
- [ ] 若可達，定位 SGTest 既有 owner、對應 UI/Gameplay 任務；補上正式素材及顯示/交互缺口，驗 frame/位置/字型（適用）、原版操作與 Before/After。
- [ ] 若僅屬舊版/未啟用/未確認，保留證據分類 variant/dormant/unknown，不建立假入口、不標功能 COMPLETE。
- [ ] 更新 GP-078-02 的證據與既有 TODO/Matrix（若有實際產品變更），Git 僅提交本切片。
