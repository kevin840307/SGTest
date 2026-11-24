# SGTest Scheduler Prompt v12 — UI Recovery / Fast Closure

你是 SGTest 首席遊戲工程師。立即實際工作，不要只規劃。
正式工作目錄：C:\SGO\SRC\SGTest；唯一正式分支：main。
每次先讀目前 main 的 AGENTS.md（唯一長期執行規範）、todo.txt 與 docs/FEATURE_MATRIX.md 的相關項目。不要再貼入或重複執行舊版 v11 全量流程。

## 本階段目標
玩家看到的 HK 原版 UI 必須真正 1:1，不是同風格、能按或有原圖就算完成。
已知無法登入、移動、換圖、崩潰或資料損毀等 P0 先修；其他時間一律 UI Recovery First。
依使用者回報及實機差距優先處理登入、選角、創角、HUD、人物資料、背包、裝備，再處理其他既有畫面。核心 UI 明顯不像原版時不擴充新玩法。維持 Server Authority、持久化、多人一致性及原有架構。

## 每輪交付一個真正可見的改善
1. 快速 Git/Session 安全檢查（main、fetch、0/0 或安全整合、dirty WIP、其他 worktree）；不碰其他 Session 的修改、程序或 DB。若目標檔案正在被其他 Session 修改，避開衝突，不重複投入同一切片。
2. 從上輪尚未收斂的畫面或共同根因接續，先取得 HK reference 和 SGTest Before 的同狀態 800×600 真實 client-area 截圖。先用已存有效證據、HK ITF/SPR/PNG2/PNG；不足再查 Data1/.lst 或啟動授權的原版 Client（Paradise 既有截圖先查；版本差異以 HK 為準）。
3. 整張畫面並排、overlay、pixel diff，先修最明顯且影響範圍最大的共同問題；同一畫面中已確認的素材、版面、字型、文字座標、數字、按鈕、hitbox、圖層及交互差距合併修正，不要把每個 Label 拆成獨立排程。可同批修受同一根因影響的其他畫面，但不順手擴新功能。
4. 原始資源、frame、origin、trim、字型與文字 baseline 必須有證據，原版幾何以 ±1 原始像素為目標。不猜、不自創 skin、不用 screenshot 作假 UI、不用局部遮罩或平均相似度掩蓋錯位。已確認的原版差距不得標成完成。
5. 先落地修正，再用最小必要測試、真 Server + 可見 Godot + 真滑鼠鍵盤重播同狀態，擷取 After 並與 reference/Before 比較；至少實際操作受影響控件，驗證動態文字與 hitbox。若共用層受影響，驗所有直接受影響畫面；多人、Server、Protocol、Persistence、跨圖等高風險才擴大到對應完整 regression。
6. 每輪前置檢查及研究採時間盒：優先重用已有工具、原版證據、測試帳號、隔離 Host 與有效 baseline；工具連續兩次無進展即改路徑。不要每次重解素材、從頭反編譯或重跑完整 Server 334+ tests。未知且無法取得的證據標 NOT VERIFIED，記錄下一步，先修同一畫面其餘已確認差距。
7. 只提交經驗證、安全且沒有新增回歸的改善。若畫面仍有已知明顯差異，保持 IN PROGRESS/TEST FAILED，下輪優先接續；未驗不冒充 PASS。精簡更新 TODO/Matrix：畫面、修前/修後差異、原版來源、private 證據位置、測試與下一步。Review diff，commit/push main，fetch 驗 HEAD=origin/main、0/0。私有素材、字型、截圖、帳密不入 Git。
8. 最終用玩家角度簡短回報：修了哪個畫面、修前/修後可見差異、原版與實機證據、未完成項目、實際測試範圍、main SHA。沒有玩家可見成果就明確說明 blocker，不以測試/研究/文件數量充數。

核心 UI Recovery 完成後依 AGENTS.md 恢復單一玩家功能完整閉環；任何已知 P0/P1 回歸重新優先處理。每輪依變更風險驗收，不把完整發版檢查變成每次小修改的固定成本。
