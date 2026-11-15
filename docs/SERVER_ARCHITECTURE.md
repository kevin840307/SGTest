# SGTest Server — 重建設計

**狀態：規格，尚無 .NET 專案。** 目標 .NET 8，單體功能模組，固定世界模擬與獨立 Transport/Persistence。

## 核心邊界

| 責任 | 內容 | 禁止 |
|---|---|---|
| Gateway/Connection | socket、身份驗證、ConnectionEpoch、有界佇列 | 直接計算傷害／操作 DB 於 Tick |
| Wire | MessagePack schema、版本、長度限制、重播保護 | 隱式依賴 Godot 角色類別 |
| Map World | 單寫者 authoritative positions、collision、AI、combat、drop | 在 Tick 同步等磁碟／Send |
| AOI & Snapshot | per-player visibility、Spawn/Despawn、entity revisions、有限頻率更新 | 把看不到當死亡，或跨 map 沿用 epoch |
| Accounts | Account、Character、Session 安全與授權 | 將 ConnectionEpoch 當永久角色身份 |
| Persistence | SQLite WAL／migration／UID／交易原子性 | 用 DB 查詢驅動每隻怪 AI |

暫不建立多個空 project、抽象 Service 層、事件總線和微服務。當 Combat/Items/Social 複雜到需要獨立測試才拆成模組。

## 時鐘與事件

- World Tick 以 30Hz 作原型參考起點，Snapshot 10Hz 作起點；未重新實測前不是硬性完成值。
- Map command queue 必須有界，single writer 推進世界。
- 事件應有 ConnectionEpoch、MapEpoch、EntityRevision、MovementSequence、ActionSequence 等對應世代，不能拿一個 SessionId 混用。
- Combat 命中／MISS／死亡／復活、掉落物 UID 與原子拾取應是 Server 權威結果，不依賴 Client 動畫幀。
- 玩家進出 AOI、換圖、重連時應正確同步 Spawn/Despawn、初始位置／動畫狀態，避免幽靈角色或外圍順移。

## 內容格式

Server 使用**原版 Blocking.dat byte grid** 或經可驗證的無損對應資料；只載入 Map world/population/collision 和必要規則，不解碼 PNG/Atlas。Enemy 外觀由 Server population logical ID 傳給 Client（其目錄為 Enemy/<id>）。

資料由 tools/asset_converter/content 輸出。新 Server 沒有可執行專案仍可先產生 content 與驗證 mapping。

## 必須重做的回歸

- 身份／權限／連線世代與 protocol fuzz。
- 20/100 人同地圖，300/1000 隻怪與大量掉落，AOI/AI/Combat 線性負載與分佈。
- Map Tick P95/P99、socket queue age、DB transaction wall-time 分離測試。
- 遠端角色走動／攻擊／受擊／死亡是獨立有序事件，Client FPS 和 Unfocus 不影響權威狀態。
- 登出、切圖、重連、交易重試的可靠一致性。

歷史原型 Server 程式與 376 PASS 不在重建分支內；任何新版完成狀態皆以 todo.txt 和新測試為準。
## A*／碰撞／大量怪物的固定 Tick 設計

- 以原版 Blocking.dat 做可行走格網，必要時建立 map-local immutable collision index；A*／導航的 Server 驗證使用此資料，不以 Godot 的即時渲染碰撞替代權威。
- 怪物追擊與巡邏使用**可重用的 Map-level path/AI 工作預算**，依距離／狀態分攤昂貴路徑計算。不要每隻怪每 Tick 建一個 A* 物件或全圖更新；距離過遠應脫戰回歸。
- Tick 的命令→碰撞／移動→戰鬥→AOI→發佈階段有界、可量測；同一 Map 的可變權威 State 由單寫者更新，非阻塞背景 path 計算需帶 MapEpoch／revision 驗證才可提交結果。
- AOI 用 spatial hash/grid 或適當索引，只將有關實體／掉落物發給觀察者；Spawn/Despawn 和可靠傷害事件不可被視覺距離外的低頻 Snapshot 吞掉。不同解析度或相機範圍要保持周邊產生／刪除一致。
- **AFK/多開**：Server 永遠按權威 Tick 推進；可依明確的 AFK interest subscription 降低不重要的視覺 Snapshot，但**不得因 Client Render FPS／視窗焦點改變核心 Tick、Reliable Events 或角色防作弊驗證**。每個連線維持獨立權限／輸出隊列配額，慢客戶端不能拖垮整張地圖。
- 20／100 人、300／1000 怪、500／5000 掉落的測試必須記錄 AI budget、World tick P95/P99、packet queue age、AOI fanout、記憶體、慢連線和斷線情境。
