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
- Evidence: 17 張已知 Enemy.dat 地圖共 29 visual；materialize 464 files，其中 461 archive base、1 loose override、2 loose-only；29/29 ACT audit complete。`scan_group_archives.py` 另對目前 HK2 41,741 個實體檔做 fail-closed footer + LZSS index 掃描，只有 Data1.dat 1 個 valid archive、0 rejected candidate；因此目前工作集沒有第二個同 xnFile group 格式 archive，但不把此結果擴張成原始發行包 provenance 結論。

## ADR-020 — Map-aware Enemy Atomic 1024 Runtime Candidate（2026-11-14）

- Status: PROVISIONAL SELECTED FOR ENEMY VISUAL PIPELINE；正式 Client streaming 仍未實作。
- Decision: 已知地圖怪物以 MapAssetPalette 決定預載 visual，單 visual 預設 atomic MaxRects 1024。每個原 SPR block 不拆、不同 block 可 co-pack；logical facing 的鏡像只存在 metadata/runtime，不複製像素。只有 1024 無法保持 locality 時才依量測升 2048。
- Evidence: Data1+loose 的 29 種 known-map visual，atomic 1024 共 32 pages／62,287,496 RGBA bytes，1,160/1,160 action-facing 單頁；17 張地圖工作集約 3.305～17.441 MiB。Map13 78/300 monster RTX3050 A/B 的 draw calls 與 frame P95 未退化，Video/Texture memory 比 dense 約少 1 MB。詳 HK2_MAP_ASSET_PALETTE.md。
- Constraint: 不把此策略直接套用 Player 紙娃娃、掉落或 UI；各類型仍需自己的 A/B。全世界素材一次常駐仍被拒絕。

## ADR-021 — Enemy Full-Catalog Atomic Measured Multi-Size（2026-11-14）

- Status: **PROVISIONAL SELECTED BUILD STRATEGY / RUNTIME MAP STREAMING REQUIRED**。
- Decision: 完整 Enemy catalog 的 build-time atlas 使用 `source_spr_atomic_maxrects_v3`，並由 `plan_enemy_atomic_sizes.py` 對 512/1024/2048/4096 真實 packing 面積選尺寸；預設先最低 RGBA，再允許 5% 記憶體 overhead 換較少頁。每個原 SPR 是不可拆 atomic block，但不同 SPR block 可 co-pack；ACT logical facing 的鏡像只保留 metadata。
- Evidence: 50 組計畫為 512×1、1024×21、2048×19、4096×9，共 65 pages／442,769,264 RGBA bytes，2,000/2,000 action-facing 單頁；Godot probe 50 groups／250 actions／2,000 directions／12,976 steps PASS。RTX3050 1440×900、300 怪：8 種時 pages 12→9、draw p95 286→275；50 種壓力 draw p95 372→372。詳 [HK2_ATOMIC_BALANCED_ATLAS.md](HK2_ATOMIC_BALANCED_ATLAS.md)。
- Runtime boundary: 不允許全 50 種常駐；ADR-020 MapAssetPalette 仍決定實際地圖工作集。完整 catalog 的全量 Video Memory 僅為壓力參考。Player／ground item／UI 必須各自驗證，不能照抄 Enemy 策略。

## ADR-022 — Animation timeline 使用 verified 50Hz ACT timing，與移動/render FPS 解耦（2026-11-14）

- Status: **ACCEPTED DEFAULT-TIMING CONTRACT / DYNAMIC SPEED PARTIAL**。
- Decision: HK2 ACT default timing 採 `max(1, floor(raw_time * 40 / 25)) * 20ms`；預設 HK2 raw_time>=1 時與原公式相同。50Hz/20ms fixed-update、40/25、decrement、`xnAction::SetSpeed` 與 `SystemSetFrame(50)` 均由 HK2 Client.bin / SGOClient.bin byte audit 驗證。Runtime 保留 raw_time/ticks/default-ms；dynamic speed 使用同樣 integer division + minimum one fixed tick。
- Rendering rule: Animation timeline 不由 Render FPS、UI FPS 或 entity world movement speed 推進。30/60/120 FPS 只取樣同一時間軸；角色跑得快、機甲位移快都不能因此讓 walk/attack/hit 動畫自動加速。只有明確 action-speed 語意可改 timeline rate。
- Evidence: `tools/audit_action_timing_binary.py`、[HK2_ACTION_TIMING.md](HK2_ACTION_TIMING.md)；178/178 ACT schema v3 PASS，50-group Godot 12,976 playback steps `timing_verified=true`。
- Remaining: 人物 numeric action ID、HK2 action-speed caller/packet/data-driven 語意、ACT event callback、AS005 原版逐格畫面。

## ADR-023 — World Movement Speed 與 ACT Action Speed 完全分離（2026-11-14）

- Status: **ACCEPTED RUNTIME CONTRACT / HK2 CALLER MAP PARTIAL**。
- Decision: 角色／怪物／寵物／機甲的世界位移速度不可自動改變 ACT playback rate。ACT speed 只能由明確 action-speed 語意改變；Render FPS 同樣不得乘入 action timeline。
- Reference evidence: `CSGOChar::SetSpeed .text:004BF3B5` 只寫 `[this+0x17C]`，沒有呼叫 action speed；`CELxnObj::SetActionSpeed .text:00541BF0` 才呼叫 `xnAction::SetSpeed`。CDDxDrawObj 與 CELxnObj vtable 都以 `+0x2C` 指向 SetActionSpeed。保守掃描「附近 InitAction(+0x24) → push immediate → SetActionSpeed(+0x2C)」得到 31 個 sequence／28 個函式，immediate 值全部為 50。
- HK2 evidence: Client.bin / SGOClient.bin 各唯一命中 `xnAction::SetSpeed` signature；dynamic duration 使用 integer division，counter=0 仍至少等待下一個 20ms fixed update。
- Implementation rule: 不得用 `entity_speed`、路段速度、機甲速度或 interpolation velocity 去設 `AnimationPlayer.speed_scale`。若未收到/建立 explicit action-speed，使用 verified ACT default timing。詳 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。
- Remaining: HK2 caller 函式級 mapping、packet/data-driven non-immediate speed、人物 action mapping、event callback。

## ADR-024 — main 整合、獨立 Godot Client 與 C#／.NET 8 Server（2026-11-14）

- Status: 使用者明確指定，實作與本機測試。
- Decision: 在 `C:\SGO\SRC\SGTest` 直接修改 main，保留 master Git 歷史並合入程式；不另建 worktree。client 放 Godot 專案，server 放獨立 C#／.NET 8（覆蓋舊文件 .NET 10 選型）。
- Server 移植既有權威模擬，無 Godot／圖片依賴；WebSocket + bounded MessagePack v1。Client I/O worker 與渲染分開；完整 FPS／失焦 trace Gate 仍 NOT VERIFIED。
- Conflict resolution: 保留 main 的 tools/convert_assets.py、inspect_act.py 與研究工具；runtime decoder 改名 runtime_spr.py。新工作目錄原有未提交 ACT／人物研究檔案保留，不覆蓋或夾帶進本次 merge commit。
- Evidence: C# domain 342 檢查、Godot wire fixture／狀態／預測／方向／地圖測試；8 client／1000 monster／5000 drop，及 2-client combat-arena 與原生 1440×900 測試見 reports。
- Migration: root/project.godot 移到 client/project.godot；Godot Server 移除，以 dotnet 啟動；生成 Client assets 與 Server collision 分開。M0 視覺／授權缺口不因此宣稱已完成。

## ADR-025 — Map13 原版出生與多種類圖集（2026-11-14）

- 使用 HK2 圖像與 Recovered/Old 原始服務端 Dat 的既有解析器，將78出生／9visual編譯為本機population.json；保留原始ID、位置、來源旗標與SHA，戰鬥數值仍是原型預設，不假稱完整原版怪物規則。
- Server 預設原版出生，明確`--monsters=`才用壓測數量；死亡復活回Home並保留Definition。Client只載入當前地圖9圖集，同種共用，重置地圖時清空；預載每個種類後讓出一個影格。
- Wire v2：28-byte snapshot新增definitionId，Action亦帶definition，World/Ready增加population SHA。v1明確拒絕；跨語言fixture同步更新。
- 驗證：C#346項檢查、Python合成population fixture、Godot9visual／360 action-facing／共享生命周期、8-client1000怪5000掉落30秒、2-client戰鬥與原生1440×900壓測PASS。多種類圖集RGBA16,424,596bytes，原生預載約121ms（包含分影格讓出），平均58.50FPS（60cap）、P9523.50ms。原始媒體／population只留本機。
- 下一步：使用者補充圖集橫直排法需更一致；將另以可重跑腳本與尺寸／載入／記憶體A/B決定，不任意旋轉或拉伸原影格。

## ADR-026 — 低功耗多開只調呈現，保留獨立同步節奏（2026-11-14）

- Status: ACCEPTED REQUIREMENT；完整實作／實機矩陣 NOT VERIFIED。
- Decision: Render FPS、Client固定邏輯／network與C# .NET 8 Server模擬分離。5／10／15FPS及不繪製世界的掛機模式只降低呈現與音效；Server30Hz、snapshot10Hz、Client input30Hz不變。背景／最小化需實測，不能只改Godot FPS上限。
- Evidence: Socket worker已獨立，但main物理回呼仍消化state／command，render回呼仍送時鐘ping；因此尚未完成隔離。使用者觀察頓挫，根因未定位，不以平均FPS宣稱正常。
- Validation: SPEC §0.4的同trace因果、實際頻率與queue門檻；5／10／15FPS掛機及30～90FPS一般遊玩／背景恢復、1／2／4／8開CPU／RAM／GPU、各角色連續位移。先建立PERF001證據，依缺口完成獨立排程，再驗收；不藉降同步頻率通過效能目標。

ADR-026補充：使用者指定Client一般遊玩支援30～90FPS；FPS選項只控制呈現，傳輸與邏輯頻率維持既有獨立契約。120FPS可額外壓測，但不作正式支援範圍。

## ADR-027 — PERF001基準保留失敗，暫不選定排法（2026-11-14）

- Status: BASELINE DELIVERED；ASSET201～205與RUNTIME206未完成。
- Evidence: [RESOURCE_BASELINE.md](RESOURCE_BASELINE.md)及reports/runtime_baseline的7組30次lifecycle／4組20client-300monster-1000drop實際JSON；版本、工作樹與PNG指紋已記錄。7組raw texture references與RSS／engine texture回收PASS，載入frame P99均超過40ms。4組一般場景約60FPS／frame P99 22.76～24.93ms，但原生Client RSS峰值202.65～207.73MiB，200MiB目標FAIL。
- Decision: 不把decode／submit平均值或較少PNG當作無頓挫證據；先做標準動畫定義，再測clip原子／map共現／hot-warm-cold混合分組與有界非同步資源管理。當前9頁entity、45頁direction及13頁readable都只保留為基準候選，4096不預設勝出；2048與4096候選可能裁切成同尺寸，單次時間差不能證明尺寸因果。
- Limits: raw texture loop不是實際map/logout owner refs／LRU驗收；一般場景1原生＋19headless不是多GPU視窗掛機。冷磁碟、實體driver VRAM、tick work長尾、完整動作／背景trace仍NOT VERIFIED。不降低目標或同步頻率來隱藏失敗。
- Validation: 7項基準判定／實驗packing fixture、89項現有研究工作樹測試、5項原型assets／4 maps／1 population、C#346 checks及Godot方向／navigation／replica／prediction／wire／9visual360動作方向測試PASS。CI另行確認，工作樹研究修改未夾帶提交。

## ADR-028 — Normalized Animation Definition v2 固化 z-order（2026-11-14）

- Status: **ASSET201 ACCEPTED / RUNTIME ATLAS MIGRATION NOT YET IMPLEMENTED**。
- Decision: 動畫語意改以 `sgtest.animation` schema v2 為 pack-independent source of truth。每個 entity→behavior→8 facing 明確保存 source clip alias、mirror、frame source/index、pivot/trim、ACT timeline/event；character facing 另外必須保存 legacy `draw_order_key`、raw `draw_layers` 與已驗證展開的 `draw_slots`。Atlas page/UV/streaming handle 不得寫回此契約。
- Versioning: v1 不包含必要 z-order 契約，因此 Python validator 與 Godot consumer 都明確拒絕 v1；不做 silent fallback。未來增加非必要欄位可向後相容，但任何會改播放/疊層語意的變更必須升 schema。
- Evidence: 真素材 normalized audit 9 Map13 monster + Boy 6 core parts + explicit death＝16 definitions／992 logical facings；624 character draw-order views 全部 `normalized_z_order_verified=true`，0 failure。另重跑四種族 core audit：A/B/G 各 282 sources、R 235 sources，各 104 logical states、0 issue、0 missing draw-order。
- Regression: `tools/tests` 89 PASS；`tools/test_animation_definition.py` 10 PASS；Godot `client/tests/test_animation_definition.gd` PASS。舊 runtime 與 reference-normalized mapping 有 182 個 facing differences，保留報告並禁止靜默沿用。
- Boundary: 這只完成 ASSET201 資料契約，不代表 AS005 原版逐格畫面、AS008 授權、ASSET202 Atlas 選型、ASSET204 Runtime store 或正式 migration 已完成。詳 [NORMALIZED_ANIMATION_DEFINITION.md](NORMALIZED_ANIMATION_DEFINITION.md)。

## ADR-029 — v2 clip-atomic Atlas 選型與 async loader 邊界（2026-11-14）

- Status: **ASSET202/203 ACCEPTED / ASSET204 REQUIRED**。
- Decision: Map13 monster Atlas 必須以 `sgtest.animation` v2 為唯一動畫語意來源；physical SPR clip atomic，512/1024/2048/4096 只改 page/rect。候選 builder 必須在寫出前模擬目前 EntityAtlas direct/alias resolution，避免實體但非 canonical 的方向檔凌駕已驗證 alias。
- Workload: Map13 78 spawns、9 visuals，依累積共現頻率分 hot6/warm2/cold1；產生 7 組 fixed-preference/memory/balanced/pages 候選並使用 PERF001 lifecycle + 20-client/300-monster/1000-drop 同場 A/B。
- Selection: clean finalists 選 `normalized_memory` 作 ASSET204 provisional base：14 pages／10,741,992 RGBA bytes；clean multiplayer P95/P99 21.54/23.81ms、draw P95 421、Video memory 約45.63MiB，texture/RAM recovery PASS。
- Rejected as final fix: 只調 Atlas packing。三個 clean finalists 的同步 lifecycle frame P99 均 >40ms；normalized_memory 為53.52ms，因此 packing alone 不解決載入頓挫。
- Consequence: ASSET204 必須 background decode + bounded main-thread GPU commit，並保留 owner refs、generation cancel、cache cap/LRU、切圖/登出 purge；完成前 `normalized_memory` 只是基底，不是 final runtime。
- Evidence: [NORMALIZED_ATLAS_CANDIDATES.md](NORMALIZED_ATLAS_CANDIDATES.md)。原始 A/B JSON/PNG 位於 gitignore 的 reports/，不提交媒體。

## ADR-030 — 原版方向語意與 Remote Motion 呈現分離（2026-11-14）

- Status: **ACCEPTED RUNTIME CORRECTION / FULL VISUAL MATRIX NOT VERIFIED**。
- Problem: Server 八方位數值本身已與 Recovered `6,7,8,1,2,3,4,5` 一致，但舊 `EntityAtlas` 會讓同名 physical clip 優先於 action-specific DirectionMapping；remote snapshot 又直接覆寫 facing，且 attack/hit active 時 position 直接 snap 到 authoritative endpoint。Server AOI 900px 亦小於 1440×1080 + cull margin 的約 1078px 可視角落。
- Decision: `sgtest.animation/v2 logical_views` 成為 Runtime requested-facing 的第一優先；physical clip 只承載像素。Client state 分 authoritative `facing` 與 presentation `visual_facing`；walking 由實際位移向量 + 28° hysteresis 決定畫面方向，可靠 action event 取得 one-shot facing 所有權。Attack/hit 不再當 position teleport barrier；spawn/death/respawn 仍是 barrier。
- AOI: bounded interest 改 enter=1100px、already-seen exit=1250px；Player/monster/drop/action event 共用 hysteresis，避免畫面邊緣 Despawn→Respawn 順移，不改成全圖廣播。
- FPS: `application/run/max_fps=60` 為預設 Render cap；Client physics/input 與 Server simulation 保持 30Hz，FPS 不得改 network/state/combat cadence。
- Direct evidence: Character `DirectionMapping .text:004C263F～004C283A`；Enemy `SetDirection .text:004CB708～004CB7E8`；Recovered `DIR_STRINGS`、monster 28° presentation hysteresis、remote-player action-facing ownership/dead-reckoning 行為。詳 [RUNTIME_DIRECTION_AND_MOTION.md](RUNTIME_DIRECTION_AND_MOTION.md)。
- Verification: Python tools/tests 89 PASS；normalized/runtime semantic 16 PASS；C# 350 PASS；8 組 Godot headless tests PASS。原版同版本逐格人工視覺、完整 FPS/背景/多開 trace 仍 NOT VERIFIED。

## ADR-031 — Bounded RuntimeAssetStore 完成 ASSET204（2026-11-14）

- Status: **ASSET204 ACCEPTED / RUNTIME206 MATRIX NOT VERIFIED**。
- Decision: 正式 Client 共用單一 `RuntimeAssetStore`；manifest/PNG 在 worker thread decode，Godot texture 只在 main thread 建立。Production 每 frame 最多 commit 1 page，budget 1.2ms；ready queue 16MiB、unowned LRU cache 32MiB。Owner+epoch+request token 定義資源生命週期，切圖 generation 可取消 stale job，多 owner 共用同一路徑，failed unowned entry 必須移除以允許 retry。
- Consumers: Map13 monster 依 spawn count priority preload；Boy core paper-doll 6 parts+death 原子 ready；map object 依 viewport on-demand pin/release；headless client 不載純視覺 paper doll。Map switch/reset/Client close 都走同一 owner release/purge 契約。
- Evidence: `normalized_memory` 30-cycle 同素材同步 loader P99 53.52ms → async P95/P99 16.885/17.028ms，max 17.309ms；load P95 249.914ms / 15 frames，GPU commit P95 1.205ms；texture handle、engine texture、Store resident、process RAM recovery 全 PASS，final resident=0，provenance unchanged。
- Multiplayer: 20 clients／300 monsters／1000 drops／40s，mean 59.99FPS、frame P95/P99 22.109/23.949ms、draw P95 422，20/20 snapshot/prediction ready、0 ERROR、max correction 0。Server warm-up tick=300 P99 69.08ms；穩態 P99 5.56～6.43ms。
- Regression: owner sharing/cache hit、automatic cache-cap eviction、loading cancellation、failed-path retry、monster generation cancel、map-object viewport release、paper-doll atomic ready/purge 均有 Godot headless test。詳 [RUNTIME_ASSET_STORE.md](RUNTIME_ASSET_STORE.md)。
- Boundary: 這完成資源載入與生命週期 Gate，不代表 RUNTIME206 的 30/45/60/75/90FPS、5/10/15 low-power、unfocus/minimize、1/2/4/8 開 trace 已完成；實體 driver VRAM、完整 cold-disk matrix、AS005/AS008 也仍未完成。

## ADR-032 — Monster Recipe 與 Population v2 完成 ASSET205（2026-11-14）

- Status: **ASSET205 ACCEPTED / RUNTIME206 FULL MATRIX NOT VERIFIED**。
- Decision: 正式新增怪物的 source of truth 改為 `content/monster_recipes/*.monster.json`。Recipe 維護 definition id、sprite identity、runtime stats、spawn 與明確 replace 意圖；原始 SPR/ACT 仍 private。`tools/monster_workflow.py` 由 recipe fail-closed 驗證來源、5 actions×8 logical facings、stats 與 collision spawn，產 normalized definition、private 40-view preview、必要時 memory-first clip-atomic atlas、population v2 與 Server copy；不要求每新增一種怪修改 C#/GDScript。
- Population migration: v2 每 definition 明確保存 HP、Damage、MoveSpeed、AggroRange、AttackRange、ChaseStopRange、AttackInterval、Respawn；Server v1 仍 backward-compatible 並使用 legacy-equivalent defaults。Map13 base 已由原始 Dat 重建為 v2 9 definitions／78 spawns，預設值與舊硬編碼完全相同。
- Runtime: `World.Populate` 把 definition stats 套進 Monster instance；AI 移速、追擊/攻擊距離、攻擊間隔、傷害、respawn/MaxHp 全由資料決定，stress palette 也同步切換 stats。Client population parser 接受 v1/v2。
- Rebuild: `rebuild_assets.ps1` 在重建原始 base population 與 normalized semantics 後，依檔名排序重套所有 `*.monster.json` recipes，再 export Server content；因此 custom monster 不靠手改 generated JSON/PNG 存活。
- Evidence: 真 HK2 EN010A validate-only 16 source files／5 behaviors／40 logical views／spawn collision PASS／preview 768×495；無既有 atlas 時可自行產 runtime v9/schema v2 1 page 252×684。Temporary live-add definition81 後 Server 載入 79 monsters／10 definitions；20 clients/40s mean 59.998FPS、frame P95/P99 17.71/17.907ms、20/20 snapshot/prediction ready、0 ERROR，且 10 definitions 共用 9 atlases；Server穩態 tick P99 約0.26～0.29ms。測後已由原始 Dat 還原正式 v2 9/78。
- Boundary: Recipe v1 只涵蓋目前 monster 五動作與 prototype combat stats；特殊 Boss action schema、掉落/技能/任務/behavior tree 仍需獨立版本。完整 30/45/60/75/90FPS、5/10/15 low-power、失焦／最小化／恢復、1/2/4/8 開 trace 統一屬 RUNTIME206。詳 [MONSTER_CONTENT_WORKFLOW.md](MONSTER_CONTENT_WORKFLOW.md)。

## ADR-033 — RUNTIME206 Render/Logic/Network isolation accepted（2026-11-14）

- Status: **ACCEPTED**。
- Decision: Render FPS、Visual low-power、Unfocus、Minimize、no-world 只屬 presentation policy；Client fixed physics/input、socket pump、packet reducer、ServerSimClock 與 Server simulation 維持獨立30Hz。一般遊玩支援30/45/60/75/90FPS，預設60FPS；5/10/15為 VisualClock low-power。低於30的 Engine cap 會被 runtime guard 拉回30。
- UI: HUD「效能」提供30/45/60/75/90 FPS及 Unfocus 節能開關；Minimize 永遠停止世界呈現，不受 Unfocus toggle 影響，且不斷 network/state。
- Evidence: completion rerun 的30/45/60/75/90與Visual5/10/15/no-world同Server authoritative hashes全部一致；正式矩陣 queue P99 最差38.992ms。真Windows minimize/unfocus/restore與unfocus-off均PASS。1/2/4/8 native clients全PASS，8開physics 29.957～30.012Hz、queue P99 33.22ms、0 Server error。
- Burst/guard: 128 packet burst drain/decode 0.888ms；故意 `--max-fps 5` 仍維持約30Hz固定邏輯，queue P99 24.344ms。
- Boundary: physical driver VRAM、不同高刷新率顯示器上的實際75/90 render、120FPS額外壓測、cold-disk跨硬體矩陣不屬正式Gate；AS005逐格視覺與AS008發布權仍是獨立未完成Gate。完整證據見 [RUNTIME_ISOLATION_VERIFICATION.md](RUNTIME_ISOLATION_VERIFICATION.md)。


## ADR-034 — Runtime Atlas 分類收斂為 lifecycle bundles（2026-11-14）

- Status: **ACCEPTED**。
- Problem: 研究階段留下 per-entity、per-direction、per-action/state、paper-doll part、hot/warm/cold、readable/dense/source-locality、固定尺寸等多種分類；其中多數只是在比較 packing，不代表不同 Runtime lifetime。把研究維度直接暴露成 production category 會增加 manifest、decode jobs、owner refs、texture switches 與維護成本。
- Decision: Production 對外只用 ownership/lifetime 分組：**map / session / 真正 on-demand**。方向、動作、mirror、pivot、ACT、z-order 留在 sgtest.animation/v2；512/1024/2048/4096 只是 builder 內部候選。新增分類必須同時證明低頻、可獨立卸載、不同 owner lifetime，且 load/frame/draw/RAM/texture/video A/B 有收益。
- Map13: 9 monster definitions 改共用 bundles/map/13/sprites.json。1024/3頁 vs 2048/1頁 A/B：load P95 83.56→66.70ms、load frames 5→4；300 mixed draws P95 254→73；frame P95/P99 幾乎相同；texture/video peak 幾乎不變。4096 無頁數收益而拒絕。Production 選 2048。
- Player base: body/leg/head/hair00/l-arm/r-arm/death 改共用 bundles/session/player-b-base.json。1024/2頁 vs 2048/1頁：load P95 83.19→66.47ms、load frames 5→4、draw P95 123→73；frame pacing 不退化。Runtime z-order 改直接讀 normalized draw metadata，不再第二次解析 draw-order 表。
- Necessary split retained: map_objects 依 viewport pin/release，因為離開視野可真實獨立卸載；terrain 是單 map texture + viewport MultiMesh；drops 目前是單一 MultiMesh，沒有另立 texture-atlas category。
- Cleanup: 被新 bundles 明確取代的 Map13 九個 per-enemy + Boy base 七個 per-part generated manifests/pages 共刪 40 個檔；hair01 等尚未納入 base 的素材保留。Legacy builders 保留研究/回歸能力但標為 research-only，production rebuild 只走 asset_pipeline.py。
- Regression: 刪除舊 generated manifests 後 Python top-level 45 PASS、tools/tests 89 PASS、全部 tracked Godot Client tests PASS。Production map bundle 30-cycle async P95/P99 16.752/16.809ms、texture/engine/store recovery PASS；20client/300monster/1000drop/40s execution PASS，59.996FPS、frame P95/P99 19.236/20.422ms、draw P95 236、20/20 prediction/snapshot ready。
- Server boundary: Server 不讀 Atlas pixels；definition id/stats/spawns、30Hz simulation、snapshot/network/combat cadence 不因 bundling 改變。
- Evidence: [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md)。

## ADR-035 — F001 Identity lifecycle + SQLite account store（2026-11-14）

- Status: **ACCEPTED**。
- Decision: WebSocket Session、Account identity、Character identity、World Entity 分離；connect 不 Spawn，select character 才 `World.Join`。logout/disconnect/takeover 必須先 LeaveWorld，再失效 AccountId/CharacterId/Ready/SessionEpoch。
- Persistence: 正式 Server 預設 `Infrastructure.Sqlite`，初始 schema 為 Account/Character，WAL + foreign_keys + busy_timeout + synchronous=NORMAL；Domain/Application 僅依 `IAccountStore` use-case contract。Memory provider 只供 deterministic/performance benchmark。
- Password: PBKDF2-SHA256 + per-account random salt；hashing 在 World actor 外，per-session max1、global max4 auth workers，結果經 bounded command queue 回到單 writer。
- Session expiry: 每次成功驗證取得新的 process-lifetime monotonic SessionEpoch；same-account takeover 立即讓舊 session epoch=0 且移除 World Entity，再可靠送 `session_replaced`。
- Evidence: C# 376 checks；SQLite provider reopen、full Server restart persistence、same-account takeover/logout、20-client first-login storm 均 PASS。20-client SQLite final storm 約1.55s 全部選角，tick300 P95/P99 2.207/7.596ms。
- Boundary: Account+Character identity 已持久化；完整角色 stat/inventory/equipment/position 資料隨後續功能加入。Email/password reset/2FA/TLS/production anti-bruteforce 屬 Security/Operations。
