# HK2 Enemy Atomic Balanced Atlas（2026-11-14）

狀態：**BUILD-TIME CANDIDATE SELECTED / RUNTIME STREAMING NOT VERIFIED**。本文件只記錄本機真 HK2 研究與 Godot GPU A/B；原始/衍生媒體仍在 gitignore 的 `local-assets/`，不提交公開 Git。

## 目標

怪物素材同時滿足：

- 原版每個 SPR 內的同一方向/動作影格不得因 atlas packing 任意跨頁；ACT 方位重用與水平鏡像只保留 metadata，不複製像素。
- 不把 50 種怪物全部常駐；runtime 仍由 MapAssetPalette/可見種類決定工作集。
- 在 512/1024/2048/4096 中依實測選尺寸，而不是用怪物外觀大小猜。
- 控制 RAM/VRAM、頁數、load spike、draw calls；UI/render FPS 不影響 network/state。

## Before

原 dense mixed-size（50 組）：
- 133 pages
- 432,604,792 RGBA bytes
- 2,000 action×facing 中 173 組跨頁

第一版 per-SPR locality：
- 2000/2000 action×facing 單頁
- 但每原 SPR 幾乎獨立成頁，頁面/texture bind 過多

atomic MaxRects v3 + 舊尺寸計畫：
- 119 pages
- 466,182,124 RGBA bytes
- 2000/2000 action×facing 單頁

## 新增 measured atomic size planner

`tools/plan_enemy_atomic_sizes.py` 直接從乾淨 HK2 解碼每組 Enemy SPR，在記憶體中對 512/1024/2048/4096 執行 atomic block packing。候選必須讓每個原 SPR 保持單一 atomic block；先找最低 RGBA，再允許預設 5% 記憶體 overhead 換較少頁數。

50 組結果：
- 512：1 組
- 1024：21 組
- 2048：19 組
- 4096：9 組
- 65 pages
- 442,769,264 RGBA bytes
- 69,437,691 PNG bytes
- 2000/2000 action×facing 單頁
- Godot catalog probe：50 groups / 250 actions / 2,000 directions / 12,976 steps PASS

相對 dense，離線全 catalog RGBA 約 +2.35%，但頁數 133→65，並消除全部 action-facing 跨頁。此 442 MB 是**全 catalog 離線總量**，不是 runtime 常駐設計。

## RTX 3050 / Godot 4.7.2 / 1440×900 A/B

每方案交替順序跑 3 回合，300 怪；結果取中位。測試工具：`tools/run_enemy_render_ab.py` + `tools/benchmark_enemy_render.gd`。

### 8 種怪

| 指標 | dense | atomic balanced |
| --- | ---: | ---: |
| pages | 12 | 9 |
| RGBA bytes | 50,782,352 | 51,088,648 |
| load median ms | 231.668 | 245.835 |
| frame p95 ms | 7.982 | 7.991 |
| draw calls p95 | 286 | 275 |
| Video Mem | 86,391,303 | 86,804,943 |

frame 差異不顯著；atomic balanced 的 pages/draw calls 較少，Video Mem 約 +0.4 MB。

### 50 種怪壓力

| 指標 | dense | atomic balanced |
| --- | ---: | ---: |
| pages | 133 | 65 |
| RGBA bytes | 432,604,792 | 442,769,264 |
| load median ms | 1,281.023 | 1,289.890 |
| frame p95 ms | 7.924 | 7.110 |
| draw calls p95 | 372 | 372 |
| Video Mem | 595,191,887 | 608,852,731 |

不能用三回合把 frame p95 的差值宣稱成普遍加速；可確認的是 draw calls 沒退化，頁數減半，Video Mem 約 +13.7 MB。正式 runtime 不會一次載入 50 種，仍以 MapAssetPalette 為準。

## 決策

1. Enemy build pipeline 採 **atomic source blocks + measured multi-size plan** 作完整 catalog 候選。
2. Runtime **不以完整 catalog 常駐**；MapAssetPalette 仍只載入目前地圖/鄰區需要的 visual。
3. `source_spr_atomic_maxrects_v3` 保留為 packing contract；logical facing 鏡像不生成重複圖。
4. 每次素材版本變動重新跑 planner；不硬編碼 EN 編號或尺寸。
5. Player 紙娃娃、ground item、UI 不直接繼承此策略，各自 A/B。

## 可重跑

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
py tools/plan_enemy_atomic_sizes.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-complete-groups.txt local-assets/enemy-atomic-size-plan.json --memory-overhead 0.05
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-atomic-balanced --group-list local-assets/enemy-complete-groups.txt --size-plan local-assets/enemy-atomic-size-plan.json --source-locality
py tools/compile_enemy_atlas_catalog.py local-assets/enemy-animation-audit.json local-assets/enemy-atomic-balanced local-assets/enemy-atomic-balanced/enemy_catalog.json
py tools/audit_action_page_locality.py local-assets/enemy-atomic-balanced/enemy_catalog.json --require-single-page
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- local-assets/enemy-atomic-balanced/enemy_catalog.json
```

GPU raw reports 留在 gitignore 的 `reports/enemy-render-ab-balanced-*.json`。正式 StreamingManager、GPU upload p99、切圖/登出回收、人物紙娃娃與原版逐格視覺仍需後續 Gate。
