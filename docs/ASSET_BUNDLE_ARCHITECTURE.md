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
| Map static object atlas | **移除 production 細拆** | Map13 實測只省約 1.8～2.8MiB object resident，卻需 21～30 個 viewport owners；改與 monster 共用 map lifecycle bundle |
| Terrain | **獨立 map resource** | 單 texture + viewport MultiMesh |
| Ground drops | **目前無 texture atlas 類別** | DropRenderer 是單一 MultiMesh |
| Rare equipment / skill FX | **未來 on-demand 候選** | 必須低頻 + 可獨立卸載 + 實測有收益 |

## 為何 production 選 2048

不是因為「大圖比較快」，而是同素材 A/B。

### Map13：九種怪物 + 45 種場景 SPR 共用 map lifecycle bundle

原先 monster-only bundle 的 1024/2048 A/B 證明 2048 可有效減頁；本輪再把真正同 map lifetime 的 scenery 納入同一候選。

| Side | Pages | RGBA bytes |
|---:|---:|---:|
| 512 | invalid | 885×711 frame 超過 side |
| 1024 | 5 | 18,572,476 |
| **2048** | **2** | **18,569,428** |
| 4096 | 1 | 18,478,500 |

4096 雖少一頁，但 20-cycle lifecycle 的 load/frame 幾乎沒有改善，transient static/decode peak 卻從 **36,911,847 → 53,543,643 bytes**。300 draw synthetic 中 4096 draw P95 73、2048 draw P95 206，但 frame P95 4096 約 7.76～8.01ms，2048 約 7.02～7.05ms，沒有 frame 勝利。

因此 auto mode 會量測 4096，但只要 2048 能容納 atomic source block，就優先 <=2048；4096 只作研究 override 或 correctness fallback。尺寸仍是 Builder 參數，不是 Runtime 類型。

Map13 1440 最大視窗 + preload margin 的 scenery working set：P50 21 atlases / 4.394MiB，P95 30 atlases / 5.414MiB；全 45 種只有 7.222MiB。細拆常態僅省約 1.8～2.8MiB object resident，卻維護 21～30 個 owner/acquire/release，因此不保留這個 production boundary。

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

Production rebuild 現在會先清空可重建的 `client/assets`（只保留 `.gdignore`），不再把整套原始素材鏡像進 Godot Runtime root。

- Before：**17,274 files / 1,179.31MiB**
- After：**12 files / 9.036MiB**
- After 頂層只有 `bundles/`、`maps/`、`.gdignore`
- `tools/summarize_assets.py`：`unexpected_top_level=[]`、`production_clean=true`
- 原始素材仍完整保留在 `.cache/client`，Viewer／研究工具可獨立查閱

Map13 production manifest 同時包含 9 normalized monster entities + 45 raw scenery clips；每個 normalized entity 另保留 `inspect[action][facing]`，所以逐怪物／逐動作／逐方向查閱能力不受 physical packing 影響。

正式 multiplayer Before → After（20 Client / 300 monster / 1000 drop / 40s）：

- mean FPS：59.996 → **59.999**
- draw P95：236 → **228**
- frame P95：19.236 → **18.947ms**
- frame P99：20.422 → **20.322ms**
- texture monitor：39,057,403 → **46,035,499 bytes**
- video monitor：45,557,371 → **52,534,347 bytes**
- 約每 Client process RSS peak：144.69 → **151.16MiB**

因此這次不是 RAM/VRAM 最佳化；它用約 +6～7MiB/client resident 換掉 45 個 per-object manifests 與 21～30 個 viewport owners，並得到較簡單的 lifecycle、可重現 clean rebuild，以及小幅 draw/frame 改善。

After 的 30-cycle async lifecycle：

- frame P95/P99 **16.824 / 16.906ms**
- texture handles release PASS
- engine texture recovery PASS
- store resident recovery PASS

完整分類稽核與 Before/After 見 [ASSET_NECESSITY_AUDIT.md](ASSET_NECESSITY_AUDIT.md)。

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
- map_objects 與 monster 共用同一 map sprite bundle；viewport 只決定 draw cull，不再決定 texture owner。場景物件沒有另一套 Loader/Cache。

Server：

- 不讀 Atlas。
- population definition id/stats/spawns 不變。
- definition 的 atlas 欄位只是 Client content contract；Map13 現在共同指向同一 manifest。
- 無封包格式、同步頻率、AI tick、combat cadence 變更。

正式模型只有一句話：

**Normalized definition 決定「是什麼」；lifecycle bundle 決定「何時一起載/卸」；RuntimeAssetStore 決定「如何有界且平滑地載/卸」。**
