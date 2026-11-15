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

**Map15 特例**：原始 ObjectList.dat 的 15 筆 `Building_object` 記錄只寫 XY、未寫 int32 Z；固定讀 XYZ 會造成後續記錄錯位。轉換器以可驗證的下一筆路徑標頭及完整記錄計數辨識，輸出缺少 Z 的 `z=0` 視覺預設，另以 `z_source_present=false` 保留來源缺欄位事實。**Z 預設及各特殊建築的正式 DrawOrder/UI 表現仍待真 Renderer 驗證**，不得以 converter PASS 抵銷視覺 Gate。

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
