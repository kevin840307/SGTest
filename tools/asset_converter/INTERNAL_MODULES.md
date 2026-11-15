# Adaptive Atomic Atlas — 轉換模組集中管理

新的正式轉換入口及其所有專案內部模組都位於 `tools/asset_converter/`。
直接執行：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

或執行獨立目錄的 `build_assets.bat`；核心為 `cli.py`。

## 分層（僅程式職責，不是新增 Runtime 分組）

- `cli.py`／`build_assets.bat`：唯一正式公開入口，執行原版解包、完整離線 Atlas 索引、per-asset Runtime 發布與報告。
- `source/*`：原始 Data1、SPR、ACT、SIF、DrawOrder 解碼與來源語義。
- `atlas/adaptive_atlas.py`、`atlas/build_atlas_groups.py`、`atlas/selector.py`：只處理**單一 logical asset 內**的 physical packing / candidate 評估；不同素材不得在此合併。
- `content/asset_paths.py`、`content/export_assets.py`：canonical logical id 與全量 per-asset 發布。
- `content/build_maps.py`：terrain、scenery placement 與原版 `blocking.dat`；Map 不包含 Monster pixels。
- `content/build_population.py`：Server monster definition / spawn 指向獨立 `monsters/<id>/atlas.json`。
- `content/animation_definition.py`、`runtime_animation_semantics.py`：動作方向、鏡像、原點與紙娃娃語義。
- 舊 `content/asset_bundle_builder.py`、`content/asset_pipeline.py` 已移除；歷史結果只保留在 Git history / 舊報告，不得重新導入 map/session lifecycle bundle。

所有依賴模組與入口放在同一個 Python package，內部使用 `from .xxx import ...`，不是由新 CLI 往上層匯入散落的 `tools/*.py`。外層同名腳本暫保留給舊分析／既有測試，**新正式流程以本資料夾實作為主**；外層未同步更動不會改變一鍵轉換結果。

必要第三方套件：`python -m pip install -r tools/asset_converter/requirements.txt`。不自動修改 Python 環境。

注意：使用者要求的單一路徑執行是指此 repo 下不再需要尋找多個轉換工具；專有原版素材、.NET Client／Server、原版 Server Dat 仍是指定來源或既有資料，不會打包進這個資料夾。完整離線 archive 保留在 Git-ignore 的 `local-assets`；`client/assets` 可發布完整磁碟素材，但 Runtime 仍由單一 AssetStore 按需載入，不會因磁碟存在就常駐 RAM/VRAM。

最新驗證見 `docs/ASSET_CONVERTER.md` 和 `tools/tests/test_asset_converter.py`。

`atlas/archive_audit.py`負責完整離線索引／source/page/metadata與sidecar驗收；`operation_lock.py`以OS鎖保護同一checkout的build/verify，程序結束即釋放，不依賴lock檔存在與否。兩者均屬離線工具，不新增Runtime類別。
