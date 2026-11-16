# SGTest Client — 重建設計

**狀態：AssetStore 資源基礎已實作並有 headless 回歸；Terrain Renderer 已有 Native GPU 切片；完整 World Renderer／Network／WorldState／玩法仍待實作。** 目標 Godot 4.7.2 / GDScript；功能導向的單一 Godot 專案。

## 職責分離（不要為了分層先建立空檔案）

| 責任 | 輸入 | 輸出 | 不可以做 |
|---|---|---|---|
| Transport | socket bytes / outgoing commands | 有界接收佇列／傳送結果 | 操作 Texture、UI、World Node |
| Decode | 協定選型後的 framed bytes／binary payload | 驗證過的 versioned DTO | 修改場景或綁死某種序列化函式庫 |
| World reducer | 有序事件、snapshot、Epoch | 可回放的 Client WorldState | 等待 Asset、依 Render FPS 推進 |
| Prediction | 本地輸入／Server correction | 自己平滑座標、可重播 command | 替 Server 決定合法傷害 |
| Interpolation | 每實體版本化位置歷史 | 遠端視覺位置與 Facing | 將畫面座標寫回權威 state |
| Assets | logical ID、owner、generation | Texture／FrameTable 或取消 | 為每種類重複建立一套 Cache |
| Renderer | 唯讀 WorldState／動畫 metadata | 視覺節點與 Y-sort | 消耗網路封包或推進世界邏輯 |
| UI | ViewModel、使用者意圖 | 指令、HUD、系統偏好 | 直接改 Network reducer／Server state |

composition root 僅做啟動／組裝／生命週期協調，**不得重建舊版大而全的 main.gd**。

## 不受 FPS 影響的更新

~~~text
socket worker / decode / ordered inbox — independent
client reducer / command dispatch      — fixed or event-driven
local/remote movement state             — tick + sequence timestamps
visual interpolation + animation        — configurable FPS
AFK/Unfocus                             — may suspend only visual work
~~~

需要建立**同一 packet trace**在 0、30、60、90FPS、失焦、縮小、AFK，以及多開狀態下的狀態 hash、事件序號和 queue-age 對照。

**執行機制要可驗證**：Transport/Decode 不得由 `_process()` 或 UI 動畫回呼輪詢；Reducer 必須具獨立時鐘／調度，不可因背景視窗停止繪製就停止處理。背景 worker 不得直接操作 Godot SceneTree、Texture 或 Control；狀態變化由明確、安全的跨執行緒交接提交，渲染僅讀取穩定快照。

## 一致的 World Y-sort

所有可能遮擋的 Player、Enemy、NPC、Pet、Scenery、Ground items 應在**統一的 world-space sorting contract** 裡；不能讓 Enemy Sprite 子節點繞開角色／場景的排序。若部分效果採 MultiMesh 或集中 Canvas，必須有視覺驗收。

- Enemy 首選候選：每種怪共享 SpriteFrames／AtlasTexture；每個可見實例 AnimatedSprite2D；逐幀 Origin／翻轉／ACT 時間與事件一致。
- Player：紙娃娃各部位不同資產，DrawOrder 與切換動作不可用單一 Enemy Sprite 規則取代。
- Monster、NPC、Equipment、Map、UI 的**素材存取共用單一 AssetStore**，不是合成同張 Atlas。
- Render 0 FPS 仍要保留 WorldState、可靠事件和 reconnect/teleport 正確性。

## AOI 與視覺狀態的邊界

- Client 只保留自己 MapEpoch／AOI 訂閱範圍內需要的遠端實體視覺資源；WorldState reducer 按 Spawn/Despawn、版本與 WorldReady 同步，不以 Client Render FPS 控制訂閱或收包。
- 相機／解析度／縮放改變時，提供 viewport 與 interest hint；**Server 仍為 AOI 權威**，依上限與更新節流決定實際可見集合；Viewport 邊緣加緩衝／hysteresis，遠端 Spawn 要帶初始位置與動作以免外圍瞬移。
- 位置與 Facing Delta 使用獨立 interpolation history；僅合併**已被更高版本取代**的非可靠狀態，不把 Spawn/Despawn、Damage/MISS、掉落拾取與交易結果當可隨意丟棄資料。
- 不在視野／AOI 內的遠端視覺節點可卸載或池化，但 AssetStore 仍需遵守 owner 引用與恢復的 epoch；AFK 可明確協議低視覺 AOI，不能減少可靠狀態。

## Asset lifecycle

從獨立外部 PCK 掛載 res://assets/**，以 AssetCatalog 解析，單一 AssetStore 負責有界 worker、GPU commit、owner 取得／釋放／epoch 取消與快取淘汰。新的檔案先驗證 source path、Origin、page 和 frame 索引。切圖與登出後無持續累積的 Texture、SpriteFrames、Node。

**滿載／缺序處置**：Movement/Facing 的舊快照可按 EntityRevision 合併；Spawn/Despawn、換圖、角色／物品／戰鬥的權威結果不得因渲染慢而悄悄丟掉。若發現可靠事件缺序、MapEpoch 不符或佇列溢出，停止套用有歧義的增量，要求新 WorldReady／完整快照；不得無限擴張 inbox 或用落後畫面假裝已同步。詳 [Wire Contract](RUNTIME_PROTOCOL.md)。

## UI 與解析度

新 Godot Theme/Control 先自製後期《星夢 Online》金色 UI；現代 Theme 等未來需求確定再評估，**不必提前實作雙套 UI**。支援多解析度，camera viewport 改變需更新可視 AOI，不可固定舊 800×600。移動速率、機甲速度與動畫播放速度解耦。

## MVP Client：Map15 + Map13，完整玩法與真實切圖

先由 Converter 的兩張原圖建立**單一可重用 Map Loader/AssetStore + Renderer**：Map15 主城 UI/NPC/裝備/交易，Map13 A*／地物／Enemy／Drop／Combat；原始 Scenery/Y-sort、八方向／鏡像／ACT timing、相機和不同解析度需一致。兩個真 Client 分別在同圖及不同圖測 AOI、人物／怪物／寵物／機甲 Facing、Attack/Hit/Death、可靠事件。

**Map15 ↔ Map13 是 MVP 真實 E2E**：離圖必須清空舊 MapEpoch/AOI/節點、釋放非必要 Texture；入圖需 WorldReady baseline、完整新可見集合及 ResourceLoader preload/cancel，往返後不得殘留老地圖、變黑圖或重播錯誤事件。UI 的 NPC 銀行倉庫商店、角色物品裝備／技能、Pet/Mecha、交易/玩家商店/PvP/GM 必須有完整操作及 Server authority 回饋，不是空窗 Demo。

Map13 同圖測 20 連線／300 Enemy／500 Drop（兩個真 Client 測畫面，Bot 只測負載），Map15 多人互動、兩圖同時活躍；FPS/AFK/Unfocus 對網路和 reducer 不可節流，記錄 CPU/RSS/VRAM／Queue/Frame tail latency；MVP 後才擴所有地圖與內容。

## 開發順序／Gate

1. 最小 socket/codec + bounded queue，驗證低 FPS 不影響收包。
2. Reducer/WorldState/MapEpoch/Prediction/Interpolation，packet trace 可重播。
3. AssetStore／外部 PCK／ResourceLoader，30 次生命週期測試。
4. World Y-sort + Enemy Sprite + Player paper-doll（含 ACT/Origin/Direction）。
5. 真 Server 多人 AOI、戰鬥及動畫中斷＋UI／AFK。
6. Windows Release 與 macOS 真實 GPU 驗證。

已有的 Model1/SGTest 隔離基準見 [research/ASSET_RENDERER_EVIDENCE](research/ASSET_RENDERER_EVIDENCE.md)，**不等於此新 Client 已測試通過**。
## A* 尋路、點擊走路與長按走路（重建必要）

- **碰撞資料來源**：使用原版 Blocking.dat 的可走網格，Server 決定最終可達性。Client 可用同一份無損網格做局部 A* 尋路／預測；地形高低差與跨障礙通道要依原遊戲資料及真實位移驗證，不能僅靠畫面矩形。
- **路徑規則**：地板點擊與怪物追擊都從可通行目標／鄰近可達點規劃；必要時重規劃（路線被阻擋、Server correction、目標移動），帶節流／取消代次，避免每 Render frame 跑全圖 A*；斜角不得穿障礙角。Server 可以驗證輸入意圖和路徑節點，拒絕穿牆或非法速度。
- **長按**：長按約 **1 秒**進入鎖定移動；左／右鍵或再次點地板取消鎖定（需保留右鍵選單的正常互動優先權）。鎖定後是「保持目標／導航意圖」，不是每 Render frame 重送相同 Move 封包。目標變更、遇障、傳點與 UI 鎖定要中斷乾淨。
- **方向與動畫**：Facing 依原版 8 方向／鏡像，走路距離由權威數值與 prediction 決定，動畫以 ACT tick 播放；移動不應重啟正在有效執行的 Attack／Hit／Death。
- **參考**：可唯讀比對 Recovered Client 的行走、A*、長按／游標、原版封包節奏與 `Old/Paradise.exe.lst`。測試必須包含台地階梯、追怪繞障、動態重規劃、不同解析度視野外圍與移速變化。

## AFK、Unfocus、最小化與多開

| 模式 | Render/UI | GPU/Texture | Network / World reducer |
|---|---|---|---|
| 正常 | 使用者選擇 30/60/90 或其他支援上限 | 僅可見／必要資產 | 正常獨立頻率 |
| Unfocus（可開關） | 可降幀或跳過不必要視覺更新 | 可延遲非必要繪製 | **維持正常** |
| 最小化（不受 Unfocus 開關限制） | **0 遊戲畫面幀** | 可釋放大量非必要 GPU 資源 | **維持正常** |
| AFK | **不渲染遊戲世界；僅極簡狀態／摘要**，音效預設關閉 | 在契約允許下釋放地圖／動畫／特效 Texture 與 Node | 保留可靠事件和角色狀態；若需降低非關鍵快照頻率，須由 Server 明確協議 AOI/Interest，與 FPS 完全無關 |

切入 AFK 不可用 `Engine.time_scale` 或停用封包輪詢來省電；工作佇列仍有界，非必要視覺命令可合併，但每個 Damage/MISS/Item/Trade/Death/Teleport 等結果不能丟失。回到正常模式必須重建視覺快取並向 Server 確認 WorldReady／MapEpoch，避免幻影物件。AFK／Unfocus 不得削減玩家必需的權威同步。

多開測試：正常＋AFK 的混合 1/2/4/8 實例；限制每個背景實例的 CPU 動畫提交、GPU texture 存量與快取上限，紀錄各程序 RSS／CPU/GPU、隊列 age、斷線及回復時間；不可藉降低封包正確性換低功耗。

**高優先順序 Gate**：用同一份封包 trace，在 Render=0／30／60／90、AFK、Unfocus、最小化時 WorldState hash／可靠事件序號相同。所有遊戲模式獨立於渲染線程。

## MVP002-ASSETSTORE 實作與選擇（2026-11-15）

唯一實作 `client/assets_runtime/asset_store.gd`：RefCounted owner／epoch store，Catalog 索引＋既有 maps/<id> world 契約；背景 DecodeJob（FileAccess／JSON／SHA／幾何驗證，無 SceneTree）與 Godot threaded ResourceLoader；main-thread pump 接收完成結果，唯讀 FrameTable／manifest／Texture 由不同 owner 共用。scope release 之後，無 owner 的 entries 直接移除，過期 serial 的工作排空後丟棄；不發舊 epoch ready、不持有額外 idle Texture cache。`begin_scope`／`end_scope` 不是伺服器 MapEpoch，而是本機視覺資源取消世代；不可拿它替代權威版本。

數量預算：metadata worker 2、Texture job 預設 1、active assets／scopes 各至多 256、RGBA 頁面估算 256 MiB。主 pump 的 max_commits 限制完成 Texture 的交接數，Godot／driver 的實際 GPU upload 時間仍須另測；metadata 初始化時只讀固定 Catalog 索引，完整 manifest／地圖 companions 在背景解析。兩 owner 同素材共用同 Texture instance。外部 consumer 若還握著 Texture／data，Store 不能強制回收；離圖必須先拆 Scene／放下引用，再 end_scope，並持續排空取消工作。

### ALT001：Texture job 並行數（同程式、同資料、同 trace）

測試：Godot 4.7.2、Windows headless/dummy、同 PCK SHA `4df45ef43ffcbe03e2e5255bca087baeefaf45210ddb7ca9ecca93714d76ae7a`；每個候選 fresh process 三輪，每輪 30 次完整 Map15／Map13 palette 往返。Map15 221 Asset／Map13 63 Asset，包含 terrain、全部當圖地物／NPC／Enemy 與 B Player 七部位；不改畫質／素材數量，沒有 Network 或 Server Tick。

| Texture jobs | 正確性 | 每輪 wall 秒 | Engine CPU 秒 | Engine Peak RSS bytes | pump P95／P99 µs | 決定 |
|---|---|---|---|---|---|---|
| 1 | 3/3 PASS，無引擎錯誤 | 32.270–32.732 | 13.156–17.641 | 146,333,696–149,008,384 | 304–370／420–502 | 預設 |
| 2 | 0/3，dummy texture 初始化錯誤／RID leak | 18.594–18.605 | 12.969–13.406 | 149,458,944–150,761,472 | 285–332／331–440 | 淘汰 |

DIRECT：2 jobs 的三輪引擎日誌分別記錄 17／7／3 個 error/leak 診斷，即使腳本的 Store counter 已歸零也判 FAIL。HYPOTHESIS：Godot dummy Texture storage 在並行建立／回收時有競態；未追蹤引擎組語／原始碼，不宣稱已證明根因或一般 GPU backend 也相同。1 job 的完整 trace 三輪無診斷，選其為可驗證的簡單 baseline；未宣稱為真實遊戲最佳吞吐。這只調整非必要視覺載入排程，沒有放慢網路／World Tick。

實際 engine exe 由 Windows console wrapper 的 sibling 執行，CPU／Peak RSS 不含 wrapper 的假低值。初次 wrapper 計數已標為不可用，保留於 `reports/client_asset_store_launcher_comparison.json`；正確報告為 `reports/client_asset_store_engine_comparison.json`／`client_asset_store_validation.json`，含 raw logs、commands、PCK／程式來源 hash。背景其他程序未控制，CPU／wall 數值為此 trace 的描述性量測；pump 百分位是函式 CPU 區間，不是遊戲 Frame／input latency。

通過內容：唯讀資料／共用實例、consumer 放下引用後 Texture weak ref 釋放、在途 Texture 取消／舊 epoch 無 ready、完整 palette 每次 release 後 owner／entry／job／RGBA 預算歸零、容量與 byte budget 拒絕、壞 metadata fail closed。峰值 221 active assets，RGBA 預算計數 70,769,968 bytes。真正顯示視窗、GPU/VRAM 釋放／記憶體 slope、SpriteFrames／Y-sort／NPC timing、AFK 邏輯與 packet trace／多人 E2E 均 NOT VERIFIED；REB012／MVP002 的整體 Gate 保持未完成。重跑命令見 client/README.md。

補充 `test_asset_store_epochs.gd` 三情境 PASS（runner --script tests/test_asset_store_epochs.gd）：同地圖 CPU job 在途重取、同 Texture path 在途重取、ready handler 同時釋放兩個 owner。舊 serial 不得通知新 scope；回呼重入不會存取已移除 owner 或向已結束的 peer 通知。raw report=`reports/client_asset_store_epochs.json`，沒有改變上述完整 palette 比較的 trace。

## MVP002-TERRAIN：原始 tile 地形選型（2026-11-16）

DIRECT 來源：唯一 `tools/asset_converter/content/build_maps.py::build` 保留原始 Map13／Map15 tiles.bin：8-byte little-endian width/height、480×480 uint16 原始 tile ID、65535 空格；world.json 的 tile_ids 確定性指向 terrain.png 格位，tile_side=32，world=15,360×15,360 pixel。Renderer 不改來源／邏輯 ID／原始座標；`TerrainData.configure` 驗證 WH／長度／ID／Atlas尺寸後，建立私有繪圖索引及矩形。Synthetic fixture 驗證空格、未知／重複原始 ID、尾端 bytes 拒絕、可視區越界及重新 configure 清掉舊 view。

正式唯一 Renderer 是 `client/rendering/terrain.gd`。以 Node2D `_draw` 對可視範圍（多一格 margin）呼叫 draw_texture_rect_region；只在可視 tile 矩形變更時 queue_redraw，使用 nearest 與 clip_uv。每張圖只有一個 ground node，共用 Store terrain Texture，沒有全圖 Mesh／額外 Texture cache，ground z=-100。這是呈現層，不承擔碰撞／移動或 authority。地物／人物排序和來源 Z/pivot 另有 Gate。

### ALT001 三個合理候選

| 候選 | 穩態 Frame P95／P99 ms | 首個 view 就緒 ms | 遠距跳轉 P99 ms | Process Peak RSS MiB | Engine buffer peak bytes | 結果 |
|---|---|---|---|---|---|---|
| 可視 tile 區域 Canvas | 0.601–1.183／0.648–1.277 | 1.044–1.306 | 0.961–1.157 | 188.23–190.77 | 12,801,208 | 正式 baseline |
| 固定32×32 tile全圖Mesh | 0.314–0.576／0.420–1.497 | 228.867–240.498 | 0.387–2.912 | 242.25–244.21 | 32,441,800 | 僅測試 |
| 可視Mesh、32 chunk硬上限／LRU | 0.285–0.606／0.687–1.993 | 3.054–5.335 | 9.619–19.123 | 194.45–195.14 | 16,642,996 | 僅測試 |

Windows／Godot 4.7.2／NVIDIA GeForce RTX 3050／OpenGL3；VSync disabled、無 FPS cap，1280×720 與1920×1080，Map15／13。同 PCK／terrain Texture、相同相機座標 trace；各組90-frame暖機＋240-frame樣本、另40次相同遠距跳轉，三輪交替候選順序、fresh process。兩 Mesh 策略共用同 builder；已把 corners/triangles 常數與 texture size 查詢移出 per-tile 內圈再量測。全圖225 chunk 與 LRU32 chunk 的保留策略是有實質差異的資源／延遲候選，不增加正式 Loader。

三候選均3/3 PASS，無引擎 ERROR／leak。每輪每候選16張 GPU capture（兩圖×兩解析度×原點邊界／圖內／0.25,0.125小數相機／遠端越界），由獨立 Python PIL 路徑使用原始 tiles.bin 與 Atlas 合成預期 RGB，逐像素完全一致；三候選三輪共144張，非144個獨立視角，也非全世界所有像素。空格與壞資料另有人工 fixture。

**決策**：Mesh 穩態快，但全圖建立與約54MiB額外 RSS 成本高；可視 Mesh 節省保留量，跨視野重建 tail 仍明顯。區域 Canvas 在此 trace 的 P99／首次呈現／跳轉／維護成本較均衡，選它作當前 baseline；未宣稱一般硬體或含大量角色時仍全面勝出。兩 Mesh 留在 `tests/terrain_*_candidate.gd`，正式 rendering/ 只保留 TerrainData＋選定 Renderer。實作搬到正式路徑後的 GPU 回歸亦 PASS；最終優化後完整重測使用目前正式路徑。

Raw report=`reports/terrain_decision.json`，含每候選／每輪／每圖／解析度的 P95/P99/Max、準備時間、更新時間、draw calls、記憶體、process CPU、來源 SHA／PCK SHA／command／GPU captures。三候選 engine CPU 秒範圍分別5.125–5.219／5.313–5.484／5.688–5.797，包含載入、截圖編碼、準備與 trace。Frame 是 render-only native loop 區間，第一 view 不含共用 tiles 正規化（約10.2–11.4ms）和 AssetStore 載入；不是可玩 Client FPS／input-to-render。網路不適用。外部背景程序未控制，OS／driver spike 不作 hard realtime 保證（觀測 Frame Max：區域1.702ms、全圖9.516ms、可視chunk3.828ms）。

引擎 Texture counter 在相同 viewport 尺寸歸還後回到13,373,731 bytes；buffer pool 留有高水位，這只代表引擎計數，**不等於 OS dedicated VRAM／RSS 全部回到基線**。GPU／driver cache、30次完整 World 生命週期與 AFK、terrain混合地物／角色／Y-sort／pivot／動畫、相機 zoom、macOS／Release、網路與多人 E2E 均 NOT VERIFIED。初版比較曾誤把LRU上限套到全圖候選，已修正並重跑；不以該錯誤淘汰全圖 Mesh。早期只有快取候選跑遠距跳轉／wall混入Python比對的報告不作最終選型數據，最終三候選使用相同 trace 並只記原生程序 wall。

### 2026-11-16 景物階層契約修正

地圖資料改world v3，AssetStore拒絕v2，需重跑runtime-only及PCK。ObjectList的XY後整數為子物件數而非Z；Map15保留5棟父建築與15子物件，parent_index使用來源objects陣列索引，子物件p仍為絕對座標；所有z=0。維持唯一AssetStore／Atlas／PCK，不增加第二套載入流程。原版建築有mode與前緣比較、父子顯示關係，不能將「共同World排序」直接等同所有Node2D啟用y_sort。DIRECT來源／位址／schema遷移詳ASSET_CONVERTER；原版遮擋、室內外、動畫時間及完整景物Renderer仍NOT VERIFIED。

### 2026-11-16 景物共用定義與alternate覆蓋

world v3新增scenery_schema v1；scenery_definitions按原始BLG/STH共用metadata，placement保留definition／parent_index／絕對p，避免重複點集。定義包含sorting_mode、SIF draw_group、前緣raw/effective、兩appearance與來源hash。Scenery／角色共同排序必須區分sorting_mode與draw_group；GetMode的0/1/2與建築前緣不能用Godot單一Y值取代。此為metadata切片時狀態；目前靜態快照Renderer進度見下一節，完整原版時序仍未完成。

AssetStore仍是唯一資產入口；測試palette改遍歷所有appearance，Map15從221增至233 Asset（含terrain及Player等），Map13仍63。Catalog新增12個alternate，總254 Asset，PCK更新見ASSET_CONVERTER。30次palette循環／3競態與16張terrain GPU像素回歸PASS；這不是建築切換／NPC timing／多人同步／VRAM斜率測試。下一步以可重跑的原版front比較及兩欄切換fixture實作景物與角色遮擋，fade cadence須再驗證。

## 靜態World景物／角色交錯切片（2026-11-16）

正式使用`world_order.gd`／`world_draw_list.gd`／`world_canvas.gd`，共用同一AssetStore的Texture與manifest。Draw list不做ResourceLoader／網路／世界更新，Caller用穩定Actor輸入順序、明確clip-frame與inside快照驅動；沒有_process時鐘或自行假設NPC／景物FPS。全map metadata先configure，預先合併root+children保守bounds，可見root保持離線順序，Actor只插入可見main list；同Y保持輸入順序，不能依相機距離改順序。inside快照true選primary並draw children，false選alternate（缺alternate回primary）。目前是兩端狀態驗證，原版中間fade／入口／自身ghost與動畫cadence尚未實作。

原始SortMapObject是**交換predicate而非嚴格全序**；合成兩建築可出現swap(A,B)及swap(B,A)都true。Converter依DIRECT CELList::BubbleSort 0x0052AB10–0x0052ABED，做相鄰交換至settled或1025輪上限，寫入world.scenery_order v1（0/1/2三組root索引及passes／settled）。原版三組sort呼叫在0x005297BD–0x005297F7；PointInFront 0x00549560、建築互比0x005497EE及InsertObjectToDrawList 0x0052A6CC提供Client幾何。來源仍為ASSET_CONVERTER所列同SHA的Paradise.exe.lst，沒有複製組語進Repo。Map13三組33／334／0 roots、1／29／1輪；Map15 89／1744／0、2／71／1輪；兩圖全部settled。Client不對它呼叫sort_custom，也不每幀重新排序全圖。

每frame按`position + trim - origin`及原始Atlas region畫；合法空白SPR幀使用page=-1／zero rect，不應錯當缺素材，亦不應偷偷換成前一幀。configure拒絕重複root、未知definition／clip／page、越界父子關係；無效frame回ERR_INVALID_PARAMETER。全clip驗證需支援JSON的float數字，不能拿float Array與int Array型別差異誤拒絕空白幀。

### ALT001候選與結果

相同原始254 Asset PCK、同一draw list／Actor／camera trace、RTX3050 OpenGL3、720p及1080p；集中Canvas與**重用**Sprite2D各fresh process三輪。每map／round有每解析度60 warmup＋120樣本（總240），每三步一次遠距切換（總80 jump）；另24 captures／round/candidate覆蓋兩圖、密集／疏場景、inside兩端與frame0/1，共144。兩候選都無engine error/leak、排序／UV／source identity一致，110原始SPR幀的origin與未壓縮Atlas crop逐byte一致。

| 指標（三輪範圍） | 集中Canvas（正式） | 重用Sprite2D（僅tests） |
|---|---:|---:|
| Map15準備P95／P99 ms | 1.150–1.250／1.223–1.460 | 1.394–1.622／1.478–1.791 |
| Map13準備P95／P99 ms | 0.526–0.557／0.541–0.626 | 0.674–0.690／0.688–0.793 |
| Map15 Frame P99 ms | 2.456–3.414 | 3.206–3.415 |
| Map13 Frame P99 ms | 1.591–3.346 | 1.731–3.147 |
| Map15 jump P99 ms | 2.655–3.451 | 3.381–3.790 |
| Map13 jump P99 ms | 1.601–4.812 | 1.737–3.199 |
| Map15首次view ms（不含Store／configure） | 1.998–2.107 | 2.190–2.402 |
| Peak Process RSS MiB | 213.89–217.20 | 213.80–214.79 |

兩候選draw calls相同（測點Map15=129／Map13=51，含viewport預覽與terrain）；engine Texture counter同為Map15 55,603,407／Map13 29,838,159 bytes，歸還相同720p viewport後都回13,373,731；Buffer pool保留12,801,208，Canvas一次高水位14,931,400。這些counter不等於OS dedicated VRAM或RSS全回基線。每個fresh process含載入／readback／PNG的CPU為Canvas12.422–12.750秒、Sprites12.484–12.703秒；runner wall還含Python驗證，不拿它冒充Frame延遲。共用configure：Map15約13–14ms，Map13約2.6–3.4ms，未納入first-view欄。

**決策**：Frame tail跨輪／map沒有穩定全面勝負，RSS差異小；Canvas準備成本較低且只有一個Canvas節點／短繪製迴圈，不必維護可變Sprite節點集合，依「差異不明顯選簡單」選它。並未宣稱低階GPU／300怪物／500掉落或大量移動Actor時仍最佳；那些需在後續同負載Gate重測。固定景物共用Atlas／FrameTable與Store，不導入第二套素材／網路／WorldState。

### 像素oracle的精度與限制

原始SPR→未壓縮Atlas的crop、origin／trim、命令排序／座標／UV／頁面identity是嚴格比對。GPU紋理在非4倍數頁面可能有block-aligned底層影像而Texture2D保留logical尺寸；CPU參考用normalized nearest取樣重現它。只有**1e-5 texel**範圍的精確邊界允許相鄰取樣，對alpha重疊傳遞上下界；色階rounding容許2，實際所有capture超出取樣界的最大差1、超過2的pixel=0。不可把它寫成原圖／GPU RGB逐pixel零差異。原始P0視覺品質、BC7匯入resize是否應用padding避免、Mac／其它backend仍NOT VERIFIED。

驗證器負向控制：錯誤順序／destination+1都因命令契約拒絕；整張畫面x+1導致789,194 pixel超界而FAIL（`reports/world_render_negative_controls.json`）。不是放寬整張圖誤差來通過。最終`reports/world_render_final.json`含source SHA前後一致、實際engine／raw metrics／144 capture；較早pilot的GDScript型別、空白幀型別判定及未處理GPU底層尺寸／tie的FAIL已修正，不作正式PASS證據。

尚存：動畫時間／事件、原版fade cadence與入口／自身ghost、視野資產非同步預取的ALT、完整World生命週期斜率／AFK／多開、Camera zoom、Player紙娃娃／Server／多人E2E、Mac／Release。全map palette預載只是本輪公平比較前提，不能作最終低資源載入策略已完成的宣稱。

## 景物ACT預設時序與建築fade（2026-11-16）

新增`action_timeline.gd`：只接受action_timeline/v1的50Hz／40÷25預設profile，保留原始frame index與raw_time，以`max(1,raw_time*40//25)`建立累計updates；binary search取樣，不因Render呼叫次數改變狀態。非loop停在最後frame，loop按總updates取餘數，raw_time=0至少1 update；SetSpeed動態覆寫不在此sampler範圍，不能把移動速度或FPS當倍率。WorldDrawList按Asset+clip共用不可變timeline，`frames_at_update(now, started_at)`回傳placement幀；phase／epoch與fixed-update計數由caller擁有，clear／換圖清除綁定。沒有_process／Timer／網路或ACT事件消耗。

只有sorting_mode=1的CELStaticObj2D綁定字面`action`；BLG不能因共享同Atlas就繼承動畫。雙圖254 Asset裡8個景物ACT已由同一Exporter嵌入v9 manifest.act；Map13 5種／17個實例、Map15 6種／44實例，兩圖所有61個多幀景物實例皆有ACT。其餘229個source_clips_only（含NPC）未因此宣稱時序／方向完成。FISH01起幀是1而非0，9個可見步各9 updates後，frame10為480 updates空白；總561 updates後回frame1，不可把空白幀換前一幀。

`building_fade.gd`是純completed-update→疊層資料：SetInOut重設counter=256，每次Step減16到0；Draw將alpha上限截255。進入時primary=255、children始終畫、alternate=min(counter,255)最後疊上；離開時primary=min(counter,255)、counter>0才畫children、alternate=min(256-counter,255)最後畫。這不是把兩圖各乘互補權重的簡單crossfade。`build(...,transitions)`接受owner提供inside／updates，Canvas與Sprite候選都用alpha/255；沒有自行啟動fade時鐘。0／1／8／16個completed updates均可測；若SetInOut發生在InsideBuildObjEdit，當次UpdateHandle已Step一次，之後Draw看到updates=1。16次更新完成，但不能忽略呼叫phase就宣稱從事件起固定再等320ms；逆轉重設亦由owner的新事件決定。入口偵測、去重／版本及自身ghost仍未實作。

DIRECT來源同SHA Paradise.exe.lst：CSGOMap::Update 0x004CF1B2透過vtable+0x60（0x005C3958=CSGOMap::UpdateMapObj）到CELMap2D::UpdateMapObj 0x0052A08B；它先InsideBuildObjEdit，再UpdateHandle。CELBldg2D::Update 0x00549AF5經draw-logic vtable+0x18（0x005C5B34）呼叫Step 0x0055050A；SetInOut 0x005504DE將counter設256。Draw 0x005506CE依inside及counter安排primary／children／alternate；CDDxDrawObj::Draw 0x005188BF的0x005188F5–0x00518919截alpha到255並除255。CELStaticObj2D ctor 0x0054A172／InitAction 0x005514C7使用字面action，Step 0x005510FE呼叫DrawObj.Update→CELxnObj::Update 0x005419A7→xnAction.Update 0x005576E0。50Hz呼叫鏈與HK2 timing primitive的舊稽核是AUDIT；本輪重新計算Client.bin／SGOClient.bin SHA仍精確等於9b45569a…dd13147／6e64368a…6cb3be，同版本原始證據未變，未宣稱實際啟動舊Client逐幀觀測。

Native回歸：固定tick逐步counter對照2000步、30/60/120與AFK跳過取樣、每placement起始phase、非loop／0-duration／超長elapsed／錯profile與fractional raw_time、共享Atlas的BLG/static型別隔離、fade0/1/8/15/16與children規則。GPU用兩圖／兩解析度／7組進出fade與ACT時刻，見TEST_PLAN。原版visible-only更新／離開視野後phase由未來owner明確決定；本sampler只消耗elapsed updates，不把展示採樣率當原版時間。ACT events僅保留metadata，不在render取樣觸發聲音／特效，更不當權威戰鬥事件。

## 建築焦點狀態／版本與室內排序（2026-11-16）

`building_visibility.gd`是單一owner的純呈現狀態，不是Server碰撞或進門權限。configure接world、單調presentation MapEpoch及first_update；advance只接受相同epoch、下一個連續fixed update、world pixel focus與按已編譯main順序排列的可見root subset。重複／舊tick、缺tick、舊epoch、重複／倒序／未知candidate一律失敗且不改狀態；缺失trace不能靠最新位置猜中間進出，owner須補播或以新epoch重建。clear保留epoch tombstone且設inactive，舊async configure不能重新接回。資料只存位置／前緣／小型counter，沒有Texture、網路／DB、_process或自行計時；snapshot是新建的獨立資料，可交給呈現端。

原版CheckPointInsideBuilding的四條邊相當於投影矩形：relative `u=x+2y, v=x-2y`，`Lx+2Ly<u<=Rx+2Ry`及`Lx-2Ly<=v<Rx-2Ry`。上下邊有開閉差異，不能用通用contains-polygon的邊界預設。CheckBldgEntrance還要求Manhattan距離<=1000，兩組legacy點資料count皆非零；0x005499E9已無條件跳return，後段逐點比對在這份reference是不可達碼。因而**不拿inside_cells/outside_cells座標當可走格／真正入口點**。Map15有15個可符合門檻的main root、Map13為0；篩選純資料條件，尚未聲稱HK2 executable的整個入口函式逐指令等版。

每次先對可見建築ShowInside（只改bool、不重設counter），最後一個inside root成為selected；selected變成新建築才對新者SetInOut(true)，selected變None才對舊者SetInOut(false)。直接A→B不額外替A重啟退出fade；重疊時其他inside也可立即顯示primary，只有最後者重設。接著只替可見candidate Step counter，故offscreen舊建築可在exit後保留256，恢復可見才繼續。這是reference策略，不用一般「每個bool變化都reset」取代，也不讓Render回呼重設時間。

`world_draw_list.configure(..., map_epoch)`與`build_focus(view, actors, snapshot, expected_update, frames)`連接狀態：epoch／active／expected update不符拒絕，不消耗舊map快照。expected_update須來自owner最新fixed狀態；這不是Renderer維護一份Render FPS clock，也不是正式Wire協定已完成。`visible_main_indices`按目前Renderer的保守root+children bounds產生候選，與原版固定800×600 SIF culling不宣稱逐bit等同。focus本身由caller傳入；legacy是camera top-left+(400,300)，新相機應明確轉換viewport center至world，不能偷偷用畫面座標／解像度改Server權威位置。

完整房間繪製還需要額外清單：selected室內建築自身Draw後，DrawMapList再次畫可見children；OnlineObjectShowHandle把位於該房間幾何內的Actor按同一原版predicate插入該額外清單，其餘Actor留main list。新版build_focus保留這個順序，讓室內角色在地板／屋頂之後與家具排序；不是把所有Actor都拉到最上層。額外清單可使child出現兩次，不能只因看似冗餘而刪除。外部插值Actor保留fractional位置作連續幾何判定，不在入口測試內截斷到int；focus fixed input仍是明確Vector2i。

DIRECT仍為同SHA Paradise.exe.lst：InsideBuildObjEdit 0x0052A3EF–0x0052A59B（0x0052A471–0x0052A490 camera center；ShowInside與last pointer／SetInOut分支），ShowInside 0x0052B2F0只寫23Ch；Bldg ctor 0x0054866D寫RTType=1（GetRTType函式本體於lst折疊，依具名call與欄位串接）。CheckPointInsideBuilding 0x00549879–0x0054994D、CheckBldgEntrance 0x00549950–0x00549A9D。InsideBuildObjEdit 0x0052A4D3取子清單，GetvisibleBldgObjList 0x00529F63先清空額外array；DrawMapList 0x0052A2FF–0x0052A314在selected root後DrawHandle。OnlineObjectShowHandle 0x0052A5D1–0x0052A6C8把室內Actor插入0A4h，外部插入5Ch。這些研究結論沒有複製組語進Git。

原版動態舊Client視覺、真正相機／Player controller、zoom／odd viewport、packet EntityRevision／ConnectionEpoch、Server碰撞／權限、NPC規則與多人E2E仍NOT VERIFIED。MapEpoch目前是呈現owner的generation契約，不把診斷中使用的AssetStore scope序號當成正式Server protocol定版。Render0 numeric trace不等於實際多人AFK驗收。P0壓縮品質／視野預取與完整World記憶體斜率仍待後續Gate。

本切片驗證：building_focus_release的兩候選共168 captures、105 source frames，含同畫面室內／室外Actor、Court子物件兩次draw，Python投影座標重播與GPU合成PASS；400 fixed updates在30/60/120/0呈現採樣下完整trace一致。負向錯selected／counter／少overlay／錯Actor順序全部拒絕，詳細命令與精度見TEST_PLAN。普通build保留手動快照相容；實際focus路徑應使用帶selected與epoch的build_focus，才能包含原版OnlineObjectShowHandle室內分流。

### Atlas physical extent契約（2026-11-16）

Renderer的FrameTable UV為原始pixel座標，必須對應Texture2D實體texel。原239個非4倍數頁匯入後被整頁nearest伸展，normalized UV雖可由舊oracle模擬，仍造成來源圖形變化。已在唯一Publisher補右下透明留白，不在Renderer加比例修正／第二份texture。Frame rect、origin、trim與景物bounds不變；更新manifest後須重建外部PCK。候選品質與byte成本見ASSET_ARCHITECTURE，實際GPU回歸見TEST_PLAN；codec仍有細節誤差，不宣稱原圖零差異或整體MVP完成。


## 2026-11-16 MVP002-CAMERA-ENTRY：單一雙圖檢視入口

正式 `client/app/main.tscn`／`main.gd` 只組裝 `WorldView` 與輕量地圖選擇工具列；`rendering/world_camera.gd` 是無時鐘的唯讀式 presentation camera，保存 focus 的小數移動累積，僅將 view origin 向下取整供1:1像素繪製；world↔screen、四邊clamp、小世界置中、非有限值／舊epoch拒絕是獨立測試。它**不是玩家的 Server authority position**，UI 30/60/120/FPS0 的相機測例不得轉換成 Network/WorldState 時鐘。

`rendering/world_view.gd` 為單一可見地圖 owner：`open_pack→begin_scope→maps/<id>→Scenery Atlas→TerrainData/WorldDrawList→WorldCanvas`；每次 request 先清上一個 Canvas/Terrain/DrawList、再 end_scope，舊的 Store worker 仍按既有 pump 取消/drain。選圖按鈕只切換本地 Viewer，**不是 MapEpoch/WorldReady／NPC 傳點，也沒有登入/角色/Combat**。使用原有唯一 Renderer/AssetStore，沒有新增第二種 Cache 或 PCK。

可重播 `python client/tests/run_world_view.py Godot --report reports/world_view_native.json`：真 Windows OpenGL3／RTX3050／正式私有 PCK，12 個 Map13/15 × 兩視窗大小×三 focus GPU capture，CPU 原始 SPR／Atlas crop／world draw order／像素采樣驗證 12/12 PASS、38 distinct SPR frame，沒有 over-tolerance pixel；原版的 CPU 素材與壓縮 GPU 仍按已聲明 sampler/rounding 界檢查，不謊稱逐 RGB 完全一致。快速 6 組反覆切圖、Input UI 攔截、相機 epoch、退出 drain 正確，final scopes/assets/jobs/RGBA=0。single-process PeakRSS226.19MB、5.75s wall，只是 Viewer E2E，不代表正式遊戲 frame tail 或多人 8 開。

後續 MVP002：把 Presenter 連接到真正的 WorldState／network command reducer／角色固定動畫與 Render FPS 解耦；逐視野資產預載和 AFK Render0 資源成本仍待公平選型。MVP003/REB020 的 Server/Wire／A*／MapEpoch 不可由此推論完成。
