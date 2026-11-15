# SGTest — 新版架構總覽

**狀態：設計已決策；Client 與 Server 皆尚未重新實作。** 版本：2026-11-15。

使用者決定移除舊版可執行原型，保留素材轉換程式和歷史研究，從協定、世界狀態與渲染責任重新開發。任何舊測試 PASS 不等於新版本 PASS。

## 正式文件（唯一入口）

| 主題 | 文件 | 用途 |
|---|---|---|
| 重建決策與舊版缺點 | [REBUILD_ARCHITECTURE_REVIEW](REBUILD_ARCHITECTURE_REVIEW.md) | 取捨、過度設計避免事項 |
| Client | [CLIENT_ARCHITECTURE](CLIENT_ARCHITECTURE.md) | 運行時資料流、World Y-sort、FPS 隔離 |
| Server | [SERVER_ARCHITECTURE](SERVER_ARCHITECTURE.md) | 權威狀態、Tick、AOI、DB |
| 素材 | [ASSET_ARCHITECTURE](ASSET_ARCHITECTURE.md) | 一素材一 Atlas／AssetStore 契約 |
| 轉換與發行 | [ASSET_CONVERTER](ASSET_CONVERTER.md) | SPR/ACT/SIF、轉換器與獨立 PCK |
| Wire | [RUNTIME_PROTOCOL](RUNTIME_PROTOCOL.md) | Epoch/序號／事件與資料合約 |
| 遊戲功能 | [GAMEPLAY](GAMEPLAY.md) | 核心功能與階段 |
| 測試／效能 | [TEST_PLAN](TEST_PLAN.md)、[PERFORMANCE](PERFORMANCE.md) | 測試 Gate 與 Model1 公平比較 |
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
- tools：必要的素材研究、契約與驗證工具；不是 Runtime。
- client/：只有 README、assets 輸出定位標記；**無 Godot 專案**。
- server/：只有 README；**無 C# 專案**。
- docs/：上述設計文件；docs/research 為歷史研究原文彙編。
- SPEC.md：功能來源，包含舊階段狀態，不應直接當新版進度。
- todo.txt：**新版唯一進度入口**。

請按照 todo.txt 逐項實作。不要把 Git 歷史中的舊 main.gd 或舊 Server World 複製回來作新版骨架。