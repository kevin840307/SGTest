# SGTest — Client / Server Wire Contract

**狀態：提案規格，尚未建置新的 wire codec。** 目前**沒有指定 JSON、MessagePack、Protobuf 或自訂二進位格式**；先比對原版／Recovered 和可重現測試，再決定封包頭、傳輸、Payload、enum 與版本化契約，不直接沿用舊程式碼的隱式格式。

## 原則

- 明確 versioned schema、封包型別、長度上限、field ranges；拒絕超限、未知高風險指令、亂序和重放。
- Server authoritative。Client packet 只承載 command，不能自行確認 damage、座標／道具 UID。
- Client Transport 接收後先 Decode/Validate，再放入**有界、順序一致**的事件佇列。Renderer FPS／AFK 不會決定封包處理速度。
- 同一 wire schema 可產生 C#／GDScript fixture，但兩個 Runtime **不能互相引用對方 executable code**。

## 通訊格式選型（先量測，不能預設 JSON／MessagePack）

- **用途分離**：遊戲即時通訊可選固定長度 header + 緊湊二進位 payload、Length-prefix + Opcode、MessagePack、Protobuf 或其他可證明合適的方案。JSON 可作設定／除錯／工具資料，**不是線上遊戲封包的必要格式**。
- **先確認原版**：唯讀查看 Recovered Client／Server、Recovered/Old、原版反編譯中的封包 framing、欄位寬度、端序、時序及更新頻率；用 fixture 驗證觀察結果，區分已證實與推測。
- **候選比較**：以 Godot 4.7.2 GDScript 與 .NET 8 C# 實作相同的移動／多人 AOI／Combat／掉落／初始快照工作負載，量測 bytes/event、bytes/sec、serialize/parse P50/P95/P99、allocations、GC、CPU、封包延遲、實作複雜度及跨平台相容性，才正式選型。
- **封包頭候選**：可包含 Length、ProtocolVersion、Opcode、Flags（及必要時序號／Epoch）；header 寬度、變長編碼、最大長度、endianness、checksum、可靠性語意、batching、壓縮閾值由比較與安全需求決定，不先鎖死，也不能把業務資料與傳輸封裝耦合。
- **零拷貝／少配置**：優先預配置／buffer pooling、分段讀寫與有界批次；先 Profile 才加入複雜壓縮或混合傳輸。TCP 需處理拆包／黏包及隊首阻塞；若評估 UDP／多通道，須明確設計可靠性、排序、重傳、驗證、加密、防作弊及部署成本。
- **格式變更**：C#／GDScript 共用 fixture 與明確版本契約；Codec 可替換但**不得多套正式 Wire Protocol 同時長期維護**。記錄選型與否決方案的實測數據後才更新正式規格。

## 世代與序號

| 欄位 | 語意 | 適用 |
|---|---|---|
| ConnectionEpoch | 一次有效認證／連線世代 | 忽略舊連線封包 |
| MapEpoch | 特定 Map Instance／切圖階段 | 禁止出舊圖殘影 |
| EntityRevision | 實體變更版本 | 忽略過期 Spawn／Despawn／狀態 |
| MovementSequence | 移動命令／確認排序 | 輸入預測與重播 |
| ActionSequence | Attack/Hit/Death/Skill 的因果排序 | 不讓 Move 覆蓋受擊動作 |
| ServerTick | Server authoritative simulation 時間 | 補間、快照、回溯診斷 |
| ClientTimestamp | RTT／延遲量測 | 不能替代 Server Authority |

不可用單一 SessionId 同時作身份／地圖／實體／動作版本。

## 可靠性、排隊與重同步契約（REB010／REB020 實作前定版）

- **先確定傳輸方式與 framing**：TCP/其他傳輸、加密與認證、訊息邊界、大小限制、心跳、超時及背壓必須明訂；目前不能從 MessagePack 自行推論「已可靠」或「已處理恰好一次」。
- **權威結果**：Account/Character、Damage/MISS/Death、交易、金錢／Item UID、Drop Pickup、MapChange 必須有應用層序號／操作 ID／確認或重同步路徑；重試採冪等處理，不能重複扣款、拾取或套用舊角色事件。
- **可合併狀態**：遠端位置、Facing、非重要視覺 Snapshot 可依 ServerTick/EntityRevision 只留最新值；Spawn/Despawn 與 WorldReady 邊界不能被快照合併吞掉。
- **佇列滿載**：各 queue 定義容量、age、drop/reject/close 策略及觀測指標；不可阻塞 Server World Tick，也不可無限制累積可靠事件。無法保證有序連續時，停止套用不完整增量，要求全量快照或安全重連。
- **初始化／切圖順序**：以 MapEpoch 與 snapshot baseline 形成原子 WorldReady 邊界，僅套用之後的有序增量；舊圖與舊 ConnectionEpoch 的封包一律忽略。

## AOI 優先的傳輸策略（硬性設計約束）

- **先決定 Interest 再編碼**：Server 根據玩家所在 Map Instance、**權威位置**、可視範圍加適當緩衝及實際可互動範圍，建立每位玩家的 AOI 集合；用 map-local spatial grid/hash 等索引取得候選，不逐封包掃全地圖，也不無條件廣播全部玩家／怪物／掉落物。
- **進出事件**：進入 AOI 可靠 Spawn（含 entity ID、版本、位置、方向與當前動作）；離開 AOI 可靠 Despawn／interest removal；切圖、登入、重連用 WorldReady／snapshot baseline 保證一致。進出邊界可用 hysteresis、margin 防止視野邊緣反覆 Spawn/Despawn。
- **差異更新**：只有訂閱中的實體且**狀態確實改變**時才送適當 Delta；遠端位置／Facing 可依 EntityRevision 合併過時快照；不動的怪物不必每 Tick 傳相同座標。Server 內部 AI Tick 與對外同步率是兩件事，須量測再調整。
- **可靠事件**：傷害／MISS、死亡、掉落產生／拾取、交易、金錢、Item UID 等權威結果按**有權接收的對象**投遞；AOI 不能誤刪該玩家自己的可靠結果，也不能以位置 snapshot 取代有因果序的結果。
- **權限與負載**：Client 的 viewport/zoom/interest 提示不可信任，Server 必須限幅、限速與驗證；使用有界 per-connection send queue、扇出預算、更新合併與慢接收者隔離，不得阻塞 Map Tick。
- **AFK 與多開**：可由 Server 確認的低視覺 interest 模式降低非必要 Snapshot，不能因 Render FPS 自動改變可靠事件、角色狀態或 Server 權威 Tick；恢復時重訂閱、取得完整一致快照。
- **正確性優先**：避免視野外實體在進入視野時瞬移，應在 Spawn 帶足夠初始狀態，結合渲染插值；觀察距離與 buffer 需符合各解析度／相機而非固定 800×600。

## 建議封包群

- Handshake／Auth／CharacterChoose／Logout
- WorldReady／FullSnapshot／DeltaSnapshot／MapChange
- SpawnEntity／DespawnEntity／Move／Stop／Facing
- StartAction／Hit／Damage（MISS 也有事件）／Death／Respawn
- DropCreate／DropRemove／PickupAck
- Item/Equipment/NPC/Trade reliable commands and outcomes
- Heartbeat／ClockSync／Diagnostic error

AOI 的進出與 WorldReady 初始狀態應定義**確定的覆蓋／重放邊界**。測試包含不同解析度、MapEpoch 不一致、多人同時上線和舊 Snapshot 晚到。

## 第一版測試先於新 Runtime

- 合法／非法封包、模糊測試、最大 payload、序號 wrap／overflow。
- GDScript ↔ C# 相同 fixture round-trip，含 header framing／端序／拆包／黏包／版本錯誤／不合法長度及二進位 byte-for-byte golden fixture。
- 0/30/60/90 FPS、失焦、Unfocus／AFK 下事件摘要 Hash 一致。
- 模擬 128+ 筆 burst、網路 jitter/out-of-order/packet loss 時不能造成 ghost spawn 或永久卡住 action。

舊版測試向量／行為證據見 [research/RUNTIME_PROTOCOL_EVIDENCE](research/RUNTIME_PROTOCOL_EVIDENCE.md)。