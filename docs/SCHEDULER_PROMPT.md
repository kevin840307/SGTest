# SGTest Scheduler Prompt v12 — UI Recovery

立即實作 SGTest，不要只規劃。正式工作目錄 C:\SGO\SRC\SGTest，GitHub main。
每輪讀最新版 AGENTS.md 並以它為唯一執行規範；只讀 todo.txt、docs/FEATURE_MATRIX.md 與當前工作有關的證據。詳細契約按需讀 docs/AGENT_REFERENCE.md。

1. 快速完成 Git/Session 安全檢查，避開其他 Session 正在修改的切片、程序與資料。已知 P0 可玩性/資料錯誤先修，否則預設 UI Recovery。
2. 依使用者回報及最大可見差距，接續 Login → Select/Create → HUD → Profile → Inventory/Equipment → 其他既有核心 UI；不擴新玩法。鎖定一個完整畫面或共同根因，同輪批次修正已確認的所有相關差異，不要只修一兩個字就結束。
3. 寫 code 前先取得同狀態 800×600 原版 reference 與 SGTest Before，並排/overlay/pixel diff。原版素材、ITF、字型、文字 baseline、rect、hitbox、狀態皆依 HK 證據；幾何目標 ±1px。Paradise 僅為可操作參考，版本衝突以 HK 為準。不猜、不自創、不重複解包、不無目的反編譯。
4. 優先修共用座標/縮放/字型/資源 renderer 根因，直接實作並快速迭代。用真 Server + 可見 Godot + 真滑鼠鍵盤取得 After，驗受影響控件/動態文字/流程；比較 Reference/Before/After。未知項標 NOT VERIFIED；先完成已確認差距，不被微小未知 raster 阻塞，也不得假報 1:1。
5. 依 AGENTS.md T0–T3 風險分級跑必要測試；純 UI 不重跑完整 Server 334+ tests。共用根因驗直接受影響畫面，多人相關必須雙 Client，高風險或里程碑才完整 Smoke。重用未失效證據與隔離環境，避免重複工作。
6. 安全且有實證的改善才提交；畫面仍有差異保持 IN PROGRESS/TEST FAILED。精簡記錄 TODO/Matrix、private reference/Before/After、主要差距、下一步；review diff、commit/push main、fetch 確認 0/0，保護其他 WIP。私有素材/字型/截圖/憑證不入 Git。

回報：實際修好哪個畫面、修前/修後差距、實機/原版證據、尚未完成項目、必要測試及 main SHA。沒有可見成果就明示 blocker，不用測試數量或文件充數。核心 UI 恢復後再依 AGENTS.md 完整收斂單一玩家功能。
