# RuntimeAssetStore — ASSET204

日期：2026-11-14。狀態：**ASSET204 PASS / RUNTIME206 MATRIX STILL NOT VERIFIED**。

## 目的

ASSET203 已證明 `normalized_memory` 的 Atlas packing 本身無法消除同步載入尖峰：Map13 9 visuals 的同步 loader frame P99 為 53.52ms。ASSET204 不再改動畫語意或降低同步頻率，而是把資源生命週期改成：背景 CPU decode + 有界 main-thread GPU commit + owner/epoch/cache 管理。

## Runtime 架構

`client/scripts/runtime_asset_store.gd` 是 Client 共用的 texture/atlas store：

- 一個 worker thread 負責 manifest/PNG CPU decode。
- Godot `ImageTexture.create_from_image()` 只在 main thread 執行。
- production `main.gd` 每 frame 呼叫 `poll_gpu(1, 1200)`：最多 1 page，目標 GPU submit budget 1.2ms。
- decoded ready queue 有 byte budget；單張較大 page 可在 queue 空時通過，避免 deadlock。
- manifest page 單張上限 64MiB；job 上限 128。
- 每個 request 有 token；owner 有 epoch。owner generation 被 release 後，舊 worker result 即使晚到也不能 resurrect atlas。
- ready atlas 可被多 owner 共用，同一 path 不重複 decode/upload。
- unowned ready atlas 是 LRU cache；超過 cache budget 自動 eviction。
- failed 且已無 owner 的 entry 立即移除，下一 generation 可 retry，不會永久吃掉 MAX_JOBS。
- `close()` 釋放所有 owner、force purge、清空 decoded queue 並 join worker。

正式 `main.gd` 設定：

- production unowned cache budget：24MiB；ready/decode queue budget 16MiB。Map/session active owners 不受 unowned LRU budget 強制驅逐。
- Map13 九種 monster 現在共用一個 `map:13:sprites` lifecycle bundle；definition 只保留 prefix/stats，不再產生九個 texture owners。
- player base（6 paper-doll parts + death）共用一個 `session:player:b:base` bundle；headless client 不載純視覺 paper doll。
- map objects 已與 monster 共用 `map:13:sprites`；viewport 只做 draw cull，不再建立 21～30 個 per-view texture owners。Map13 A/B 顯示細拆只省約 1.8～2.8MiB object resident，代價不足以維持另一套 cache/lifetime。

## Owner / lifecycle

### Monster palette

`monster_visuals.begin_async()` 每次 map generation 先 release 舊 owner、清除 definition→prefix 與 manifest strong refs，再取得新 epoch。Map13 9 definitions 全部指向同一 map manifest，所以 Store 只有一個 atlas entry；切圖途中尚未完成的舊 job 仍由 token/epoch cancel。

### Map objects

`map_objects.configure(..., store, owner)` 只 acquire `world.sprite_bundle` 一次；Map13 scenery 與 monsters 指向同一 manifest/path，因此 RuntimeAssetStore 共用同一 atlas entry。`prepare(view)` 只更新可視 object draw list，不變更 texture ownership；切圖 reset 釋放 map owner。

### Player paper doll

`paper_doll.begin_async()` 只 acquire 一個 session base manifest；body/leg/head/hair00/arms/death 是同一 EntityAtlas 裡的 logical prefixes。z-order 直接由 normalized `draw_layers/draw_slots` 取得，不再另讀第二套 draw-order Runtime 表。登入/Client 結束時 release owner；Store close 會回收。

## correctness tests

Godot synthetic/real-private-asset tests：

- `test_runtime_asset_store.gd`：owner sharing、cache hit、force purge、loading cancellation、failed-path retry、automatic cache-cap eviction。
- `test_monster_visuals_async.gd`：Map13 9 definitions / 360 logical views / **1 map bundle**、generation clear/cancel、owner purge。
- `test_map_objects_async.gd`：單一 map bundle acquire、viewport draw cull 不改 ownership、map reset 後完整 purge。
- `test_paper_doll_async.gd`：**1 session bundle**、6 parts + death、normalized draw-order、purge。

## Lifecycle-bundle 簡化後補充（2026-11-14）

Production 已由多個 per-entity/per-part/per-map-object manifests 收斂成 map/session lifecycle bundles；完整理由見 [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md) 與 [ASSET_NECESSITY_AUDIT.md](ASSET_NECESSITY_AUDIT.md)。目前 Map13 monster + scenery 共用 2048×2 map bundle。After 的 30-cycle async lifecycle：frame P95/P99 **16.824/16.906ms**，texture handles、engine texture、Store resident recovery 全 PASS。20client/300monster/1000drop/40s：**59.999FPS**、frame P95/P99 **18.947/20.322ms**、draw P95 **228**、execution PASS；代價是 texture/video monitor 約增加 7MiB，並已明確記錄為 lifecycle/維護性 trade-off，不宣稱 RAM/VRAM 最佳化。

以下 ASSET204 數字保留為「簡化前 normalized_memory 14-page」歷史基準，用來證明 bounded async store 本身的改善。

## 30-cycle lifecycle A/B

候選：ASSET203 selected `normalized_memory`，14 pages / 10,741,992 RGBA bytes。RTX 3050、1440×900、60FPS cap、warm OS cache。

| 指標 | 同步 loader（ASSET203） | RuntimeAssetStore async（final） |
|---|---:|---:|
| Frame P95 | — | **16.885ms** |
| Frame P99 | **53.52ms FAIL** | **17.028ms PASS** |
| Frame max | — | 17.309ms |
| Load wall P50 | — | 249.003ms |
| Load wall P95 | — | 249.914ms |
| Load frames P95 | — | 15 frames |
| GPU commit P95 | main-thread spike path | **1.205ms** |
| Upload submit P95 | — | 0.904ms |
| Worker decode P95 | main-thread decode | 6.409ms |
| Peak Store resident | — | 10.24MiB |
| Peak decoded ready queue | — | 9.02MiB |
| Process RSS peak | — | 164.55MiB |

30 measured cycles：

- `all_texture_handles_released=true`。
- `engine_texture_recovery_passed=true`。
- `store_resident_recovery_passed=true`；final resident=0。
- RAM recovery PASS；last10-first10 median growth約 1.74MiB。
- 448 decoded pages / 448 uploaded pages。
- `source_files_unchanged_during_run=true`。

這個結果不是「載入變成 0ms」；總 wall 約 249ms，刻意攤到約 15 個 frame，避免玩家吃 50ms 級單幀停頓。

## 20-client / 300-monster / 1000-drop

selected `normalized_memory` + production async store，40秒，profile 35秒：

- execution PASS，provenance unchanged。
- mean FPS 59.99。
- frame P95/P99 **22.109 / 23.949ms**，max 28.554ms。
- draw-call P95 422。
- texture memory約 39.44MiB；video memory約 45.64MiB。
- 20/20 `snapshot_received`、20/20 `prediction_ready`、0 ERROR / SCRIPT ERROR。
- prediction `max_correction_px=0`，observed max pending=4。
- monster palette 9 shared atlases / 10.24MiB；與 player core 同 Store 時 native client resident約 **18.57MiB**（19,473,444 bytes）。
- palette async load wall約 378ms；load 期間 network/state 仍由 physics/network 路徑消化。
- Server 大量 client 進場 warm-up tick=300 P95/P99 33.02/69.08ms；穩態 tick=600/900/1200 P99 6.43/5.56/6.31ms。

## 邊界 / 尚未宣稱

- 實體 driver VRAM 仍 NOT VERIFIED；目前使用 Godot texture/video monitor。
- cold-disk cache 尚未做完整重複矩陣；上述 lifecycle 是 warm OS cache。
- 正式 login/logout UI 尚未存在；Store `release_owner/purge/close` 與 map-generation lifecycle 已測。未來 logout flow 必須呼叫同一契約，不得自行保留 atlas refs。
- 30/45/60/75/90 FPS、5/10/15 low-power、unfocus/minimize/restore、1/2/4/8 開的 network/state/render trace 屬 RUNTIME206，ASSET204 不因此宣稱完成。
- AS005 原版同版本人工逐格視覺與 AS008 發布權仍未完成。

## 重跑

```powershell
cd C:\SGO\SRC\SGTest

# Store/component tests
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_runtime_asset_store.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_monster_visuals_async.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_map_objects_async.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_paper_doll_async.gd

# 30-cycle async lifecycle
py tools/benchmark_runtime_baseline.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --case lifecycle --loader async --cycles 30 --monster-assets reports/normalized_atlas_candidates/normalized_memory --output reports/runtime_asset204_final

# Multiplayer Gate
py tools/benchmark_runtime_baseline.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --case multiplayer --clients 20 --monsters 300 --drops 1000 --seconds 40 --monster-assets reports/normalized_atlas_candidates/normalized_memory --output reports/runtime_asset204_final
```
