# SGTest 素材轉換器（重建版本）

**目前狀態：正式唯一 Converter 包含 Height-Dense Builder 及 Godot external assets.pck packer，已完成純 Python unit tests 與空白 Godot 專案掛載 PCK 的 smoke；完整原版素材重新轉換、Windows Release 與 macOS 實機尚未完成。**

## 正式入口

~~~powershell
python -m pip install -r requirements.txt
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
# 等效入口
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

Converter **不應依賴**可執行的 client/project.godot 或 server/*.csproj。保留的 client/assets/ 和 server/content/ 僅供輸出。

## MVP Map15 + Map13 定向素材與資料轉換

先確認原版 Map15／Map13 的 Map、Scenery、Blocking、原始座標、Server population/NPC、傳點；用唯一 Converter 產出兩圖必要資產及 Server content，不預先全量解包所有 Runtime 素材。Map15 先供應城市 NPC、Item/Equipment、UI、Player；Map13 供應 Enemy 多種體型與各 Action／方向、地上 Drop、技能與世界地物；Pet/Mecha 依玩法逐步補充。**素材缺失先擴充同一 Converter／metadata，不新增第二套 Map15 或 Map13 腳本。**

`build` 與 `world-content` 現在都接受**可重複的 `--map`**，會排序／拒絕重複 map ID。`world-content --include-map` 使用**同一正式 CLI** 一次輸出 `maps/<id>/world.json`、`terrain.png`、`tiles.bin`、`blocking.dat`、`npcs.json` 到私有 `--output-root`，不需要重建 Client／Server／完整 Atlas；每張圖的 Sprite Atlas 參照仍保有單獨 Asset ID。此為**原始內容資料產出**，不能冒稱完整 Runtime/PCK 已就緒。

~~~powershell
# 首次由 Data1.dat 抽出完整來源；之後指定 --extracted-source 重用本機 .cache
python tools/asset_converter/cli.py world-content "C:\SGO\Stargate Online IE HK2" `
  --extracted-source .cache/asset_converter/source `
  --server-dat "C:\SGO\Recovered\Old\服務端\Dat" `
  --map 15 --map 13 --include-map `
  --output-root local-assets/asset_converter/world `
  --report reports/world_content_mvp_maps.json
# 已發布完完整獨立 Atlas Catalog 後才可使用下列 Runtime 專用建置
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" `
  --extracted-source .cache/asset_converter/source --runtime-only `
  --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 15 --map 13
~~~

**2026-11-15 來源實測 PASS（僅此範圍）**：Data1.dat 索引 41,687 筆、loose overlays 41,707；`world-content --include-map --map 15 --map 13` 完成兩圖、Converter SHA 執行前後一致。Map13：865 種 tile、230,400 tile 引用、367 個地物／17 動畫地物、3 NPC（3 個已驗證靜態傳點）。Map15：976 種 tile、230,400 tile 引用、1,848 個地物／44 動畫地物、52 NPC（4 個已驗證靜態傳點；48 個 script_required）。兩圖 terrain 像素往返驗證 PASS，兩張原版 Blocking.dat 與原版 Server Dat 同圖版本 SHA256 完全相同。

**Map15 特例**：原始 ObjectList.dat 的 15 筆 `Building_object` 記錄只寫 XY、未寫 int32 Z；固定讀 XYZ 會造成後續記錄錯位。轉換器以可驗證的下一筆路徑標頭及完整記錄計數辨識，輸出缺少 Z 的 `z=0` 視覺預設，另以 `z_source_present=false` 保留來源缺欄位事實。**Z 預設及各特殊建築的正式 DrawOrder/UI 表現仍待真 Renderer 驗證**，不得以 converter PASS 抵銷視覺 Gate。

**2026-11-15 Enemy 原始來源驗證**：同一 Converter 支援 `world-content --include-population --map 13 --map 15`（另選 `--include-map` 可同時輸出地形）。在 `--output-root/maps/<id>/population_evidence.json` 寫入**原版來源證據**而非正式 Server population：從唯讀 Old Server `Enemy.dat` + `Enemy_Data.csv` 讀取原始 Enemy ID／出生座標／Flags，結合 Data1 解出的真實 ACT/SPR、完整 8 方向與每一動作的 Frame，記錄每個輸入檔 SHA-256、可追溯的動畫定義 content hash，**不填寫未經證實的 HP、傷害、攻擊範圍或 AI 參數**。來源缺失／截斷／未知 Enemy 會 fail closed。

Map13 **78 Spawn／9 Enemy、488 原始 Frame**；Map15 **4 Spawn／1 Enemy、69 Frame**；10 種共 557 Frame、10 個 Enemy ACT/SPR 定義都成功解析。使用正式 `atlas/dense_atlas.py` Builder 在 `local-assets/asset_converter/mvp_enemy_preview/sprites` 以原版素材試轉 **10/10 獨立 Atlas、10 頁／557 Frame PASS**；重跑兩圖 `population_evidence.json` SHA 2/2 一致、57 項 Python tests PASS。**此 smoke 為分組編譯驗證，不能視為正式 Runtime Catalog、完整 Archive 或 Godot ResourceLoader 成功。** 所有原版 Spawn 位置仍需在權威 World／Blocking 和實際客戶端驗證。

**尚未通過**：全部原版 SPR/ACT 的完整 Archive/source coverage、Player／NPC／Scenery／Enemy 正式 `client/assets` Catalog、Godot 真圖 ResourceLoader、正式 PCK、跨圖 E2E、Server 權威路徑／NPC 腳本與 Enemy 玩法規則及 AssetStore 引用釋放。MVP001 仍保持未完成；兩圖資料檔互不覆蓋不等於交替完整 PCK Build PASS。

MVP 通過後再逐張擴其他地圖、全部素材與完整 Windows/macOS Release，不能將兩圖來源覆蓋稱為全 7,890 asset PASS。

## 主要責任

| 區域 | 職責 |
|---|---|
| source/ | Data1.dat / 原版 client extraction、SPR、ACT、SIF、方向／Item/NPC 原始格式 |
| atlas/ | 依 asset ID 抽幀、裁切、排列、像素驗證／候選演算法 |
| content/ | AssetCatalog、Map terrain/blocking/scenery、Server Population、NPC、DrawOrder、可索引資產發布 |
| `runtime_pack.py` + `runtime_pack_packer.gd` | **已整合**：Godot imported textures，BPTC/ASTC remap、UI lossless、external assets.pck；不依賴可執行 Client，macOS 實機仍待驗證 |

不新增第二套「依怪物／裝備／地圖類別」的 Converter，也不回復舊 map/session bundles。

## 輸出

- local-assets/asset_converter/complete：完整私有來源／sidecars／index。
- client/assets：runtime 資產，含 Enemy/<id>/atlas.json 等獨立頁／Manifest。
- server/content：只輸出權威 Server 要用的地圖、Blocking.dat、Population，不包含圖片。
- .cache 與 reports：可重建的本機測試／分析產物；Git 忽略。

有清除／重新產生需要時**只處理有明確 ownership 的輸出目錄**，不得直接清空 source game、Recovered、其他 Session 的 .cache。

## 重要驗收

- 來源覆蓋／hash：先前原型曾完成 75,588 SPR、282,968 frame／7,890 asset groups；此為**歷史基準，clean-slate 重新跑才算當次通過**。
- 每個 Asset 必須保持獨立 logical/physical identity，Map13 Monster 外觀以 Enemy logical ID 查找。
- 來源 SPR/ACT、trim、Origin、8 方向、事件時間與紙娃娃 DrawOrder 可完整索引和驗證。
- 同一素材 P0/P1 与 VRAM compression 的畫質與 Texture/Load/Frame 指標需做實測。
- 外部 PCK 的 Windows Release 與 macOS import/remap/載入有獨立 Gate。

歷史文件全文見 [research/INDEX](research/INDEX.md)，最新重建任務見根目錄 todo.txt。
## 保持轉換器獨立與可驗證

```powershell
# 純 Python 契約：不會啟動或依賴任何 Client/Server
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1

# 額外從原始 Data1.dat 抽樣重建（需重新解包，清掉快取後會較慢）
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1 -Client "C:\SGO\Stargate Online IE HK2" -PreviewGroups 2

# 真正全量轉換，Server population 的原版 Dat 可用 --server-dat 提供
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" --server-dat "C:\SGO\original-server-Dat-path"
```

`--server-dat` 所示為**示例佔位路徑**，必須換成實際原始 Server Dat；無該來源時 Converter 可做 SPR/Atlas，但不能宣稱 Server population 已重建。目錄 `.cache` 和 `local-assets` 會按命令重新建立，舊圖片不在 Git。避免讓正式轉換器依賴已刪除的 `client/project.godot` 或舊 `server/*.csproj`。

## 已合併的 Height-Dense 與 external PCK 驗證

```powershell
# 不依賴舊 Client/Server 的 Converter 合約測試
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1

# 真正呼叫 Godot 4 Editor Import／PCKPacker，
# 建立 1 份壓縮 Enemy 圖與 1 份無損 UI 圖，
# 再用完全空白 Godot 專案載入外部 assets.pck；所有暫存自動清除
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"

# 有完整已轉換素材時再輸出正式外部 PCK
python tools/asset_converter/runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --assets client/assets --output .cache/client-release/assets/assets.pck
```

以上 Smoke 已在 Windows/Godot 4.7.2 通過，驗證 PCK 實際可由**另一個空白 Godot 專案**載入。這**不是**正式遊戲的 Windows Release、多機多開或 macOS GPU 格式相容性驗證。壓縮 UI icon 及字體必須繼續使用 lossless 模式。

## Archive 索引一致性（MVP001-ARCHIVE-INTEGRITY）

完整 Archive 的 `index.json` 不只是清單：發布前 audit 必須拒絕重複 group，且每個 row 的 frames/pages/rgba_bytes/png_bytes 須等於驗證後的獨立 manifest 與實體 PNG；manifest group 身分及 PNG 大小也必須一致。來源 group set 的 set 比對不足以發現重複 row，否則發布器逐 row 輸出可能重複註冊 logical ID 或回報不實統計。此修正沿用唯一 audit／Builder，沒有新增產線或改變 Atlas 格式；屬一致性修正，無實質替代方案，無需另做效能選型。

回歸包含重複 group、四種 row 計數損毀、還原後合法 Archive、既有 Preview 拒絕及 PNG 損毀；2026-11-15 以臨時目錄複製當前 tools 執行同一 unittest suite，58 tests PASS。使用隔離快照是避免測試 CLI 與正式長時間建置競爭每 checkout 的 OS lock，不停用正式互斥鎖。完整 7,890 group 建置進行中，完整 Archive／Catalog／PCK 仍 NOT VERIFIED。
