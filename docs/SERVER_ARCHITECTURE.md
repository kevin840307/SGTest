# SGTest Server Architecture

狀態：**TARGET ARCHITECTURE / ACCEPTED**
日期：2026-11-15

> Server 採 **單一 .NET 8 專案 + 功能導向模組化單體**。
> 不為了 Clean Architecture 形式先拆成多個 csproj，也不使用 Application / Domain / Infrastructure / Transport 四層當主要目錄。
> 目前 repo 尚未全部搬移完成；新功能應優先遵守本文件。

---

## 1. 核心原則

- Server 永遠權威：位置、碰撞、傷害、掉落、物品、貨幣、交易。
- gameplay state 在 RAM；durable 資產再持久化。
- 固定 World simulation 與 Client FPS 完全無關。
- Network connection 不持有 gameplay world rules。
- 一張地圖的世界狀態以 single-writer World runtime 為主，避免 session lock / DB lock 卡住整圖。
- 先維持一個 `SGTest.Server.csproj`；真的有部署／編譯邊界需要才拆 project。

---

## 2. 目標目錄

```text
server/
├── SGTest.Server.csproj
├── Program.cs                         # composition root
│
├── Configuration/
│   └── ServerOptions.cs
│
├── Networking/
│   ├── ClientConnection.cs
│   ├── ConnectionRegistry.cs
│   ├── Protocol.cs
│   ├── MessageCodec.cs
│   └── SnapshotCodec.cs
│
├── Accounts/
│   ├── Account.cs
│   ├── Character.cs
│   ├── Authentication.cs
│   └── AccountRepository.cs
│
├── World/
│   ├── WorldHost.cs
│   ├── MapWorld.cs
│   ├── PlayerState.cs
│   ├── MonsterState.cs
│   ├── GroundItemState.cs
│   ├── VisibilityState.cs
│   ├── SpatialIndex.cs
│   ├── CollisionGrid.cs
│   ├── MovementSystem.cs
│   └── Population.cs
│
├── Combat/
│   ├── CombatSystem.cs
│   ├── DamageCalculator.cs
│   ├── SkillSystem.cs
│   └── StatusEffects.cs
│
├── Items/
│   ├── Inventory.cs
│   ├── Equipment.cs
│   ├── ItemDefinition.cs
│   ├── ItemInstance.cs
│   ├── DropSystem.cs
│   ├── ShopSystem.cs
│   ├── Warehouse.cs
│   └── Bank.cs
│
├── Social/
│   ├── Trade.cs
│   ├── Chat.cs
│   └── PlayerShop.cs
│
├── Persistence/
│   ├── SqliteDatabase.cs
│   ├── AccountRepository.cs
│   ├── CharacterRepository.cs
│   └── migrations/
│
├── Content/
│   ├── ContentCatalog.cs
│   └── maps/
│       └── <map_id>/
│           ├── world.json
│           ├── population.json
│           └── blocking.bin
│
├── Diagnostics/
│   ├── Metrics.cs
│   └── RuntimeTrace.cs
│
└── tests/
    └── SGTest.Server.Tests.csproj
```

### 目錄建立規則

- 只有實際功能存在才建立模組。
- 不為未實作功能預先產生 20 個空類別。
- 若一個模組只有一個小檔案，先放在最接近的既有模組。
- 不用 `Services/`、`Managers/`、`Helpers/` 當雜物桶。

---

## 3. 不再使用模糊的 Session 命名

目前 `Transport/Session.cs` 同時有：

- WebSocket
- Outbound queue
- AccountId
- CharacterId
- AuthPending / Ready
- AOI Seen / SeenDrops

這是不同責任混在一起。

目標拆分：

### ClientConnection

只代表網路連線：

```text
ConnectionId
WebSocket
OutboundQueue
Send()
Disconnect()
WriteLoop()
ConnectionEpoch
```

放在：

```text
Networking/ClientConnection.cs
```

### PlayerContext / PlayerState

登入後角色身份與世界狀態不應塞在 Socket。

- Account / Character 身份：Accounts。
- Position / HP / current map：World/PlayerState。
- Seen entities / AOI：World/VisibilityState。

不再用通用的 `Session` 當所有東西的容器。

### Epoch Naming

```text
ConnectionEpoch
MapEpoch
EntityRevision
MovementSequence
ActionSequence
```

比 `SessionEpoch` 更直觀。

---

## 4. Server 模組依賴

```text
Networking
    |
    v
command/message DTO
    |
    v
World / Accounts / Combat / Items / Social
    |
    v
Persistence + Content
```

更精確地說：

- Networking 可以驗證封包格式與 connection state。
- Networking 不直接計算 damage / item transaction。
- World 可以調用 Combat / Items 的純規則。
- Persistence 不反向驅動 World Tick。
- Content 是唯讀定義資料，不持 gameplay state。
- Diagnostics 可以被所有層寫 metrics，但不得改 gameplay 行為。

---

## 5. World Runtime

每個 map/channel 是一個權威世界擁有者：

```text
MapWorld
├── Players
├── Monsters
├── GroundItems
├── Movement
├── Combat
├── SpatialIndex
├── Visibility
└── Replication
```

原則：

- 同一 World state 由 single writer 更新。
- Network worker 投遞 command，不持 world lock。
- DB I/O 不在 world tick 熱路徑同步等待。
- Monster AI 可低頻決策，但 movement/time advance 仍由 Server time。
- 靜止 entity 不重複送 position。
- Player / combat / reliable actions 優先。

---

## 6. Server Tick 與 Client Render 完全分離

Server：

```text
SimulationClock = fixed
Network send/receive = independent I/O
Client render FPS = irrelevant
```

Client 30 / 60 / 90FPS、Unfocus、Minimize、AFK，都不能改：

- Server simulation tick。
- combat resolution。
- drop expiration。
- pickup / trade。
- player movement authority。
- connection heartbeat semantics。

---

## 7. AFK / Low Power Replication

AFK 不等於「降低 network FPS」。

Client 可以發送 presentation/replication preference，例如：

```text
PresentationMode = Active | Background | Afk
```

但 Server 只可對**可重建的非權威視覺資料**做降量：

可降低／合併：

- 遠距 decoration movement。
- 可由最新 snapshot 重建的 transient visual state。
- 同 entity 連續 movement delta，可在不跨 barrier 的情況 coalesce。

不可降低或丟失：

- Heartbeat / disconnect detection。
- Authentication / reconnect。
- HP / MP / status。
- Combat resolved。
- Death / respawn。
- Item / money / inventory。
- Pickup。
- Trade / shop / bank / warehouse。
- Teleport / map transfer。
- Stop / correction / reliable action barrier。

恢復 Active 時：

1. Server 提供目前 authoritative snapshot。
2. Client 用最新 state 重建 rendering。
3. 不需要補傳 AFK 期間所有視覺 frame。

這樣可以降低 AFK 流量與 Client GPU 成本，但**不降低 transport/reducer 正確性**。

---

## 8. Gameplay 模組如何拆

### Accounts

只處理：

- 登入。
- 帳號。
- 角色清單。
- 建角。
- 選角。

### World

只處理：

- map lifecycle。
- entity state。
- movement。
- collision。
- AOI。
- spawn/despawn。

### Combat

只處理：

- attack validation。
- damage / miss。
- skill。
- death。
- status effect。

### Items

只處理：

- inventory。
- equipment。
- item UID。
- drop。
- shop。
- warehouse。
- bank。

### Social

等真的需要才建立：

- trade。
- chat。
- player shop。

不要因為原版有功能就先建立空模組。

---

## 9. Persistence

Persistence 是技術細節，不是 gameplay owner。

```text
Persistence/
├── SqliteDatabase.cs
├── ...Repository.cs
└── migrations/
```

規則：

- Repository API 使用 domain/gameplay value。
- DB schema object 不直接流進 network DTO。
- World movement 不每步寫 DB。
- 登出、換圖、關鍵資產交易要有明確 flush/transaction。
- 經濟交易需 idempotency / request id，不能發成功後 DB 失敗。

---

## 10. Content

Server Content 是資料，不是 Client 圖片。

```text
Content/maps/<map_id>/
├── world.json
├── population.json
└── blocking.bin
```

未來可再加：

```text
Content/
├── items/
├── skills/
├── monsters/
├── npcs/
└── maps/
```

但僅在 Server gameplay 真正消費時建立。

Client 的 PNG/Atlas 不放 server。

---

## 11. 命名規則

C#：

- folder：`PascalCase`
- file / class：`PascalCase.cs`
- interface：有必要才使用 `IName`
- methods / properties：標準 .NET PascalCase
- private fields：`_camelCase`

推薦：

- `ClientConnection`
- `MapWorld`
- `PlayerState`
- `VisibilityState`
- `CombatSystem`
- `ItemDefinition`

避免：

- `Session`
- `Manager`
- `Helper`
- `Utils`
- `BaseService`
- `CommonService`
- `Processor`

除非語意真的只有該名稱最正確。

---

## 12. 目前 Repo 對照

目前：

```text
Application/WorldHost.cs
Domain/World.cs
Domain/Identity.cs
Domain/CollisionGrid.cs
Domain/Population.cs
Infrastructure/SqliteAccountStore.cs
Transport/Session.cs
Transport/Wire.cs
Transport/SnapshotCodec.cs
```

目標搬移大致：

```text
Application/WorldHost.cs          -> World/WorldHost.cs
Domain/World.cs                   -> World/MapWorld.cs
Domain/CollisionGrid.cs           -> World/CollisionGrid.cs
Domain/Population.cs              -> World/Population.cs
Domain/Identity.cs                -> Accounts + World 對應型別
Infrastructure/SqliteAccountStore -> Persistence/
Transport/Session.cs              -> Networking/ClientConnection + World/VisibilityState
Transport/Wire.cs                 -> Networking/Protocol.cs / MessageCodec.cs
Transport/SnapshotCodec.cs        -> Networking/SnapshotCodec.cs
```

搬移時：

- 先 rename/move，保持行為。
- 再拆責任。
- 不要一次把 namespace、協定、資料庫、遊戲規則一起改。

---

## 13. 不拆多 csproj 的門檻

目前一個 Server project 最合理。

只有以下條件出現才考慮拆：

- 可獨立部署。
- 可獨立版本化。
- 明確需要不同依賴集。
- 編譯或測試隔離有可量測收益。

不要單純為了「Clean Architecture 看起來漂亮」拆成：

```text
Domain.dll
Application.dll
Infrastructure.dll
Host.dll
```

這會增加維護成本，對目前 SGTest 沒有必要。

---

## 14. 其他 Session 開發前必讀

Server 任務開始前至少閱讀：

1. 本文件。
2. `ARCHITECTURE.md`
3. `NETWORK_AND_MOTION.md`
4. `DATA_MODEL.md`
5. `RUNTIME_PROTOCOL.md`
6. `SECURITY_OPERATIONS.md`

修改 Protocol / DB schema / authority 邊界時必須更新 ADR。
