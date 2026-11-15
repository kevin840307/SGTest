# SGTest tools

工具維持少量、明確入口。Production 資產轉換只有一套，不因 Enemy / NPC / Item / Effect 建立多套 CLI、Loader 或 Cache。

## Production asset entry

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

實際入口：

```powershell
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
```

詳見：

- `docs/ASSET_ARCHITECTURE.md`
- `docs/ASSET_CONVERTER.md`
- `docs/ASSET_PACKING_BENCHMARK.md`

## Production responsibilities

`tools/asset_converter/`：

- 唯讀抽取原版 Client。
- SPR decode / pixel/pivot/hash 驗證。
- per-asset Atlas build。
- logical catalog。
- Map terrain / original Blocking grid / world references。
- Server population → independent Monster asset references。
- 全量 Runtime disk export。

`tools/monster_workflow.py`：

- 驗證新增/修改 Monster definition、ACT、spawn。
- population 只引用既有 `Enemy/<sprite>/atlas.json`。
- **不重建 Map + Monster bundle**。

`tools/export_server_content.py`：

- 只輸出 Server 需要的 `world.json / population.json / blocking.dat`。
- 不輸出任何 PNG/Atlas pixels。

## 禁止的 production output

```text
client/assets/bundles/map/
client/assets/bundles/session/
Map + Monster shared atlas
different Monsters shared physical atlas
different scenery source assets shared physical atlas
Player base parts merged into one physical atlas
```

同一 logical asset 內可以自行 pack action/facing/frame；不同 asset 不可以跨界合併。

## Build / verify entry

- Build：`run_build_assets.bat` → `tools/asset_converter/cli.py build`。
- Runtime verification：`tools/asset_converter/verify_runtime.ps1`，會 export Server contract 並跑 Godot asset regressions。

舊 lifecycle bundle builder、normalized candidate scripts 與對應 tests 已從工作樹移除；歷史結果只存在 Git history / 舊報告，不是 production path。

## Research / benchmark

研究工具可測 Dense / Per-Source / Strict Row / Adaptive 等 physical packing，但必須：

- 使用相同 asset IDs。
- Map 與 Monster root 分離。
- 每個 mode 獨立 process。
- 不把研究輸出寫成新的 Runtime asset category。

## Runtime

Runtime 永遠只有共用：

```text
AssetCatalog -> RuntimeAssetStore -> Renderer
```

「每素材獨立」是 data/manifest boundary，不是每素材建立 Loader。

## Tests

- `tools/test_*.py`：workflow/contract。
- `tools/tests/`：converter/parser/evidence regression。
- `client/tests/`：Godot Runtime behavior。
- Packing 比較結果：`docs/ASSET_PACKING_BENCHMARK.md`。
