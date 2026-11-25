# SGTest Scheduler Prompt v15 — HK Full Parity / End-to-End Gameplay / Multiplayer Performance

立即實際開發。正式 C:\SGO\SRC\SGTest，https://github.com/kevin840307/SGTest，唯一 main。先讀最新版 AGENTS.md；v15 取代舊規則。

1. Git/Session 安全同步，保護其他 WIP/程序/桌面/DB/port；真 P0、使用者指定優先。查 todo.txt、docs/FEATURE_MATRIX.md、docs/legacy_task_catalog_20261124/executable/README.md、EXECUTION_INDEX.csv，選未完成 UI-/GP- Task ID、讀直接相依/metadata。
   每輪開局依 AGENTS.md §4 清理上一輪及更舊、已確認閒置的 SGTest 測試程序/可重建測試 EXE，釋放桌面/port；結束保存證據後關閉本輪測試程序並清暫存。保護正式 EXE、原版素材、DB/證據、活躍 Session/WIP、Scheduler；不因已確認舊殘留跳過實機。
   既有未提交/新增 WIP 不可一律避開：開局按 diff、Task ID、Session 活動及相依分組；上一輪/已結束 Session 的成果優先接續或補齊驗收，安全切片明確 stage/commit/push main（每輪至少一組可接續 WIP，直至收斂）。只保護活躍、身分不明或確實阻塞部分，列具體原因與下一步；不盲目 add -A、不覆寫/丟棄 WIP、不提交私有資產/憑證/DB。
2. 三軌 UI 35% / 玩法 35% / 多人效能 30% 長期平衡；每輪完整主題、2～4 候選、3～5 實質 leaf 或整體畫面/流程、25～40 分鐘有效工作目標。每個切片 commit 後續作；兩輪同類非 P0 局部修補無玩家成果，重現完整流程或換軌。阻塞記錄後改做安全獨立項。
3. 先驗正式角色 combat-locked 與最早未通可玩鏈。正常輸入→Server 權威/封包→畫面動畫/回饋→適用雙 Client/持久化。UI 整屏原 SPR/frame/rect/font/baseline/state/hitbox/拖曳/動畫，800×600 同 DPI Reference/Before/After + ROI/overlay/diff、幾何 ±1px。優先 HUD/背包/技能/人物/NPC，HK-only 保留切換、不移除地圖與 actors。
4. 完整來源見 AGENTS.md §0/§3：UI_CONTROL_METADATA.json、SPR_FRAME_METADATA.json 首選，HK ITF/SPR/.lst/原始 PNG、Old Client/Server、Recovered/Recovered2、Paradise 截圖/原版 client、SGOAuto、正式 converter。參考唯讀，Godot MCP 優先實際工具，兩次失敗 fallback；真 Server + 可見 Windows Godot + 真鍵鼠才是實機。
5. 多人雙真 Client，驗移動/動畫/AOI/戰鬥/掉落/跨圖/重連；依 docs/TEST_PLAN.md v15 Gate 執行 20 人/300 怪/500 Drop 主動混合基準及必要階梯/soak，量 frame/tick P95/P99、延遲、queue、CPU/RSS/VRAM；不降畫質/正確性，不以 Bot 站樁或 headless 替代。按 T0–T3 驗受影響項。
6. 五 Gate（UI/Gameplay/Multiplayer/Performance/Persistence）分列 PASS/FAIL/NOT VERIFIED/N/A；無證據不猜、不冒充 COMPLETE。更新 TODO/Matrix、證據、下一 Task ID；只 commit 本輪及經審查安全整合的 WIP、push main、fetch 驗 0/0。結束前重查安全候選與資源，能續作就繼續；回報玩家成果、main SHA、未驗、停止原因。不湊時間/測試/commit。
