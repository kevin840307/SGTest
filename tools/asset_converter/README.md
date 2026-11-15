# SGTest Asset Converter

**唯一正式入口：`tools/asset_converter/cli.py`。**
本目錄不需要舊版 Godot Client 或 .NET Server，可從原始遊戲目錄
重新產生素材。不同 Enemy、裝備、場景和人物部位保持獨立；PCK 是
配送容器，不是跨 Asset 合圖。

## 結構

```text
asset_converter/
  cli.py                 # 原始素材處理與資料產出入口
  source/                # Data1/SPR/ACT/SIF 及原版 Decoder
  atlas/
    dense_atlas.py       # 正式 Height-Dense P0 Builder
    layout.py            # 原素材分組及超限單 Asset 分頁 fallback
    archive_audit.py     # 來源 / atlas integrity
  content/               # catalog、map/blocking、NPC、population
  json_files.py          # 原子寫入、避免輸出半套 JSON
  runtime_pack.py        # Godot import 與外部 PCK
  runtime_pack_packer.gd
  smoke_runtime_pack.py  # 獨立的 PCK ResourceLoader smoke
  operation_lock.py      # 避免並行建置覆蓋產物
```

不維護舊 Adaptive Atomic/Selector、多套 Loader、過期 packing／Renderer
Benchmarks；研究結果與原始腳本仍可在 [歷史資料](../../docs/research/INDEX.md)／Git 歷史找到。

## 建置與驗證

```powershell
python -m pip install -r requirements.txt
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1

# 另外檢查 Godot imported Texture + external PCK 能被空白專案讀取
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"
```

`-Client "原始遊戲路徑" -PreviewGroups 2` 可交給 `verify_converter.ps1`
對 Data1.dat 做少量來源試轉，但全量覆蓋必須另外執行 Gate。

MVP001 也可透過**同一 CLI** 的 `world-content --include-map --map 13 --map 15`，從原始來源定向重建兩張地圖的地形、地物、Blocking、NPC 與已靜態驗證的傳點資料，私有輸出位於 `local-assets/asset_converter/world/maps/<id>/`。配合 `--extracted-source .cache/asset_converter/source` 可重用已解包來源，避免每次重新展開 Data1.dat；詳細可重跑命令見 `docs/ASSET_CONVERTER.md`。

`world-content` 可加 `--include-population`，讀取原版 `Enemy.dat` + `Enemy_Data.csv` 與真實 SPR/ACT，在每張地圖輸出 `population_evidence.json`；只驗證 Spawn/定義/動畫來源，不發明 HP/ATK/AI，也不偽裝成 Server Runtime population。2026-11-15 原版 Map13／Map15 合計 82 Spawn、10 種 Enemy、557 原始動畫 Frame 驗證 PASS；正式 Height-Dense Builder 獨立預覽 10/10 Atlas PASS。相關回歸在 `test_population_evidence.py`。

`build --runtime-only` 同樣接受多次 `--map`，但要先有正式 `client/assets/catalog/assets.json` 和完整獨立 Atlas；完整 7,890 素材 Archive audit 與雙圖定向 Catalog（242 Asset／2,269 Frame）已 PASS，雙圖 Runtime content／空白 Godot external PCK 資源 Gate 已 PASS（244 Texture）；**可執行 Client/Server E2E 仍 NOT VERIFIED**。多圖輸出拒絕重複 ID，隔離及重跑 SHA 回歸見 `tools/tests/test_npc_content.py`。

## 輸出（均可刪除後重新建置）

- `local-assets/asset_converter/complete`：私有 archive，不進 Git。
- `client/assets`：各獨立 logical Atlas／Manifest，例如 `Enemy/<id>/`。
- `server/content`：原版 Map/Blocking.dat/Population；沒有圖片。
- `.cache` 與 `reports`：暫存、benchmark／匯入產物；不是 Git 原始碼。

舊 Renderer Benchmark 需要已刪除的實驗快取，**不作現行工具入口**；
未來正式 Client/Server 建立後，才新增對應可重跑的實際遊戲效能測試。

詳見 [正式規格](../../docs/ASSET_CONVERTER.md)、[歷史對照](../../docs/research/ASSET_RENDERER_EVIDENCE.md)。

定向發布沿用同一 CLI：`export-assets "原版路徑" --extracted-source .cache/asset_converter/source --server-dat "原版 Server Dat" --map 13 --map 15`。必須有完整已 audit Archive；只輸出兩圖真實地物／NPC／Enemy 參照及目前 B Player 七部位，Catalog 明示 selected_sprite_assets 範圍，各 Asset 的動作／方向不裁切，不額外複製全世界音效或原始 payload。未指定 --map 保持 full export；詳 ASSET_CONVERTER。

真實雙圖資源 smoke：`python tools/asset_converter/smoke_runtime_pack.py "Godot console exe" --assets client/assets --map 13 --map 15 --output .cache/client-release/assets/assets.pck --report reports/real_maps_pck_smoke.json`。驗證 ResourceLoader 與封裝 hash，不代表 Renderer／Y-sort／實機畫質或遊戲 E2E。未提供 --assets 仍測 synthetic Enemy＋UI。
