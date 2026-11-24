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

HP 證據由 Old Server `Enemy_Data.csv[3:5]` 選擇 `EN_AttNN.csv` 與列鍵，再讀該列第 2 欄。輸出記錄屬性表 SHA-256。找不到 `0xffff` 列鍵時保留原始鍵並標記 HP 缺失。Client v3 視覺清單只保留來源層級欄位，仍以 `stats=null` 發布。

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


## HK Pet visual source evidence

`pet-visual-evidence` 只建立可稽核的 HK source identity，不把原版 PetID
直接當成 SGTest DefinitionId，也不輸出原版名稱／描述文字：

~~~powershell
python tools/asset_converter/cli.py pet-visual-evidence \
  --hk-petcli-itd "C:\path\to\HK\Item\petcli.ITD" \
  --extracted-source ".cache\asset_converter\source" \
  --output "local-assets\asset_converter\pet\hk_pet_visual_evidence.json"
~~~

Parser 依 HK Client `sub_403B49` 的實際讀取順序 fail-closed 解析
`PetID + EvolID + Enemy outlook`；`--extracted-source` 會再以正式
animation compiler 驗證 outlook 對應的原始 Enemy SPR+ACT 是否具備
normalized NS/FA 八方向與 timeline。可另傳 `--assets client/assets`
做 production atlas gate；缺少任何 atlas/view 即失敗，不 fallback。
目前 HK source identity 已可重建，且正式專案已加入 `sgtest.pet_definition_bridge/v1`：
`100001..100024` 是 **SGTest 自有 DefinitionId**，明確對應 HK source PetID
`5100..5123`，絕不把兩套 ID 當成同一命名空間。`pet-runtime-catalog` 會把 bridge
與已 audit 的 evidence 合併，並要求每個對應 Enemy atlas 具備 normalized NS/FA view；
缺少 mapping、atlas 或 metadata 即 fail-closed。Client 的 `PetVisualCatalog` / `WorldView`
只對目前 AOI snapshot 真正出現的 Pet 動態 acquire 對應 atlas，不預載全部 46 組。

~~~powershell
python tools/asset_converter/cli.py pet-runtime-catalog `
  --evidence "local-assets/asset_converter/pet/hk_pet_visual_evidence.json" `
  --assets "client/assets" `
  --output "client/assets/catalog/pet_visuals.json"
~~~

**46 組 Pet private runtime atlas publication 已完成並通過 production atlas gate；Pet action 動態 A/B 仍未完成。**
正式 evidence 同時驗證 24 family／87 evolution、46/46 source NS/FA animations 與 46/46 runtime atlases；
363 組既有 runtime + Pet 聯集 publication PASS。`EN115A` 的全動作 normalization 仍因 `ND1` ACT frame 超界失敗，
因此只明確標記 NS/FA 為 `normalized_enemy_partial`，不冒充全動作 verified。NS/FA 作為 Pet runtime idle/attack
的玩法語意仍標 `INFERRED`，直到 HK/Paradise 動態 A/B 完成。

## 私有原版衍生道具名稱目錄（2026-11-17）

只在已合法取得的本機 TestGoGo/client/Items 上執行；此處沒有將原版私有 .tres 或圖片加入 Git。正式唯一入口：

~~~powershell
python tools/asset_converter/cli.py item-catalog --items "C:\\path\\to\\TestGoGo\\client\\Items" --source-reference "TestGoGo/feature/local-server-net8/client/Items"
# 若只要小型輸出，可加 --item-id 2701 --item-id 15001
~~~

預設輸出被 Git ignore 的 client/content/item_catalog.json。由各類別資料夾讀取 filename ID、.tres resource 的 id/item_name/icon_id，核對類別、來源 SHA256、檔名與重複 ID，再排序以原子檔案替換方式發布。缺失/非法來源即拒絕，不用猜測原版名稱。Godot UI 只有合法 JSON 目錄才能顯示衍生名稱，沒有則回退『道具 N』。實際 icon atlas、裝備屬性、原始 ITD 對照仍 NOT VERIFIED。測試：python -m unittest discover -s tools/tests -p test_*.py -q；Converter CI 44098608220 PASS。
