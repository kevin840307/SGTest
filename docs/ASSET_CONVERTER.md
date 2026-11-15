# SGTest Asset Converter

狀態：**PRODUCTION ENTRY / PER-ASSET OUTPUT**
日期：2026-11-15

正式名稱仍使用 **Adaptive Atomic Atlas** 描述目前 SPR physical packing；但 Runtime 邊界以 [Asset Architecture](ASSET_ARCHITECTURE.md) 為準：**每個素材獨立，不允許跨素材 bundle**。

## 1. 唯一正式入口

```powershell
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
```

已抽取來源可加：

```powershell
--extracted-source .cache/asset_converter/source
```

只發布完整 source catalog 到 `client/assets`：

```powershell
python tools/asset_converter/cli.py export-assets "C:\SGO\Stargate Online IE HK2" ^
  --extracted-source .cache/asset_converter/source ^
  --archive-output local-assets/asset_converter/complete ^
  --assets client/assets
```

Map13 contract（不重新合圖）：

```powershell
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" ^
  --extracted-source .cache/asset_converter/source ^
  --runtime-output client/assets ^
  --runtime-only --map 13
```

## 2. 工具目錄

```text
tools/asset_converter/
├── cli.py
├── operation_lock.py
├── atlas/
│   ├── adaptive_atlas.py
│   ├── build_atlas_groups.py
│   └── selector.py
├── content/
│   ├── asset_paths.py
│   ├── export_assets.py
│   ├── build_maps.py
│   ├── build_population.py
│   ├── npc_content.py
│   └── server_source.py
└── source/
    ├── extract_client.py
    ├── convert_assets.py
    └── runtime_spr.py
```

正式 converter 需要的程式應放在此資料夾。不要再新增第二套 production converter。

## 3. Source / Archive / Runtime 三層

### Extracted source

```text
.cache/asset_converter/source/
```

保留原版來源結構，例如 Enemy / Player / NPC / Map / Interface / MA / MS / SR。

### Verified archive

```text
local-assets/asset_converter/complete/
```

用途：

- 完整 SPR decode。
- Adaptive Atlas source archive。
- pixel/pivot/hash audit。
- ACT/SIF/DrawOrder sidecars。
- benchmark/research。

不代表全部會同時進 VRAM。

### Runtime assets

```text
client/assets/
├── catalog/
├── maps/
├── monsters/
├── npcs/
├── players/
├── equipment/
├── pets/
├── mecha/
├── items/
├── effects/
├── ui/
├── audio/
└── data/
```

不使用 `generated/`、`generation/`、`bundles/map/`、`bundles/session/`。

## 4. 每素材一個 identity

`asset_paths.py` 是 source group → Runtime logical id 的 canonical mapping。

硬性條件：

```text
source groups count == unique logical ids count
```

2026-11-15 全量驗證：

```text
7,890 source groups
7,890 unique logical ids
0 path collisions
```

原版存在重名但不同 source tree 的素材（例如 Player Cloth legacy alternate path）時，必須保留不同 identity，不能因名稱相同覆蓋。

## 5. Map build

Map build 只建立：

```text
maps/<id>/
├── world.json
├── population.json
├── terrain.png
├── tiles.bin
└── blocking.dat
```

`world.json.objects[].atlas` 指向獨立 `maps/visuals/**/atlas.json`。

`population.json.definitions[].atlas` 指向獨立 `monsters/<sprite>/atlas.json`。

禁止產生 `sprite_bundle`。

## 6. Collision

`blocking.dat` 是原版 Blocking.dat 的 compact byte grid，不轉 Godot CollisionShape。

Client / Server 使用同一內容與 SHA contract，避免整張 MMORPG 地圖建立大量物理節點。

## 7. Player

Player B base 已改為：

```text
players/b/base/body/atlas.json
players/b/base/leg/atlas.json
players/b/base/head/atlas.json
players/b/hair/hair00/atlas.json
players/b/base/left_arm/atlas.json
players/b/base/right_arm/atlas.json
players/b/base/death/atlas.json
```

七個 physical assets 分離，但 ACT/DirectionMapping/DrawOrder metadata 仍由原版資料正規化。

## 8. 未驗證語意

不能因檔名看起來像機甲、寵物或裝備就自行重新命名。

MA / MS / SR 等尚未完整確認的來源：

```text
data/legacy/<source-code>/...
```

等原版程式語意證實後再移到正式 semantic category。

## 9. 全量發布結果（2026-11-15）

`export-assets` PASS：

- source files：81,162。
- SPR groups：7,890。
- frames：282,968。
- archive pages：8,094。
- managed Runtime disk files：21,723。
- output bytes：1,402,595,018。
- errors：0。

Semantic categories：

- maps：1,183。
- monsters：137。
- npcs：643。
- players：60。
- equipment：1,451。
- pets：96。
- items：3。
- effects：648。
- ui：514。
- audio：220。
- 未解/資料類放 data，不能虛構語意。

Map13 Runtime contract：

- 367 scenery placements。
- 45 independent scenery visuals。
- 9 Monster definitions / 9 independent Monster visuals。
- 78 Monster spawns。
- 7 Player base/death independent assets。
- `cross_asset_bundle=false`。
- original Blocking byte grid preserved。

## 10. Runtime

磁碟很多檔案不代表 Runtime 常駐很多資源。

Godot 仍只有：

```text
AssetCatalog -> RuntimeAssetStore -> Renderer
```

RuntimeAssetStore 負責 async decode、bounded GPU upload、owner/refcount/LRU。2026-11-15 cache-pressure fixture 驗證 128 independent entries、pinned/recent/LRU/backpressure/recovery，最後 resident=0。

Release 可把所有獨立檔案裝進外部 `assets.pck`；PCK 只是容器，不是一起載入的 bundle。

## 11. Packing benchmark

四種 physical packing 的公平比較見：

- [Asset Packing Benchmark](ASSET_PACKING_BENCHMARK.md)

重要：舊 12-kind Adaptive benchmark 曾因 root 內容不同而選到不同的 12 隻 Monster；舊 76.59 MiB 數字已作廢。

目前 production 仍是 Adaptive Atomic；是否改成 Dense 或新的 Adaptive Per-Asset policy，要用同素材公平 benchmark 再更新 ADR，不得直接改。

## 12. 驗證

已執行：

- Python `tools/tests`：121 PASS。
- Monster workflow fixture：4 PASS。
- Godot：directions / map_objects_async / monster_visuals / paper_doll_async / runtime_asset_store / navigation PASS。
- AssetStore cache-pressure PASS。
- 24 次公平 packing benchmark PASS（4 modes × 2 scenarios × 3 repeats）。
- .NET Server tests：使用專案 `.cache/dotnet` SDK 8.0.428，從 repo root 執行 **376 PASS**（包含原版 Map13 collision contract）。

原始與衍生專有圖片保持本機，不推 Git。
