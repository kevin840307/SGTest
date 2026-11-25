# SGTest Scheduler Prompt v14 — Task-driven Delivery / HK Full-screen Parity

立即實際開發，不要只規劃、打招呼、重述規範或反覆研究。
正式目錄 C:\SGO\SRC\SGTest；GitHub https://github.com/kevin840307/SGTest；唯一正式分支 main。
先讀最新版 AGENTS.md（唯一常駐規範），依 v14 執行；舊 v11–v13 衝突規則不適用。

1. 快速 Git/Session 安全同步（status、branch、fetch、ahead/behind），保護其他 WIP、桌面、程序、DB、port、輸出。讀相關 todo.txt、docs/FEATURE_MATRIX.md 與既有證據。真 P0 及使用者指定優先。
2. 任務來源必須先查 docs/legacy_task_catalog_20261124/executable/README.md、EXECUTION_INDEX.csv，選明確未完成 Task ID，讀該 UI-/GP- MD 與直接相依；UI 用 UI_CONTROL_METADATA.json、SPR_FRAME_METADATA.json 查精確 control ID/offset/rect/SPR/frame/origin，不從零猜測。找不到再定向查 HK ITF/SPR/SGOClient.exe.lst、C:\SGO\Recovered\Old（Client/Server 反編譯、效果/排版/功能/規則/資料）、C:\SGO\Recovered（圖名稱、素材/實作參考）。HK 原版為最終 UI 目標，Old/Recovered 版本差異標明。原始參考唯讀，不重複全量掃描。
3. 每三個有效非緊急交付至少兩個核心玩法、一個遊戲內 UI。玩法選最早未通的可玩鏈：正式角色真戰鬥資格→選怪/普攻/MISS/受擊/擊殺/EXP/掉落→技能/MP/冷卻/動畫→拾取/背包/使用/裝卸/重登→NPC/商店/銀行/倉庫/修理→多人/組隊/交易→其餘玩法。檢查目前是否仍 combat-locked，若已通就推進，不假定永遠阻塞。玩法 leaf 必須有正常玩家操作、Server 權威、Client 真資料、UI 回饋及適用的保存/多人；不以空殼、測試數量、SyntheticTest、假資料或繞過 fail-closed 充數。首項完成立即做下一個安全小任務；整合父任務等子項驗收才關閉。遇其他 Session 或證據 blocker 記錄原因後改做獨立高優先項。
4. UI 優先整個世界 HUD、背包/裝備、人物資料、技能、NPC；預設 HK-only UI，保留切換功能，地圖/人物/NPC/怪物/地上物必須正常。按整屏逐控件對照原 SPR、frame、rect、font/baseline、文字、圖層、hitbox、按鈕狀態、動態資料/效果；先修共用座標/字型/渲染根因再批次對齊。原圖可取不得自畫通用 UI。HP/MP 動畫及數值、F1…文字、右側/背包按鈕、NPC 對話圖片組合、所有指定可拖曳面板（非互動區拖，按鈕/槽位/輸入不吞事件，不穿透地圖）必須驗。不得一直修登入 caret/AA/Tab；非阻塞視覺差異仍 UI gate FAIL/NOT VERIFIED，明確續作，不阻礙獨立玩法。
5. Godot MCP 已安裝：需要 Godot 時先發現實際可用工具、確認版本及正式 client/project.godot；能查 scene/tree、rect/anchor/theme/font/texture、debug error 或 run_project 就使用。工具不提供或連續失敗兩次立即 fallback 到本機 Godot/Remote debugger/OpenChatX；只操作本輪隔離程序，禁止假裝已使用 MCP。MCP、headless、直接 handler/emit 不是玩家驗收。
6. 真 Server + 可見 Windows Godot + 真鍵鼠；UI 必須同狀態 800×600 同 DPI 的完整 HK Reference/Before/After、ROI/overlay/diff、已知幾何 ±1px、逐控件及 hitbox/拖曳、動態項連續影格。玩法實測成功、失敗/取消、真權威及適用重登/雙可見 Client；依 AGENTS.md T0–T3 風險分級，不重跑無關全量 suite。原版未確認數值、語意、frame 或 AA 如實標未知，不猜；已知差異不得宣稱 1:1/COMPLETE。
7. 每輪記錄 Task ID、已關閉的子項、Gameplay Gate 與 UI Gate 分別結果、實機證據/剩餘缺口/下一項，更新 TODO/Matrix（唯一正式功能矩陣）。review diff，僅提交本輪成果，push main、fetch 驗 0/0。保護私有圖檔、字型、截圖、反編譯全文、憑證及並行 WIP。
8. 每輪持續交付：開局選一個可玩主題及 2～4 個候選 Task ID，目標 25～40 分鐘有效開發、3～5 個實質相關 leaf 或完整大型玩法/UI 畫面。首項及每次 commit/push 後立即續做下一項；遇阻塞換安全獨立項。結束前重查可做候選與資源，有安全高優先項且資源足夠就繼續；只有主題與合理工作量完成且無合適續作、實際資源限制需交接，或剩餘項均有具體阻塞才結束。回報實際交付 Task ID、Gate、main SHA、停止原因及下一項。不得 sleep、湊時間/測試/commit、降驗收或拆假任務。

回報玩家現在能實際做什麼、哪些整屏差異已修、哪些未驗、main SHA；不能用文件、class、測試數量替代玩家成果。沒有成果時具體說明 blocker。
