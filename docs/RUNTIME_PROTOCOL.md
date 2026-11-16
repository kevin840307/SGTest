# SGTest — Client / Server Wire Contract

**狀態：WIRE001 codec、Server Gateway boundary 與單連線 TCP+TLS 1.2/1.3 session 已完成第一階段；正式 Runtime 採單一 versioned compact binary。** `GatewayConnection` 提供 trusted binding／bounded parser／reliable egress；`TlsWireConnection` 強制 TLS 並要求 TLS 內的 Auth/CharacterChoose callback 成功後才進 gameplay。**真正 Account Auth/CharacterChoose、production listener/cert deployment、WorldReady/ACK/reconnect 與 Godot TLS Client 仍未完成。** MessagePack／Protobuf 只保留在 test probe。

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

`float32` positions give sub-0.002-pixel resolution within the current ~15K-pixel map range; C# Server World retains double-precision authoritative movement. The Godot Client validates versions, advertised lengths, finite/nonnegative coordinates, legal moving bit, generation/revision, same-map identity and WorldReady-before-Spawn/Delta. A trusted transfer's larger MapEpoch allows WorldReady to clear the old map atomically; older AOI frames are ignored and future-map entity frames fail closed. AOI frames are routed only through the existing Gateway reliable FIFO after trusted binding and do not accept any raw Client ActorId. **WorldReady ACK, real AOI sender pump and complete two-client E2E are not implemented in this codec slice.**

### 2026-11-16 AOI runtime delivery baseline

The existing `WorldAoiPump` now sends the four V1 server-only AOI frames through the same authenticated `GatewayConnection` reliable FIFO as MoveReceipt. World joins/leaves/transfers from the 30Hz owner are preserved as bounded lifecycle batches; the AOI worker scopes entity state to map/viewer and MapEpoch. The sender confirms an event only after Gateway enqueue succeeds. The Godot non-render WorldState reducer receives WorldReady and clears old-map entities before applying Spawn/Delta/Despawn; the one-client true TLS fixture now requires `world_ready=PASS` and `receipt=PASS`. This is **not** a WorldReady ACK protocol or a guaranteed recovery/resync mechanism after lost/overfilled queues. Those remain required before multiplayer release.


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

WorldReady still establishes own ActorId/MapId/MapEpoch/XY. The first subsequent SelfState binds its ConnectionEpoch (received from the trusted server, never accepted from client commands); subsequent owner frames must keep that generation. The reducer validates own actor, both map epochs, map ID, revision, finite XY, moving flag and nondecreasing MovementActionSequence, then publishes `x/y` and `self_state` independently of rendering. Old epoch/tick/revision frames are ignored; future epochs, wrong identity/map/connection and regressed action sequence fail closed. New-map WorldReady resets this owner baseline; duplicate same-epoch WorldReady is ignored because no same-epoch resync protocol exists yet. Remote Spawn/Delta still reject own ActorId. Both endpoints must be updated together; old Clients reject opcode 24.

This closes authoritative own-position delivery, **not** prediction/interpolation, facing/animation, camera-follow, ACK/resync or coordinate persistence. The TLS fixture now continues routing World results after the first receipt so the Godot test can observe arrival and stop rather than merely an accepted command.
