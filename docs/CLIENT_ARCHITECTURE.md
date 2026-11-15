# SGTest Client Architecture

狀態：**CANONICAL / IMPLEMENTED STRUCTURE**
日期：2026-11-15

本文件描述目前 Client 的實際程式結構與新增程式時必須遵守的模組邊界。不要再建立平鋪的 `client/scripts/*.gd`，也不要為尚未存在的功能預先建立空目錄或空 Manager。

## 1. 原則

Client 採功能導向 modular monolith：

- Networking、World、Rendering、Assets、UI 分離。
- Server authority 不依賴 Client Render FPS。
- Render FPS / UI FPS 可調；Network I/O、World state/reducer 正確性不可跟著降頻。
- AFK 可停止 world rendering 並釋放 world GPU assets，但 connection、可靠事件與 authoritative state 仍運作。
- 全 Client 只有一套 RuntimeAssetStore；不同 Monster/NPC/Item 不建立不同 Loader。
- 不使用 `Common/`、`Utils/`、`Helpers/`、`Managers/`、`Core/` 當雜物桶。

## 2. 實際 Script Tree

```text
client/scripts/
├── app/
│   └── main.gd
├── assets/
│   ├── asset_pack.gd
│   ├── entity_atlas.gd
│   ├── runtime_asset_store.gd
│   └── verify_export.gd
├── diagnostics/
│   └── frame_profiler.gd
├── gameplay/
│   └── action_policy.gd
├── networking/
│   ├── input_codec.gd
│   ├── message_pack.gd
│   ├── network_client.gd
│   ├── server_clock.gd
│   └── snapshot_codec.gd
├── rendering/
│   ├── animation_definition.gd
│   ├── drop_renderer.gd
│   ├── legacy_facing.gd
│   ├── map_objects.gd
│   ├── monster_visuals.gd
│   ├── paper_doll.gd
│   └── terrain_renderer.gd
├── ui/
│   └── auth_ui.gd
└── world/
    ├── client_replica.gd
    ├── collision_grid.gd
    ├── local_prediction.gd
    ├── movement.gd
    └── spatial_grid.gd
```

只有真正新增功能後才建立新檔案。例如未來 inventory UI 真實作時才新增 `ui/inventory_ui.gd`；不要先建立空殼。

## 3. 各模組責任

### app

Composition root。負責：

- 啟動流程。
- 模組組裝。
- Scene / app lifecycle。
- AFK / foreground mode 協調。

不應逐漸變成 combat、network codec 或 asset cache 的實作位置。

### networking

只處理 transport 與 wire contract：

- WebSocket worker。
- bounded queues。
- MessagePack。
- input/snapshot codec。
- Server clock。

Networking 不畫圖、不 decode PNG、不計算 UI FPS。

### world

持有與權威世界對齊的 Client-side state：

- replica。
- prediction。
- movement。
- collision。
- spatial queries。

World 可以被 Rendering 讀取，但不應直接操作 CanvasItem / Texture。

### gameplay

放 Client input/presentation side 的 gameplay policy。Server 仍是戰鬥、物品、移動的 authority。

### rendering

把 World state 投影成畫面：

- terrain/scenery。
- monster/player paper doll。
- drops。
- animation/facing。

Rendering 可因 FPS、Unfocus、AFK 降低或停止；不能因此改變 Network 收包或 Server state reducer。

### assets

唯一資產存取層：

```text
AssetPack
   ↓
logical manifest
   ↓
RuntimeAssetStore
   ↓
EntityAtlas / Texture
```

不同素材保持獨立 manifest，但共用同一 Store、worker、LRU、refcount、GPU commit queue。

### ui

Control/UI interaction。UI 不直接實作 socket、world simulation 或 asset eviction。

### diagnostics

只觀測，不改 gameplay 行為。

## 4. 依賴方向

```text
UI / Input
    ↓
App
    ↓
Networking ──→ World
                 ↓
              Rendering
                 ↓
               Assets
```

更精確：

- Networking 可以產生 protocol/state update。
- World 消費 update 並維護 state。
- Rendering 讀 World，不反向推進 World time。
- Assets 不依賴 Rendering FPS。
- UI 可以讀 View state，不直接改 Server-authoritative entity state。

避免循環依賴。

## 5. Render / Network / AFK

正常遊玩：

```text
Network I/O       independent
World state       fixed correctness
Render FPS        30~90 configurable
UI FPS            presentation concern
```

AFK：

```text
Network              ON
World state/reducer  ON
Reliable events      ON
World rendering      OFF
World GPU assets     releasable
External asset PCK   mounted
```

PCK 掛載不代表 texture 常駐。

## 6. Runtime Asset Tree

```text
client/assets/
├── catalog/
├── maps/
│   ├── <map_id>/
│   │   ├── world.json
│   │   ├── population.json
│   │   ├── terrain.png
│   │   ├── tiles.bin
│   │   └── blocking.dat
│   └── visuals/<asset>/
├── Enemy/<asset>/
├── npcs/<asset>/
├── players/...
├── equipment/...
├── pets/...
├── items/...
├── effects/...
├── ui/...
├── audio/...
└── data/legacy/...
```

Hard rule：不同 logical asset 永遠不跨 asset atlas。詳見 `ASSET_ARCHITECTURE.md`。

## 7. Client Tool Tree

```text
client/tools/
├── build/
│   └── pack_assets.gd
├── benchmarks/
│   ├── asset_lifecycle.gd
│   ├── asset_worker_idle.gd
│   └── runtime_asset_store.gd
├── previews/
│   ├── direction_preview.gd
│   └── map_preview.gd
└── research/
    └── atlas_benchmark.gd
```

分類規則：

- build：正式 build support。
- benchmarks：可重跑效能/生命週期量測。
- previews：人工查看工具。
- research：非 production policy 的研究程式。

不要把 benchmark script 再放回 `client/tools/` 根目錄。

## 8. Naming

Godot：

- folder / file：`snake_case`
- class_name：`PascalCase`
- logical asset id：小寫、語意清楚，例如 `Enemy/en023a`

避免：

- Session
- Manager
- Helper
- Utils
- Common
- Core

優先：

- `network_client.gd`
- `client_replica.gd`
- `runtime_asset_store.gd`
- `monster_visuals.gd`
- `collision_grid.gd`

連線世代命名使用 `connection_epoch`，不再使用 `session_epoch`。

## 9. 新功能放哪裡

判斷順序：

1. 是 wire/socket/clock？→ networking。
2. 是 replicated state/prediction/collision？→ world。
3. 是純畫面？→ rendering。
4. 是 asset lifetime/decode/cache？→ assets。
5. 是 UI interaction？→ ui。
6. 是 Client gameplay input/policy？→ gameplay。
7. 都不是才重新思考是否真的需要新模組。

不要因為「Monster」「NPC」「Pet」不同就複製整套架構。
