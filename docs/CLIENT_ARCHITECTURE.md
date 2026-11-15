# SGTest Client Architecture

狀態：**TARGET ARCHITECTURE / ACCEPTED**
日期：2026-11-15

> 本文件定義 Client 的目標結構、命名與模組邊界。
> 目前 repo 尚未全部搬移到此目錄，看到本文件 **不代表所有檔案已完成重構**。
> 新功能應優先遵守本文件；搬移舊檔時不得改變遊戲行為，並保留 Godot UID / res:// 引用正確性。

---

## 1. 設計原則

Client 採 **功能導向的模組化單體（modular monolith）**：

- 低耦合：Networking、World、Rendering、Assets、UI 不互相跨層操作。
- 高聚力：同一功能的狀態、演算法、渲染集中在同一模組。
- 不建立一堆 Manager / Service / Helper / Utils。
- 不因 Enemy / Player / NPC / Pet / Item 各建立一套 Loader/Cache。
- AssetStore 只有一套；不同素材差異放在 definition / metadata，不新增 Runtime 架構。
- Server authority 與 Client presentation 分開。
- Render FPS、UI FPS、Network、Logic 四者不可綁在同一個更新頻率。

---

## 2. 目標目錄

```text
client/
├── project.godot
├── export_presets.cfg
│
├── scenes/
│   ├── app/
│   │   └── main.tscn
│   ├── world/
│   │   └── world.tscn                  # 必要時才建立；不要一 entity 一 scene
│   └── ui/
│       ├── login/
│       ├── character_select/
│       └── hud/
│
├── scripts/
│   ├── app/
│   │   ├── main.gd                     # composition root，只組裝，不塞玩法
│   │   └── app_state.gd
│   │
│   ├── networking/
│   │   ├── network_client.gd           # WebSocket worker / bounded queues
│   │   ├── protocol.gd                 # opcode / message contract
│   │   ├── message_codec.gd
│   │   ├── snapshot_codec.gd
│   │   └── server_clock.gd
│   │
│   ├── world/
│   │   ├── world_state.gd              # authoritative/replicated compact state
│   │   ├── world_reducer.gd            # ordered event reducer
│   │   ├── client_prediction.gd
│   │   ├── movement.gd
│   │   ├── collision_grid.gd
│   │   ├── spatial_index.gd
│   │   ├── visibility.gd
│   │   └── action_policy.gd
│   │
│   ├── gameplay/
│   │   ├── combat.gd                   # 只放 Client presentation/input side
│   │   ├── interaction.gd
│   │   └── input_commands.gd
│   │
│   ├── rendering/
│   │   ├── player_renderer.gd
│   │   ├── monster_renderer.gd
│   │   ├── npc_renderer.gd
│   │   ├── pet_renderer.gd
│   │   ├── mecha_renderer.gd
│   │   ├── item_drop_renderer.gd
│   │   ├── map_renderer.gd
│   │   ├── effect_renderer.gd
│   │   └── frame_profiler.gd
│   │
│   ├── assets/
│   │   ├── asset_store.gd              # 唯一 runtime asset cache/store
│   │   ├── asset_catalog.gd
│   │   ├── atlas.gd
│   │   ├── animation_definition.gd
│   │   └── legacy_facing.gd
│   │
│   ├── ui/
│   │   ├── login_ui.gd
│   │   ├── character_select_ui.gd
│   │   ├── hud.gd
│   │   ├── inventory_ui.gd
│   │   ├── equipment_ui.gd
│   │   ├── shop_ui.gd
│   │   ├── warehouse_ui.gd
│   │   ├── bank_ui.gd
│   │   ├── chat_ui.gd
│   │   └── settings_ui.gd
│   │
│   └── diagnostics/
│       ├── runtime_trace.gd
│       └── memory_probe.gd
│
├── assets/
│   ├── catalog/
│   │   ├── assets.json                 # logical asset id -> file/page/metadata
│   │   └── animations.json
│   │
│   ├── characters/
│   │   ├── players/
│   │   │   ├── body/
│   │   │   ├── hair/
│   │   │   └── equipment/
│   │   ├── monsters/
│   │   ├── npcs/
│   │   ├── pets/
│   │   └── mecha/
│   │
│   ├── items/
│   │   ├── icons/
│   │   ├── photos/
│   │   └── ground/
│   │
│   ├── effects/
│   │   ├── skills/
│   │   ├── weapons/
│   │   ├── combat/
│   │   └── environment/
│   │
│   ├── maps/
│   │   └── <map_id>/
│   │       ├── terrain/
│   │       ├── scenery/
│   │       ├── objects/
│   │       └── metadata/
│   │
│   ├── ui/
│   │   ├── common/
│   │   ├── windows/
│   │   ├── cursors/
│   │   └── icons/
│   │
│   ├── audio/
│   │   ├── bgm/
│   │   └── sfx/
│   │
│   └── data/
│       ├── quests/
│       ├── skills/
│       ├── items/
│       └── tables/
│
├── shaders/
├── tests/
└── tools/                              # Godot client 專用工具，不放原版轉換器
```

### 目錄建立規則

上面是完整目標分類，**不是要求立即建立所有空目錄**。

- 有實際檔案才建立資料夾。
- 少於 2～3 個高度相關檔案時，不要為了分類而多包一層。
- 禁止建立 `common/`、`utils/`、`helpers/` 當雜物桶。
- `core/` 不使用；名稱太抽象，應直接使用 networking/world/assets 等實際責任。

---

## 3. 原版 / Recovered 資產與 Runtime 資產要分開

原版與 Recovered 的分類包含：

- Player：body / hair / cloth / weapon / paper-doll layers
- Enemy
- NPC
- Pet
- MA / MS / SR
- Item：Icon / Photo / Ground
- Effect：skill / weapon / hit / environmental
- Map / scenery / terrain / map object
- Interface / UI
- Audio：BGM / SE
- Quest / data tables
- ACT / SIF / DrawOrder

這些分類對**轉換器**很有價值，但不應原樣變成 Godot Runtime 架構。

### Converter Source Catalog

`tools/asset_converter/` 可以保留來源分類與原始 key：

```text
source catalog
├── Player/
├── Enemy/
├── NPC/
├── Pet/
├── MA/
├── MS/
├── SR/
├── Item/
├── Effect/
├── Map/
├── Interface/
└── Audio/
```

若 MA / MS / SR 的正式遊戲語意尚未完全驗證：

- metadata 保留原代號。
- 不自行發明錯誤名稱。
- 語意確認後再映射到 Runtime catalog。

### Runtime Asset Catalog

Runtime 只使用較穩定的語意分類：

```text
characters / items / effects / maps / ui / audio / data
```

Runtime 不需要知道來源是 SPR、Recovered PNG、MA、MS 還是 SR；它只依 logical asset id 取 definition。

---

## 4. 資產生命週期不放在資料夾名稱

不使用：

```text
assets/bundles/session/
assets/bundles/on_demand/
```

因為 `session` 不直觀，也把「內容類型」與「存活時間」混在一起。

改成 metadata ownership：

| ownership | 意義 |
|---|---|
| `app` | 啟動到關閉，UI 共用素材等 |
| `character` | 選角後到登出，玩家 base / 常用紙娃娃 |
| `map` | 目前地圖內怪物、NPC、scenery |
| `on_demand` | 低頻裝備、大技能、特殊 UI 等 |

Ownership 是 AssetStore 的 eviction/refcount 規則，**不是多套 Loader**。

---

## 5. Client 模組依賴方向

```text
Input / UI
    |
    v
input_commands
    |
    v
Networking <------> Server
    |
    v
World Reducer
    |
    v
World State
   /   \
  v     v
UI VM  View Projection
          |
          v
      Rendering
          |
          v
        Assets
```

允許：

- Rendering -> Assets
- UI -> read-only ViewModel
- World -> protocol DTO / value types
- Input -> typed command gateway

禁止：

- Renderer 直接讀 socket。
- UI 直接修改 authoritative WorldState。
- AssetStore 處理網路事件。
- NetworkClient 直接播放動畫。
- AnimationPlayer 決定移動速度 / 命中 / 傷害。
- `main.gd` 成為所有功能入口的巨大 God Object。

---

## 6. Render / Logic / Network / AFK 四條排程

### Network

`NetworkClient` 的 WebSocket poll / receive / send：

- 獨立 worker / bounded queue。
- 不受 Render FPS 影響。
- 不受 UI FPS 影響。
- AFK / unfocus / minimized 都不能停止。

### Logic / World State

固定邏輯：

- 目前正式基準約 30Hz。
- consume network queue、ordered reducer、prediction、command flush。
- 不能由 `_process()` 視覺刷新率驅動。
- render 5FPS / 15FPS / 30FPS / 60FPS 時結果必須一致。

### Rendering

可調：

- 30 / 45 / 60 / 75 / 90FPS。
- low-power visual 可以 5 / 10 / 15FPS。
- minimized / AFK 可為 0FPS。
- 只影響畫面取樣和 GPU commit，不改 world state。

### AFK / Low Power

AFK 的正確模式：

```text
Network I/O          ON
Protocol decode      ON
World reducer        ON
Authoritative state  ON
Heartbeat            ON
Reliable events      ON
World rendering      OFF
Effects              OFF
World audio          OFF / reduced
GPU world assets     可釋放
```

AFK **不是 network FPS 降頻**。

若要降低傳輸量，使用 Server replication policy：

- 可暫停純視覺、可重建、非權威的遠距細節。
- Move snapshot 可以合法 coalesce，但不能跨 Stop / Teleport / Hit / Death barrier。
- Combat / HP / item / trade / pickup / disconnect / map transition 必須照常。
- transport poll / heartbeat / sequence 處理頻率不因 AFK 降低。
- 從 AFK 回來先拿最新完整 snapshot / current timeline，再重建 Render，不補播數分鐘舊畫面。

---

## 7. Rendering 設計

不要因資產類型建立一套 Loader，但 Renderer 可以依幾何特性不同：

- Player：paper-doll layered renderer。
- Monster / NPC / Pet：shared atlas frame renderer。
- Ground items：批次 / MultiMesh 優先。
- Map scenery：map-local shared atlas + spatial culling。
- Effects：pool / short lifetime。
- UI：Godot Control/Theme，不混入 world renderer。

所有 Renderer 都讀：

```text
AssetCatalog + AssetStore + immutable RenderState
```

而不是自己解碼圖片。

---

## 8. 命名規則

Godot：

- folder：`snake_case`
- script：`snake_case.gd`
- scene：`snake_case.tscn`
- class_name：`PascalCase`
- logical asset ids：穩定的小寫 namespace，例如 `characters/monsters/en023a`

優先使用明確名稱：

- `network_client.gd`
- `world_state.gd`
- `world_reducer.gd`
- `asset_store.gd`
- `monster_renderer.gd`

避免：

- `manager.gd`
- `helper.gd`
- `utils.gd`
- `common.gd`
- `service.gd`
- `core/`

除非該名稱在該領域有非常明確且唯一的意義。

---

## 9. 目前 Repo 對照

目前存在的：

- `scripts/main.gd`
- `network_client.gd`
- `client_replica.gd`
- `runtime_asset_store.gd`
- `monster_visuals.gd`
- `paper_doll.gd`
- `terrain_renderer.gd`

後續搬移時：

- `client_replica.gd` -> `world/world_state.gd` 或拆 reducer/state。
- `runtime_asset_store.gd` -> `assets/asset_store.gd`。
- `monster_visuals.gd` -> `rendering/monster_renderer.gd`。
- `paper_doll.gd` -> `rendering/player_renderer.gd`。
- `main.gd` 逐步縮成 composition root。

**不要一次同時搬檔、改協定、改邏輯。**

---

## 10. Addons 決策

目前 `client/addons/raw_assets_export/` 是匯出插件，不是 gameplay module。

不能只因 Recovered 沒有 addons 就直接刪除。

刪除前必須證明替代方案可：

1. Windows export 成功。
2. PCK/EXE 包含 Runtime 所需 raw PNG/JSON/BIN。
3. AssetStore 在 release build 可以讀取。
4. Login -> Map -> Player/Monster/Map assets 全正常。
5. 匯出大小、載入時間、記憶體沒有不可接受退化。

若 Godot 標準 resource/import 可以取代自製 plugin，則優先移除 addons，減少維護面。

---

## 11. 其他 Session 開發前必讀

Client 任務開始前至少閱讀：

1. 本文件。
2. `CLIENT_RUNTIME_ISOLATION.md`
3. `NETWORK_AND_MOTION.md`
4. `ASSET_RUNTIME_STRATEGY.md`
5. `RUNTIME_ISOLATION_VERIFICATION.md`

任何新設計若違反 Network/Logic/Render 分離，需要先更新 ADR，不可直接繞過。
