# SGTest Asset Converter

狀態：**PRODUCTION ENTRY / PER-ASSET BOUNDARY**
日期：2026-11-15

正式 build 只有一套：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

或直接：

```powershell
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
```

## 目錄

```text
tools/asset_converter/
├── cli.py
├── build_assets.bat
├── verify_runtime.ps1
├── operation_lock.py
├── source/
│   ├── extract_client.py
│   ├── runtime_spr.py
│   ├── convert_assets.py
│   ├── inspect_act.py
│   ├── legacy_direction.py
│   └── legacy_npc.py
├── atlas/
│   ├── adaptive_atlas.py
│   ├── build_atlas_groups.py
│   ├── selector.py
│   └── archive_audit.py
├── content/
│   ├── asset_paths.py
│   ├── export_assets.py
│   ├── animation_definition.py
│   ├── runtime_animation_semantics.py
│   ├── build_maps.py
│   ├── build_population.py
│   ├── build_enemy_map_palette.py
│   ├── npc_content.py
│   └── server_source.py
└── benchmarks/
    └── packing/
        ├── build_candidates.py
        ├── benchmark.gd
        └── run.py
```

## 職責

- `source/`：只理解原版資料格式。
- `atlas/`：單一 logical asset 內的 packing / pixel verification。
- `content/`：canonical logical id、Map/Population contract、Runtime publication。
- `benchmarks/`：可重跑 A/B，不是 production dependency。

禁止重新建立：

- lifecycle bundle builder。
- map/session atlas。
- normalized candidate 的另一條 production pipeline。
- 每素材類型一套 Loader/Cache。

## Asset Boundary

```text
one Monster    = one independent asset
one NPC        = one independent asset
one scenery    = one independent asset
one Player part= one independent asset
one equipment  = one independent asset
```

同一 asset 內 action/facing/frame 可以重新 packing；不同 asset 不可以合併。

## Output

Canonical private archive：

```text
local-assets/asset_converter/complete/
```

Runtime disk assets：

```text
client/assets/
├── maps/
├── monsters/
├── npcs/
├── players/
├── equipment/
├── pets/
├── items/
├── effects/
├── ui/
├── audio/
└── data/
```

不建立 `generated/`、`generation/`、`bundles/map/`、`bundles/session/`。

## Build / Verify

完整 build：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

只驗完整 archive：

```powershell
python tools/asset_converter/cli.py verify "C:\SGO\Stargate Online IE HK2"
```

Runtime contract + Godot asset regressions：

```powershell
tools\asset_converter\verify_runtime.ps1 -Client "C:\SGO\Stargate Online IE HK2"
```

## Packing Research

目前 production physical policy 仍是 Adaptive Atomic；asset boundary 已與 packing 解耦。

最新候選：

- Per-Asset Dense Single。
- Adaptive Dense Shelf。
- Adaptive Dense MaxRects。
- Adaptive Atomic。

重跑：

```powershell
python tools/asset_converter/benchmarks/packing/build_candidates.py
python tools/asset_converter/benchmarks/packing/run.py
```

結果只寫入：

```text
.cache/asset_packing_benchmark/
```

不要把 A/B 產物再放進 `local-assets/`。

最新結果與建議見 `docs/ASSET_PACKING_BENCHMARK.md`。目前正式 7,890 asset 的幾何掃描全部可使用單一 Dense page <=4096；若使用者確認切換，才修改 production Builder 並重跑完整 pixel/origin/hash verification。

## Legacy Research

`tools/` 根目錄仍有少數原版格式研究／viewer script，供 Recovered/Old 對照。它們不是 production converter dependency。

已移除：

- lifecycle `asset_bundle_builder.py` / `asset_pipeline.py`
- normalized atlas candidate builder/reporter/tests
- bundle lifecycle Godot benchmark
- shared-bundle monster async test
- 舊 `local-assets` packing experiments

不要為了「看起來乾淨」刪掉仍負責解析原版 SPR/ACT/SIF/ITF 的研究工具；production 與 research 的界線以本文件為準。
