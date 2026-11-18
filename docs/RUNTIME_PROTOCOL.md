# SGTest — Client / Server Wire Contract

**狀態：WIRE001 codec、正式 TLS listener、SQLite Account/Auth/CharacterChoose、Godot TLS Client、WorldReady baseline ACK 與 bounded same-connection AOI resync 已整合到唯一 versioned compact-binary Runtime。** `GatewayConnection` 提供 trusted binding／bounded parser／reliable egress；`TlsWireConnection` 強制 TLS，成功 Auth/CharacterChoose 後才進 gameplay，正式 WorldReady 必須 exact ACK 才開放 Move/NPC。**尚未完成的是實際部署憑證/公網政策、TCP 中斷後自動 reconnect、crash recovery 與後續 Combat/經濟可靠事件協定。** MessagePack／Protobuf 只保留在 test probe。

## 原則

- 明確 versioned schema、封包型別、長度上限、field ranges；拒絕超限、未知高風險指令、亂序和重放。
- Server authoritative。Client packet 只承載 command，不能自行確認 damage、座標／道具 UID。
- Client Transport 接收後先 Decode/Validate，再放入**有界、順序一致**的事件佇列。Renderer FPS／AFK 不會決定封包處理速度。
- 同一 wire schema 可產生 C#／GDScript fixture，但兩個 Runtime **不能互相引用對方 executable code**。

## 通訊格式選型（已依 WIRE001 實測收斂）

- **用途分離**：遊戲即時通訊可選固定長度 header + 緊湊二進位 payload、Length-prefix + Opcode、MessagePack、Protobuf 或其他可證明合適的方案。JSON 可作設定／除錯／工具資料，**不是線上遊戲封包的必要格式**。
- **先確認原版**：唯讀查看 Recovered Client／Server、Recovered/Old、原版反編譯中的封包 framing、欄位寬度、端序、時序及更新頻率；用 fixture 驗證觀察結果，區分已證實與推測。
- **候選比較**：以 Godot 4.7.2 GDScript 與 .NET 8 C# 實作相同的移動／多人 AOI／Combat／掉落／初始快照工作負載，量測 bytes/event、bytes/sec、serialize/parse P50/P95/P99、allocations、GC、CPU、封包延遲、實作複雜度及跨平台相容性，才正式選型。
- **封包頭候選**：可包含 Length、ProtocolVersion、Opcode、Flags（及必要時序號／Epoch）；header 寬度、變長編碼、最大長度、endianness、checksum、可靠性語意、batching、壓縮閾值由比較與安全需求決定，不先鎖死，也不能把業務資料與傳輸封裝耦合。
- **零拷貝／少配置**：優先預配置／buffer pooling、分段讀寫與有界批次；先 Profile 才加入複雜壓縮或混合傳輸。TCP 需處理拆包／黏包及隊首阻塞；若評估 UDP／多通道，須明確設計可靠性、排序、重傳、驗證、加密、防作弊及部署成本。
- **格式變更**：C#／GDScript 共用 fixture 與明確版本契約；Codec 可替換但**不得多套正式 Wire Protocol 同時長期維護**。記錄選型與否決方案的實測數據後才更新正式規格。

### 2026-11-16 WIRE001 codec 選型結果

同一個 deterministic 4,000-event Move／Combat／Drop／Spawn trace，以共同 8-byte framing 計入線路大小；三候選皆先通過 C# round-trip，再以相同 Move fixture 做 Godot 4.7.2 byte-for-byte cross-runtime 驗證。Recovered 私有來源也重新核對：現行舊實作確為 JSON → AES-CBC/PKCS7 → Base64 + WebSocket text，Client 世界內以每 physics frame 最多 128 packet／4ms budget drain；這只作 legacy 行為證據，不沿用其固定 IV／JSON envelope。

| 候選 | 平均 frame bytes/event | C# encode P50/P95/P99 µs | C# decode P50/P95/P99 µs | C# encode/decode alloc B/event | Godot Move encode/decode µs |
|---|---:|---:|---:|---:|---:|
| compact binary | 35.40 | 0.080 / 0.096 / 0.107 | 0.118 / 0.123 / 0.203 | 51.6 / 0.0 | 1.205 / 0.922 |
| MessagePack | 28.61 | 0.273 / 0.539 / 0.608 | 0.382 / 0.396 / 0.436 | 168.4 / 0.0 | 2.135 / 3.506 |
| Protobuf | 29.39 | 0.230 / 0.291 / 0.300 | 0.287 / 0.372 / 0.390 | 169.4 / 224.8 | 4.021 / 5.278 |

因此正式 Runtime 選 **compact binary**。在此 trace 它比 MessagePack 約多 23.7% bytes/event、比 Protobuf約多20.4%，但 C#／Godot encode/decode 都顯著較快、decode 可做到零配置、沒有第三方序列化 runtime 依賴，且欄位寬度與安全驗證可完全明確。對本專案大量移動/AOI更新、多開、低 CPU/GC 與 Godot 維護成本的整體權重較合適；未來主要網路節省應由 AOI、changed-only、coalescing/batching 帶來，而不是為數 byte 付出每封包較高 CPU。此結果是本機 Release／headless microbenchmark，不是 TCP/TLS 或真多人 latency Gate。

正式 V1 header 為 little-endian `uint32 frame_bytes + uint16 version + uint16 opcode`，最大 frame 65,536 bytes。目前只定義：
- `MoveTarget(1)`：frame 28 bytes；payload = `int64 MapEpoch + uint32 MovementSequence + int32 TargetX + int32 TargetY`。ActorId／ConnectionEpoch **不由 Client 傳入**，未來 Gateway 必須由已驗證 connection binding 補入 Server command。
- `MoveReceipt(2)`：frame 30 bytes；payload = `int64 MapEpoch + uint32 MovementSequence + uint16 Status + int64 ServerTick`。Status 使用明確 0..10 mapping，不依賴 C# enum 隱式 ordinal。

已完成的 Server-side transport-independent 邊界：Client Move frame 不含 ActorId/ConnectionEpoch；`GatewayConnection` 只能由可信 `ActorSnapshot` 建立，將 connection identity 補成 `MoveTargetCommand`；`MoveReceipt` 內部帶 ActorId/ConnectionEpoch/MapEpoch 供安全路由。固定容量 `WireStreamParser` 不在 backpressure 時吞掉完整 frame；可靠輸出佇列有 frame/byte 雙上限、FIFO、無 DropOldest。MapEpoch 只能在同一 ConnectionEpoch 上由 trusted transfer 單調更新；重連必須建立新 GatewayConnection。

### 2026-11-16 TLS001 Server socket slice

`TlsWireConnection` 已可接收**一個已 accept 的 `TcpClient`**，強制 TLS 1.2/1.3，沒有 plaintext fallback。TLS handshake 完成後，必須先執行上游 `authorizeAndBind(SslStream,...)`；只有 callback 驗證 Account/session/CharacterChoose 並回傳由可信 `ActorSnapshot` 建立的 `GatewayConnection` 後，才開始讀 gameplay frame。Production Host **尚未開 listener**，也沒有內建 test account。Handshake/Auth/read/backpressure 都有明確上限；可靠輸出以 awaitable queue 寫入 TLS，不 busy-poll；World ingress 暫滿時保留完整 frame 並有限時間等待，超過 budget 關閉 session。

GitHub Ubuntu 24.04 / .NET 8 的真 loopback `TcpListener + TcpClient + SslStream`：授權前 gameplay gate＋MoveReceipt roundtrip、Auth reject、plaintext reject、backpressure retry 共4項 PASS；Server/Host build 0 warning/error，整體 synthetic checks **40/40 PASS**。自簽 certificate 只存在測試記憶體，**不是 production certificate policy**。

尚未完成：真正 Account DB/password Auth、CharacterChoose wire flow、production listener/certificate deployment、應用層 ACK、WorldReady/AOI snapshot、重連/resync、Godot TLS Client、batching 與 20 真連線。這些保持 **NOT VERIFIED**，不可用 TLS loopback PASS 代替 REB020／MVP003 完成。

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
- **當時尚缺／後續狀態**：MessagePack/Protobuf 公平 codec 比較已於 2026-11-16 WIRE001-CODEC 完成並選定 compact binary；仍缺原版 Old wire 逐欄核實（僅作歷史相容研究，不阻擋新版 codec）、真正 transport（TCP/TLS）、真實 Map13/怪物 AOI 連線與可靠事件驗收、Godot 非 headless 實機 latency、Server Tick／GC 與擴展負載。不得用本段早期結果冒充正式網路 Gate。

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

本輪**合成資料實跑 PASS**：.NET 和 Godot 兩端對相同 32-byte Golden Fixture、全部 33 個單點分段、100 組 seeded 多段／黏包（每組三 frame）、5 種不合法 length、3 種不合法 header/payload、半包 Reset、合法 frame 後緊接不合法 frame 都通過；.NET 程式確認 bounded buffer 不超過預設限制。這是 framing 候選的歷史證據；其後 WIRE001-CODEC 已補做 MessagePack/Protobuf 公平比較並選 compact binary。此 test-only parser 仍不是正式 Socket parser；無 TLS/Auth、per-player Packet Queue、實際網路延遲／P95/P99 或 reconnect E2E，故 **REB020 仍 NOT VERIFIED**。

### 2026-11-16 Identity core boundary

The Server now has persistent Account/Character records and real Argon2id password verification, but identity is **not yet a gameplay wire protocol**. The existing TLS session still accepts only an injected `authorizeAndBind` callback. The next protocol slice must add bounded Auth + CharacterList/CharacterChoose messages inside TLS, authenticate through `SqliteIdentityStore`, verify selected-character ownership, then call trusted World registration and construct `GatewayConnection`. Until that wiring exists, the Production Host still exposes no public listener and REB020 remains incomplete.

### 2026-11-16 AUTHWIRE001 identity/login wire

All identity messages are legal **only inside the already-established TLS authorization phase**. They use the same V1 8-byte header (`uint32 frameBytes, uint16 version, uint16 opcode`, little-endian):

- opcode 10 `AuthRequest`: `uint16 usernameUtf8Bytes + uint16 passwordUtf8Bytes + username + password`; username 3..32 bytes, password 8..256 bytes, strict UTF-8.
- opcode 11 `AuthResult`: one byte status: `0 Success, 1 InvalidCredentials, 2 Disabled`.
- opcode 12 `CharacterList`: `uint8 count` then repeated `int64 characterId + uint8 slot + uint16 nameUtf8Bytes + name`; max 32 entries and 4096-byte frame.
- opcode 13 `CharacterChoose`: `int64 characterId`.
- opcode 14 `CharacterChooseResult`: `uint8 status + int64 characterId + int32 mapId + float64 x + float64 y + int64 mapEpoch`; status `0 Success, 1 NotOwned, 2 WorldRejected`. Failure frames carry zeroed identity/spawn fields.

`IdentityTlsAuthorization` verifies password first, emits only that Account's CharacterList, checks ownership again on CharacterChoose, then uses persistent `character_id` plus stored server spawn to call `TrustedRegisterAsync`. Only the returned ActorSnapshot creates the gameplay Gateway binding. Thus a Client may select from server-advertised IDs but cannot assert authoritative ActorId/ConnectionEpoch or spawn. Successful result exposes MapEpoch because MoveTarget requires it; ConnectionEpoch remains server-only.

The TLS handshake timeout remains 5s. The overall pre-gameplay authorization/character-selection timeout is configurable, default 120s and bounded to 10 minutes; the future production listener must add global/per-IP admission and rate limiting. After Gateway binding, identity opcodes are protocol violations. Godot support for these new opcodes is the next client slice and remains NOT VERIFIED.

### 2026-11-16 Godot V1 identity codec / transport

The Godot Client now implements the V1 identity frames it actually sends/receives and a stream framing helper. A dedicated threaded `ClientTransport` owns `StreamPeerTCP` + `StreamPeerTLS`; full frames are handed across bounded queues. Production TLS uses normal certificate/hostname validation. The local C# fixture E2E proves the Godot layout is interoperable with `WireProtocol.cs` through Auth, owned CharacterChoose and the first authenticated Move. The production Host still has no public listener, so this fixture is evidence of protocol/transport interoperability rather than a deployment claim.

### 2026-11-16 AOI compact binary V1 frames (Server→Client)

All frames retain the common little-endian `uint32 frameBytes + uint16 version(1) + uint16 opcode` header. Four *server-only* AOI opcodes extend the same formal codec (no alternate serialization library):

| Opcode | Event | Total bytes | Payload, little endian |
|---|---|---:|---|
| 20 | WorldReady | 44 | `int64 receiverMapEpoch, int64 serverTick, int64 ownActorId, int32 mapId, float32 x, float32 y` |
| 21 | EntitySpawn | 73 | `int64 receiverMapEpoch, int64 serverTick, int64 actorId, int64 entityConnectionEpoch, int64 entityMapEpoch, int32 mapId, int64 entityRevision, float32 x, float32 y, uint8 moving, uint32 movementActionSequence` |
| 22 | EntityDespawn | 40 | `int64 receiverMapEpoch, int64 serverTick, int64 actorId, int64 entityConnectionEpoch` |
| 23 | EntityDelta | 73 | Same payload as EntitySpawn |

`float32` positions give sub-0.002-pixel resolution within the current ~15K-pixel map range; C# Server World retains double-precision authoritative movement. The Godot Client validates versions, advertised lengths, finite/nonnegative coordinates, legal moving bit, generation/revision, same-map identity and WorldReady-before-Spawn/Delta. A trusted transfer's larger MapEpoch allows WorldReady to clear the old map atomically; older AOI frames are ignored and future-map entity frames fail closed. AOI frames are routed only through the existing Gateway reliable FIFO after trusted binding and do not accept any raw Client ActorId. 此段為最初 AOI codec 歷史切片；其後 **WorldReady ACK、正式 AOI sender pump、雙 Client E2E 與同連線 resync 已完成**，現行契約見下方 2026-11-17 baseline ACK/resync。

### 2026-11-16 AOI runtime delivery baseline

The existing `WorldAoiPump` now sends the four V1 server-only AOI frames through the same authenticated `GatewayConnection` reliable FIFO as MoveReceipt. World joins/leaves/transfers from the 30Hz owner are preserved as bounded lifecycle batches; the AOI worker scopes entity state to map/viewer and MapEpoch. The sender confirms an event only after Gateway enqueue succeeds. The Godot non-render WorldState reducer receives WorldReady and clears old-map entities before applying Spawn/Delta/Despawn; the one-client true TLS fixture now requires `world_ready=PASS` and `receipt=PASS`. 此段為初始 delivery baseline；現行 Runtime 已在其上加入 WorldReady exact ACK、bounded same-connection AOI resync，以及受限次數的 TLS 自動 reconnect。不可恢復的身份／Map／Connection 錯誤仍 fail-closed；Server process crash／長時間離線 recovery 仍未完成。


### 2026-11-16 Authenticated NPC portal command (V1)

The **same** little-endian header `uint32 length + uint16 version=1 + uint16 opcode` adds exactly two frames:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 3 NpcInteract | Godot→Server TLS only | 24 | `int64 mapEpoch; uint32 interactionSequence; int32 mapScopedNpcSpawnId` |
| 4 NpcInteractResult | Server→Godot reliable | 29 | `int64 requestMapEpoch; uint32 interactionSequence; uint8 status; int64 resultMapEpoch` |

Status `0=Accepted, 1=StaleGeneration, 2=ReplayedSequence, 3=UnknownNpc, 4=TooFar, 5=DestinationRejected`. An accepted result must have a greater resultMapEpoch than requestMapEpoch; a denial returns the requestMapEpoch unchanged. **Client cannot send destination MapId/XY, ActorId, ConnectionEpoch, or portal permission.** The Gateway binds the request to the authenticated character, and the 30Hz World owner checks current map/position, NPC source index, replay, proximity and destination Blocking against only audited Converter static TS_Common metadata. Unsupported Quest scripts do not become portals. A per-socket bounded 16-request worker serializes slow trusted operations; output uses the existing bounded reliable FIFO and explicitly fails on overflow. `WorldReady` and portal receipt may arrive in either order; Godot tracks one outstanding NPC action, ignores duplicate replies and resets the movement/NPC sequence only on a newer WorldReady.

This is a schema and gameplay slice, **not** an assertion that original NPC click range or private map/visuals have been validated.

### 2026-11-17 Own authoritative state (V1 opcode 24)

`SelfState` is a **server-only 73-byte** frame with exactly the EntitySpawn/EntityDelta payload layout above. It is routed only to the authenticated owner when its authoritative EntityRevision increases, through the existing bounded reliable AOI/Gateway FIFO. It does not enter remote AOI visibility, create an extra actor, resend on idle ticks, or change MoveReceipt. Gateway validates ActorId/ConnectionEpoch/MapEpoch against its trusted binding. A full queue retains the unsent event and fails explicitly through the existing supervised pump; no snapshot coalescing discards reliable events.

WorldReady still establishes own ActorId/MapId/MapEpoch/XY. The first subsequent SelfState binds its ConnectionEpoch (received from the trusted server, never accepted from client commands); subsequent owner frames must keep that generation. The reducer validates own actor, both map epochs, map ID, revision, finite XY, moving flag and nondecreasing MovementActionSequence, then publishes `x/y` and `self_state` independently of rendering. Old epoch/tick/revision frames are ignored; future epochs, wrong identity/map/connection and regressed action sequence fail closed. New-map WorldReady resets this owner baseline；一般 duplicate same-epoch WorldReady 仍忽略，但 Client 進入 `resync_pending` 後允許由 Server 送同 MapEpoch replacement WorldReady，藉此原子重建 SelfState／可見 Spawn baseline。 Remote Spawn/Delta still reject own ActorId. Both endpoints must be updated together; old Clients reject opcode 24.

This closes authoritative own-position delivery；其後已完成 presentation interpolation、Facing／camera-follow、WorldReady ACK/resync 與受控 session release 的座標持久化。Combat action ownership與 crash-safe periodic checkpoint 仍是後續 Gate；短暫 TLS/TCP 中斷自動 reconnect 已完成。 The TLS fixture now continues routing World results after the first receipt so the Godot test can observe arrival and stop rather than merely an accepted command.

### 2026-11-17 Authoritative Facing extension (current V1 layout)

Superseding the earlier byte counts above, append **uint8 Facing (1..8)** to WorldReady at offset 44 (**45 bytes total**) and EntitySpawn/EntityDelta/SelfState at offset 73 (**74 bytes total**). Other offsets and opcodes are unchanged. Both formal endpoints and golden fixtures are updated together; old 44/73-byte state frames are rejected by the exact-length gate, without silent direction fallback. This is the existing single compact-binary codec, not an alternate format.

The World updates facing only after a legal traversed route segment: E=6, SE=7, S=8, SW=1, W=2, NW=3, N=4, NE=5, using nearest 45-degree sector. Stop/same-cell target, invalid target and idle ticks preserve direction; trusted map transfer preserves it too. Spawn initializes Facing=1 as an explicit provisional server policy, not a verified original spawn/default-facing rule. The convention is traced in `research/RUNTIME_PROTOCOL_EVIDENCE.md` under Runtime direction and movement; logical artwork mapping still belongs to the Converter. Client stores authoritative facing directly and must not infer it from click targets, render FPS or camera direction. WorldReady supplies initial/transfer facing even before the next owner delta. Both codecs reject values outside 1..8. Animation/action ownership, attack/hit/death locks and facing persistence remain later gates.


### 2026-11-17 WorldReady baseline ACK / bounded same-connection AOI resync

The formal V1 codec adds two **Client→Server TLS-only control frames**; they are control-plane frames and do not increment gameplay command accounting:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 25 WorldReadyAck | Godot→Server | 24 | `int64 mapEpoch; int64 worldReadyServerTick` |
| 26 WorldResyncRequest | Godot→Server | 16 | `int64 currentMapEpoch` |

When `GatewayConnection` enqueues a formal `WorldReady`, Move/NPC gameplay for that binding remains closed until the Client returns the **exact MapEpoch + ServerTick** ACK. A map transfer resets the ACK generation. Wrong/future identity or generation remains a protocol violation; ACK/resync never allows a Client to choose ActorId, ConnectionEpoch, MapId or snapshot contents.

A recoverable same-session AOI continuity failure (for example Delta without its Spawn, or a forward EntityRevision/action gap) pauses Client gameplay and emits one bounded `WorldResyncRequest`. The Gateway **arms the resync gate before publishing the request** so the AOI worker cannot race a replacement WorldReady ahead of the gate; if the bounded request queue is full the parser retains the complete frame and the ACK state is restored for retry. Requests are consumed only by the existing single `WorldAoiPump` writer. `WorldAoi.TryResync` discards that observer's unsent stale AOI backlog and rebuilds, on the same MapEpoch, `WorldReady → SelfState → current visible Spawn set`. Frames already accepted into the Gateway reliable FIFO stay ordered ahead of the replacement WorldReady; while `resync_pending`, Godot ignores those FIFO-old AOI frames. Gameplay reopens only after the replacement WorldReady is ACKed.

Security/correctness boundary: wrong own ActorId/MapId/EntityMapEpoch/ConnectionEpoch still fails closed; resync is for continuity loss within an authenticated current binding, not a way to heal identity corruption. Server core CI 43988244612 passes 92/92 checks including the live bounded AOI pump rebuild and raw TLS listener ACK. Client core CI 43988918797 passes the reducer golden fixtures, 0/30/60/90 FPS independence, automatic `ClientSession` resync orchestration, real TLS, UI, two-client AOI/cross-map/NPC and formal Host + two real main scenes. Same-socket resync本身不等於 reconnect；其後正式 ClientSession 已另加入 bounded TLS/TCP auto-reconnect，見下一節。


### 2026-11-17 Bounded TLS/TCP automatic reconnect

`ClientSession` may auto-reconnect only after the session has previously reached a valid in-world baseline and ACKed WorldReady. A transient transport failure does not immediately become logout: the Client clears queued gameplay intents, stops the failed transport, creates a fresh `WorldState`, and retries the **same TLS/Auth/CharacterChoose path** for the already-selected character. There are at most three attempts with 100/250/500ms backoff. No old Move/NPC request is replayed across a connection generation. Auth/protocol/reducer failures remain terminal; only transport failure and a transient WorldRejected during reconnect are retryable.

The active session retains the login password only as a UTF-8 byte buffer required for re-authentication. The buffer is overwritten/cleared on explicit stop, immediate connect failure, or terminal session failure. This is deliberately simpler than introducing a second resume-token identity protocol. A future long-lived Internet deployment may replace this with a short-lived server-issued resume ticket without changing World/AOI generation rules.

After reconnect, CharacterChoose must produce a new World generation; WorldReady must again be ACKed before gameplay. Server-side graceful release/persistence keeps the old actor registered until its last authoritative Map/X/Y has been stored, preventing the reconnect from racing an old SQLite spawn. The public true-TLS fixture forces the first socket closed only after the Client observes a completed authoritative move at 24,8, verifies that 24,8 is durable, then accepts the reconnect. Client CI 43990367533 proves generation 1→2, restored spawn 24,8, movement sequence reset, a second move to 40,8, final durable 40,8, and zero active actors/sessions; the same run also passes UI, two-client AOI/cross-map/NPC and formal Host + two real main-scene regressions.

This is intended for **short transient TCP/TLS failures**. It is not a cross-process resume-ticket scheme and does not survive a long outage beyond the bounded retry window. Periodic authoritative **position** checkpointing was added separately afterward; it limits position rollback after a process failure but does not make combat/economy settlement crash-safe.


### 2026-11-17 Coalesced authoritative position checkpoint

The official Host now owns one process-local `CharacterPositionCheckpoint`. The 30Hz World still performs **no SQLite I/O**: `WorldResultRouter` exposes each successfully-retained authoritative Changed/Lifecycle batch to a memory-only observer, which coalesces the newest `ActorSnapshot` per character by `ConnectionEpoch → MapEpoch → EntityRevision`. By default every 5000ms (Host option `--position-checkpoint-ms`, bounded 500..60000ms), dirty characters are written in one SQLite transaction.

Periodic flush and graceful disconnect final persistence share one writer gate. `FinalizeAsync` marks the current generation finalized before waiting for SQLite and retains a generation/revision tombstone afterward. Therefore a periodic batch captured earlier cannot overwrite the final disconnect position; a strictly newer authenticated connection generation is the only event that re-opens tracking for that character. No DB schema migration is required.

The Host supervises the checkpoint task alongside listener/router/AOI and reports checkpoint statistics at shutdown. Server CI 43991017853 passes 94/94 checks, including coalescing latest revisions, stale-after-final rejection and bounded background persistence. Client CI 43991017743 remains fully green including true TLS reconnect, two-client AOI/cross-map/NPC and formal Host + two main scenes. With the default interval, **position** rollback after an abrupt process failure is bounded by the most recent committed checkpoint. Server core 43991613110 performs a real Linux SIGKILL after 40,8 is committed and after a later 24,8 movement remains volatile; a fresh process reopens the DB at 40,8. This is process-kill/reopen coverage for committed position state, not a kill-during-SQLite-transaction fault injection, and combat/items/currency still require their own reliable transactional persistence semantics.


### 2026-11-17 Monster state frames and shared resync

V1 adds **Server→Client reliable monster state** on the existing TLS/Gateway FIFO:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 27 MonsterSpawn | Server→Godot | 78 | receiver MapEpoch, ServerTick, EntityId, EnemyId, SourceIndex, MapId, X/Y, EntityRevision, CurrentHP/MaxHP, AI state, Facing, TargetActorId |
| 28 MonsterDelta | Server→Godot | 78 | same fixed layout as MonsterSpawn |
| 29 MonsterDespawn | Server→Godot | 40 | receiver MapEpoch, ServerTick, EntityId, last EntityRevision |

Monster EntityId/EnemyId/HP/state/facing/target are server-owned. The client cannot submit damage through these frames. `WorldState.monsters` has its own identity/revision reducer; duplicate/stale events are ignored, while missing Spawn or forward revision gaps enter the existing bounded `WorldResyncRequest` path. A replacement WorldReady clears both player and monster baselines. Monster state therefore reuses the exact ACK/resync generation contract rather than creating a second recovery protocol.

The same `WorldAoi` tracks player and monster spatial visibility. Monster-enabled hosts preserve every authoritative 30Hz World Tick as the single AI clock, but empty ticks do not themselves create reliable wire frames. On a tick that changes monster state, AOI stages the final coalesced snapshot before applying Player Join/Move visibility and then performs a revision-idempotent final refresh; a joining client receives one current MonsterSpawn rather than stale Spawn + same-tick Delta.

Cross-runtime fixtures validate 78/40-byte C#↔Godot goldens and malformed state rejection. Server CI 44021869726 passes the live shared-AOI/Gateway path; Client CI 44021869721 passes `AOI_WIRE_GODOT_PASS ... monster_gap=RESYNC`, visual presentation, real TLS/reconnect, two-client/cross-map/NPC regressions and the formal Host with two actual main scenes. These frames carry authoritative monster state only; player→monster attack requests, player HP settlement, MISS/skills/rewards/drop durability remain later Combat protocol work.


### 2026-11-17 Player normal attack → monster authority

V1 adds two combat frames on the existing TLS connection:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 5 NormalAttack | Godot→Server | 28 | `int64 MapEpoch; uint32 AttackSequence; int64 TargetMonsterEntityId` |
| 6 NormalAttackResult | Server→Godot | 57 | MapEpoch, AttackSequence, status, ServerTick, target EntityId, Damage, CurrentHP/MaxHP, target EntityRevision |

The request deliberately carries **no ActorId, ConnectionEpoch, player Attack, damage or attack range**. `GatewayConnection` injects the authenticated actor generation and an optional trusted combat profile loaded during CharacterChoose from SQLite. Missing combat profile does not invent defaults; combat resolves as `CombatUnavailable`. WorldReady must be exact-ACKed before attack ingress, and the bounded Router attack channel applies the same backpressure/fail-closed policy as other authoritative gameplay work.

The single `MonsterWorldCoordinator` resolves request MapEpoch, per-session AttackSequence replay, server-tick cooldown, authoritative player position, trusted AttackRange and original Blocking direct line before calling `MonsterRuntime.ApplyTrustedDamage`. The current first-slice damage settlement is `max(1, trustedProfile.Attack)`; this is an explicit compatibility baseline, **not a claim that the original player-vs-monster DEF/MISS/crit formula has been verified**. `Miss` exists in the result status contract so the eventual verified hit formula can preserve MISS as a reliable event, but this slice does not fabricate random misses.

A successful attack produces two different authoritative outputs with separate responsibilities: `NormalAttackResult` is a requester reliable result for damage/MISS/rejection/UI, while monster HP/state/revision is changed only by the existing shared `MonsterDelta` AOI stream. Godot stores the reliable result but never mutates `WorldState.monsters` from it; duplicate results are idempotently stale, future unsent sequences fail closed, and map transfer resets attack sequence state.

Server core 44038307685 passes 108/108 checks. Client core 44038307692 passes the codec/reducer gate and a real Godot→C# TLS E2E using an explicitly `SyntheticTest` combat profile: the client sends only the target intent, Server applies 60 damage to a 100-HP authoritative monster, reliable result reports 40 HP, and the shared MonsterDelta independently reaches the same HP/revision. Existing TLS reconnect, two-client AOI/cross-map/NPC and formal Host + two real main scenes remain green.


### 2026-11-17 Durable monster → player damage / death

V1 adds two Server→Godot player-combat frames:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 7 PlayerCombatState | Server→Godot | 24 | `int64 MapEpoch; int32 CurrentHP; int32 MaxHP` |
| 8 PlayerDamage | Server→Godot | 57 | MapEpoch, DamageSequence, ServerTick, attacker Monster EntityId/revision, Damage, CurrentHP/MaxHP, Died |

For authenticated characters with a stored combat profile, `PlayerCombatState` is sent after successful CharacterChoose and before WorldReady. A character with no verified/synthetic profile gets no invented HP baseline and is not admitted as a Monster combat target.

`PlayerCombatSettlement` is the only durability owner for Monster→Player HP. The 30Hz World/Monster/AOI path performs no SQLite I/O: it reserves projected HP in bounded memory and posts a trusted hit. Projected HP prevents extra same-tick hits after lethal damage. A single background writer commits SQLite, compares the transaction's actual Damage/HP with the reserved timeline, and only then queues `PlayerDamage` into the authenticated Gateway reliable FIFO. Drain completion occurs after reliable queue admission, so map transfer/disconnect cannot advance generation ahead of a committed old-generation Damage/Death result.

The combat state starts suspended when CharacterChoose registers the actor. It becomes targetable only after the Gateway route itself is attached, closing the race where AI could commit damage before any socket route existed. Portal transfer and socket release suspend/close and drain the combat timeline before changing MapEpoch or detaching. Reconnect therefore obtains the durable current HP baseline instead of an older pre-hit value.

Godot treats `PlayerDamage` as the sole own-HP damage authority: exact/current MapEpoch only, strictly contiguous DamageSequence, monotonic per-damage ServerTick, explicit Died↔HP=0 consistency, stale old-map/duplicate events ignored, gaps/future generation fail closed. Client death blocks Move/NPC/NormalAttack before any packet is sent. The current damage compatibility rule is `max(1, MonsterAttackDamage - Defense/5)`, inherited from the public compatibility implementation; it is **not a verified claim about the original legacy DEF/MISS/crit formula**.

Server core 44041147095 passes 114/114 checks; Client core 44041146895 passes reducer and true TLS two-way combat: synthetic player Attack60 reduces Monster 100→40, one synthetic Monster raw20 hit against Defense10 commits 18 damage and Player 100→82, with the durable PlayerDamage event observed only after DB commit. Existing reconnect, cross-map, NPC and formal Host regressions remain green.

### 2026-11-18 Shared player hit/death/respawn presentation

Durable own-HP authority remains opcode7/8 and respawn authority remains opcode30/31. Two additional Server-only AOI frames carry **presentation facts only**:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 42 PlayerDamageVisual | Server→Godot reliable AOI | 66 | `ReceiverMapEpoch; ServerTick; VictimActorId; VictimConnectionEpoch; DamageSequence; AttackerMonsterEntityId; AttackerMonsterRevision; Damage; Died; Facing` |
| 43 PlayerRespawnVisual | Server→Godot reliable AOI | 45 | `ReceiverMapEpoch; ServerTick; ActorId; ConnectionEpoch; RespawnSequence; Facing` |

Opcode42 deliberately contains **no CurrentHP/MaxHP**. A remote observer may render the same committed hit/death but cannot obtain a new authoritative remote HP baseline from this frame; the victim's HP is still changed only by its authenticated opcode8 after SQLite commit. The Server posts damage presentation only from `PlayerCombatSettlement`'s post-commit lifecycle. A bounded Router channel transfers it to the single WorldAoiPump owner, which fans out only to observers currently seeing the exact victim Actor/Connection/Map generation, including the victim. Opcode43 is posted only after durable respawn succeeds, the World death gate is cleared and the requester result is admitted.

Godot validates receiver MapEpoch, visible actor generation and monotonic per-generation sequence before retaining presentation events. Duplicate/stale events are ignored, future/generation conflicts use the existing resync/fail-closed boundary, and neither event mutates HP. `PlayerPresentation` uses optional complete NH/ND logical-view sets: NH is a one-shot hit action; ND remains latched until the matching shared respawn event. Missing/incomplete NH or ND assets do not create guessed clips.

Server core 44200133417 PASS includes 66/45-byte C# goldens, malformed-frame rejection and victim/visible-peer-only AOI fanout. Client core 44200393222 PASS includes the matching Godot golden/reducer, presentation NH/ND lifecycle, real lethal TLS + respawn, and two real TLS clients where actor and observer receive the same durable 18-damage presentation while only the actor receives HP authority.

### 2026-11-17 Server death gate and authoritative player respawn

Death is now a Server lifecycle state, not only a Client presentation flag. After a lethal durable PlayerDamage commit, the trusted combat lifecycle sets the matching World actor `CombatDead=true` before the reliable death frame is queued. The World immediately clears any active route. A raw/stale client that still sends Move receives `MoveReceiptStatus.CombatDead=11`; NPC interaction returns `NpcPortalStatus.CombatDead=6`; combat resolution also refuses attacks from an actor whose authoritative combat HP is zero. A character reconnecting with persisted HP=0 is restored into the same dead gate before WorldReady.

V1 additionally defines the first player-respawn command/result on the existing TLS/Gateway FIFO:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 30 PlayerRespawn | Godot→Server | 20 | `int64 MapEpoch; uint32 RespawnSequence` |
| 31 PlayerRespawnResult | Server→Godot | 29 | `MapEpoch; RespawnSequence; status; CurrentHP; MaxHP` |

The request has no ActorId, ConnectionEpoch, HP, coordinates or resurrection flag. Gateway injects the authenticated identity/generation. The current first-slice location policy is deliberately **in-place at the authoritative death position**; it is provisional and does not claim the original Reborn/SavePoint location rule has been verified.

`PlayerCombatSettlement` remains the HP serialization owner. Respawn first suspends targetability and drains already-reserved damage. SQLite then performs one trusted atomic transition only when the stored profile is dead: `current_hp=0 → max_hp`. Already-alive or absent combat profiles fail closed. After durable restore, the actor remains suspended until the World death gate is cleared and the reliable PlayerRespawnResult has been admitted to the current Gateway FIFO. Only then is Monster targetability resumed, so a post-respawn PlayerDamage cannot overtake the full-HP respawn baseline.

Godot accepts an outgoing respawn only while the current combat baseline is dead. Same-map duplicate/stale result sequences are idempotent; future/unsent sequences fail closed. Successful respawn restores own HP and clears `player_dead`, but **does not reset DamageSequence** inside the same MapEpoch/connection generation, preserving reliable damage causality across death→respawn. Map transfer/reconnect still establishes the generation boundary.

Server core run **44049150018** passes **121/121** plus process-kill checkpoint recovery. Client core **44049150061** passes `PLAYER_DAMAGE_GODOT_PASS ... respawn_reopens_intents=true damage_sequence_continuous=true` and the real Godot↔C# TLS combat E2E reports `death_hp=0 respawn_hp=100 dead=false ... move_after_respawn=true`; reconnect, two-client AOI, Map15↔13, NPC portal and formal Host + two main-scene regressions also remain green.

### 2026-11-17 Inventory destroy/discard V1 (opcodes 39/40)

The same V1 little-endian header is extended without a second protocol:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 39 InventoryDiscard | Godot→Server TLS only | 32 | `int64 MapEpoch; uint32 DiscardSequence; int64 ItemUid; int32 Quantity` |
| 40 InventoryDiscardResult | Server→Godot reliable | 41 | `int64 MapEpoch; uint32 DiscardSequence; uint8 Status; int64 ItemUid; int32 ItemDefinitionId; int32 DiscardedQuantity; int32 RemainingQuantity` |

The Client command cannot supply ActorId, ConnectionEpoch or ItemDefinitionId. Quantity is bounded to 1..1,000,000 and the Server verifies ownership against SQLite. Result status is `0 Applied, 1 AlreadyApplied, 2 Missing, 3 NotOwned, 4 InvalidQuantity, 5 StaleSequence, 6 StaleGeneration`. Applied/AlreadyApplied carry the committed item/result quantities; rejected results carry zero item-definition/discarded/remaining fields. Opcode40 is Server-only and enters the same bounded reliable FIFO as other authoritative outcomes.

Exactly-once gameplay effect is provided by a durable SQLite receipt, not by assuming TCP never retries. Godot tracks one outstanding discard sequence, applies only an `Applied` result to its UID-keyed inventory, does not subtract `AlreadyApplied` again, and resets the sequence on a newer map epoch while ignoring older-map replies. This action destroys inventory and **does not create a GroundDrop**. C#/Godot golden and reducer Client core 44113626146 PASS; real authenticated TLS discard/replay/relogin Server core 44113938394 PASS. Commit `2b5af066` additionally proves the actual Godot ClientSession emits opcode39 to the C# LoginFixture over TLS and applies opcode40 before local quantity mutation; Client core 44114783838 PASS, with Server core 44114783786 PASS. Original bind/no-drop/confirmation/ground-drop rules remain NOT VERIFIED.

### 2026-11-18 Verified single-target skill authority (opcodes 44/45/46)

V1 extends the same TLS compact-binary combat path with one deliberately narrow skill slice:

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 44 SkillUse | Godot→Server | 32 | `MapEpoch; SkillSequence; SkillId; TargetMonsterEntityId` |
| 45 SkillUseResult | Server→Godot reliable | 61 | `MapEpoch; SkillSequence; status; ServerTick; SkillId; TargetEntityId; Damage; CurrentHP; MaxHP; TargetRevision` |
| 46 PlayerSkillVisual | Server→Godot reliable AOI | 70 | `ReceiverMapEpoch; ServerTick; AttackerActorId; AttackerConnectionEpoch; SkillSequence; SkillId; TargetEntityId; status; Damage; TargetRevision; Facing` |

The Client command carries no actor identity, connection generation, damage, range or cooldown. Gateway injects authenticated identity and the trusted combat profile. `SkillRuleCatalog` is an explicit operator-admitted rule source and currently accepts at most 64 rules only when the manifest says `verified_single_target_skill_rules`, version 1, `rules_verified=true`, `gameplay_ready=true`. This first slice intentionally admits only `single_monster`, fixed positive damage, bounded range/cooldown and `mp_cost=0`. Nonzero MP, AoE, buffs and formula-derived damage are rejected until those authoritative state owners and original rules are verified.

`MonsterWorldCoordinator` is still the single combat writer. It consumes a monotonically increasing per-generation SkillSequence, enforces a per-skill Server-tick cooldown, resolves the current authoritative actor position, range and Blocking direct-clear, and calls the existing `MonsterRuntime.ApplyTrustedDamage`. Therefore HP/Death/respawn and lethal Drop flow are unchanged. `SkillUseResult` is a reliable requester result and does not mutate Godot monster HP; only the shared MonsterDelta does.

`PlayerSkillVisual` is presentation-only. WorldAoi emits it only for Applied/Miss outcomes and only to viewers currently seeing both the exact attacker generation and target monster. It carries no player HP authority. Godot validates MapEpoch, target revision and attacker generation, keeps a bounded transient event list plus per-generation SkillSequence memory, and clears transient visual events on a replacement WorldReady while retaining sequence memory for same-epoch replay protection. Exact original skill ACT/effect rendering is intentionally not guessed by this slice.

Server core **44216490279** passes **151/151**, including verified-manifest rejection of MP/AoE, 32/61-byte wire, trusted Gateway identity injection and rule-owned damage/range/cooldown/replay/death. Client core **44216804891** passes 32/61/70-byte Godot codec/reducer, true TLS 99001 (`fixed_damage=40`) killing the remaining 40 HP only through MonsterDelta, and two real Godot clients where actor and observer both receive the authoritative Skill visual while neither uses it as monster HP authority. Existing reconnect, Map15↔13, NPC portal and formal Host + two main-scene gates remain green.

Remaining skill work: source-verified original skill definitions/formulas, MP/SP durability, AoE target selection/budgeting, Buff/Debuff lifecycle, MISS/Hit/Dodge rules, cooldown/resource UI, and source-verified visual/effect mapping.

### 2026-11-18 Durable skill MP settlement and mid-session combat baseline

`SkillUse` remains intent-only. A nonzero MP rule is now settled off the AOI/World hot path with a durable receipt. SQLite schema v7 adds `skill_mp_actions`, keyed by authenticated character + durable connection generation + MapEpoch + SkillSequence. Successful MP debit and its replay receipt commit atomically; exact replay returns the original before/after MP without spending twice, while reuse of the same sequence with another SkillId or MP cost fails closed. Dead, missing-profile and insufficient-MP cases do not mutate the resource.

`MonsterWorldCoordinator` now exposes a Prepare/Commit boundary. Prepare consumes the skill sequence and verifies rule, cooldown, current authoritative actor/target, range and Blocking but does not change monster HP or start cooldown. Production TLS skill ingress is drained by a bounded async worker. It asks the sole AOI/combat owner to Prepare, suspends/drains incoming player damage, performs the SQLite MP debit, queues an authoritative opcode7 PlayerCombatState resource baseline to the same Gateway FIFO, asks the AOI owner to Commit or Cancel, waits until the SkillUseResult has actually been admitted, and only then resumes Monster attacks against the player. SQLite never runs in the AOI/30Hz owner.

Opcode7 remains 32 bytes (`MapEpoch, CurrentHP, MaxHP, CurrentMP, MaxMP`). At initial login it establishes the complete combat baseline. During the same MapEpoch after combat has already been established it is now a resource refresh: Godot requires CurrentHP and MaxHP to match the existing authoritative HP timeline, updates MP/MaxMP only, and preserves DamageSequence, last PlayerDamage and death state. A mid-session baseline that attempts to change HP fails closed.

`SkillUseStatus.InsufficientMp=11` is reserved for a prepared skill cancelled by durable MP settlement. Zero-MP rules use the same Prepare/Commit bridge without touching SQLite. A prepared target can still become invalid before Commit; in that race an already-durable MP debit is not rolled back in this first slice. That behavior is explicit/provisional and must not be claimed as the original game's refund rule.

Current-client source evidence for `client/Skills/99001.tres` is: name 星界震盪, cast_range 0, aoe_radius 900, cost_mp 1, base_damage 1000, damage_multiplier 100, cooldown 0, tick_count 1. This evidence proves the stored fields, not the final authoritative damage formula. The real TLS fixture therefore deliberately keeps its existing SyntheticTest single-target fixed40/range48/cooldown1 rule and only exercises `MpCost=1`; it does not claim to be the original AoE implementation.

Server core **44270223291** passes **155/155**. Client core **44270223539** passes the mid-session MP refresh reducer, real Godot↔C# combat with persisted MP `2→1`, two-client observer where only the actor owns MP state, reconnect/cross-map/NPC gates, and formal Host + two actual main scenes.
