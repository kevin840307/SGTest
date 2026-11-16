# SGTest Server — 重建設計

**狀態：已建立 .NET 8 Blocking/A*、單寫者 World、30Hz Host、compact-binary V1、generation-safe Gateway，以及真 loopback TCP+TLS 1.2/1.3 單連線 session；Production Host 仍不開 listener，尚無真正 Account Auth/CharacterChoose、正式 AOI 或 Godot Client/Server E2E。** 目標為單體功能模組，固定世界模擬與獨立 Transport/Persistence。

## 核心邊界

| 責任 | 內容 | 禁止 |
|---|---|---|
| Gateway/Connection | trusted Actor binding、ConnectionEpoch、bounded parser/reliable queue、TLS 1.2/1.3 per-connection session；後續 Auth/listener | 讓 Client 宣稱 ActorId/ConnectionEpoch、plaintext fallback、直接計算傷害／操作 DB 於 Tick |
| Wire | 實測選型的 binary/framed codec、版本、長度限制、重播保護 | 綁死 JSON／MessagePack、隱式依賴 Godot 角色類別 |
| Map World | 單寫者 authoritative positions、collision、AI、combat、drop | 在 Tick 同步等磁碟／Send |
| AOI & Snapshot | per-player visibility、Spawn/Despawn、entity revisions、有限頻率更新 | 把看不到當死亡，或跨 map 沿用 epoch |
| Accounts | Account、Character、Session 安全與授權 | 將 ConnectionEpoch 當永久角色身份 |
| Persistence | SQLite WAL／migration／UID／交易原子性 | 用 DB 查詢驅動每隻怪 AI |

暫不建立多個空 project、抽象 Service 層、事件總線和微服務。當 Combat/Items/Social 複雜到需要獨立測試才拆成模組。

## 時鐘與事件

- World Tick 以 30Hz 作原型參考起點，Snapshot 10Hz 作起點；未重新實測前不是硬性完成值。
- Map command queue 必須有界，single writer 推進世界。過載時採非阻塞入列、每 Tick 工作預算與明確拒絕／重試回覆；**不得為等待慢 Client／DB 把 Tick 卡住，也不得把可靠遊戲指令當成可丟棄視覺快照**。
- 事件應有 ConnectionEpoch、MapEpoch、EntityRevision、MovementSequence、ActionSequence 等對應世代，不能拿一個 SessionId 混用。
- Combat 命中／MISS／死亡／復活、掉落物 UID 與原子拾取應是 Server 權威結果，不依賴 Client 動畫幀。
- 玩家進出 AOI、換圖、重連時應正確同步 Spawn/Despawn、初始位置／動畫狀態，避免幽靈角色或外圍順移。WorldReady 快照與後續增量須有明確序號邊界；缺序／慢客戶端必須安全重同步，不可讓事件佇列無限增長。

## Wire 選型與 AOI 流量 Gate

- REB020/WIRE001 已選定**單一 compact-binary V1**；`GatewayConnection` 綁定 trusted Actor/ConnectionEpoch，`TlsWireConnection` 已對真 loopback TCP 強制 TLS 1.2/1.3。TLS 內先跑 Auth/CharacterChoose callback，回傳 trusted Gateway 後才准 gameplay；plaintext 直接握手失敗。Production Host 仍不開 listener。下一 Gate 是真正 Account/Character persistence + Auth/CharacterChoose wire flow、production cert/listener，再接 ACK／WorldReady／reconnect。
- 對相同 World Tick 與可見 AOI，量測每玩家 bytes/sec、訊息／秒、Serialize/Deserialize P95/P99、GC alloc、AOI 候選與扇出成本、Send Queue Age、輸入到畫面延遲；須與全圖無過濾基準公平比較，確認可節流網路且不破壞同步。
- AI 的各怪物決策時間與位置推進分離；獨立 NextDecisionTick／分散預算避免所有怪物同時轉向／移動，Server 仍維持可重播且單寫者權威，不能用同一批群體動畫假裝自然 AI。

## MVP Server：Map15 + Map13 兩張權威地圖與互通

Server 初期只加載兩張原版 Map/Blocking/population/NPC，**共用相同 Map World/Session/AOI/Blocking/A***；Map15 主城支援角色登入、多角色、NPC 商店銀行倉庫修理、交易/玩家商店/聊天、物品強化與持久化；Map13 台地用於怪物獨立 AI、追擊、Combat/Skill/MISS/Death、掉落拾取與高密度多人 AOI。原版各圖功能／NPC／PvP 權限需從 Recovered/Old 或原始 Dat 核實。

MVP 的**Map15↔Map13 真實轉圖**必須由 Server authority 決定合法入口與目標位置；原子轉移 Session 的 MapEpoch／WorldReady／known-sets、清除舊圖 AOI observer、建立新圖 snapshot，處理多人同時傳送、延遲封包、重連、Drop / NPC / Combat 的舊圖事件隔離。不得只把 Client 隱藏地圖做成假傳點。DB/Socket/資產讀取不能阻塞 World Tick。

MVP 中物品 UID／倉庫銀行／技能／Pet/Mecha/卡片／升級／交易／商店／PvP／GM 等單圖可用功能依原版全部需要完整 Server Authority 與重送／競態／持久化測例；好友暫緩。Map13 同圖 20 玩家/300 Enemy/500 Drop，Map15 NPC／玩家商店多人操作與兩圖同時活躍，須測 Tick P95/P99、AOI fanout、Queue、CPU/GC/RSS 及可靠事件。MVP 後才全圖／全內容與 100/1000/5000 Gate。

## 內容格式

Server 使用**原版 Blocking.dat byte grid** 或經可驗證的無損對應資料；只載入 Map world/population/collision 和必要規則，不解碼 PNG/Atlas。Enemy 外觀由 Server population logical ID 傳給 Client（其目錄為 Enemy/<id>）。

資料由 tools/asset_converter/content 輸出。新 Server 沒有可執行專案仍可先產生 content 與驗證 mapping。

## 必須重做的回歸

- 身份／權限／連線世代與 protocol fuzz。
- 20/100 人同地圖，300/1000 隻怪與大量掉落，AOI/AI/Combat 線性負載與分佈。
- Map Tick P95/P99、socket queue age、DB transaction wall-time 分離測試。
- 遠端角色走動／攻擊／受擊／死亡是獨立有序事件，Client FPS 和 Unfocus 不影響權威狀態。
- 登出、切圖、重連、交易重試的可靠一致性。

歷史原型 Server 程式與 376 PASS 不在重建分支內；任何新版完成狀態皆以 todo.txt 和新測試為準。
## A*／碰撞／大量怪物的固定 Tick 設計

- 以原版 Blocking.dat 做可行走格網，必要時建立 map-local immutable collision index；A*／導航的 Server 驗證使用此資料，不以 Godot 的即時渲染碰撞替代權威。
- 怪物追擊與巡邏使用**可重用的 Map-level path/AI 工作預算**，依距離／狀態分攤昂貴路徑計算。不要每隻怪每 Tick 建一個 A* 物件或全圖更新；距離過遠應脫戰回歸。
- Tick 的命令→碰撞／移動→戰鬥→AOI→發佈階段有界、可量測；同一 Map 的可變權威 State 由單寫者更新，非阻塞背景 path 計算需帶 MapEpoch／revision 驗證才可提交結果。
- **AOI 先於封包編碼與廣播**：用 map-local spatial hash/grid 或實測合適的索引維護每位玩家的訂閱集合；只將相關 Player／Enemy／NPC／Drop 更新給有權接收的觀察者，**不能全圖每 Tick 廣播或每封包掃描所有實體**。登入／進出視野／換圖有可靠 Spawn/Despawn／WorldReady 基準，視野邊界使用適當 margin/hysteresis；同一實體未改變不重送相同狀態，位置／Facing 的過時快照可按版本合併。可靠傷害、物品與交易結果須送給正確對象且不可被視覺 AOI 吞掉。不同解析度或相機範圍保持周邊同步一致；**Client 回報的 viewport/zoom 必須由 Server 限幅與限頻**，防止過大視野或頻繁改尺寸放大廣播成本。
- **AFK/多開**：Server 永遠按權威 Tick 推進；可依明確的 AFK interest subscription 降低不重要的視覺 Snapshot，但**不得因 Client Render FPS／視窗焦點改變核心 Tick、Reliable Events 或角色防作弊驗證**。每個連線維持獨立權限／輸出隊列配額，慢客戶端不能拖垮整張地圖。
- 20／100 人、300／1000 怪、500／5000 掉落的測試必須記錄 AI budget、World tick P95/P99、packet queue age、AOI fanout、記憶體、慢連線和斷線情境。

### population v3 的資料邊界

Converter 當前輸出 `visual_spawn_content` v3：原始位置／外觀可供資料驗證；`stats=null`、`stats_verified=false`、`gameplay_ready=false` 表示原版戰鬥／AI 規則未完成。正式 Server 必須在原版數值與規則完成驗證後才啟用玩法，不能把空值補成先前原型的 HP／damage／速度。v2 產物需以唯一 CLI 重建，沒有正式 Server／DB consumer 需要線上遷移。NPC 檔保留 script_required 與未核實動畫旗標，資料輸出不代表已執行 NPC 商店／銀行／任務。

## 2026-11-16 REB021-BLOCKING-NAV：首個 .NET 8 World 核心切片

`server/src/SGTest.Server` 現有**不依賴正式 Wire 方案**的 `BlockingGrid` 與 `MapNavigator`。原版 `Blocking.dat` 是 8-byte 小端 width/height＋width×height raw flags，world v3 指明的 `collision_cell=16`、`size`、`map_id` 與 SHA-256 必須全部吻合才能載入。Map13/15 兩張原圖為 960×960 cell（15360×15360 world pixels），flag 分佈僅 0／6／7；目前嚴格 raw 0=walkable，6／7=blocked，其它值 fail-closed。**原版 3 等特殊地形權限尚未直接核實**，舊 SGO prototype 的推測不直接納入正式 Server。

Segment collision 使用完整 cell supercover＋禁止穿格角；8-neighbor A*（10/14 cost、octile heuristic）禁止斜角切障，tie-break 決定性、路徑含起終 cell，擴展預算預設4096、用完明確拒絕。原版 Map13 2560/2880、Map15 7872/11296 的 portal destination 格和相鄰路徑查詢已核對，但**沒有權威傳送／Session／MapEpoch Runtime**。

測試使用 `server/tests/SGTest.Server.Checks`，不依賴外部 NuGet／原版私有圖片；9 組合成 case 加兩張原版資產交叉驗證一組，共10組PASS。原版近距 128 deterministic 目的地嘗試／圖（有效目標 Map13=77、Map15=119）全成功，A* query P95 約59.5／26.0µs、allocated 10.2／10.9KB/request；是單一 Windows 實機 isolation trace，**不是實際 20 人、300 怪或 World Tick Gate**。Map-level Navigator 應共用，但這版 query scratch 尚會配置 Dictionary/PriorityQueue，未比較池化／多負載，不宣稱低 allocation 最佳化已完成。詳 `server/README.md` 的重跑命令。

下一步須以原版 Direct 證據確認特殊格旗標與走路語意，再接單寫者 Map Tick、budgeted path jobs、可信命令來源／MovementSequence／MapEpoch 與 Client actor 的完整路徑使用；尚未啟動 Socket/World Simulation。

## 2026-11-16 REB021-WORLD-CORE：單寫者手動 Tick（未接正式 Server Host）

實際新增 `server/src/SGTest.Server/World/ServerWorld.cs`，依賴既有 `BlockingGrid` 和 per-map `MapNavigator`，不依賴 Socket／Godot／DB，未引入第二套導航資料源。建構 Thread 是唯一可修改 Actor 的 owner；外部 Producer 只可透過 MPSC `TryPostMove` 非阻塞入列。MaxPendingCommands（預設512）採 CAS 限幅，Tick 每輪至多 MaxCommandsPerTick=64 和 MaxPathRequestsPerTick=8，避免無限路徑搜尋／命令累積；當滿載明確拒絕或產生 `TickRouteBudgetExhausted` receipt，不能誤當成功並讓 Client 順移。Tick 回傳最新 Actor delta 與 Receipt，但是**尚未有權威網路可靠事件佇列／backpressure/per-connection fairness**。

每個 Actor 只有已確認的 Server MapId／Position；`ConnectionEpoch` 遞增識別重新登入、`MapEpoch` 遞增識別可信換圖、`LastMovementSequence` 消費輸入、`MovementActionSequence` 標示最後**成功授權**的 Move／Stop、`EntityRevision` 標示可見狀態更新。先驗證世代及序號才以 immutable 原圖 `Blocking.dat` 做 A*，每個固定步進都複核線段碰撞。錯誤命令不終止既有合法路線；Stop 即使不移動亦發修訂，避免其他玩家一直顯示走路。Debug StateHash 可確認相同指令 trace 在不同 Render-read 取樣頻率下仍一致。Session 建立／轉圖／登出是**可信 Server 內部 API**，尚未實作登入、NPC 傳點權限與 WorldReady／AOI，絕不能直接映射到不可信 Client 封包。

世界目前**只有手動 `Tick()`，沒有真正每秒30次的 scheduler**；`MovementPixelsPerTick=8` 是固定測試候選值，不是原版速度。正式的 actor rate/mount mode、可取消長路徑、超時重連、Snapshot ACK 與可靠事件的 Gateway 還需建立。原版 special terrain 權限仍未驗證，raw 0 walkable／其餘 flags 禁行保守 fail-closed。詳 `server/README.md`；`MVP003` 與 `REB021` 主項未完成。

可重跑 `dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj`，合成21/21 PASS；加 `-- --assets <private_client_assets>` 在真 Map13／Map15 核對再加3組，合計24/24 PASS，其中有20個合成 Actor 同圖、一次 Tick 6 路徑／14拒絕的上限驗證。沒有20條Socket、300隻Enemy或雙真Client，也未測實際30Hz的P95/P99。

## 2026-11-16 REB021-HOST-CADENCE：真正的固定時鐘但尚非網路伺服器

`server/host/SGTest.Server.Host` + `server/src/SGTest.Server/World/ServerWorldHost.cs` 使原先需要手動呼叫 Tick 的 World 核心可用 .NET 8 Release 獨立 Console Host 持續按 30Hz 模擬。**此 Host 不開 Socket／HTTP，也不提供帳號或 Gameplay**；它不等於完整 Server、30Hz+10Hz AOI Snapshot、正式 Client/Server E2E。

World 必須在它自己的單一專屬 Thread 上建立／Tick；Stopwatch 單調 deadline 不耦合 UI FPS、Sleep 無忙等，落後週期累積 `SkippedDeadlines` 並**跳過過時 deadline**，避免集中 catch-up 壓垮 AI/網路。Host 控制命令和 TickResult 各使用固定容量 Channel；可信註冊/轉圖/斷線只能由已經驗證授權的 Server 內部使用；即使 TryPostMove 是 thread-safe，未有 Gateway 綁定身份，不准原樣暴露給外部連線。每輪 TrustedControl / A* / Move 皆有各自上限。

WorldTickResult 有權威 Receipt/Delta；送出 Channel **不使用 DropOldest**，滿載則 Host 異常終止，讓未來連線層進行斷線／完整快照重同步，避免事件在背壓下消失。Stop 仍未處理的命令視為未確認，不能將 TryPostMove=true 當成最終交易／行動 ACK；正式 Gateway 必須訂出重送與操作序號的可靠性。Host 現在只有 Result Channel，不具備每玩家隔離、AOI 過濾、ACK、慢消費者分流、WorldReady 及 DB/AI；這些仍屬 REB019/020/021 後續項目。

真兩圖 5秒 idle Windows 實測：151 ticks／5020.14ms、30.079Hz、SkippedDeadline=0、max single tick work 1219μs（**0個 Actor，無 socket/AI/Drop**），作為將來 20人/300怪/500Drop 世界Tick p95/p99的**起始空載對照，不宣稱滿載性能**。合成27組、含原始Map13/15則共31組 Server 回歸 PASS。使用命令詳 `server/README.md`。

## 2026-11-16 REB023-IDENTITY-STORE

Identity persistence is a separate non-World service. `PasswordHasher` uses one production Argon2id format (`v=19,m=9216,t=4,p=1`, 16-byte random salt, 32-byte hash) with a bounded concurrent-KDF semaphore; parameters are encoded with the hash so a later policy can rehash on successful login. `SqliteIdentityStore` owns schema v1, WAL/foreign-key configuration, Account/Character constraints and transactions. Connection pooling is intentionally disabled: identity traffic is not a Tick hot path, and deterministic file/connection lifetime is preferred over hidden pooled state.

Security boundary: Account authentication returns server-owned account/character records only. The future TLS authorization flow must select a character owned by that authenticated account and use the persistent `character_id` as the trusted identity when calling `TrustedRegisterAsync`; Client packets must never carry an authoritative ActorId/ConnectionEpoch. DB/KDF calls must never execute on the World single-writer thread.

Current gate: six Identity checks plus existing TLS/Gateway/World checks = **46/46 PASS** locally on Windows; Server/Host Release build 0 warnings/errors. TLS Auth/CharacterChoose wire integration and production listener remain NOT VERIFIED.

## 2026-11-16 REB020-AUTH-CHARACTER-WIRE

The TLS authorization callback now has one production implementation: `IdentityTlsAuthorization`. Its state transition is `TLS established -> AuthRequest -> Account authenticated -> CharacterList -> CharacterChoose ownership check -> TrustedRegisterAsync -> GatewayConnection -> gameplay`. No database operation runs on the World thread. The selected `character_id` is accepted only if it belongs to the authenticated Account; the authoritative spawn and first MapEpoch come from persisted server state and World registration.

This closes the previous identity gap between TLS and World without opening a listener. It does **not** yet solve production socket admission, brute-force/rate limits, account creation, WorldReady/AOI, reconnect or Godot transport. Character selection is allowed a configurable human-scale timeout (120s default); future listener policy must bound simultaneous pre-gameplay sessions.
