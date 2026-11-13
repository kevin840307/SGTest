# SGTest 系統架構（Architecture v1）

狀態：設計提案；具體 Tick 數字與資源方式需經基準校正。參考來源詳見 LEGACY_REFERENCE.md、NETWORK_AND_MOTION.md。

## 1. 避免舊版問題的四條邊界

1. WorldState ≠ Database：世界中位置/怪物 HP/攻擊冷卻等短期狀態在 RAM；背包/角色/貨幣/物品 UID 的最終資產在 DB transaction。不得每次怪移動讀/寫 DB。
2. LogicState ≠ RenderState：邏輯/封包/AOI 持續運行，畫面可見物件才花 CPU/GPU/Node；切圖/AFK 不會使攻擊或拾取停擺。
3. EntityState ≠ Scene Node：網路來的1000個 Entity 僅產生緊湊資料；Viewport 可見約束和 Visual Budget 決定要建立哪些 UI/繪圖，玩家視覺優先。
4. Direction/Motion ≠ Animation：面向由 movement/action 的明確優先級決定；攻擊/受擊是有 ActionId 的一次性事件，普通 Move 不能重啟或取消它。

## 2. 結構

~~~
SGTest/
  client/                      # Godot 專案
    scenes/                    # 登入/選角/世界/UI 等少量場景
    modules/                   # login, world, combat-view, inventory-ui, social-ui
    core/
      protocol/                # WebSocket/codec/seq/epoch/connection
      state/                   # packed EntityRecord / movement snapshots / action timeline
      presentation/            # visible renderer / paper-doll / sprite atlases / pool
      camera/                  # viewport AOI + culling
      resources/               # cache/streaming/disposal
    tests/
  server/
    SGTest.Host/               # composition + gateway, no gameplay SQL
    SGTest.Protocol/           # typed versioned contract and validation
    SGTest.Domain/             # pure rules/entities
    SGTest.Application/        # use cases, world orchestrator
    SGTest.WorldRuntime/       # single-writer map/channel actor
    SGTest.Infrastructure/     # SQLite provider, migrations, content catalogs, Outbox
    tests/                     # domain, protocol, integration, load
  shared/contracts/           # ID schema + generated wire source
  game-data/                   # own/authorized defs, maps, art placeholders
  tools/                       # importer, map/item editors, replay/benchmark
  docs/
  .github/workflows/
~~~

引用依賴：Host → Application → Domain；Infrastructure 實作 Application/Domain 的 ports（不得讓 Domain 引用 Infrastructure）。Protocol DTO 不混入資料庫 Entity。Client 的 Scene 不能呼叫 Server DB 或自行計算權威戰鬥傷害。

## 3. Client 資料流與排程

NetworkIO（不可由 FPS 停掉） → BoundedDecoder/Verify → OrderedEventReducer → PackedWorldState → ViewProjection → RenderFacade。

- NetworkIO：從 WebSocket 取完整 Frame，按線上版本驗證長度/格式；有限工作預算不能任意丟可靠事件，超載以 metrics 與背壓/斷線恢復方案處理；僅在主執行緒必要位置寫 Godot Node。
- OrderedEventReducer：以 MapEpoch/SessionEpoch/EntityRevision/MovementSequence/ActionSequence 去重與排序；Entity 狀態先於 Visual。
- PackedWorldState：以 struct-like Typed Array/Dictionary of Arrays 管理 EntityId、position、velocity、facing、action、hp、visualResourceId；查詢 O(1)，不要一 entity 多層隨機 Dictionary 和 Timer。
- ViewProjection：產生 stable RenderRecord、AnimationIntent；依 Camera bounds + AOI + 近中遠 tier 計算可見 Visual 清單。
- RenderFacade：角色紙娃娃（少量玩家維持高品質、動作最優先）、怪物/掉落物簡化 batch，SpriteAtlas/material 共享，不要所有 Entity 都有獨立 Area2D、Label、AnimatedSprite2D；點擊命中獨立空間查詢。
- ResourceCache：分 AssetCatalog/Atlas、Chunk/Scene、FX pools，明確 ownership/refcount、prewarm/TTL/eviction；logout 釋放世界相關引用，測100次進出不成長。
- FixedLogicLoop：維持輸入、StateReducer、遊戲內需更新的時間軸與連線；可以用固定 60Hz 但不可被 render FPS、低畫質或 AFK 降頻。
- RenderLoop：前台30/60/其他配置與低畫質模式只限畫面；失焦與最小化可省繪圖，但需有測試證明網路/固定邏輯/戰鬥不降速。

### Godot Scene 建議
少量 Scene：GameRoot、WorldCanvas、UIRoot、PlayerVisual、RemotePlayerVisual、MonsterVisual（僅可見/重用）、FxPool。World Canvas 可以按 Material/Texture atlas 分 CanvasItem/RenderingServer 批次。不要為掉落物建立每件 Timer；用統一時間及稀疏資料表。

### GDScript、C#、GDExtension 的門檻
先 Pure GDScript 搭 profiler 定位；如果有可重現熱點（例如 1000 Entity state processing）才做 C#/GDExtension A/B，並把 .NET Runtime 起始 RAM、開發成本、CI 圖形回歸算入。優先改善資料布局/演算法/批次及素材生命周期。

## 4. Server MapActor（關鍵）

~~~
WebSocketGateway <-> SessionRegistry <-> CommandAdmission
                                    |
                                    v
                             MapRouter / MapActor
                               |- Player states (single writer)
                               |- Monster states + AI scheduler
                               |- Ground drop runtime
                               |- Spatial index / AOI cache
                               |- Combat & movement rules
                               |- OutboundEventBuilder
                               +--> Durable command/outbox worker
                                         |
                                       SQLite
~~~

- 以 MapId+ChannelId 決定 Actor，同圖世界 state 單寫者。網路 worker 只投遞有界命令，不持 MapActor 的長鎖。
- Actor 排程在固定 SimTick 上整合移動/攻擊/怪物，事件按順序產生；AI 決策可較低頻率，但路段移動/權威時間不能跳到終點（固定推進/確定時間函式）。
- 尋路高成本工作可以在獨立 pool 產出帶 StateVersion 的候選路徑，回 Actor 重新驗證版本、Blocking、目標；過期結果丟棄。
- 每 Tick 先計算戰鬥與狀態，再產出 AOI 事件；每個客戶端在 WorldSnapshot 裏不再擁有獨立整張地圖怪物。
- DB：在 actor 外的資產交易以唯一 requestId、預先保留/完成/失敗狀態回覆，再由 actor 套用結果；不可先顯示成功後在 DB 扣款失敗；Outbox/一致性策略需按領域明確定義，避免「非同步」變雙花。
- 位置持久化：記憶體記錄 latest、低頻批次寫入與換圖/登出 flush，失敗可重試。不要每個 walk/idle 都同步 UpdateState 寫 DB。
- 送網路由 OutboundEventBuilder 出 immutable payload，再交 ConnectionWriter；慢 socket 不得回頭阻塞地圖模擬。
- 初版 1 process 持有多 MapActor；達瓶頸才拆 Channel/Pod，跨圖採 two-phase authority handoff + epoch barrier，不複製玩家或掉落。

## 5. 頻率模型（初始值，非承諾）

- Fixed gameplay step：先試 30Hz（33.3ms）或 20Hz；Client fixed state step 60Hz。必須測算是否足以回應玩家操作與碰撞，不可任意拿 250ms Monster AI 當世界唯一更新頻率。
- AI Decision：基於行為不同優先級，例如戰鬥中的 5–10Hz、閒置 wander 2–4Hz，錯峰/時間預算；移動持續依 Server 時間 advance。
- Player movement updates：主動 Start/Turn/Stop 事件即時通知，moving corrections 初步評估 10–20Hz 並結合 AOI/視野變化；不能把接收間隔當移速。
- Monster：path/waypoint 改變即推送 movement segment；不每幀重複發位置，遭攻擊/停止/死亡必須即時權威事件。
- Drop：出現/拾取/消失事件可靠；不動物件沒有定期 Move；視覺動畫可低資源。

依 docs/PERFORMANCE.md 對 20 玩家+300怪數據決定實際頻率；若 Tick 超時就找 CPU/DB/Queue 根因，不靠降低封包處理速度蒙混。

## 6. 規格性測試
- 一張地圖50個 session 共享同一個怪物生命與座標；任何 map/session epoch 更新後無 ghost。
- 10人同一怪最後一擊只有一次 death/exp/drop；經濟/物品守恆。
- MapActor Tick p99、actor queue age、Gateway queue age、DB CommitMs 有獨立 metrics；Server 頻寬按 AOI 近遠分層。
- 角色 Action FSM 在高延遲、同幀收到 Move/Hit/Stop/Death 仍完全正確。

## 7. 多尺寸資產系統及 Client/Server/UI 完整隔離（2026-11-13）

- 主文件：[ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md)、[CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
- NetworkPump/Codec/CommandGateway → bounded ordered queues → OrderedEventReducer（MapEpoch/Seq/Action barrier）→ PackedWorldState → immutable ViewProjection/ViewModel → WorldRenderer／UIRenderer。Server MapActor 有獨立 SimulationClock，Client NetworkPump/StateReducer 與 Render/VisualClock 分離。
- **無論 UI FPS/Render FPS 30、40、50、60、120 或最小化/失焦/AFK，Client↔Server 收送包/解析/State reducer/command flush/戰鬥/移動/撿物工作率不得因設定改變。** 不可只在被 FPS 控制的 `_process(delta)` 裡 poll Socket。UI 只經 command/event snapshot 互動。
- MonsterVisual/ItemVisual 以 VisualDefinitionId 共用 Texture/FrameTable；多實例不重複解碼和建立大型 Node。離線 Map Asset Palette 用 shared/map hot/warm/cold + 512～4096 的候選 Atlas，Runtime 用有界 async streaming、GPU commit、lifecycle 管理。
- Visual 邏輯在資源尚未載入時不得停止 State；素材完成後讀最新 pose，避免 AOI 外圍遠端角色從原點/舊位置瞬移。不同解析度依實際 CameraWorldRect 預載，不能假設 800×600。
- 原「Client fixed state step 60Hz」屬初始候選頻率，不是由 Render FPS 推導或控制的實作要求。Godot/OS 背景 throttle 需以獨立 IO/Reducer 排程和 telemetry 實機驗證。
- 本章為設計，**尚未有可執行 Client/Server、NOT VERIFIED**。
