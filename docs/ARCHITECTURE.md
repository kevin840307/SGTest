# SGTest — 新版架構總覽

**狀態：設計已決策；Client 與 Server 皆尚未重新實作。** 版本：2026-11-15。

使用者決定移除舊版可執行原型，保留素材轉換程式和歷史研究，從協定、世界狀態與渲染責任重新開發。任何舊測試 PASS 不等於新版本 PASS。

## 正式文件（唯一入口）

| 主題 | 文件 | 用途 |
|---|---|---|
| Client | [CLIENT_ARCHITECTURE](CLIENT_ARCHITECTURE.md) | 運行時資料流、World Y-sort、FPS 隔離 |
| Server | [SERVER_ARCHITECTURE](SERVER_ARCHITECTURE.md) | 權威狀態、Tick、AOI、DB |
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

## 目前儲存庫

- tools/asset_converter：唯一正式素材 Converter；保留轉換／研究／格式測試。
- tools/tests：只保留正式 Converter 的契約與格式驗證；舊 Viewer、第二套建置器與已淘汰 Benchmark 不在新版本維護。
- client/：只有 README、assets 輸出定位標記；**無 Godot 專案**。
- server/：只有 README；**無 C# 專案**。
- docs/：上述設計文件；docs/research 為歷史研究原文彙編。
- SPEC.md：當期核心規格與技術契約；舊 SPEC 全文僅保留於 research 歷史彙編。
- todo.txt：**新版唯一進度入口**。

請按照 todo.txt 逐項實作。不要把 Git 歷史中的舊 main.gd 或舊 Server World 複製回來作新版骨架。
## 建議的最小目錄配置（功能先於分類）

以下是**逐步建立時的目標目錄**，不是宣稱目前已存在：

```text
client/
  project.godot
  scenes/                     # 啟動畫面、World、UI，只有真的需要才新增
  scripts/
    app/                      # 極薄啟動與生命週期組裝
    network/                  # Transport、Codec、bounded message queues
    world/                    # State、Reducer、prediction、navigation、AOI
    rendering/                # World Y-sort、動畫、插值與視覺快取
    assets/                   # 唯一 AssetCatalog / AssetStore
    ui/                       # 控制項／Theme／ViewModel
  assets/                     # Converter 輸出；各原素材各自獨立
server/
  src/
    Network/                  # Connection、Wire、序號／封包驗證
    World/                    # Map authoritative tick、AOI、Blocking、A*
    Gameplay/                 # Combat、Monster AI、Drops、Items（需要時拆）
    Accounts/                 # 登入／角色
    Persistence/              # SQLite／交易
  tests/
  content/                    # Converter 產出；不包含貼圖
tools/
  asset_converter/            # 唯一正式轉換器（source/atlas/content）
  tests/                      # 與轉換器相關的研究／合約驗證
docs/                         # 少量正式設計；research 為歷史證據彙編
```

不事先建立空的 Manager、Repository、Service、Engine 或不同類型專用 Cache；
未來若設計真正需要再拆。Client 和 Server 共用**規格／資料格式／測試向量**，
不共用彼此 executable code。Windows／macOS 使用相同 logical asset identity；
Texture import／發行包可有平台差異。

## 交互關注的責任及不可妥協條件

- 高聚力低耦合：每個模組只有一個主要變動原因。網路不能呼叫渲染；渲染不能推進 World tick；資產載入完成不能重新排序權威事件。
- 同機多開：每個 Client 都有自己的連線和狀態；**同一個 Client 實例內**素材去重，不跨程序共享易出錯的可變 Texture／World state。量測單開、2 開、4 開、8 開的 RSS、CPU、GPU、queue age、操作延遲。
- FPS：UI/World 渲染可設定 0/30/60/90，伺服器 Tick 與 Client command/reducer/network worker 依獨立時鐘運作。動畫播放固定原版 ACT 時間，不因移速／FPS 改速。
- AFK 不是停網路：允許卸載大部分 GPU Texture/Node、停止遊戲內容繪製，保留必要網路收送、狀態 reducer、可靠事件、重連及回復全量快照；顯示器最小化可直接進 Render 0。
- 對 A*、長按移動、Blocking 及原版互動邏輯，先對照 Recovered/Old 反編譯或既有行為測例，**移植行為而非盲目複製已不相容的代碼**。
