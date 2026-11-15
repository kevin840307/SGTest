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

## 10. Model1 / Recovered 深入比較（2026-11-15）

本節回答「即使使用 Model1，frames 怎麼排序、要不要分桶、Recovered 實際作法是否更好」。

### 10.1 Recovered Model1 實際作法

Recovered 不是模擬資料，而是直接檢查：

- `C:\SGO\Recovered\client\Assets\Monsters/*.png + *.json`
- `MonsterLoader.get_monster_frames()`
- `PaperDollLoader.get_sprite_frames()`
- `MonsterEntity / PaperDollEngine`

Recovered Enemy renderer：

```text
one Enemy
  -> one large PNG + JSON
  -> ResourceLoader
  -> one shared SpriteFrames
  -> canonical AtlasTexture per JSON frame
  -> AnimatedSprite2D per entity
```

Recovered Player/weapon/equipment：

```text
one part
  -> one PNG + JSON
  -> PaperDollLoader
  -> SpriteFrames / AtlasTexture
  -> independent AnimatedSprite2D layer
  -> DrawOrder controls layer order
```

Recovered Model1 重要特性：

- 同一 asset 單張大 atlas。
- frame edge-to-edge，沒有 extrude padding。
- `AtlasTexture.filter_clip=true` 防止線性採樣污染鄰格。
- 沒有刪 frame：實測 Enemy 的 Recovered JSON frame count 與 source archive frame count一致。
- 常見寬度固定在 2048，但大型 Enemy 高度可超過4096，例如 EN078A 為 2048x5872。

### 10.2 Model1 排序 / 分桶全類型幾何比較

使用正確四邊 padding 定義，聚焦 2,761 個會直接影響本專案 Runtime 的資產：

- Enemy
- Player Weapon
- Player Equipment
- Map scenery

| Strategy | Estimated RGBA MiB | Median Waste | P90 Waste |
|---|---:|---:|---:|
| **Height global** | **3136.55** | **18.59%** | **52.10%** |
| Size bucket soft | 3155.88 | 19.36% | 52.74% |
| Size bucket hard | 3183.81 | 21.33% | 52.60% |
| Aspect bucket soft | 3300.62 | 24.55% | 64.06% |
| Max-side global | 3307.57 | 23.55% | 73.50% |
| Height band 32 | 3315.59 | 24.92% | 68.72% |
| Height band 64 | 3357.04 | 25.89% | 72.50% |
| Action bucket soft | 3384.79 | 27.81% | 69.52% |
| Width global | 3423.28 | 28.53% | 72.56% |
| Area global | 3485.21 | 29.15% | 81.51% |
| Action bucket hard | 3703.47 | 39.95% | 87.95% |

結論：**不要先按 action/source/size 硬分桶。Height-desc global shelf 是目前最穩定基準。**

各類別：

| Type | Height global | Next best | 結論 |
|---|---:|---:|---|
| Enemy | **703.68 MiB** | Size soft 707.53 | Height 最佳 |
| Weapon | **140.60 MiB** | Size soft 144.60 | Height 優勢更明顯 |
| Equipment | **1926.69 MiB** | Size soft 1938.17 | Height 最佳 |
| Scenery | 約 **365.58 MiB** | 幾乎相同 | 多數單/少 frame，不值得複雜分桶 |

### 10.3 Recovered atlas vs Model1 Height-P0

這裡只比較 atlas 幾何，不混 Renderer。

#### Enemy

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| small EN032A | 2048x178 / 1.391 MiB | 447x521 / 0.888 MiB | **-36.1%** |
| medium EN016A | 2048x468 / 3.656 MiB | 494x1507 / 2.840 MiB | **-22.3%** |
| large EN081A | 2048x1605 / 12.539 MiB | 1015x2940 / 11.383 MiB | **-9.2%** |
| huge EN078A | 2048x5872 / 45.875 MiB | 2778x4094 / 43.385 MiB | **-5.4%** |

#### Weapon

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| B-Dagger006 | 0.135 MiB | 0.085 MiB | **-36.7%** |
| B-Sword024 | 0.381 MiB | 0.340 MiB | **-10.9%** |
| B-Stick003 | 1.203 MiB | 0.797 MiB | **-33.8%** |
| B-Stick011 | 1.875 MiB | 1.327 MiB | **-29.2%** |

#### Equipment

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| G-Bonnet092 | 0.204 MiB | 0.193 MiB | **-5.3%** |
| A-Back023 | 1.031 MiB | 0.866 MiB | **-16.0%** |
| R-Back029 | 3.695 MiB | 3.475 MiB | **-6.0%** |
| R-Back026 | 16.266 MiB | 15.654 MiB | **-3.8%** |

因此在**純 packing**上，可變尺寸 Height-P0 普遍優於 Recovered 固定2048寬。

### 10.4 Padding 比較

候選：

- Height-P0：0px padding。
- Height-P1：1px extrude。
- Height-P2：2px extrude。
- MaxRects-P1：single-page MaxRects + 1px extrude。

代表結果：

- Enemy small：P0 0.888 MiB / P1 0.943 / P2 1.006 / MaxRects-P1 0.970。
- Enemy huge：P0 43.385 / P1 44.235 / P2 44.891 / MaxRects-P1 46.462。
- Weapon large：P0 0.797 / P1 0.857 / P2 0.926 / MaxRects-P1 0.817。
- Equipment huge：P0 15.654 / P1 16.288 / P2 16.571 / MaxRects-P1 17.227。

幾何上 P0 最省。是否能 production 使用 P0，必須與 texture filtering / bleeding 策略一起決定。Recovered 用 edge-to-edge + `filter_clip=true`；SGTest 如果保留直接 Canvas draw，則必須另外驗證 nearest/filter clip/scale 情境，不可只因面積較小就直接移除 padding。

## 11. Native Renderer 比較

這一輪不是把兩種格式塞進同一 Renderer，而是使用各自真正的 Runtime path。

### Recovered

- ResourceLoader。
- imported texture。
- shared SpriteFrames。
- AtlasTexture。
- AnimatedSprite2D per entity/layer。

### SGTest

- Image.load_from_file。
- ImageTexture.create_from_image。
- EntityAtlas。
- GDScript sample()。
- CanvasItem.draw_texture_rect_region()。

條件：

- 1280x720。
- 100 frames / 30 warmup。
- VSync off / uncapped。
- Recovered benchmark 強制 render loop 開啟，避免失焦省電模式污染結果。
- 單素材情境：Enemy 1000 instances；Weapon/Equipment 300 layers。
- 混合情境：2 repeats 取 median。

### 11.1 Enemy mixed

| Scenario | Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---|---:|---:|---:|---:|
| 4 kinds / 1000 | **Recovered Model1** | **35.67** | **2.188** | 1001 | **21.18** |
| | Height-P0 | 189.69 | 4.793 | 1000 | 77.98 |
| | Height-P1 | 181.68 | 4.726 | 1000 | 79.77 |
| | Height-P2 | 181.04 | 5.014 | 1000 | 81.30 |
| | MaxRects-P1 | 208.03 | 4.872 | 1000 | 83.80 |
| 12 kinds / 1000 | **Recovered Model1** | **67.96** | **2.414** | 1001 | **62.26** |
| | Height-P0 | 592.43 | 4.750 | 1000 | 231.17 |
| | Height-P1 | 598.23 | 5.399 | 1000 | 234.59 |
| | Height-P2 | 632.72 | 4.879 | 1000 | 239.57 |
| | MaxRects-P1 | 656.76 | 4.834 | 1000 | 251.78 |

**重要：這不代表 Recovered packing 較小。** 前一節已證明 Height-P0 幾何更小。這裡巨大差距主要來自 loader / texture representation / renderer path。

### 11.2 Weapon mixed 4 / 300 layers

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| Recovered Model1 | 27.19 | **0.810** | 301 | 4.79 |
| **Height-P0** | 9.82 | 1.730 | 300 | **3.40** |
| Height-P1 | **9.47** | 1.794 | 300 | 3.63 |
| Height-P2 | 10.94 | 1.619 | 300 | 3.87 |
| MaxRects-P1 | 10.23 | 1.560 | 300 | 3.58 |

Weapon 的 SGTest atlas 在 load / texture footprint 已有優勢，但 direct Canvas GDScript renderer 仍較慢。

### 11.3 Equipment mixed 4 / 300 layers

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| Recovered Model1 | 65.26 | **1.101** | 301 | 28.26 |
| **Height-P0** | **62.89** | 1.818 | 300 | **26.91** |
| Height-P1 | 81.55 | 1.565 | 300 | 28.30 |
| Height-P2 | 86.26 | 1.718 | 300 | 29.13 |
| MaxRects-P1 | 100.38 | 1.672 | 300 | 29.92 |

### 11.4 Scenery mixed 4 / 600

Recovered 沒有相同的 isolated loose-scenery loader；它的 Map 使用不同 PackedScene/Map lifecycle，因此不假裝做 apples-to-apples。

SGTest：

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| **Height-P0** | 44.84 | **2.859** | 600 | **16.18** |
| Height-P1 | 45.32 | 3.064 | 600 | 16.33 |
| Height-P2 | 49.12 | 3.116 | 600 | 16.49 |
| MaxRects-P1 | **43.95** | 2.992 | 600 | 17.40 |

差距很小；scenery 不值得建立獨立複雜 packing policy。

## 12. 這輪真正的結論

### Packing

目前第一名：

**Per-Asset Height-Descending Dense Atlas**

也就是：

```text
one independent asset
 -> trim frames
 -> sort by height descending, width descending
 -> search useful atlas width
 -> single page <=4096
```

目前沒有證據支持：

- 先按 action 分桶。
- 先按 source 分桶。
- size hard bucket。
- aspect bucket。
- Model1 主路徑使用 MaxRects。

MaxRects 留給未來超過4096的單 asset fallback 比較合理。

### Runtime

目前最大瓶頸不是 packing。

SGTest Enemy 的問題是：

```text
PNG
 -> Image.load_from_file
 -> ImageTexture.create_from_image
 -> Dictionary-heavy EntityAtlas.sample
 -> per-instance GDScript draw submission
```

Recovered 的優勢路徑是：

```text
Godot imported texture
 -> ResourceLoader
 -> shared SpriteFrames / AtlasTexture
 -> engine-side AnimatedSprite2D update/render
```

因此下一輪最值得測的不是再發明新的 bucket，而是：

1. 保留 **per-asset Height Dense**。
2. A/B `0px + filter_clip/nearest` vs `1px extrude`，確認縮放/旋轉/不同解析度無 bleed。
3. A/B **external PCK imported/compressed texture + ResourceLoader** vs目前 raw PNG + ImageTexture。
4. 比較 SGTest 目前 GDScript direct Canvas 與「共享 frame descriptor / engine-side sprite or lower-allocation draw path」。
5. 多 Enemy 交錯時，兩邊都約一 instance 一 draw call；另開 texture-array / safe Y-band batching research，但**不得跨 asset identity 合圖**。

## 13. Runtime Asset Folder Naming

Monster 的 domain 名稱仍為 monster；實體資產 folder 依原版名稱固定：

```text
client/assets/Enemy/<id>/atlas.json
```

Server population definition 也引用：

```text
Enemy/<id>/atlas.json
```

不要再建立舊怪物素材資料夾；Runtime 實體名稱固定為 `client/assets/Enemy/`。

## 15. Runtime-only comparison（3 repeats, Windows process-tree RSS）

本節依使用者要求，**不把磁碟大小、PNG大小、atlas空白率當決策指標**。只比較實際執行：

- Windows Godot process-tree Peak RSS（包含 console + real renderer process）。
- Godot Texture/Video memory delta。
- load time。
- Frame P95 / P99。
- draw calls。
- CPU seconds / Node count 作輔助。

條件：1280x720、VSync off、120 frames、30 warmup、每情境3個獨立 process。Recovered 使用它自己的 ResourceLoader/SpriteFrames/AnimatedSprite2D renderer；SGTest 使用自己的 EntityAtlas/direct Canvas renderer。

> Peak RSS 是完整實際 process 成本，因此包含 Recovered 較多 autoload/node 的基線；這正是實際執行資源，但不能誤解成純 texture 差異。

### 15.1 Enemy：不同尺寸 × 1000

| Size | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | Draw |
|---|---|---:|---:|---:|---:|---:|
| Small | Recovered Model1 | 209.5 | **0.5** | **1.4** | **1.015** | 2 |
| | SG Height-P0 | **147.4** | 1.2 | 3.9 | 3.334 | **1** |
| Medium | Recovered Model1 | 209.4 | **1.2** | 18.5 | **1.059** | 2 |
| | SG Height-P0 | **151.0** | 3.8 | **11.8** | 3.219 | **1** |
| Large | Recovered Model1 | 209.9 | **4.2** | **35.8** | **1.579** | 2 |
| | SG Height-P0 | **156.7** | 15.2 | 39.1 | 3.172 | **1** |
| Huge | Recovered Model1 | 220.0 | **15.3** | **50.7** | **2.042** | 2 |
| | SG Height-P0 | **179.0** | 57.8 | 129.4 | 3.250 | **1** |

Enemy 結論：

- Recovered 的 texture representation + engine-side animation 明顯優於目前 SGTest Enemy renderer。
- SGTest process RSS 低約 29~62 MiB，且只需1個 renderer node。
- SGTest 目前最大問題不是 Height-P0 packing，而是 raw PNG → ImageTexture 與每 instance GDScript sample/draw。

### 15.2 Enemy 混合

| Scenario | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | Draw |
|---|---|---:|---:|---:|---:|---:|
| 4 kinds / 1000 | **Recovered Model1** | 212.9 | **21.2** | **32.5** | **2.461** | 1001 |
| | Height-P0 | **201.7** | 78.0 | 174.9 | 5.072 | 1000 |
| | Height-P1 | 202.7 | 79.8 | 194.8 | 5.130 | 1000 |
| | Height-P2 | 203.2 | 81.3 | 185.4 | 5.123 | 1000 |
| | MaxRects-P1 | 207.7 | 83.8 | 194.3 | 5.202 | 1000 |
| 12 kinds / 1000 | **Recovered Model1** | 217.9 | **62.3** | **77.7** | **2.681** | 1001 |
| | Height-P0 | 190.4 | **231.2** | 609.8 | 5.358 | 1000 |
| | Height-P1 | **189.5** | 234.6 | **600.9** | 5.303 | 1000 |
| | Height-P2 | 194.2 | 239.6 | 602.3 | 5.149 | 1000 |
| | MaxRects-P1 | 194.4 | 251.8 | 650.4 | **5.051** | 1000 |

12-kind Recovered vs Height-P0：

- SGTest Peak RSS 約 **12.6%低**。
- Recovered Texture memory 約 **73%低**。
- Recovered load 約 **87%快**。
- Recovered P95 約 **50%快**。
- draw calls 幾乎相同。

因此 Enemy 不能只調 atlas排序；要優先換 texture/renderer path。

### 15.3 Weapon：不同尺寸 × 300 layers

| Size | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms |
|---|---|---:|---:|---:|---:|
| Small | Recovered | 209.0 | 0.2 | 1.8 | **0.518** |
| | Height-P0 | **146.3** | **0.1** | **0.8** | 1.138 |
| Medium | Recovered | 209.3 | 0.5 | 2.4 | **0.549** |
| | Height-P0 | **146.6** | 0.5 | **2.0** | 1.155 |
| Large | Recovered | 209.4 | 1.6 | 20.3 | **0.542** |
| | Height-P0 | **147.0** | **1.1** | **2.6** | 1.116 |
| Huge | Recovered | 209.4 | 2.5 | 20.9 | **0.507** |
| | Height-P0 | **150.2** | **1.8** | **4.5** | 1.245 |

Weapon 結論：SGTest Height-P0 的實際資源/load 明顯更好；Recovered 只是 renderer frame time 更快。不要退回 Recovered 固定2048 atlas。

### 15.4 Weapon mixed 4 / 300

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | CPU s |
|---|---:|---:|---:|---:|---:|
| Recovered | 209.7 | 4.8 | 27.7 | **1.029** | 1.48 |
| **Height-P0** | **151.3** | **3.4** | 9.6 | 1.618 | **0.69** |
| Height-P1 | 151.0 | 3.6 | **9.5** | 1.707 | 0.72 |
| Height-P2 | 151.6 | 3.9 | 10.4 | **1.549** | 0.72 |
| MaxRects-P1 | 151.3 | 3.6 | 10.3 | 1.749 | 0.73 |

Height-P0 是最好的資源基準。P2 的 P95 僅快約0.07ms，但多用 texture memory，沒有足夠理由成為主策略。

### 15.5 Equipment mixed 4 / 300

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | CPU s |
|---|---:|---:|---:|---:|---:|
| Recovered | 214.6 | 28.3 | 65.1 | **1.391** | 1.47 |
| **Height-P0** | 167.1 | **26.9** | **63.4** | 1.750 | **0.77** |
| Height-P1 | **167.0** | 28.3 | 69.2 | 1.844 | 0.78 |
| Height-P2 | 167.3 | 29.1 | 79.3 | 1.782 | **0.75** |
| MaxRects-P1 | 167.6 | 29.9 | 74.3 | **1.672** | 0.81 |

MaxRects-P1 只在 P95 贏 Height-P0 約0.08ms，但 texture/load 都更差；不值得增加 production 分支。

### 15.6 Scenery mixed 4 / 600

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms |
|---|---:|---:|---:|---:|
| **Height-P0** | **160.1** | **16.2** | 48.0 | 3.200 |
| Height-P1 | 163.5 | 16.3 | 47.3 | 3.375 |
| Height-P2 | 163.0 | 16.5 | 47.0 | 3.234 |
| MaxRects-P1 | 163.3 | 17.4 | **46.6** | **3.095** |

MaxRects 的約0.1ms P95 / 1.4ms load 優勢很小，但增加約3.2MiB Peak RSS與1.2MiB texture；目前不值得為 scenery 做另一套 packing。

## 16. Runtime decision list

### SGTest packing 候選排名

1. **Height-P0** — 整體資源效率最佳；Weapon/Equipment/Scenery/Enemy 都是目前 baseline。
2. **Height-P1** — 若 visual bleeding 驗證需要 padding，作安全 fallback；效能沒有實質優勢。
3. **Height-P2** — 更保守但資源持續增加；沒有 runtime 證據支持預設使用。
4. **MaxRects-P1** — 少數 P95 略好，但 memory/load 綜合較差；只保留研究/fallback。

### Renderer / loader 排名

Enemy：
1. **Recovered imported texture + ResourceLoader + shared SpriteFrames/AnimatedSprite2D**：實際 frame/load/texture 明顯最佳。
2. SGTest direct Canvas：Peak RSS低、node少，但目前 texture/load/frame慢。

Weapon / Equipment：
1. **SGTest Height-P0 asset + lean process**：實際 RSS、load、texture/CPU綜合最佳。
2. Recovered：frame P95較快，但 process/node成本高。

### Recommended hybrid

不要直接複製 Recovered 整套，也不要只改 atlas：

```text
Per-asset Height-P0/Height-P1
        +
Godot imported/compressed texture / ResourceLoader
        +
shared immutable animation/frame metadata
        +
移除每 instance Dictionary-heavy EntityAtlas.sample()
        +
engine-side 或低 allocation renderer
```

Enemy 的多 texture Y-order 交錯使 Recovered 與 SGTest 都接近一 instance 一 draw call；這是下一個 renderer batching 問題，不應靠跨 Enemy 合圖解決。
