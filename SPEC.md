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
- 協定：版本化 MessagePack / Protobuf 擇一（ADR 紀錄）；TLS WebSocket 初版，ServerSeq/SimTick/SessionEpoch/EntityRevision/MapEpoch；不沿用舊 AES/Base64 JSON wire 格式。
- 內容：原生 JSON/二進位資料包，離線工具轉換 Old；官方原版素材與原始碼需先確認合法使用權，否則以自製/測試素材代替。
- 開發：GitHub Actions 自動 lint/compile/test/資料驗證/模擬負載；以具體指標改善，無測試就不聲稱達標。

## 3. 功能需求與完成定義

### Phase P0 — 可玩最小多人核心
- F001 帳戶/登入/登出/選角/建角/多角色/重連/身分過期，重登不得保留幽靈 Entity。
- F002 地圖載入（登入→選角→入圖多階段進度）、Blocking/視野/傳點、世界座標與地圖局部座標轉換；AOI 半徑依實際 viewport／zoom／角色位置與緩衝計算，不能沿用原版 800×600 固定可見範圍，須測各預設、視窗拖曳及地圖邊角。
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

## 8. 補充不變契約：素材共用與 FPS 隔離（2026-11-13）

- **素材原則**：EntityCount 與 UniqueAssetCount 分離；同圖數百怪/數千掉落可能只有少數素材種類，同種類共用 Texture/AnimationDefinition/FrameTable/Material，每實例只保存狀態。離線編譯 512/1024/2048/4096 候選尺寸、Map/Region co-occurrence、hot/warm/cold、Paper Doll 層、Boss 動作分頁；按 RAM/VRAM/Frame/DrawCall 和正確性實測選型。
- **Network/State 不變**：ServerSimClock、ClientNetworkPump、OrderedEventReducer、PackedWorldState、ViewProjection、WorldRenderer、UIRenderer 必須具清楚責任界線。Client 送收封包、解碼、按序套用、輸入送達與 Server tick **不得由 UI FPS/World FPS 增減而變快或變慢**。失焦/縮小/AFK/低畫質只可調整視覺、音效和資源生命週期。
- 不能依賴只在 `_process(delta)` 被畫面刷新時才 poll Network；背景/OS throttle 及 Engine FPS 設定影響須實機驗證。動作依 ServerSeq/SimTick/ActionId 的時間軸取 pose，素材未載入不得停網路/State，也不得補播遠端玩家舊位置。
- **獨立 Gate**：不同 UI/World FPS 下相同 packet trace 的 Rx/Tx、State hash、可靠事件次序與 QueueAge/ReducerDelay；不同解析度/Zoom 下相同 visible world/camera contract 的 Texture/VRAM/DrawCalls 與不卡頓。具體規格見 [docs/ASSET_STREAMING_AND_RENDERING.md](docs/ASSET_STREAMING_AND_RENDERING.md)、[docs/CLIENT_RUNTIME_ISOLATION.md](docs/CLIENT_RUNTIME_ISOLATION.md)。
- **現況：僅方案，效能與 gameplay 實作 NOT VERIFIED。** AS001～AS009 原任務進度不因此變更。
