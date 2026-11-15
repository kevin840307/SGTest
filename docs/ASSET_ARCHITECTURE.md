# SGTest — Asset Architecture

**目前狀態：Height-Dense P0 Builder + 外部 PCK Converter 已整合與測試；新版 Client AssetStore 已有有界非同步／owner-epoch 資源基礎與 headless 生命週期回歸；Terrain Renderer 已有原生 GPU／像素切片；完整 World Renderer／真 GPU 生命週期仍未完成驗證。** 正式實作位於 `tools/asset_converter/atlas/dense_atlas.py`、`runtime_pack.py`，不需另一個維護路徑。

## 一個原始素材 = 一個獨立資產

- Enemy/<id>（原資料夾名稱保留 Enemy，不使用 monsters）
- NPC、Player base／部位、Equipment、Pet、Mecha、Effects、Scenery、UI、Item 等**每個 logical asset 物理獨立**。
- 同一 asset 的方向、動作、Frame 可以共用 Atlas；**不同 asset 不可合圖**，包括不同 Enemy、Map+Monster、不同 scenery、Player+Equipment。
- 不依 action/direction/source/pixel-size 建立硬分桶，也不在 assets 下多建立 generation 資料夾。

## 素材編譯

~~~text
Old SPR/ACT/SIF/DrawOrder / Map
       ↓ parse + pixel/origin validation
one asset ID -> frames (+trim/pivot/timeline/flip/event)
       ↓ Height-Dense / candidate evaluator
one asset -> Atlas pages + manifest
       ↓ imported Texture2D / compression
external assets.pck -> ResourceLoader -> shared AssetStore
~~~

**最新推薦**：每個 Asset 內依 Frame 高度再寬度降序，候選寬高動態取最佳；偏向一頁 <=4096×4096，單一素材確實超限才分頁。1024×256 等 2 次方矩形桶可保留候選，**不強制**。P0 (0px padding) 省資源，但必須驗證壓縮和過濾不污染透明邊；必要才切 1px extrude。

原型獨立 Benchmark 說明 Render/Loader 選擇：壓縮 Texture2D + 共享 SpriteFrames/AnimatedSprite2D 在多種怪物負載下能兼顧低 P95 與 RAM；單一超大型 Enemy 部分情境 Recovered 較快。這不是新版可執行 Client 的 Gate；詳 [research/ASSET_RENDERER_EVIDENCE](research/ASSET_RENDERER_EVIDENCE.md)。

## Runtime logical tree（本機生成，不進公開 Git）

~~~text
client/assets/
├── catalog/
├── maps/<map-id>/           # terrain/layout/blocking/scenery references
├── maps/visuals/<asset>/    # 每個場景素材獨立
├── Enemy/<asset>/
├── npcs/<asset>/
├── players/<asset>/
├── equipment/<asset>/
├── pets/<asset>/
├── items/<asset>/
├── effects/<asset>/
├── ui/<asset>/
├── audio/
└── data/
~~~

Map terrain/scenery 和 Enemy 必須分離。Server Population 定義怪物出生／外觀 logical ID；地圖不應偷放怪物像素。Asset owner 是 app / character / map / on_demand **生命週期 metadata**，不是 bundle/資料夾分組。

## Loader/發行契約

單一 AssetStore 按需 acquire／release／cancel，map palette 可預取**參照**但不能使不在畫面的整張地圖怪物常駐 VRAM。原始視圖資料要保留 8 方向 alias、鏡像、Origin/trim、逐幀 ticks、attack/hit/death 時序、paper doll draw order。

外部 PCK 與主程式分離；Godot imported VRAM compressed Texture 的 Windows 桌面與 macOS 所需格式須分平台驗證。**PCK 只是封裝，不是 Atlas 共用依據**。Windows 取得的壓縮結果不能推論 macOS 實測已通過。

## 已落地的可維護策略

將 Atlas layout 與 Godot Renderer **分開決策**，防止日後替換 Canvas／Sprite 就必須重新轉換原始素材。現在正式 Builder 已保留：每素材獨立、Height-Dense P0、依尺寸自動選擇單頁、原版方向/Frame/Origin metadata、失效 cache 來源雜湊檢查。外部 PCK 使用 Godot VRAM-compressed importer；小型 UI/Icon 保持 lossless。GPU format 由平台支援情況決定，不假設 BPTC 等於 Mac 的實際硬體壓縮格式。

**Runtime 首選**為同 Enemy 共用 `SpriteFrames`、可見實體獨立 `AnimatedSprite2D`、所有類型共用 World Y-sort；但屬於新 Client 待實作，不把隔離式 Benchmark 當成已交付的遊戲功能。

## 新 Client 資源 owner

`client/assets_runtime/asset_store.gd` 已從零實作。單一 Store 管 Atlas／terrain，metadata 背景 worker、threaded Texture 與取消 drain，不新增按類別分開的 Loader／Cache；map palette 只持參照，載入種類仍由 owner 決定。owner epoch 是資源世代，和 Server MapEpoch 分開。scope 冪等持有，資料唯讀且跨 owner 共用；end_scope 不會強制釋放外部 Scene 還保留的引用。具體容量／取消 API、30 次 palette 測試與 Texture job 1/2 比較見 CLIENT_ARCHITECTURE。RGBA 預算值是保守頁面估計，headless 引用歸零不等於 VRAM／RSS 返回基線，實機 Gate 保持未驗證。

### 2026-11-16 景物階層契約修正

地圖資料改world v3，AssetStore拒絕v2，需重跑runtime-only及PCK。ObjectList的XY後整數為子物件數而非Z；Map15保留5棟父建築與15子物件，parent_index使用來源objects陣列索引，子物件p仍為絕對座標；所有z=0。維持唯一AssetStore／Atlas／PCK，不增加第二套載入流程。原版建築有mode與前緣比較、父子顯示關係，不能將「共同World排序」直接等同所有Node2D啟用y_sort。DIRECT來源／位址／schema遷移詳ASSET_CONVERTER；原版遮擋、室內外、動畫時間及完整景物Renderer仍NOT VERIFIED。
