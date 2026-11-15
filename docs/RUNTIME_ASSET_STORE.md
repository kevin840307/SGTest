# RuntimeAssetStore — ASSET204

> **2026-11-15 CURRENT POLICY:** RuntimeAssetStore 現在管理 per-asset manifests；Map/Monster/scenery/Player parts 不再 physical merge。下方所有舊 `map/session bundle` 數據只保留作歷史效能證據，現行邊界以 [ASSET_ARCHITECTURE](ASSET_ARCHITECTURE.md) 為準。

日期：2026-11-14，2026-11-15補PERF002/003/ASSET211。ASSET204原Gate已通過；RUNTIME206完整隔離矩陣的獨立證據見 [RUNTIME_ISOLATION_VERIFICATION](RUNTIME_ISOLATION_VERIFICATION.md)，本次未重跑該矩陣。

## 目的

ASSET203 已證明 `normalized_memory` 的 Atlas packing 本身無法消除同步載入尖峰：Map13 9 visuals 的同步 loader frame P99 為 53.52ms。ASSET204 不再改動畫語意或降低同步頻率，而是把資源生命週期改成：背景 CPU decode + 有界 main-thread GPU commit + owner/epoch/cache 管理。

## Runtime 架構

`client/scripts/runtime_asset_store.gd` 是 Client 共用的 texture/atlas store：

- 一個 worker thread 負責 manifest/PNG CPU decode。
- Godot `ImageTexture.create_from_image()` 只在 main thread 執行。
- production `main.gd` 每 frame 呼叫 `poll_gpu(1, 1200)`：最多 1 page，目標 GPU submit budget 1.2ms。
- decoded ready queue 有 byte budget及128個item上限（metadata／error也計數）；單張較大page可在queue空時通過，避免deadlock。另有一個worker中的in-flight page，byte budget不是整個程序RAM上限。
- manifest page 單張上限 64MiB；job 上限 128。
- 每個request有token；owner有epoch。最後owner取消loading時，立即移除queued job／ready events／active token；正在進行的PNG decode只能在page邊界停止，但結果不能重新入queue或提交GPU。部分GPU頁及failed atlas也扣回resident bytes並清除引用。重現與回歸見 [PERF003_CANCELLATION](PERF003_CANCELLATION.md)。
- ready atlas 可被多 owner 共用，同一 path 不重複 decode/upload。
- unowned ready atlas 是 LRU cache；超過 cache budget 自動 eviction。
- entry數也受128上限約束；新增path遇到滿額時，以同一LRU規則先騰一格，即使bytes未超標。只淘汰無owner的ready atlas；全部仍被引用時回傳ERR_BUSY，釋放後可恢復。PERF004自製fixture在512KiB／128項已重現並驗證修正。
- failed 且已無 owner 的 entry 立即移除，下一 generation 可 retry，不會永久吃掉 MAX_JOBS。
- `close()` 釋放所有 owner、force purge、清空 decoded queue 並 join worker。

正式 `main.gd` 設定：

- production unowned cache budget：24MiB；ready/decode queue budget 16MiB。active owners 不受 unowned LRU budget 強制驅逐。
- 每個 Monster visual 使用自己的 `monsters/<sprite>/atlas.json`；同 sprite 的多個 definition/instance 共用相同 path entry。
- Player B base/death 是 7 個獨立 manifests，但可共用一個 character owner；headless client 不載純視覺 paper doll。
- Map scenery 每個來源 visual 獨立，`map_objects` 只對 viewport 需要的 paths acquire；超出視野一段時間後 release，仍由同一 Store/LRU 控制。
- PCK 只是磁碟容器，不改變 per-asset cache entry。

## Owner / lifecycle

### Monster palette

`monster_visuals.begin_async()` 每次 map generation 先 release 舊 owner、清除 definition→path refs，再取得新 epoch。每個不同 sprite path 是獨立 Store entry；同一 sprite 的多個 definition/instance 只共用同一 entry。切圖途中尚未完成的舊 job 仍由 token/epoch cancel。

### Map objects

`map_objects.configure(..., store, owner)` 讀 `world.objects[].atlas` 的獨立 scenery paths。`prepare(view)` 只 acquire 可視／預取範圍需要的 assets；離開視野超過 release timeout 後釋放該 path。不同 scenery 不共用 physical atlas，但仍共用同一 Store worker/cache。

### Player paper doll

`paper_doll.begin_async()` acquire body/leg/head/hair/left-arm/right-arm/death 七個獨立 manifests；它們共用 character owner epoch。z-order 仍由 normalized `draw_layers/draw_slots` 取得。登入/Client 結束時 release owner；Store close 會回收。

## correctness tests

Godot synthetic/real-private-asset tests：

- `test_runtime_asset_store.gd`：owner sharing、cache hit、force purge、loading cancellation、failed-path retry、automatic cache-cap eviction。
- `test_runtime_asset_cancellation.gd`：1000 generations、MAX_JOBS、過期publish、priority、zero-byte事件數上限、byte backpressure取消、close／restart。`probe_asset_cancel_queue.gd -- --assert-bounded`另驗證部分提交／後頁失敗／相同path重試及native Texture弱引用歸零。
- `test_runtime_asset_cache_pressure.gd`：128個真小Texture低於byte budget時，第129個path可按LRU騰一格；保護pinned／最近使用項目、所有項目有owner時維持ERR_BUSY、釋放一項後恢復；headless及native均PASS。Before/After在`reports/perf004_*.json`。
- `test_monster_visuals.gd`：Map13 9 definitions 指向獨立 monster assets；同 sprite reuse、360 logical views PASS。
- `test_map_objects_async.gd`：獨立 scenery assets、viewport demand、shared Store、reset/purge PASS。
- `test_paper_doll_async.gd`：7 independent base/death manifests、normalized draw-order、shared owner、purge PASS。
- `test_runtime_asset_cache_pressure.gd`：128 independent entries、LRU/pinned/recent/backpressure/recovery，final resident=0。

## 歷史：Lifecycle-bundle 實驗（2026-11-14，**SUPERSEDED**）

以下資料只保留歷史 Before/After；2026-11-15 已由 per-asset architecture 取代。當時曾由多個 per-entity/per-part/per-map-object manifests 收斂成 map/session lifecycle bundles；完整理由見 [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md) 與 [ASSET_NECESSITY_AUDIT.md](ASSET_NECESSITY_AUDIT.md)。早期2048×2 map bundle數據保留在該文件。2026-11-15 PERF002改為Runtime優先512／1024，Map13為1024候選5頁、session2頁，ownership仍不變。修正worker idle持有Image／metadata，telemetry各有2048筆上限、idle不追加，`statistics(false)`不複製樣本。正式release峰值187.09MiB，30次map載卸frame P95/P99 16.822/16.971ms、回收PASS；見 [PERF002_MEMORY](PERF002_MEMORY.md)。取消／部分載入錯誤邊界另由PERF003自製fixture與native probe實證修正，不能單以串行30次載卸證明。

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
