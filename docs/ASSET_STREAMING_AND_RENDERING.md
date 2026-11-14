# SGTest 高效能素材存取、Atlas 與渲染架構

版本：設計 v1／2026-11-13，2026-11-14 加入 HK2 實測。正式 runtime **NOT VERIFIED**。乾淨 HK2 的 SPR/ACT/SIF 已可本機轉換，並有三組實驗 Atlas；不代表原版視覺、動畫、授權或 Client 已完成。必須遵守 AS001～AS009 及 AS008 授權 Gate。詳 [HK2 Atlas 候選實測](HK2_ATLAS_CANDIDATES.md)。

## 1. 核心目標

- 降低 Client RAM、VRAM、CPU、GPU Draw Calls、載入 Spike，維持人物、怪物、掉落物準確且流暢；正式視窗寬度約 800～1440 像素，支援多種長寬比與鏡頭縮放。
- **EntityCount ≠ UniqueAssetCount ≠ AtlasPageCount ≠ Godot NodeCount**：一張圖有 500 隻怪、2,000 件掉落物，實際可能只有 8 種怪、25 種物品圖。每個種類只共享一份 Texture、FrameTable、材質和動畫定義；實例只保留自己的 EntityId／座標／方向／ActionTimeline。
- State/Network/Server 權威絕不等待 Texture 讀取、動畫或視覺更新；FPS 不得影響收送包和戰鬥。見 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
- 原版方向×動作×影格及 duration、pivot、offset、palette、alpha、z-order、attachment、事件一律保真。方向數由真實 decoder 證據確立，不預設八向。

## 2. 離線素材管線（不在遊戲執行中解碼/合圖）

```text
原件唯讀 inventory → 格式/版本證據 → Decoder → RGBA + Canonical JSON
→ 透明裁邊（可還原）／重複像素影格去重 → Usage-aware grouping
→ deterministic MaxRects atlas packer → Pages + Frame Region Manifest
→ Godot compiled resource catalog → 視覺 Golden/回歸測試
```

Canonical Metadata 至少包含：SourceHash、DecoderVersion、VisualDefinitionId、ActionId、FacingId、FrameIndex、DurationMs、原始尺寸、TrimRect、Pivot、Offset、ZOrder、AttachmentPoint、Hit/Action Events、Alpha/Palette 轉換資訊。Unknown 欄位明列，不補假資料。裁邊後使用原始坐標還原，不得改變腳底錨點/武器疊層。

Packing：stable sort + deterministic seed + hash，輸出 `AtlasPageId, RegionRect, UV, TextureVariant, ContentHash`。可允許同一動作跨頁，但絕不改變動畫時間軸。壓縮/縮圖必須經視覺測試；PNG/RGBA 為保真基準，VRAM 壓縮未驗證不得預設啟用。

## 3. Adaptive Multi-Size Atlas 與地圖感知分群

| 頁面（候選） | 單頁 RGBA8 理論 VRAM，無 mipmap | 使用情境 |
| --- | ---: | --- |
| 512×512 | 1 MiB | 少量小素材或分散圖示 |
| 1024×1024 | 4 MiB | 小怪、掉落物、常見圖示 |
| 2048×2048 | 16 MiB | 一般怪/較多動作、角色部件 |
| 4096×4096 | 64 MiB | 大型 Boss 或分組多影格 |

**尺寸不是依怪物顯示大小直接決定**，也不是越大/越少頁就越快。分類依：
1. 場景/地圖/region 同時出現（co-occurrence），例如 Map13 常見怪、掉落物。
2. 種類出現頻率（spawn frequency），Hot/暖/Cold 分層。
3. 材質、透明度、filter、mipmap 設定（兼顧 batching）。
4. 動畫動作、特效使用時機、紙娃娃部件與快取生命週期。
5. frame 總矩形、packing efficiency、GPU 最大尺寸與 runtime VRAM budget。

先按使用關聯分組，群內選 512/1024/2048/4096 page，測試 70–85% 填充率作初始 heuristic，但**填充率不是單一成功指標**。Padding/extrusion 先試 2–4px，依 nearest/linear/mipmap 真實驗證，防止貼圖 bleeding。不能為合批破壞 Y-sort、背後/前方武器層序。

### Map Asset Palette

離線由 map spawn table、NPC、可能掉落物和鄰區集合編譯：

```text
Global Shared Assets: common monsters, common ground items, player parts, common FX
Map13 Palette:
  hot: 6 common monster visuals / 15 common item visuals
  warm: rare monster types / near-area items
  cold: boss / rare special action pages
Map15 Palette:
  own NPC/map pages + references shared assets
```

上述數字與名稱是**設計示例，非真實遊戲盤點**。跨地圖共用頁只留一份；若某類素材只在一張地圖常用，不要和全世界低頻素材合成常駐巨頁。Boss 的 Idle/Walk 與稀有技能動畫可分頁，避免只顯示待命就載入所有特效。Map/Region 的 resource manifest 可以做確定性預取，減少每次 Spawn 的磁碟 I/O。

## 4. 不同實體採不同 Rendering Strategy

| 實體 | 資產共用 | Runtime 呈現 |
| --- | --- | --- |
| 自己角色 | 身體、髮型、裝備、武器各自 TextureAtlas | 精確 Paper Doll、多方向 layer z-order、玩家優先 |
| 其他玩家 | 相同部件 Atlas、可選有上限 AppearanceKey 合成快取 | 可見 Visual Pool；不同玩家不同 Pose/ActionTime |
| 普通怪物/NPC | 同種共享 Texture/AnimationDefinition/Material | 資料導向可視 renderer，少量 Node/CanvasItem；必要時 RenderingServer |
| 大 Boss/機甲/寵物 | 可分動作頁、特殊部件 | 獨立或分層渲染，不為 batch 犧牲動作正確性 |
| 掉落物 | 少數 item visual atlas；同類共用 | 位置與物品 ID 緊湊表、區域批次；避免逐個 Timer/Area2D/Scene |
| 地圖 | 區塊 TileAtlas | Camera WorldRect + Chunk Streaming |
| UI/FX | Theme/NinePatch/FX page | 與 World/Network 的資源 owner 分開 |

MonsterRenderer 可以比較 Sprite2D / CanvasItem / RenderingServer 與 MultiMeshInstance2D；**MultiMesh 使用不同 Atlas UV、不同動畫及 Y-sort 有額外 shader/排序限制，未 benchmark 不選定**。掉落物不動時不重建每個 RenderCommand，點擊用獨立空間查詢，Server 撿取權威不受渲染精簡影響。玩家換裝只替換相關 layer，不能預合成所有外觀的笛卡兒積；相同外觀合成 cache 需 bounded LRU 和失效規則。

## 5. Runtime Asset Streaming 與記憶體

```text
Catalog / Map Palette → Prefetch + Prioritized Request Queue
→ Disk Read / Decode / Prepare（非同步、有界）
→ Main/Render-safe GPU Commit（時間和 bytes 預算）
→ Hot GPU Handles + Optional Warm Prepared RAM + Disk Compiled Pack
→ RefCount/TTL/LRU/Hysteresis → Safe Release
```

- 不強制 RAM、VRAM 各保留一份完整解碼貼圖；依 Godot ResourceLoader 實際行為量測 resident memory。
- P0：自己角色、畫面可見玩家必備動作；P1：戰鬥中的敵人/受擊動作；P2：近場怪與地圖；P3：掉落圖示、裝飾；P4：遠區及不常用技能。**僅排序視覺準備，不排序/丟棄 Server 可靠事件**。
- Background loading 中避免 blocking get；資源完成後的 GPU upload/scene bind 也要限制每幀 commit budget，測 FirstVisibleDelay/UploadSpike。
- 設 preload margin、較大的 unload margin、最短保留時間、防止界線進出反覆 cache churn；切圖、登出解除所有 world owners，shared/global 明確計入基線。
- Cache key 包含 VisualDefinitionId、version/hash、material、quality、appearance，cache 容量有上限，裝備改變需 invalidate。
- 缺圖只影響視覺，不得令 Entity/HP/Position/Action 消失。資源完成時以**最新** WorldState 直接顯示現在 pose，不回放舊 Move。

## 6. 多視窗尺寸與鏡頭縮放

相同 Atlas/Animation Metadata 服務各解析度。區分「相同世界視野但像素變多」及「WorldRect/Zoom 擴大、同屏更多實體」；前者偏 GPU 像素成本，後者會擴張 Chunk 和 VisibleInstances/AOI/資產工作集。必須從 Camera Transform 計算實際世界可見邊界及預載 margin，不可沿用原版 800×600 的固定距離。

可選 QualityVariant 應離線生成、用同一 AssetId 對應；像素美術保真優先。Server 對巨大視野的 AOI 需明確上限與協商，不可讓 Client 已顯示的關鍵角色不更新。

## 7. AS007 實測矩陣與驗收 Gate

比較 A 個別 PNG/SpriteFrames、B 固定 1024、C 固定 2048、D 混合 512/1024/2048/4096、E D+Map co-occurrence hot/warm/cold、F E+不同 rendering backend。保持**相同可視實體、動畫、貼圖解析度、材質品質與硬體**；測試同圖「500怪/8種、2000掉落/25種」的高重用示例與高 unique 種類壓力例；另測不同裝備玩家、Boss、快速切圖、100次登出、800×600/1280×720/1440×810/1440×900/Zoom-out。先前 1080p/4K 測量僅為超規壓力參考，不是正式視窗需求。

測量：UniqueAssetCount、VisibleInstances、AtlasPagesTouched、PageFillRate、DrawCalls、TextureChanges、Overdraw、RAM WorkingSet/PrivateBytes、VRAM Dedicated/Shared、Frame P95/P99、Load P95、CacheHit/Miss、UploadSpikeP99、EvictionChurn、LogoutGrowth，以及獨立的 network/reducer telemetry。

三組 HK2 真素材已作一次 Godot/OpenGL 300 同種類實例 A/B，見 [實測記錄](HK2_ATLAS_CANDIDATES.md)；它不涵蓋此矩陣的多種類、掉落、快取和解析度案例。其他未實測項目仍為 **NOT VERIFIED**。原版逐格 Viewer、授權與完整 AS001～AS009 Gate 尚未完成。
