# SGTest Server Architecture

狀態：**CANONICAL / IMPLEMENTED STRUCTURE**
日期：2026-11-15

Server 是單一 .NET 8 modular monolith。目錄按功能分類，不使用 Application / Domain / Infrastructure / Transport 四層作主要架構。

## 1. 原則

- Server authority：位置、碰撞、戰鬥、掉落、物品與交易最終都由 Server 決定。
- World simulation 固定時間步，與 Client FPS 無關。
- Network connection 不持 gameplay rule。
- DB I/O 不阻塞 World tick。
- 同一 World state 優先 single writer。
- 不建立沒有實作內容的空 Combat/Items/Social 模組。
- 不使用 Common/Utils/Manager/Service 雜物層。

## 2. 目前實際 Tree

```text
server/
├── SGTest.Server.csproj
├── Program.cs
├── Accounts/
│   └── AccountService.cs
├── Networking/
│   ├── ClientConnection.cs
│   ├── SnapshotCodec.cs
│   └── Wire.cs
├── Persistence/
│   └── SqliteAccountStore.cs
├── World/
│   ├── CollisionGrid.cs
│   ├── Population.cs
│   ├── World.cs
│   └── WorldHost.cs
└── tests/
    ├── SGTest.Server.Tests.csproj
    └── Program.cs
```

未來只有 Combat / Items / Social / Content / Diagnostics 真正出現獨立責任時才建立對應目錄。

## 3. Accounts

`AccountService.cs` 目前包含：

- account/character identity contracts。
- authentication rules。
- memory account store fixture。

持久化實作不放這裡。

## 4. Persistence

`SqliteAccountStore.cs`：

- SQLite schema/migration。
- account/character durable data。
- repository implementation。

Persistence 不反向推進 World tick。

## 5. Networking

### ClientConnection

只代表網路連線與 transport state：

- Connection Id。
- WebSocket。
- outbound bounded queue。
- account/character connection context。
- AOI seen cache（目前仍在 connection，未來有必要才抽 VisibilityState）。
- `ConnectionEpoch`。

舊 `Session` 類別名稱已移除。

### Wire

bounded MessagePack contract，不做 gameplay calculation。

### SnapshotCodec

只編碼 snapshot rows。

## 6. World

### WorldHost

World actor / scheduler：

- 固定 30Hz。
- command queue。
- authentication result dispatch。
- AOI / replication。
- snapshot/ground publish。

### World

目前權威 simulation：

- Player / Monster state。
- movement。
- AI。
- combat。
- drops / pickup。
- respawn。

未來 Combat/Items 複雜度真的增加時再把純規則搬到 `Combat/`、`Items/`；不要為了形式先拆。

### CollisionGrid

直接讀原版 `blocking.dat` compact byte grid；不依賴 Godot CollisionShape。

### Population

原版 Server population contract。

## 7. Dependency Direction

```text
Program
  ├── Networking
  ├── Accounts
  ├── Persistence
  └── World

Networking → command/DTO → World / Accounts
Accounts   → persistence abstraction
World      → compact content/collision state
```

禁止：

- Persistence 驅動 World Tick。
- Networking 計算 damage。
- Client Render FPS 決定 Server simulation。
- Server 讀 Client atlas/PNG。

## 8. ConnectionEpoch

不再使用模糊 `SessionEpoch`。

`ConnectionEpoch` 用來區分一次有效登入/連線世代；舊連線的訊息不能取得新世代權限。這是 wire 中既有欄位語意的命名整理，欄位位置沒有因此改變。

未來跨地圖另使用：

- `MapEpoch`
- `EntityRevision`
- `MovementSequence`
- `ActionSequence`

不要用一個 Session id 同時代表所有世代。

## 9. Content / Assets

Server 不載入 PNG/Atlas。

Server map content 只需要：

```text
server/content/maps/<id>/
├── world.json
├── population.json
└── blocking.dat
```

Monster visual id 由 population definition 傳給 Client；真正 visual asset 位於 Client `monsters/<id>/`。

## 10. 新功能歸類

- 登入/角色身份 → Accounts。
- socket / codec / connection → Networking。
- SQLite/DB → Persistence。
- map/player/monster/visibility/simulation → World。
- 戰鬥規則大到可獨立測試與維護後 → Combat。
- inventory/equipment/shop 等大到獨立責任後 → Items。
- trade/chat/player shop → Social。
- metrics/trace → Diagnostics。

一個小檔案不值得獨立架構層。
