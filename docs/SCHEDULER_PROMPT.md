# SGTest Scheduler Prompt v13 — Playable Gameplay + UI Parity

立即實作 SGTest。正式目錄 C:\SGO\SRC\SGTest，GitHub main。
讀最新版 AGENTS.md（唯一執行規範）、相關 todo.txt / docs/FEATURE_MATRIX.md；詳細契約按需讀 docs/AGENT_REFERENCE.md。舊 v11/v12 的 UI-only 及全量流程不適用。

1. 快速 Git/Session 安全檢查，保護其他 Session WIP、程序、資料。真 P0 優先；登入已能進世界時，不再因字型 AA/caret/Tab 等非阻塞差異反覆回到登入。
2. 預設 Gameplay-first + UI Parity：每三個非緊急有效交付至少兩個核心玩法、一個遊戲內 UI；按 TODO/Matrix/近期成果選擇，不按啟動次數。優先打通正式角色的戰鬥資格/可信資料→選怪/普攻/受擊/擊殺/EXP/掉落→技能/MP/冷卻/動畫→撿取/背包/使用/裝卸/持久化→NPC 商店/銀行/倉庫/修理→多人/組隊/交易→其他玩法。依實際依賴調整。現在優先確認正式角色 combat-locked blocker，禁止 SyntheticTest 或假數值冒充正式可玩。
3. 每輪從 `docs/legacy_task_catalog_20261124/executable/README.md` 與 `EXECUTION_INDEX.csv` 選取一個符合前述優先順序、尚未完成且無並行衝突的 Gameplay leaf 或 UI MD，依該檔逐項實作與驗收；整合任務待子任務驗收後關閉，未配對 SPR 證據任務不冒充新玩法。已完成項目以既有有效證據核對，未知/阻塞則記錄原因並改選獨立項目。每輪鎖定一個玩家可操作垂直切片，檢查既有功能，補齊必要 Server Authority、資料、Client、UI、持久化及適用多人；有餘力且已驗可接續下一切片。阻塞或其他 Session 正在做則改做獨立高優先項，留下具體原因及下一步。
4. UI 軌優先 HUD/戰鬥/技能/背包/裝備/NPC；整張畫面或共同根因批次修正。HK 原圖、ITF、字型、文字 baseline、rect、hitbox、交互依證據；800×600 幾何目標 ±1px。UI 修改必須原版/Before/After 實機 overlay/diff；非 UI 玩法不用重做登入像素對照。非阻塞 UI 差距可留 backlog，不阻塞其他玩法，不標假 VERIFIED/COMPLETE。
5. 使用真 Server + 可見 Godot + 真滑鼠鍵盤驗玩家操作及權威結果，按 AGENTS.md T0–T3 風險分級測試，適用多人雙 Client、持久化重登、原版 A/B；純 UI 不重跑全量 Server。重用未失效證據及隔離環境；未知數值不猜、不弱化 fail-closed。沒有可玩成果不算完成。
6. 更新 TODO/Matrix：本輪玩法/UI 軌、實際改善、測試、未解差距/下一個具體阻塞；review diff、commit/push main、fetch 確認 0/0。保護其他 WIP，不提交私有素材/字型/截圖/憑證。

回報玩家現在能做什麼、實際測試、未驗項與 main SHA；不得以文件、測試數量充數。完整 HK 1:1 與功能 COMPLETE 標準不變，但不等登入完美才開發其他玩法。
