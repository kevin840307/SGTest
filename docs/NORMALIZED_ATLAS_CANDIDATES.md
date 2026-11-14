# ASSET202/203 — Normalized Clip-Atomic Atlas 候選與選型

日期：2026-11-14。狀態：**ASSET202 PASS / ASSET203 PASS / ASSET204 REQUIRED**。

## 核心原則

Atlas 不再決定動畫語意。所有 Map13 monster 的 logical facing、source clip、flip、pivot、ACT timeline 都來自 `sgtest.animation` schema v2；packing 只改 page/rect。

這解決舊 runtime 的一個實際風險：如果 `NS2.spr` 實體檔存在，但 reference-normalized mapping 要 logical facing 2 重用 `NS1`，舊 manifest 的 direct clip 會凌駕 alias。新 builder 只暴露 v2 真正使用的 canonical physical clips，logical requested clip 由 v2 編譯的 alias 表解析，並在寫出 manifest 前模擬 `EntityAtlas` resolution，360/360 logical facings 必須一致，否則 fail closed。

## Builder

工具：`tools/build_normalized_atlas_candidates.py`

- source：`.cache/client` 私有原始 SPR。
- semantics：`reports/normalized` 的 ASSET201 v2 definitions。
- workload：`client/assets/generated/maps/13/population.json`。
- 每個 physical source clip（原 SPR）保持 atomic；不同 clip block 可共同放入一頁。
- 512 / 1024 / 2048 / 4096 全部實際 pack 後再決定，不以怪物顯示尺寸猜測。
- Map13 78 個 spawn 依累積出現比例分：hot 6 visual、warm 2、cold 1。
- 所有衍生 PNG / JSON 留在 gitignore 的 `reports/normalized_atlas_candidates/`。

產生 7 組候選：

| Candidate | Pages | RGBA bytes | 約 MiB | 說明 |
|---|---:|---:|---:|---|
| normalized_512 | 16 | 11,007,424 | 10.50 | 512 preferred，不足才升級 |
| normalized_1024 | 9 | 11,135,572 | 10.62 | 1024 preferred |
| normalized_2048 | 9 | 12,039,176 | 11.48 | 2048 preferred |
| normalized_4096 | 9 | 12,403,128 | 11.83 | 4096 preferred |
| normalized_memory | 14 | 10,741,992 | 10.24 | 每 entity 最低 RGBA |
| normalized_balanced | 10 | 10,820,716 | 10.32 | hot 可用 8%、warm 3% 記憶體換少頁 |
| normalized_pages | 9 | 10,917,764 | 10.41 | 最少頁優先 |

這些數字是 Map13 九種怪的離線工作集，不是整個 Client RAM / VRAM。

## PERF001 lifecycle

所有候選先做 30 次同步 load/unload。第一輪發現有一個早期 Godot assertion Session 殘留，因此第一輪只保留為趨勢，不作正式選型。清理到系統沒有任何 `Godot_v4.7.2` 測試程序後，對 finalists 重新執行完整 lifecycle + multiplayer。

乾淨 finalists：

| Candidate | Load P50/P95 ms | Decode P95 ms | Submit P95 ms | Frame P99 ms | RSS peak MiB | Texture release | RAM recovery |
|---|---:|---:|---:|---:|---:|---|---|
| normalized_1024 | 38.39 / 40.65 | 6.35 | 0.290 | 43.61 | 158.38 | PASS | PASS |
| normalized_memory | **36.88 / 40.35** | **6.08** | **0.162** | 53.52 | 157.82 | PASS | PASS |
| normalized_balanced | 38.39 / 41.30 | 6.22 | 0.245 | 53.26 | **157.77** | PASS | PASS |

三組的同步 loader frame P99 都高於 40ms，因此 **packing alone FAIL**。這不是 Atlas 選錯就能消失的問題；ASSET204 必須使用 background decode + bounded main-thread GPU commit，不可再同步整批 `Image.load + ImageTexture + force_sync`。

## PERF001 multiplayer

相同條件：
- 1 C#/.NET 8 Server
- 20 Client（1 rendered + 19 headless）
- Map13
- 300 monsters
- 1000 drops
- 1440×900
- 60 FPS render cap
- 40 秒，render profile 35 秒
- 不降低 Server tick、snapshot、Client command/network rate

乾淨結果：

| Candidate | Frame P95/P99 ms | Draw calls P95 | Texture MiB | Video MiB | Mean FPS |
|---|---:|---:|---:|---:|---:|
| normalized_1024 | 23.74 / 26.03 | 441 | 39.94 | 48.17 | 59.99 |
| normalized_memory | **21.54 / 23.81** | **421** | **39.44** | **45.63** | 60.00 |
| normalized_balanced | 22.29 / 24.33 | 439 | 39.54 | 45.74 | 60.00 |

三組 multiplayer frame Gate 都 PASS。

## ASSET203 決策

**選 `normalized_memory` 作 ASSET204 的 provisional runtime base。**

理由：
1. 360 logical facings 由 v2 definition 驗證，不靠 filename alias 猜測。
2. 三個 clean finalists 中 RGBA 最低。
3. clean multiplayer 的 frame P95/P99、draw calls、texture/video memory 都最好。
4. 30 次 texture handle release、engine texture recovery、process RAM recovery PASS。
5. 14 頁雖多於 1024 的 9 頁，但實際多人 draw calls 沒有更差；相反 P95 更低。
6. 對 ASSET204 而言較細頁面也提供更細的 owner/eviction 粒度。

ASSET204 已完成 bounded async RuntimeAssetStore 並重跑同一 Gate：`normalized_memory` 30-cycle frame P99 由同步 **53.52ms FAIL** 降至 async **17.028ms PASS**，texture/resident/RAM recovery 全 PASS；20-client/300-monster/1000-drop frame P95/P99 22.109/23.949ms。正式結果與 owner/cache/generation 契約見 [RUNTIME_ASSET_STORE.md](RUNTIME_ASSET_STORE.md)。Atlas packing 本身仍不是解法，改善來自 background decode + bounded GPU commit。

## 重跑

```powershell
cd C:\SGO\SRC\SGTest

py tools/build_normalized_atlas_candidates.py --source .cache/client --definitions reports/normalized --population client/assets/generated/maps/13/population.json --output reports/normalized_atlas_candidates

py tools/benchmark_runtime_baseline.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --case lifecycle --cycles 30 --monster-assets reports/normalized_atlas_candidates/normalized_memory --output reports/runtime_asset202_clean/normalized_memory

py tools/benchmark_runtime_baseline.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --case multiplayer --clients 20 --monsters 300 --drops 1000 --seconds 40 --monster-assets reports/normalized_atlas_candidates/normalized_memory --output reports/runtime_asset202_clean/normalized_memory

py tools/report_normalized_atlas_candidates.py
```

## 後續未完成

- ASSET205：新增怪物的一鍵 definition/atlas/spawn/preview flow。
- RUNTIME206：30–90 FPS + 低功耗 5/10/15 + unfocus/minimize 的 network/state/render 隔離實機矩陣。
- AS005：原版同版本畫面逐格視覺比對。
- AS008：原媒體公開發布權。
