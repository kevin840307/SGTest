# SGTest 完整開發規格（v1.0 / 2026-11-12）

狀態：設計基準，未實作。新遊戲原創開發，唯讀參考 SGO/TestGoGo 的 Old、Client、Server 與測試。性能數字與里程碑都屬「目標」，不是已驗證成果。

## 1. 願景、關鍵成功標準與範圍

目標：建立可長期開發的輕量 2D MMORPG，支持多人同圖、怪物/掉落物密集、即時移動與戰鬥、地圖探索、裝備強化、任務、寵物與機甲，後續擴充 PVP、交易、社交和小屋。

硬性優先序：**正確性與權威 > 人物移動/方向/狀態的主觀流暢 > 戰鬥即時性 > 渲染效率/記憶體 > 開發迭代便利 > 裝飾效果**。正確性不可因效能優化被降級；不允許 Client 幀率或失焦決定邏輯更新率。

「比現版 Godot 更好」必須有同等素材/場景/硬體下的 A/B：玩家畫面、RAM、VRAM、CPU、GPU、Frame P95/P99、Server Tick P95/P99、Net E2E P95/P99。比較方法見 docs/PERFORMANCE.md。

## 1.1 最新選定：後期星夢 Online 金黃色 UI 和原始資產
- 使用者 2026-11-13 指定後期《星夢 Online》金黃色 UI。早期星空之門藍色 UI 不是正式基準。
- 用 Godot Control、Container、Theme、.tscn 全新建立 UI，不複製 TestGoGo 的 HUD/視窗/登入/背包等 UI 程式碼。
- 目標是導入原版素材，但素材來源、版本、格式與合法使用權需要先確認。優先唯讀盤點/獨立解析/匯出 PNG+Atlas+Animation metadata，並確保透明度、方向、偏移、幀時長正確。
- 原版後期 .itf 目前只有檔名索引，Old/ClientData 沒看到實體 .itf；不能宣稱完整原版 UI 素材已齊。未確認授權不傳到公開 GitHub。
- 相關文件：docs/OLD_UI_DESIGN.md、docs/LEGACY_ASSET_PIPELINE.md。新的優先 Gate：A008/A009/A010/B007/B008。最小多人 demo 保留，只作技術基線。

## 2. 技術選型與品質原則

- Client：Godot 4.x，首選 GDScript + 輕量 Data Oriented Entity State；Node 專注 UI/少量互動、畫面可視 Pool。是否改用 C# 或 C++ GDExtension，只能由 Profiler 證據決定，須包含 Runtime 記憶體成本。
- Server：C#/.NET 10 LTS，ASP.NET Core Gateway + MapActor/World State + Domain Application；單體模組化起步，未達負載瓶頸前不拆微服務。
- DB：PostgreSQL，原子資產事務/Outbox；熱路徑 WorldState 在 RAM，地圖每 Tick 不讀寫角色 DB。
- 協定：版本化 MessagePack / Protobuf 擇一（ADR 紀錄）；TLS WebSocket 初版，ServerSeq/SimTick/SessionEpoch/EntityRevision/MapEpoch；不沿用舊 AES/Base64 JSON wire 格式。
- 內容：原生 JSON/二進位資料包，離線工具轉換 Old；官方原版素材與原始碼需先確認合法使用權，否則以自製/測試素材代替。
- 開發：GitHub Actions 自動 lint/compile/test/資料驗證/模擬負載；以具體指標改善，無測試就不聲稱達標。

## 3. 功能需求與完成定義

### Phase P0 — 可玩最小多人核心
- F001 帳戶/登入/登出/選角/建角/多角色/重連/身分過期，重登不得保留幽靈 Entity。
- F002 地圖載入（登入→選角→入圖多階段進度）、Blocking/視野/傳點、世界座標與地圖局部座標轉換，支援超寬視窗及變焦。
- F003 玩家本地即時操控、點地面/點怪/NPC、停走、轉向、追路、人物速度；Local Input→Server command，視覺可預測但戰鬥/阻擋由 Server 驗證。
- F004 遠端玩家 Spawn/Despawn、初始方向/外觀、移動、停止、轉彎、跳圖/重連；不得跳格、回拉、原地跑步；同畫面 20 人行走時優先維護。
- F005 怪物 AI：出生、巡邏、追擊、脫戰/回巢、不能穿牆、方向/動作、攻擊、死亡、重生；數百怪共享地圖運算，不能每 Session 有獨立副本。
- F006 普通攻擊、命中/未命中、傷害浮字、受擊/攻擊/死亡方向與動畫、技能與 CD；多觀察者看見相同權威結果，動作及位置封包互不相互覆蓋。
- F007 地上物：產生、可見/不可見、拾取權限、保護/期限、切圖回來一致；多掉落物不得造成畫面長時間卡住。
- F008 角色基本 UI、快捷欄、背包、裝備、聊天、基本 NPC 互動、GM 查詢；視覺 UI 不修改權威資料。
- F009 低資源選項：畫質與可見物密度、AFK、失焦、最小化；只對渲染/音效生效，收包、固定邏輯、戰鬥/撿物不中斷。
- F010 診斷/測試：真 Client+Server E2E、20 Bot+300 怪基準、對照素材與效能收集。

### Phase P1 — 完整 MMORPG 核心
- F011 ItemDefinition/ItemInstance、裝備欄/強化/修理/耐久/套裝/附加屬性，所有裝備類型能持有強化等級。
- F012 NPC 商店/銀行/倉庫/玩家交易/攤位，持久化與雙花防護、斷線重試無重複扣款。
- F013 任務/對話/選項/分支/獎勵、傳送/復活點與進度儲存。
- F014 多職業/技能樹/主動/被動/範圍/目標技能、Buff/Debuff/特殊連動與視覺特效。
- F015 寵物召喚/跟隨/自動攻擊/離開/經驗裝備；機甲/坐騎/升級/部件/移速。**動畫播放速率不得直接用人物移速倍率代替**。
- F016 組隊、好友、基本公會、聊天、掉寶機率/經驗/貨幣倍率分離，權限受控 GM 工具。

### Phase P2 — 擴充系統
- F017 PVP 合法地圖規則、藍/白/粉/紅名狀態/倒數/掉落/懲罰、GM 指定顏色可安全控制。
- F018 小屋 Housing Instance、家具放置/移動/儲存、訪客/權限、場景載入與低資源快取。
- F019 好友進階/公會功能/活動/排行/信件/拍賣等。
- F020 多 Channel / MapActor 多實例，安全跨圖移交與水準擴充，保留最初單服行為一致。

## 4. 重大技術契約

- EntityId 是暫態網路身分，CharacterId 是持久身分，ItemInstanceId 是獨立資產 UID，ItemDefinitionId 是共用物品種類；不得混用。
- 移動命令發目的地/動作意圖，Server 回傳移動段 start/end、速度/時間、方向、序列；遠端按權威 Server 時間插值，舊資料不能覆寫新 Stop/Teleport/Death。
- **Facing**：獨立 DirectionState（支援原版必要方向數、優先由方向查核決定），與 locomotion / action animation 分開；站立/攻擊/受擊維持正確面向。
- **Animation**：Locomotion Loop 與 OneShot(Attack/Hit/Death) 分開，明確優先級，晚到 Move 不能重啟 OneShot。技能事件有唯一 ActionId，傷害事件有唯一 CombatEventId。
- **AOI**：Server 空間格與滯後區域、View 角落動態半徑、新人先 Spawn Snapshot 後 Delta；離開/換圖後舊事件不得復活 Entity。
- **Queue**：時效性 Move 可依 Entity / epoch 取最新值且保留 Stop 屏障；Attack/Hit/Death/Trade/Drop/Pickup、Teleport、Spawn/Despawn 等不可任意合併或丟棄。
- **資料庫**：位置快取批量持久化並在地圖遷移/登出 flush；資產交易強一致並具冪等鍵；不能為流暢而跳過交易保全。
- **強化物品**：取消 +1/+2 變種 ID；初始匯入統一生成新連續 ID，舊碼→基本種類與強化級別映射；物品定義 ID 正式發版後維持不變。

## 5. 效能/可靠性/可維護需求

- R001 同場景下應不劣於舊 Godot：效能與正確性對照，目標 Login RAM <=100MB、World RAM <=200MB、Logout RAM 回到 Login 基線+10%、常態60 FPS；這些是待驗證的**期望**，不保證可達。
- R002 20 玩家+300怪+大量掉落，人物 move/display 視覺不抽動；Network E2E movement P95 目標 <=150ms（本機壓測）、高壓尾延遲/突刺另記 P99；百分位定義與場景需固定。
- R003 300 怪 AI 在同一 Map 的 tick 不隨玩家數線性增加；AI/觸發/運動固定步階解耦，重型尋路非同步並有版本校驗。
- R004 Client Node/Texture/Animation/RenderTarget 分析與卸載；登出/切圖/掛機不遺漏資源。
- R005 100/500/1000 Bot 分階段容量探索只做 Release Gate，不先假定單服能承受；失敗不能靠降戰鬥速率「達標」。
- R006 初版採模組化單體；每系統含 Domain/Service/Handler/Store Interface/Tests，避免全域狀態互相讀改。
- R007 多語系、設定、編輯器、事件、儲存格式資料驅動且有 Schema/版本/檢查工具。
- R008 安全：TLS、身分/授權、速率限制、協定版本、重放保護、審計、登入與資產驗證。
- R009 Godot 渲染與邏輯全分離：FPS/低畫質/AFK/失焦不減收包、遊戲命令、戰鬥、撿物頻率。

## 6. 里程碑（每個 Gate 必須附驗證）

M0 資料與原理確認：Old 原始反編譯中的移動、方向、動作/攻擊與地圖/物品碼查核；新舊差異矩陣；授權確認；ID 緊密重編設計測試。

M1 基礎工程：Server/Client/shared/contracts/game-data/tools/tests，版本化 Wire，CI Build+Unit+Godot headless 可用。

M2 可玩切片：多人進圖/移動/方向/普攻/傷害/怪/掉落/撿取/切圖，至少 2 真人 Client 互相看見。

M3 大量同步：20/50 Bot +300怪+500掉落，在 1400×900 / 1920×1080 / 超寬/Zoom/邊緣無瞬移、正確受擊方向、死亡事件一致，P95/P99 具體可追。

M4 優化低資源：0/20/50/100人、不同畫質、登出切圖重複、長時間 soak、RAM/VRAM/Frame P95 A/B。

M5 遊戲系統與資料轉換：物品、強化、商店、技能、任務、寵物、機甲、編輯器與正式備份。

M6 PVP、小屋、社交、多 Channel、部署與維運。

交付標準：每個 Gate 提交 commit、測試指令、CI URL、負載種子、資產清單、環境、測量值、已知風險。未執行=NOT VERIFIED。