# SGTest 開發與 AI Session 交接規則

## 開始工作前（強制）

1. git fetch，確認 SGTest main 最新 HEAD；先讀 README.md、SPEC.md 第7節、todo.txt、docs/LEGACY_ASSET_PIPELINE.md、research/source_manifest.json；素材 Gate 通過後才讀其他遊戲系統文件。
2. 若任務需要舊版證據，確認參考 TestGoGo 的 feature/local-server-net8 最新 SHA。首份規格基準：aee7ec7fff9e446452655cf6d2be460dfd7a88f1。引用精確路徑/函式/位址/輸入輸出，不憑記憶臆測。
3. 檢查 TODO 最新領取狀態；只領一個未完成 Task ID。同時有人處理同檔案，使用個人分支/工作樹；不得 force push、不得覆寫同事的新版本。
4. 每個 Task 先列「修改前行為、可觀測指標、回歸案例」，之後才修改。拆成可審核的小 commit。測試未執行一律寫 NOT VERIFIED（不可虛稱 PASS）。
5. 完成後同步更新 todo.txt：證據、檔案、commit SHA、實際測試命令與結果、未解風險；遇到衝突重新 fetch/rebase，禁止硬蓋其他 Session。
6. 純文件階段不可擅自宣稱 Client、Server、E2E 已能運作；遊戲程式實作需獨立 Gate。

## 當前 M0 限制

- 2026-11-13 已清掉早期 Godot/.NET Demo 與 CI，禁止把原型視為仍在 main；目前只處理 AS001～AS009 原版素材來源、載入邏輯、解碼與 viewer。
- 未有完整原版 Client 實證前不得製作正式遊戲畫面、伺服器或 Atlas；不得以副檔名宣稱解碼成功。
- 原版「方向 × 動作」分離及動畫偏移/幀時間/分層需完整驗證，不得先假定 Sprite Sheet 一定較快。

## 不可違反

- 新專案必須自主、低資源、可擴充、單一真相；不要把舊 Godot 超大網路/HUD/DB 類別複製過來。
- 使用者已選擇後期《星夢 Online》金黃色 UI，必須用新 Godot Theme/Control/Scene 自製；不得沿用 TestGoGo 現有的 game_hud.gd 或舊 UI .tscn，也不可混用早期藍色 UI。
- 原版素材需先做合法使用權/格式/版本查核，未確認權利時只可本機研究，不能把原版/衍生圖檔上傳公開 GitHub。
- Server 最終決定：座標、碰撞、範圍、傷害、命中/暴擊、死亡、金錢、物品 UID、交易、防重放。
- Player 的 Spawn/Move/Stop/Attack/Hit/Death/Facing/Teleport/Despawn 要有明確因果與版本邊界，優先於掉落物視覺。不得讓普通 Move 重啟受擊/攻擊/死亡動畫。
- 不靠調低 FPS、物理 Tick、網路輪詢、封包消化速度或 AFK 斷包來改善效能；只有渲染、音效或非必要視覺排程可降級。
- Client 預測/平滑座標不作為 Server 碰撞、戰鬥或撿物依據。
- 不在 MapActor / Session 大鎖內做 DB 磁碟寫入、網路 Send 或重型路徑搜尋。
- 共享素材/動畫快取用受控生命週期，登出/切圖恢復基線，禁止資源持續累積。
- 不在確認授權前從 Old 複製貼圖、音效、反編譯程式碼或原始文本到此 repo。可引用路徑、位址、演算法研究結論。
- 原強化 +1/+2/+3 是 legacy 定義別名，不是新 ItemDefinitionId 的獨立定義。所有新裝備都必須支援規格化強化欄位；非裝備不因此變可強化。
- ID「往前排」只能在初次匯入時使用明確、可重跑、確定性映射；新 ID 正式公開後必須保持穩定。

## 文件與程式碼風格

- 設計文件、TODO、驗證摘要繁體中文；程式類別與協定欄位使用一致英文命名。
- Domain/Transport/Persistence/Presentation 責任明確，避免跨層呼叫。
- 以依存方向保護 Domain；單一檔案與方法過大需要拆模組，但禁止只為分檔做沒測試的大改。
- 每項設計變更進 docs/DECISIONS.md；重要 Protocol/DB schema 變更需版本與遷移方案。
- 原版反編譯結論分級：DIRECT（原始碼/組語可定位）、AUDIT（舊 repo 文件記述）、HYPOTHESIS（待驗證），需補可靠性/誤差。

## 每個 Task 的完工格式

- ID / 範圍 / 改動摘要
- 來源/原理/性能目的
- Before→After（數據或行為）
- Git SHA / 相關檔案
- 測試命令、CI run URL、PASS/FAIL/NOT VERIFIED
- 尚存缺口、下個接手人可直接跑的命令

## 禁止直接大規模變更的區域

物品 ID 轉換、伺服器時間線/佇列、MapActor 生命週期、PostgreSQL 資產交易及原始地圖座標契約都必須先有可重現 fixture 和相容性測試。