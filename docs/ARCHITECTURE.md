# SGTest Architecture

狀態：**CANONICAL ARCHITECTURE INDEX / ACCEPTED**
日期：2026-11-15

本文件是 SGTest 架構入口。詳細目標結構分開維護：

- [Client Architecture](CLIENT_ARCHITECTURE.md)
- [Server Architecture](SERVER_ARCHITECTURE.md)
- [Client Runtime Isolation](CLIENT_RUNTIME_ISOLATION.md)
- [Network and Motion](NETWORK_AND_MOTION.md)
- [Asset Architecture](ASSET_ARCHITECTURE.md)
- [Asset Packing Benchmark](ASSET_PACKING_BENCHMARK.md)
- [Asset Runtime Strategy](ASSET_RUNTIME_STRATEGY.md)
- [Asset Converter](ASSET_CONVERTER.md)

> Client / Server 主程式已在 2026-11-15 搬到本文件定義的功能導向結構；之後新增功能不得再建立舊 Application/Domain/Transport 或平鋪 scripts 根目錄。

---

## 1. 架構風格

SGTest 採 **功能導向的模組化單體**：

- Client：Godot 單一專案。
- Server：單一 .NET 8 project。
- Asset Converter：獨立離線工具。
- 不為架構形式建立多個空 project / loader / manager。
- 以明確責任邊界達成低耦合、高聚力。

---

## 2. Repository 目標

```text
SGTest/
├── client/                     # Godot runtime
├── server/                     # .NET authoritative server
├── tools/
│   └── asset_converter/        # 原版素材離線轉換
├── docs/
├── reports/
├── SPEC.md
├── todo.txt
└── AGENTS.md
```

原版、Recovered、Old 都是唯讀研究來源，不搬進 production Runtime tree。

---

## 3. 五個硬性邊界

### Authority

Server 決定：

- position validity
- collision
- combat
- HP
- items
- money
- pickup
- trade
- map transition

Client prediction 只能改善畫面。

### Network != Render

Network I/O / decode / reducer 不依賴 Render FPS。

### Logic != Render

WorldState 先更新，Renderer 只是讀目前 state。

### Asset != State

Texture 尚未載入不能阻止 Spawn / Hit / Death / Pickup state 生效。

### Persistence != World Tick

DB 不可放進 movement / monster tick 熱路徑。

---

## 4. 時鐘與低功耗

```text
Server Simulation Clock     fixed
Client Network I/O          independent
Client State/Logic          fixed/queue-driven
Client Render               configurable
UI animation                presentation only
```

支援：

- Render 30/45/60/75/90FPS。
- Low-power visual 5/10/15FPS。
- Minimized / AFK world render 0FPS。
- Network / state reducer / authoritative results 不因上述模式降低。

AFK 可降低**可重建的 visual replication payload**，但 transport heartbeat、reliable gameplay events 和 protocol processing 不降頻。

---

## 5. 資產原則

原版來源實際包含大量：

- Player paper-doll / weapons / clothing / hair
- Enemy / NPC / Pet
- MA / MS / SR
- Item icon / photo / ground
- Effects
- Map / scenery
- Interface / UI
- Audio
- ACT / SIF / DrawOrder / data

Converter 可以保留原始分類。

Godot Runtime 不照抄原版目錄，而以內容語意直接分類：

```text
maps/
monsters/
npcs/
players/
equipment/
pets/
items/
effects/
ui/
audio/
data/
```

每個來源素材保持獨立 logical/physical identity；PCK 只是磁碟容器，不代表共同載入。Map 只放 terrain/layout/blocking 與 scenery reference，Monster appearance 永遠放在 `monsters/` 並由 Server definition 決定。

Asset lifetime 是 metadata：

```text
app / character / map / on_demand
```

不是資料夾或多套 loader。

正式發行的遊戲素材使用**平台共用 external PCK**，不塞進主程式 PCK：

```text
Application package
├── executable / .app
├── main PCK
└── assets/
    └── assets.pck
```

Windows 與 macOS 使用相同 `assets.pck` 內容與 `res://assets/**` logical path；只有啟動時的實體路徑解析不同。Client startup 先掛載 external pack，再由單一 AssetStore 按需 decode / GPU commit。

---

## 6. 命名方向

避免模糊名稱：

- Session
- Manager
- Helper
- Utils
- Common
- Core
- BaseService

優先使用實際責任：

- ClientConnection
- WorldState
- VisibilityState
- AssetStore
- MapWorld
- CombatSystem
- MonsterRenderer

---

## 7. 開發規則

1. 先確認該功能屬於哪個模組。
2. 不跨層偷改 state。
3. 不為單一素材種類新增 Runtime loader/cache。
4. 新資料夾需有實際責任與至少一組實作，不建立空架構。
5. 搬移檔案與改行為分開 commit。
6. Protocol、authority、DB schema、資產 ownership 改變需更新 ADR。
7. Client / Server 任務開始前分別閱讀對應 Architecture 文件。

---

## 8. 現況

Client 主程式目前已實際分到：

```text
app / assets / diagnostics / gameplay / networking / rendering / ui / world
```

Server 主程式目前已實際分到：

```text
Accounts / Networking / Persistence / World
```

Combat / Items / Social / Content / Diagnostics 等 Server 模組只在真正有獨立實作時建立，不先做空架構。

ClientConnection 與 ConnectionEpoch 已取代模糊的 Session 命名。詳細實際檔案 Tree 見 Client / Server Architecture。
