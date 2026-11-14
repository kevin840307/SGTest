# Runtime Asset Bundle Architecture

日期：2026-11-14  
狀態：**Production policy accepted / AS005 原版逐格人工視覺與 AS008 發布權仍是獨立 Gate**

## 核心規則

SGTest 正式 Runtime 不再把「怪物種類、方向、動作、人物部位、Atlas 尺寸、hot/warm/cold」當成資產類型。

對外只保留 ownership / lifetime：

1. **map**：進入同一地圖一起需要，切圖一起釋放。
2. **session**：登入到登出期間一起需要。
3. **on-demand**：只有低頻且真的能獨立卸載時才建立；目前不預先建立空分類。

方向、動作、mirror、pivot、ACT timeline、人物 z-order 全部留在 sgtest.animation/v2，只是 bundle 內部 logical metadata，不是 unload boundary。

Production sprite pipeline 唯一入口：

    python tools/asset_pipeline.py

完整重建仍由 tools/rebuild_assets.ps1 呼叫。舊 build_assets.py、build_atlas_groups.py、build_character_core_atlas.py、build_normalized_atlas_candidates.py 保留作研究、歷史 A/B、原版素材驗證，不是 production Runtime 分類入口。

## 為什麼舊分類過度

### Map13 monster

Before：9 monster definitions 各一個 manifest，但九種怪只要進 Map13 就一起存在，切圖也一起離開。怪物種類不是有效 unload boundary。

After：definition id / sprite / stats / logical clips 全部保留，但九種都指向 bundles/map/13/sprites.json。MonsterVisuals 只用 definition id 選 prefix，RuntimeAssetStore 只 acquire 一個 map sprite manifest。

### Player base

Before：body / leg / head / hair00 / l-arm / r-arm / death 共 7 manifests，但實際上同時顯示、同時釋放。

After：一個 bundles/session/player-b-base.json。六個 base parts + death 共用 EntityAtlas；z-order 直接讀 normalized draw_layers / draw_slots，不再 Runtime 第二次解析 CharDrawOrder.dat。

hair01～10、裝備、武器不因「是部位」自動塞進 base；只有低頻且能獨立卸載時才考慮 on-demand。

## 分類必要性

| 舊概念 / 類別 | Production | 判定 |
|---|---|---|
| Monster per entity | **移除** | 同 map 共現、同生同滅 |
| Direction atlas / direction_512... | **研究維度** | 方向是動畫語意，不是 lifetime |
| Action/state atlas | **研究維度** | 動作頻繁切換，不適合作 unload boundary |
| hot/warm/cold | **研究 heuristic** | 可作 priority 參考，不是 public type |
| readable/grid/dense/source-locality | **研究 packing** | 只決定 page placement |
| 512/1024/2048/4096 | **候選尺寸** | A/B 決定，不是資產種類 |
| Player base part | **移除** | 同 session lifetime |
| Map sprite bundle | **保留** | 進圖一起需要、切圖一起 release |
| Session base bundle | **保留** | 登入後一起需要、登出一起 release |
| Map static object atlas | **保留 viewport 粒度** | 可視才 pin，離開 viewport 能獨立 release |
| Terrain | **獨立 map resource** | 單 texture + viewport MultiMesh |
| Ground drops | **目前無 texture atlas 類別** | DropRenderer 是單一 MultiMesh |
| Rare equipment / skill FX | **未來 on-demand 候選** | 必須低頻 + 可獨立卸載 + 實測有收益 |

## 為何 production 選 2048

不是因為「大圖比較快」，而是同素材 A/B。

### Map13 九種怪合成一個 map bundle

| Side | Pages | RGBA bytes |
|---:|---:|---:|
| 512 | 11 | 10,297,356 |
| 1024 | 3 | 10,598,644 |
| **2048** | **1** | **10,805,780** |
| 4096 | 1 | 11,476,296 |

2048 相對最低 RGBA 的 512 多約 4.9%，但頁數 11→1。4096 沒再減頁，因此拒絕。

RuntimeAssetStore 20-cycle：

| Candidate | Load P95 | Load frames P95 | Frame P95/P99 | Texture peak | Video peak |
|---|---:|---:|---:|---:|---:|
| 1024 / 3 pages | 83.56ms | 5 | 16.764 / 16.857ms | 24,521,643 | 30,932,275 |
| **2048 / 1 page** | **66.70ms** | **4** | **16.782 / 16.827ms** | 24,798,879 | 31,209,511 |

300 mixed-frame render（三輪）：

| Candidate | Draw P50/P95 | Frame P95 | Frame P99 |
|---|---:|---:|---:|
| 1024 / 3 pages | 250 / 254 | 約7.99ms | 約8.05～8.08ms |
| **2048 / 1 page** | **73 / 73** | 約7.91～8.01ms | 約8.01～8.09ms |

2048 transient decode/static 工作區約多 8MiB，但常駐 texture/video 幾乎相同；換來較少 upload frames 與大量較少 texture switches/draw calls。

### Boy base 七 logical parts

| Side | Pages | RGBA bytes |
|---:|---:|---:|
| 512 | 5 | 4,430,400 |
| 1024 | 2 | 4,428,064 |
| **2048** | **1** | **4,390,912** |
| 4096 | 1 | 4,367,016 |

2048 自身比 1024 少 decoded area；4096 只少約24KiB、沒有頁數收益，所以不選。

| Player candidate | Load P95 | Load frames P95 | Draw P50/P95 | Frame P95 |
|---|---:|---:|---:|---:|
| 1024 / 2 pages | 83.19ms | 5 | 113 / 123 | 約8.00ms |
| **2048 / 1 page** | **66.47ms** | **4** | **73 / 73** | 約8.00ms |

## 真正刪除舊分類後的 Gate

新 bundle 建立後，Map13 九個舊 per-enemy manifests/pages + Boy base 七個舊 per-part manifests/pages 共移除 **40 個 generated files**。hair01 等未被新 bundle 取代的素材保留。

刪除後：

- Python top-level：45 tests PASS。
- tools/tests：89 tests PASS。
- Git tracked Godot Client tests全部 PASS。
- Map13：9 definitions / 360 action-direction combinations / **1 map bundle** PASS。
- Player：6 parts + death / normalized draw-order / **1 session bundle** PASS。
- RuntimeAssetStore owner/share/cache/cancel/purge PASS。
- 128 packet queue drain/decode 約0.9ms。

Production map bundle 30-cycle async lifecycle：

- frame P95/P99 **16.752 / 16.809ms**
- texture handles release PASS
- engine texture recovery PASS
- store resident recovery PASS

20 Client / 300 monster / 1000 drop / 40s：

- execution PASS
- mean **59.996 FPS**
- frame P95/P99 **19.236 / 20.422ms**
- draw-call P95 **236**
- texture monitor 39,057,403 bytes
- video monitor 45,557,371 bytes
- 20/20 prediction/snapshot ready
- source files unchanged

Server protocol、30Hz simulation、snapshot cadence、definition id、stats、spawn contract 沒有因合圖改變。Server 不讀 PNG/Atlas pixels。

## 新增分類的硬規則

新增 Runtime asset category 必須同時證明：

1. 使用頻率明顯不同。
2. 能在不影響同一畫面必要內容的情況下獨立卸載。
3. owner lifetime 真的不同。
4. 拆分後 load P95/P99、frame P95/P99、draw calls、RAM/texture/video memory 有量測收益。
5. normalized definition 仍完整保留方向、mirror、pivot、ACT、z-order。

只因圖大、檔名不同、不同怪物、不同方向、不同動作、不同紙娃娃部位，都不足以建立新分類。

## 原版動畫測試能力保留

Runtime bundle 簡化不會刪研究能力。

.cache/client、sgtest.animation/v2、legacy direction mapping、viewer/preview、pixel/pivot/hash audit 仍獨立存在。驗證的是 logical requested clip → normalized physical clip + flip + timeline + pivot，不依賴最後 pack 在哪張 PNG。

因此 production 可以只用少量 lifecycle bundles，同時保留逐怪物、逐方向、逐動作的原版驗證。AS005 原版同版本逐格人工視覺比較仍是獨立 NOT VERIFIED Gate。

## Client / Server 介面

Client：

- EntityAtlas 是唯一 sprite manifest reader。
- RuntimeAssetStore 是唯一 async decode/GPU commit/cache/owner store。
- MonsterVisuals：definition id → prefix，多種怪共用 map manifest。
- PaperDoll：logical part prefix，六部位＋death 共用 session manifest。
- map_objects 保留 viewport pin/release，因為它真的有不同 lifetime。

Server：

- 不讀 Atlas。
- population definition id/stats/spawns 不變。
- definition 的 atlas 欄位只是 Client content contract；Map13 現在共同指向同一 manifest。
- 無封包格式、同步頻率、AI tick、combat cadence 變更。

正式模型只有一句話：

**Normalized definition 決定「是什麼」；lifecycle bundle 決定「何時一起載/卸」；RuntimeAssetStore 決定「如何有界且平滑地載/卸」。**
