# SGTest tools

工具維持少量清楚入口，不因 Enemy / NPC / Item / Effect 等素材類型建立專屬 Loader 或多套 CLI。

## Production asset path

日常完整重建只使用：

```powershell
.\tools\rebuild_assets.ps1 -Client 'C:\SGO\Stargate Online IE HK2'
```

已存在 `.cache/client` 時，sprite/runtime bundle 入口是：

```powershell
python tools\asset_pipeline.py
```

Production 組成：

- `extract_client.py`：唯讀提取原始 Client 到 `.cache/client`。
- `build_maps.py`：從原始 map/grid/object references 產生目前 Runtime 會用的 terrain、collision、world metadata；場景 SPR bounds 直接解原檔，不依賴舊 generated atlas。
- `asset_pipeline.py`：唯一公開 sprite runtime pipeline；依 lifetime 建 map/session bundle。
- `asset_bundle_builder.py`：pipeline 內部共同 packing primitive；normalized animation 與無 ACT raw SPR 都走同一 manifest/runtime。
- `monster_workflow.py`：資料驅動新增／更新怪物；重建既有 map bundle，不建立新 Loader。
- `export_server_content.py`：只輸出 Server 所需 collision/population contract，不輸出圖片。

`client/assets` 是可重建 Runtime output，不是完整原始素材鏡像。未被 Runtime 使用的原始檔留在 `.cache/client`。

## Research / experiment

以下工具保留格式研究、A/B 或歷史回歸能力，但不是 production build 入口：

- `build_assets.py`
- `build_atlas_groups.py`
- `build_character_core_atlas.py`
- `build_normalized_atlas_candidates.py`
- `build_baseline_candidates.py`
- `copy_support_assets.py`
- `convert_assets.py`
- `tools/experiments/*`

不要讓研究候選名稱（direction、hot/warm/cold、512/1024/2048/4096 等）變成 Runtime asset type。

## Benchmark

`benchmark_*.py`、`benchmark_*.gd` 只量測 Before/After。沒有同 workload Before/After，不得在文件中宣稱最佳化完成。

## Viewer / diagnostic

- Viewer：`legacy_asset_viewer.py`、`view_legacy_asset.py`、`client/tools/*preview.gd`。
- Diagnostic：`audit_*.py`、`inspect_*.py`、`report_*.py`、`summarize_assets.py`。

它們可以讀 `.cache/client` 或 reports，但不能自行建立另一套 production cache/loader。

## Tests

- 根目錄 `tools/test_*.py`：production pipeline/contract tests。
- `tools/tests/`：parser、research builder、legacy evidence regression。
- `client/tests/`：Godot Runtime behavior。

## 新素材規則

新增 Enemy、NPC、Equipment、Item、Pet、Mecha、Effect 或 UI 時，預設只新增資料／definition，沿用 `asset_pipeline.py + EntityAtlas + RuntimeAssetStore`。只有不同生命週期且有 load/RAM/VRAM/draw/frame Before/After 證據時，才允許新增 bundle boundary；只有現有 Runtime 無法表達需求時，才考慮新增 Loader。
