# 全 Godot 素材分類必要性稽核

日期：2026-11-14
範圍：Enemy、Player、NPC、Equipment、Item、Pet、Mecha、Map、Effect、UI、Ground Drop，以及所有 Builder / Bundle / Loader / Cache。
狀態：**Production policy / Before-After evidence recorded**。AS005 原版逐格人工視覺與 AS008 發布權仍是獨立 Gate。

## 結論

SGTest 不以素材種類、檔名、方向、動作、圖片尺寸或原始來源檔建立 Runtime 類型。

正式模型只保留：

1. **Normalized definition**：保存「這是什麼」；每個 entity 的 action / facing / mirror / pivot / timeline / layer 可獨立查閱。
2. **Lifecycle bundle**：保存「哪些素材實際一起載入、一起卸載」。
3. **RuntimeAssetStore**：保存「如何共用、引用計數、async decode / GPU commit、bounded cache、確實卸載」。
4. **EntityAtlas**：單一 sprite manifest reader；不知道 Enemy / NPC / Item 等業務分類。

Builder 可以依同一 workload 的量測選 512 / 1024 / 2048 / 4096 或多頁；**Atlas side 是 packing 參數，不是 Runtime asset type**。每個怪物仍能逐動作、逐方向查閱，不要求一怪一圖。

## 分類必要性矩陣

| 現有／可能概念 | 判定 | Production 決策 |
|---|---|---|
| normalized animation definition | **必需** | 保留每 entity 的 action/facing/mirror/pivot/timeline/layer |
| EntityAtlas | **必需** | 唯一 sprite manifest reader |
| RuntimeAssetStore | **必需** | 唯一 async decode/GPU commit/owner/cache store |
| Map lifecycle sprite bundle | **有實測收益** | Map13 monster + scenery 同 bundle；切圖一起 release |
| Session player-base bundle | **有實測收益** | body/leg/head/hair00/arms/death 同 session lifetime |
| Terrain texture + viewport MultiMesh | **必需** | 地圖地磚有不同表示法，不另建 Atlas Loader |
| Ground Drop MultiMesh | **必需（目前 renderer）** | 現階段沒有原版 Item texture consumer，不預建 Item/Drop Atlas 類型 |
| per-enemy Atlas / Loader | **冗餘** | 同 map 共現；已移除 production 分組 |
| per-player-part Atlas / Loader | **冗餘** | base parts 同 session lifetime；已移除 |
| per-map-object Atlas / viewport owner | **冗餘（現 Map13）** | 45 種 scenery 已併入 map lifecycle bundle |
| Direction / Action bundle | **僅供研究** | 是動畫語意，不是 unload boundary |
| Hot / Warm / Cold / Boss / Rare | **僅供研究 heuristic** | 沒有真實 lifetime + A/B 不得變 Runtime type |
| readable / grid / dense / source-locality | **僅供研究 packing** | Builder 內部候選，不暴露 Runtime |
| 512 / 1024 / 2048 / 4096 類型 | **冗餘** | 只作 Builder 候選參數 |
| copy_support_assets 全量鏡像 | **僅供研究；production 冗餘** | 不再由正式 rebuild 產生 |
| NPC / Equipment / Item / Pet / Mecha / Effect 專屬 Loader | **尚未需要；預建即冗餘** | 功能實作時先沿用同一 definition/bundle/store |
| Legacy Interface 全量 Runtime 分類 | **尚未需要；預建即冗餘** | 目前 UI 是 Godot Control；原版 UI 素材仍可在研究工具查閱 |
| Audio 全量 Runtime 鏡像 | **尚未需要；預建即冗餘** | 原始資料留 .cache；有 consumer 後按實際 lifetime 導入 |

## 「可獨立查閱」不等於「獨立圖片」

Production bundle manifest 的每個 normalized entity 都保留：

- `entity`
- `definition_sha256`
- `inspect[action][facing] -> logical requested clip`
- `physical_clips`

例如 Enemy 可直接查 `FA / ND / NH / NS / NW` 的 facing 1～8；requested logical view 再指向 normalized physical clip + `flip_h` + timeline。Viewer／測試不需要知道它最後在第幾張 PNG、第幾頁。

因此：

**Debug / Viewer 的查閱粒度 = entity / action / facing。**
**Runtime 的卸載粒度 = 實測 lifecycle bundle。**
兩者不綁定。

## Runtime asset root 清理 Before / After

完整 production rebuild 以前，`client/assets` 實際包含大量尚未被 Runtime 讀取的全量轉檔輸出：

- Before：**17,274 files / 1,179.31 MiB**
- 類別包含 Player、Enemy、data、Map、MA、MS、SR、Effect、Interface、NPC、audio/images 等。

Client Runtime 引用盤點對 `audio/ images/ interface/ effect/ npc/ ma/ ms/ sr/ data/` 的實際路徑 consumer 為 **0**；`ms/` 唯一文字命中只是註解，不是路徑。

正式 rebuild 改成先清空可重建的 `client/assets`（保留 `.gdignore`），只輸出目前 Runtime 真的讀取的 `bundles/` + `maps/`：

- After：**12 files / 9.036 MiB**
- 檔案數減少約 **99.93%**
- 磁碟量減少約 **99.23%**
- `tools/summarize_assets.py` 驗證 `unexpected_top_level=[]`、`production_clean=true`

原始素材沒有刪除；仍在 `.cache/client`，研究／Viewer 可讀。這是停止「把整套原版資料複製進 Godot Runtime root」，不是丟失素材能力。

## Map13 scenery 分組 A/B

### 舊 viewport 細拆

Map13：

- 367 placed objects
- 45 unique object atlases
- 全部 object decoded RGBA：約 **7.222 MiB**
- 1440 最大視窗 + 180px preload margin 的採樣：
  - P50：21 atlases / **4.394 MiB**
  - P95：30 atlases / **5.414 MiB**
  - Max：32 atlases / **5.813 MiB**

也就是常態為省約 1.8～2.8MiB object resident，需要管理 21～30 個 manifest/acquire/release/owner。

### 合併 monster + scenery 候選

Map13 9 monster + 45 scenery raw clips：

| Side | Pages | RGBA bytes |
|---:|---:|---:|
| 512 | invalid | 885×711 frame 超過 side |
| 1024 | 5 | 18,572,476 |
| **2048** | **2** | **18,569,428** |
| 4096 | 1 | 18,478,500 |

2048 與 4096 lifecycle 20-cycle：

| 指標 | 2048×2 | 4096×1 |
|---|---:|---:|
| Load P95 | 100.107ms | 100.031ms |
| Frame P95 | 16.749ms | 16.729ms |
| Frame P99 | 16.790ms | 16.764ms |
| Upload page P95 | 2.448ms | 2.258ms |
| Static/decode peak | 36,911,847 | **53,543,643** |
| Texture peak | 35,146,819 | 35,023,371 |
| Video peak | 41,557,451 | 41,434,003 |

Synthetic 300 draw 三輪：

- 2048×2：draw P95 **206**；frame P95 約 **7.02～7.05ms**
- 4096×1：draw P95 **73**；frame P95 約 **7.76～8.01ms**

4096 少一頁，但沒有 load/frame 勝利，反而 transient decode/static workspace 多約 16MiB。因此 auto mode 優先 `<=2048`；4096 仍會被量測，也可研究強制，但只有 2048 無法容納 atomic source block 時才自動 fallback。

### 正式 20 Client / 300 Monster / 1000 Drop / 40s

Before（monster bundle + per-object viewport atlases）對 After（monster + scenery shared map bundle）：

| 指標 | Before | After |
|---|---:|---:|
| Mean FPS | 59.996 | **59.999** |
| Draw P95 | 236 | **228** |
| Frame P95 | 19.236ms | **18.947ms** |
| Frame P99 | 20.422ms | **20.322ms** |
| Static memory monitor | 69,419,012 | **68,961,657** |
| Texture memory monitor | **39,057,403** | 46,035,499 |
| Video memory monitor | **45,557,371** | 52,534,347 |
| 20-client process RSS peak | **2,893.73 MiB** | 3,023.27 MiB |
| 約每 Client RSS peak | **144.69 MiB** | 151.16 MiB |

所以這不是「RAM/VRAM 最佳化」；它是約 **+6～7MiB/client resident** 的明確 trade-off，換來：

- 45 個 per-object manifests/pages 不再是 production dependency
- viewport 不再每次管理 21～30 個 texture owners
- clean rebuild 不再依賴 stale `map/*.json`
- draw/frame 小幅改善
- map lifetime 更單純、好維護

30-cycle async lifecycle：

- `all_texture_handles_released=true`
- `engine_texture_recovery_passed=true`
- `store_resident_recovery_passed=true`
- frame P95/P99 **16.824 / 16.906ms**

增加的 resident 在離開 map 後可完整釋放，沒有量到 leak。基於「沒有足夠收益不要增加分組」，Map13 保留 shared map bundle。

## 全素材新增規則

新增 Enemy、NPC、Equipment、Item、Pet、Mecha、Map object、Effect、UI 或 Drop 素材時，Builder 依序回答：

1. 是否通常同時使用？
2. 是否相同載入／卸載時機？
3. 拆開後是否真的能釋放可觀的 RAM / texture / video memory？
4. 合併後是否造成 load P95/P99、GPU upload、frame P95/P99 的可觀退化？
5. 拆分是否在相同 workload 有可量測收益？
6. 新增／修改素材是否仍能只改資料，不改 Runtime core？

沒有證據時，沿用現有 bundle boundary。

任何新 boundary 必須附：

- Before / After 同 workload
- CPU decode
- GPU upload
- draw calls
- RAM / process RSS
- Godot texture/video monitor；若有 driver VRAM 則另列
- 首次載入、切圖、卸載
- cache hit / resident recovery
- frame P95/P99
- 長時間 lifecycle / soak

沒有同 workload Before／After，只能寫「候選／研究」，不能宣稱最佳化完成。

## Builder 離線成本

`build_maps.py` 改成在單次 map build 內只 decode 每個 unique scenery SPR 一次；這只是函式內 local cache，不是新的 Runtime Cache／Loader 類型。相同 Map13 原始來源與相同 fingerprint `3827af...03d9`：

- Before（367 placements 重複 decode）：約 **3.65～3.78s**
- After（45 unique SPR decode）：**0.715～0.717s**

約 80% build-time 改善，輸出 fingerprint 與 pixel round-trip 不變。

## 工具邊界

`tools/README.md` 定義：

- Production：`rebuild_assets.ps1`、`asset_pipeline.py`、`asset_bundle_builder.py`、`build_maps.py`、`export_server_content.py`
- Research / Experiment：舊 atlas builders、全量 support exporter、尺寸候選等
- Benchmark：`benchmark_*`
- Viewer / Diagnostic：viewer、audit、inspect、report、summarize
- Test：production contract 與 research regression 分開

不為每個素材類別建立資料夾層級、Builder、CLI 或 Loader。

## Gate

這次架構簡化不代表：

- AS005 原版同版本逐格人工視覺已完成
- AS008 原版素材公開／發行授權已完成
- NPC / Equipment / Item / Pet / Mecha / Effect / Legacy UI 已實作

這些仍是各自功能／證據 Gate。現在的規則只是確保未來導入時，**資料增加不等於 Runtime 分類增加**。
