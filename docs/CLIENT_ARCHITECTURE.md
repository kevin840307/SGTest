# SGTest Client — 重建設計

**狀態：AssetStore 資源基礎已實作並有 headless 回歸；Renderer／Network／WorldState／玩法仍待實作。** 目標 Godot 4.7.2 / GDScript；功能導向的單一 Godot 專案。

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
