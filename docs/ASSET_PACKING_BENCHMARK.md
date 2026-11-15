# Asset Packing Benchmark — Map / Monster Separated

日期：2026-11-15
狀態：**CURRENT FAIR COMPARISON**

## 測試目的

比較四種 Monster physical packing。Map13、scenery、Monster 必須是分離 assets；不能再用 Map + Monster lifecycle bundle。

四種模式：

1. **Dense Single Atlas**：同一 Monster 的 frames 可自由 dense pack。
2. **Per-Source**：同一 Monster 的每個原 SPR 各自一張 texture。
3. **Strict Row**：同一 Monster 一個 atlas，每個 source SPR 佔獨立 row。
4. **Adaptive Atomic Atlas**：目前 production builder；每個 source SPR 是 atomic placement block，自動選 page side（目前 auto 優先 <=2048）。

所有模式都維持 **一隻 Monster = 一個 logical asset**；沒有跨 Monster 合圖。

## 公平性

固定 Monster IDs：

```text
EN114A EN089A EN121A EN116A EN023A EN084A
EN111A EN125A EN050C EN080A EN093A EN079A
```

情境：

- Map13 + 2 kinds / 1000 monster instances。
- Map13 + 12 kinds / 1000 monster instances。
- 1440×900。
- 180 frames，60 warmup。
- 每種模式 3 repeats。
- repeat 2 反轉 mode 執行順序。
- 每次 trial 是新的 Godot process。
- deterministic action/facing/frame trajectory。
- Map13 固定 CAMERA=(5000,2000)。
- 可視 Map scenery：16 independent visual assets / 48 placed objects。
- Map assets 固定為 17 texture pages、7,570,272 RGBA bytes。
- 每次 trial 都要求 `asset_boundary_verified=true`。

測試環境：Godot 4.7.2 OpenGL Compatibility / NVIDIA GeForce RTX 3050。

## 2 kinds / 1000 instances

| Packing | Monster pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms | Update P95 ms | Draw submit mean ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| **Dense Single** | **2** | **64.73** | **227.8** | **107.52** | **926** | 8.007 | 8.027 | **1.320** | **2.272** |
| Per-Source | 30 | 77.26 | 233.5 | 124.17 | 1099 | 8.000 | 8.046 | 1.415 | 2.332 |
| Strict Row | 2 | 123.36 | 286.5 | 185.68 | **926** | 8.016 | 8.069 | 1.436 | 2.311 |
| Adaptive Atomic | 7 | 79.77 | 240.2 | 127.55 | 1064 | **8.000** | **8.016** | 1.537 | 2.358 |

Dense vs current Adaptive：

- Monster RGBA **-18.9%**。
- Monster load median **-5.2%**。
- Texture monitor **-15.7%**。
- Draw P95 **-13.0%**。

## 12 kinds / 1000 instances

| Packing | Monster pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms | Update P95 ms | Draw submit mean ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| **Dense Single** | **12** | **179.06** | **616.7** | **259.93** | 1106 | 7.990 | 8.018 | 1.422 | 2.609 |
| Per-Source | 180 | 212.01 | 669.2 | 303.69 | 1106 | 7.933 | **7.992** | **1.414** | 2.463 |
| Strict Row | **12** | 336.03 | 818.0 | 469.19 | 1106 | 8.013 | 8.587 | 1.476 | 2.522 |
| Adaptive Atomic | 30 | 211.98 | 684.2 | 303.76 | 1106 | **7.909** | 8.016 | 1.520 | **2.422** |

Dense vs current Adaptive：

- Monster RGBA **-15.5%**。
- Monster load median **-9.9%**。
- Texture monitor **-14.4%**。
- Draw P95 相同。

Frame/update/draw-submit 的三輪 range 大量重疊，約 8ms cadence 下不能宣稱 Adaptive 有顯著 frame-time 勝利；確定且可重現的差距主要是 decoded texture area、page 數與 load time。

## 舊測試作廢項目

先前 12-kind 報告曾列：

```text
Adaptive Atomic RGBA ≈ 76.59 MiB
```

此數字不能再比較。

原因：

- Mode1/2/3 benchmark root 只有固定 12 隻 Monster。
- 舊 Adaptive root 有更多 Monster。
- 舊 benchmark 只排序 manifest 然後取前 12。
- 所以 Adaptive 實際用了**不同的 12 隻怪**。

2026-11-15 新 benchmark 明確指定相同 ID；因此本文件取代舊 12-kind packing 結論。

## 結論

在目前固定 Monster set：

- **Dense Single Atlas 是最好的 deterministic memory/load 候選。**
- Per-Source texture/page 太多；2-kind draw calls 明顯增加。
- Strict Row 透明浪費最大，不適合作 production。
- 目前 Adaptive Atomic 的維護邊界合理，但 physical packing 並沒有在公平測試中勝過 Dense。

下一步不應回到跨素材 bundle，而是把 Mode4 升級成真正的 **Adaptive Per-Asset Atlas**：

> Logical asset 仍獨立；Builder 在單一 asset 內同時評估 Dense / Atomic / page-size candidates，再以 measured RGBA、page count、load/runtime benchmark 選 physical layout。

在這個新策略正式實作並 A/B 前，production 仍維持現有 Adaptive Atomic，不偷換政策。
