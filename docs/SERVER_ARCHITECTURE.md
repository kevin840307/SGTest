# SGTest Server — 重建設計

**狀態：規格，尚無 .NET 專案。** 目標 .NET 8，單體功能模組，固定世界模擬與獨立 Transport/Persistence。

## 核心邊界

| 責任 | 內容 | 禁止 |
|---|---|---|
| Gateway/Connection | socket、身份驗證、ConnectionEpoch、有界佇列 | 直接計算傷害／操作 DB 於 Tick |
| Wire | 實測選型的 binary/framed codec、版本、長度限制、重播保護 | 綁死 JSON／MessagePack、隱式依賴 Godot 角色類別 |
| Map World | 單寫者 authoritative positions、collision、AI、combat、drop | 在 Tick 同步等磁碟／Send |
| AOI & Snapshot | per-player visibility、Spawn/Despawn、entity revisions、有限頻率更新 | 把看不到當死亡，或跨 map 沿用 epoch |
| Accounts | Account、Character、Session 安全與授權 | 將 ConnectionEpoch 當永久角色身份 |
| Persistence | SQLite WAL／migration／UID／交易原子性 | 用 DB 查詢驅動每隻怪 AI |

暫不建立多個空 project、抽象 Service 層、事件總線和微服務。當 Combat/Items/Social 複雜到需要獨立測試才拆成模組。

## 時鐘與事件

- World Tick 以 30Hz 作原型參考起點，Snapshot 10Hz 作起點；未重新實測前不是硬性完成值。
- Map command queue 必須有界，single writer 推進世界。過載時採非阻塞入列、每 Tick 工作預算與明確拒絕／重試回覆；**不得為等待慢 Client／DB 把 Tick 卡住，也不得把可靠遊戲指令當成可丟棄視覺快照**。
- 事件應有 ConnectionEpoch、MapEpoch、EntityRevision、MovementSequence、ActionSequence 等對應世代，不能拿一個 SessionId 混用。
- Combat 命中／MISS／死亡／復活、掉落物 UID 與原子拾取應是 Server 權威結果，不依賴 Client 動畫幀。
- 玩家進出 AOI、換圖、重連時應正確同步 Spawn/Despawn、初始位置／動畫狀態，避免幽靈角色或外圍順移。WorldReady 快照與後續增量須有明確序號邊界；缺序／慢客戶端必須安全重同步，不可讓事件佇列無限增長。

## Wire 選型與 AOI 流量 Gate

- REB020 先依 [Wire Contract](RUNTIME_PROTOCOL.md) 做封包頭／緊湊二進位、MessagePack 等候選的 C# ↔ GDScript golden fixture 和實測，**不要求使用 JSON**；選定後才把單一 Codec 寫成正式契約。
- 對相同 World Tick 與可見 AOI，量測每玩家 bytes/sec、訊息／秒、Serialize/Deserialize P95/P99、GC alloc、AOI 候選與扇出成本、Send Queue Age、輸入到畫面延遲；須與全圖無過濾基準公平比較，確認可節流網路且不破壞同步。
- AI 的各怪物決策時間與位置推進分離；獨立 NextDecisionTick／分散預算避免所有怪物同時轉向／移動，Server 仍維持可重播且單寫者權威，不能用同一批群體動畫假裝自然 AI。

## MVP Server：單 Map13 全核心功能權威驗證

先建 Map13 的 Single-writer World、Blocking/A*、身份／角色／登入／持久化、可測 Wire、每連線 AOI/WorldReady 與有界佇列，再接 Enemy/AI/Combat/Skills/Drop。**MVP 內需完成所有可在單張地圖驗證的主要規則**：物品 UID／強化、背包／銀行／倉庫／商店／修理、NPC 互動、技能/Buff/MISS/Death、寵物、機甲／卡片／升級、交易／玩家商店、PvP 顏色與 GM 權限。每項需要真實資料與 Server 端合法性檢查、異常／重送／斷線／多人競態、原版行為比對，不只是少數 Happy Path。

怪物 AI 使用可重現且分散的獨立決策排程，允許部分靜止；不因共同 Tick 造成怪物同時移動或瞬移。單圖壓測階段須有 20 連線玩家／300 怪／500 Drop（至少兩個真 Client 驗證畫面），測 AOI fanout、Queue、World Tick P95/P99、CPU/GC/RSS、安全背壓與可靠事件。只有跨地圖真正 E2E 因先一圖可記錄未驗證，MVP 後加入第二真圖再補 Gate；所有地圖／全物種資料與 100/1000/5000 的擴量也留 MVP 後。

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
- **AOI 先於封包編碼與廣播**：用 map-local spatial hash/grid 或實測合適的索引維護每位玩家的訂閱集合；只將相關 Player／Enemy／NPC／Drop 更新給有權接收的觀察者，**不能全圖每 Tick 廣播或每封包掃描所有實體**。登入／進出視野／換圖有可靠 Spawn/Despawn／WorldReady 基準，視野邊界使用適當 margin/hysteresis；同一實體未改變不重送相同狀態，位置／Facing 的過時快照可按版本合併。可靠傷害、物品與交易結果須送給正確對象且不可被視覺 AOI 吞掉。不同解析度或相機範圍保持周邊同步一致；**Client 回報的 viewport/zoom 必須由 Server 限幅與限頻**，防止過大視野或頻繁改尺寸放大廣播成本。
- **AFK/多開**：Server 永遠按權威 Tick 推進；可依明確的 AFK interest subscription 降低不重要的視覺 Snapshot，但**不得因 Client Render FPS／視窗焦點改變核心 Tick、Reliable Events 或角色防作弊驗證**。每個連線維持獨立權限／輸出隊列配額，慢客戶端不能拖垮整張地圖。
- 20／100 人、300／1000 怪、500／5000 掉落的測試必須記錄 AI budget、World tick P95/P99、packet queue age、AOI fanout、記憶體、慢連線和斷線情境。
