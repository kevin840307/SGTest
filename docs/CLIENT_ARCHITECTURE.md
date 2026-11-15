# SGTest Client — 重建設計

**狀態：規格，尚無實作。** 目標 Godot 4.7.2 / GDScript；功能導向的單一 Godot 專案。

## 職責分離（不要為了分層先建立空檔案）

| 責任 | 輸入 | 輸出 | 不可以做 |
|---|---|---|---|
| Transport | socket bytes / outgoing commands | 有界接收佇列／傳送結果 | 操作 Texture、UI、World Node |
| Decode | MessagePack bytes | 驗證過的 versioned DTO | 修改場景 |
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

## Asset lifecycle

從獨立外部 PCK 掛載 res://assets/**，以 AssetCatalog 解析，單一 AssetStore 負責有界 worker、GPU commit、owner 取得／釋放／epoch 取消與快取淘汰。新的檔案先驗證 source path、Origin、page 和 frame 索引。切圖與登出後無持續累積的 Texture、SpriteFrames、Node。

**滿載／缺序處置**：Movement/Facing 的舊快照可按 EntityRevision 合併；Spawn/Despawn、換圖、角色／物品／戰鬥的權威結果不得因渲染慢而悄悄丟掉。若發現可靠事件缺序、MapEpoch 不符或佇列溢出，停止套用有歧義的增量，要求新 WorldReady／完整快照；不得無限擴張 inbox 或用落後畫面假裝已同步。詳 [Wire Contract](RUNTIME_PROTOCOL.md)。

## UI 與解析度

新 Godot Theme/Control 先自製後期《星夢 Online》金色 UI；現代 Theme 等未來需求確定再評估，**不必提前實作雙套 UI**。支援多解析度，camera viewport 改變需更新可視 AOI，不可固定舊 800×600。移動速率、機甲速度與動畫播放速度解耦。

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
