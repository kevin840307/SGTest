# SGTest Server — 重建設計

**狀態：已建立 .NET 8 Blocking/A*、單寫者 30Hz World、compact-binary V1、TLS listener／帳號認證／遊戲內創角／選角與本機 provision、generation-safe AOI／跨圖及自身權威位置／方向。創角出生點由 Host policy 指定並以 Blocking 驗證，Client 提交 slot/name，Server rules 開啟時可另送允許的 appearance tuple。登入節流、原版創角外觀／職業／出生規則、主要玩法與 Release Gate 仍未完成。** 目標為單體功能模組，固定世界模擬與獨立 Transport/Persistence。

## 核心邊界

創角外觀的 domain 與持久化在既有 `Identity/`：`CharacterAppearanceRules` 是 trusted Server 的不可變完整 tuple allowlist，`SqliteIdentityStore` 才擁有建立角色／外觀／錢包的唯一原子交易，資料庫 schema v24 以可選 `character_appearances` 保留舊角色未知狀態。外觀 Race 與後續 CombatProfile provision 必須一致。創角／列表 Wire 已接上；來源合法集合、Host 配置、AOI 及 UI 選項尚待接入，不能把 Storage 層驗證當作完整創角；詳 [來源與接入進度](research/CHARACTER_CREATION_APPEARANCE.md)。

| 責任 | 內容 | 禁止 |
|---|---|---|
| Host / Adapter | process composition、config、CLI、TLS listener 啟停；外部 token 使用英文 stable contract | 在 Host 寫 gameplay rule、localized Server command alias、直接改 World state |
| Gateway/Connection | trusted Actor binding、ConnectionEpoch、bounded parser/reliable queue、TLS 1.2/1.3 per-connection session；Auth/listener dispatch | 讓 Client 宣稱 ActorId/ConnectionEpoch、plaintext fallback、直接計算傷害／操作 DB 於 Tick |
| Wire | 實測選型的 binary/framed codec、版本、長度限制、重播保護；傳 semantic enum/state，不傳 UI 顏色 | 綁死 JSON／MessagePack、隱式依賴 Godot 角色類別、把 localized text 當規則 |
| Gameplay Policy | 純規則與 bounded parameters；英文 enum + 集中 `const/static readonly/Options` | handler 內散落 magic number、RGB/中文字串驅動 gameplay |
| Map World / Coordinator | 單寫者 authoritative positions、collision、AI、combat、drop、social state transitions | 在 Tick 同步等磁碟／Send；讓 presentation 決定規則 |
| AOI & Snapshot | per-player visibility、Spawn/Despawn、entity revisions、有限頻率更新 | 把看不到當死亡，或跨 map 沿用 epoch |
| Accounts | Account、Character、Session 安全與授權 | 將 ConnectionEpoch 當永久角色身份 |
| Persistence | SQLite WAL／migration／UID／交易原子性 | 用 DB 查詢驅動每隻怪 AI；在 repository layer 重算 gameplay rule |

實作分類（同一個 `SGTest.Server` 專案）：`GatewayContracts.cs` 是公開 trusted command 契約；`GatewayConnection` 使用同一個 `partial class` 按 `Ingress`／`ReliableOutput`／`AoiOutput` 實際生命週期分檔，主檔持有連線綁定、兩個 Gate 與唯一 bounded reliable FIFO。這不是切出新的 TCP listener、World/AOI 寫入者或各自的封包協定；WorldReady ACK／Resync 和 AOI 事件的入列／更新次序必須在同一鎖下保持。
`WorldResultRouter` 同樣是一個物件、同一個 session 索引和 12 條 bounded Channel；依 `GameplayIngress`／`ReliableReplies`／`WorldTicks` 部分類別分區。唯一 World Tick 消費者負責先保存 Changed 再可靠回覆；生命週期結束時必須完成**全部** Channel（包含 Identity／PvP 視覺），傳遞相同失敗例外，防止 AOI 下游等待無限延長。這不是把 Router 換成第二個 EventBus 或不同的 Queue Owner。

`Gateway/TlsCommandWorker<T>` 統一每個 TLS session 內 19 類一般指令既有的 bounded FIFO、單讀者、滿載拒絕與取消／故障傳遞；命令型別仍各有獨立隊列／容量，所有權與 close/join 仍由原 `TlsWireConnection` 掌握。NPC Portal Worker 需可靠發送 `NpcResult`、Skill Worker 需在乾淨關閉時排空已接受命令，因此刻意維持原特殊 Worker。使用泛型不是建立另一條玩法管線，測試必須驗證 FIFO、滿佇列、取消和來源例外。

暫不建立多個空 project、抽象 Service 層、事件總線和微服務。當 Combat/Items/Social 複雜到需要獨立測試才拆成模組。這個 modular-monolith + authoritative simulation + adapter/persistence boundary 是常見且適合目前規模的 game-server 架構；不是因為大型遊戲常見某技術就先導入 ECS、Actor framework 或 microservice。

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

實際新增 `server/src/SGTest.Server/World/ServerWorld.cs`，依賴既有 `BlockingGrid` 和 per-map `MapNavigator`，不依賴 Socket／Godot／DB，未引入第二套導航資料源。建構 Thread 是唯一可修改 Actor 的 owner；外部 Producer 只可透過 MPSC `TryPostMove` 非阻塞入列。MaxPendingCommands（預設512）採 CAS 限幅，Tick 每輪至多 `MaxCommandsPerTick=64`；`MaxPathRequestsPerTick` 已於 2026-11-18 由 **8 調為32**。原因不是提高無上限吞吐，而是 MVP 明確要求 20 名同圖玩家：真 TLS 20 Client 同 Tick 移動時，8 會穩定使部分合法玩家收到 `TickRouteBudgetExhausted`；32 可讓 20 人同時一次 A* 保有餘裕，同時仍是固定上限。Monster AI 使用自己的 `MaxPathQueriesPerStep`，不與 Player A* 混成無界工作。超過路徑預算仍須明確回覆 `TickRouteBudgetExhausted`，不能丟命令或讓 Client 順移。Tick 回傳最新 Actor delta 與 Receipt，但是**尚未有權威網路可靠事件佇列／backpressure/per-connection fairness**。

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

The TLS authorization callback has one production implementation: `IdentityTlsAuthorization`. Its pre-gameplay transition is `TLS established → AuthRequest → Account authenticated → CharacterList → optional CharacterCreate/result/list refresh loop → CharacterChoose ownership check → TrustedRegisterAsync → GatewayConnection → gameplay`. Character creation calls the same SQLite store outside the World thread; only slot/name arrive from Client. Spawn map/position come from `CharacterCreationPolicy`, which production Host enables only through explicit operator flags after validating the point against the served authoritative Blocking grid. The selected `character_id` is accepted only if owned by the authenticated Account.

This originally closed the identity gap between TLS and World before the production listener existed. The current formal Host now owns bounded global/per-address socket admission and an `AuthenticationRateLimiter` at the authenticated pre-gameplay boundary. Character selection keeps a configurable human-scale timeout (120s default, max 10 minutes). The auth limiter applies address + normalized-account failure windows/lockouts with bounded tracking; active lockouts cannot be LRU-evicted under key pressure, and complete protected-table saturation activates a temporary scope-wide fail-closed gate instead. Host exposes bounded `--auth-failures-per-address`, `--auth-failures-per-account`, `--auth-failure-window-seconds` and `--auth-lockout-seconds`. This remains process-local safety, not Internet volumetric-DDoS infrastructure or distributed/persistent bans.

## 2026-11-16 REB021-WORLD-RESULT-ROUTER

`Gateway/WorldResultRouter.cs` is the **single consumer of ServerWorldHost.Results**, routing only server-owned MoveReceipt to a TLS-authorized GatewayConnection by ActorId/ConnectionEpoch. Late stale-connection receipts are counted, never delivered to a replacement player session. Future ConnectionEpoch/MapEpoch without trusted binding fails closed. A known same-connection old MapEpoch remains tagged for Client discard. TryAttach allows replacement only with a greater ConnectionEpoch, and stale TryDetach cannot remove the replacement.

Changed actor batches are preserved with Tick in a **bounded Channel** for the future AOI/WorldReady consumer; a full Changed queue or reliable Gateway queue throws instead of silently losing a result. Callers MUST treat router fault as terminal (no retry of a partially enqueued Tick); production supervisor and AOI consumer are not implemented yet. The World single writer performs no extra socket/DB/render work. Five synthetic/real-host checks cover two-player isolation, generations, map transfers, FIFO, full queues and single-reader async pump; the existing real Godot TLS fixture now routes through this component. GitHub Server checks 56/56 PASS and Client TLS E2E PASS. **This does not yet publish AOI/WorldReady to clients.**

## 2026-11-16 REB020-TLS-SESSION-LIFECYCLE

New `IdentityTlsAuthorization.RunAuthenticatedSocketAsync` is the official session orchestration entry point for a future production listener (no listener is opened here): it performs TLS authorization/CharacterChoose, attaches the trusted Gateway route, then releases both Gateway binding and World actor on socket closure. `TlsWireConnection` has an optional bound-session exit callback, called after its reliable writer has stopped, independent of socket cancellation. The wrapper also uses lock-protected idempotent cleanup in finally to handle the outer Auth timeout/attach race. Selection cancellation or failed successful-choose response **after TrustedRegister** rolls back with `TrustedDisconnectAsync`; awaiting the trusted registration task itself prevents an orphan when only the caller's WaitAsync is cancelled. World tombstone generations remain, so a later same-character login gets a strictly newer ConnectionEpoch/MapEpoch. Stale detach cannot remove a replacement session.

C# real-TLS tests cover normal EOF/relogin and protocol-violation/relogin plus the existing Godot secure login/receipt E2E. This guarantees the tested login lifecycle only; **persistent last position, production admission/security policy, crash restart/reconnect resync and AOI remain NOT VERIFIED**.

## 2026-11-16 trusted lifecycle + map-bucket AOI baseline

`ServerWorld` now publishes ordered `ActorLifecycleEvent(Joined/Left)` for trusted register, disconnect, and cross-map transfer at the next 30Hz Tick, including when no MoveTarget exists. `ServerWorldHost` and `WorldResultRouter` preserve these in bounded `WorldChangedBatch` so stationary players do not appear/disappear only after someone moves. `WorldAoi` consumes batches in Tick order on a separate single-writer worker path, tracks player snapshots by map and spatial bucket, applies a server-bounded rectangular enter radius plus exit hysteresis, filters by map and generation, and retains ordered WorldReady, Spawn, Delta, Despawn *domain events* in bounded per-observer FIFOs. A transfer invalidates old-map visibility before publishing new-map WorldReady; disconnect clears the observer and sends Despawn to watchers. A backlog overflow is fatal, not DropOldest. No client-provided viewport or render FPS affects Server Tick.

**Important boundary:** this slice is limited to authoritative *player* snapshots and AOI event preparation; Godot wire encoding, the production AOI pump, Gateway reliable AOI egress, Enemy/Drop entity kinds, WorldReady ACK/resync and two-client visual E2E remain NOT VERIFIED. Do not mark the full MMORPG AOI requirement completed based on these deterministic server checks.

## 2026-11-16 WorldAoiPump runtime boundary

The single `WorldAoiPump` consumes `WorldResultRouter.ChangedBatches` in authoritative Tick order, applies the existing map-bucket WorldAoi and delivers pending WorldReady/Spawn/Delta/Despawn to authenticated Gateway reliable egress. `WorldResultRouter` remains the only WorldHost.Results reader. AOI handles only the current player-Actor model; the 30Hz World single writer performs no socket, serialization or AOI fanout. Peek→Gateway.QueueAoi→confirm prevents losing a reliable event on backpressure; generation mismatch is not sent to the wrong map/connection. A bounded 10ms idle poll and 32-event/observer budget prevent spinning or one busy observer monopolizing a tick. A full reliable FIFO raises a fatal AoiBackpressureException for future supervisor/resync, not DropOldest.

C# live 30Hz two-player tests cover WorldReady/Spawn/Delta/Despawn and bounded overflow; real Godot 4.7.2↔C# TLS loopback now receives WorldReady plus MoveReceipt. GitHub Server 68/68 and Client core PASS. Production listener/supervisor, WorldReady ACK, enemy/drop visibility, two visual clients and original-client performance parity remain incomplete.

## 2026-11-16 Trusted same-socket map-transition handoff

`WorldResultRouter.TrustedTransferAsync` bridges a server-authorized WorldHost transfer into the existing trusted Gateway binding without a parallel protocol. It captures the bound ActorId/ConnectionEpoch/MapEpoch, calls `ServerWorldHost.TrustedTransferAsync` (world collision/Map validation, old route cleared, monotonic MapEpoch), then advances the same Gateway binding before the AOI pump can queue events for the new receiver epoch. If the World transfer succeeds but the socket is already disconnected or the trusted binding was superseded, it detaches the old Gateway and issues a trusted disconnect for the *new* MapEpoch; this prevents a ghost character that its old map's TLS cleanup could not remove. Rejected map/walkability is a no-op. World Leave+Join lifecycle continues to generate AOI despawn and fresh WorldReady/Spawn by map.

**Trust boundary:** this method is callable only by a future server-side portal/NPC validator. It is not a client MoveTarget, and does not authorize arbitrary coordinates or implement portal rules. CI must validate the two-map pipeline before the TODO can be marked done; no claim yet of real Map15/13 art, client portal input, or cross-map Godot TLS E2E.

## 2026-11-16 Audited NPC portal authority slice

`Npc/PortalCatalog.cs` loads bounded, per-map `sgtest.npcs/v1` JSON generated by the **existing one** asset converter (not raw legacy Lua). Only `interaction.mode=confirm_transfer`, `adapter=audited_TS_Common_v1`, `dependencies_complete=true`, `choice=0` and `destination_status=walkable` become trusted portal entries; all other script modes, including `script_required`, are NOT executed. The catalog is immutable after startup, and duplicate maps/spawn IDs or malformed selected transfers fail closed. The original scripts' compile-time TS_Common hash is audited by the converter, not by this runtime loader.

`ServerWorld.TryTrustedNpcInteract` checks the authenticated ActorId, ConnectionEpoch, MapEpoch and monotonic interaction sequence, the map-scoped NPC SpawnId, server-known actor coordinates within a **candidate** 96px interaction radius (legacy distance NOT VERIFIED), and the authoritative destination Blocking. All checks and the existing `TryTrustedTransfer` occur atomically on the single World owner thread; no client-supplied destination is accepted. `ServerWorldHost.TrustedNpcInteractAsync` uses the existing bounded trusted control path. `WorldResultRouter.TrustedNpcInteractAsync` advances the same authenticated Gateway's MapEpoch only after a successful World transfer; if its TLS binding disappeared during the handoff it releases the new World generation to avoid a ghost actor.

This slice alone does **not** introduce an unauthenticated RPC or production NPC command wire. Actual client interaction and true-original Map15/13 private content validation remain separate.

## MVP003-TLS-LISTENER：Host服務組裝（2026-11-16）

`TlsGatewayListener`只負責接入與Socket生命週期，借用有效憑證及唯一IdentityTlsAuthorization／WorldResultRouter，不另建Wire／Auth或遊戲狀態。總容量128／每IP8／backlog128為有界預設，含握手與登入中Socket；拒絕不建立TLS任務。IPv4-mapped address先規格化；計數與Task集合的小鎖不執行DB或Send。正常認證／格式拒絕仍可繼續接入；非預期會話故障終止listener並讓supervisor觀測Task fault。取消會等待可信Disconnect及Router detach，並行Dispose共用清理Task，憑證／World／DB須存活至清理完。

Host `serve`明確讀取兩圖world／blocking與已稽核npcs、SQLite identity及PFX，預設loopback而非自動公開監聽；不產生帳密或憑證。World 30Hz owner、Router Results單reader、AOI ChangedBatches單reader與TLS網路相互獨立，任一pipeline停止／fault都停止服務；正常關閉先排空Socket，再停World與pumps。既有限時無網路smoke仍可用。未新增DB／Wire版本；Startup／參數命令見server README。已完成本機7項針對測試與Host建置；私有雙圖、Godot連正式listener、長時間／20人壓測與Release NOT VERIFIED。

## MVP003-IDENTITY-ADMIN（2026-11-16）

Host的本機`account-create`／`character-create`委派同一SqliteIdentityStore API；管理者有本機DB權限，這不是Client網路註冊／GM RPC。密碼從隱藏終端輸入或SGTEST_ACCOUNT_PASSWORD環境來源取得，不接受CLI明碼或輸出明文。建立角色先驗證管理者明示的Map13/15位置通過同一BlockingGrid來源hash／邊界／flag0，之後驗證帳密／disabled與Store的名稱／slot政策，沒有另建SQLite或Game Rule。

兩個管理命令各自原子、不是跨命令交易；重複帳號不改密碼，名稱／slot衝突不修改既存角色。管理 CLI 與遊戲內 CharacterCreate 共用同一 `SqliteIdentityStore` 名稱／slot／角色數政策。遊戲內創角已能在既有 TLS Session 對空帳號建立角色、刷新列表並直接選角；若 Host 未設定 server-owned spawn policy 則明確回 `Unavailable` 且 DB 不變。**網路公開帳號註冊、角色外觀／種族／職業選擇、原版預設出生點與完整遊戲 Gate**仍未完成。

### 2026-11-20 Identity migration / backup boundary

Identity persistence remains one `SqliteIdentityStore` outside the 30Hz World owner. Schema migration is a startup/operator concern, never a gameplay Policy or Client RPC. Before `InitializeAsync()` mutates any recognized older schema, the Store first creates a transactionally consistent SQLite Backup API snapshot in the same directory; only after that snapshot passes schema equality and `PRAGMA integrity_check` does normal migration proceed. Current schema is v22. The backup primitive remains unchanged: a recognized older schema is snapshotted before migration, and the current target version is reflected in the generated `pre-v<old>-to-v22` backup name.

The local `identity-backup --identity <DB> --output <NEW_DB>` adapter uses the same backup primitive but deliberately does **not** initialize or migrate the source, so an operator can preserve the exact pre-upgrade schema. Destination creation is fail-closed: source==destination is invalid, existing files are never replaced, and a successful result reports schema version, byte length and SHA-256. This is an operator/persistence boundary only; no game socket can invoke it, and it does not add a background DB worker to World.

Server Core **44766021159 PASS** verifies a live WAL point-in-time snapshot, a pre-v20→v21 migration backup that preserves the old version/data, CLI reopen/authentication and no-overwrite behavior; the complete Server suite reports **252/252 PASS**. This safety barrier is not an off-host backup service: retention, remote replication, encryption/key lifecycle and disaster-recovery rehearsal remain deployment work.

### 2026-11-21 Server-owned learned SkillBook

Original public Old-server evidence establishes that learned skills are durable Server state: `CSkill::AddSkill(WORD SkillID)` creates a `CDSkillInfo`, stores the original SkillID and initializes `Lv=1`; the old Skill DB exposes `SkillID/Lv/DBID/Recast`, and `CDSkillSys::GetSkillLv()` reads level from that Server-owned record. SGTest therefore does **not** accept a skill level from Client gameplay packets.

SQLite schema v22 adds the single `character_skills(character_id, skill_id, level, source_reference, updated_unix_ms)` table keyed by character + original SkillID. The trusted Store API can upsert/read the learned level; V1 bounds SkillID to 1..65535 and level to 1..255. Login opcode104 `SkillBookSnapshot` is Server→Client only and carries chunked `SkillID(i32)+Lv(u8)` rows. Non-empty books are sent before WorldReady; zero-skill characters intentionally emit no extra frame so existing V1 login ordering stays unchanged, and WorldReady closes the baseline as authoritative empty. A partial non-empty book followed by WorldReady fails closed.

Godot keeps only the authoritative level map and never invents levels locally. The real TLS gate seeds `28101 Lv3` in SQLite and requires Godot to observe `skill_book[28101]=3` before continuing gameplay. Server Core **44862370339 PASS** and Client Core **44862556115 PASS (71/71 steps)** cover schema persistence/reopen, bounded wire/golden, malformed/duplicate/reordered chunks, non-empty TLS reconstruction, normal reconnect/economy/PvP/cross-map regressions and formal Host + two main-scene clients. This is the authority prerequisite for original Cure/Buff formulas; **28101 healing itself is not enabled by this slice**.

### 2026-11-17 Changed-only owner state

WorldAoi now enqueues SelfState on an owner's increasing authoritative revision, separately from the unchanged remote interest filtering. It reuses ActorSnapshot, the existing single AOI consumer, bounded pending FIFO, Gateway reliable FIFO and fail-closed overflow supervisor; no new timer, broadcast, manager or serializer. Gateway requires the owner ActorId/ConnectionEpoch/MapEpoch binding. Idle revisions emit nothing. This is an additive V1 opcode using the existing 73-byte EntityState layout, a correctness fix to the established delivery path rather than a new algorithm selection. A future rate/coalescing change must preserve reliable events and measure the affected workload.

### 2026-11-17 Authoritative route facing

ActorSnapshot carries Facing (byte 1..8). The single World writer quantizes each successfully traversed path segment using the original world-space convention (E6/SE7/S8/SW1/W2/NW3/N4/NE5); blocked/invalid commands cannot rotate the actor. Stop retains the last direction, and transfer preserves it in WorldReady. Facing participates in the deterministic state hash and in changed authoritative snapshots; no idle direction heartbeat was added. Initial Facing=1 remains a provisional spawn policy until original spawn rules and persistent orientation are implemented. V1 WorldReady/EntityState are now 45/74 bytes; see RUNTIME_PROTOCOL.


### 2026-11-17 Position checkpoint boundary

`CharacterPositionCheckpoint` is the single Runtime owner for periodic player-position persistence. It is deliberately outside `ServerWorld`: World remains a single 30Hz in-memory writer, while `WorldResultRouter` makes a memory-only observation of already-published authoritative snapshots. The coordinator coalesces by Actor/ConnectionEpoch/MapEpoch/EntityRevision and performs one SQLite transaction per periodic dirty batch; default cadence is 5 seconds and is bounded/configurable by `--position-checkpoint-ms`.

Graceful disconnect final persistence and periodic writes use the same writer semaphore. A final snapshot marks its generation finalized before waiting on the database and leaves a tombstone after commit, so a delayed older periodic capture cannot overwrite the final Map/X/Y. A newer authenticated connection generation legitimately re-opens tracking. This keeps database latency and locks off the World Tick, avoids a second persistence architecture, and bounds process-crash **position** loss without pretending position snapshots are reliable combat/economy events.

The Host supervises this checkpoint pump exactly like routing/AOI: a checkpoint infrastructure fault is terminal to the service and is reported through `TLS_HOST_FAIL`. Public CI proves coalescing, background flush and stale-vs-final ordering. Server core 43991613110 additionally hard-kills the producer process after durable 40,8 and a later uncheckpointed 24,8 movement; a fresh verifier process reopens the same SQLite DB at 40,8. This validates process-kill/reopen of the last committed position, but not a kill injected in the middle of the SQLite transaction itself.


## 2026-11-17 Monster authority / shared AOI boundary

Monster gameplay state is map-owned, never connection-owned. `MonsterPopulationCatalog` is the trust gate between visual/source evidence and gameplay: converter v3 population is explicitly visual-only and cannot instantiate a runtime; only a bounded verified v4 document may create `MonsterRuntime`. Original Enemy_Data evidence currently establishes spawn identity/position/flags plus Level (column 3) and MaxHP (column 9). Damage, aggro/range/cooldown and player combat stats remain unresolved source rules and are not silently inherited from the earlier TestGoGo prototype.

Each `MonsterRuntime` is a single-writer map authority with bounded work (`MaxAiPerStep`, path-query budget and A* expansion budget) and round-robin fairness. It consumes only already-authoritative player position/generation snapshots through `MonsterWorldCoordinator`; it performs no DB/socket/render work. Monster-enabled `ServerWorldHost` opts into `PublishIdleTicks` so the existing 30Hz World clock remains the sole simulation clock even while players stand still. Player-only hosts retain event-only output. `WorldAoiPump` is still the only AOI consumer. Same-tick monster snapshots are staged in the same `WorldAoi` before Player Join/Move visibility refresh, producing one latest Spawn baseline instead of a stale Spawn followed by an immediate Delta.

Players and monsters share one interest manager, hysteresis, visible limit, Gateway reliable FIFO and same-MapEpoch resync. There is no MonsterAoi or second transport. Server CI 44021869726 passes 101/101 checks including a 300-monster bounded AI trace and a live Host→Router→AI→AOI→Gateway path.

Client presentation remains non-authoritative. `WorldState.monsters` is separate from player entities; `MonsterPresentation` resolves only the v3 visual population's sprite/atlas and verified ACT semantics (S→NS idle, W→NW movement, A→FA attack, D→ND death; H→NH reserved for a future hit event). It uses the existing PositionHistory/ActionTimeline and joins players in the same WorldDrawList/scenery order. Client CI 44021869721 proves the presentation gate and two real formal-host main scenes with one presented monster each. Synthetic CI combat numbers are marked test-only and are not original-game claims.


## 2026-11-17 Ground loot durability slice (MVP006 partial)

正式資料庫維持一個 SqliteIdentityStore（schema v3），新增 `ground_drops`、`item_instances` 及 `loot_world_generations`；**不是第二套持久化引擎**。背景可信代碼可從 SQLite 分配每次 Server 啟動的 WorldGeneration；由 WorldGeneration + 單一啟動期間單調的 Monster EntityRevision 組成 DeathRevision，避免重啟後同一 monster 的 local revision 從 1 重算而誤與既有掉落事件衝突。分配機制與 `TrustedDropSpawn` 已完成，**正式 Host 尚未掛接啟動時分配**。

同一 death/slot 重複送達只能取得原 drop_id，資料內容衝突不得修改既存掉落。拾取結算以單一 SQLite transaction 同時設定 ground_drops 領取者（含連線／地圖世代與操作序號）及插入唯一 item_instances item_uid；重送已成功的相同請求回原 item_uid，不會重發；兩角色同搶同一 UID 只有一個成功。API 的 `TrustedPickupContext` 必須將來由 authenticated Gateway 以 Server authoritative ActorSnapshot 建立，不能把玩家自報的座標／CharacterId 當可信來源。領取距離暫用 Server 端 96 world pixels 作合成測試 policy，**不是已證實的原版拾取規則**。資料庫 I/O 嚴禁發生於 World 30Hz owner thread。

`GroundLootChecks` 覆蓋建立冪等、內容不一致、無效來源、啟動世代隔離、錯地圖／超距／重送／舊序號、兩角色同搶與 SQLite 關閉再開。2026-11-17 [Server CI 44074950492](https://github.com/kevin840307/SGTest/actions/runs/44074950492) 通過，既有完整 [Client CI 44074950367](https://github.com/kevin840307/SGTest/actions/runs/44074950367) 通過。**尚未實作：原版可信掉落表、MonsterDeath→bounded async drop commit、active drop→WorldAoi 單一 interest、可靠 Spawn/Despawn／PickupResults Wire、WorldReady/resync/reconnect drop 恢復、Godot 地上物／背包 UI、多人實機 E2E。** 這些仍為 MVP006 的下一個完整切片。


## 2026-11-17 MVP006 — lethal Monster → bounded durable loot handoff

唯一 `MonsterWorldCoordinator.ResolveNormalAttack` 經 Server attack MapEpoch／Sequence／可信 CombatProfile、距離、Blocking、cooldown 得出 Applied 與死亡 MonsterSnapshot；`WorldAoiPump` 是死亡事件的唯一投遞點，且仍按同 Tick `ApplyMonsters` 維護權威 AOI。只有 Applied+HP=0+State=Dead 可投遞，重送、MISS/Blocked、CD、錯 map 和已死怪不會再落物。 `MonsterLootSettlement` 提供有界 channel，TryPostDeath 不進 DB／不 await；獨立 Worker 對明確 source-verified 規則按死亡 UID 生成 drop slot，滿佇列 fail-closed，乾淨停機等 AOI 停止後完整排空 Worker。Host 可選 `--loot-rules`，完全不提供來源時不啟用掉落；只有指定明確 `source_reference`、可信 v4 population identity、唯一 slot 的人工稽核 manifest 才生效。初版是保證掉落列表，原版機率未證實且不臆測。

同一 DB 原 `SqliteIdentityStore` 的 schema v3 在 Host 啟用 loot 時先 `AllocateLootWorldGenerationAsync`，由 `WorldGeneration << 32 | MonsterEntityRevision` 識別死亡；`CreateTrustedGroundDropsAsync` 對同 death 的 1～32 個 slot 共用一筆 WAL SQLite transaction，冪等衝突核對、任一不同內容則整批 rollback，無部分 slot 遺留。背景 DB latency 不在 World 30Hz Tick 或 AOI 路徑。 `GroundLootChecks`、`MonsterLootSettlementChecks`、`NormalAttackChecks` 的公開合成資料驗證 Worker、來源閘、單多 slot 同/異內容重送、容量背壓、真 TLS-independent normal attack→death→DB pipeline，Server CI 44080311785 PASS。這不代表真原版掉落表、概率、多人 GroundDrop AOI／Wire、inventory UI 或 hard kill 後死亡日誌已完成；入隊與 commit 間仍有 crash gap，且來源真實性依賴操作者的人工審核。


## 2026-11-17 Immutable ground-drop AOI without a second interest engine

`WorldAoi` map-local buckets now include SQLite-committed `GroundDropRecord` and each observer's `VisibleGroundDrops`. `SeedGroundDrops` runs once before the first World batch from Host's startup-only SQLite read, so the existing WorldReady/Join/MapEpoch/Resync replay naturally contains active unpicked UIDs. Workers publish bounded committed result batches only after atomic DB commit; `WorldAoiPump` stages at most 8 such result batches per poll and uses existing reliable egress accounting, never performs SQLite I/O. DropSpawn/Despawn opcodes 32/33 are 52/32 bytes; drops are immutable and generate no idle Tick deltas. Capacity/observer counts apply across Player/Monster/Drop. `RemoveGroundDrop` is an AOI method reserved for the future authoritative successful Claim delivery; it is not connected to Client pickup yet.

CI 44083366652 PASS tests actual NormalAttack lethal -> trusted SQLite commit -> authenticated GroundDropSpawn, map isolation, view enter/exit, WorldReady & Resync restoration, and 20 viewers/500 drops with no idle resend and a single removal fanout. This is a deterministic correctness gate, not 20/500 CPU/RSS/P95 performance evidence. No original item sprite or verified drop probability is implied. Database journal for kill-to-commit crash recovery and Gateway pickup commands remain NOT VERIFIED.


## 2026-11-17 Pickup result V1 and atomic ground removal

Authenticated raw client GroundPickup V1 (34/28 bytes) does not carry actor identity, position, or item-definition. Gateway reconstructs `TrustedGroundPickup` from the current bound TLS session; a bounded per-connection async pickup worker reads a ServerWorld owner-thread actor snapshot, checks current Router binding and epoch, blocks dead characters, and serializes with NPC transfer on the same socket via `gameplayGate`. The only inventory mutation API is the pre-existing SqliteIdentityStore `ClaimGroundDropAsync`, with SQLite WAL transaction committing claim + unique item_instance together, fixed provisional `PickupRange=96` world px and idempotent authenticated generation+sequence. Rejections never mutate AOI; successful claims enqueue ground-drop removal into bounded `WorldResultRouter.ClaimedGroundDrops`, consumed by the **same** WorldAoiPump that sends all Player/Monster/Drop messages, then send a reliable per-request `GroundPickupResult` V1 (35/45 bytes). Client never has authority to supply earned UID or remove the shared drop. A notification queue overflow after DB commit is terminal, not a fabricated success; restart bootstrapping removes already picked objects.

Server Checks now verify C# ↔ Godot fixed golden binary and Server-only result rejection, plus actual real World attack lethal→trusted worker→SQLite ground UID→Gateway pickup→one-time item UID→nearby AOI Despawn. Server CI 44085408907 PASS. Open: full inventory snapshot/merge, actual original pickup rules and item icons, Server death-vs-pickup atomic serialization, end-to-end process crash recovery, measured 20-player same-drop network stress.


### Pickup generation uniqueness after Server boot (2026-11-17)

World's in-memory Actor.ConnectionEpoch resets across process restarts; using it verbatim as SQLite `pickup_connection_epoch` can collide for the same character/map/client action sequence. Formal TLS Host now reserves a DB `loot_world_generations` identity on **every** boot, not only when loot-rule content is active. In `IdentityTlsAuthorization.PickupHandler`, once the current Router binding and ServerWorld snapshot have been validated with their **real** in-memory epochs, `SqliteIdentityStore.ComposePickupConnectionEpoch(worldGeneration, actor.ConnectionEpoch)` is passed only as the DB idempotency generation. Wire and World authority are unchanged. `GroundLootChecks.RestartConnectionGeneration` exercises same actor, recreated ConnectionEpoch=1 / MapEpoch=1 / PickupSequence=1 after simulated restart, new UID grant and old session's unchanged replay UID. Client cannot supply generation. Isolated test fixtures that construct IdentityTlsAuthorization without formal Host may use the former raw epoch; production supplies a nonzero durable value.


## 2026-11-17 Auth-bound durable inventory baseline

Existing SQLite `item_instances` remains the only source of truth for owned items; no new inventory database or recurring polling worker. Once authenticated account ownership, character registration and combat state attach succeed, authorization reads `ListInventoryItemsAsync(character_id,4096)` exactly once and refuses to present a misleading partial baseline if more than 4095 items exist. Before the World AOI writer starts for this connection, it sends the compact Server-only `InventorySnapshot` opcode 36 in consecutive 128-item chunks (23-byte header, 25 bytes/UID: ItemUid, ItemDefinitionId, Quantity, SourceDropId, EnhancementLevel(u8)). `Complete` on the last chunk is mandatory, including zero-item inventories. The same TLS session and authenticated character ID are used, and no Client opcode can forge its own inventory baseline.

Client keeps UID-keyed immutable baseline and uses only validated `GroundPickupResult` to append newly earned item UIDs; map transfer preserves inventory and reconnect reconstructs afresh. Server `InventorySnapshotChecks` covers fixed cross-language golden binary, strict item/content validation, 4095-entry bounds and 32 chunks; `TlsGatewayListenerChecks` seeds a verified synthetic fixture into SQLite with two unique UIDs and proves both survive two real TLS logins. Server CI 44089132226 PASS. Recovery use and destroy-discard are implemented in later sections; equipment, other item-use rules, ownership transfer/trade/bank, pagination beyond 4095 and measured large-inventory allocation overhead remain open.

## 2026-11-17 Verified recovery item settlement

Production --recovery-rules is optional and defaults to NO effect admission. Server reads only an explicitly operator-reviewed verified_recovery_item_rules/v1 manifest, never untrusted item names/images. Compact V1 RecoveryUse opcode37 (28 bytes) carries map epoch, monotonic sequence and item UID; Gateway binds authenticated ActorId/ConnectionEpoch. A 16-request bounded TLS worker shares the gameplay gate with NPC, respawn and pickup. Combat owner suspends/drains queued Monster damage, then identity SQLite v4 atomically reduces item quantity (or deletes last stack), restores durable current_hp and records a unique receipt in recovery_use_actions. Committed combat projection is synchronized and reliable RecoveryUseResult opcode38 (45 bytes) is queued BEFORE resuming AI hits. A replay of a previously committed request returns the original receipt without applying HP/quantity again, including after DB reopening. Formal TLS, re-login, Host manifest rejection and queued-monster-hit ordering tests: Server CI 44109415226 PASS.

Source-derived recovery .tres values alone do not prove original game formula, cooldown, MP/SP, job/race restrictions, buffs or animation. These remain NOT VERIFIED. Schema v3 to v4 uses existing InitializeAsync migration path; no second DB or Tick-bound SQL.

## 2026-11-17 Authenticated inventory destroy/discard settlement

Inventory discard is a **destroy action**, not a ground-drop creation path. The compact V1 Client command is opcode39 / 32 bytes: `MapEpoch + uint32 DiscardSequence + ItemUid + Quantity`. ActorId, ConnectionEpoch, item definition and any economic effect remain server-owned. Gateway injects the authenticated binding and a bounded 16-request TLS worker shares the existing gameplay gate so inventory mutations cannot race the same connection's recovery/pickup/transfer operations.

SqliteIdentityStore schema v5 adds `inventory_discard_actions`. In one transaction it verifies exact ownership and quantity, conditionally decrements the selected `item_instances` row or deletes the final unit, then persists a durable receipt keyed by character + durable connection generation + map epoch + action sequence. An identical committed retry returns `AlreadyApplied`; a reused sequence with a different payload, foreign UID or excessive quantity cannot destroy inventory. Reliable opcode40 / 41-byte `InventoryDiscardResult` reports the authoritative item id, discarded quantity and remaining quantity only for Applied/AlreadyApplied results.

Godot applies inventory changes only after an authenticated Applied result, never on button press. It rejects unsent/future/wrong-UID results, ignores old-map/replayed results and serializes discard against recovery item use. Client core 44113626146 PASS includes the C#/Godot 32/41-byte golden, one-time reducer, final-stack removal, transfer/replay/spoof checks and SessionPanel intent boundary. Server core 44113938394 PASS adds a real TLS login → durable InventorySnapshot → discard/replay/final-delete → relogin test proving the removed stack is not resurrected. Commit `2b5af066` then closes the cross-runtime gap: a real Godot 4.7.2 ClientSession receives the SQLite-backed inventory, sends two authenticated discard commands through TLS, waits for each authoritative result before local mutation, and the C# fixture verifies the durable stack is empty; Client core 44114783838 PASS and companion Server core 44114783786 PASS. Original rules for bind/no-drop items, confirmation UI, whole-stack behavior or dropping an item into the world remain NOT VERIFIED.

## 2026-11-18 Durable player combat presentation fanout

Monster→Player HP settlement remains owned by `PlayerCombatSettlement` and SQLite. Its committed lifecycle posts a compact `PlayerDamageSettlement` notification into a bounded `WorldResultRouter` presentation channel **after the database transaction has committed**. The notification is not sent directly from the DB worker to sockets or AOI. `WorldAoiPump`, the existing sole AOI owner, drains that channel and asks `WorldAoi` to fan out only to observers that currently see the exact victim ActorId/ConnectionEpoch/MapEpoch generation. The victim is included. Backpressure faults the service rather than silently dropping a committed hit visual.

Wire opcode42 is deliberately presentation-only and omits CurrentHP/MaxHP; only damage amount, death flag, attacker monster identity/revision and facing are shared. The victim-specific opcode8 remains the only live HP damage authority. Durable respawn follows the same ordering: SQLite restore → World CombatDead clear → requester opcode31 admitted → bounded opcode43 presentation post → combat targetability resume. This prevents presentation from becoming a second gameplay state machine.

Server core 44200133417 PASS covers codec rejection, Gateway reliable FIFO, AOI visible-peer scope/stale generation rejection and existing settlement/death/respawn regressions. Client core 44200393222 additionally exercises the same server through real TLS with two clients.


## 2026-11-20 Authenticated equipment enhancement boundary

Enhancement is an Identity/SQLite-owned economic mutation coordinated from the authenticated TLS session, not a World-tick formula. The formal path is `Godot ClientSession -> compact opcodes 94/96 -> Gateway authenticated binding -> bounded enhancement worker -> gameplayGate -> current ServerWorld actor/NPC access revalidation -> SqliteIdentityStore -> reliable opcode 95/97`. The client never chooses identity, current/target level, success probability, price, failure charging policy or random roll.

`NpcEnhancementCatalog` admits only operator-verified enhancer spawns and resolves their coordinates through the audited NPC data already used by other NPC systems. `EquipmentEnhancementCatalog` admits only explicit verified per-definition steps. Formal Host requires the NPC and item manifests as a pair; no manifests means no enhancement authority. The SQLite transaction keeps the existing ItemUid in inventory/equipped storage, applies the admitted step and wallet charge, and stores the exact authenticated generation/MapEpoch/sequence receipt so retry cannot double-charge or double-enhance.

The 30Hz World/AOI owner does not execute SQLite enhancement work. Current ActorId/ConnectionEpoch/MapEpoch/death and NPC distance are revalidated before preview or mutation. Enhancement commands share the existing per-session gameplay serialization with other inventory/economic mutations. Only the committed reliable result changes client-visible +N.

Schema v20 introduced `enhancement_level` on inventory, storage and equipped rows. To make reconnect reconstruction complete, the one existing authenticated login baselines carry that field directly: InventorySnapshot opcode 36 and EquipmentSnapshot opcode 57 use 25-byte item rows with a trailing u8 level. StorageSnapshot opcode 53 likewise uses 25-byte rows so +N survives storage browsing and round-trips without a second state channel.

Schema v21 extends the same single ItemUid state into durable trade/player-shop snapshots. `trade_commit_items` and `player_shop_listings` persist EnhancementLevel, while the existing trade receipt durability field remains part of the committed item snapshot. TradeSnapshot opcode 71 now exposes each offered row as UID/definition/quantity/provenance/+N/DurabilityRaw. After both authenticated gameplay gates are acquired, final settlement re-reads the offered inventory rows and rejects any definition/quantity/provenance/+N/durability drift before commit. This makes Client-side “economic mutation disabled while trading” only a UX optimization, not a trust boundary: a malicious or parallel repair that changes reviewed durability 80→70 is rejected before ownership or wallet mutation. The durable trade receipt replays the committed +N and durability without consulting mutable current item state. Player-shop listings freeze +N together with UID/provenance/durability; Buy compares the current row against that immutable snapshot inside SQLite, so enhancing or otherwise changing the listed item state makes the listing stale instead of silently selling a different state.


## 2026-11-20 opt-in public account registration boundary

Public account creation remains inside the existing TLS pre-gameplay identity boundary; it is not a second HTTP service and does not grant World authority. Formal Host defaults registration to disabled. When explicitly enabled, opcode17 AccountRegisterRequest enters a dedicated `AccountRegistrationRateLimiter` keyed only by canonical source IP before `SqliteIdentityStore.CreateAccountAsync` runs. The registration limiter is separate from the authentication-failure limiter because a successful creation is still expensive work and must consume quota; every Created/Duplicate/Invalid attempt counts. The table is bounded and capacity exhaustion fails closed.

A successful create returns opcode18 `Created`, but `IdentityTlsAuthorization` deliberately keeps the connection pre-gameplay and requires the next frame to be the ordinary AuthRequest. Account ownership, CharacterList, CharacterChoose, World registration and Gateway binding therefore retain exactly the same authority path as provisioned accounts. Godot exposes registration as an explicit action and returns to `STATE_NEW` after Created before entering `STATE_AUTHENTICATING`; automatic reconnect never repeats registration. Failed/disabled/rate-limited registration ends authorization without a trusted actor.

Server Core **44769015052 PASS (256/256)** covers disabled/enabled TLS registration, duplicate/quota behavior and zero World actors. Client Core **44770285345 PASS** proves real Godot→TLS→C# registration followed by normal Auth on the same socket, an empty authoritative CharacterList, and no gameplay actor. This boundary intentionally does not provide email verification, CAPTCHA, account recovery, cross-node quotas or WAF/DDoS protection.

## 2026-11-20 online Whisper routing boundary

Whisper is part of the existing authenticated chat subsystem, not a separate socket/service. `GatewayConnection` binds sender identity and enforces one shared per-socket ChatSequence for Nearby and Whisper. `TlsWireConnection` keeps the same bounded chat worker. `IdentityTlsAuthorization` accepts exactly one direct-message selector: positive ActorId or bounded character name. ActorId resolves through the current Gateway binding; names use `WorldResultRouter.TryResolveOnlinePlayerByName`, which resolves only currently connected authenticated PlayerIdentity with Form-C/trimmed ordinal matching and fails closed on missing/ambiguous names. Only a currently connected exact generation can receive a direct message.

Nearby remains dual-AOI fanout. Whisper is a direct online route and may cross MapEpoch/map boundaries. The sender-side opcode99 result is reliable and is Delivered only after the target reliable queue accepts opcode93. For name targeting the result carries the Server-resolved ActorId/current generation/name. `GatewayConnection.QueueWhisperResult` keeps exact TargetActorId equality for legacy ActorId requests but allows trusted name-resolution to correlate a resolved ActorId back to the original TargetActorId=0 request; Actor/Connection/MapEpoch/ChatSequence correlation remains exact. Server Core 44756567741 and Client Core 44756567680 PASS this boundary. There is no offline persistence or mailbox polling. Party membership/routing is implemented below; Guild/Faction/Warband remain future work.


## 2026-11-20 online Party membership boundary

Party state is owned by one in-memory `PartyCoordinator` attached to the formal TLS Host. It is intentionally **online and ephemeral**: PartyId, invitations, membership, leader and revision are not SQLite records. This keeps a reconnect/restart from accidentally resurrecting stale connection generations while still allowing a party to survive Map15↔Map13 transfer on the same authenticated socket.

The trust path is `Godot ClientSession -> opcode100 PartyCommand -> Gateway authenticated Actor/Connection/Map binding -> bounded 16-command party worker -> PartyCoordinator -> reliable opcode101/102/103`. Client commands carry only PartySequence/action/PartyId plus an invite target selector. ActorId-targeted invites retain the fixed 37-byte form; invite-by-name appends a bounded UTF-8 character name. `WorldResultRouter` resolves that name only against current authenticated PlayerIdentity using Form-C/trimmed ordinal semantics and feeds the resolved current ActorId/ConnectionEpoch into the same PartyCoordinator generation/TTL path. Client never chooses the target generation. Membership stores exact ConnectionEpoch per Actor; stale old-socket disconnect cleanup therefore cannot evict a newer generation. Leader-only invite/kick/promote is checked Server-side. Leader leave/disconnect transfers leadership deterministically to the first remaining member; final departure dissolves the Party. Outstanding invitations are invalidated on leader changes.

Pending online invitations are scoped to the **recipient connection generation** as well as the inviter connection generation; they expire after **60 seconds of Stopwatch monotonic elapsed time**. Accept/reject of an outdated invite returns `InviteStale`; a new invitation to the same ActorId can replace an expired or previous-generation record. An old socket's late Disconnect callback removes a pending invitation only if its **recipient ConnectionEpoch matches**, so new connections cannot have their invitations erased by stale cleanup. Expiry is lazy on relevant Party commands rather than a new background worker. CI [44742204787](https://github.com/kevin840307/SGTest/actions/runs/44742204787) **PASS** includes exact reconnect-invite and simulated 61-second timeout tests without wall-clock sleeping. The 60-second lifetime is a V1 anti-stale policy, not a recovered original-game rule.

Party Chat deliberately reuses the existing bounded chat subsystem: opcode92 channel 3 enters the same per-socket ChatSequence replay gate as Nearby, and opcode93 fanout uses Server-owned sender identity. Recipient selection comes only from current Party membership, not AOI. This allows cross-map chat while preventing nonmembers from receiving it. If a remote member's reliable queue is full, that session is closed rather than silently dropping a reliable Party message.

Godot keeps authoritative Party membership through map transfer, clears only pending map-scoped Party commands, and rejects conflicting or stale snapshots/revisions. SessionPanel renders member names, leader, invite/result state and exposes invite/accept/reject/leave/kick/promote plus Party Chat without performing local membership transitions.

Verification: Server Core 44739756995 PASS; Client Core 44739756959 PASS on functional HEAD `efcc52c6`. The two-real-Client Map15→Map13→Map15 Gate creates the Party before transfer, retains membership after AOI separation, delivers Party Chat cross-map, and ends with `party_count=0` after clean socket teardown. Formal Host advertises `online_party_enabled=true`.

Still outside this boundary: verified original party-size/distance behavior, EXP/drop sharing, party buff/assist gameplay rules, persistent/offline parties, and private Windows party visuals. Authenticated online invite-by-name is implemented, but does not claim the original game's distance/UI rules. Guild/Faction/Warband remain future systems.


交易由 `TradeCommandIngress` 管理單一連線的請求順序及 binding 檢查，`PlayerTradeCoordinator` 保有雙方確認、雙 actor gameplay gate 與原子結算順序。Gateway 組裝 ingress，並接入換圖及離線生命週期。PvP 地圖與犯罪規則各有獨立檔案。未來領域見 [模組導航](ARCHITECTURE.md#模組導航與預留位置)。

`PvpMapPolicy.cs` 管地圖是否允許 PvP。`PvpCrimePolicy.cs` 管犯罪與標記轉換，並保有一起使用的 `PvpMode` 和 `PvpNameState`。新領域以實際程式及依賴邊界建立目錄，規劃見 [架構導航](ARCHITECTURE.md#模組導航與預留位置)。
