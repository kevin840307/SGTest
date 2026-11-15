# Asset Packing Benchmark — Per-Asset Candidates

日期：2026-11-15
狀態：**CURRENT FAIR COMPARISON / PRODUCTION SWITCH NOT YET APPLIED**

## 1. 不變的硬規則

本 benchmark 只比較**同一 logical asset 內部**如何排列 frames。

永遠禁止：

- Map + Monster 合圖。
- 不同 Monster 合圖。
- 不同 NPC 合圖。
- 不同 scenery 合圖。
- Player body/head/arms/death 跨 part 合圖。
- Equipment 與 Player base 合成共同 lifecycle atlas。

因此 packing policy 可以換，asset identity 不會換。

## 2. 這次比較的四個候選

### A. Per-Asset Dense Single

舊研究 Mode1 的直接語意名稱。

- 同一 asset 的所有可見 frames 自由 dense shelf。
- 目標單一 page。
- 寬度可變，輸出只保留實際 used bounds。
- 不要求 power-of-two。
- 不跨 asset。

### B. Adaptive Dense Shelf

- 同一 asset 內 frames 可自由排列。
- 候選 side：1024 / 2048 / 4096。
- 允許多 page。
- 在 5% decoded area 容忍範圍內偏好較少 pages。

### C. Adaptive Dense MaxRects

- 同一 asset 內 frames 可自由排列。
- 候選 side：1024 / 2048 / 4096。
- frame-level MaxRects。
- 允許多 page。

### D. Adaptive Atomic

目前 production builder 的 physical policy。

- 原始 SPR 保持 atomic placement block。
- 512 / 1024 / 2048 / 4096 候選。
- 可多 page。

## 3. 公平測試條件

固定 Monster IDs：

```text
EN114A EN089A EN121A EN116A EN023A EN084A
EN111A EN125A EN050C EN080A EN093A EN079A
```

固定：

- Map13 terrain。
- 16 個可視 independent scenery assets。
- 48 個可視 scenery objects。
- 1440×900。
- 1000 monster instances。
- 180 frames / 60 warmup。
- 每候選 3 個獨立 Godot process。
- repeat 2 反轉執行順序。
- deterministic action/facing trajectory。
- Map root 與 Monster root 分離。
- 每次要求 `asset_boundary_verified=true`。

工具：

```text
tools/asset_converter/benchmarks/packing/
├── build_candidates.py
├── benchmark.gd
└── run.py
```

產物只放：

```text
.cache/asset_packing_benchmark/
```

不再把 benchmark PNG/JSON 堆在 `local-assets/`。

## 4. 2 kinds / 1000 instances

| Candidate | Pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| **Per-Asset Dense Single** | **2** | **64.73** | **222.5** | **107.52** | **926** | 8.008 | 8.016 |
| Adaptive Dense Shelf | 11 | 69.35 | 225.8 | 113.66 | 1009 | 8.006 | 8.012 |
| Adaptive Dense MaxRects | 5 | 67.80 | 224.5 | 111.60 | 1041 | 8.001 | **8.010** |
| Adaptive Atomic | 7 | 79.77 | 263.0 | 127.55 | 1064 | **8.002** | 8.016 |

Frame-time 差異很小且三輪有重疊；不能把 0.00x ms 差距解讀成某個候選明顯更快。確定差距是 pages / decoded texture area / texture monitor / load。

## 5. 12 kinds / 1000 instances

| Candidate | Pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| **Per-Asset Dense Single** | **12** | **179.06** | 638.5 | **259.93** | 1106 | 7.993 | 8.266 |
| Adaptive Dense Shelf | 42 | 192.33 | **617.5** | 277.56 | 1106 | 7.308 | 7.983 |
| Adaptive Dense MaxRects | 23 | 189.26 | 663.9 | 273.48 | 1106 | 7.272 | **7.787** |
| Adaptive Atomic | 30 | 211.98 | 659.2 | 303.76 | 1106 | **7.215** | 7.990 |

這組 load/frame 有 OS/driver cache 波動；三次中位數不能單獨決定 packing。Dense Single 的優勢是 deterministic：最低 pages 與最低 decoded texture footprint。

## 6. 全素材幾何覆蓋

另外使用正式 archive 的 frame metadata 掃描目前完整 asset set：

- 正式 index：7,890 logical sprite groups。
- 7,890 / 7,890 都能以單一 Dense page、最大邊 <= 4096 放下。
- Monster：137 / 137。
- 目前沒有一個正式 asset 需要先拆成多 page。

Monster 中接近上限的例子：

- EN023A：4051×1174。
- EN078A：4017×2908。
- EN119A：3837×625。

全素材接近上限例子包括 Player back equipment 與 Effect，但仍 <=4096。

這個掃描只使用已驗證的 frame crop/rect metadata 做幾何規劃；production 切換前仍必須由 Builder 重新輸出並做 pixel/origin/hash verification。

## 7. 建議：Per-Asset Dense Atlas

目前最簡單、可維護且有數據支持的目標 policy：

```text
one logical asset
      ↓
trim frames
      ↓
dense single-page pack
      ↓
atlas.json + page_0.png
```

Hard rules：

1. asset boundary 永遠不跨越。
2. 優先單 page。
3. 最大邊 4096。
4. 保留原 frame source / crop / pivot / logical action/facing metadata。
5. 若未來真的出現 >4096 的新素材，才使用 **Paged Dense MaxRects** fallback。
6. fallback 是單一 asset 內部分頁，不是跨 asset bundle。

正式名稱建議：

- primary：**Per-Asset Dense Atlas**
- fallback：**Paged Dense Atlas**

不使用 Mode1 / Mode4 當 production 名稱。

## 8. 為什麼不選其它方案

### Adaptive Atomic

優點是原 SPR placement locality 很直觀；缺點是大型動畫容易多 page。公平測試下記憶體/page/load 沒有勝過 Dense。

### Dense MaxRects

幾何比 shelf 緊，但在限制候選 side 後可能拆頁；對 2 kinds 的 draw calls 反而高於單頁 Dense。若未來有超過4096的 asset，它適合當 fallback。

### Per-Source

已淘汰。每 SPR 一 texture 造成 page/texture switch 過多。

### Strict Row

已淘汰。透明區域浪費大。

### Texture2DArray

目前只列 research candidate，不作 production。它會引入 custom shader / layer-dimension 約束；目前所有正式 asset 已可單 page，沒有足夠利益支持增加這層複雜度。

## 9. Production 狀態

**這份文件尚未把 production builder 從 Adaptive Atomic 切成 Dense。**

使用者目前要求先討論與比較，因此本輪先完成：

- 可重跑候選。
- 公平 benchmark。
- 全 asset 幾何覆蓋。
- 架構與舊工具清理。

若決定採用 Per-Asset Dense Atlas，下一個 implementation commit 才修改正式 Builder，並重新跑完整 7,890 asset pixel/origin/hash verification + Godot runtime regression。
