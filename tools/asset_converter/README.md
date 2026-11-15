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

`build --runtime-only` 同樣接受多次 `--map`，但要先有正式 `client/assets/catalog/assets.json` 和完整獨立 Atlas；目前只有地圖資料層已 PASS，**Runtime/PCK/Godot E2E 仍 NOT VERIFIED**。多圖輸出拒絕重複 ID，隔離及重跑 SHA 回歸見 `tools/tests/test_npc_content.py`。

## 輸出（均可刪除後重新建置）

- `local-assets/asset_converter/complete`：私有 archive，不進 Git。
- `client/assets`：各獨立 logical Atlas／Manifest，例如 `Enemy/<id>/`。
- `server/content`：原版 Map/Blocking.dat/Population；沒有圖片。
- `.cache` 與 `reports`：暫存、benchmark／匯入產物；不是 Git 原始碼。

舊 Renderer Benchmark 需要已刪除的實驗快取，**不作現行工具入口**；
未來正式 Client/Server 建立後，才新增對應可重跑的實際遊戲效能測試。

詳見 [正式規格](../../docs/ASSET_CONVERTER.md)、[歷史對照](../../docs/research/ASSET_RENDERER_EVIDENCE.md)。
