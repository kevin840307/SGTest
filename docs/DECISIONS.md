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
- 2026-11-14 使用者補充：視窗**最大寬度約 1440 像素**，不是 1440p；預設 1280×720，提供 800×600、1024×768、1366×768、1440×810、1440×900 與拖曳調整。UI/文字縮放與世界鏡頭縮放分開；先前 1080p/4K A/B 僅作超規壓力研究。
- 影響／下次驗證：以上尺寸、高 DPI 與縮放場景檢查 UI 遮擋、地圖邊界 Spawn/Despawn、多人／怪物／掉落負載；切換 UI 不改網路或世界 Tick。

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
- Date: 2026-11-13；Status: **PROVISIONAL（部分 Godot 實測，正式 runtime 未驗證）**。
- Decision: 正確解碼後使用 canonical Frame + Metadata、deterministic 多尺寸 Atlas（512/1024/2048/4096 候選）、按 Map/Region co-occurrence、Monster/Drop 視覺種類重用、hot/warm/cold、材質與動畫用途分群，執行時引用計數/有界串流/回收。
- Reason: 同圖實體數雖多但種類通常少；多個 Entity 應共享 Texture/FrameTable，而不是大量重複 Node/Texture。降低 GPU/RAM 工作集並保留原版動畫事件/圖層。
- Alternatives: 個別 PNG/SpriteFrames、單尺寸 Atlas、所有素材一張 4096、預合成所有玩家裝備、MultiMesh；不能未測就否決。
- Risks: Y-sort、紙娃娃前後層、padding bleed、頁過大導致 VRAM 浪費、首次 upload spike、跨圖 cache churn。
- Evidence: 乾淨 HK2 已可全量轉換；單外觀初測見 [HK2_ATLAS_CANDIDATES.md](HK2_ATLAS_CANDIDATES.md)，8 種怪／20 玩家代理／500 小圖代理及 10 次貼圖釋放見 [HK2_MIXED_ATLAS_BENCHMARK.md](HK2_MIXED_ATLAS_BENCHMARK.md)。真正掉落圖、多人同步、正式快取及完整 AS007 尚未驗證。Next: AS007/AS-T01～T05。
- 2026-11-14 資料契約補充：以來源 SHA、ACT 動作／索引及專用怪物方向映射編譯局部 Atlas catalog；引用不完整的群組隔離，保留原始時間、事件和方位鏡像，不推測毫秒或視覺校正。Godot headless 實證 8 群組可讀，詳 [HK2_ENEMY_ATLAS_CATALOG.md](HK2_ENEMY_ATLAS_CATALOG.md)。
- 2026-11-14 離線視覺檢查補充：怪物 ACT Viewer 以 catalog 選動作／方位，只解碼目前引用的頁面，保留原始索引、事件與時間欄位；固定 100ms 僅供檢查，不能回寫為動畫定義。詳 [HK2_ASSET_VIEWER.md](HK2_ASSET_VIEWER.md)。
- 使用者補充可接受每物品／怪物各自圖片，條件是效能與資源實測勝出。現有候選以**每怪物外觀一個獨立群組**、其他單一 SPR（含本次 MS 代理）各自分組，群組可能因尺寸分成多頁；沒有跨怪物合成一張全域圖。正式決策需以同一可見工作集比較逐來源貼圖、單外觀頁面與地圖共同使用的局部頁面，兼看維護成本、記憶體與載入尖峰。
- 2026-11-14 真正地上物品圖 `Interface/ground.spr` A/B：500 實例／25 或 500 種同屏，逐圖 25／500 張與局部 1024 單頁對照，draw calls 約 572→73；500 種載入中位約 162→36ms，Godot Video Mem monitor 約 21.3→23.5 MB。2048／4096 在此集合無收益。暫選地圖共現物品用 1024，單一稀有圖可獨立；正式 item ID 對應及真地圖工作集待查，詳 [HK2_GROUND_ART_BENCHMARK.md](HK2_GROUND_ART_BENCHMARK.md)。
- 2026-11-14 物品圖號補充：`item.itd` 9,096 筆已由 HK2 2009 `Client.bin 0x004F4ECC～0x004F4F54` 的 `ground%d.spr`／÷500 直接規則驗證，8,049 筆→`ground.spr`、1,047 筆→`ground1.spr`、0 越界。13 個 duplicate ID 中 12 個有不同地上圖；參考 `GetItemByID` 使用 CRT `bsearch`，不足以建立 first/last precedence，故衝突保持 quarantine。正式掉落 palette 應同時涵蓋 ground/ground1，詳 [HK2_ITEM_LIBRARY_MAPPING.md](HK2_ITEM_LIBRARY_MAPPING.md)。

ADR-016 的逐怪物尺寸實驗補充（2026-11-14）：50 組各測 1024／2048／4096，採最低實際 RGBA 的可重現候選，得到 42／4／4 組；保留逐組解碼時間供覆核。正式 runtime 尚未驗證。見 [HK2_PER_ENEMY_ATLAS_SIZES.md](HK2_PER_ENEMY_ATLAS_SIZES.md)。

## ADR-017 — Client Network/State 與 UI/Render FPS 排程隔離


- Date: 2026-11-13；Status: **ACCEPTED CONTRACT / IMPLEMENTATION NOT VERIFIED**。
- Decision: ServerSimClock、ClientNetworkPump、CommandGateway、OrderedEventReducer、PackedWorldState 與 ViewProjection、WorldRenderer、UIRenderer、VisualClock 完全分責。UI FPS 增減、Render FPS、失焦、最小化、AFK **不得提高/降低 Client↔Server 收送包、解碼/State 套用、戰鬥、移動、撿物及 Server Tick 的工作率**。
- Rejected: 依 `_process(delta)` 的渲染 callback poll socket、以 UI Animation frame 送 Move/Skill、AFK 停止處理 Server events。
- Risks: Godot/OS 背景 throttle、queue backlog、worker Thread 不安全地觸碰 SceneTree、GPU upload 卡住主執行緒。
- Verification: 同 packet trace 30/40/50/60/120 + UI 限速 + 失焦/最小化/AFK，RX/TX/State hash 與 QueueAge P95/P99；CI-T01～T04 以及實機 GPU 延遲。詳 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。

## 人物素材研究補充（2026-11-14）

原版 draw-order 的 +2 token 會展開為兩個有序繪圖槽位；素材資料契約必須同時保留原始 token 與展開序列，不能套用物品強化別名清理規則。兩份主要人物表 102 行已稽核，貼圖來源與實際遮擋仍待驗證，詳 [HK2_DRAW_ORDER.md](HK2_DRAW_ORDER.md)。

## ADR-018 — Per-SPR 動作方向局部性候選（2026-11-14）

- Status: **EXPERIMENTAL / NOT SELECTED**。保留現有 dense atlas，不直接改正式策略。
- Decision: 新增可選 `--source-locality`，按原始 SPR 各自 pack，保留 ACT 原影格、來源 SHA、方位重用與鏡像；再用 `audit_action_page_locality.py` 定量測同動作/方位的跨頁數。若單 SPR 大於頁面仍允許多頁並明確報告，不宣稱一定同頁。
- Evidence: 舊 50 組 adaptive catalog 有 173/2,000 個動作×方向跨頁；真 HK2 EN023A 15 個 SPR 合成 15 頁，40/40 同頁、Godot headless 272 播放步 PASS；單組離線 RGBA 18,816,668 bytes。資料與執行方式見 [HK2_ACTION_PAGE_LOCALITY.md](HK2_ACTION_PAGE_LOCALITY.md)。
- Risks/Next: 單獨來源頁可能增加 texture binds/draw calls；須對同場相同實體做 GPU/RAM/載入/切圖 A/B，再決定混用策略。授權、真視覺及多人尚未驗證。

## ADR-019 — Data1 archive base + loose override 虛擬素材層（2026-11-14）

- Status: ACCEPTED FORMAT / RUNTIME MATERIALIZER PROVISIONAL。
- Decision: 素材來源不再把 loose 目錄視為完整真相。依原版 xnFileRead 行為，虛擬檔案系統 precedence 固定為 loose path override > opened group archive base；工具以 case-insensitive path identity 比對，但 manifest 保留實際來源檔名與 SHA。
- Direct evidence: xnFileOpenGroup .text:00552980、FileData::ReadBuf .text:00551810、LzssDecompress .text:0055A0E0、xnFileRead .text:00552090。HK2 Data1.dat 實證 41,687 entries，詳 HK2_DATA1_ARCHIVE.md。
- Implementation: legacy_group_archive.py 只讀索引／LZSS／選擇性抽取；materialize_legacy_overlay.py 只 materialize 指定 pattern/palette 到 gitignore 私有目錄。禁止把原始／衍生媒體提交公開 Git。
- Evidence: 17 張已知 Enemy.dat 地圖共 29 visual；materialize 464 files，其中 461 archive base、1 loose override、2 loose-only；29/29 ACT audit complete。

## ADR-020 — Map-aware Enemy Atomic 1024 Runtime Candidate（2026-11-14）

- Status: PROVISIONAL SELECTED FOR ENEMY VISUAL PIPELINE；正式 Client streaming 仍未實作。
- Decision: 已知地圖怪物以 MapAssetPalette 決定預載 visual，單 visual 預設 atomic MaxRects 1024。每個原 SPR block 不拆、不同 block 可 co-pack；logical facing 的鏡像只存在 metadata/runtime，不複製像素。只有 1024 無法保持 locality 時才依量測升 2048。
- Evidence: Data1+loose 的 29 種 known-map visual，atomic 1024 共 32 pages／62,287,496 RGBA bytes，1,160/1,160 action-facing 單頁；17 張地圖工作集約 3.305～17.441 MiB。Map13 78/300 monster RTX3050 A/B 的 draw calls 與 frame P95 未退化，Video/Texture memory 比 dense 約少 1 MB。詳 HK2_MAP_ASSET_PALETTE.md。
- Constraint: 不把此策略直接套用 Player 紙娃娃、掉落或 UI；各類型仍需自己的 A/B。全世界素材一次常駐仍被拒絕。
