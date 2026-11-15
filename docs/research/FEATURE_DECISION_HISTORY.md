# Historical SGTest reference — Feature Decision History

> REFERENCE ONLY. Archived from the pre-rebuild codebase on 2026-11-15.
> These are exact historical texts, NOT the current client/server implementation or tests.
> Use the current ARCHITECTURE.md and todo.txt for decisions.
> Original individual documents remain recoverable from git commit f755a47.

## Contents

- [DECISIONS.md](#source-decisions-md)
- [F002_WORLD_TRANSITIONS.md](#source-f002-world-transitions-md)
- [ITEM_ID_MIGRATION.md](#source-item-id-migration-md)
- [MAPS_AND_CONTENT.md](#source-maps-and-content-md)
- [MONSTER_CONTENT_WORKFLOW.md](#source-monster-content-workflow-md)
- [TODO_PRE_REBUILD_20261115.txt](#source-todo-pre-rebuild-20261115-txt)

---

<a id="source-decisions-md"></a>

## DECISIONS.md

*Historical source: `docs/DECISIONS.md` — SHA-256 `ffe0190dbbc4c02156d88ab95e0b3549626c1455c8e482662ddce9d3526a30ef`*

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
- Consumers: 當時 Map13 monster 依 spawn count priority preload、Boy core paper-doll 6 parts+death 原子 ready、map object 依 viewport on-demand pin/release；後續全素材稽核已由 ADR-036 以實測推翻 map-object 細拆，改為 scenery + monster 共用 map lifecycle bundle。headless client 不載純視覺 paper doll。
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
- Necessary split（後續修訂）：ADR-034 初版曾保留 map_objects viewport pin/release；ADR-036 以 1440 viewport working-set + multiplayer A/B 證明其節省不足以合理化 21～30 個 owners，已改併 map lifecycle bundle。terrain 仍是單 map texture + viewport MultiMesh；drops 目前是單一 MultiMesh。
- Cleanup（第一階段）：先移除被 bundles 取代的 40 個 per-enemy/per-part generated files；ADR-036 後進一步把 production asset root 從全量轉檔鏡像收斂為只生成真 Runtime consumers。Legacy builders 保留研究/回歸能力但標為 research-only。
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

## ADR-036 — 全素材分類必要性稽核；查閱粒度與 physical packing 分離（2026-11-14）

- Status: **ACCEPTED**。
- Scope: Enemy、Player、NPC、Equipment、Item、Pet、Mecha、Map、Effect、UI、Ground Drop，以及 Builder/Bundle/Loader/Cache。
- Decision: normalized definition 保留每個 entity 可獨立查閱的 action/facing/mirror/pivot/timeline/z-order；production manifest 另保留 `inspect[action][facing]`。Debug/Viewer 的查閱粒度不再與 PNG/page/bundle boundary 綁定；physical packing 只由 lifecycle + Before/After A/B 決定。
- Runtime categories: production 不因素材種類、entity、direction、action、部位、來源檔、512/1024/2048/4096、hot/warm/cold 等建立新 Runtime type。正式 sprite runtime 只用 EntityAtlas + RuntimeAssetStore + 少量 map/session/真正 on-demand lifetime boundaries；尚未有 consumer 的 NPC/Equipment/Item/Pet/Mecha/Effect/Legacy UI 不預建 Loader/Bundle。
- Runtime root: 移除 `client/assets/generated` 額外層；`client/assets` 是可重建 Runtime output，不是完整原版素材鏡像。完整 rebuild 先清理舊輸出，只生成目前真正消費的 `bundles/` + `maps/`。Before 17,274 files / 1,179.31MiB → After 12 files / 9.036MiB；原始素材仍在 `.cache/client`。
- Map13 scenery audit: 367 placements / 45 unique old object atlases；1440 viewport+margin P50 21 atlases/4.394MiB、P95 30/5.414MiB、全量7.222MiB。細拆只省約1.8～2.8MiB object resident，維護成本不足，故 scenery 與9種 monster 共用 map lifecycle sprite bundle。
- Atlas A/B: monster+scenery 1024=5 pages/18,572,476 RGBA；2048=2/18,569,428；4096=1/18,478,500。4096 lifecycle load/frame 無明顯勝利且 transient static/decode peak 36,911,847→53,543,643 bytes，因此 auto mode 優先 <=2048；4096 保留研究 override / 2048 無法容納時的 correctness fallback。
- Multiplayer trade-off: 20 clients/300 Enemy/1000 drops/40s Before→After：draw P95 236→228、frame P95 19.236→18.947ms、P99 20.422→20.322ms；texture 39.06→46.04MiB、video 45.56→52.53MiB、process RSS 約 +6.47MiB/client。此變更是 lifecycle/維護性與小幅 draw/frame 改善，不宣稱 RAM/VRAM 最佳化。
- Release: 30-cycle async lifecycle all texture handles released、engine texture recovery、Store resident recovery 全 PASS，P95/P99 16.824/16.906ms。
- Tools: `tools/README.md` 固定 production / research / benchmark / viewer-diagnostic / tests 邊界；不得為每個素材小功能建立另一套 production Builder/CLI/Loader。
- Evidence: [ASSET_NECESSITY_AUDIT.md](ASSET_NECESSITY_AUDIT.md)、[ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md)。AS005 原版逐格人工視覺與 AS008 發布權仍未完成。

## ADR-037 — Adaptive Atomic Atlas 與一鍵原始素材轉換（2026-11-14）

- Status: **ACCEPTED**；完整來源離線生成仍需獨立完成結果驗收。
- Naming: 原研究「Mode 4」正式命名 **Adaptive Atomic Atlas（自適應原子圖集）**，實際 `packing_policy=adaptive_atomic_atlas_v1`。名字代表來源 SPR 先作完整動畫區塊、再用 MaxRects 裝箱和 RGBA/page 成本選尺寸，必要時多頁，不意味所有來源都合成單張。
- Scope: `tools/asset_converter/` 對外提供唯一單次執行 CLI／batch；支援原版 Data1.dat+loose overlay、完整原始 SPR 私有離線 index、ACT/SIF/DrawOrder sidecars、Map13 與 Player Base 現有 Runtime Bundle。其他 map/equipment/UI gameplay consumers **不因此宣稱已實作**。
- Lifecycle: 完整來源圖像只放 gitignore `local-assets/asset_converter/complete/`，不塞進 `client/assets` 和 GPU；Runtime 只保留 `map/session/on-demand` owner，既有 EntityAtlas/RuntimeAssetStore、Render/Logic/Network 隔離均不變；不加 `generated/`、獨立種類 Loader 或對應 Cache。
- Optimizations: 單幀候選避免重複建立四組 PIL packing workspace；全量逐群組流式處理與 SHA256 增量快取；頁面損壞自動重建；預設 <=2048（現有 Runtime）、4096 僅過大來源 correctness fallback；原始畫素逐幀比較、origin/crop/source index 保留。
- Safety: 原版只讀；Runtime staging 驗證後逐檔 atomic replace；不在開始前刪除所有 live assets；gitignore 防止原版衍生圖上傳。舊 `tools/rebuild_assets.ps1` 保留相容用途但不是推薦新入口。
- Evidence: `docs/ASSET_CONVERTER.md`、`tools/asset_converter/README.md`、`local-assets/asset_converter/build_report.json`。Map13 9 visuals/78 spawns、573 frames/2 pages，Boy base 881 frames/1 page；Godot tests 5 PASS，Python tools/tests 92 PASS；Enemy 137 group all PASS、Weapon A-Sword014 1 group PASS；是否有全 7,893 群組全部通過以離線 `index.json` 為準。
- Limits: 完整原版 screenshot Golden（AS005）與公開授權（AS008）、所有遊戲內容 Runtime integration、實際 GPU timing/切圖冷熱載入 Before/After 均 NOT VERIFIED；源 SPR 全轉換與玩法完整性不能混為一談。
## ADR-038 — 單一SPR解碼器與可追溯的完整離線轉換（2026-11-15）

- Status: 實作／fixture驗證；ASSET209全量與sidecar驗收進行中。
- Before: asset_converter入口含literal換行逃逸造成SyntaxError；離線strict decoder拒絕indexed Ver08的非零frame offset，與Runtime解碼分叉。舊progress停6650/7893、242失敗，但沒有可稽核的完整index。不能認定這些素材已損毀。
- Decision: source/convert_assets共用source/runtime_spr的RGB565/indexed/CDE解碼；原始來源唯讀。每組來源錯誤帶相對路徑＋SHA；每50組checkpoint保留結果。完整SPR集合對原版Data1+loose稽核，禁止殘缺extracted cache通過完整建置並發布Runtime。
- Cache: decoder SHA與metadata SHA加入manifest；舊cache重新decode並驗證像素／crop／size／pivot後只更新metadata，不重pack正確PNG。圖或metadata損毀重建，空白影格不得補成假圖。
- Evidence: 目前原版union 75,588 SPR／隔離source 75,588，missing=0、extra=0，實際分組7890；舊75591/7893不是本次基準。Indexed fixture保留綠色pixel與origin、CLI跨工作目錄啟動、cache修復與incomplete-source拒絕已測；全量完成與sidecar統計另補，不提前勾選ASSET209。

## ADR-039 — 候選只量幾何，選定後產圖並雙向驗證（2026-11-15）

- Status: 4924dc4幾何評估實作已合入；本次補可重跑A/B與發布保護。
- Decision: 保持原SPR atomic／2px shelf／MaxRects的實際演算法，512/1024/2048/4096候選評估不建立PIL頁面。只繪製最終選中方案；發布前actual page count/RGBA必須與候選一致，再逐格比對source crop/size/origin/pixels。
- Evidence: `tools/benchmark_converter_planning.py`，三次CPU process_time中位數：EN010A 765.625→1.719ms、Boy body 3093.750→30.625ms、hair00 3062.500→36.250ms；geometry batch100避免短測量量化成0。四尺寸候選頁數／RGBA相等。原始樣本與source/module SHA見reports/converter_planning_geometry.json。此為候選評估，非全建置／Runtime FPS或RAM提升宣稱。
- Regression: 160組隨機候選對照真打包、全透明／atomic多頁拒絕／無畫布配置、錯誤估值拒絕發布、JSON類型壞cache重建；tools/tests共108 PASS。全量archive仍獨立驗收，ASSET209尚未完成。

## ADR-040 — 完整Archive驗收與build/verify操作隔離（2026-11-15）

- Status: ASSET209完整離線轉換PASS；整體遊戲／RAM目標未完成。
- Decision: 同一CLI提供build/verify；verify以原始來源集合和完整index稽核所有群組，拒絕preview/partial作完整證據，檢查source/decoder/metadata/page SHA、frame identity、PNG實際尺寸及候選成本。ACT/SIF/DrawOrder使用共用document解析器重新比對，cache不只看source SHA。驗收不假稱重新做全量pixel replay。
- Concurrency: 已重現解包與verify重疊時會讀到暫態來源，因此同checkout採OS lock互斥；busy exit2、不改舊報告。lock檔殘留不是活程序證據，跨程序／異常退出解鎖已測。
- Evidence: 7890groups/75588SPR/282968frames/8094pages、613ACT/1165SIF/9DrawOrder，0錯誤；原始路徑一鍵build與後續verify PASS。docs/ASSET_CONVERTER.md、reports/converter_full_build.json、converter_archive_verification.json。113 Python／376 C#／6組Godot PASS；30cycle async回收與20client場景PASS，原始JSON見reports/asset_converter_acceptance。
- Open: Client RSS216.96MiB仍高於200MiB（FAIL），PERF002先定位CPU Image／metadata／worker持有再優化；不改同步頻率或減畫質。其它Recovered功能及AS005/AS008仍是獨立未完成範圍。

## ADR-041 — Runtime小頁工作空間及Windows release實測（2026-11-15）

- Decision: Runtime bundle自動優先512／1024；原SPR atomic block放不下才採最小有效2048／4096。維持語意／ownership、shared texture、畫質／實體數／同步頻率；完整離線archive不改。尺寸由Builder內部處理，新增怪物不增加Runtime分類。
- Evidence: 同1440×900／20連線／300怪／1000掉落／40秒，release2048 packed峰值199.80MiB（另早期217.08FAIL），1024 packed三次184.95～189.11MiB，frame P95≤18.602／P99≤19.294ms。正式原版來源重建及release打包後187.09MiB、60.003FPS、18.549／19.200ms。增加Draw Calls與極小texture bytes是明列取捨；實體driver VRAM未測，開發引擎仍超200MiB。詳docs/PERF002_MEMORY.md與reports/perf002_production/。
- Lifetime: per-job/per-page函式frame解除idle worker對CPU Image／metadata持有；weakref由1/1→0/0。telemetry固定2048筆、idle不追加，一般diagnostics不複製樣本。30次map async load P95 130.829ms、GPU submit P99 1.591ms、frame P99 16.971ms，引用／texture／RSS回收PASS。取消風暴邊界另測，不由串行回收推論完成。
- Build: 正式Windows EXE＋PCK保留raw PNG／JSON／BIN；專用EditorExportPlugin加入被.gdignore排除的原始bytes。tools/build_client.py驗證官方templateSHA與版本，build後啟動release檢查全部asset/source SHA；benchmark測量前共用此檢查，拒絕stale PCK。EXE/PCK與原版圖像僅留.cache，不公開。
- Regression: Python tools/tests113、asset_pipeline5、runtime_baseline8；Godot方向／360 logical views／scenery／paper doll／Store／wire六組及native weakref probe PASS。沒有改動C#協定或Server模擬，沒有重跑完整RUNTIME206矩陣；仍維持Render30～90FPS與Client input30Hz／Server30Hz／snapshot10Hz分離契約。

## ADR-042 — 取消須移除工作、ready events與部分載入帳目（2026-11-15）

- Before: 實際paused-worker fixture取消1000次後entry=0但queued jobs=1000；兩頁自製fixture在第一頁GPU上傳後取消或第二頁失敗，owner清除後resident帳目仍4096bytes。原始JSON reports/perf003_before.json。
- Decision: main與worker共用mutex保護有效token；取消時移除queued request／ready events／token，publisher在相同鎖下拒絕失效token，於job/page邊界停止舊解碼。零byte事件也受128-item上限。ready LRU、部分載入取消、error共用引用／resident帳目清理，錯誤即可釋放已上傳部分，不等logout。
- Evidence: After取消queue=0、partial／failed resident=0、native Texture弱引用=0、相同path修復後重試PASS；1000generations／priority／MAX_JOBS／zero-byte cap／byte backpressure／close+restart三次PASS。重建Windows release：184.88MiB、59.997FPS、frame P95/P99 18.884/19.573ms；30次map載卸回收PASS。docs/PERF003_CANCELLATION.md及reports/perf003_*。
- Limits: 正在進行的單頁PNG不做不安全中斷；ready byte budget之外仍可有一頁in-flight。idle worker維持既有poll，獨立idle探針不能稱為8個Client或零CPU；未宣稱idle CPU改善。Server／network／logic頻率不改，完整RUNTIME206矩陣本次未重跑。

## ADR-043 — Entry壓力與byte壓力共用無引用LRU（2026-11-15）

- Before: 自製128個32×32 Texture共524288bytes、byte budget16MiB，只有1項pinned，其餘已release；第129個path仍ERR_BUSY(44)，無任何eviction。不是網路或檔案遺失。
- Decision: acquire遇到128-entry上限時，呼叫同一purge_unowned並指定entry_limit=127，按原last_used規則騰出位置；預設purge維持128上限及既有bytes規則。不能直接force清光cache，也不能淘汰有owner的項目。只有cache hit／未滿情境不改。
- Evidence: headless/native均新acquire=OK、恰1個最舊無owner項目移除、pinned與最近觸及項目保留，resident帳目同步減4096bytes，最後close歸0。全部128項都有owner時仍ERR_BUSY，釋放一項後成功。reports/perf004_before.json、perf004_after.json、perf004_after_native.json，test_runtime_asset_cache_pressure.gd；Store與cancellation既有回歸PASS。
- Scope: 沒有改動Atlas、影格／方向、network／logic／Server、GPU提交頻率。本次未重跑一般2-bundle多人FPS／RAM與RUNTIME206矩陣，不把前一版本數值當作本次重測。此修正覆蓋滿128項才會進入的cache admission分支。

## ADR-044 — NPC及傳點先保真內容，再接權威多地圖（2026-11-15）

- Decision: asset_converter新增world-content離線入口；保留NPC type／原座標／reserved／source index／Big5 name bytes／六個visual slots與來源hash。原始CDE外觀表必須先解包，不把未知方向／時間填成假八向。原版Server Map07目錄與Map7採唯一候選解析，NPC與population共用。
- Portal: 不依_to_或抽第一個MovePlayer決定傳點。只將直接閱讀過的TS_Common固定hash及完整靜態主檔形狀編譯為確認式transfer，動態／未知／停用腳本明列狀態；原目的地以原Blocking檢查，不擅自移位。其餘226筆仍需F013完整腳本／對話引擎。
- Evidence: 原始Client路徑抽取＋Map13三筆PASS；全24份NPC.dat原header合計275筆、輸出275、0未解primary SPR；49已審模板／226script_required，Map41/46缺檔另列。快取內容編譯1.241s，非Runtime或冷抽取數字。reports/world_content_all.json及F002_WORLD_TRANSITIONS.md；Python tools/tests121、asset_pipeline5、maps3+1SKIP。
- Scope: 僅離線F002A，不宣稱Server／Client切圖、完整NPC動畫、任務或Recovered全部功能完成。接續MapCatalog／權威transfer fixture→MapEpoch與共用Server時鐘→Client loading／資源解除→真雙圖多人及資源回歸；同步頻率維持既有契約。原版內容JSON及圖片不公開。

## ADR-038 — Client / Server 目標目錄與命名（2026-11-15）

- Status: **ACCEPTED / IMPLEMENTED STRUCTURE**；2026-11-15 Client/Server 主程式已完成目錄搬移，後續不得再擴大舊分層。
- Style: Client 與 Server 都採功能導向 modular monolith；禁止為了形式建立多層 Clean Architecture、每素材獨立 Loader、或抽象的 Common/Utils/Manager 雜物層。
- Client: 主要模組為 app / networking / world / gameplay / rendering / assets / ui / diagnostics；Render FPS、UI FPS、Network I/O、World State 必須隔離。AFK 可停止 world rendering、降低可重建 visual replication、釋放 GPU world assets，但不能降低 heartbeat、reliable gameplay event、state reducer 或 authoritative state 的處理正確性。
- Server: 維持單一 .NET 8 project，主要模組為 Networking / Accounts / World / Combat / Items / Social / Persistence / Content / Diagnostics；不再擴大 Application / Domain / Infrastructure / Transport 目錄。
- Naming: 模糊的 `Session` 類別名稱已移除；網路連線使用 `ClientConnection`，既有 wire 世代欄位內部命名改為 `ConnectionEpoch`。AOI/角色狀態若未來複雜度需要再抽 `VisibilityState` / `PlayerState`，不先建立空類別。
- Assets: Converter source catalog 可保留原版 Player/Enemy/NPC/Pet/MA/MS/SR/Item/Effect/Map/Interface/Audio key；Runtime catalog 使用 maps/Enemy/npcs/players/equipment/pets/items/effects/ui/audio/data。每個素材保持獨立 identity；生命週期以 app/character/map/on_demand metadata/owner 表達，不用資料夾或 physical merge 代表 ownership。
- Source of truth: docs/ARCHITECTURE.md、docs/CLIENT_ARCHITECTURE.md、docs/SERVER_ARCHITECTURE.md。其他 Session 在修改對應端前必須先讀這三份與相關專題規格。

## ADR-039 — 移除 raw-assets EditorPlugin，改用跨平台 external asset pack（2026-11-15）

- Status: **ACCEPTED / WINDOWS VERIFIED / macOS APP EXPORT NOT VERIFIED**。
- Remove: `client/addons/raw_assets_export/` 與 `project.godot [editor_plugins]`。Runtime 原本就由 AssetStore 直接讀 JSON/PNG；舊 addon 只負責 Export 階段把 `.gdignore` raw files 塞進主 PCK，屬多餘特殊機制。
- Build: `client/tools/build/pack_assets.gd` 使用 Godot PCKPacker，把 `client/assets/**` 以相同 `res://assets/**` path 打成平台無關的 `assets/assets.pck`；main application export 明確排除 `assets/**`。
- Runtime: autoload `scripts/asset_pack.gd` 在 gameplay 前呼叫 `ProjectSettings.load_resource_pack()`。Windows 預設 `<exe dir>/assets/assets.pck`；macOS 支援 distribution sibling `<app parent>/assets/assets.pck`、`.app/Contents/Resources/assets/assets.pck` 與顯式 `--asset-pack=`。掛載後 Renderer/AssetStore 只看 `res://assets/**`，不得處理平台 path。
- Same rules: Windows/macOS 共用**完全相同**的 asset pack、catalog、logical ids、ownership metadata 與 AFK resource policy；平台差異只在啟動 path resolver。
- AFK: pack 可持續掛載；省資源依 AssetStore 釋放 world textures/GPU refs，不用 unmount，不降低 Network/Reducer/authoritative state。
- Evidence: Godot 4.7.2 Windows Release 主 PCK 無 asset files，external pack 9,414,272 bytes / 15 current runtime files，release process 預設路徑自動掛載並讀到 `res://assets/bundles/index.json`；pack SHA256 `12111f73f074af449eaa8239c1f12b6f50e96d1766780155ea33fd4e91146c88` 與 runtime 回報一致。macOS path unit test PASS，但真正 macOS app export 尚未執行。

## ADR-045 — Runtime 素材改為 per-asset identity；Map/Monster 分離；公平四模式重測（2026-11-15）

- Status: **STRUCTURE ACCEPTED / PACKING MODE PENDING USER CHOICE**。
- Boundary: 每個原版素材保持獨立 logical/physical identity。Monster、NPC、scenery、Player body part、equipment 不因 map 或 character lifetime 被合成同一 atlas。PCK 只作磁碟容器，Runtime 仍由單一 AssetStore 按需載入/釋放。
- Map: `maps/<id>/` 只放 `world.json / terrain.png / tiles.bin / blocking.dat`；scenery placement 只引用 `maps/visuals/<asset>/atlas.json`。Monster visual 不屬於 map asset；Server population/definition 指向 `Enemy/<sprite>/atlas.json`。
- Collision: `blocking.dat` 保留原版 Blocking.dat byte grid，不轉 Godot CollisionShape；Client/Server 共用 hash 與 O(1) cell lookup。
- Full export evidence: 75,588 SPR → 7,890 independent groups → 7,890 unique logical IDs，0 collision、0 export error；21,723 managed files，約1.31GiB；Map13 contract 367 placements / 45 scenery assets、9 monster definitions / 9 monster assets、7 Player-B base parts，cross_asset_bundle=false。
- Benchmark correction: 舊 Mode4 root 有超過固定12隻怪，舊腳本排序後取前12；Mode1/2/3 root 只有固定12隻，因此舊 Mode4 12-kind cross-mode數字失效。新版測試固定同一12個 monster IDs、固定 Map13 independent asset、每 Mode 獨立 Godot process，2/12 kinds×1000 instances×3 repeats，repeat 2 反轉模式順序。
- Fair result: **Per-Asset Dense Single** 在固定大型怪物集合的 texture footprint / pages 綜合最佳；Adaptive Atomic 不再顯示舊版記錄中的巨大記憶體優勢。另以完整 archive frame metadata 掃描 7,890/7,890 logical assets，全部可在單一 <=4096 Dense page 幾何限制內放下。正式 packing mode 不在此 ADR 偷改，等使用者確認是否切為 Per-Asset Dense Atlas。
- Runtime tests: Python tools/tests 123 PASS；root-level contracts 37 PASS/1 SKIP；Godot 現有 `client/tests/test_*.gd` 15/15 PASS；`verify_runtime.ps1` 完整 PASS；使用 `.cache/dotnet` SDK 8.0.428 從 repo root執行 C# build 0 warning/0 error、tests **376 PASS**。

---

<a id="source-f002-world-transitions-md"></a>

## F002_WORLD_TRANSITIONS.md

*Historical source: `docs/F002_WORLD_TRANSITIONS.md` — SHA-256 `3424a3c5ca9fb0cfe729257dfc363be31097c89df47261f15104a3aa2a34a8b9`*

# F002：地圖／NPC／傳點執行順序與證據

2026-11-15。完整F002仍未完成；不把離線內容解析當成可遊玩切圖。Server持續C#／.NET8，Client render30～90FPS與input30Hz／Server30Hz／snapshot10Hz獨立契約不變。

## 現況與首個交付

Server `Program.cs`只載一張碰撞圖與population，`WorldHost`擁有單一World；WorldReady只比對mapId／fingerprint／populationHash，沒有MapEpoch。Client雖有world_generation取消素材，但沒有真正跨圖協定。不能先讓Client自行改map或座標冒充Server傳送。

**F002A首個交付：** 在asset_converter內提供可重跑NPC／傳點來源解析、私有normalized JSON、來源hash與自製fixture。Map13的三個傳點腳本都是TS_*，沒有_to_；依檔名猜測會漏掉。輸出只留`local-assets/asset_converter/world/`，尚未接消費端前不塞進Client常駐素材或當作Server已可用功能。

```powershell
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --map 13
# 已有完整抽取cache時：
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --map 13
# 所有目前存在NPC.dat的地圖；不將缺檔猜成空資料：
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --all-maps
```

`--server-dat`可指定原版服務端Dat；未指定時沿用現有唯一Recovered/Old/Dat resolver。圖片／外觀表從原始Client的Data1＋loose抽取；NPC出生與腳本來自原版服務端資料，不能從Client貼圖猜測權威傳點。預設JSON與原始腳本文字／名稱不公開；Git只留工具、人工fixture、hash及摘要。

### F002A實際驗收

原始路徑已完成Data1＋loose抽取與Map13內容轉換，報告`reports/world_content_original_map13.json`；抽取摘要`world_content_source_extraction.json`保留Data1 SHA／41687 archive entries及loose overlay計數。其後以目前程式與同抽取結果重跑全量NPC內容，Map13 fingerprint完全相同：`c9a4fe9aa9dc61a74c2508dbcad1a8d81460b453288ac9096df3d6ae065e266a`。

全量範圍為目前服務端Dat中存在的24份NPC.dat：原始header合計275筆、輸出275筆，0列遺失，275筆primary SPR均可解析。49筆符合已審確認式TS模板且來源／目的地可行走；226筆保留script_required，沒有強制改成簡單傳點。Map41／46有目錄但沒有NPC.dat，明列未知，不假裝已完成其NPC資料。本次275筆引用的primary SPR均為單影格；完整NPC時間／方向欄位語意仍未驗證，不憑此推導未來動畫規則。

`reports/world_content_all.json`含逐map結果、來源hash、converter module SHA與執行期間檔案未變動證據；使用已有抽取快取的內容編譯1.241秒，不能稱為冷抽取或Runtime載入時間。首輪原始抽取未記錄獨立elapsed，不拿來比較效能。完整NPC名稱／腳本／座標／資料JSON只在local-assets，不公開。

驗證：`python -m unittest discover -s tools/tests -q` 121 PASS，包含8個新NPC/CLI/原版目錄fixture；`tools/test_asset_pipeline.py` 5 PASS；`tools/test_maps.py` 3 PASS／1 private fixture SKIPPED。CDE自製fixture驗證共用decoder委派，真原始CDE表569列驗收則由此次原始來源執行證明，不混稱為自製加密fixture。F002B～E及完整任務／對話／Server transfer依然未完成。

## 來源契約

唯讀參考Recovered `feature/local-server-net8` SHA `0662e491ed6cdc46f047380aa063363fae0ce190`：

- `server/Infrastructure/GameData/NpcDatReader.cs::Read`：count:i32、每列type:i32／x:i32／y:i32／reserved:u8／script:NUL ASCII／name:NUL Big5。新reader加上完整長度、計數／字串上限、encoding與EOF檢查，不把截斷資料讀成空字串。reserved與name原bytes仍保留，不解讀未知位元。
- `server/Infrastructure/GameData/LegacyNpcVisualCatalog.cs`提供六欄NUL表的參考。實際原始`npc/npc_table.dat`是C.D.E.包裝，必須共用現有unwrap decoder後讀取；本機解出569列×6欄。所有六個source slots保留；只將已存在的primary SPR標為resolved。NPC動作時間與方向欄位語意尚未實證，明列false，不假裝八方向或沿用怪物方向政策。
- `server/Infrastructure/Quest/QuestRuntime.cs`的`MovePlayer(x,y,mapId)`及include preprocessing提供呼叫語意參考。Map13三份TS_*主檔的GetLoc為常數，`TS_Common.txt` SHA256 `8c972667f520ef00441f2b12c5a16fa1475a2e0a5ce1dd20e16bb79e37697562`已直接閱讀：start→CanMove→Ask確認→choice0才呼叫GetLoc／MovePlayer。這不是將任意Lua中的第一個MovePlayer當成有效傳點。

轉換只接受該已審模板hash與完整靜態入口形狀。主檔若停用入口、加入條件／額外函式、動態座標或未知include，就保留blocked／script_required／unavailable，不能自動開啟。註解與字串不會建立虛假的GetLoc；包含Lua等號長註解。這是保守的舊格式adapter，沒有執行Lua；F013完整任務／對話引擎仍待開發。

位置維持原始world pixel整數；以原版Blocking grid的16px cell檢查來源／目的地bounds與低3bits可行走性。目的地阻擋、缺圖、越界會保留原值並標不可用，不能離線擅改座標。NPC本身不是行走實體，來源position_status僅保留稽核資訊，不因站在阻擋格就擅自新增腳本進入限制。未來Server仍須在傳送時重新檢查當前地圖狀態，不能只相信此離線結果。

`spawn_id=source_index+1`僅在同map及同content fingerprint範圍有效，重建不任意重排；未來wire必須帶MapEpoch／content fence，不能跨版本用裸spawn_id。normalized內容與來源hash使用確定性fingerprint，不含主機絕對路徑與時間戳。

原版服務端低編號目錄使用Map07／Map08／Map09；population先前只找Map7。共用`build_maps.server_map_directory`同時供NPC／Enemy使用，只有一個合法候選才接受；Map7與Map07同時存在時fail-closed，不偷偷選一份。

## 後續實作順序

1. **F002A內容／fixture。** 原始路徑可重跑、Map13三筆資料與24份NPC.dat覆蓋稽核、未解腳本／視覺／目的地分類。逐項保留失敗；未測列NOT VERIFIED。
2. **F002B Server MapCatalog及transfer domain fixture。** immutable map定義與碰撞資料按需載入；每map一個共用World，不隨Session複製AI。先用自製雙圖與阻擋格證明權威transfer、原圖離開／新圖進入、共享怪物／掉落、失敗不半轉移。磁碟／重型decode不能卡30Hz世界迴圈。
3. **F002C協定與時鐘。** 在改production前建立wire fixture，為世界訊息／輸入／互動／WorldReady增加MapEpoch及版本邊界；舊圖input、ack、action不能污染新圖或同圖重入。多map需共用明確Server單調時間基準，不把各新World從0開始的time拿來混用既有Pong／插值時鐘。
4. **F002D Client真正loading流程。** 登入→選角→入圖／切圖進度由不可變狀態呈現；network／fixed logic獨立持續。先解除舊map owner／replica／prediction，再按新epoch預載並驗hash；完成後才WorldReady。Session紙娃娃跨圖共用、登出確實釋放。恢復畫面直接呈現當前狀態，不補播舊圖事件。
5. **F002E真資料雙圖E2E。** Map13與已驗證目的地、兩位觀察者、傳點距離／確認／拒絕條件、連續切圖／重連／中途登出、舊封包晚到；native畫面與30次資源回收。最後重跑同20連線／300怪／1000掉落／1440×900基準及受影響FPS隔離矩陣，再宣告F002完成。

現有SPEC資源／frame門檻維持不變：一般frame P95≤25／P99≤40ms且相同場景不退化>10%，熱cache map素材ready≤1秒，GPU submit P99≤8ms，正常release峰值RSS≤200MiB；切圖失敗不可犧牲其他map的30Hz模擬或10Hz snapshot。本階段沒有完成上述Server／Client transfer或完整NPC動畫，不能以離線PASS替代。

---

<a id="source-item-id-migration-md"></a>

## ITEM_ID_MIGRATION.md

*Historical source: `docs/ITEM_ID_MIGRATION.md` — SHA-256 `1ef1f8db1900b5cf5a94c58dff5162a2467483628a5dc1f3fde4616b3ef8a6be`*

# 物品編碼重設與舊 ID 遷移 — 必做

狀態：設計規格；舊 Weapon.itd/Equip.itd 為非 UTF-8/二進位資料，尚未逐筆解析；**沒有產生真實完整新舊 ID 清單**。TODO A003/A004 負責解析、測試、正式產表。不能臆測 +1/+2 在每個檔案必然是相鄰數字。

## 1. 使用者強制要求

- 所有裝備種類皆能有強化等級（依 slot/類型配置上限、成功率、花費及增益），不再需要為「長劍 +1、長劍 +2…」產生不同 ItemDefinitionId。
- 保留只有一個基本裝備定義，強化屬性存在每個個別裝備 ItemInstance。
- 刪除舊 +1/+2/+3 等強化變體的獨立編碼，後方物品編碼**往前排、填補空洞**，形成新遊戲緊密排序的定義 ID（一次性初始生成）。
- Old 來源 ID 仍保留在獨立 ImportMapping/Audit 資料中作查核，不能在 runtime 以 ID 加減法尋找 +N 物品。
- 所有 NPC、怪物掉落、任務、商店、技能消耗、裝備效果、登入資料、舊 SQL 引用必須一起依映射轉換；遇不確定必須 quarantine、報錯而不是對錯物品。

## 2. 新資料模型

ItemDefinition：
 id（int，發布前從1密集排序；發布後永久穩定）
 key（不可變字串，例如 equip.sword.basic_01）
 category / equip_slot / subtype / base_stats / icon_resource / render_resource
 can_equip / can_enhance（所有真正裝備都 true）/ enhancement_profile_id / max_stack

ItemInstance：
 uid（雪花或 UUID，與 definition 無關）
 owner / location(inventory, equipped, bank, ground, trade, house)
 definition_id / enhancement_level（>=0）/ durability / rolled_affixes / bind_state / version
 quantity（可堆疊非裝備）/ created_at

EnhancementProfile：
 equip_slot_category、max_level、cost_curve、stat_curve、success_rate、failure_rule、special_effect；與 definition id 無關。所有裝備至少有 default profile，可依類別 override。
單獨強化同一類別的兩把劍，應各有獨立 level/耐久/UID；不可修改 ItemDefinition 或其他玩家裝備。

## 3. 初始新 ID 生成演算法

輸入：已解析、已校驗、具有 source_category、old_id、name、type、variant_label、base_relation、stat_delta、source_sha 的 OldItemRecord。
步驟：
1. 類別/檔案編碼解碼清洗：Big5/CP950/二進位結構由專用 decoder，Unicode 正規化僅用於名稱比較，不把名稱當唯一 key。
2. 建 legacy compound key = (source_catalog, source_category, old_id)；不得假設不同檔案 old_id 全域唯一。
3. 確定性識別真正「強化 +N 變種」：需靠原版欄位、對應數據、效果/分類、原始流程核對；**僅看到字串尾巴 +1 不能視為證據**；重名/異名/碰撞進 quarantine。
4. 選唯一 Base ItemDefinition：如果原物件是合法基礎款保留；將確認的 +N 變體變成 LegacyAlias(oldId → baseKey, enhancementLevel=N)，不建立新 Definition。
5. 所有保留的基礎定義依 (原始全域排序明確 index, category tiebreak, old_id) 決定穩定順序；若沒有原始全域排序，預設以 (old_id, source_catalog, source_category) 排，須稽核衝突。
6. **僅第一次初始匯入**指派新 id = index+1，所有有效 base definition ID 緊密連續、無洞。這會使原本 +N 變體後面的定義向前遞補。
7. 輸出 ItemDefinitions.json、LegacyItemMap.csv/json、Unresolved.csv、MigrationReport.md、轉換摘要/hash，並以內容 SHA 給出可重現版本。
8. 正式版本發佈後 ID 凍結！新增項目 append 新 id；移除定義保留 tombstone，不能再因後續項目移除重新排號，否則破壞已持久化物品及封包。

## 4. 純示例（人工構造，不是 Old 真正 ID）

| 舊ID | 舊名稱 | 新定義ID | 新強化級別 | 備註 |
|---|---|---|---|---|
| 100 | 短劍 | 1 | 0 | 來源基本裝備 |
| 101 | 短劍+1 | 1 | 1 | 不佔新 ID |
| 102 | 短劍+2 | 1 | 2 | 不佔新 ID |
| 103 | 戰斧 | 2 | 0 | 後方前移 |
| 104 | 戰斧+1 | 2 | 1 | 不佔新 ID |
| 105 | 藥水 | 3 | 0 | 不可裝備/不可強化 |

新背包可同時存在：
 ItemInstance(uid=A, definition=1, enhancement=0)
 ItemInstance(uid=B, definition=1, enhancement=2)
 ItemInstance(uid=C, definition=2, enhancement=5)
原版來源 102 的既有物件轉成 (definition=1, enhancement=2)；新遊戲內沒有獨立的 102 強化定義。

## 5. 交叉引用與遷移安全

- Inventory/Equip/Bank/PlayerShop/Drop/Mail/GM commands/Quest/NPC Shop/Shop price/Mecha/Card/Pet Equip/Hotkey/Trade/Repair/ItemLog 全面掃描。
- 先建立 dry-run 重寫表，不直接寫正式 DB；差異 diff 必須列出來源 ID、原名稱/種類、候選新 ID、強化級別及所有引用位置。
- Reference Resolution Policy：同來源多個候選、同 ID 不同名稱/類型或缺原 Item 定義時，產 ERROR/Unresolved，不以同數字與新 ItemId 相等或最近 ID 猜測。
- SQL 載入採 transactional staging→驗證數量/外鍵→atomic swap；若資料轉換只用參考而不遷移正式舊服玩家，也應跑「資產守恆與映射完整度」測試。
- 新遊戲全新角色不一定要匯入舊玩家資料，但依然需要 Old 定義 ID/內容引用的完整轉換報告。

## 6. 測試/驗收 Gate

- 不同時區/作業系統/原始資料順序相同輸入 → bit-identical mapping。
- 真 +N 變體可還原 Base + N；非變體包含 + 符號的名稱不得錯誤合併。
- 相同名稱不同種類/來源不可被合併；同一舊 ID 多來源需 compound key。
- 0,1,2,5,20 等強化 level 能獨立存在且公式/上限一致；非裝備不可意外強化。
- ID 除已排除 alias 外無重複、無洞；每個引用可解析到合法 Definition 或明確 unresolved。
- 任務/掉落表/商店/GM/保存/登入/交易/銀行與裝備 UI 同步不因重編造成錯物、錯價、重複發放。
- 一次完成後匯入新項目不能重新排列舊項目；已發布資料版本升級以 Migration/Tombstone 達成。

## 7. 來源

- Old/服務端/Dat/Item/Weapon.itd、Equip.itd、General.itd、Recovery.itd、Booster.itd、pet.ITD
- Old/ClientData/Item/Item.itd、Item_cht.itd、Item_en.itd
- Old/資料庫/game.sql（equip/ItemID/ItemIdx、generalitem 等實體）
- 舊版 source current client/Items 與 server/Infrastructure/GameData/CurrentClientDataCatalog.cs 可參考欄位，不沿用有歧義的歷史 ItemID。
- 來源 SQL game.sql 中可見 equip 表 ItemID 與 ItemIdx，尚不足推論 +1/+2 真正公式，須查實際 Item.dat 與反組譯流程。

---

<a id="source-maps-and-content-md"></a>

## MAPS_AND_CONTENT.md

*Historical source: `docs/MAPS_AND_CONTENT.md` — SHA-256 `5eb8a40515ef6ef21222cf86e584e36ef7e840426ff0e30ea56e65b64668b2c6`*

# 地圖、路徑、編輯器與內容資料設計

狀態：設計；Old binary 需離線解析校驗（A005）。**不擅自複製未授權 Map/*.scn、圖片與反編譯原始碼**。

## 1. 資料來源與行為參考

舊來源：
- Old/服務端/Dat/Map/MapXX/Blocking.dat：可行走格與特殊地形。
- MapXX/Enemy.dat：怪物位置、種類與 spawn 屬性。
- MapXX/NPC.dat：NPC/傳點位置及腳本參考。
- MapXX/safe_area.dat：地圖安全區域，是否存在依地圖不同而異。
- Old/服務端/Dat/Enemy/Enemy_Data.csv、Old/服務端/Dat/Quest/*.txt、Old/ClientData/...
- 參考來源的 game-editor/README.md 記有 world→grid：floor(world /16)，cell center=16*cell+8，blocking mask=0x07；此為目前專案稽核記錄，不保證每張 Old 地圖沒有例外，必須驗證。

## 2. 新格式與 ID

- MapDefinitionId：單一 immutable 新定義 ID + stable key（e.g. world.map13-like-plateau，正式名稱須原創）。LegacyMapId 僅存於 ImportMapping，避免新系統被舊 ID 限制。
- MapChunk：chunk_id、bounds、static tiles/occluders、collision grid、resource manifests；使用內容 hash 方便重用與增量快取。
- MapEntityPlacement：spawn_id（穩定）、npc_def_id/monster_def_id、x/y、facing、spawn_rule、respawn_group、script_id。
- MapRegion：safe zone、PVP zone、傳點、音效區、觸發邊界、Housing 門口。
- NavGrid / CollisionGrid：Server 與 Editor 相同資料來源及轉換工具；Client 只用讀取副本做視覺/路徑預覽，Server 最後驗證。

## 3. 座標與渲染

- 明確分離 WorldUnit、GridCell、RenderPixel、ScreenPixel；提供確定性 world↔cell 函式及物件中心偏移。
- 地圖大於 Viewport，客戶端根據 Camera WorldRect + 預載 margin 動態載入 chunk；遠圖只留索引與輕量資料，貼圖素材可依 chunk 受控卸載。
- 貼圖圖集依使用頻率及區域拆分，防止進一張地圖就載入全部資產；避免在每次 Spawn 上同步解壓大型貼圖。
- 避免地圖/障礙、人物紙娃娃與掉落物共用昂貴且常駐的 RenderTarget；必要時量測 viewport 開銷。
- Camera 高解析度/超寬/縮放：送 Server 的 world view rectangle 是依實際 canvas transform 反算，不假設舊 800x600。Server 有可接受的最大負載範圍，縮放過度時 Client 顯示限制提示，不能讓 Server 跳過實際已顯示的重點玩家。

## 4. 移動、阻擋、尋路

- Server movement intent -> path validator：terrain/footprint/斜角/穿牆/NPC距離/怪物 melee line-of-sight。
- 靜態地圖 Blocking 專用不變 grid；怪物 path A* 或 JPS 依 benchmark 選，按地圖與 AI 優先級限制擴展節點與重算頻率。
- 尋路結果帶 map state/path version，過期目標/新牆/新位置要重新校驗；不可因路徑 worker 延遲而直接瞬移。
- 怪物每個移動路段按 Server monotonic time 持續 advance，AOI 按當前權威位置，而非提前把 current 設成 end。
- Client local 主觀移動採可恢復的視覺預測/行走方向；不以 Client 位置進行怪物追擊或掉落。
- 原始 Map13 台地階梯、Map15 城內可用來構建現代化**自製** fixture 的幾何/動作測試。

## 5. 地圖編輯器（M5）

- 工具可先用 Tiled 或純 Web 編輯器，需 ADR 比較；輸出 canonical GameData JSON/compiled binary，不能直接改生產 DB。
- 支援 Tilemap/Collision/Monster/NPC/Spawn/PVP/SafeZone/Portal、可視化拖曳、框選、復原/重做、複製/貼上、圖層/格線/座標。
- 存檔前檢查地圖 bounds、collision blocked spawn、無效 NPC/Quest/Monster/Item reference、重複 spawn_id、傳點目的地及地圖循環。
- 支援 Map13-like fixture 實際障礙繞行、門檻、不同解析度預覽、map import dry-run、改動 diff。
- 匯入舊二進位由獨立 CLI 提供，以 fixture 保證 decode/encode 對等；沒有來源授權不要提交 Old 原檔。

## 6. 測試

- world ↔ grid 10000種隨機/邊界值 round-trip，行走/禁止格/斜角與新/舊資料一致。
- 500怪在近/遠/視野邊緣跨區域，AOI Spawn/Despawn、AI 位置與怪物受擊權威一致。
- Chunk 來回進出/登出100次、地圖任務/掉落不重複建立、不出現記憶體單向成長。
- 不同 zoom/超寬視窗下，玩家無突然從遠處跳入畫面。

---

<a id="source-monster-content-workflow-md"></a>

## MONSTER_CONTENT_WORKFLOW.md

*Historical source: `docs/MONSTER_CONTENT_WORKFLOW.md` — SHA-256 `4bfd7dd7963f2b9e31b467062707b3ade9d5bf7a742ecf8528e254d701764a06`*

# Monster Content Workflow — ASSET205

日期：2026-11-14。狀態：**ASSET205 PASS / RUNTIME206 FULL MATRIX STILL NOT VERIFIED**。

## 目標

新增一種怪物時，不要求修改 C#、GDScript 或手工改生成 PNG/JSON。正式 source of truth 是 `content/monster_recipes/*.monster.json`；原始 SPR/ACT 維持 private，Runtime 資產由工具確定性重建。

一份 recipe 同時定義：

- map id
- monster definition id
- sprite identity
- runtime stats
- spawn positions / source flags
- 是否明確 replace 已有 definition

工具 `tools/monster_workflow.py` 會 fail-closed 驗證 source、ACT、8方位、stats、collision，再產 normalized definition、private preview、population v2，並**重建該 map 的共享 lifecycle sprite bundle** 與 Server copy。新增怪物不再建立新的 per-monster production atlas 分類。

## Recipe v1

```json
{
  "version": 1,
  "map_id": 13,
  "replace": false,
  "definition": {
    "id": 81,
    "sprite": "EN010A",
    "stats": {
      "hp": 120,
      "damage": 12,
      "move_speed": 72,
      "aggro_range": 520,
      "attack_range": 75,
      "chase_stop_range": 45,
      "attack_interval_seconds": 0.9,
      "respawn_seconds": 3
    }
  },
  "spawns": [{"p": [6428, 4881], "source_flags": 0}]
}
```

格式範例：`content/monster_recipes/example.monster.json.example`。只有 `*.monster.json` 會被 full rebuild 自動套用。

## 一次命令流程

先驗證，不修改 population：

```powershell
python tools/monster_workflow.py content/monster_recipes/my_monster.monster.json --validate-only
```

正式套用：

```powershell
python tools/monster_workflow.py content/monster_recipes/my_monster.monster.json
python tools/export_server_content.py
```

一般不需第二行：workflow 本身會同步該 map 的 population 到 `server/content`；完整 Runtime 驗證入口為 `tools/asset_converter/verify_runtime.ps1`，最後會統一 export collision/map contract。

## Source / animation Gate

sprite `ENxxxx` 會對應 private extracted source：

- `enemy/<sprite>_*.spr`
- `enemy/<sprite>.act`

工具固定要求 monster 5 個 behavior：`NS / NW / FA / NH / ND`，以 `sgtest.animation/v2` 與 `legacy_direction.py` 驗證 8 logical facing，共 **40 logical views**。缺 ACT、缺必要 SPR、timeline frame 越界、pivot/hash 不一致都直接失敗。

Animation source of truth 仍是 `sgtest.animation/v2`。Production asset boundary 依 [ASSET_ARCHITECTURE.md](ASSET_ARCHITECTURE.md)：每個 Monster visual 保持獨立 `Enemy/<sprite>/atlas.json`，Map population 只引用它；新增怪物不允許重建 Map + Monster shared atlas。512/1024/2048/4096 與 Dense/Atomic 都只是單一 asset 內部 packing 候選，不是 Runtime 類型；不能手改 atlas。

## Private preview

每次 validate/apply 都會在 `reports/monster_workflow/<sprite>/` 產：

- `definition.json`
- `preview.png`
- `report.json`

preview 是 5 actions × 8 facings 的 40-view contact sheet，依 source frame pivot 與 mirror 繪製，中心十字標示 origin。`reports/` 已 gitignore，因此不會把原版衍生圖公開提交。

真 HK2 EN010A smoke：16 source files、5 actions、40 logical views，preview **768×495**，spawn `[6428,4881]` 原位 walkable。

## Spawn collision Gate

`blocking.dat` 直接保留原版 Blocking.dat byte grid，Client/Server 使用相同 `CollisionGrid` 規則：16px cell、`cell & 7 == 0` 才可走。

- 預設：blocked/out-of-map spawn 直接拒絕，並回報最近可走建議。
- 只有明確 `--snap-spawns` 才自動移到 64-cell 搜尋範圍內最近 open cell。
- report 永遠保存 `requested_p / actual p / snapped`，不做靜默修正。

## Population v2：每 definition 的 Runtime Stats

ASSET205 把 population contract 從 v1 升成 v2。v1 Server 仍可讀，並套 legacy-equivalent defaults；v2 每個 definition 必須有明確 stats：

| 欄位 | 原 Map13 預設 |
|---|---:|
| hp | 30 |
| damage | 10 |
| move_speed | 65 |
| aggro_range | 500 |
| attack_range | 70 |
| chase_stop_range | 45 |
| attack_interval_seconds | 1.0 |
| respawn_seconds | 2.0 |

`World.Populate` 會把 stats 套進每個 Monster instance；AI 移速、追擊/攻擊範圍、攻擊間隔、傷害、respawn、MaxHp 都由 definition data 決定。Stress mode 循環 definition 時 stats 一起循環；死亡復活恢復該 definition 的 MaxHp。

數值有 fail-closed bounds；例如 `attack_range > aggro_range`、HP<=0、非有限浮點數都拒絕。

既有 Map13 已由原始 Dat 重建成 population v2：**9 definitions / 78 spawns**，所有 stats 與舊硬編碼值完全相同，因此 schema 升版不應改變既有手感。

## Runtime verify 不會重建 shared bundle

`tools/asset_converter/verify_runtime.ps1` 是 build 後的 Runtime 驗證入口：

1. 呼叫 `tools/asset_converter/cli.py build`，發布 per-asset 素材。
2. 建立 Map world/population contract；不重新合併 Map/Monster。
3. custom monster recipe 只修改 definition/spawn，並要求對應 `Enemy/<sprite>/atlas.json` 已存在。
4. export Server `world.json / population.json / blocking.dat` contract。

所以正式 custom monster 必須提交 recipe，而不是提交 private PNG/SPR/ACT，也不能藉 recipe 建立 shared map atlas。

## Replace / 重複 ID

- 新 recipe 若 definition id 已存在，預設 FAIL。
- 只有 recipe `"replace": true` 或 CLI `--replace` 才會刪除該 definition 原 spawns，改用 recipe 新 definition/spawns。
- 同 sprite 的多個 definitions 可以共用同一個 `Enemy/<sprite>/atlas.json` AssetStore entry；不同 sprite 永遠是不同 asset。definition id 只決定 stats/spawn/appearance reference，不建立 shared map atlas。

## 實測

### Offline / E2E

- Python `test_population + test_monster_workflow`: 5 tests PASS（之後完整回歸另列）。
- 真 EN010A validate-only：16 sources / 5 behaviors / 40 views / collision PASS / preview 768×495。
- temp assets 正常模式：v1 base → v2 10 definitions / 79 spawns，definition 81 stats 完整，Server population copy byte-equivalent。
- 無既有 atlas 案例：工具自己建 runtime v9 / schema v2 atlas，EN010A 1 page `252×684`、689,472 RGBA bytes、40 logical views。

### 真多人 temporary live add

為驗證新增流程真的能上線，曾在本機 generated content 暫時加入：

- definition 81
- sprite EN010A（與 definition 1 共用 visual）
- HP120 / Damage12 / MoveSpeed72 / Aggro520 / Attack75 / interval0.9s / respawn3s
- 1 spawn

Server log：`population_loaded monsters=79 definitions=10`。

20 clients / 原 population 79 monsters / 0 drops / 40s：

- mean FPS **59.998**
- frame P95/P99 **17.71 / 17.907ms**，max 28.901ms
- draw-call P95 149
- 20/20 `snapshot_received`，20/20 `prediction_ready`
- 0 ERROR / SCRIPT ERROR
- `source_files_unchanged_during_run=true`
- Client `monster_palette`: **10 definitions / 9 shared atlases**，證明同 sprite definition 共用 texture。
- Server tick P99：tick300 3.329ms；600/900/1200 約 0.285/0.261/0.259ms。

測完已從原始 Server Dat 重建恢復正式內容：population v2 **9 definitions / 78 spawns**，Client/Server population 一致。

## 尚未包含

- Recipe 目前針對既有 `sgtest.animation/v2` monster 五動作契約；特殊 Boss/多段技能/不同 action schema 需另立版本，不會猜。
- 掉落表、技能表、任務/AI behavior tree 尚未放進 monster recipe；ASSET205 只把目前 prototype combat stats 與 spawn/visual 資料化。
- AS005 原版逐格人工視覺與 AS008 發布權仍未完成。
- 30/45/60/75/90FPS、5/10/15 low-power、unfocus/minimize、1/2/4/8 開完整 trace 是 RUNTIME206，不因本次 60FPS live Gate 而完成。

## 驗證命令

```powershell
cd C:\SGO\SRC\SGTest
cd tools
py -m unittest test_population test_monster_workflow -q
cd ..
.\.cache\dotnet\dotnet.exe run --project server\tests\SGTest.Server.Tests.csproj -c Release
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_monster_visuals.gd
```

---

<a id="source-todo-pre-rebuild-20261115-txt"></a>

## TODO_PRE_REBUILD_20261115.txt

*Historical source: `docs/TODO_PRE_REBUILD_20261115.txt` — SHA-256 `59f9faeac52c2a534459125ed0aee84bf77fd3ac92daac7a5c3a18133d7c4e83`*

SGTest TODO — 2026-11-13 / M0 原版素材研究優先
唯一進度來源。標記：[ ] 未開始 [~] 進行中 [x] 完成且可驗證 [!] 受阻 [?] 待決。
研究前先看 SPEC.md 第7節、docs/LEGACY_ASSET_PIPELINE.md、docs/HK2_INVENTORY.md；任何下一 Session 不得把舊 demo 視為現存實作。

===== P0 / AS：素材來源與真實格式，未完成不得開始遊戲實作 =====
AS001 [~] HK2 loose 實體目錄已唯讀盤點 41,741 檔／1,333,254,792 bytes；Data1.dat 已按原版 `xnFileOpenGroup/FileData::ReadBuf/LzssDecompress` 直接證據解出 41,687 entries，真檔 585,022,714 bytes、SHA-256 `9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42`。已證實原版查找 precedence 為 loose override > archive base，並有選擇性 materializer。`tools/scan_group_archives.py` 已對目前 HK2 的 41,741 個實體檔做同格式 footer + LZSS index fail-closed 掃描：1 candidate／1 valid／0 rejected，唯一 archive 為 Data1.dat。HK2 `Client.bin`（2009-02-18）與 `SGOClient.bin`（2008-02-28）PE timestamp 均晚於參考 Paradise.exe（2004-11-24），已排除 binary-identical；兩者仍含 Data1/ITF/ground/ACT 同系資源字串。詳 docs/HK2_DATA1_ARCHIVE.md、docs/ASSET_EVIDENCE_MATRIX.md。安裝包／更新來源 provenance 仍待建立。
AS002 [~] HK2 全 178 個 ACT 已解析幀、循環、事件；Client.bin/SGOClient.bin 已由 `tools/audit_action_timing_binary.py` 唯一驗證 40/25、action decrement、`xnAction::SetSpeed`、duration scale、fixed GameManager loop 與 `SystemSetFrame(50)`，default fixed tick=20ms。Paradise reference 又由 `audit_action_speed_reference.py` 證實 `CSGOChar::SetSpeed` world-speed 與 ACT action-speed 分離，31 個可保守辨識的 immediate caller 全使用 speed=50；dynamic counter=0 仍至少等下一個 20ms fixed tick。ACT schema v3、50 組怪 Godot 12,976 playback steps timing_verified=true。詳 docs/HK2_ACTION_TIMING.md、docs/HK2_ACTION_SPEED.md、docs/HK2_DIRECTION_MAPPING.md。仍待人物 numeric action ID、HK2 caller/packet/data-driven speed、event callback 與原版實際視覺。
AS003 [~] HK2 實體類別、ITF→SPR 引用、9 份人物 draw-order 表、219 份地圖 manifest 與 186 份地圖 SIF 初盤，詳 docs/HK2_INVENTORY.md、docs/HK2_UI_REFERENCES.md、docs/HK2_DRAW_ORDER.md、docs/HK2_MAP_REFERENCES.md、docs/HK2_SIF_RESEARCH.md；122 筆 UI SPR 引用中 59 筆無實體同名檔，Blocking 只有 121/219 引用有實體檔，SIF 有 2 筆缺 SPR。人物鏡像規則已查到。`item.itd` 9,096 筆定義已按 HK2 `Client.bin` 0x004F4ECC～0x004F4F54 的 ÷500 ground-page 公式驗證：8,049→ground.spr、1,047→ground1.spr、0 越界；13 個 duplicate ID 中 12 個地上圖衝突已 quarantine，舊版 GetItemByID 最終委派 CRT bsearch，未提供可移植 first/last precedence。詳 docs/HK2_ITEM_LIBRARY_MAPPING.md。裝備實際遮擋、duplicate HK2 同版 precedence 與其他類別缺口仍待補。
AS004 [~] tools/convert_assets.py 已從 HK2 全量轉出 40,224/40,224 SPR、178/178 ACT、186/186 SIF、9/9 draw-order 表；ACT schema v3 保留 raw_time／default update calls／50Hz default ms／runtime speed override flag，178/178 真 ACT重跑 PASS。動態 action-speed 的整數除法與 minimum-one-tick contract 已查清，且不得由 world movement speed 或 render FPS 推導。RGBA/origin/source SHA/SIF 原始值仍保留。詳 docs/HK2_SPR_POC.md、docs/HK2_ACTION_TIMING.md、docs/HK2_ACTION_SPEED.md、docs/HK2_DRAW_ORDER.md；人物 paper-doll/action mapping、HK2 dynamic-speed caller 與原版視覺仍未完成。
AS005 [ ] 原版視覺/影格/方向/透明度/對齊/裝備疊圖實證比對；含靜止/行走/攻擊/受擊/死亡，截圖與錄影人工確認。
AS006 [~] tools/view_legacy_asset.py 可逐格檢查原始 SPR/manifest、鏡像、origin、透明與縮放；tools/view_enemy_animation.py 已支援 catalog v2，verified timing 時按每幀 default ms 播放，舊/unverified catalog 才 fallback 100ms，且只解碼目前 action/facing 引用頁。人物紙娃娃圖層、批次匯出與原版視覺校驗仍待做；詳 docs/HK2_ASSET_VIEWER.md、docs/HK2_ACTION_TIMING.md。
AS007 [~] 怪物 Atlas 已進一步驗證 atomic MaxRects v3：同原始 SPR 不拆頁、不同 SPR block 可 co-pack。Data1+loose overlay 的 29 種已知地圖怪物 29/29 ACT 完整，atomic 1024 為 32 pages／62,287,496 RGBA bytes，1,160/1,160 action×facing 單頁；Godot headless 29 groups／145 actions／1,160 directions／8,792 steps PASS。17 張真 Enemy.dat 地圖已編譯 MapAssetPalette，單圖 1～10 visuals、約 3.305～17.441 MiB atlas RGBA；Map13 真 78 怪與 300 怪 RTX3050 A/B 未增加 draw calls，詳 docs/HK2_MAP_ASSET_PALETTE.md。正式 StreamingManager、GPU upload p99、切圖／登出回收、人物／物品完整策略仍未驗證。
AS008 [!] 確認原版圖片、UI、音效的本地研究與公開發布/轉換權利；未確認權利不放 GitHub。
AS009 [x] 已建立 docs/ASSET_EVIDENCE_MATRIX.md：集中記錄 Data1／Client binaries／SPR／ACT／人物 draw-order／item.itd／ground pages／Map13 Enemy.dat 的具體 SHA、HK2／參考 Paradise.exe 函式位址、可重跑驗證與 PASS/PARTIAL/BLOCKED。AS009 本身 PASS；M0 Gate 明確 NOT PASSED，因 AS005 原版逐格視覺與 AS008 發布權利仍未完成，另有人物 action/paper-doll、duplicate item ID precedence 等技術缺口；ground1 runtime 選檔已於 2026-11-14 由 HK2 binary 直接關閉。

AS001/AS002/AS007（2026-11-14 Data1＋真地圖）[~] 新增 tools/legacy_group_archive.py、materialize_legacy_overlay.py、build_enemy_map_palette.py、compile_map_asset_palette.py、atomic MaxRects v3 與 GPU A/B runner。Data1 41,687 entries 可只讀索引／選擇性抽取；17 張已知地圖共 29 visual，base+overlay materialize 464 files（461 base／1 override／2 loose-only），29/29 ACT 完整。Map13 78 spawns／9 visuals／9 pages，atomic v3 Video Mem 約33.55 MB、dense約34.54 MB，draw calls相同；300怪壓測亦同。Python 單元測試目前 63 項 PASS。原版視覺／人物紙娃娃／正式 Client runtime 仍 NOT VERIFIED。
AS007（2026-11-14 atomic balanced 全 catalog）[~] 新增 tools/plan_enemy_atomic_sizes.py，以真 SPR 在 512/1024/2048/4096 執行 atomic packing 實測後選尺寸；50 組得到 512×1、1024×21、2048×19、4096×9，共 65 pages／442,769,264 RGBA bytes，2,000/2,000 action-facing 單頁，Godot 12,976 steps PASS。RTX3050 1440×900/300怪：8種 draw p95 286→275、50種 372→372；frame p95 無可宣稱退化，完整 50 種壓力 Video Mem 約 +13.7 MB，因此 runtime 仍必須 MapAssetPalette 串流，不得全量常駐。`py -m unittest discover -s tools/tests -q` 77 tests PASS（加入 planner 測試後）。詳 docs/HK2_ATOMIC_BALANCED_ATLAS.md。

===== 已確認的事實（不是素材解析完成） =====
[x] 已指定後期《星夢 Online》金色 UI 的視覺目標。
[x] 已有 HK2 全量 inventory、舊版反編譯位址與唯讀盤點工具可用作研究起點。
[!] 早期盤點僅見部分 SPR/SIF/ITD；後期 ITF 實體與完整人物/地圖原圖未確認。
[ ] 原版 Client 安裝資料夾實際 inventory、格式 Decoder、Viewer、Godot 真素材匯入全部未完成。

===== 後續階段（暫停；不得搶跑） =====
M1 [ ] 根據實證建立 Godot 4 Client skeleton、.NET Server、版本化契約及 CI（之前 demo 已從 main 清除；需要時可從 Git 歷史取回）。
M2 [ ] 登入/選角/地圖/人物方向/移動/普攻/受擊/死亡可玩多人切片。
M3 [ ] 多人/怪物/掉落/同步/AOI 的大規模壓測及不跳格驗證。
M4 [ ] 資源共用/分組 Atlas/地圖串流/AFK/登出回收；不能降低網路或戰鬥邏輯速度。
M5 [ ] 物品/強化/商店/交易/技能/任務/寵物/機甲系統。
M6 [ ] PVP、社交、小屋、多 Channel、維運及最終授權檢查。

===== 2026-11-13 Git cleanup =====
早期 Godot World Demo、金色 Gallery、.NET Server 雛形及其 CI 從 main 移除，只保留研究工具、研究索引與規格文件；不改寫 Git 歷史。
所有原有 B001/B002/B007 的原型完成敘述不代表現存程式碼；正式重做須從研究 Gate 通過後開始。
每項任務交付：證據位置/來源 SHA、測試命令、PASS/FAIL/NOT VERIFIED、Git commit、下一步。

===== 2026-11-13 HK2 本機研究工作日誌 =====
AS001（部分）| 2b7cfe4 | tools/inventory_client.py、docs/HK2_INVENTORY.md | `py -m unittest tools.tests.test_inventory_client` PASS（2）；HK2 實體檔 SHA/magic 全量盤點 PASS（41,741）；Data1.dat 內容與來源版本未驗證。
AS004（部分）| dc930b2 | tools/convert_assets.py、docs/HK2_SPR_POC.md | `py -m unittest tools.tests.test_convert_assets` PASS（3）；Enemy 769/769 SPR 轉換、Player/Interface/Map/Effect 各20樣本 PASS；原版動作／方向／幀時長未驗證。
AS002（部分）| a182e60 | tools/inspect_act.py、docs/HK2_ACT_RESEARCH.md | 178/178 ACT 原始結構解析 PASS；Godot 4.7.2 headless PNG/manifest 讀取 PASS；時間單位、方向和 callback 語意未驗證。
AS004（部分）| 6d51f91 | ACT raw JSON 併入 convert_assets.py | 8 個 Python 單元測試 PASS（當時）；HK2 全量 40,224 SPR／178 ACT 本機批次轉換 PASS（零失敗，後續工作樹驗證）；正式 Godot runtime 包裝與原版畫面比對未完成。
AS003/AS007（部分）| 05b6d4f | 20 ITF 引用盤點與 Godot headless PNG 解碼基準 | 9 個 Python 測試 PASS；20 SPR 樣本分幀136頁/83.584ms，512 atlas40頁/66.662ms（5次中位，單次實驗）；GPU/正式多人未驗證。
AS003（部分）| d14e1ed | 9 份人物 draw-order、219 份地圖引用盤點，client/ 與 server/ 分開建立入口 | 11 個 Python 測試 PASS；地圖 Blocking 121/219 有實體檔，缺件與鏡像語意待查。
AS001～AS009（參考版本研究）| origin/main e497e21 | docs/LEGACY_ASSET_EVIDENCE_20261113.md、tools/legacy_spr_decoder.py、tools/legacy_asset_viewer.py | 參考版本的 4 個 NPC SPR 結構與 1 個 SIF 樣本，曾有 GitHub CI 10 項測試通過；該文件的「尚未取得完整安裝目錄」是當時狀態，現以 HK2 本機盤點與轉換報告為準。與本分支合併後 Python 24 項測試 PASS。
AS007（部分）| 86d7406 | tools/build_atlas_groups.py、tools/benchmark_group_atlas.gd、tools/benchmark_atlas_render.gd、docs/HK2_ATLAS_CANDIDATES.md | 乾淨 HK2 的 Enemy 15 SPR/111 幀、Player 衣物 59 SPR/219 幀、Map 1 SPR/1 幀，測 512/1024/2048/4096 候選；Godot 4.7.2 headless 解碼與 RTX 3050 OpenGL 300 同類實例實機 PASS，Python 27 項測試 PASS。Before：僅每 SPR 分幀/固定頁面初測；After：同外觀局部群組、2px 延展、來源 SHA/幀對應、尺寸與 draw calls/Frame p95 初測。多種類／多人／掉落、VRAM、切圖回收與原版視覺仍 NOT VERIFIED；可依研究文件命令重跑。
AS007 CI | 492b804 | .github/workflows/legacy-asset-tools.yml | 初次分支 CI 因未安裝 Pillow 失敗，補 `tools/requirements.txt` 安裝後 GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43461384594 PASS；本機 `py -m unittest discover -s tools/tests -q` 27 tests PASS。CI 為 synthetic fixture，不含 HK2 媒體或真 GPU；GPU 數據僅本機初測。
AS007（混合負載與生命週期部分）| 1b2e8a5 | tools/select_mixed_benchmark_groups.py、tools/benchmark_mixed_render.gd、tools/benchmark_atlas_lifecycle.gd、docs/HK2_MIXED_ATLAS_BENCHMARK.md | 8 種高面積怪／20 玩家衣物代理／500 MS 小圖代理，1024/2048/4096 在 1080p 三輪及 4K 單輪 Godot RTX 3050 實測；1024 Video Mem monitor 約285.5 MB、4096 約324.8 MB，draw calls 均892。10 次載入／釋放 ImageTexture 各回基線。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43537407091 PASS。此時 4K 僅屬超規壓力測試，後續正式目標依使用者修訂為視窗寬約 800～1440 像素；資料同步／真掉落圖／正式資源管理仍 NOT VERIFIED。
AS002（怪物映射部分）| 2fd9729 | tools/legacy_direction.py、tools/audit_enemy_animations.py、docs/HK2_ENEMY_ANIMATION_MAPPING.md | 原版參考 SetEnemSprite/SetDirection/InitAction 位址顯示 SWDAH→NS/NW/ND/FA/NH，邏輯八方重用／鏡像少數來源圖；HK2 53 組 ACT/SPR 稽核 50 組來源與索引可對上，EN060A／EN061A 的特殊引用與 EN115A 的 D 索引待查。不能以 SPR 張數或 ACT 播放紀錄數判定少圖；原版即時畫面仍 NOT VERIFIED。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43538554588 PASS。
AS002/AS007（怪物圖集動畫資料契約部分）| 73ab4b8 | tools/compile_enemy_atlas_catalog.py、tools/godot_enemy_catalog_probe.gd、docs/HK2_ENEMY_ATLAS_CATALOG.md | Before：ACT 方位來源稽核與 Atlas 頁面各自獨立；After：以來源 SHA、影格索引、八方位鏡像及實際頁面區域編譯索引，引用不完整者隔離。`py -m unittest discover -s tools/tests -q` 34 tests PASS；HK2 8 種怪 1024/2048 Atlas 各編譯 8/8、隔離 0，Godot 4.7.2 headless 各解碼 8 群組／40 動作／320 邏輯方位／2336 播放步 PASS。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43538989330 PASS（synthetic fixture，不含 HK2/GPU）；原版動作毫秒、實際視覺、3 個特殊怪群組、正式資源生命週期、多人同步 NOT VERIFIED。可依 docs/HK2_ENEMY_ATLAS_CATALOG.md 重跑。
AS002/AS007（50 組全量候選）| 39663b9 | tools/audit_enemy_animations.py 新增可重跑的 `--complete-group-list`，docs/HK2_ENEMY_ATLAS_CATALOG.md 記錄全量流程；使用者允許每怪／物品獨立圖片，但須由同場 A/B 決定。Before：需手動挑 8 組測試；After：乾淨 HK2 中 50 個稽核完整群組自動列清單並全部打包；1024 atlas 50/50 成功、143 頁、實際 RGBA 433,336,404 bytes；Godot 4.7.2 headless 驗證 50 群組／250 動作／2,000 邏輯方位／12,976 播放步 PASS；`py -m unittest discover -s tools/tests -q` 35 tests PASS。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43539372112 PASS（synthetic fixture，不含 HK2/GPU）；433 MB 是離線全量頁面總和，不是目標常駐工作集；3 個特殊怪、原版視覺／動作時序、逐來源貼圖與局部分頁 A/B、正式 Client/Server NOT VERIFIED。
AS003/AS007（真地上物品與逐圖 A/B 部分）| 97897bc | 原版參考 `CSGOItemDrawLogic::InitItem` `.text:004CCD17`～`004CCDA9` 直接使用 `Interface\ground.spr`；HK2 實體 SHA `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6` 可解 500 非空幀。Before：MS 機甲圖只作掉落尺寸代理；After：tools/build_ground_candidates.py 以真 ground 影格生成逐圖／1024／2048／4096 同像素候選，tools/benchmark_ground_render.gd 在 1440×900、500 實例實測 25／500 圖種各三輪。500 種逐圖→1024：draw calls 572→73、載入中位 161.763→36.151ms、Video Mem monitor 21,347,039→23,513,803 bytes；frame p95 約 8ms 差異不明顯。`py -m unittest discover -s tools/tests -q` 36 tests PASS；GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43540513923 PASS（synthetic fixture，不含 HK2/GPU）。item 定義 ID→ground 幀、真地圖共現、GPU upload、正式資源回收 NOT VERIFIED。詳 docs/HK2_GROUND_ART_BENCHMARK.md。
AS003（物品定義→地上圖號部分）| 371b73c | tools/inspect_item_library.py、docs/HK2_ITEM_LIBRARY_MAPPING.md | Before：只知 `ground.spr` 有 500 幀，物品 ID/圖號關係未知；After：參考 `CSGOCore::InitItemLib` `.text:00404D5E`～`0040647E`、`GetItemByID` `.text:00403AC1`～`00403B75`、地上物品建立 `.text:004D87CA`～`004D8845` 的欄位對應，HK2 `item.itd` 解封後 8,236,329 bytes、9,096/9,096 筆結構完整，8,049 筆圖號對應 `ground.spr`、1,047 筆為 `ground1.spr` 連續分段候選；13 個重複 ID 保留。`py -m unittest discover -s tools/tests -q` 38 tests PASS；GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43541621190 PASS（synthetic fixture，不含 HK2/GPU）。`ground1` 執行時選檔、重複 ID 優先序、真地圖掉落共現、原版畫面 NOT VERIFIED。
AS006（怪物 ACT 離線 Viewer 部分）| 2a5b70b | tools/view_enemy_animation.py、tools/tests/test_view_enemy_animation.py、docs/HK2_ASSET_VIEWER.md | Before：單一 SPR 檢視器無動作／方位選單；After：讀本機 enemy_catalog.json，可選怪物、S/W/D/A/H、1～8 方位，顯示來源影格、raw origin、鏡像、raw_time、Update 呼叫數與事件 raw 位置；只解碼選中頁面，視窗最大 1440×900，固定 100ms 標示人工預覽。`py -m unittest discover -s tools/tests -q` 39 tests PASS；HK2 EN023A 5 動作／40 方位／272 播放步的 atlas 還原像素與來源 SPR、origin 全相等；Tk GUI 短時啟動／關閉 PASS；GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43542323912 PASS（synthetic fixture，不含 HK2/GPU）。原版錄影／畫面人工對照、實際動作毫秒、鏡像錨點及人物紙娃娃 NOT VERIFIED。
AS007（逐怪物多尺寸候選）| 1f36940 | tools/benchmark_enemy_size_candidates.gd、tools/plan_enemy_atlas_sizes.py、tools/build_atlas_groups.py、docs/HK2_PER_ENEMY_ATLAS_SIZES.md | Before：同批怪物固定尺寸候選；After：50 組各測 1024/2048/4096，依最低實際 RGBA 產生 42/4/4 組尺寸計畫並從乾淨 HK2 重建；混合尺寸 133 頁／432,604,792 bytes，對全1024的143頁／433,336,404 bytes。Godot headless 50組／250動作／2,000方位／12,976播放步 PASS；Python 40 tests PASS。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43543214702 PASS（自造 fixture，不含 HK2/GPU）；正式 GPU 工作集／串流尖峰／多人與原版視覺 NOT VERIFIED。
AS002（人物雙槽位分層部分）| 8b9d7e7 | tools/audit_character_layers.py、docs/HK2_DRAW_ORDER.md | Before：人物 draw-order 的 +2 後綴語意未知；After：參考 LoadDrawOrderDat `.text:004082D2`～`004084BC` 證實 L-Arm+2→6,5、R-Arm+2→8,7、Body+2→10,9、leg+2→13,12，保留配對順序與武器槽位別名；乾淨 HK2 兩份主要人物表 102/102 行展開 PASS，Python 41 tests PASS。GitHub Actions https://github.com/kevin840307/SGTest/actions/runs/43544736868 PASS（自造 fixture）；各槽位貼圖來源、人物 action ID／動作時間、HK2 二進位一致性及原版視覺 NOT VERIFIED。不得把裝備強化別名移除套到 draw-order。

AS003（HK2 ground page runtime 直接證據）| pending | tools/inspect_item_library.py、tools/tests/test_inspect_item_library.py、docs/HK2_ITEM_LIBRARY_MAPPING.md、docs/ASSET_EVIDENCE_MATRIX.md | HK2 2009 Client.bin `0x004F4ECC～0x004F4F54` 直接證實 zero-based picture index 以 500 分頁：page0=ground.spr、pageN=groundN.spr；2008 SGOClient.bin 同樣具 ÷500 流程。真 item.itd 9,096/9,096：8,049→ground.spr、1,047→ground1.spr、0 越界。13 duplicate ID 中 12 個地上圖衝突、1 個等價；參考 GetItemByID→CRT bsearch 不足以建立 duplicate precedence，衝突保持 quarantine。`py -m unittest discover -s tools/tests -q` 66 tests PASS；原版視覺、真掉落共現、ground1 GPU A/B 與 HK2 duplicate precedence 仍 NOT VERIFIED。

AS001（HK2 全目錄 group archive 掃描）| pending | tools/scan_group_archives.py、tools/legacy_group_archive.py、tools/tests/test_legacy_group_archive.py、docs/HK2_DATA1_ARCHIVE.md、docs/ASSET_EVIDENCE_MATRIX.md | 對目前 HK2 全 41,741 實體檔以 xnFile footer 結構做 fail-closed 預篩，再以 LZSS index／entry bounds 完整驗證；footer candidate=1、valid=1、rejected=0，唯一為 Data1.dat。這關閉目前工作目錄『是否另有同格式 group archive』缺口，但原安裝包／更新來源 provenance 與授權仍未完成。`py -m unittest discover -s tools/tests -q` 67 tests PASS。

AS002（HK2 ACT default timing）| 074b639 | tools/audit_action_timing_binary.py、inspect_act.py、convert_assets.py、audit_enemy_animations.py、compile_enemy_atlas_catalog.py、godot_enemy_catalog_probe.gd、view_enemy_animation.py、docs/HK2_ACTION_TIMING.md | Client.bin/SGOClient.bin 各唯一命中 40/25、decrement、duration scale、fixed loop、SystemSetFrame(50)，default tick=20ms；HK2 178/178 ACT schema v3 PASS；50 組怪 12,976 playback steps Godot 4.7.2 headless timing_verified=true；真 EN023A S facing1 首幀 180ms；`py -m unittest discover -s tools/tests -q` 83 tests PASS。仍待 SetSpeed caller/packet、人物 action ID、event callback 與 AS005 視覺。

AS002（movement/action-speed separation）| aba7c04 | tools/audit_action_speed_reference.py、audit_action_timing_binary.py、docs/HK2_ACTION_SPEED.md、CLIENT_RUNTIME_ISOLATION.md、NETWORK_AND_MOTION.md | Paradise reference 證實 CSGOChar::SetSpeed 只寫 world-speed，不呼叫 ACT speed；CDDxDrawObj/CELxnObj vtable +0x2C=SetActionSpeed，31 個保守 InitAction→immediate→SetActionSpeed sequence 的 immediate 全為50。HK2 Client.bin/SGOClient.bin 各唯一命中 xnAction::SetSpeed signature；dynamic counter=0 仍至少 1×20ms fixed tick。py -m unittest discover -s tools/tests -q：85 tests PASS。仍待 HK2 caller/packet/data-driven speed、人物 action mapping、event callback、AS005 視覺。

===== 使用者 2026-11-14 最新指示：合併與分離 =====
INT001 [~] master 合入 main，直接修改目前工作目錄；Godot Client 放 client，C# Server 放 server。使用者已授權繼續開發，不代表 M0 原版逐格視覺／發布權利已通過。
修改前：main 只有研究工具與空工作區，master 有混合 Godot Client/Server 原型。
指標：保留研究工具和 Git 歷史；獨立 Godot Client；C# Server 不依賴 Godot／圖片；移動、戰鬥、掉落、拾取不退化。
回歸：main Python 研究測試、素材管線測試、Godot 狀態與動畫測試、C# world/碰撞/輸入測試、C# Server + Godot Client 實際多人及原生畫面。

INT001 實測更新：C#/.NET 8 build 零警告；339 domain checks；main 研究測試 79（移入新資料夾前）；Godot 5 組狀態／預測／地圖／方向／跨語言 wire 通過；8 client／1000 monster／5000 drop 30秒與2人combat-arena15秒通過。後續在指定新目錄重新驗證後記錄 merge SHA。原有未提交研究更動保留於 worktree，不納入本次提交。

INT001 [x] 在指定的 C:\SGO\SRC\SGTest 重新驗證：89 個研究測試、5 個 runtime asset fixture、3 個 map fixture、342 個 C# domain/wire 檢查，以及 Godot 方向／碰撞／replica-clock／prediction／跨語言 wire 全 PASS。實際 C#/.NET8 + Godot：8客戶端／1000怪／5000掉落30秒 PASS；原生1440×900 GPU視窗 PASS。命令見README；報告見reports/README.md、network_load.json、rendered_probe.json。merge commit 的父節點包含 e2d84c2(main) 及02ca1db(master)，SHA於git log --merges -1查詢。CI workflow已加入，遠端執行結果尚待推送後確認。此項只完成整合／目錄／C#移植，其他M0/M1/M2缺口未改標完成。

INT001 CI修正：6eb4941 的 Linux C# server job PASS，原 main研究CI PASS；runtime asset CI 的地圖別名測試意外依賴 .cache 原版檔案，已改為必跑的自製 BLG→STH→SPR fixture，原版案例僅在私有資料存在時執行，避免上傳素材。重跑4個本機map tests PASS。CI：https://github.com/kevin840307/SGTest/actions/runs/43575276776；修正後結果另見最新 run。

INT001 最終整合證據：merge 6eb4941（parents e2d84c2＋02ca1db），可攜測試修正16a32cd，均已正常push origin/main。16a32cd 的 Runtime C# and asset contracts 與 Legacy asset inventory tools 均為SUCCESS：
https://github.com/kevin840307/SGTest/actions/runs/43575396466
https://github.com/kevin840307/SGTest/actions/runs/43575396469
本機342項C#檢查、89項目前工作樹研究測試、Godot與多人原生測試PASS；CI只驗證已提交版本，不包含工作樹尚未提交的ACT／人物研究修改。完整遊戲與原版視覺／發布權利Gate仍未完成。

INT002 [~] 接入已知Map13原版怪物出生／種類與多圖集呈現。
Before：Server以固定種怪物隨機填滿，Client固定EN004A；已知原版78個出生／9種visual仍只作研究報表。
指標：預設真出生與定義ID，壓測明確指定數量；同種共用圖集，不把不同種類畫成同一隻；死亡復活保留種類與出生區；只載入當前地圖palette。
回歸：自製population fixture、28-byte快照與v2契約、每實體revision／方向／同種共享、C#原版內容匹配與碰撞、多種類Godot原生場景及8-client壓測。舊版ENet及v1報告不能當v2證據。

INT002 [x] Map13 78原版出生／9種類已接入；原版CLI啟動log為78／9，protocol2。Python合成fixture PASS；C#346 checks PASS；Godot9visual／360 action-facing＋共享reset PASS，八方向／碰撞／replica／prediction／跨語言wire PASS；8client／1000怪／5000掉落30秒與2client combat15秒 PASS；原生1440×900平均58.50FPS、P9523.50ms，9圖集RGBA約15.66MiB、分影格載入約121ms。實際報告見reports；未把私有population或圖片納入Git。下一步INT003圖集排列一致性A/B。

INT003 [~] 使用者補充：圖集直橫方向需一致、方便新增，必須由原始檔腳本轉換且兼顧資源／流暢度。
Before：方向區塊已集中，但shelf最小面積候選會在直／橫之間變動。
指標：比較compact與統一landscape候選，維持不旋轉原影格、同方向不拆、像素可還原；完整catalog幾何面積／空白成本、代表素材Godot載入及多種類FPS有直接證據後才改預設與重建。
回歸：人工細長／不同方向fixture、原始SPR逐幀像素回建、原版9種runtime及C#同步不受排法影響；增量重建只需指定entity。

INT003使用者最新補充：只統一橫向仍不夠，需容易看懂方向／動作／影格規律，容許沒有顯著影響的記憶體差異；載入不能讓玩家感到lag。下一個A/B以方向panel→動作row/grid→原始frame順序為主，保留原始方位重用並提供可讀預覽，不以最緊密但難讀的矩形排序作最終答案。
INT004 [x] 新增怪物資料流程完成：`content/monster_recipes/*.monster.json` 為 source of truth；`tools/monster_workflow.py` 驗證 ID／SPR／ACT／5 actions×8 facings／runtime stats／collision spawn，產 private 40-view preview、normalized definition、必要時 runtime atlas，並更新 population v2＋Server copy。full rebuild 自動重套 recipes，不需改 C#/GDScript 或手改生成 PNG；詳 docs/MONSTER_CONTENT_WORKFLOW.md。

INT003補充驗收：使用者要求GPU載入速度、CPU／記憶體、容易卸載與快速重載。需拆量PNG解碼、主執行緒GPU提交／frame尾延遲、GPU紋理計數與容量，以及重複載入／卸載後基線；圖集張數／大小依這些實測與可讀性一起決定，不能只用PNG大小或CPU解碼數字。

===== 2026-11-14 使用者指定資源架構重評，先交付基準 =====
INT003 [?] 先前橫向／readable／grid排法及asset_stream為實驗，不能視為最終方案。v8 consistent曾全量回建282,990影格PASS，但使用者最新架構／可讀性／GPU／引用生命週期要求尚未完成。
PERF001 [~] 首個交付：SPEC第0節的可重跑基準工具＋實際JSON／報告，先測目前同步載入／卸載30次、CPU解碼／GPU提交／同步等待、RAM／GPU峰值、Draw Calls及20player/300monster/1000drop場景；資料不足標NOT VERIFIED。
ASSET201 [x] 標準定義 v2：monster/character→behavior→8facing→frame，來源／alias／pivot／ACT／z-order 已與 pack 位置解耦；v1 明確拒絕。真素材 audit 16 definitions／992 logical facings／624 character draw-order views／0 failure，四種族 core audit A/B/G 各282來源、R235來源、各104 states、0 issue。詳 docs/NORMALIZED_ANIMATION_DEFINITION.md。
ASSET202 [x] v2 normalized clip-atomic 候選完成：Map13 9 visual／78 spawns 依 hot6/warm2/cold1，實測 512/1024/2048/4096 並產 7 組候選；manifest 在寫出前模擬 EntityAtlas direct/alias resolution，360 logical facings 全部必須符合 schema v2。7 組跑過 30-cycle lifecycle＋20client/300monster/1000drop；正式 clean finalists 詳 docs/NORMALIZED_ATLAS_CANDIDATES.md。
ASSET203 [x] 依 clean PERF001 選 `normalized_memory` 作 ASSET204 provisional base：14 pages／10,741,992 RGBA bytes；multiplayer P95/P99 21.54/23.81ms、draw P95 421、Video約45.63MiB、texture/RAM recovery PASS。同步 loader frame P99 53.52ms FAIL，明確不得宣稱 Atlas 已解決頓挫。
ASSET204 [x] bounded async RuntimeAssetStore 完成：owner refs／共享／priority preload＋viewport on-demand／worker decode／16MiB ready queue／production 每幀1 page+1.2ms GPU commit／32MiB unowned LRU cache／failed retry／generation token cancel／map-owner reset／close purge 已實作與測試。`normalized_memory` 30-cycle：同步 P99 53.52ms→async P95/P99 16.885/17.028ms、load P95 249.914ms/15 frames、texture+resident+RAM recovery PASS、final resident=0；20client/300monster/1000drop：59.99FPS、P95/P99 22.109/23.949ms、0 ERROR、20/20 snapshot/prediction ready，穩態 Server P99 5.56～6.43ms。詳 docs/RUNTIME_ASSET_STORE.md。
ASSET205 [x] 新增怪物資料流程完成：recipe→來源/動作/stats/spawn fail-closed→normalized definition→memory-first clip-atomic atlas→40-view private preview→population v2/client+server。真 EN010A validate/E2E PASS；臨時 live-add definition81 後 10 definitions/79 spawns，20 clients 40s：59.998FPS、P95/P99 17.71/17.907ms、10 definitions/9 shared atlases、20/20 snapshot+prediction ready、0 ERROR，Server穩態P99約0.26～0.29ms；測後已由原始 Dat 恢復正式 v2 9/78。完整不同FPS/失焦/多開 trace 統一留給 RUNTIME206。詳 docs/MONSTER_CONTENT_WORKFLOW.md。

PERF001補充（2026-11-14）：使用者觀察畫面頓挫，原因待定位；加入frame pacing／獨立實體插值／prediction修正／snapshot queue／載入CPU-GPU尖峰分段，不以平均FPS判PASS。
RUNTIME206 [x] Render／Client Logic／Network／Server 隔離正式 Gate 完成：一般遊玩30/45/60/75/90FPS、Visual 5/10/15低功耗、no-world、真 Windows unfocus/minimize/restore、Unfocus toggle、1/2/4/8開皆以同 trace 驗證；共同 Server tick authoritative state hash 一致，physics/input 固定約30Hz，queue P99 正式矩陣皆<40ms。預設Render60FPS，UI可選30/45/60/75/90；失焦節能可關，最小化永遠停世界呈現但不斷同步。完整證據見 docs/RUNTIME_ISOLATION_VERIFICATION.md。

RUNTIME206補充：Client一般遊玩支援30～90FPS；最低覆蓋30/45/60/75/90，5/10/15為掛機另測，FPS不影響Client↔Server傳輸／固定邏輯。原120FPS項目保留額外壓測用途，不是正式支援範圍。

PERF001 [x] 首個「基準工具＋原始JSON＋報告」交付，非效能全部達標。工具benchmark_runtime_baseline.py／report_runtime_baseline.py／build_baseline_candidates.py及client/tools/benchmark_asset_lifecycle.gd；報告docs/RESOURCE_BASELINE.md與reports/runtime_baseline_summary.json。7組30次raw texture回收PASS；同步載入frame P99全部FAIL；4組20client/300monster/1000drop一般frame達標，但Client RSS 202.65～207.73MiB高於200MiB，FAIL。詳細命令／NOT VERIFIED項目見報告，ADR-027。
測試：python -m unittest discover -s tools -p test_runtime_baseline.py（7 PASS）；tools/tests（89 PASS，包含保留的本機研究修改）；test_assets（5）、test_maps（4）、test_population（1）PASS；.cache/dotnet/dotnet.exe run --project server/tests/SGTest.Server.Tests.csproj -c Release（346 PASS）；Godot tests/test_directions/navigation/replica/prediction/wire/monster_visuals.gd全部PASS。本機GPU不是CI替代品，完整低FPS／背景trace／owner-LRU登出／冷快取／實體driverVRAM仍NOT VERIFIED。
ASSET201 [x] 完成：`sgtest.animation` schema v2 已保留 raw ACT／alias／pivot／source／event／draw-order raw layers＋expanded slots；Python tools/tests 89 PASS、definition 10 PASS、Godot consumer PASS；本機9怪＋Boy6 core＋death共16 definitions／992 facings，z-order 624/624 PASS；四種族 core mapping complete。舊 runtime 有182個方向差異已報告、不靜默接受。AS005視覺與AS008授權仍未完成。
ASSET202 [x]／ASSET203 [x] 完成：normalized clip-atomic 7 候選已 A/B，clean finalists 選 normalized_memory 作 ASSET204 基底；詳 docs/NORMALIZED_ATLAS_CANDIDATES.md。同步 loader P99 仍 FAIL，packing alone 不算解決。
ASSET204 [x] 完成並通過 PERF001 async lifecycle／multiplayer Gate；同步 loader 的 53.52ms P99 已由 bounded async commit 降至 17.028ms，詳 docs/RUNTIME_ASSET_STORE.md。ASSET205 [x] monster recipe/population v2/live-add Gate 完成，詳 docs/MONSTER_CONTENT_WORKFLOW.md。RUNTIME206 [x] Render/Logic/Network isolation matrix 完成，詳 docs/RUNTIME_ISOLATION_VERIFICATION.md。

RUNTIME206方向／順移阻塞修正（2026-11-14）[x]：使用者實機回報人物／怪物方向錯誤、卡頓與順移後，已對照 Paradise.exe `CSGOCharDrawLogic::DirectionMapping`、`CSGOEnemDrawLogic::SetDirection` 與 Recovered 行為修正 Runtime。`logical_views` 現優先於同名 physical clip；authoritative facing 與 presentation facing 分離，walking 用實際位移＋28° hysteresis、attack/hit event 鎖定畫面方向；attack/hit 不再 position snap；Server AOI 1100 enter／1250 exit hysteresis；預設 Render60FPS、physics/Server仍30Hz。後續完整30/45/60/75/90、Visual5/10/15、no-world、真失焦／最小化／恢復、1/2/4/8開矩陣均 PASS，故此阻塞項與RUNTIME206一併完成。AS005人工逐格視覺仍獨立 NOT VERIFIED。
ASSET204 [x] RuntimeAssetStore 已完成並保留方向／同步 hotfix；ASSET205 [x] recipe/population v2 workflow 完成；RUNTIME206 [x] 已由完整多FPS／低功耗／背景／多開矩陣正式完成，不再只依單一60FPS Gate。


ASSET207 [x] Runtime Atlas 分類簡化：production 對外只保留 map/session/on-demand lifecycle ownership；方向／動作／部位／hot-warm-cold／512~4096 全改為 normalized metadata 或 builder 內部 A/B 維度。Map13 9 definitions → 1×2048 map bundle；Boy base 6 parts+death → 1×2048 session bundle。已真刪除被取代的40個 generated legacy檔後，Python 45+89 PASS、tracked Godot tests 全 PASS；30-cycle async P95/P99 16.752/16.809ms，20client/300monster/1000drop 40s execution PASS、59.996FPS、P95/P99 19.236/20.422ms、draw P95 236。此為第一階段收斂；Map object 細粒度的假設後續由 ASSET208 實測推翻並合入 map lifecycle bundle。詳 docs/ASSET_BUNDLE_ARCHITECTURE.md、ADR-034。AS005原版逐格人工視覺與AS008授權仍是獨立未完成Gate。

ASSET208 [x] 全 Godot 素材分類必要性稽核＋Runtime root 扁平化（2026-11-14）：適用 Enemy/Player/NPC/Equipment/Item/Pet/Mecha/Map/Effect/UI/Ground Drop。正式查閱粒度與 physical packing 分離；每 normalized entity 保留 `inspect[action][facing]`，逐怪物／逐動作／逐方向可獨立查閱，但 Builder 可依 A/B 自動跨 entity 合圖。移除 `client/assets/generated` 額外層；production rebuild 只生成真 consumer 需要的 `client/assets/bundles` + `maps`，Before 17,274 files/1,179.31MiB → After 12 files/9.036MiB，原始素材仍在 `.cache/client`。Map13 45種 scenery 舊 atlas 的1440 viewport P50/P95 為21/30 owners、4.394/5.414MiB；細拆只省約1.8～2.8MiB object resident，故與9 monster 共用 map lifecycle bundle。1024/2048/4096 combined 候選為5/2/1 pages；4096雖一頁但無 load/frame 優勢且 transient static/decode peak 約多16MiB，auto維持2048×2。20client/300monster/1000drop Before→After：draw P95 236→228、frame P95/P99 19.236/20.422→18.947/20.322ms；代價 texture/video monitor 約+7MiB、process RSS約+6.47MiB/client，明確記錄為維護/lifecycle trade-off，不宣稱RAM/VRAM最佳化。30-cycle release/recovery 全PASS。詳 docs/ASSET_NECESSITY_AUDIT.md、ADR-036。AS005/AS008仍獨立未完成。

F001 [x] 帳號／登入／登出／選角／建角／多角色／重連／身分過期完成（2026-11-14）：WebSocket connect 不再直接 Spawn；選角後才建立 World.Player。正式 Server 預設 SQLite WAL provider，Account/Character 可跨 Server restart 持久化；PBKDF2-SHA256 在 bounded auth workers 執行，不阻塞30Hz World。takeover 舊 Session 先清 AccountId/CharacterId/Ready/SessionEpoch/World Entity，可靠回 `session_replaced`；同帳號 lifecycle `/health players` 1→1→0 PASS。20-client SQLite 首次登入 final storm 1.55s 全進圖，tick300 P95/P99 2.207/7.596ms PASS。詳 docs/IDENTITY_LIFECYCLE.md、docs/RUNTIME_PROTOCOL.md。

===== 2026-11-14 Adaptive Atomic Atlas 單入口轉換 =====
ASSET208 [x] Adaptive Atomic Atlas（原研究 Mode4）命名與轉換器入口：`tools/asset_converter/adaptive_atlas.py`、`cli.py`、`build_assets.bat`、根目錄 `run_build_assets.bat`。以原 SPR 區塊為 atomic MaxRects、512/1024/2048/4096 量測、自動分頁、SHA256 incremental + 故障頁重建、單影格快速量測；Action/Facing/Frame 邏輯與 physical pages 分離。只新增離線產生器，不加 Runtime Loader/Cache/type。
Before→After：前版工具散於 tools/，`rebuild_assets.ps1` 有先清除 live assets 的風險；新版一鍵提供原始 Data1 路徑，離線完整 SPR 索引在 gitignore local-assets，Runtime 先隔離建立/驗證再複製所需 bundles/maps，不清除無關 live assets。
真素材驗證：Enemy 137/137 source groups、8,481 frames、218 pages、RGBA8約823.12MiB（**離線候選總量，不是實際 Runtime 常駐**），失敗0；Player/A/Weapon/A-Sword014 1group成功；Map13 9 visual／78 spawns、573 frames／2頁；Boy base 881 frames／1頁；terrain 865 tiles/367 objects，像素roundtrip PASS。
回歸：`python -m unittest discover -s tools/tests -q` 92 PASS；Godot `test_directions.gd`、`test_monster_visuals.gd`、`test_map_objects_async.gd`、`test_paper_doll_async.gd`、`test_runtime_asset_store.gd` 五項 PASS；CLI real-source preview/Enemy full/raw runtime build PASS。見 docs/ASSET_CONVERTER.md、docs/DECISIONS.md ADR-037、local-assets/asset_converter/build_report.json；Git SHA 見本項提交。
ASSET209 [~] 完整離線原版 source archive（7,893 groups/75,591 SPR）的第一次全量生成與 sidecar ACT/SIF/DrawOrder 驗收：CLI 已開始執行，進度見 `local-assets/asset_converter/complete/progress.json`；少數 Interface SPR 的 frame offsets 異常已明確 fail-closed，不能用空白圖片假裝完成。完整 `index.json` 尚須驗收，AS005 原版畫面 Golden／AS008 發布權與其它素材 Runtime wiring 仍 NOT VERIFIED。
後續：如果輸入格式不合法，應回查原版 Data1 / 版本資料，確認其真實解碼方式，保留來源SHA並記錄無法轉換；不要改以跳過或發明影格資料。Player/Equipment/Pet/Mecha/Effect 的動畫連接與完整資源卸載仍需各自負載/視覺測試。
ASSET209續作（2026-11-15）[~]：修改前：asset_converter/cli.py含literal\n導致SyntaxError，完整離線progress停於6650/7893且未有完整index；archive strict decoder與Runtime palette decoder分叉。指標：CLI可編譯、單一SPR解碼實作、indexed/RGB565 fixture逐像素roundtrip、完整來源群組數／失敗原因／sidecars與可恢復進度。回歸：工具包依賴集中、incremental/壞頁/來源變動、原版只讀、Runtime不受archive錯誤覆蓋。只修改tools/asset_converter及相應測試；既有.cache/client目前非完整來源，改用獨立.cache/asset_converter/source並核對抽取checkpoint，未驗證不標完成。

ASSET209修復驗證（2026-11-15）：tool package compileall PASS；tools/tests 99 PASS（含10項asset_converter fixture）；Godot directions/monster_visuals/map_objects_async/paper_doll_async/runtime_asset_store五項PASS。已修復共用indexed/RGB565 decoder、CLI語法、decoder/metadata hash快取及來源集合拒絕；原版SPR presence 75588/75588、missing=0 extra=0，7890groups。全量archive目前仍在實際執行，未完成不標[x]；進度與錯誤checkpoint已可稽核。另一組batch/ASSET_CONVERTER文件更新保留，不夾帶本修復commit。

ASSET209 幾何尺寸評估最佳化（2026-11-15）[x]：`atlas/build_atlas_groups.measure_source_blocks` 以純幾何模擬來源 SPR padded shelf＋MaxRects，在四個尺寸候選只算頁數與 RGBA 面積，最終仍用原 pack_source_blocks 繪製一次且逐格像素/來源原點驗證。Player/A/Standard/A-Hair00 四候選舊3,396.8ms→新24.97ms（僅候選量測，不是整體轉換速度）；四候選頁數/面積精確一致。新增 tools/tests/test_adaptive_measure.py 4組回歸（透明、多頁、padding 邊界），工具測試 103 PASS；真原版玩家髮型單群組建置 PASS。完整離線 archive 稽核仍需驗證 index.json，未完成不標[x]。

ASSET209補充驗證（2026-11-15）：已對4924dc4幾何評估加入可重跑三次A/B、160組隨機候選對照／無PIL頁配置／選定估值與真打包成本一致保護／非dict壞cache重建。tools/tests 108 PASS；實測CPU及範圍見ADR-039、reports/converter_planning_geometry.json。第一個舊評估程序在6750/7890時有意結束，因已證明幾何等價且CPU節省顯著；新程序續用逐像素驗證後的cache，原始資料未變，沒有因觀察逾時重啟。全量來源及sidecars仍在驗收，不提前勾選。

ASSET209 [x] 完整離線SPR＋ACT/SIF/DrawOrder轉換與驗收完成（2026-11-15）。原始Data1+loose來源75588 SPR／7890 groups／282968 frames／8094 pages；原版presence missing/extra=0；613 ACT、1165 SIF、9 DrawOrder來源重解析與完整index/PNG/source/decoder/metadata/frame identity驗收PASS，錯誤0。舊242錯誤不代表原版損壞；完整SPR解碼共用後本次全量0錯誤，舊7893/75591不再作原版門檻。
一鍵原始路徑：python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"（PASS，warm cache 246.95s）；接著序列執行cli.py verify同路徑（PASS）。Map13 9 visuals/78spawns／573 frames/2頁、Boy base 881frames/1頁，terrain865tiles／367objects、pixel-roundtrip PASS。報告reports/converter_full_build.json、converter_archive_verification.json；完整圖像只留local-assets，未公開。verify不重播逐像素比對，pixel已由builder綁定source/page/decoder/metadata指紋；驗收範圍寫入報告。
Before→After：CLI語法錯誤、indexed SPR解碼分叉、全量未完成／preview舊index，改為完整產物與獨立verify；sidecar同source SHA但內容壞掉會重建；OS lock讓build/verify互斥並保留既有報告，程序異常結束會自動解鎖。最終tools/tests113 PASS、C#376 PASS、Godot方向/360方向動作/map objects/paper doll/store/wire六組PASS。30cycles async：P95/P99 16.826/16.955ms、handle/texture/store resident/RSS recovery PASS；20client/300monster/1000drop40s：59.996FPS、P95/P99 18.965/20.444ms，Client CPU30.8%/Server2.35%一核心。原始JSON與指紋reports/asset_converter_acceptance/。
限制：原版逐格人工Golden(AS005)、公開發行權(AS008)、其餘地圖/NPC/裝備/物品/任務/技能/寵物/機甲/社交等Recovered玩法仍未完成；本次60FPS回歸不替代完整背景矩陣。Client RSS峰值216.96MiB高於200MiB目標，FAIL，不勾資源目標全部完成。
PERF002 [x] 原領取範圍（完成證據見下方）：定位並降低上述原生Client RAM峰值，維持同素材／解析度／20clients/300monsters/1000drops及同步頻率。Before：RSS216.96MiB、Godot static 69,249,567 bytes、engine texture46,035,499 bytes。指標：metadata共享／CPU Image存活／worker與frame cache持有、啟動與穩態峰值、owner release後回收；目標RSS≤200MiB、frame P95/P99≤25/40ms且較基準不退化>10%。先建立可重現分段證據與引用回歸，再改實作，不降低畫質／實體量／同步頻率。
PERF002實測後原計畫（2026-11-15；已執行，結果見下方）：Before確認worker永久迴圈保留最後CPU Image／JSON、idle telemetry每幀無界追加；以獨立函式生命週期及2048-sample ring修正，weakref驗證owner回收。Windows release同1440×900／20連線／300怪／1000掉落：2048 packed峰值199.80MiB；1024 packed三次184.95～189.11MiB，P95≤18.602ms／P99≤19.294ms，Draw Calls P95增加至298～312需如實保留。1024 async 30次卸載回收PASS。下一步僅調整Runtime自動候選優先上限1024，原SPR不拆頁，較大SPR逐級fallback；離線archive不改。回歸：候選512/1024/2048/4096、oversized source fail-closed、方向／像素／pivot／ACT一致、正式重建與打包內容hash、30次卸載與標準多人測試。此句為實作前狀態，最終驗收見下方。

PERF002完工（2026-11-15；919a7c8）：僅完成正式Client RAM／idle引用／telemetry及Runtime小頁決策範圍，未宣稱完整Recovered。asset_converter Runtime優先512/1024、SPR不拆頁、最小有效2048/4096 fallback；離線完整archive不改。從原始來源Runtime-only重建35.94s、15檔，map573／session881影格逐格PNG像素、pivot／trim／size對照來源PASS（含2個透明空幀）；原始資料reports/perf002_production/pixels.json。新增build_client.py與raw export plugin，官方模板hash／source inventory／raw檔hash驗證後建立真正Windows release。正式20clients/300怪/1000掉落/1440x900/40s：OS峰值187.09MiB、60.003FPS、frame P95/P99 18.549/19.200ms、Client CPU24.6%一核心；30次map async卸載所有回收gate PASS、load P95 130.829ms、GPU submit P99 1.591ms、frame P99 16.971ms。弱引用loaded CPU Image=0，release後Image/metadata=0；telemetry10000次idle無增長、每類2048筆上限。實測／決策docs/PERF002_MEMORY.md、ADR041、SPEC§9、reports/perf002_*。命令：python -m unittest discover -s tools/tests -q（113 PASS）、tools/test_asset_pipeline.py（5 PASS）、tools/test_runtime_baseline.py（8 PASS）；Godot test_directions/monster_visuals_async/map_objects_async/paper_doll_async/runtime_asset_store/wire六組與native probe PASS。限制：開發引擎221.80～224.00MiB仍FAIL；release早期217.08FAIL數據保留；Draw Calls取捨有記錄；實體driver VRAM、完整FPS背景矩陣本次未測，Server頻率不改。build EXE/PCK／所有原版圖像未公開。
PERF003 [x] 完工（6ae9645，CI結果見下方）：補測密集acquire/release取消時decode request／ready item是否保持有界、取消舊map是否浪費解碼及阻擋新map。Before疑點：release只刪entries，_requests可能累積；先用暫停worker fixture重現並記錄queued jobs，再改實作。指標：MAX_JOBS界線、ready bytes／item數、取消後不提交舊GPU、按priority載入新owner、close/restart與idle CPU。不可用已通過的串行30次卸載代替取消風暴回歸；網路/logic排程不動。

PERF003修改前補充：逐頁GPU提交後仍loading時release直接刪entry，但_resident_bytes未扣回已提交頁；error/purge failed路徑同樣需要驗證。先以自製兩頁fixture（第一頁有效、第二頁缺失）分別重現partial cancel與error，再修正並驗證可retry／close。

PERF003實作／回歸（2026-11-15，待最終提交對帳）：有效token與request/ready queue同鎖清除，取消的PNG於page邊界停止；128-item上限同樣計入metadata/error。partial/error/ready淘汰共用帳目與引用清理。Before queued1000→After0，partial/failure owner release後resident4096→0、native Texture弱引用0，同path修復後真圖片retry PASS。自製test_runtime_asset_cancellation三次PASS（128job cap／1000generation／stale publish／priority／zero-byte cap／byte backpressure／close+restart）；既有monster/scenery/paper doll/store四組及native lifetime PASS。正式release來源／asset SHA通過：20clients/300monster/1000drop/1440x900/40s，184.88MiB／59.997FPS／P95/P99 18.884/19.573ms／CPU27.9%一核心；30cycle map async load P95 131.832ms、GPU submit P99 1.775ms、frame P99 16.908ms，所有回收gate PASS。原始reports/perf003_*，詳docs/PERF003_CANCELLATION.md/ADR042；沒有降低同步頻率。idle CPU提供獨立0/1/8 decoder探針（包含console launcher+engine child），短窗口0不表示零CPU，不宣稱改善。前一919a7c8兩項CI已成功：43633607671、43633607695。本次CI與最終SHA待push後對帳；完整RUNTIME206矩陣本次未重跑，原版Golden／physical VRAM／其餘Recovered仍未完成。
PERF004 [x] 已實測並提交（3d96922；報告94f778f）：MAX_JOBS滿時acquire僅purge_unowned(false)，如果128個小ready atlas雖無owner但未達byte budget，可能無法騰出新map/session entry。先用自製空頁manifest快取fixture重現，再決定是否將entry pressure納入LRU；不可直接force清掉全部cache。通過後銜接F002真實地圖／傳點／NPC資料與切圖進度，先盤點Recovered與原版資料契約，維持map/session正確卸載及Server權威座標。

PERF003提交對帳：6ae9645已在main；兩項CI 43634937843／43634937828均completed/success。PERF004修改前／指標／回歸：128個小ready atlas低於byte budget且多數已無owner時，嘗試第129個path；檢查能否按LRU只淘汰最舊的無owner項目、保護pinned與最近使用項目、owned全滿時仍正確ERR_BUSY、釋放後可恢復；不改快取命中與未滿時策略，不force清光cache。

PERF004實作驗收（待commit SHA對帳）：Before自製128個32×32頁共524288bytes（budget16MiB），多數無owner仍第129個path ERR_BUSY=44。After以entry_limit=127共用LRU，只移除最舊無owner項目，保護pinned及最近使用項目；全部128有owner時維持ERR_BUSY，釋放一項後恢復，close resident=0。headless與native test_runtime_asset_cache_pressure PASS，test_runtime_asset_store／test_runtime_asset_cancellation PASS；原始reports/perf004_before/after/after_native.json。修改只涉及滿額admission分支，未重跑普通2-bundle多人FPS/RAM，不將6ae9645數值當本次重測。docs/RUNTIME_ASSET_STORE.md、ADR043已更新；CI與提交待對帳。

PERF004提交對帳：3d96922含程式／native回歸／文件，94f778f為Before/After文字報告；CI43635332053 completed/success。工作樹乾淨，滿額分支修正已推main。
F002A [x] 首個離線交付（4d3ab78，僅此範圍）：原版NPC／傳點內容契約與asset_converter可重跑轉換。Before：Runtime僅single World、沒有NPC/portal catalog與MapEpoch；Map13 Old NPC.dat三個TS_*不含_to_，不能用檔名判斷，原版NPC_Table.dat為C.D.E.包裝、解包569列×6欄；原版Map07/08/09用前導0，現有population只找Map{n}亦需fixture後修正。首個可交付是離線NPC/portal JSON＋來源hash＋自製binary/script fixture＋Map13真資料稽核，不宣稱可遊玩傳點。指標：完整records／不截斷／reserved原值／視覺來源與未解欄位、腳本TS_Common固定已審hash及可確認的GetLoc；未知／停用／動態Lua不扁平化為可用portal；原始目的地不擅改。回歸：CDE/壞長度/缺NUL/尾隨bytes/路徑逸出/腳本註解或字串偽造/未知template／前導0目錄。後續順序：Server MapCatalog／權威transfer與epoch fixture→wire/client loading＋舊圖資源回收→真雙圖多人E2E／資源回歸；F002整體仍未完成。

F002A實測驗收（4d3ab78）：已從原始Client路徑完成抽取並編譯Map13三筆；其後目前版本全24份NPC.dat 275/275筆、primary SPR全可解析、49個已審確認式TS模板／226個script_required；Map41/46缺NPC.dat不猜空。原始CDE NPC_Table解出569×6欄，reserved／Big5原bytes／source index／slots／未驗方向時間保留。全量既有抽取cache內容編譯1.241s，Map13 fingerprint與原始路徑結果相同；reports/world_content_original_map13.json、world_content_source_extraction.json、world_content_all.json。私有NPC JSON只在local-assets，未接Client／Server前不增加常駐。新增source/legacy_npc.py、content/npc_content.py、CLI world-content及all-maps；同時修正低編號population的Map07目錄與CLI互斥旗標NameError。tools/tests121 PASS（新NPC／CLI fixture8）、asset_pipeline5 PASS、maps3 PASS/1 SKIP；無Server改動，不宣稱可玩傳點或重跑FPS矩陣。F002完整順序及來源SHA見docs/F002_WORLD_TRANSITIONS.md、ADR044。下一步F002B MapCatalog／單map共用World與Server權威transfer fixture，之後才wire／Client。

F002A提交／CI對帳：4d3ab78已推main；CI43662661521與43662661517均completed/success。原版內容仍私有；此項[x]只代表已驗收的離線NPC／傳點資料契約，不代表F002整體或可遊玩傳點已完成。
F002B [ ] 下一個唯一實作任務：先建立C#/.NET8自製雙圖fixture與MapCatalog／共用World registry契約，再接權威transfer。Before目前WorldHost只有單World，World.Time各自從0開始，Player資料及input/state沒有MapEpoch；不能先暴露Client自行跳圖。指標／回歸：角色id/HP/背包保留、來源離開與目的地進入一次、失敗留原圖、目的地bounds/Blocking／傳點距離、舊input與pending attack不跨圖延續、同map多人共用怪物與掉落、global server time／tick維持30Hz、不在tick內讀檔；MapEpoch／wire fixture先於正式協定修改。後續Client真loading／NPC獨立asset按需載入／切圖owner回收與雙圖多人E2E依docs/F002_WORLD_TRANSITIONS.md執行，完整Recovered其餘功能仍保留未完成。

ASSET210 [~] 使用者2026-11-15最新目標優先：先把全部素材匯出至client/assets，遵循7a2db4c新架構；F002B暫未領取。Before完整7890來源groups/8094pages仍在local-assets，Runtime只有Map13/Boy；目標先發布可供Godot共用Store讀取的v9 source-clip manifests＋logical catalog、完整audio／sidecars與其餘來源payload coverage。檔案按maps/monsters/npcs/players/equipment/pets/items/effects/ui/audio/data分類；metadata ownership用app/character/map/on_demand，不新增多套Loader。不將MA/MS/SR猜成機甲/寵物，暫存data/legacy保留代號與unverified；未知動畫時間／方向明列，不虛造缺幀。指標：原始source每檔coverage、SPR逐影格identity／PNG SHA／pivot/trim／既有方向alias、Godot async抽樣共用及卸載、全量輸出後native啟動RAM/FPS不退化。回歸：壞archive拒絕、路徑／ID collision、增量／舊產物僅按owned清理、audio CDE解包、原版只讀／媒體不推Git。先完成完整匯出與報告，再獨立處理舊runtime路徑／程式目錄重構及addons替代驗收；不把尚未驗證的所有map/ITF/ITD語意／可玩功能標完成。

ASSET211 [x] 2026-11-15 per-asset Runtime boundary：移除 Map+Monster / session physical bundle 假設；75,588 SPR→7,890 independent source groups→7,890 unique logical IDs，0 collision/0 export error。Map13 world 367 placement/45 independent scenery assets；population 9 definitions/9 independent monster assets；Player-B base/death 7 independent assets；blocking.dat 保留原版 byte grid，cross_asset_bundle=false。Python tools/tests 121 PASS；Godot directions/map objects/store/navigation/paper-doll PASS；C# test executable從repo根目錄376 PASS。公平 packing benchmark固定同一12 monster IDs、Map13 terrain+16 visible scenery assets、每mode獨立process、2/12 kinds×1000 instances×3 repeats；舊 Mode4 12-kind因取到不同 monster set 作廢。新版結果 Dense Single 在 RGBA/page/load 綜合優於現 Adaptive Atomic；最終 physical packing policy等使用者依 docs/ASSET_PACKING_BENCHMARK.md 決定，不偷換 production policy。

ASSET212 [x] 2026-11-15 架構／工具清理與可重跑 packing 比較完成。Client 實際 scripts 已搬到 app/assets/diagnostics/gameplay/networking/rendering/ui/world；Server 已移除 Application/Domain/Infrastructure/Transport 主目錄，改 Accounts/Networking/Persistence/World，Session→ClientConnection、SessionEpoch內部命名→ConnectionEpoch（wire欄位位置/值不變）。Client tools 分為 build/benchmarks/previews/research；移除舊 lifecycle asset_bundle_builder/asset_pipeline、normalized candidate builder/report/tests、bundle lifecycle benchmark、shared-bundle monster async test、無引用 asset_stream；rebuild_assets.ps1 改名 tools/asset_converter/verify_runtime.ps1。local-assets 舊四模式/atomic/locality/尺寸實驗產物全清，只保留 asset_converter canonical archive；complete/sprites 另清除2個不在7890正式index的殘留root atlas。現行工具只認 per-asset catalog；summarize_assets實測21730 files/13464 catalog entries、unexpected_top_level=[]、cross_asset_bundle=false、production_clean=true。回歸：tools/tests 123 PASS；root tools tests 37 PASS/1 SKIP；Godot test_*.gd 15/15 PASS；Server build 0 warning/0 error、376 PASS；verify_runtime.ps1 75588/75588 coverage、Map13 45 scenery/9 monster/7 player parts及6 regressions PASS。文件：ARCHITECTURE/CLIENT_ARCHITECTURE/SERVER_ARCHITECTURE/ASSET_ARCHITECTURE/ASSET_CONVERTER/ASSET_PACKING_BENCHMARK、ADR-038/045。
ASSET213 [ ] Physical packing 最終切換待使用者確認：目前 production 仍 Adaptive Atomic；最新候選建議 **Per-Asset Dense Atlas**（一 logical asset 優先一張<=4096 Dense page；未來真的超限才 Paged Dense fallback）。公平A/B固定12種怪、Map13背景、1000 instances、每candidate 3 repeats：2-kind Dense 2 pages/64.73MiB/222.5ms/texture107.52MiB，Atomic 7pages/79.77MiB/263.0ms/127.55MiB；12-kind Dense 12pages/179.06MiB/texture259.93MiB，Atomic30pages/211.98MiB/303.76MiB。完整7890/7890 asset幾何皆可單頁<=4096。若確認採用，下一步只改production Builder packing，必須重跑7890完整pixel/origin/hash、catalog/runtime verify、15 Godot、376 C#與多人/生命週期A/B；不得改asset boundary或增加Loader/Cache。

ASSET214 [x] 2026-11-15 Model1 深入比較完成：使用 Recovered 真實 Assets/Monsters PNG+JSON、MonsterLoader/PaperDollLoader/AnimatedSprite2D，並以 SGTest EntityAtlas/direct Canvas 對應 renderer 比較；不是共用 renderer 假比較。聚焦 Enemy/Weapon/Equipment/Map scenery 不同大小。2,761 relevant assets 幾何：Height global 3136.55MiB/median waste18.59% 第一；Enemy 703.68MiB、Weapon140.60MiB、Equipment1926.69MiB 都第一；scenery 各策略近乎相同。Recovered 固定2048寬相比 Height-P0：Enemy small/medium/large/huge RGBA -36.1/-22.3/-9.2/-5.4%；Weapon -36.7/-10.9/-33.8/-29.2%；Equipment -5.3/-16.0/-6.0/-3.8%。Native mixed：Enemy12 Recovered load67.96ms/p95 2.414ms/texture62.26MiB vs Height-P0 592.43ms/4.750ms/231.17MiB；Weapon4 Recovered27.19ms/0.810ms/4.79MiB vs P0 9.82ms/1.730ms/3.40MiB；Equipment4 Recovered65.26ms/1.101ms/28.26MiB vs P0 62.89ms/1.818ms/26.91MiB。結論：packing baseline=Per-Asset Height-Dense；真正最大優化點在 imported texture/ResourceLoader + renderer path，而不是再分 action/source bucket。詳見 docs/ASSET_PACKING_BENCHMARK.md。
ASSET215 [ ] Model1 production 決策前需補：A/B Height-P0（0px edge-to-edge + filter_clip/nearest）vs Height-P1（1px extrude），涵蓋縮放、旋轉、不同解析度、線性/nearest，確認無 bleed；再 A/B external PCK imported/compressed texture + ResourceLoader vs raw PNG + ImageTexture；另量 SGTest GDScript EntityAtlas.sample/direct Canvas allocation cost。不同 asset identity 不得合併。
ASSET216 [x] Runtime monster asset folder 命名改回原版直觀 `Enemy/`；converter canonical path、Client tests/tools/docs、本機 client/assets 與 population/catalog references 已同步。Domain 內 monster 名稱不變。

ASSET217 [x] 2026-11-15 Runtime-only 3-repeat comparison完成（不以磁碟/空白率排名）。Windows監控完整 Godot console+real renderer process tree Peak RSS；Godot記錄Texture/Video delta、load、P95/P99、draw、nodes。Enemy mix12：Recovered RSS217.9MiB/Tex62.3/load77.7ms/P95 2.681ms vs SG Height-P0 RSS190.4/Tex231.2/load609.8ms/P95 5.358ms；Enemy主要瓶頸確定為 raw PNG/ImageTexture + GDScript EntityAtlas.sample/direct draw，而非Height packing。Weapon mix4：Recovered209.7MiB/4.8/27.7/1.029 vs P0 151.3MiB/3.4/9.6/1.618；Equipment mix4：Recovered214.6/28.3/65.1/1.391 vs P0 167.1/26.9/63.4/1.750。SG候選整體資源排名 Height-P0 > P1 > P2 > MaxRects；MaxRects少數P95略優但memory/load不值得獨立production policy。下一步建議保留per-asset Height，優先A/B Godot imported/compressed texture+ResourceLoader與低allocation/engine-side renderer。詳見 docs/ASSET_PACKING_BENCHMARK.md §15-16。

ASSET218 [x] 2026-11-15 高保真 Renderer A/B 驗證完成（僅隔離渲染）：Recovered MonsterLoader ACT + frame_changed origin / SGTest 同一批 VRAM-compressed Height-P0 atlas，對比原生 SpriteFrames/AnimatedSprite2D 與 Fast Canvas；兩邊每幀移動、World Y-sort、定期動作切換，3次獨立process中位數。12種Enemy×1000：Recovered Peak RSS215.6MiB/Texture62.3MiB/P95 3.047ms/P99 3.316ms；SGTest Sprite 172.6/58.0/3.014/3.298；Fast Canvas 168.2/58.0/4.399/4.548。12種×2000：Recovered219.8/62.3/6.576/7.396；Sprite178.7/58.0/5.602/6.142；Fast168.7/58.0/8.997/9.265。超大單一Enemy×1000仍Recovered P95 1.988ms勝Sprite 2.732ms，不能宣稱全面勝出。驗證修正Sprite逐幀trim/origin、ACT logical-view durations/loop、Y-sort；12種1,179 frame metadata assertions PASS；4種不同大小96組實際viewport像素對照全部0 diff、36組左右翻轉且畫面非空白；12種1000 Sprite連續5次load/unload Texture與Node均回到0 delta。正式client/gameplay/Windows Release EXE與macOS尚未完整驗收。詳docs/RENDERER_PARITY_V2_2026-11-15.md。
ASSET219 [ ] 正式Renderer整合待驗收：保留一Asset一Height-Dense＋Godot VRAM壓縮/外部PCK；優先將共享SpriteFrames/AnimatedSprite2D用於可視Enemy，並透過RuntimeAssetStore owner/refcount按需釋放；確保原版ACT/Origin/flip、受擊打怪死亡中斷、Map13 World Y-sort、AOI與Network/World fixed tick互不依賴、多人多怪多掉落、AFK/Unfocus與換圖30次、Windows Release與macOS實機。Player/Pet/Mecha/Equipment紙娃娃仍需獨立畫質/動畫/分層驗證；單一超大怪回歸與多Texture約1 draw/instance持續追蹤。沒有完整E2E前不得標完成。

ASSET220 [x] 2026-11-15 Map13 + AOI mixed renderer benchmark (isolated; not full gameplay): new mixed_map13_renderer.gd/run_map13_mix.py and ysort_layer_parity.gd. Uses actual Map13 world.json object positions, 41 scenery visuals/16 asset identities, 20 player placeholders, 500 drop markers; same 14 action/direction views and shared World Y-sort, independent 12 Enemy types, 5% AOI churn every 20 frames. Three Windows processes each at 300/1000/2000 Enemy. Recovered vs SGTest compressed Sprite (Peak RSS MiB / Texture MiB / Frame P95 ms / P99 ms): 300=214.1/66.7/1.498/1.663 vs 164.3/62.4/1.402/1.535; 1000=217.6/66.7/3.640/3.901 vs 170.7/62.4/3.421/3.865; 2000=219.8/66.7/7.470/10.361 vs 176.6/62.4/6.900/7.728. Verified 105/350/700 AOI despawn and respawn; 215/715/1430 action switches; original Recovered idle/walk/attack mirrored direction mapping and SGTest view flip_h updated on every action; Y-sort pixel occlusion 5/5 PASS. Runner now fail-closed, process tree RSS, configurable --counts/--repeats/--frames/--output-dir, avoids stale reports. Python128 PASS; root tools37 PASS/1skip; Godot15 PASS; C#376 PASS. Docs: docs/MAP13_MIXED_RENDERER_VALIDATION_2026-11-15.md.
ASSET221 [ ] Production world Sprite integration pending: current main.gd still draws players/Enemy/scenery in one ordered CanvasItem. Changing Enemy to child SpriteNodes without shared World Y-sort could break cross-category occlusion. Need correct scene composition + RuntimeAssetStore ownership, live server AOI/network, actual player paper-doll, real terrain, sound/attack/hit/death interruption, FPS/logic independence, 30 map/AFK/relogin cycles, Windows Release EXE and macOS. Do not mark complete from isolated mixed renderer results.

---


<a id="source-spec-md"></a>

## SPEC.md

*Historical source: `SPEC.md` — SHA-256 `3bdda28d1c98ed690edea068cf587e33dc1e33a2ed8f1a5b7b715bdfdc4e8c57`*

# SGTest 完整開發規格（v1.1 / 2026-11-13）

狀態：已有可執行原型，現在依第0節先建立資源架構基準再選型；舊M0紀錄保留供追溯。新遊戲原創開發，唯讀參考 SGO/TestGoGo 的 Old、Client、Server 與測試。性能數字與里程碑都屬「目標」，不是已驗證成果。

## 0. 2026-11-14 最新執行契約：先基準，再選型與實作

本節優先於舊 M0 狀態及舊 .NET 10 選型。現有為 Godot Client／C# .NET 8 Server 原型；直接修改 `C:\SGO\SRC\SGTest` 的 main。使用者最新要求採「標準化動畫定義＋自動混合 Atlas Builder＋統一 Runtime 資源管理」，不把目前實驗排法或 loader 草稿視為定案。

### 0.1 明確順序與首個交付

1. **PERF001：首個可交付成果是可重跑的基準工具、原始 JSON、可讀報告與本節／TODO。** 記錄硬體、Godot／.NET、Git HEAD＋工作樹指紋、素材catalog SHA、場景、解析度、FPS上限、暖機及快取狀態。先量目前方案的CPU解碼、GPU提交／同步等待、載入／重載、RAM／Godot追蹤的GPU記憶體、卸載、Draw Calls與多人場景。數據缺少就寫NOT VERIFIED。
2. **ASSET201：定義標準化。** Entity（怪物／角色／部件）→Behavior→八個LogicalFacing→Frame；保留原SPR來源／索引、alias／flip、trim／pivot／z-order、ACT raw time／事件。動畫速率與位移／Render FPS分開。最小實體fixture、八方向及一次性動作回歸先通過。
3. **ASSET202：候選比較。** 同一批定義測來源SPR分開、按action×direction原子分組、依map共現／使用頻率混合分組；候選512／1024／2048／4096。不強制全entity一張，也不只比較PNG大小。Frame順序與來源可檢查，另輸出標示預覽；runtime頁內可讀性與透明空白一起量測。
4. **ASSET203：資料支持的決策。** 比較載入／重載、GPU提交尖峰、卸載粒度、RAM／GPU峰值、CPU、Draw Calls、Frame P95/P99及cache amplification，再更新ADR／SPEC／TODO。沒有實測不能宣稱某尺寸／單張／MultiMesh較快。
5. **ASSET204：統一Runtime實作。** 統一Definition／Page ID；同種及多人共享immutable動畫與Texture。owner引用計數、有界CPU解碼queue、主執行緒GPU提交預算、按需／預載、cache上限、只淘汰ref=0的LRU、取消generation。切圖／登出釋放owner及不必要常駐資源。Net／State／Command／Server頻率不受載入或渲染限制影響。
6. **ASSET205：新增怪物資料流程。** 原始SPR/ACT＋一份 recipe（definition id／runtime stats／出生設定）→fail-closed驗證→normalized definition／clip-atomic builder→40-view private preview→population v2／client/server內容。新增種類不需改C#／GDScript；ID、來源hash與內容指紋穩定，full rebuild會由 `content/monster_recipes/*.monster.json` 重套。ASSET205需跑離線/E2E與標準60FPS多人 live-add Gate；30/45/60/75/90FPS、5/10/15 low-power、失焦／最小化／1/2/4/8開完整trace統一歸 RUNTIME206，不重複宣稱。
7. **RUNTIME206：Render／Client Logic／Network／Server 隔離。** 已完成正式 Gate：一般30/45/60/75/90FPS、Visual 5/10/15低功耗、no-world、真 Windows unfocus/minimize/restore、Unfocus toggle、1/2/4/8開、queue age與同Server authoritative state hash。預設Render60FPS；UI只改呈現，physics／input／network reducer／Server固定30Hz。完整重跑命令與限制見 `docs/RUNTIME_ISOLATION_VERIFICATION.md`。

### 0.2 可接受門檻（目標，非已通過）

本機基準：RTX3050、Compatibility/OpenGL、1440×900、上限60FPS；20players／300monsters（至少9visual）／1000drops；暖機2秒後至少30秒。另保留1000怪／5000掉落壓測，不拿較輕場景取代。

| 項目 | 驗收目標 |
|---|---|
| 正常遊玩 Frame | P95≤25ms、P99≤40ms；新方案較同場基準P95/P99不退化超過10% |
| 載入主執行緒 GPU submit | 每頁P95≤4ms、P99≤8ms、單次≤16ms；大頁不能靠平均值隱藏尖峰 |
| 載入期間畫面 | frame P99≤40ms、max≤66.7ms；超過需拆分／預載，不可用降同步頻率通過 |
| 熱OS快取 map working set ready | ≤1秒；將CPU decode、GPU submit、GPU同步等待、等待預算時間分開記錄 |
| Client RAM | 正常場景尖峰RSS≤200MiB；metadata／CPU解碼／GPU各有明確上限 |
| GPU | 當前map＋角色的managed texture working set目標≤96MiB；RenderServer texture/video counters另列，不能稱為整卡／driver實際VRAM |
| CPU | 正常單客戶端CPU中位數≤一核心100%；Server中位數≤一核心30%，超過記錄並優化，不改既有tick |
| Server／網路 | Server30Hz、snapshot10Hz、input30Hz不減；tick work P95≤10ms、P99≤20ms；同trace因果／狀態結果一致 |
| 引用與卸載 | owner釋放後資源handle／引用歸零；兩個render frame後engine-tracked texture bytes回暖基線+1MiB內 |
| 30次載入／卸載 | 先2次暖機；卸載後RSS≤暖基線+max(16MiB,10%)，Godot static memory≤暖基線+max(8MiB,10%)；最後10次對最初10次RSS中位數增幅≤8MiB，無持續上升趨勢 |
| 正確性 | pixel／frame順序／八方向alias／pivot／ACT及多觀察者Move/Stop/Hit/Death等回歸零錯誤；未測項目不標PASS |

4096可採用，但需通過相同GPU尖峰／常駐與卸載門檻。門檻屬目前硬體的可接受目標；若原始素材或基準不符，先明示失敗、提出實測取捨並更新決策，不偷偷降低場景、FPS或同步頻率。

### 0.3 資源管理必備契約

- GPU page為共用／引用／LRU單位，Entity definition不直接強持有所有世界圖片；只預載當前map／角色需要的working set。
- hot/warm/cold分類及共現決定co-pack；冷死亡／特效不必和所有待機／行走常駐一張，但重要動作未ready時State仍即時套用，不補播過期動作。
- decoder單一或有界worker；只在主執行緒做GPU commit，限制每frame時間／頁數。取消切圖後的過期CPU結果不得再建立GPU資源。
- 明確分開active/pinned refs、無引用warm cache、LRU eviction與force purge。登出／測試purge必須能回基線；快取快不等於永不卸載。
- `RenderingServer.force_sync()`只在基準隔離同步完成成本時使用，不在正式每次載入強制卡住CPU／GPU。WDDM等不支援per-process實體VRAM時標N/A，不用RSS代稱VRAM。

### 0.4 渲染／Client／Server 分離與低功耗多開（2026-11-14 補充）

- 渲染 FPS 只決定視覺取樣與提交頻率。Server 模擬維持30Hz、snapshot10Hz、Client input30Hz；收包、解碼、可靠事件套用、命令、時鐘同步不得掛在渲染回呼或受 FPS 上限、VSync、失焦、最小化節流影響。Client 固定邏輯須有獨立排程證據，僅設定 physics_ticks_per_second=30 不算完成隔離。
- 一般遊玩支援30～90FPS（可選30／45／60／75／90或此範圍自訂上限），效能基準仍以60FPS量測；調整Render FPS不得改變傳輸頻率。低功耗多開需驗證15／10／5FPS及停止世界繪製的掛機模式。可降低呈現／音效／非必要視覺資源，既有戰鬥、撿物、狀態與連線持續正常。恢復畫面直接取當前狀態，不補播積壓動畫。這些模式目前是待實作與待驗證需求。
- 頓挫調查分開量 frame pacing（P95／P99／max、長幀數）、每角色插值／修正位移、snapshot 到達間隔／queue age、CPU decode／GPU submit 尖峰。平均60FPS不能單獨證明流暢；各角色不可等全場批次更新才一起移動。原版低影格動畫與世界座標平滑分別檢查。
- 驗收使用相同輸入／packet trace、seed及時間戳，覆蓋5／10／15／30／45／60／75／90FPS、前景／失焦／最小化／掛機與恢復，單開及2／4／8開。離線確定性trace的命令／可靠事件因果與最終狀態hash須一致；真實連線另量實際頻率、queue延遲、CPU／RAM／GPU及每個程序的數值，不能要求不同網路排程的封包總數逐位相同。
- 正常本機條件下，Client固定邏輯／input／Server tick實際頻率各在目標±5%內；snapshot發送維持10Hz（無相關實體時依協定），相較60FPS同負載基準的Client reducer／command queue P99延遲增加≤10ms且絕對值≤50ms，可靠事件遺失為0；CPU／RAM／GPU省多少依實測填寫。5FPS仍正常同步不能靠每200ms一次追趕批次通過。上述為目標，NOT VERIFIED。

## 1. 願景、關鍵成功標準與範圍

目標：建立可長期開發的輕量 2D MMORPG，支持多人同圖、怪物/掉落物密集、即時移動與戰鬥、地圖探索、裝備強化、任務、寵物與機甲，後續擴充 PVP、交易、社交和小屋。

硬性優先序：**正確性與權威 > 人物移動/方向/狀態的主觀流暢 > 戰鬥即時性 > 渲染效率/記憶體 > 開發迭代便利 > 裝飾效果**。正確性不可因效能優化被降級；不允許 Client 幀率或失焦決定邏輯更新率。

「比現版 Godot 更好」必須有同等素材/場景/硬體下的 A/B：玩家畫面、RAM、VRAM、CPU、GPU、Frame P95/P99、Server Tick P95/P99、Net E2E P95/P99。比較方法見 docs/PERFORMANCE.md。

實作選擇以多人、密集怪物／掉落、即時動作同步下的正確性與效能證據決定。Godot 內建 Physics／Area2D／AnimatedSprite2D 不是強制；若相同場景下較慢或佔用較多，可使用自製空間索引、緊湊狀態、可見繪圖或批次渲染，並以行為回歸及 A/B 證明。不得把效能提升建立在降低戰鬥、網路輪詢或可見重要事件更新率上。

## 1.1 最新選定：後期星夢 Online 金黃色 UI、現代 UI 與原始資產
- 使用者 2026-11-13 指定後期《星夢 Online》金黃色 UI。早期星空之門藍色 UI 不是正式基準。
- 用 Godot Control、Container、Theme、.tscn 全新建立 UI，不複製 TestGoGo 的 HUD/視窗/登入/背包等 UI 程式碼。
- 可在遊戲設定中切換「後期金色風格」與「現代排版」；兩者讀取同一份 UI ViewModel，切換不改角色狀態、網路輪詢或世界模擬。預設視窗 1280×720；提供 800×600、1024×768、1366×768、1440×810、1440×900 等多尺寸預設及視窗拖曳調整，寬度目標上限約 1440 像素。UI/文字縮放與世界鏡頭縮放分開設定，檢查安全區和排版。
- 原版 UI 圖資缺件時，可用 Godot 原生控件／StyleBox／NinePatch 及自製素材做風格相近且功能完整的金色版；不因缺少原版圖而阻塞 UI 操作。
- 目標是導入原版素材，但素材來源、版本、格式與合法使用權需要先確認。優先唯讀盤點/獨立解析/匯出 PNG+Atlas+Animation metadata，並確保透明度、方向、偏移、幀時長正確。
- HK2 有 20 個實體 .itf，但其中 122 筆 SPR 引用有 59 筆找不到同名實體檔；不能宣稱完整原版 UI 素材已齊。未確認授權不傳到公開 GitHub。
- 相關文件：docs/OLD_UI_DESIGN.md、docs/LEGACY_ASSET_PIPELINE.md。新的優先 Gate：AS001～AS008；素材來源與可解析性未確認前，不建立正式 Godot 場景、紙娃娃、UI 或 Server。已刪除初期 Demo 原型，Git 歷史仍可回溯。

## 2. 技術選型與品質原則

- Client：Godot 4.x，首選 GDScript + 輕量 Data Oriented Entity State；Node 專注 UI/少量互動、畫面可視 Pool。是否改用 C# 或 C++ GDExtension，只能由 Profiler 證據決定，須包含 Runtime 記憶體成本。
- Server：C#/.NET 10 LTS，ASP.NET Core Gateway + MapActor/World State + Domain Application；單體模組化起步，未達負載瓶頸前不拆微服務。
- DB：初版 SQLite（WAL、明確單寫入排程、原子資產事務/Outbox），透過 Application ports 與獨立 provider/migration 隔離；未來可換 PostgreSQL 等資料庫。熱路徑 WorldState 在 RAM，地圖每 Tick 不讀寫角色 DB。
- 協定：版本化 MessagePack / Protobuf 擇一（ADR 紀錄）；TLS WebSocket 初版，ServerSeq/SimTick/ConnectionEpoch/EntityRevision/MapEpoch；不沿用舊 AES/Base64 JSON wire 格式。
- 內容：原生 JSON/二進位資料包，離線工具轉換 Old；官方原版素材與原始碼需先確認合法使用權，否則以自製/測試素材代替。
- 開發：GitHub Actions 自動 lint/compile/test/資料驗證/模擬負載；以具體指標改善，無測試就不聲稱達標。

## 3. 功能需求與完成定義

### Phase P0 — 可玩最小多人核心
- F001 帳戶/登入/登出/選角/建角/多角色/重連/身分過期，重登不得保留幽靈 Entity。
- F002 地圖載入（登入→選角→入圖多階段進度）、Blocking/視野/傳點、世界座標與地圖局部座標轉換；AOI 半徑依實際 viewport／zoom／角色位置與緩衝計算，不能沿用原版 800×600 固定可見範圍，須測各預設、視窗拖曳及地圖邊角。
  - F002A離線NPC／傳點來源契約已有實際交付：24份NPC.dat／275筆保真輸出，49個已審確認式TS模板、226筆script_required；41／46缺檔明列。由asset_converter從原始Client解包外觀表／SPR，再讀原版Server出生及腳本，不靠檔名猜測。這不是可遊玩切圖；Server MapCatalog／MapEpoch協定／Client loading／雙圖E2E仍未完成。順序與證據見 [F002_WORLD_TRANSITIONS](docs/F002_WORLD_TRANSITIONS.md)。
- F003 玩家本地即時操控、點地面/點怪/NPC、停走、轉向、追路、人物速度；Local Input→Server command，視覺可預測但戰鬥/阻擋由 Server 驗證。
- F004 遠端玩家 Spawn/Despawn、初始方向/外觀、移動、停止、轉彎、跳圖/重連；不得跳格、回拉、原地跑步；同畫面 20 人行走時優先維護。
- F005 怪物 AI：出生、巡邏、追擊、脫戰/回巢、不能穿牆、方向/動作、攻擊、死亡、重生；數百怪共享地圖運算，不能每 Session 有獨立副本。
- F006 普通攻擊、命中/未命中、傷害浮字、受擊/攻擊/死亡方向與動畫、技能與 CD；多觀察者看見相同權威結果，動作及位置封包互不相互覆蓋。
- F007 地上物：產生、可見/不可見、拾取權限、保護/期限、切圖回來一致；多掉落物不得造成畫面長時間卡住。
- F008 角色基本 UI、快捷欄、背包、裝備、聊天、基本 NPC 互動、GM 查詢；後期金色風格與現代排版可切換、共同支援多解析度；視覺 UI 不修改權威資料。
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

M3 大量同步：20/50 Bot +300怪+500掉落，在 800×600／1280×720／1440×810／1440×900 與視窗縮放／鏡頭 Zoom／地圖邊緣無瞬移、正確受擊方向、死亡事件一致，P95/P99 具體可追。

M4 優化低資源：0/20/50/100人、不同畫質、登出切圖重複、長時間 soak、RAM/VRAM/Frame P95 A/B。

M5 遊戲系統與資料轉換：物品、強化、商店、技能、任務、寵物、機甲、編輯器與正式備份。

M6 PVP、小屋、社交、多 Channel、部署與維運。

交付標準：每個 Gate 提交 commit、測試指令、CI URL、負載種子、資產清單、環境、測量值、已知風險。未執行=NOT VERIFIED。

## 7. 2026-11-13 研究優先 Gate：原版遊戲素材取得與解析（先於 M1）

**範圍**：使用者指定後期《星夢 Online》視覺為唯一目標。此階段不開始遊戲 Client/Server，也不預設原版素材已存在或已可解析。優先人類角色（站立/走路/攻擊/受擊/死亡、方向、紙娃娃）、其次怪物/裝備/機甲/寵物、地圖/特效、後期金色 UI、聲音。任何關於檔案格式的結論需標示 DIRECT/AUDIT/HYPOTHESIS。

**AS001 原始安裝包調查**：盤點使用者本機原版 Client 安裝目錄、安裝程式/封包/更新檔、各版本來源；產生相對路徑、大小、SHA256、magic bytes、檔案群組、版本猜測的報表；不可從零星 SVN/Git 檔案宣稱原版素材齊全。保留只讀、不修改原件；權利未確認不公開原件/匯出圖。

**AS002 載入路徑追查**：以 Paradise.exe.lst 定位檔案開啟、資源目錄、解包/解壓、格式讀取、palette/alpha、影格表、方向表、裝備分層及 UI atlas 相關呼叫。產出「證據（檔案路徑、函數/位址）→格式假設→驗證方式」，不能只因副檔名推斷。

**AS003 來源完整性矩陣**：分角色（含男女/職業）、NPC、怪物、武器、裝備、機甲、寵物、技能、物品、地圖、金色 UI、音效；逐類紀錄實體檔、索引存在、缺件與可用性。區別 .spr/.sif/.itf/.itd/DAT 各自實際用途及版本差異；目前 Git 所見零星 SPR/ITD 與 .lst 不等於完整 Client。

**AS004 最小解碼 Proof of Concept**：先以一個有權使用的原版角色/怪物素材做唯讀格式解析，正確輸出多影格 RGBA PNG 與 JSON metadata（source hash、animation、facing、frame index/duration、pivot、offset、z-order、hit/action event）。不修改色盤、透明度或亂補影格。未知欄位必須記錄，不偽造。

**AS005 原版對照驗證**：建立原始遊戲截圖/錄影與解碼影格比對；檢查 8/其他方位數、待命、走路、攻擊、受擊、死亡、武器疊層、幀速度、透明邊緣、腳底錨點。所有解析錯誤明列，不默默略過。測試使用小型自建 fixture；原素材保留本機。

**AS006 Legacy Asset Viewer**：獨立於 Godot 的離線預覽工具，按檔案/角色/方向/動作瀏覽及逐格播放、顯示 frame/pivot/offset/z-order/來源雜湊，並匯出可重現 PNG+metadata。Viewer 不應反過來決定原版格式。

**AS007 Godot 資料契約評估（不實作遊戲）**：只有 AS001～AS006 實證成立後，評估分開 PNG、SpriteFrames、分組 Atlas、共享 Texture、按需載入/卸載；用同一批測試素材量測 RAM/VRAM/Draw Calls/Frame P95。不可先假定全部合成一張圖必然更快。

**AS008 法律及公開發行 Gate**：分清本機研究、修改轉換、公開 Git 發布、遊戲發行與配樂授權。無法確認權利者標 BLOCKED；正式 repo 只保留自行撰寫工具、規格、非侵權測試資料、指紋與研究摘要。

**進入 M1 條件**：至少一套原版角色完整方向與動作能重現、素材來源/格式/版本有證據、缺口列表已建立、解析器與 viewer 有可重現測試、權利範圍已確認。若無完整安裝檔，先做目錄/來源查找，不以占位圖假裝已完成原版還原。

## 7.1 2026-11-13 實證更新（不變更 M0 Gate）

參照 [原版素材位元組／呼叫鏈調查](docs/LEGACY_ASSET_EVIDENCE_20261113.md)，已以 `Paradise.exe.lst` 原始 Git Blob 與四個 NPC SPR、一個 SIF 樣本建立可追索證據。

- **已確認：** 原版支援鬆散讀檔及 group 資源 fallback、LZSS 解壓；部分 `.spr` 為 XN「影格資料 + 尾端影格索引」、逐列指令編碼、16-bit 像素與局部透明度。獨立 `.act` 路徑及人物方向／動作／部件資源組合的函式確實存在；`.itf` 是 UI 資源讀檔路徑，`.itd` 是 Item/Pet 資料表讀檔路徑。
- **已實作但非格式完整：** `tools/inventory_client.py` 對乾淨 HK2 做 SHA-256/magic 唯讀盤點；`tools/convert_assets.py` 全量轉 SPR/ACT/SIF/draw-order；`tools/legacy_spr_decoder.py` 是參考版本 XN 變體的獨立解析器，`tools/view_legacy_asset.py` 與 `tools/legacy_asset_viewer.py` 提供兩種預覽入口。未知動畫毫秒、完整方位與裝備 Z 層仍標 `null` 或 `UNVERIFIED`；固定 100ms 僅預覽。
- **未完成、不得放行：** 後期《星夢 Online》完整 Client 安裝包／更新檔盤點、完整 Player/Effect/Map/金色 ITF 原件、ACT 格式、角色 8 或其他方向數、所有動畫速度與紙娃娃排序、原版畫面逐格校驗、法律授權、Godot 性能 A/B；原版安裝檔缺失是硬性阻礙。不得先實作 Godot UI/Server/Atlas。
- **測試界線：** 本機自製 SPR/PNG/metadata/viewer 篩選單元案例已執行；原件 row structure 偵測與「動畫正確播放」是不同測試。CI、真正 Tk GUI、完整 Client、視覺對照的結果請依 todo.txt 與 CI URL 查證，不能預設 PASS。

## 8. 不變契約：素材共用、分組最小化與 FPS 隔離（2026-11-14）

- **查閱粒度與實體打包分離**：Enemy／Player／NPC／Equipment／Item／Pet／Mecha／Effect 等每個 normalized entity 的 action、facing、mirror、pivot、timeline、z-order 必須可獨立查閱與測試；這不代表一個 entity、方向、動作或部位要獨立 PNG／Atlas／Bundle。正式圖片如何合圖由 Builder 依相同 workload 的 A/B 自動決定。
- **Runtime 分組只看生命週期與實測收益**：不要因素材類型、怪物種類、方向、動作、圖片尺寸、來源檔不同而新增 Atlas／Bundle／Loader／Cache。經常一起載入、一起使用、一起卸載者優先共用；只有拆開能真實釋放可觀 RAM／VRAM，且 load、GPU upload、draw、frame P95/P99 或維護性有 Before/After 收益時才新增 boundary。沒有證據時預設較少分組。
- **Atlas side 不是資產類型**：512／1024／2048／4096 只可作 Builder 候選參數。4096 不因「一頁」自動勝出；需同時比較 CPU decode、transient workspace、GPU upload、texture/video memory、draw calls、frame pacing、切圖與卸載。
- **不要預建未出現的分類**：Boss、Rare、Hot、Warm、Cold、Direction、Action，以及尚未有 consumer 的 NPC／Equipment／Item／Pet／Mecha／Effect／Legacy UI 專屬 Runtime 類型，不得提前建立。新增素材優先只增加資料／definition，沿用 EntityAtlas + RuntimeAssetStore。
- **單一實體資源共用**：同一 manifest/page path 在 RuntimeAssetStore 只有一份 ready atlas，可由多個 owner 引用；owner generation、引用計數、bounded cache 與 release/purge 定義生命週期。新增內容不得要求修改 Runtime 核心才能載入。
- **Production assets 不是原始素材鏡像**：`client/assets` 只放目前 Runtime 真正使用的可重建輸出；完整原始／研究素材留 `.cache/client` 或 reports。正式 rebuild 不因「未來可能會用」把全部 Enemy/NPC/UI/Effect/Item 等搬入 Godot asset root。
- **完整成本 Gate**：任何「最佳化完成」必須提供同 workload Before/After：CPU decode、GPU upload、Draw Calls、process RAM、Godot texture/video monitor（driver VRAM 若可量另列）、首次載入、切圖、卸載、cache hit/recovery、Frame P95/P99、長時間 lifecycle/soak。沒有證據只能標研究候選。
- **Network/State 不變**：ServerSimClock、ClientNetworkPump、OrderedEventReducer、PackedWorldState、ViewProjection、WorldRenderer、UIRenderer 必須具清楚責任界線。Client 送收封包、解碼、按序套用、輸入送達與 Server tick **不得由 UI FPS/World FPS 增減而變快或變慢**。失焦/縮小/AFK/低畫質只可調整視覺、音效和資源生命週期。
- 不能依賴只在 `_process(delta)` 被畫面刷新時才 poll Network；動作依 ServerSeq/SimTick/ActionId 的時間軸取 pose，素材未載入不得停 Network/State，也不得補播遠端玩家舊位置。
- 完整素材必要性稽核與本機數據見 [docs/ASSET_NECESSITY_AUDIT.md](docs/ASSET_NECESSITY_AUDIT.md)；Render/Logic/Network 隔離驗證見 [docs/RUNTIME_ISOLATION_VERIFICATION.md](docs/RUNTIME_ISOLATION_VERIFICATION.md)。AS005 原版逐格人工視覺與 AS008 發布權仍是獨立未完成 Gate。

## 9. asset_converter 完整離線來源驗收（2026-11-15）

正式素材轉換只修改／使用`tools/asset_converter/`，由原始Data1+loose產生Godot所需素材；原版唯讀，八方向的來源重用／flip與ACT時間不能被packing改寫。原版完整SPR集合由當次來源稽核，不沿用舊cache中的固定總數。

目前原版來源75,588 SPR、7,890 groups、282,968 frames、8,094 pages完成離線建置；613 ACT、1,165 SIF、9 DrawOrder可從來源重解析比對。完整離線總量保留local-assets，不當作Runtime常駐；live仍只輸出真正有consumer的map/session/on-demand資源。PERF002實測後，Runtime優先512／1024候選，原SPR放不下再逐級使用2048／4096；離線archive策略不改。Map13為9 visuals/78 spawns、573 frames/5頁；Boy base 881 frames/2頁。Windows release正式重建驗收：OS記憶體峰值187.09MiB、frame P95/P99 18.549/19.200ms；地圖async 30次載卸回收通過，load P95 130.829ms、GPU submit P99 1.591ms。詳 [PERF002_MEMORY](docs/PERF002_MEMORY.md)；開發引擎仍超200MiB、實體driver VRAM及其餘未測項不能標PASS。

單入口`cli.py build <original>`先解包、來源稽核、增量建置再staging發布；`cli.py verify <original>`只驗收既有離線產物並輸出報告，檢查完整index、source/decoder/metadata/page SHA、影格來源序列、PNG尺寸及sidecar內容。verify不重播全量逐像素比對；pixel equality由builder建立並綁定前述指紋。preview或partial index不能通過完整驗收。build/verify以OS lock互斥，busy回傳2且不覆蓋先前報告；lock檔存在不代表程序仍執行。

候選只作幾何評估，選定後產圖、比較實際頁數/RGBA、逐格核對crop/size/origin/pixels。ACT/SIF/DrawOrder快取同時比較解碼內容，不能只看source SHA而沿用已被破壞的JSON。

此驗收只完成離線素材轉換範圍，不代表F002～F020或全Recovered功能已完成。Client一般30～90 FPS只改呈現，Client input/Server30Hz、snapshot10Hz仍依既有獨立契約；本次沒有修改同步頻率。具體證據見ASSET_CONVERTER、TODO及reports/converter_*。

PERF003後續取消／失敗回歸：1000次取消queued jobs由1000歸零，部分GPU提交取消／後頁失敗的resident帳目與Texture引用正確回收；metadata/error也受128-item上限。正式Windows release同場景峰值184.88MiB、59.997FPS、frame P95/P99 18.884/19.573ms，30次map載卸回收PASS。此為資源管理邊界修正，未改Render30～90與同步分離契約；範圍／重跑指令見 [PERF003_CANCELLATION](docs/PERF003_CANCELLATION.md)。

---

<a id="source-progress-md"></a>

## PROGRESS.md

*Historical source: `PROGRESS.md` — SHA-256 `326825bcf10a7cb0acc71e3a865a7e1e8e20a1f62cb24f0c02a571d553817335`*

# 開發進度（2026-11-14）

目前工作位置 `C:\SGO\SRC\SGTest`；直接同步 main，Godot Client 位於 client、C#/.NET 8 Server 位於 server。使用者指定合入 master；完整遊戲目標仍在進行。

## 已有實測

- 原版 HK2 Data1＋loose overlay 可重建素材；75,591 SPR／282,990 影格像素回建通過，方向區塊與 alias 保留。HK 與 HK2 的提取輸入逐檔 hash 一致。
- C# Server 權威移動、碰撞、雙向戰鬥、倒地／復活、出生保護、掉落與原子拾取；單調30Hz，不依 Client FPS。Server 不載入 Godot 或紋理。
- Godot Client 本地預測、確認重播、每實體歷史、RTT 時鐘、動作 revision。怪物 AI 分散5Hz，位置持續30Hz，動畫各自起始時間。
- Map13 的78個原版怪物出生／9種visual已接入，C#權威快照／動作帶definitionId、原地復活；Client按地圖共享圖集並分影格預載。
- Map13 地磚／物件／碰撞與男性基本紙娃娃接入，視窗最大寬1440及多解析選擇。
- C# domain測試、Godot狀態／方向／預測／跨語言MessagePack、8 client／1000怪／5000掉落、雙向戰鬥與原生1440×900測試均有 reports 證據。main原有及工作樹新研究測試也重新執行；最新結果記於todo。
- C# Server採workstation GC降低小型服務記憶體，最新CPU/RSS／frame P95見reports/README.md。舊ENet／UDP退化測試保留為歷史，不能套用於新WebSocket協定。

## 尚需完成

- 真實物品圖像／名稱映射及其他地圖怪物種類；main已有item.itd／ground-page研究可整合，不需重新猜測固定記錄大小。
- 怪物障礙繞路、其他地圖／轉場、正式登入／角色／背包持久化。
- 裝備與外觀自訂、完整UI與原版逐格視覺驗證。
- 新WebSocket協定的退化網路／重連，以及不同FPS／失焦／最小化下的完整trace一致性與資源回收。
- 正式發布權利及原版視覺Gate仍未通過；原版／生成媒體不放公開Git。

保留使用者在此資料夾原有的未提交ACT／人物研究變更，本次merge不覆蓋或夾帶提交。

---
