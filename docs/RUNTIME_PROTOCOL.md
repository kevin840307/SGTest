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

## 2026-11-15 初步實測證據（僅合成負載，尚未定版）

獨立可重跑：tools/tests/wire_aoi_probe/WireAoiProbe.csproj（.NET 8 Release）與 verify_binary.gd（Godot 4.7.2 headless）；皆不依賴正式 Client／Server，**不能當作正式 Wire/AOI E2E PASS**。

- **Recovered 實際現況（唯讀分析）**：Recovered/server/Protocol/ClientProtocol.cs 使用 JSON UTF-8 → AES-CBC/PKCS7 → Base64，WebSocket **Text** 傳送；Client scripts/network_manager.gd 的 socket.poll() 在 _physics_process，世界內有每 physics tick 128 封包／4ms drain budget。舊版 CBC 使用固定 IV，**不適合作為新版安全設計**；新協定需要明確的 TLS/加密認證策略。
- **單筆 MoveEntity 同語意欄位**：JSON UTF-8 **109 bytes**；舊式 JSON + AES-CBC + Base64 信封 **152 bytes**；測試用 little-endian 8-byte header + 24-byte payload **32 bytes**（比純 JSON 小 **70.6%**）。**這不是實際線路 throughput，未包含傳輸層／TLS／批次成本，也沒有比較 MessagePack 或 Protobuf**。
- **.NET 8 Release 微基準**：JSON 編碼／解碼每筆約 **0.556/1.494 μs**，binary **0.070/0.123 μs**；5 次 × 40,000 次中位數。這是單筆 CPU／配置比較，不是 P95/P99 網路延遲。
- **Godot 4.7.2 headless**：32-byte Binary Golden Fixture 雙向編解碼 **PASS**；GDScript 單筆 JSON 編碼／解碼約 **3.510/3.064 μs**、binary 約 **0.900/0.562 μs**（各 20,000 次；單次測量，非跨機保證）。
- **AOI correctness**：模擬 100 Player + 1,000 Enemy + 5,000 Drop，均勻散布 24,000×24,000；Spatial Grid cell 1024／enter 2600／exit 3000，比 brute-force 得到相同可見集合；候選接收者 **609,900 → 20,812**（-96.59%），變動實體接收者 **17,666 → 601**（-96.60%）；Hysteresis PASS。
- **AOI 最差密集情境**：全員／全物件集中同一 AOI，**609,900／609,900 都可見（節省 0%）**。因此 AOI 不能單獨保證高密度戰鬥低負載，仍需 changed-only Delta、可靠／非可靠流量區分、批次、Client/Server 佇列背壓及 AI 分散更新。
- **尚缺**：原版反編譯 wire 逐欄核實、MessagePack/Protobuf 公平比較、真正 transport（TCP/WS/UDP、TLS/AEAD）、真實 Map13/怪物分布、AOI 連線與可靠事件驗收、Godot 非 headless 實機 FPS／Latency、Server Tick／GC 與擴展負載。**REB019 尚未完成，不得因上述結果就定版**。

## 第一版測試先於新 Runtime

- 合法／非法封包、模糊測試、最大 payload、序號 wrap／overflow。
- GDScript ↔ C# 相同 fixture round-trip，含 header framing／端序／拆包／黏包／版本錯誤／不合法長度及二進位 byte-for-byte golden fixture。
- 0/30/60/90 FPS、失焦、Unfocus／AFK 下事件摘要 Hash 一致。
- 模擬 128+ 筆 burst、網路 jitter/out-of-order/packet loss 時不能造成 ghost spawn 或永久卡住 action。

舊版測試向量／行為證據見 [research/RUNTIME_PROTOCOL_EVIDENCE](research/RUNTIME_PROTOCOL_EVIDENCE.md)。
## 2026-11-16 REB020-FRAMING-CANDIDATE（僅候選合約驗證）

在既有 `tools/tests/wire_aoi_probe/` 加入 `FramingProbe.cs` 與 `verify_framing.gd`，針對**目前測試用的** little-endian `uint32 frame_bytes + uint16 version + uint16 opcode + 24-byte Move`（32-byte frame）驗證 TCP byte stream 任意拆包／黏包。這不是建立正式 `server/` 或 `client/network/` 協定。C# parser 的暫用上限為 65,536 bytes（本輪 fuzz 採64 bytes），只接受 version=1、opcode=1、Move width=32；拒絕負向／超限 length、未知版本／Opcode、僅8-byte header但遺失 Move payload，重連需 Reset 清除舊連線部分封包。C# 使用有界儲存與同步 ReadOnlySpan 消費 callback（不能留存 span 到非同步佇列）；正式 Queue、connection epoch、ack／重播與慢客戶端仍需要另外實作。

可重跑命令（Windows、.NET 8 Release、Godot 4.7.2；在 repo 根目錄執行）：

```powershell
dotnet run -c Release --project tools/tests/wire_aoi_probe/WireAoiProbe.csproj
$godot = "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"
& $godot --headless --script (Resolve-Path tools/tests/wire_aoi_probe/verify_binary.gd).Path
& $godot --headless --script (Resolve-Path tools/tests/wire_aoi_probe/verify_framing.gd).Path
python -m unittest discover -s tools/tests -p "test_*.py" -q
```

本輪**合成資料實跑 PASS**：.NET 和 Godot 兩端對相同 32-byte Golden Fixture、全部 33 個單點分段、100 組 seeded 多段／黏包（每組三 frame）、5 種不合法 length、3 種不合法 header/payload、半包 Reset、合法 frame 後緊接不合法 frame 都通過；.NET 程式確認 bounded buffer 不超過預設限制。現行 Python 81 tests PASS；無 socket／TLS、無 Packet Queue、未作實際網路延遲／P95/P99 或 MessagePack/Protobuf 公平比較，**WIRE001／REB020 仍 NOT VERIFIED**。早期微基準只比較 synthetic JSON／Binary 單筆 encode/decode，不能用來推斷 TCP 端到端效能、選定協定或宣稱低 GC 的正式 Server。
