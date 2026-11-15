# SGTest Asset Architecture

狀態：**CANONICAL / ACCEPTED**
日期：2026-11-15

> 本文件是 SGTest 資產邊界的 source of truth。
> 2026-11-15 起，舊的 map/session lifecycle bundle 政策失效。磁碟檔案數不是優先目標；Runtime RAM/VRAM、載入延遲、Draw Calls、Frame P95/P99、可釋放性與維護性才是。

## 1. 核心規則：每個素材保持獨立 identity

原版的一個 logical visual asset，在 Runtime 仍是一個獨立 asset：

- 一隻 Monster ≠ 另一隻 Monster。
- 一個 NPC ≠ 另一個 NPC。
- 一個 scenery SPR ≠ 另一個 scenery SPR。
- Player body / leg / head / hair / left arm / right arm / death 各自獨立。
- Equipment / Pet / Effect / Item 等同樣遵守。
- **禁止**把 Map + Monster、不同 Monster、不同 NPC、不同 scenery、Player 多部位為了少檔案而合成跨素材 atlas。

同一素材內的 action / facing / frame 可以由 Builder 排進一張或多張 page；logical action/facing/frame 必須仍可獨立查閱。

> Physical packing 可以最佳化，logical identity 不可以被 packing 改掉。

## 2. Runtime Asset Tree

```text
client/assets/
├── catalog/
│   ├── assets.json
│   ├── maps.json
│   ├── monsters.json
│   ├── npcs.json
│   ├── players.json
│   ├── equipment.json
│   ├── pets.json
│   ├── items.json
│   ├── effects.json
│   ├── ui.json
│   ├── audio.json
│   └── data.json
│
├── maps/
│   ├── <map_id>/
│   │   ├── world.json
│   │   ├── population.json
│   │   ├── terrain.png
│   │   ├── tiles.bin
│   │   └── blocking.dat
│   └── visuals/
│       └── <original-map-visual>/
│           ├── atlas.json
│           └── page_N.png
│
├── monsters/
│   └── <sprite_id>/
│       ├── atlas.json
│       ├── definition.json
│       └── page_N.png
│
├── npcs/
│   └── <asset_id>/
│       ├── atlas.json
│       └── page_N.png
│
├── players/
│   └── <race>/
│       ├── base/
│       └── hair/
│
├── equipment/
│   └── <race>/<family>/<asset>/
│
├── pets/
├── mecha/                  # 只有來源語意確認後才發布到此類
├── items/
├── effects/
├── ui/
├── audio/
└── data/
    └── legacy/             # MA/MS/SR 等未證實語意先保留來源代號
```

不要增加 `generated/`、`generation/` 或 `bundles/session/`。

## 3. Map 與 Monster 完全分離

Map 只描述世界本身：

- terrain / tile array
- scenery placement/reference
- blocking / navigation
- map metadata
- population reference data

Map visual 只能指向 `maps/visuals/**`。

Monster visual 只能指向 `monsters/<sprite>/atlas.json`。Server population definition 決定某張地圖出現哪個 Monster；地圖貼圖本身不包含 Monster pixels。

好處：

- 同一 Monster 跨地圖直接重用。
- 換 Monster 不需重包 Map。
- Map scenery 和 Monster 可以各自按需載入/釋放。
- Benchmark 可以真正量到 Monster packing，而不是把 Map 成本混進去。

## 4. Player / Equipment

Player 紙娃娃保持原版分層 identity：

```text
players/b/base/body/
players/b/base/leg/
players/b/base/head/
players/b/hair/hair00/
players/b/base/left_arm/
players/b/base/right_arm/
players/b/base/death/
equipment/...
```

Runtime 可以讓這些 part 共用同一 owner lifetime，但 **owner 相同不代表 physical atlas 必須合併**。

DrawOrder、ACT timeline、DirectionMapping 是 metadata；不為了獨立 asset 而丟失原版動畫語意。

## 5. 碰撞：直接使用原版 Blocking.dat

Map collision 不轉成 Godot CollisionShape2D 節點群。

正式輸出：

```text
maps/<id>/blocking.dat
```

內容保持原版 Blocking byte grid：

- Header：width / height。
- 每格 1 byte。
- 目前已驗證 cell = 16 px。
- Client / Server 都使用 compact grid O(1) 查詢。
- source SHA / collision SHA 驗證原始資料一致。

這比為整張 MMORPG 地圖建立大量 Node/Shape 更省 RAM、建立時間與碰撞查詢成本。

## 6. 磁碟很多檔案可以接受

優先順序：

1. Runtime resident RAM / VRAM。
2. Frame P95/P99。
3. Draw Calls / CPU / GPU。
4. 載入與卸載 latency。
5. AFK 能否釋放 GPU world resources。
6. 可維護、可 patch、可驗證。
7. 最後才是磁碟檔案數。

正式 Release 可以把大量獨立檔案放入同一個外部 `assets.pck`。PCK 只是磁碟容器，**不是 lifecycle bundle**；放在同一個 PCK 不代表一起載入 RAM/VRAM。

## 7. Runtime 仍只有一個 AssetStore

每素材獨立不代表每素材一套 Loader。

```text
Catalog
   ↓ logical id / manifest
AssetStore
   ↓ async decode + bounded GPU commit + refcount/LRU
Renderer
```

同一 visual 的 1、100、1000 個 instance 共用同一 Texture/Atlas。不同 visual 才有不同 manifest/cache entry。

Map scenery 採 viewport demand；Monster 依 Server population/AOI demand；AFK 可以釋放 world texture references，Network/WorldState 不受影響。

## 8. Packing 規則

目前正式 Builder 名稱：**Adaptive Atomic Atlas**。

但是 packing policy 只是 physical implementation，不是 Runtime 類型。未來 Builder 可以在同一 logical asset 內比較 Dense / Atomic / page side 候選，只要：

- 不跨 asset。
- 所有來源 pixels/pivot/frame identity 驗證一致。
- action/facing/frame metadata 不丟失。
- 使用同一批素材做公平 A/B 後才改 production policy。

## 9. Benchmark 公平性 Gate

比較任何 Atlas 策略時必須：

1. Map 與 Monster 使用不同 asset roots。
2. 四個模式使用**完全相同 monster IDs**。
3. 每個 mode 獨立 Godot process。
4. 同 window、instances、frames、deterministic action/facing trajectory。
5. Map/scenery background 完全相同。
6. 分開記錄 map load/RGBA 與 monster load/RGBA。
7. 至少 3 repeats，執行順序至少一輪反轉。
8. 不允許以「不同素材組」的結果比較 packing policy。

2026-11-15 稽核發現舊 12-kind benchmark 的 Mode1～3 root 只有固定 12 隻，但舊 Adaptive root 含更多怪物；腳本排序後取前 12，導致 Adaptive 實際測到不同怪物。舊的「Adaptive 12-kind 76.59 MiB」**作廢，不再作 production 決策證據**。

最新公平測試見 [Asset Packing Benchmark](ASSET_PACKING_BENCHMARK.md)。

## 10. 2026-11-15 全量發布證據

原版來源：

- 75,588 SPR presence verified，missing=0 / extra=0。
- 7,890 source groups。
- 282,968 frames。
- 8,094 source-atlas pages。
- 7,890 source groups → **7,890 unique logical asset ids**，collision=0。

`export-assets`：

- source files covered：81,162。
- managed runtime files：21,723。
- disk bytes：約 1.31 GiB。
- Monster：137 independent assets。
- NPC：643。
- Player：60。
- Equipment：1,451。
- Pet：96。
- Map visuals：1,183。
- Effect：648。
- UI：514。
- Audio：220。
- 未驗證來源語意保留在 `data/legacy`，不自行猜 MA/MS/SR 意義。

Map13 contract：

- 367 placed scenery objects。
- 45 independent scenery assets。
- 9 monster definitions / 9 independent monster assets。
- 7 independent Player base/death assets。
- `cross_asset_bundle=false`。
- original `blocking.dat` byte grid preserved。

## 11. 開發硬規則

任何 Session 若要重新引入跨素材合圖，必須先：

1. 更新本文件與 ADR。
2. 使用相同素材集做公平 benchmark。
3. 證明 Runtime RAM/VRAM/frame/load 獲益足以抵銷 coupling。
4. 得到使用者明確同意。

否則視為架構回歸。
