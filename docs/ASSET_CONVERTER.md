# SGTest 素材轉換器（重建版本）

**目前狀態：正式唯一 Converter 包含 Height-Dense Builder 及 Godot external assets.pck packer，已完成純 Python unit tests 與空白 Godot 專案掛載 PCK 的 smoke；完整私有 Archive 重新建置與獨立稽核已通過；雙圖正式 Catalog／Runtime content 與真圖 PCK ResourceLoader 已通過，正式遊戲 Windows Release 與 macOS 實機尚未完成。**

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
# 必須先有通過完整 audit 的 Archive；只發布兩張地圖參照的正式獨立 Atlas
python tools/asset_converter/cli.py export-assets "C:\SGO\Stargate Online IE HK2" `
  --extracted-source .cache/asset_converter/source `
  --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 13 --map 15 `
  --report reports/mvp_scoped_asset_export.json
# 已發布兩圖所需獨立 Atlas Catalog 後才可使用下列 Runtime 專用建置
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" `
  --extracted-source .cache/asset_converter/source --runtime-only `
  --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 15 --map 13
~~~

**2026-11-15 來源實測 PASS（僅此範圍）**：Data1.dat 索引 41,687 筆、loose overlays 41,707；`world-content --include-map --map 15 --map 13` 完成兩圖、Converter SHA 執行前後一致。Map13：865 種 tile、230,400 tile 引用、367 個地物／17 動畫地物、3 NPC（3 個已驗證靜態傳點）。Map15：976 種 tile、230,400 tile 引用、1,848 個地物／44 動畫地物、52 NPC（4 個已驗證靜態傳點；48 個 script_required）。兩圖 terrain 像素往返驗證 PASS，兩張原版 Blocking.dat 與原版 Server Dat 同圖版本 SHA256 完全相同。

**ObjectList 契約修正（2026-11-16，world v3）**：先前把 XY 後的 int32 當 Z、把15筆子物件當「缺Z」的結論已撤回。DIRECT：唯讀 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst`，SHA256 `e559b93b2f89a80d7f012901665f0a45fb54579b463805b9a66c1fff3a2314b8`；`CELMap2D::MapObjListConvert` 0x005291B5–0x005297AB，0x0052952C–0x00529556 讀子物件數並計入總數，0x00529577–0x00529603 對子物件只讀 name+XY；根／子物件 SetWorldPosition 均明確傳 Z=0（0x0052937E–0x0052938B、0x005296C9–0x005296D6）。Header 是含 children 的總筆數；根筆 name+XY+child_count，接續 children 只有 name+XY，座標仍為絕對世界座標。Map15 的 5 個 root 各有1／2／4／3／5個 children（共15），總1848；Map13共367且無children。world v3 `objects` 保留來源扁平順序，以 `parent_index`（根為null）及 `child_count` 表達關聯，`z` 一律0，刪除誤導的 `z_source_present`。Parser以count解析，不再猜下一筆路徑；負數／超出總數／非BLG父物件／截斷／非零垃圾尾端拒絕。v2不能靠改版本號升級，須由唯一CLI從原始ObjectList重建雙圖及PCK；Runtime與AssetStore拒絕v2。Atlas／p／trim-origin bounds／Terrain／Blocking未改。證據可靠性：反編譯組語DIRECT，非執行原版的動態trace；所有特殊建築的實際顯示仍 NOT VERIFIED。

**2026-11-15 Enemy 原始來源驗證**：同一 Converter 支援 `world-content --include-population --map 13 --map 15`（另選 `--include-map` 可同時輸出地形）。在 `--output-root/maps/<id>/population_evidence.json` 寫入**原版來源證據**而非正式 Server population：從唯讀 Old Server `Enemy.dat` + `Enemy_Data.csv` 讀取原始 Enemy ID／出生座標／Flags，結合 Data1 解出的真實 ACT/SPR、完整 8 方向與每一動作的 Frame，記錄每個輸入檔 SHA-256、可追溯的動畫定義 content hash，**不填寫未經證實的 HP、傷害、攻擊範圍或 AI 參數**。來源缺失／截斷／未知 Enemy 會 fail closed。

Map13 **78 Spawn／9 Enemy、488 原始 Frame**；Map15 **4 Spawn／1 Enemy、69 Frame**；10 種共 557 Frame、10 個 Enemy ACT/SPR 定義都成功解析。使用正式 `atlas/dense_atlas.py` Builder 在 `local-assets/asset_converter/mvp_enemy_preview/sprites` 以原版素材試轉 **10/10 獨立 Atlas、10 頁／557 Frame PASS**；重跑兩圖 `population_evidence.json` SHA 2/2 一致、57 項 Python tests PASS。**此 smoke 為分組編譯驗證，不能視為正式 Runtime Catalog、完整 Archive 或 Godot ResourceLoader 成功。** 所有原版 Spawn 位置仍需在權威 World／Blocking 和實際客戶端驗證。

**尚未通過**：跨圖 E2E、NPC 動畫／腳本、Server 權威路徑與 Enemy 玩法規則、AssetStore 引用釋放、渲染畫質／P0 壓縮邊緣與正式發行。雙圖 Catalog／Runtime content 及 PCK 資源載入已完成，詳後列分項驗證。MVP001 仍保持未完成；兩圖資料檔互不覆蓋不等於交替完整 PCK Build PASS。

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

回歸包含重複 group、四種 row 計數損毀、還原後合法 Archive、既有 Preview 拒絕及 PNG 損毀；2026-11-15 以臨時目錄複製當前 tools 執行同一 unittest suite，58 tests PASS。使用隔離快照是避免測試 CLI 與正式長時間建置競爭每 checkout 的 OS lock，不停用正式互斥鎖。完整建置及獨立 audit 已 PASS；後續 Catalog／PCK 結果見下列 CATALOG／Runtime／PCK 分項。

2026-11-15 正式全量重新驗證：唯一 CLI `build --archive-only --extracted-source .cache/asset_converter/source`（822.07 秒）與後續 `verify` 皆 exit 0。實際 7,890 group／75,588 SPR／282,968 Frame／7,871 頁，613 ACT／1,165 SIF／9 DrawOrder，全部無錯誤。PNG 合計 960,300,862 bytes；未壓縮頁面理論 RGBA 4,107,323,520 bytes，**不是 Runtime RSS 或 VRAM 常駐量**。Archive index SHA256：`bd6b8a6397f23bbba33f4ade59092c4abe40a446512a524b904a67047836ea55`，本機報告 `reports/mvp_complete_archive_verification.json`。Builder 做逐幀像素／原點比較；audit 獨立重算來源、metadata、PNG hash／幾何、來源幀 identity 和 sidecar，不重播像素比較。主 checkout 完整 suite 在 CLI 結束後單獨執行 58 tests PASS。原版／衍生圖及來源 JSON 均不進 Git；這仍不是全動畫語意、P0 壓縮畫質或正式 Client/Server PASS。

## 雙圖定向 Catalog（MVP001-CATALOG-SCOPE）

`export-assets --map 13 --map 15` 沿用既有 Map／NPC／Enemy evidence 編譯器追蹤原始參照，加上目前基礎 B Player 七部位，選取需發布的完整 Asset，**不裁去其動作／方向／Frame**。所有 Atlas 仍來自同一份完整、已 audit 的 Archive；不建立第二套 Builder。未指定 `--map` 保持完整來源發布。空集合／未知 Asset ID 與完整 Archive audit 失敗均在建立輸出前拒絕；未知 NPC type／缺 SPR 不可假裝沒有外觀。只有原始 NPC 空 primary slot 允許無 visual；兩圖實測 55 NPC 均已解析外觀。

Catalog 的 `source_scope=selected_sprite_assets` 與 `_source_coverage.json` 明確只涵蓋選取 SPR；不複製不相關音效、ACT sidecar 或 opaque payload，所需 Enemy／Player 的編譯動畫定義隨 Atlas 輸出。這是原有 Publisher 的可選範圍，沒有額外 Loader／Cache 或平行 Converter。每個 Asset 內維持完整 Frame／來源 hash／頁面 hash／Origin；切換發布範圍只刪除前次追蹤且 hash 仍相同的舊輸出，人工修改保留並列入 report。

2026-11-15 真實雙圖發布 PASS：195 地物＋10 Enemy＋30 NPC 外觀＋7 Player 部位＝242 Asset／242 頁／2,269 Frame／756 SPR，515 managed files，20,332,844 bytes（約 19.4 MiB）。10 Enemy＋7 Player 動畫定義已 normalized，225 個地物／NPC 僅 source clips，**不宣稱 NPC 動畫時間／方向已驗證**。反向 `--map 15 --map 13` 重跑，517 個實際檔案（含追蹤檔及 `.gdignore`）SHA 全部一致；報告 `reports/mvp_scoped_asset_export*.json`、`reports/mvp_scoped_asset_repeatability.json`。完整 62 項 Python tests PASS，含 full export 相容、scope 切換／保留修改、未知 ID、無關素材損毀仍拒絕及 deterministic replay。

未包含本階段沒有參照的裝備／UI／掉落／Pet／Mecha；後續功能需擴同一素材選取流程。Runtime map content 與 PCK ResourceLoader 的後續 PASS 見下列分項；NPC timing／紙娃娃真畫面與 Client/Server E2E 仍 NOT VERIFIED。定向是依真實參照排除未用資料，沿用同一 packing，非 Atlas／Renderer 性能選型，不宣稱改善遊戲 FPS。

## Runtime 地圖資料 v3（MVP001-RUNTIME-CONTENT）

`build --runtime-only --map 13 --map 15` 現在沿用同一 Map／NPC／Enemy compiler，每張圖輸出 6 個檔案（world.json、terrain.png、tiles.bin、blocking.dat、population.json、npcs.json）。NPC visual 加入由 source SPR 確定性推導的獨立 Atlas 參照，內容 fingerprint 隨新欄位重算；驗證 schema／map identity、原始來源映射與 Atlas 存在性。未知 type／缺 SPR／缺 Atlas／錯映射 fail closed；原版明示 empty_primary_slot 可保留無視覺 NPC。沿用暫存 stage，驗證失敗不覆寫該地圖原發布內容。

**population v2 → v3：必須重建資料，不做補值遷移。** 移除無原版依據的 DEFAULT_STATS／validate_stats；v3 `kind=visual_spawn_content`，根與各 definition `stats_verified=false`，definition `stats=null`，根 `gameplay_ready=false`。原始 Spawn 位置／definition ID／source_index／flags／source SHA／Atlas 不改；外部傳入的猜測 stats 也不會成為原版已驗證數值。現有 validator 拒絕 v2 與夾帶 stats／虛稱 verified 的資料。此 repo 沒有正式 Client/Server 或資料庫 schema consumer，遷移方式是刪除舊輸出依既有 CLI 重跑，**不是移植假的怪物數值**。正式 Server 在原版規則未驗證前不可啟用戰鬥／AI；來源規則研究仍為後續工作。

2026-11-15 真實來源重建與反向重跑 PASS：Map13 3 NPC／78 Enemy Spawn，Map15 52 NPC／4 Enemy Spawn，全部 NPC 外觀參照存在；兩圖 12 檔 SHA 重跑完全一致，stats 全部 null／未驗證。`reports/runtime_content_validation.json` 記錄 SHA 與範圍。65 項 Python tests PASS，包括缺 NPC Atlas 不覆寫舊輸出、錯圖／錯映射／未知 NPC fail closed、v2 拒絕及移除猜測 stats。CLI PASS 僅為資料契約；NPC 動畫／腳本執行、權威 Spawn／A*／Combat、真 Client/Server E2E 仍 NOT VERIFIED；PCK 資源驗證見下節。

## 真實雙圖 external PCK（MVP001-PCK-REAL-MAPS）

既有 `smoke_runtime_pack.py` 新增可選 --assets／--map／--output／--report，未指定仍測合成 Enemy＋UI。真圖模式先驗證正式 Catalog／Atlas page SHA／尺寸與兩圖 Runtime content，再使用同一 `build_runtime_pack`。另一個完全空白 Godot 專案掛載外部 PCK，以 FileAccess 逐一比對封裝原檔 SHA，逐頁 ResourceLoader 載入 Atlas／terrain Texture2D 並比對尺寸；沒有第二個素材 Builder／Client。

```powershell
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --assets client/assets --map 13 --map 15 --output .cache/client-release/assets/assets.pck --report reports/real_maps_pck_smoke.json
# 原有 synthetic fixture 回歸
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --report reports/synthetic_pck_regression.json
```

2026-11-15 Godot 4.7.2 實跑 PASS：242 個獨立 Atlas、244 Texture（242 Atlas＋2 terrain）載入成功，526 原始封裝檔案 hash 相符（包括 catalog／動畫定義／world／NPC／population／Blocking／tiles）；242 BPTC＋242 ASTC 匯入變體檢查 PASS。私有 PCK 為 68,066,172 bytes，SHA256=`4df45ef43ffcbe03e2e5255bca087baeefaf45210ddb7ca9ecca93714d76ae7a`，輸出位於 `.cache/client-release/assets/assets.pck`，不進 Git。報告 `reports/real_maps_pck_smoke.json`。既有 synthetic 2/2 ResourceLoader PASS，壓縮 Enemy＋lossless UI Policy 仍正確。另將空白專案 verifier 的 expected Map13 Blocking SHA 改成全零，Godot 實際 exit 3／hash mismatch，未印成功旗標，負向 Gate PASS（`reports/pck_blocking_rejection.json`）。

67 項 Python tests PASS，新增原始頁 hash／Texture 尺寸繫結及缺少尺寸記錄拒絕；沒有新增渲染或 packing 選型。這只證明現有 PCK 可在 Windows headless ResourceLoader 載入及資料封裝一致，**不證明實際畫面、VRAM 峰值、P0 壓縮邊緣、Y-sort／NPC timing、macOS 實機、正式發行、Server Spawn／Collision／Combat 或多人 E2E**。map13／15 的 gameplay_ready 仍 false。

### 景物遮擋的原版邊界

同一份DIRECT來源：`CELMap2D::WorldToScreenLoc` 0x00528517–0x00528595 將世界XY減相機XY，沒有套用Z；`SortMapObject` 0x00529CAA–0x00529DBC、`QuickSortMapObject` 0x00529B63–0x00529CA9 對一般物件比較Y，但sorting mode=0的建築使用 `CELBldg2D::CheckPointInFront` 0x00549560–0x005496C2／CheckBldgInFront。`DrawMapObj` 0x0052A057–0x0052A08A 還有前／主／後draw list。因此不能將所有Scenery與建築直接套Godot y_sort，或將舊誤判count當z_index。下一切片須解析SIF mode、BLG前緣及室內／室外來源，再驗證角色交錯、同Y與建築子物件顯示；當前只完成資料階層修正。

### 景物 presentation metadata 與兩欄素材（2026-11-16）

沿用world v3，新增明確的 `scenery_schema=sgtest.scenery/v1` 與共用 `scenery_definitions`。每個placement只增加definition引用，同BLG/STH的模式、幾何、點集與來源hash不逐實例重複。舊v3缺scenery_schema也必須用唯一CLI重建；AssetStore拒絕缺契約的map。這是尚未完成景物Renderer前的加法契約，沒有第二套素材載入管線。

- SIF：length-prefixed SPR名，舊16-byte尾為center X/Y、width/length且mode預設1；17-byte尾多一個mode byte。保留mode及是否來源實際帶byte，不改原始Archive `parse_sif` 的raw sidecar定義。mode非0/1/2仍保留，draw_group依原版switch fallback=1。17-byte尾若恰少1 byte可能符合舊格式，不能單靠長度辨識損毀；必須以來源完整性hash控管。
- BLG：四個length-prefixed名稱（兩組authoring/runtime），兩個前緣端點（4×int32）及兩組count+XY點。authoring絕對路徑只略過，不發布／追蹤為依賴；runtime路徑仍檢查不能離開原始tree。保留front_raw及file-based constructor將正Y改-10的front；inside_cells/outside_cells保留兩組原始整數點，未實作遊戲出入口判定。空alternate可為null；空primary目前明確拒絕，不憑空補素材。
- appearances[0]/[1]保留原始兩欄，名稱採primary/alternate順序，不靠檔名猜室內外；每欄有source／atlas／clip、SIF mode／center／size、frame count與local_bounds（LTRB）。placement bounds為兩欄全部SPR原點及非透明像素的保守聯集（XYWH）；primary的舊atlas／clip欄位是同一資產的相容引用。mode來自primary；共用center／size依原版ctor被最後一個非空SIF覆寫。sorting_mode由物件類型決定（BLG=0，static=1），與SIF draw_group不同。
- `scenery_asset_paths`供唯一scoped export與runtime checker共同遍歷所有appearance；兩欄都要進Catalog／PCK，不能只按placement.primary選資產。每個definition記錄解包後BLG/STH/SIF/SPR SHA；既有Height-Dense Atlas／logical ID不變。

DIRECT證據仍是上述同SHA的Paradise.exe.lst：`CELStaticObj2D(char*,char*,int)` 0x0054A172（0x0054A385–0x0054A44D辨識16/17尾、寫mode／center／size）；`CELBldg2D(char*,char*,int)` 0x005485BF（0x005488D5–0x00548905 primary mode、0x00548AF9–0x00548BA3 alternate幾何、0x00548C14–0x00548CC6 front與clamp）；vtable 0x005C5894／0x005C58CC將+0x18映射GetSortingMode、+0x1C映射GetMode，GetMode函式本體在lst折疊，mode欄位用途由constructor及具名virtual call串接。MapObjLoad／MapObjListConvert依GetMode分0/1/2 lists並SetSortingMode(0/1)。`CELDrawBldgLogic2D::Draw` 0x005506CE–0x00550EA0依InOut選兩欄並有交疊fade及子物件draw；`SetInOut` 0x005504DE將fade設256，Step 0x0055050A每次減16，**更新的實際時間節奏仍未驗證，不得直接當16ms或Render FPS**。CheckBldgEntrance 0x00549950與CheckPointInsideBuilding 0x00549879提供後續切換證據；本輪只保留資料，未宣稱切換alpha／遮擋／碰撞規則PASS。

真來源：Map13 45 definitions／85非空appearance欄／45 distinct Scenery Asset；Map15 182 definitions／358欄／194 distinct Asset，其中12 definitions兩欄不同。雙圖scoped Catalog從242增至254個獨立Asset／254頁／2281 frames（207 Scenery＋10 Enemy＋30 NPC＋7 Player），25,959,843 bytes；完整Archive audit再通過，79.027秒。舊Archive頁面未重做，沒有上傳原圖。兩圖world JSON分別159,175／753,882 bytes；world/terrain/tiles/blocking四檔重跑逐byte一致。來源與page SHA／clip覆蓋報告 `reports/scenery_metadata_validation.json`。

新版PCK SHA256=`7784d572ffd0c0bb769d7df4b90bd36597422639ffc67e74a756a70ed67ec57d`，86,385,080 bytes，254 BPTC＋254 ASTC；上述CLI runtime-only／runtime_pack命令不變。72項Python tests PASS；AssetStore完整30次雙圖palette循環PASS（Map15 peak233 Asset、預算95,161,196 bytes，final counts全0；engine無error/leak），epochs三情境PASS；正式Terrain同雙圖720p/1080p 16 captures像素0差異PASS。詳TEST_PLAN。新增素材提高PCK大小，未據此聲稱效能改善；Renderer對完整palette與逐視野預載的選型仍待公平測量。

### 原版景物順序離線編譯（2026-11-16）

world v3／scenery v1新增`scenery_order` v1：三個groups（原版GetMode 0/1/2），各為來源objects的root索引，children只隨父物件畫，不當root重排；保留passes／settled。來源DIRECT與有界1025輪算法見CLIENT_ARCHITECTURE。普通同Y穩定，但建築比較不是嚴格全序，禁止換成一般sort而改變結果。只在同一Converter建置時執行一次，Client不用每幀做整圖排序。Map13 group counts=33/334/0、passes=1/29/1；Map15=89/1744/0、passes=2/71/1，皆settled；同資料重算完全一致（reports/world_order_validation.json）。

唯一CLI雙圖runtime-only PASS，13.55秒；PCK重建PASS，254 BPTC／254 ASTC不變，SHA256=`0346cc610d3cbd643dc12b4efd8d0921b9d6b132e9e5e2bdb6d1a2a8ea65b409`，86,394,680 bytes。Python suite75 PASS，原生靜態景物／角色交錯驗證與兩候選比較見TEST_PLAN／CLIENT_ARCHITECTURE。Archive頁面／邏輯Asset ID未改，原始素材仍只在本機。動畫與出入口時序、P0壓縮品質及正式Client/Server仍未完成。

### 景物ACT default profile（2026-11-16）

唯一export-assets新增map/來源的字面`action`綁定；有ACT才輸出`atlas.v9.act[clip]`的`sgtest.action_timeline/v1`，固定50Hz／numerator40／default_speed25、raw_time／frame index／loop／events與ACT來源SHA。未知key／空action／clip index越界拒絕，沒有ACT就保留source_clips_only，不猜FPS。8種normalized_scenery_act、10 Enemy、7 Player、229 source_clips_only，總254 Asset／254頁／2281 Frame不變；Archive audit完整再通過，372.769秒。Scenery timing只涵蓋default25；dynamic_speed_verified=false，事件callback與視覺逐格舊Client parity仍未驗證。WorldDrawList只替原版CELStaticObj2D類型綁定，不把BLG引用同素材當成會播放ACT。

FISH01 ACT首index1，最後index10為raw300的合法長空白幀；不能依SPR幀數平均時間。Map13有5種／17實例，Map15有6種／44實例，兩圖全部多幀景物61實例已涵蓋，source-clips-only的NPC等時序不因此完成。私有bindings／ACT SHA見reports/scenery_time_bindings.json；本輪重新hash兩個HK2 binary與歷史timing audit版本一致，精度／DIRECT+AUDIT呼叫鏈見CLIENT_ARCHITECTURE。

PCK重建PASS：SHA256 `02d05d9ae68f7760d6ded076c34c1c4f77c9bd0342f552a1a8df323df6cbb9d4`，86,399,320 bytes，254 BPTC＋254 ASTC；只有metadata增加，頁面／ID未改。79項Python tests PASS，原生ACT／fade與端點／epoch回歸見TEST_PLAN。原始素材與ACT產物仍只在本機，未上傳公開Git。

### Runtime block4頁面（2026-11-16）

唯一`export-assets`在完整Archive audit後，以`runtime_page`保留原始Frame rect／origin／trim與頁面所有來源texel，只補右下透明留白至4倍數。Archive頁與索引不重做；Runtime atlas v9另外保留archive_page_sizes／archive_page_sha256，page_sizes／page_sha256改為真正發布頁。重新export後必須重新pack；不能把新manifest配舊PCK。選型／codec誤差與未解畫質見ASSET_ARCHITECTURE。

```powershell
python tools/asset_converter/check_atlas_compression.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --archive local-assets/asset_converter/complete --work .cache/atlas-block-comparison-new --report reports/atlas_block_comparison_new.json
python tools/asset_converter/cli.py export-assets "C:\SGO\Stargate Online IE HK2" --extracted-source .cache/asset_converter/source --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 13 --map 15 --report reports/atlas_block_export.json
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --assets client/assets --map 13 --map 15 --output .cache/client-release/assets/assets.pck --report reports/atlas_block_pck.json
```

比較工具只建私有候選、共用正式Height-Dense geometry與PCK，`--work`須為未用過目錄；不發布第二套Catalog或維護第二個Builder。`--archive`核對原始／已發布Frame幾何一致後，以原始P0頁重播修正前239頁基線；未指定則p0是現有發布頁，不代表修正前結果。正式證據為帶素材／測試程式hash的atlas_block_comparison_final.json。比較PASS只表示完成比較與extent gate，並非全素材codec畫質核准。空白PCK smoke現在對標記block4的頁，同時檢查Texture2D logical和get_image physical extent，並用CompressedTexture2D.load逐一核對封裝內BPTC／ASTC兩個變體的實體尺寸；不直接ResourceLoader.load內部ctex路徑，也不把ASTC metadata載入當Mac GPU測試。
