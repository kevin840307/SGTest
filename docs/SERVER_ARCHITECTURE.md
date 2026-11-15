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

## MVP Server：Map15 + Map13 兩張權威地圖與互通

Server 初期只加載兩張原版 Map/Blocking/population/NPC，**共用相同 Map World/Session/AOI/Blocking/A***；Map15 主城支援角色登入、多角色、NPC 商店銀行倉庫修理、交易/玩家商店/聊天、物品強化與持久化；Map13 台地用於怪物獨立 AI、追擊、Combat/Skill/MISS/Death、掉落拾取與高密度多人 AOI。原版各圖功能／NPC／PvP 權限需從 Recovered/Old 或原始 Dat 核實。

MVP 的**Map15↔Map13 真實轉圖**必須由 Server authority 決定合法入口與目標位置；原子轉移 Session 的 MapEpoch／WorldReady／known-sets、清除舊圖 AOI observer、建立新圖 snapshot，處理多人同時傳送、延遲封包、重連、Drop / NPC / Combat 的舊圖事件隔離。不得只把 Client 隱藏地圖做成假傳點。DB/Socket/資產讀取不能阻塞 World Tick。

MVP 中物品 UID／倉庫銀行／技能／Pet/Mecha/卡片／升級／交易／商店／PvP／GM 等單圖可用功能依原版全部需要完整 Server Authority 與重送／競態／持久化測例；好友暫緩。Map13 同圖 20 玩家/300 Enemy/500 Drop，Map15 NPC／玩家商店多人操作與兩圖同時活躍，須測 Tick P95/P99、AOI fanout、Queue、CPU/GC/RSS 及可靠事件。MVP 後才全圖／全內容與 100/1000/5000 Gate。

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
