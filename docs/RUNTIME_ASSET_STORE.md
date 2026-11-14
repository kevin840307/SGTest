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

- cache budget：32MiB。
- ready/decode queue budget：16MiB。
- Map13 monster 依 spawn count 作 priority preload。
- player core（6 paper-doll parts + death）priority preload；headless client 不載純視覺 paper doll。
- map objects 依目前 viewport on-demand acquire，離開 viewport 後移除 local strong ref，交回 Store 做 bounded LRU。

## Owner / lifecycle

### Monster palette

`monster_visuals.begin_async()` 每次 map generation 先 release 舊 owner、清除 definitions/shared strong refs，再取得新 epoch。切圖途中尚未完成的舊 job 會被 token/epoch cancel；測試涵蓋 9 visuals loading 中直接切 generation，只留下新 generation。

### Map objects

`map_objects.configure(..., store, owner)` 建立 map owner；`prepare(view)` 只 pin 可視 atlas。離開 view 後 release 該 path owner，Store 可保留成 LRU；切圖 reset 會釋放 map owner。

### Player paper doll

`paper_doll.begin_async()` 一次 acquire 6 core parts + death，全部 ready 後才原子替換 `parts`；因此不會出現只載半身的中間 frame。登入/Client 結束時 release owner；Store close 會回收。

## correctness tests

Godot synthetic/real-private-asset tests：

- `test_runtime_asset_store.gd`：owner sharing、cache hit、force purge、loading cancellation、failed-path retry、automatic cache-cap eviction。
- `test_monster_visuals_async.gd`：Map13 9 visuals / 360 logical views、map generation clear/cancel、owner purge。
- `test_map_objects_async.gd`：viewport acquire、離開 viewport local release、LRU purge。
- `test_paper_doll_async.gd`：7 atlases、6 parts、draw-order、purge。

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
