# Architecture Decision Records (ADR) — 初始版

狀態語意：ACCEPTED(已決定)、PROVISIONAL(暫用需量測)、OPEN(尚待判斷)、REJECTED(附理由)。每項修改都要留 Date/Reason/Evidence/Impact/Next Test；不可默默改。

## ADR-001 — Godot Client + .NET Server
- Date: 2026-11-12；Status: ACCEPTED（可由實測改版）
- 決策：以 Godot 4 Client + C#/.NET 10 Server 建新專案；初版 SQLite，持久化 provider 可替換（見 ADR-015）。
- 原因：Godot 2D/UI/工具和 GitHub headless CI 成熟；Server .NET 可模組化、資料一致性、測試方便。
- 風險：大量 Godot Node/Texture/紙娃娃造成 RAM/CPU；需資料導向狀態和 RenderingServer 的漸進測試。
- 禁止：直接 Fork 或直接搬舊 Godot Client/Server 內部大型類別與歷史包。

## ADR-002 — 單 Map Actor 與資料庫隔離
- Status: ACCEPTED
- 每張 map/channel 的 mutable 世界由單一 Actor 持有；不在 MapActor 的鎖/熱路徑做 SQL 或 Socket IO；資產交易使用 SQLite 原子事務與操作冪等，透過 provider ports 隔離。
- 避免 Session/World 共享大鎖與 SQLite write contention 導致 250ms+ Tick Spike。
- 任務：B002, C001, D006。

## ADR-003 — Remote Player Server Timeline，而非接收間隔 Tween
- Status: ACCEPTED（參數仍 OPEN）
- Server 給具有 ServerTick 的 movement segment、correction/stop barrier、Facing、ActionSequence；Client 使用插值/有限外推。玩家與戰鬥事件優先可靠、有因果。
- 初始 buffer 100–150ms/移動10–20Hz只是建議實測起點，不是原版已證明發包節奏，不宣稱適合所有網路。
- 任務：A002, C003, C004, C005, D003。

## ADR-004 — ItemDefinition 不含強化 +N 變體
- Status: ACCEPTED（解碼規格尚 OPEN）
- 所有裝備個別 ItemInstance.enhancement_level；Old +1/+2 變體轉 alias，初次 import 移除變體定義，後方定義 ID 前移，從1密集編號。
- 初版發布前可重算，發布後不可重排；衝突 unknown quarantine。
- 任務：A003, A004, F001。

## ADR-005 — 低資源階梯式
- Status: ACCEPTED
- 先做 Godot DataState / 少 Node / shared Atlas / visible pool / chunk streaming，再測 RenderingServer／批次；僅可追溯 profiling 熱點才考慮 C#/GDExtension，不以「重寫為 C++ 一定更快」臆測。
- 任務：B001, E001~E006。

## ADR-006 — 網路協定 codec
- Status: OPEN
- 候選：MessagePack、Protobuf；需比較 payload size、C#/GDScript decoder 可用度、allocation、schema evolution/CI coverage、版本兼容。初版 Transport WebSocket over TLS。
- 切換格式不可改 Domain action/sequence semantics；測實際20/100 bot。
- 任務：A006, B003。

## ADR-007 — 角色/怪/AI Tick 取樣率
- Status: PROVISIONAL
- 評估固定世界模擬20/30Hz、角色廣播10–20Hz、戰鬥中怪AI 5–10Hz、閒置AI 2–4Hz；關鍵 Event edge triggered，不允許 AI 調度阻塞玩家移動。
- 原版 Core 預設 server 10Hz update，舊Godot monsters 250ms AI，不能直接互換/視為實際發包速度。
- 量測後再定實際值；任務：A002, D001, D007。

## ADR-008 — 地圖格式與 Editor
- Status: OPEN
- 由 A005 Old Blocking/Map 真實解析與編輯器 A/B 決定是否選 Tiled 或專用工具。必須有相同 world→grid/wall collision 契約，不沿用老 800x600。
- 任務：A005, F006。

## ADR-009 — 舊版程式/素材權利
- Status: ACCEPTED（權利核實尚未完成）
- SGTest 在授權明確前不傳入 Old 原件/素材/反編譯程式碼到新 repo；只能在研究文件中引用可定位的行為/函式和自身撰寫程式。
- 任務：A001。

## ADR-010 — 僅 GitHub CI 不代表 GPU/實機通過
- Status: ACCEPTED
- GitHub headless 可驗證算法、封包、Scene 行為和流程；GPU FPS、VRAM/外觀與真實多人感受需固定 Windows GPU runner/本機測量，不能誤標 PASS。
- 任務：B004, C008, E005。

## ADR-011 — 後期《星夢 Online》金色 UI（2026-11-13）
- Status：ACCEPTED，已由使用者選定後期金黃色皮膚（非早期藍色版本）。
- Client 全部 Godot Control/Theme/.tscn 全新製作，保留 Old 後期的 HUD 排版與風格，不複製 TestGoGo 的 UI/Scene。
- 原始素材來源/使用權/解析需先確認：Old/ClientData 尚無實體 .itf；現階段先原創 Theme/Gallery，取得合法完整資產再做像素/互動比對。
- UI 不干涉地圖模擬、人物資料同步、網路連線與低資源模式的固定更新。
- docs/OLD_UI_DESIGN.md、docs/LEGACY_ASSET_PIPELINE.md 定義驗收門檻。

## ADR-012 — SPR 輸出佈局（2026-11-13）
- Status: PROVISIONAL；來源與實測：docs/HK2_SPR_POC.md。
- 決策：PoC 以原尺寸分幀 PNG 為預設，保留原始幀與 origin；每 SPR atlas 僅作實驗選項，不把全部怪物合成一張全域 atlas。
- 原因：20 個 HK2 怪物 SPR 樣本的 512 atlas 將 PNG 數從 136 降至 40、RGBA 像素面積由 18.5 MB 升至 19.2 MB；1024 atlas 20 張／22.2 MB，2048 atlas 20 張／27.4 MB。Godot headless 的 5 次 PNG 解碼中位數為 83.584ms→66.662ms，但不能推論 GPU VRAM 或實際幀時間。
- 影響／下次驗證：先解 ACT/SIF 的方向、動作、幀時間和圖層，再以 Godot 同場景比較分幀、每角色小 atlas、場景分組 atlas 的 RAM/VRAM/draw calls/frame P95 與載入／卸載成本；以可維護的明確分組規則選擇正式方案。
- 全量 HK2 保真輸出達 153,115 張 PNG；此為 QA 中間層，不應全部直接交給 Godot 專案匯入。正式 runtime 包裝需按實際同屏使用組合、資源生命週期與 GPU 基準另定。

## ADR-013 — 多解析度與雙 UI（2026-11-13）
- Status: ACCEPTED；來源：使用者明確要求原版相近 UI 加可切換現代排版，並超越原版 800×600 限制。
- 決策：後期金色風格與現代排版讀取同一份唯讀 ViewModel；Control/Container 的錨點、安全區和文字縮放適應視窗；AOI 由實際 viewport、zoom、位置和緩衝推算，不由 HUD 外觀或固定 800×600 決定。
- 影響／下次驗證：1400×900、1920×1080、21:9、最大化、高 DPI 與縮放場景檢查 UI 遮擋、地圖邊界 Spawn/Despawn、多人／怪物／掉落負載；切換 UI 不改網路或世界 Tick。

## ADR-014 — 碰撞與實體渲染的實作選擇（2026-11-13）
- Status: PROVISIONAL；來源：使用者要求多人／多怪／多掉落、低資源及即時動作優先，允許不用 Godot 內建碰撞等功能。
- 決策：Server 權威碰撞以地圖資料與 MapActor 的批次／空間索引為基礎；Client 不把每個怪物和掉落物都強制建成 Physics/Area2D Node。內建與自製方案以相同可見量、碰撞結果、點擊命中及 RAM/CPU/Frame P95/P99 A/B 選擇。
- 驗收：20 玩家、300 怪、500 掉落、持續移動／受擊／拾取與超寬／zoom；正確性與事件時序不得因渲染優化下降。

## ADR-015 — SQLite 初版與可替換持久化（2026-11-13）
- Status: ACCEPTED；來源：使用者指定先用 SQLite，未來換其他 SQL 必須容易維護。
- 決策：Domain/Application 只依交易語意明確的 repository ports；`Infrastructure.Sqlite` 持有連線、WAL、migration、SQL 與 busy/retry 策略。MapActor 的即時座標/AI/戰鬥狀態在 RAM，磁碟寫入經 actor 外的持久化工作流；物品 UID、金錢、強化、交易與 Outbox 在同一 SQLite transaction 原子提交。
- 相容條件：provider contract tests 驗證交易守恆、冪等、唯一性、併發致死／拾取、斷線重試；未來 `Infrastructure.Postgres` 需通過同一測試及 migration／資料搬遷驗證後替換，不靠 Domain 判斷 SQL 方言。
- 性能風險：SQLite 單寫入者在多怪持續傷害與位置保存下可能爭用；需量測 write gate、busy、commit P95/P99，保留權威順序，不用延遲 HP/資產提交偽造低延遲。

## ADR-016 — Map-aware Shared Asset Pool / Adaptive Multi-Size Atlas
- Date: 2026-11-13；Status: **PROVISIONAL（DESIGN ONLY）**。
- Decision: 正確解碼後使用 canonical Frame + Metadata、deterministic 多尺寸 Atlas（512/1024/2048/4096 候選）、按 Map/Region co-occurrence、Monster/Drop 視覺種類重用、hot/warm/cold、材質與動畫用途分群，執行時引用計數/有界串流/回收。
- Reason: 同圖實體數雖多但種類通常少；多個 Entity 應共享 Texture/FrameTable，而不是大量重複 Node/Texture。降低 GPU/RAM 工作集並保留原版動畫事件/圖層。
- Alternatives: 個別 PNG/SpriteFrames、單尺寸 Atlas、所有素材一張 4096、預合成所有玩家裝備、MultiMesh；不能未測就否決。
- Risks: Y-sort、紙娃娃前後層、padding bleed、頁過大導致 VRAM 浪費、首次 upload spike、跨圖 cache churn。
- Evidence: 乾淨 HK2 已可全量轉換；三組 512～4096 本機 Godot 初測見 [HK2_ATLAS_CANDIDATES.md](HK2_ATLAS_CANDIDATES.md)，但多種類／掉落／快取與完整 AS007 尚未驗證。Next: AS007/AS-T01～T05。詳 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md)。

## ADR-017 — Client Network/State 與 UI/Render FPS 排程隔離
- Date: 2026-11-13；Status: **ACCEPTED CONTRACT / IMPLEMENTATION NOT VERIFIED**。
- Decision: ServerSimClock、ClientNetworkPump、CommandGateway、OrderedEventReducer、PackedWorldState 與 ViewProjection、WorldRenderer、UIRenderer、VisualClock 完全分責。UI FPS 增減、Render FPS、失焦、最小化、AFK **不得提高/降低 Client↔Server 收送包、解碼/State 套用、戰鬥、移動、撿物及 Server Tick 的工作率**。
- Rejected: 依 `_process(delta)` 的渲染 callback poll socket、以 UI Animation frame 送 Move/Skill、AFK 停止處理 Server events。
- Risks: Godot/OS 背景 throttle、queue backlog、worker Thread 不安全地觸碰 SceneTree、GPU upload 卡住主執行緒。
- Verification: 同 packet trace 30/40/50/60/120 + UI 限速 + 失焦/最小化/AFK，RX/TX/State hash 與 QueueAge P95/P99；CI-T01～T04 以及實機 GPU 延遲。詳 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
