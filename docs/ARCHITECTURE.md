# SGTest — 新版架構總覽

**狀態：新版 Client／Server 基礎、TLS 登入／選角、權威移動／AOI／跨圖及初步玩家呈現已逐步實作；主要玩法與多人視覺／效能 Gate 仍未完成。** 目前以 todo.txt 和各切片證據為準。

使用者決定移除舊版可執行原型，保留素材轉換程式和歷史研究，從協定、世界狀態與渲染責任重新開發。任何舊測試 PASS 不等於新版本 PASS。

## 正式文件（唯一入口）

| 主題 | 文件 | 用途 |
|---|---|---|
| Client | [CLIENT_ARCHITECTURE](CLIENT_ARCHITECTURE.md) | 運行時資料流、World Y-sort、FPS 隔離 |
| Server | [SERVER_ARCHITECTURE](SERVER_ARCHITECTURE.md) | 權威狀態、Tick、AOI、DB |
| 命名／功能分類 | [CODE_ORGANIZATION](CODE_ORGANIZATION.md) | C#、GDScript、Wire owner、命名與測試 Gate |
| 素材 | [ASSET_ARCHITECTURE](ASSET_ARCHITECTURE.md) | 一素材一 Atlas／AssetStore 契約 |
| 轉換與發行 | [ASSET_CONVERTER](ASSET_CONVERTER.md) | SPR/ACT/SIF、轉換器與獨立 PCK |
| Wire | [RUNTIME_PROTOCOL](RUNTIME_PROTOCOL.md) | Epoch/序號／事件與資料合約 |
| 遊戲功能 | [GAMEPLAY](GAMEPLAY.md) | 核心功能與階段 |
| 測試／效能 | [TEST_PLAN](TEST_PLAN.md) | 測試 Gate、Model1 公平比較、AFK／多開驗收 |
| 素材原版證據 | [LEGACY_ASSET_PIPELINE](LEGACY_ASSET_PIPELINE.md) | 原始素材與可追溯資訊 |
| UI | [OLD_UI_DESIGN](OLD_UI_DESIGN.md) | 原版後期金色視覺的設計參考 |

完整的舊研究報告／ADR／歷史 TODO 不刪內容，集中至 [research/INDEX](research/INDEX.md)。其中「已實作」敘述全是**舊原型歷史**。

## 架構風格

**Godot 4 Client + .NET 8 Server + Python Asset Converter**。Client、Server 各是功能導向 modular monolith，不拆微服務，也不建立空 Manager／Core／Domain 階層。

1. **Authority** — Server 決定座標、Blocking.dat 碰撞、攻擊、死亡、掉落、物品 UID、金錢、交易與跨圖。
2. **Network ≠ Render** — 網路 I/O、decode 與可靠事件處理不依賴 UI FPS、視窗焦點或 AFK。
3. **State ≠ Render** — Client WorldState 可重播；Renderer 只是讀取狀態的投影。
4. **Asset ≠ State** — 影像未就緒仍正常處理 Spawn/Move/Hit/Death。
5. **DB ≠ World Tick** — 持久化與 socket I/O 不得阻塞 Map tick。
6. **Logical asset ≠ pack bundle** — PCK 是發行容器，同素材才合 Atlas，不跨 Enemy/NPC/Player part/Scenery 混圖。
7. **AOI ≠ full-map broadcast** — Server 先依 Interest 過濾可見與必要事件，只為需要更新的實體產生 Delta；不因地圖有大量怪物／掉落物就向每位玩家傳全部。
8. **Wire ≠ JSON** — 不預設 JSON、MessagePack 或自訂二進位，先研究原版／Recovered，採同負載實測選擇低成本、可維護的單一正式協定。

## 功能模組

Server 使用 PascalCase C# namespace，Client 使用 Godot 小寫目錄。相同功能沿用相同名稱，從入口即可找到規則或呈現。

| 功能 | Server | Client |
|---|---|---|
| 地圖 | `Map/` 負責 Blocking、格線與尋路 | `map/` 負責地形、建物與遮擋 |
| NPC | `Npc/` 負責傳點、商店、銀行、倉庫、修理、強化、任務資料 | `quest/` 已有任務面板；其他 NPC 服務 UI 仍由 `session/` 組裝 |
| PvP | `Pvp/` 的地圖政策、狀態與指令序號 | `pvp/` 的名字狀態呈現 |
| 怪物 | `Monster/` 的人口、模擬、協調和戰利品 | `monster/` 的怪物呈現 |
| 戰鬥與技能 | `Combat/` 傷害結算、`Skill/` 技能規則資料 | 目前由 `world/` 接收事件、`player/` 和 `monster/` 呈現 |
| 社交與交易 | `Party/`、`Trade/`、`PlayerShop/` 各自保有流程與狀態 | `trade/`、`player_shop/`、`party/`、`chat/` 面板已獨立；`session/` 負責共用連線與 UI 組裝 |
| 世界與視野 | `World/` 單寫者、生命週期，`Visibility/` AOI | `world/` reducer、相機、畫面組合 |
| 帳號、連線與封包 | `Identity/`、`Gateway/`、`Wire/` | `session/` 登入與會話；`network/` TLS 與封包 |
| 客戶端支援 | Host 組裝服務及載入內容 | `inventory/`、`movement/`、`animation/`、`asset_store/` |

World 擁有權威狀態。Map 提供碰撞與尋路查詢，NPC 提供體系內的互動資料，Visibility 讀取世界結果並交由 Gateway 送出。SQLite 交易保留於 Identity 以維持原子性，功能協調器透過現有交易方法處理提交。Client Session 餵入 WorldState，WorldView 消費快照，角色動畫、地形與資源層透過明確的輸入參數接入。新增功能先確定規則、狀態與輸出的所有者，再在 Host 或 App 組裝；需要替換的行為透過具體輸入／結果或窄介面接入。現有跨功能協調器仍有直接依賴，後續依實際交易或玩法邊界解耦。

## 目錄深度與拆分準則

- 預設用一層功能目錄。檔案少時放在同一層，只有功能內出現多組明確責任、維護者經常需要分開找時，才加子目錄。
- 經常一起修改、共用同一狀態或交易邊界的檔案放在一起。小型 enum、DTO 或輔助方法可以與主要責任同檔。檔案行數是檢查訊號，拆分以責任及測試邊界為準。
- Server／Client 用同一套功能名稱，各自保留實際所需的模組。共用基礎能力放在清楚的所有者底下，跨模組整合由 Host、App 或既有協調器負責。
- 共用流程的程式放在一起，保留檔案責任。例如 Client 的 TLS transport 和封包 codec 同在 `network/`，各自保有獨立檔案。
- 尚無獨立程式的功能集中記錄於下方 TODO。第一個可執行切片需要功能邊界時，再於正式原始碼新增目錄。

## 模組導航與預留位置

以下保留兩端的共同功能名稱。路徑相對於 Server 的 `server/src/SGTest.Server/` 和 Client 的 `client/`。

| 查找什麼 | 現有位置 |
|---|---|
| 地圖、尋路與視野 | Server `Map/`、`Visibility/`；Client `map/`、`world/` |
| 登入、連線及封包 | Server `Identity/`、`Gateway/`、`Wire/`；Client `session/`、`network/` |
| PvP | Server `Pvp/PvpMapPolicy.cs` 和 `Pvp/PvpCrimePolicy.cs`；模式及標記型別隨後者放置。Client `pvp/` 呈現狀態 |
| 玩家交易 | Server `Trade/PlayerTradeCoordinator.cs` 與 `Trade/TradeCommandIngress.cs`；Client `trade/trade_panel.gd` |
| 背包與穿戴 | Server `Identity/SqliteIdentityStore.Equipment.cs`、`Identity/SqliteIdentityStore.InventoryDiscard.cs`；Client `inventory/inventory_panel.gd`、`item_catalog.gd` |
| 資產 | Client `asset_store/` 管理執行期載入；`assets/` 放 Converter 輸出 |

尚未建立或仍待接入的檔案、資料邊界與驗收項目統一列在 [架構 TODO](ARCHITECTURE_TODO.md)。

Client 模組讀取快照並由 Session 送出意圖。Server Gateway 管身分與連線，對應功能模組管理玩法規則。共用交易與鎖順序交由協調器管理，保留現有 SQLite 原子性。

## MVP 範圍：Map15 主城 + Map13 台地，兩張真地圖互通

地圖先行，用唯一 Converter 先驗證兩圖 terrain／scenery／Blocking／NPC／傳點及原始方向動畫；不能因增加第二張圖就先建全圖多套 Loader。Map15 優先驗證登入角色、NPC 商店銀行倉庫修理、背包裝備、玩家交易／商店等主城互動；Map13 驗證 A*、追擊、Server authoritative AI/Combat、多人 AOI、掉落／拾取與正確 Facing/Attack/Hit/Death。實際原版地圖人口與功能必須由來源核對。

**雙圖轉換和 MapEpoch/WorldReady 是 MVP 必須完成的功能**：Map15↔Map13 雙向傳點、AOI 舊實體清除、快照重建、資產載入／卸載、位置檢查、同時換圖／斷線恢復，至少兩個真 Client E2E。資料不足時擴充單一 Converter 並記錄阻礙，不能用偽 Map 代替已完成真圖。

每一張圖上能使用的主要原版系統仍需完整 Server Authority、正常／錯誤／競態測試；好友暫緩。MVP 總 Gate：Map13 同圖 20 玩家／300 Enemy／500 Drop，Map15 多人城市互動，兩圖同時活躍／往返傳點、AFK／多開和 CPU/RSS/VRAM／Network／P95/P99。**MVP 後**再擴其餘地圖／全種類與 100/1000/5000 正式大負載，不把兩張圖視為全部原版覆蓋。

## 多方案比較 Gate

若封包／AOI、AI 排程／A*、Animation/Interpolation、Renderer、Atlas/PCK、Cache、AFK 等有合理的替代設計，**先核對原版／Recovered，依 TEST_PLAN.md 的 ALT001 比較同條件的正確性、資源、網路和尾延遲，選最佳方案後才實作後續依賴。**

優先淘汰不正確、不安全、遺失可靠事件或洩漏資源的候選；性能接近時保留較簡單、低耦合的實作。只整合勝出方案與必要可重跑測試，不保留第二套正式路徑。結果寫回本來就存在的文件和 TODO。

## 最小資料流

~~~text
User input → Client Command Queue → Network I/O → Server Command Queue
                                                ↓
                            authoritative single-writer World Tick
                                                ↓
                            ordered snapshots / reliable events
                                                ↓
Client Network I/O → Decoder → World Reducer → WorldState
                                                  ↓
                       Prediction / Interpolation / Action state
                                                  ↓
                         Visual World (one common Y-sort root)
                                       ↓
                      AssetCatalog / AssetStore / external PCK
~~~

Render FPS 可 0 / 30 / 60 / 90；network/Client Reducer/Server tick 不可因此節流。Server 30Hz、Snapshot 10Hz 為**原型參考 baseline**，新版本須測試後決定。

## 實際目錄與入口

這棵樹列出目前正式 Git 檔案的主要結構。預計新增的檔案及其責任見 [架構 TODO](ARCHITECTURE_TODO.md)。

```text
client/
  project.godot
  app/                      # main.tscn / main.gd，場景組裝與輸入
  network/                  # TLS 收送、封包編解碼
  session/                  # 登入、選角、連線世代、重連、介面組裝
  world/                    # 權威快照 reducer、世界畫面、相機、排序
  map/                      # 地形、建物、遮擋
  player/                   # 角色呈現
  monster/                  # 怪物呈現
  pvp/                      # PvP 名稱標記
  inventory/                # 背包與裝備面板
  bank/                     # NPC 銀行 UI（Server 權威存提款）
  trade/                    # 玩家交易面板
  player_shop/              # 玩家擺攤與買賣面板
  quest/                    # NPC 任務對話與選項面板
  chat/                     # 附近聊天、密語及訊息歷史面板
  party/                    # 組隊邀請、隊長及隊伍聊天面板
  movement/                 # 移動取樣與輸入狀態
  animation/                # 共用 ACT 時序
  asset_store/              # 執行期素材載入與釋放
  assets/                   # Converter 輸出
  tests/                    # Godot 定向測試與跨程序驗收
server/
  host/SGTest.Server.Host/  # Program.cs，組裝與啟動 .NET Host
  src/SGTest.Server/
    Gateway/                # 登入及指令路由、連線限制
    Wire/                   # WireContracts + feature-partial 編解碼
    World/                  # 單寫者狀態與 Tick
    Map/                    # Blocking 與尋路
    Visibility/             # AOI
    Identity/               # 帳號、角色與 SQLite 交易
    Npc/                    # NPC 可互動資料與服務規則
    Monster/                # 怪物模擬與戰利品協調
    Combat/                 # 傷害結算
    Skill/                  # 技能規則
    Pvp/                    # 玩家對戰規則
    Party/                  # 隊伍狀態與操作
    Trade/                  # 玩家雙方交易
    PlayerShop/             # 玩家擺攤
  tests/                    # Server Checks、Godot TLS LoginFixture
tools/
  asset_converter/         # cli.py；source/ atlas/ content/；runtime_pack.py
  tests/                   # Converter 契約與獨立比較
docs/                       # 架構、規格與測試；research/ 為歷史證據
SPEC.md                     # 核心規格與技術契約
todo.txt                    # 任務順序、完成狀態、驗證證據
.github/workflows/          # CI 驗證
```

Player、Monster、Trade 等功能依自己的狀態與流程分組。Client 和 Server 共用協定、素材資料契約與測試向量，各自有可執行程式。跨端驗收從 Host、Godot 與 Converter 的現有入口開始。

## 交互關注的責任及不可妥協條件

- 高聚力低耦合：每個模組只有一個主要變動原因。網路不能呼叫渲染；渲染不能推進 World tick；資產載入完成不能重新排序權威事件。
- 同機多開：每個 Client 都有自己的連線和狀態；**同一個 Client 實例內**素材去重，不跨程序共享易出錯的可變 Texture／World state。量測單開、2 開、4 開、8 開的 RSS、CPU、GPU、queue age、操作延遲。
- FPS：UI/World 渲染可設定 0/30/60/90，伺服器 Tick 與 Client command/reducer/network worker 依獨立時鐘運作。動畫播放固定原版 ACT 時間，不因移速／FPS 改速。
- AFK 不是停網路：允許卸載大部分 GPU Texture/Node、停止遊戲內容繪製，保留必要網路收送、狀態 reducer、可靠事件、重連及回復全量快照；顯示器最小化可直接進 Render 0。
- 對 A*、長按移動、Blocking 及原版互動邏輯，先對照 Recovered/Old 反編譯或既有行為測例，**移植行為而非盲目複製已不相容的代碼**。
