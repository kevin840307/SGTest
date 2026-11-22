# SGTest — Client / Server Wire Contract

**2026-11-22 現行傳輸覆寫歷史狀態：** 正式 Serve 已由 TLS 改為 **TCP + AES-256-CBC（固定 32B Key／16B IV）+ Compact Binary V1**。外層每筆 `uint32_le ciphertext_bytes` + `AES-CBC/PKCS7(binary-frame)`，上限 65552。WireProtocol（Header/Opcode/DTO）、Server Authoritative、可靠佇列／身分綁定不變。Transport 位置：`server/src/SGTest.Server/Transport/`、`client/network/aes_cbc_frame.gd`。**CBC 無 MAC、固定 IV、共用 Key 非 TLS 同等安全**；這是使用者指定的折衷，不代表加密認證安全；其後既有 TLS 內容僅保留為版本歷史與測試證據。請見 `server/README.md`。


**2026-11-22 現行程式碼：** 唯一 V1 `WireProtocol`，`AesGatewayListener` + `AesWireConnection` + `AesCbcFrameStream` 正式 TCP AES-256-CBC 傳輸；TLS Server Socket、Listener 與憑證設定已移除。帳號／角色授權由 `IdentitySessionAuthorization` 接管，需在成功 Auth/CharacterChoose／WorldReady ACK 後依權威規則執行玩法。Godot TCP Transport 已透過真實 Client/Server 登入、UI、重連、交易與 AOI E2E 驗證。完整 Release 與正式 CI 尚需獨立驗收；MessagePack／Protobuf 非正式協定。

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
- opcode 12 `CharacterList`: `uint8 count` then repeated `int64 characterId + uint8 slot + uint16 nameUtf8Bytes + name`; max 32 entries. Optional bounded appearance extension below raises the maximum frame size to 19,980 bytes.
- opcode 13 `CharacterChoose`: `int64 characterId`.
- opcode 14 `CharacterChooseResult`: `uint8 status + int64 characterId + int32 mapId + float64 x + float64 y + int64 mapEpoch`; status `0 Success, 1 NotOwned, 2 WorldRejected`. Failure frames carry zeroed identity/spawn fields.

`IdentityTlsAuthorization` verifies password first, emits only that Account's CharacterList, checks ownership again on CharacterChoose, then uses persistent `character_id` plus stored server spawn to call `TrustedRegisterAsync`. Only the returned ActorSnapshot creates the gameplay Gateway binding. Thus a Client may select from server-advertised IDs but cannot assert authoritative ActorId/ConnectionEpoch or spawn. Successful result exposes MapEpoch because MoveTarget requires it; ConnectionEpoch remains server-only.

### Character appearance extension (2026-11-22)

The same Wire V1 identity messages now support `Appearance = uint8 race + uint16 hairIndex + uint8 hairColor` (4 bytes, little-endian). Bounds are race 0..4, hairIndex 0..65535, hairColor 0..7; these are storage/codec bounds, **not** legal creation choices. HairIndex is a presentation index, not an original Hair item-definition ID. Only a trusted Server `CharacterAppearanceRules` allowlist may permit the complete tuple. No source reference, ActorId, combat stats or spawn is accepted from the Client.

| Opcode | Appearance encoding |
|---|---|
| 12 CharacterList | When any character has a saved appearance or creation choices exist, append `uint8 extensionSchema=1`, then **in the existing character order** `uint8 known + 4 appearance bytes` for each character; unknown has known=0 and four zero bytes. Follow with `uint16 choiceCount` and choiceCount distinct 4-byte tuples. At most 4,096 choices; whole-frame bound 19,980 bytes includes 32 maximum-length names. |
| 15 CharacterCreate | Existing `uint8 slot + uint16 nameBytes + UTF8 name`, optionally followed by exactly 4 appearance bytes. Max frame 111 bytes. |
| 16 CharacterCreateResult | Existing `uint8 status + int64 characterId + uint8 slot + uint16 nameBytes + UTF8 name`, optionally followed by exactly 4 appearance bytes **only on success**. Max frame 120 bytes. New failure status 7 is InvalidAppearance; failures have no character/name/appearance grant. |

Without appearance data/options the original message bytes remain unchanged. Partial tails, unknown extension schema, invalid flags, nonzero unknown fields, duplicated choices, invalid tuples and trailing bytes fail closed. Creation with configured appearance rules requires an explicitly allowed tuple; omission is rejected. Supplying appearance to a creation policy without appearance rules is also rejected. A duplicate creation remains a slot/name conflict and grants no second character or wallet.

Godot ClientSession copies the requested tuple into its existing bounded intent FIFO; the sole WorldState validates the advertised tuple, matches a successful result against the pending choice, and checks the refreshed list against that receipt. Snapshots deep-copy appearance data. Successful CharacterChoose clears the potentially large creation menu so ordinary World/AOI publication never copies it. No new socket, reducer or alternate gameplay protocol is introduced.

Tests: C#/Godot shared golden, malformed extension and maximum-size parsing; real C# TLS rejects omitted/unlisted/unconfigured choices and tests duplicate/relogin; `run_tls_login.py --appearance` runs a real Godot ClientSession through creation, disconnect, durable relogin, selection and WorldReady. Source rules Host loading, UI selection/profile rendering and AOI appearance publication remain separate pending work; this protocol slice does not prove those features.

The TLS handshake timeout remains 5s. The overall pre-gameplay authorization/character-selection timeout is configurable, default 120s and bounded to 10 minutes. Formal Host admission is bounded globally and per canonical IP before session allocation, and pre-gameplay authentication additionally uses an in-memory dual-scope limiter keyed by source address plus normalized account. Defaults are 12 failures/address, 6 failures/account, 60s failure window and 30s lockout; Host exposes bounded `--auth-*` overrides and reports the active values at startup. Invalid/blocked attempts use the same InvalidCredentials response. Active lockout buckets are never evicted merely to admit a new key; if every bounded slot is protected, the limiter temporarily fails closed for the configured lockout interval rather than allowing capacity pressure to erase a ban. After Gateway binding, identity opcodes are protocol violations. This is one-process admission/brute-force policy, not firewall/DDoS or cross-node persistent lockout.

### 2026-11-20 opt-in public AccountRegister (opcodes 17–18)

Public account creation is an explicit TLS pre-authentication action; it is **not** an AuthRequest fallback. Formal Host defaults to `--account-registration disabled`. When explicitly enabled, opcode 17 `AccountRegisterRequest` carries the same bounded strict-UTF8 credential fields as AuthRequest (username 3–32 bytes, password 8–256 bytes). Opcode 18 `AccountRegisterResult` reports `Created(0) / Unavailable(1) / UsernameTaken(2) / InvalidInput(3) / RateLimited(4)`. The Client never sends an ActorId, account ID, character ID or spawn data during registration.

Registration has a separate bounded canonical-IP quota (default 3 attempts per 10 minutes, 4096 tracked addresses). Every request consumes quota regardless of Created/Duplicate/Invalid so successful account creation cannot bypass the existing failure-only authentication limiter. Quota or table-capacity exhaustion fails closed before Argon2 account creation work. This remains process-local abuse control; firewall/WAF/DDoS, email verification, CAPTCHA, account recovery and distributed quota persistence are outside this contract.

`Created` means only that durable credentials exist. The authorization state machine stays on the same mandatory TLS connection and requires the **next frame to be the ordinary opcode10 AuthRequest**. Only Auth success emits CharacterList; only later CharacterChoose can create a trusted World/Gateway binding. A non-Created registration result ends pre-gameplay authorization. Godot mirrors this boundary: `STATE_REGISTERING -> Created -> STATE_NEW -> STATE_AUTHENTICATING`; the SessionPanel exposes a distinct “建立帳號並登入” action, while normal Login never creates an account implicitly. After a successful registration, automatic reconnect uses ordinary Auth and never replays AccountRegister.

Verification: Server Core **44769015052 PASS (256/256)** covers strict wire, default-disabled registration, enabled real-TLS create→Auth, duplicate and quota rejection with no World actor. Client Core **44770285345 PASS** covers codec/reducer/UI plus real Godot↔C# TLS: `CLIENT_REGISTRATION_TLS_PASS created=true authenticated=true character_list_empty=true gameplay_authority=false`, `REGISTER_FIXTURE_PASS ... characters=0 active=0`, and `GODOT_CSHARP_TLS_REGISTRATION_E2E_PASS ... same_tls=true`. The full Client run also preserves reconnect, login/logout UI, AOI, Map15↔13, portal and formal Host/main-scene regressions.

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

A recoverable same-session AOI continuity failure (for example Delta without its Spawn or a forward **wire EntityRevision** gap) pauses Client gameplay and emits one bounded `WorldResyncRequest`. The Gateway **arms the resync gate before publishing the request** so the AOI worker cannot race a replacement WorldReady ahead of the gate; if the bounded request queue is full the parser retains the complete frame and the ACK state is restored for retry. Requests are consumed only by the existing single `WorldAoiPump` writer. `WorldAoi.TryResync` discards that observer's unsent stale AOI backlog and rebuilds, on the same MapEpoch, `WorldReady → SelfState → current visible Spawn set`. Frames already accepted into the Gateway reliable FIFO stay ordered ahead of the replacement WorldReady; while `resync_pending`, Godot ignores those FIFO-old AOI frames. Gameplay reopens only after the replacement WorldReady is ACKed.

Security/correctness boundary: wrong own ActorId/MapId/EntityMapEpoch/ConnectionEpoch still fails closed; resync is for continuity loss within an authenticated current binding, not a way to heal identity corruption. Server core CI 43988244612 passes 92/92 checks including the live bounded AOI pump rebuild and raw TLS listener ACK. Client core CI 43988918797 passes the reducer golden fixtures, 0/30/60/90 FPS independence, automatic `ClientSession` resync orchestration, real TLS, UI, two-client AOI/cross-map/NPC and formal Host + two real main scenes. Same-socket resync本身不等於 reconnect；其後正式 ClientSession 已另加入 bounded TLS/TCP auto-reconnect，見下一節。

### 2026-11-22 Remote player checkpoint coalescing + authoritative movement intent

`ServerWorld` 仍以 30Hz 保存每一個權威 Player `EntityRevision`，本人的 `SelfState` 也仍在每個權威 revision 可靠送達，Client prediction/reconciliation 不降頻。對其他 observer 的位置 checkpoint 仍由 `WorldAoi` coalesce：同一段連續移動預設最多每 **3 World ticks** 或累積 **24px** 送一個 `EntityDelta`；Facing 改變、Move↔Stop、CombatDead 改變及 idle 權威位置校正立即送。為了不把「刻意 coalesce」誤判成丟包，AOI 對每個 observer 分開保存 **SourceRevision** 與 **WireRevision**，每個真正送出的 Delta wire revision 只 +1；Godot 的 `entity_revision_gap` 保護不放寬，same-MapEpoch resync 仍以最新 source snapshot 重新 Spawn baseline。

在此 checkpoint 之上，正式 V1 compact-binary 追加一個 **server-only** 事件，不改舊 opcode 編號：

| Opcode | Event | Total bytes | Payload, little endian |
|---|---|---:|---|
| 110 | PlayerMovementIntent | 62 | `int64 receiverMapEpoch, int64 serverTick, int64 actorId, int64 entityConnectionEpoch, int64 entityMapEpoch, uint32 movementActionSequence, float32 finalTargetX, float32 finalTargetY, uint8 moving, uint8 facing` |

`PlayerMovementIntent` 的 target 不是 Client 原始 click 座標；它由 `ServerWorld` 已接受、已通過 Blocking/A* 權威驗證的 route 最後一格中心推導。AOI 在 **Start / Retarget / Stop** 邊界可靠送出 intent；若 observer 在另一玩家已移動途中才進 AOI，或 same-MapEpoch resync 重建 baseline，順序為 `Spawn → PlayerMovementIntent`，所以不需等待下一個 checkpoint 才知道目前動作目標。Stop 會保留即時 `EntityDelta` checkpoint，再追加同 action sequence 的 `moving=false` intent。事件使用既有 bounded AOI pending FIFO、Gateway reliable FIFO、MapEpoch/ConnectionEpoch generation gate，沒有新增 Client authority 或第二套 transport。

Godot `WorldState` 將 intent 存在獨立的 `remote_movement_intents`，不覆寫 checkpoint revision。Reducer 要求先有對應 Spawn、generation 完全一致、action sequence 不回退；同 action 若 target 無故改變或 stop 後又以相同 sequence restart 會要求 bounded resync。Despawn、換圖與 replacement WorldReady 都清除舊 intent。**目前 presentation 尚未消費 final target 去重建 Blocking route**，所以這個切片只完成 Server→wire→reducer 的正式語意與安全邊界，不能宣稱遠端人物已做到原版整段自走。

固定相同的 30 個、每 tick 8px source trace A/B 仍為舊 every-revision **30 Delta**、正式預設 **10 Delta**，減少 **66.7%**；新增 intent 後沒有把 checkpoint 降頻倒退。`SGTest.Server.Checks --aoi-only` **23/23 PASS**；`--load-only` **5/5 PASS**。本機最重 sustained Gate 為 20 個真 AES 認證連線、同視野 300 Monster + 500 Drop、320 MoveReceipt：move receipt P95 **47.23ms**、P99 **47.26ms**、Router backpressure **0**、World skipped deadlines **0**。C#／Godot 共用 62-byte golden、Godot AOI reducer與 wire codec headless 測試亦 PASS；Windows 真 renderer A/B、WAN/jitter、長時間 soak 與 presentation route reconstruction 仍 NOT VERIFIED。

### 2026-11-23 Pet authority V1（第一階段）

Pet 第一個正式切片沿用既有 authenticated Gateway／bounded SessionCommandWorker／reliable FIFO，不新增第二套 transport。Client 只送召喚意圖；Pet Definition、Level、EXP、active state 與 state blob 均由 Server/SQLite 權威持有。

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 111 `PetCommand` | Client→Server | 29 | `int64 MapEpoch, uint32 PetSequence, uint8 Action, int64 PetUid`；Action=`1 Summon / 2 Recall / 3 Refresh`，Refresh 的 PetUid 必須為0 |
| 112 `PetResult` | Server→Client | 38 | `int64 MapEpoch, uint32 PetSequence, uint8 Action, uint8 Status, int64 PetUid, int64 ActivePetUid` |
| 113 `PetSnapshot` | Server→Client | `26 + 24*N` | `MapEpoch, ActivePetUid, count`，每列 `PetUid, DefinitionId, Level, Experience`；N≤64 |
| 114 `PetEntitySpawn` | Server→Client | 64 | `int64 ReceiverMapEpoch, int64 ServerTick, int64 OwnerActorId, int64 OwnerConnectionEpoch, int64 OwnerMapEpoch, int64 PetUid, int32 DefinitionId, int32 Level` |
| 115 `PetEntityDespawn` | Server→Client | 56 | `int64 ReceiverMapEpoch, int64 ServerTick, int64 OwnerActorId, int64 OwnerConnectionEpoch, int64 OwnerMapEpoch, int64 PetUid` |

SQLite schema v30 新增 `pet_instances` 與 `pet_actions`；同一角色以 partial unique index 保證最多一隻 active pet，action key 綁定 `(character, connection generation, MapEpoch, sequence)`。完全相同重送可回 AlreadyProcessed；相同 sequence 不同 payload 回 SequenceConflict；外來 PetUid 不可召喚。登入/重連 attach 會可靠送 authoritative roster/active snapshot。Godot `WorldState` 只接受目前 MapEpoch，舊 map frame 計 stale 不污染新角色/新地圖，future frame fail closed；`ClientSession` 的 summon/recall/refresh 共用正式 intent FIFO。

`PetEntitySpawn/Despawn` 是第二階段的 **world identity / AOI presence**，仍不代表真正 Pet follow AI。Server 只在 durable active pet 已提交後把 identity 發給單一 AOI owner；observer 只有在看得到 owner（或 owner 自己）時才看得到 Pet，recall、離開視野、斷線與換圖 generation 會可靠 Despawn/重建。Godot 以 `OwnerActorId + OwnerConnectionEpoch + OwnerMapEpoch` 驗證，replacement WorldReady/換圖會清除舊 Pet entity。此階段 Pet 位置刻意錨定 owner，未實作獨立座標、跟隨距離、A*、動畫或戰鬥，因此**不得**把這個 AOI presence 當成 HK follow 行為已還原。

第一階段 authority 實作 commit `e62fdda`；第二階段 PET-AOI-V1 為本次提交。C#/Godot Pet/AOI golden、architecture conventions **26/26 PASS**、Server `--pet-only` **2/2 PASS**、`--aoi-only` **24/24 PASS**，以及 Godot `test_pet_wire.gd`、`test_aoi_wire.gd`、`test_client_fps_independence.gd` 均實跑 PASS；Server/Checks Release build 0 warning/0 error。本機既有 PID 61460 鎖住正式 Host Release DLL，因此未終止該程序，改以 `tmp/pet-aoi-host-validation` 隔離編譯本次 source，`--identity-admin-only` **7/7 PASS**、完整 Server Checks **301/301 PASS**。真正 follow/pathing、戰鬥、外觀/動畫、成長、裝備/卡片、取得流程、Mecha 與 Paradise/HK A/B 仍 NOT VERIFIED。

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

Monster EntityId/EnemyId/HP/state/facing/target are server-owned. The client cannot submit damage through these frames. `WorldState.monsters` has its own identity/revision reducer: duplicate/stale revisions are ignored, missing Spawn or identity conflicts enter the bounded `WorldResyncRequest` path, but a strictly newer revision may jump because `MonsterRuntime` can make several internal AI transitions inside one server tick and `WorldAoi` intentionally publishes only the latest complete coalesced snapshot. Reliable FIFO, MapEpoch, ServerTick and complete snapshot identity provide the ordering boundary; the revision is monotonic state version, not a guaranteed contiguous event sequence. A replacement WorldReady clears both player and monster baselines.

The same `WorldAoi` tracks player and monster spatial visibility. Monster-enabled hosts preserve every authoritative 30Hz World Tick as the single AI clock, but empty ticks do not themselves create reliable wire frames. On a tick that changes monster state, AOI stages the final coalesced snapshot before applying Player Join/Move visibility and then performs a revision-idempotent final refresh; a joining client receives one current MonsterSpawn rather than stale Spawn + same-tick Delta.

Cross-runtime fixtures validate 78/40-byte C#↔Godot goldens and malformed state rejection. The reducer fixture now explicitly validates a forward coalesced Monster revision jump without false resync while a Delta with no Spawn baseline still requests bounded resync. Combat presentation frames may reference that newer coalesced target revision before the full MonsterDelta is reduced; they remain presentation-only and never mutate monster HP/state. Client Core [44750589961](https://github.com/kevin840307/SGTest/actions/runs/44750589961) **PASS** includes the previously failing two-real-Godot authoritative player-attack-visual E2E plus reconnect, AOI, cross-map, portal and formal Host/main-scene regressions. These frames carry authoritative monster state only; player→monster attack requests, player HP settlement, MISS/skills/rewards/drop durability remain later Combat protocol work.


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

### 2026-11-18 Self-centered area skill completion

`SkillUse` opcode44 remains the only Client skill command. A Server rule with `target_kind=self_centered_area` requires the Client to send `TargetMonsterEntityId=0`; this is an explicit area sentinel, not a monster identity. Godot exposes it only through `ClientSession.cast_area_skill(skill_id)`. The ordinary `cast_skill(skill_id, target)` API still rejects target zero. Client commands still carry no damage, radius, line-of-sight policy, MP cost or selected-target list.

The Server selects targets from the authoritative actor position and current MonsterRuntime. A verified area rule must state radius and line-of-sight policy explicitly. Selection is bounded to 1024 targets; overflow rejects the whole cast as `SkillUseStatus.TooManyTargets=12` instead of silently applying a partial effect. Durable MP settlement still occurs once per authenticated SkillSequence before effect commit.

For an Applied area cast, opcode45 is deliberately a summary only: `TargetEntityId=0`, `Damage=0`, `CurrentHP=0`, `MaxHP=0`, `TargetRevision=0`. It therefore cannot become aggregate or per-target HP authority. Every actual hit uses the existing opcode46 presentation event for that concrete monster and the existing authoritative MonsterDelta for HP/death. Godot replay tracking is keyed by attacker generation + SkillSequence + target: multiple different targets sharing one sequence are legal (bounded to 1024), while a duplicate target for the same sequence and any lower sequence are stale.

The real TLS gate uses a SyntheticTest rule `skill=99001, fixed_damage=40, radius=18, mp_cost=1` only to verify mechanics. From actor `(24,24)`, two known-walkable monsters at distance16 each become 60/100, a third at about22.6 stays100/100, and persisted MP changes 2→1 exactly once. This does **not** claim radius18 or fixed40 are original 99001 values; current-client source evidence separately records `aoe_radius=900`, `cost_mp=1`, `base_damage=1000`, `damage_multiplier=100`, `cooldown=0`, `tick_count=1` without promoting those fields to a final damage formula.

Client core run **44277982674** is PASS. Relevant gates: `SKILL_COMBAT_GODOT_PASS ... area_target_zero=true area_summary_no_hp_authority=true same_sequence_multi_target_visual=true duplicate_target_stale=true`; `CLIENT_SKILL_AOE_TLS_PASS ... hits=2 near_hp=60,60 far_hp=100 mp=2_to_1 summary_damage=0 per_hit_visuals=2 monster_delta_authority=true`; C# fixture `SKILL_AOE_FIXTURE_PASS ... monster_hp=60,60,100 player_mp=1/2 commands=1`; runner `GODOT_CSHARP_TLS_SKILL_AOE_E2E_PASS ...`; reconnect, cross-map, NPC and formal Host/main-scene gates remain green.

Remaining source work: original AP/DP/HR/DR stat semantics are still not runtime-ready. The converter currently labels EN_Att correlations as `candidate_only`, `semantic_names_verified=false`, `runtime_ready=false`; therefore the verified Old hit/damage formulas must not yet be activated with guessed enemy columns.

### 2026-11-19 Personal storage authority (opcodes 52–56)

Personal storage uses the same authenticated TLS compact-binary path and reliable FIFO as inventory/economy. V1 deliberately moves only a whole Item UID stack; original capacity, fees, partial-stack behavior, and account-wide versus character-scoped storage are not inferred.

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 52 StorageOpen | Godot→Server | 24 | MapEpoch; StorageSequence; NpcSpawnId |
| 53 StorageSnapshot | Server→Godot reliable | 32 + 29*N | MapEpoch; StorageSequence; status; NpcSpawnId; StartIndex; Complete; Items[ItemUid, ItemDefinitionId, Quantity, SourceDropId, EnhancementLevel(u8), DurabilityRaw(i32; -1=unset)] |
| 54 StorageDeposit | Godot→Server | 32 | MapEpoch; StorageSequence; NpcSpawnId; ItemUid |
| 55 StorageWithdraw | Godot→Server | 32 | MapEpoch; StorageSequence; NpcSpawnId; ItemUid |
| 56 StorageTransferResult | Server→Godot reliable | 50 | MapEpoch; StorageSequence; kind; status; NpcSpawnId; ItemUid; ItemDefinitionId; Quantity; SourceDropId |

Client commands never supply ActorId, ConnectionEpoch, ItemDefinitionId, quantity, fee, or storage coordinates. The Gateway rebuilds identity from the TLS binding and rechecks Actor/MapEpoch/death/storage-NPC proximity for every command. One bounded 16-command worker per connection shares the existing gameplayGate, keeping SQLite work outside the World/AOI tick.

The formal Host enables storage only with --storage-rules and a verified_npc_storage_rules v1 manifest declaring rules_verified=true and gameplay_ready=true. The manifest identifies map/NPC spawn IDs only; coordinates are joined from audited NPC converter output. Missing/unverified rules create no usable storage NPC.

SQLite preserves exact ItemUid, provenance, durable `enhancement_level`, and `durability_raw` while moving a whole stack between item_instances and storage_items in one transaction. StorageSnapshot item rows are 29 bytes so opening storage reconstructs +N and per-instance durability directly; deposit/withdraw receipts remain 50 bytes because the reducer copies both fields only from the already-authoritative source location after exact UID/definition/quantity/provenance validation. Cross-location triggers reject duplicate ItemUid or non-null SourceDropId across both locations. storage_actions records SourceDropId so exact retry after a lost success response returns AlreadyApplied with enough authority to reconstruct the same item without minting or moving it twice. Godot accepts ordered snapshot chunks (max 128 items/frame, 4095 total), exposes storage_ready only after the final chunk, and fails closed on UID overlap, wrong request/generation, future reply, or provenance mismatch.

Verification: Client Core 44425019503 PASS covers Godot codec/reducer and whole-stack SessionPanel controls; Server Core 44425185533 PASS adds an authenticated loopback SslStream lifecycle with exact replay and relogin. Client Core 44425960941 PASS additionally executes a storage-specific real Godot ClientSession → TLS → C# Gateway/Auth → SQLite → reliable result → Godot reducer lifecycle: open, whole-stack deposit, whole-stack withdraw, with the C# fixture confirming the final inventory retains the same ItemUid, Quantity, and SourceDropId and storage is empty. The same final Client Core run also passes reconnect, two-client AOI, Map15↔Map13 transfer, audited portal, and formal Host + two main-scene regressions. Windows/private-asset GUI verification remains NOT VERIFIED.

### 2026-11-19 Durable equipment authority (opcodes 57–60)

Equipment V1 introduces a third physical Item UID location beside inventory and personal storage. It carries the already-authoritative per-instance durability value, but intentionally does **not** define original slot names, stat bonuses, paper-doll mappings, durability wear/repair formulas, enhancement rules, or class/level restrictions. Those semantics remain source-gated.

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 57 EquipmentSnapshot | Server→Godot reliable login baseline | `23 + 29*N` | `CharacterId; StartIndex; Complete; Items[ItemUid, SlotId, ItemDefinitionId, SourceDropId, EnhancementLevel(u8), DurabilityRaw(i32; -1=unset)]` |
| 58 EquipmentEquip | Godot→Server | 28 | `MapEpoch; EquipmentSequence; ItemUid` |
| 59 EquipmentUnequip | Godot→Server | 28 | `MapEpoch; EquipmentSequence; ItemUid` |
| 60 EquipmentTransferResult | Server→Godot reliable | 46 | `MapEpoch; EquipmentSequence; kind; status; ItemUid; ItemDefinitionId; SlotId; SourceDropId` |

The Client never supplies ActorId, ConnectionEpoch, ItemDefinitionId, SlotId, quantity, stats, or bonuses. Identity comes from the TLS binding. On Equip, SQLite reads the owned inventory row inside the same transaction, then resolves its ItemDefinitionId through the operator-admitted `verified_equipment_item_rules` manifest. A valid V1 manifest is version 1, has `rules_verified=true`, `gameplay_ready=true`, bounded provenance, and unique ItemDefinitionId→opaque SlotId rules. Missing rules reject Equip with `NoRules`; Unequip remains available so persisted equipment cannot become trapped.

SQLite schema v11 stores quantity-1 rows in `equipped_items`, enforces one `(character, slot)` row, and uses cross-location triggers so the same ItemUid or non-null SourceDropId cannot coexist in inventory, storage, and equipment. `equipment_actions` is the durable idempotency receipt keyed by authenticated character, durable connection generation, MapEpoch, and EquipmentSequence. Exact replay returns `AlreadyApplied`; conflicting sequence reuse is stale. Slot occupied, wrong owner, missing rule, and stale generation do not mutate an item.

Equipment work is drained by a per-connection bounded 16-command worker and serialized through the existing gameplayGate, so SQLite does not run in the 30Hz World/AOI owner. Login sends EquipmentSnapshot only when equipment rules are enabled or persisted equipped rows already exist; equipment-disabled empty deployments retain the previous login frame sequence. Godot requires an ordered complete baseline before allowing Equip/Unequip, moves UID state only after authoritative Applied/AlreadyApplied results, preserves equipment across map transfer, resets only map-scoped mutation sequence, and fails closed on UID, slot, provenance, future-generation, or wrong-request conflicts.

Verification: Server Core **44439539491 PASS** covers schema v11, verified-rule rejection, atomic UID transfer/replay, gateway binding, forged server-result rejection, TLS login/relogin persistence, and hard-kill regression. Client Core **44439678502 PASS** covers C#/Godot wire golden, equipment reducer, opaque-slot SessionPanel UI, and a real Godot ClientSession → TLS → C# Auth/Gateway → SQLite → reliable result → Godot reducer Equip/Unequip lifecycle; reconnect, two-client AOI, Map15↔Map13, audited portal, and formal Host/main-scene regressions remain green.

Not verified: original ItemDefinition→named body slot semantics, AP/DP/HR/DR/stat application, class/level/sex restrictions, auto-swap behavior, weapon/paper-doll visuals, production combat wear/Broken effects/enhancement, or Windows/private-asset GUI. Normal-item durable repair now has a separate verified vertical slice below.

### 2026-11-19 Scoped bank authority (opcodes 61–65)

Bank V1 reuses the existing character wallet and adds a durable bank balance whose owner is **not inferred by the Client**. Every verified bank NPC rule must explicitly declare either character-scoped or account-scoped balance ownership.

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 61 BankOpen | Godot→Server | 24 | `MapEpoch; BankSequence; NpcSpawnId` |
| 62 BankSnapshot | Server→Godot reliable | 42 | `MapEpoch; BankSequence; Status; NpcSpawnId; Scope; WalletMoney; BankMoney` |
| 63 BankDeposit | Godot→Server | 32 | `MapEpoch; BankSequence; NpcSpawnId; Amount` |
| 64 BankWithdraw | Godot→Server | 32 | `MapEpoch; BankSequence; NpcSpawnId; Amount` |
| 65 BankTransactionResult | Server→Godot reliable | 51 | `MapEpoch; BankSequence; Kind; Status; NpcSpawnId; Scope; Amount; WalletMoney; BankMoney` |

The Client never supplies ActorId, ConnectionEpoch, balance scope, wallet balance, or bank balance. Gateway injects authenticated identity. Auth rechecks exact actor generation, MapEpoch, death state, verified bank NPC identity, and proximity for every Open/Deposit/Withdraw request. The formal Host only enables bank NPCs through `--bank-rules` with a `verified_npc_bank_rules` v1 manifest declaring `rules_verified=true`, `gameplay_ready=true`, audited NPC provenance, and explicit `balance_scope=character|account`.

SQLite schema v12 keeps character wallets as the only spendable on-character money source and adds separate character/account bank balances plus `bank_actions` durable receipts. A Deposit or Withdraw updates wallet and the selected Server-owned bank scope in one transaction. Exact replay returns `AlreadyApplied` with original committed balances; conflicting sequence reuse is stale. Account scope is shared only by characters under the same account; character scope is isolated by character.

Bank work runs in its own bounded 16-command connection worker and shares the existing gameplayGate, keeping SQLite outside the 30Hz World/AOI owner. Godot never predicts bank arithmetic: it replaces wallet/bank values only from Server snapshots/results. If a shop is open, the same authoritative wallet value also updates `shop_money`, preventing a second client-side wallet truth. Map transfer and access-invalidating failures close the bank session.

Result validation is fail-closed: Applied/AlreadyApplied and economic failures that expose balances must carry an explicit Character or Account scope; only access failures such as UnknownBank/TooFar/StaleGeneration/PlayerDead may use no-scope with zero balances.

Verification: Server Core **44454312829 PASS** covers character/account scope isolation/sharing, atomic deposit/withdraw, exact replay, stale sequence, insufficient funds, DB reopen, verified NPC scope, Gateway anti-forgery, and authenticated TLS bank lifecycle. Client Core **44454334513 PASS** covers codec/reducer/UI strict scope validation. The real Godot ClientSession → TLS → C# Auth/Gateway → SQLite → reliable result → Godot reducer lifecycle passed in **44454151798**, including open, deposit 400, withdraw 400, final durable wallet=1000/bank=0, and the remainder of reconnect, two-client, Map15↔Map13, portal, and formal Host/main-scene regression gates.

Not verified: the original Map13/15 bank NPC set, whether the original game uses character or account ownership at each bank, fees, interest, transfer limits, cross-character transfer semantics, or Windows/private-asset visual parity.

### 2026-11-19 Server-authoritative repair and durable durability (opcodes 66–69)

Repair V1 extends the same durable Item UID across inventory, personal storage, and equipped locations. SQLite schema v13 stores nullable `durability_raw` on all three physical item locations and preserves that raw value during storage/equipment moves. `repair_actions` is the authenticated durable idempotency receipt keyed by character, durable connection generation, MapEpoch, and RepairSequence. Exact retry returns `AlreadyApplied` with the original committed item/cost/wallet settlement instead of deducting twice.

Repair rules remain source-gated. `verified_repair_item_rules` v1 supplies admitted ItemDefinition price/max-durability/can-repair metadata. `verified_npc_repair_rules` v1 identifies an audited NPC and its admitted repair-rate parameter. The formal Host requires **both** `--repair-rules` and `--repair-item-rules`; supplying only one fails configuration, and supplying neither leaves repair disabled. Current Old/Recovered evidence includes `Repair.txt -> OpenRepair(10)`, the separate horse-equipment repair script using `OpenHorseEquipRepair(80/100)`, and a Recovered client repair UI that sends only item type + UID while displaying durability from ItemDefinition and instance Damage. This V1 slice implements normal inventory/equipped items only; it does not claim horse/mecha repair parity.

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 66 RepairOpen | Godot→Server | 24 | `MapEpoch; RepairSequence; NpcSpawnId` |
| 67 RepairSnapshot | Server→Godot reliable | 40 + 29×N | `MapEpoch; RepairSequence; Status; NpcSpawnId; WalletMoney; StartIndex; Complete; Items[]` |
| 68 RepairItem | Godot→Server | 32 | `MapEpoch; RepairSequence; NpcSpawnId; ItemUid` |
| 69 RepairResult | Server→Godot reliable | 62 | `MapEpoch; RepairSequence; Status; NpcSpawnId; ItemUid; ItemDefinitionId; Location; CurrentDurability; MaxDurability; Cost; WalletMoney` |

RepairSnapshot is chunked at at most 128 items/frame and 4095 items total. The Client never submits ActorId, ConnectionEpoch, ItemDefinitionId, item location, current/max durability, price, repair cost, or wallet balance. Gateway restores authenticated identity; Auth rechecks exact actor generation, MapEpoch, death state, verified repair NPC identity/proximity, and verified repair item rules for every request. Repair work is drained by a per-connection bounded 16-command worker and serialized through the existing gameplayGate, keeping SQLite outside the 30Hz World/AOI owner.

Godot treats preview/result values as authoritative presentation and settlement. It does not recalculate repair cost or predict a wallet debit. The reducer verifies that each preview UID exists in the claimed current inventory/equipment location with the same ItemDefinition, applies durable result values only for the exact outstanding NPC/UID/sequence, treats duplicate committed results as stale, synchronizes any already-open shop/bank wallet view from the same Server wallet value, and closes the repair session on map transfer or authority-invalidating failures. SessionPanel displays the Server-provided location, current/max durability, cost, and wallet and sends only the selected UID when repairing.

Verification: Server Core **44470032084 PASS** covers schema v13 durability persistence across equipment/storage moves, admitted rule/catalog rejection, repair-cost and durability conversions, atomic wallet+durability settlement, minimum-one repair, insufficient-funds non-mutation, exact replay, DB reopen, Gateway anti-forgery, authenticated TLS open/repair/replay/relogin, and LoginFixture build. Client Core **44470148328 PASS** includes the targeted Godot repair codec/reducer/UI gates plus a real Godot ClientSession → TLS → C# Auth/Gateway → SQLite → reliable RepairResult → Godot reducer lifecycle: equipped UID remains identical, durability converges 80/100→100/100, Server cost=20, wallet 100→80. The remainder of the same run also keeps login UI, reconnect, two-client AOI, Map15↔Map13 transfer, audited portal, and formal Host/two-main-scene regressions green.

Still not verified/implemented: the production Map13/15 repair-NPC mapping, full original ItemDefinition repair-rule import, the original runtime events that cause wear and the exact raw wear amount per attack/hit/death/etc., Broken-state combat/stat effects, horse/mecha repair, enhancement, and Windows/private-asset visual parity. `ApplyTrustedEquippedWearAsync` is therefore a Server-trusted durable primitive and test seam, **not** a claim that combat wear gameplay is complete.

### 2026-11-19 Player trade authority (opcodes 70-72)

Player trade is a two-session negotiation. Pending offers are ephemeral; only the final SQLite settlement is durable. Client commands do not carry ActorId or ConnectionEpoch. TLS binding restores identity, both actors must stay on the same authoritative map generation, and disconnect or map transfer cancels a pending trade.

Opcode 70 TradeCommand is 53 bytes and carries MapEpoch, TradeSequence, Action, TradeId, TargetActorId, ItemUid and Money. Opcode 71 TradeSnapshot is now 59 + 29*N bytes and carries the session revision, peer identity, both money offers, lock/confirm flags and both whole-ItemUid offer lists; each offer row is ItemUid, ItemDefinitionId, Quantity, SourceDropId, EnhancementLevel(u8) and DurabilityRaw(i32, -1=unset), with at most 32 items per side. Opcode 72 TradeTerminal remains 37 bytes and carries MapEpoch, TradeId, Revision, terminal status and authoritative wallet money.

ItemDefinitionId, Quantity, SourceDropId, EnhancementLevel and DurabilityRaw come only from Server-owned inventory rows; verified_trade_item_rules v1 decides tradeability. Any offer mutation clears both lock/confirm flags. Settlement requires both locks and then both final confirmations. After both authenticated gameplay gates are acquired, the coordinator re-reads each offered UID and compares definition/quantity/provenance/+N/durability with the exact snapshot both peers reviewed; a changed +N or repaired/damaged durability fails the trade before SQLite commit. CommitInventoryTradeAsync swaps admitted ItemUid rows and both money offers in one SQLite transaction. Schema v21 stores EnhancementLevel in the durable trade_commit_items receipt, so exact replay by TradeId returns the originally committed +N rather than re-reading mutable state; conflicting reuse returns StaleTrade.

Concurrent peer mutations are supported: AddItem and SetMoney revalidate the same live session, issuing participant sequence/lock and current ownership after SQLite awaits, without rejecting a valid mutation merely because the peer advanced the shared session revision. State mutation, immutable snapshot capture and reliable FIFO admission are serialized by the coordinator so cancel/disconnect cannot place a terminal before an older snapshot.

Verification: the original concurrency/snapshot-ordering gate remains covered by Server Core 44478021176 / Client Core 44478021268. The +N integrity extension is covered by Server Core 44730043863 PASS. The completed durability extension is covered by Server Core **44733884290 PASS**: durable receipts replay durability 80/60, and a dedicated authority test changes a reviewed raw durability 80 to 70 before the second final confirm and proves the final commit returns CommitFailed without moving UID or wallets. Client Core **44733971354 PASS** decodes the 29-byte offer row, rejects DurabilityRaw below -1, renders authoritative durability in the trade UI, and the real two-Client TLS trade swaps +5/+7 items while preserving durability 80/60. The same run also passes Whisper cross-map routing, reconnect, combat/PvP, NPC portal and formal Host with two actual main-scene clients. Full original trade rules and Windows/private-asset UI remain incomplete.


### 2026-11-20 Player-shop enhancement snapshot integrity (opcodes 89–91)

Player-shop commands remain identity-minimal: opcode 89 carries only the action-specific MapEpoch/sequence/seller/listing/item/price choices, while authenticated ActorId/ConnectionEpoch and ownership remain Server-owned. Opcode 90 PlayerShopResult is now 91 bytes and appends EnhancementLevel(u8) to the authoritative listing/item snapshot; opcode 91 PlayerShopSold remains 48 bytes.

Schema v21 adds EnhancementLevel to player_shop_listings. Creating a listing freezes UID, definition, quantity, SourceDropId, durability and +N as one immutable sale snapshot. Buy re-reads the seller's current inventory row in the same SQLite transaction and requires every frozen field, including EnhancementLevel, to match. If the same UID changes from +2 to +3 after listing, the listing becomes StaleListing and no wallet or ownership mutation occurs. Godot Inspect/Buy receives and displays the Server +N; a successful purchase inserts that same +N into the buyer's authoritative inventory reducer, while replay checks reject a conflicting local UID state.

Verification: Server Core 44730043863 PASS covers schema v20→v21 migration, +5 listing/sale/replay persistence and a +2 listing mutated to +3 before purchase that is rejected stale with seller ownership and both wallets unchanged. Client Core 44729632771 attempt 2 PASS runs the real two-ClientSession TLS player-shop path with a +6 item: seller login/listing, buyer inspect, atomic purchase and buyer inventory all retain +6. Windows/private-asset GUI parity and original shop presentation remain NOT VERIFIED.

### 2026-11-20 Server-authoritative equipment enhancement (opcodes 94–97)

Equipment enhancement reuses the one durable Item UID and SQLite schema v20 `enhancement_level` field; +1/+2/... are instance state, never replacement ItemDefinition IDs. Production rules remain source-gated. `verified_equipment_enhancement_rules/v1` supplies the admitted current→target step, success probability in ppm, money cost and whether failure consumes that money. The current V1 deliberately admits only the verified `no_change` failure behavior; downgrade, item destruction, materials, stat formulas and original content are not invented. A separate `verified_npc_enhancement_rules/v1` identifies audited enhancer NPC spawns. Formal Host enhancement is enabled only when both verified manifests are supplied.

| Opcode | Direction | Bytes | Payload |
|---|---|---:|---|
| 94 EnhancementOpen | Godot→Server | 24 | `MapEpoch; EnhancementSequence; NpcSpawnId` |
| 95 EnhancementSnapshot | Server→Godot reliable | `40 + 28*N` | `MapEpoch; EnhancementSequence; Status; NpcSpawnId; WalletMoney; StartIndex; Complete; Items[ItemUid, ItemDefinitionId, Location, CurrentLevel, TargetLevel, SuccessPpm, MoneyCost, ConsumeMoneyOnFailure]` |
| 96 EnhancementItem | Godot→Server | 32 | `MapEpoch; EnhancementSequence; NpcSpawnId; ItemUid` |
| 97 EnhancementResult | Server→Godot reliable | 70 | `MapEpoch; EnhancementSequence; Status; NpcSpawnId; ItemUid; ItemDefinitionId; Location; PreviousLevel; TargetLevel; CurrentLevel; Succeeded; MoneyCost; MoneyCharged; WalletMoney; RollPpm` |

The Client does not send ActorId, ConnectionEpoch, ItemDefinitionId, location, enhancement levels, probability, cost, wallet balance or random roll. Gateway binds authenticated identity. A per-connection bounded-16 worker stays outside the 30Hz World owner; authorization rechecks current actor generation, MapEpoch, death state and verified NPC proximity on every open/attempt. SQLite owns the random outcome, money debit and level mutation in one transaction and persists an idempotent receipt; an exact retry returns the committed result without charging or enhancing twice.

Godot keeps an exact pending MapEpoch/sequence/NPC/UID gate, renders only the Server preview, and mutates local level/wallet state only after a matching Server result. Enhancement is serialized against the other inventory/economic mutations on the same session. Map transfer clears the map-scoped enhancement session; duplicate committed replies are stale rather than reapplied.

Because enhancement level and per-instance durability are durable owned-item state, the existing baselines carry both rather than adding a second state channel: opcode 36 InventorySnapshot and opcode 57 EquipmentSnapshot item rows are 29 bytes, ending with `EnhancementLevel(u8)` plus `DurabilityRaw(i32; -1=unset)`; opcode 53 StorageSnapshot uses the same trailing fields. A true second TLS login is covered by the Server enhancement lifecycle check and must reconstruct the committed +N directly from that baseline.

Verification: Server Core runs 44720352423 and 44722946220 PASS the wire/Gateway/catalog/authenticated TLS/atomic replay/relogin coverage. The real Godot ClientSession enhancement gate has also passed the full Godot→TLS→C#→SQLite→reliable result path with a deterministic synthetic verified rule (0→1, 100% admitted probability, 100 money cost) and confirms the same ItemUid, Server-owned probability/cost, one debit and durable +1. Original Map15/Map13 enhancer mappings, original non-`no_change` failure rules, materials/stat effects, equipment visual changes and Windows/private-asset GUI parity remain NOT VERIFIED.


### 2026-11-20 authenticated online Whisper (opcodes 98–99, delivery reuses 93)

Whisper extends the existing bounded authenticated chat path rather than creating a second social transport. Existing opcode 92 remains Nearby-only. Opcode 98 `WhisperCommand` keeps the legacy ActorId form byte-for-byte compatible: `MapEpoch; ChatSequence; TargetActorId; BubbleType; UTF8 Message`. A name-target request sets TargetActorId=0 and appends `TargetNameLength(u16); UTF8 TargetName`; exactly one selector is legal. Sender identity remains connection-bound Server state. Opcode 99 `WhisperResult` is the reliable sender receipt. Name requests are resolved only against current authenticated PlayerIdentity through the shared Form-C/trimmed ordinal resolver; `Delivered` returns the resolved ActorId/current ConnectionEpoch/Server name, while missing or ambiguous names return `TargetUnavailable` without target generation/name.

The recipient still receives opcode 93 `ChatDelivery` with `Channel=Whisper(4)`. Unlike Nearby, Whisper does not require AOI visibility or same-map membership: it routes to the resolved target's current online Gateway binding. It remains **online-only**; there is no offline mailbox, friend requirement or persistent message history.

Nearby and Whisper share the same per-socket `ChatSequence` and bounded TLS chat worker. Godot serializes a pending Whisper result and remembers the requested name only for correlation; it never chooses target generation. Outgoing history is created only after authoritative `Delivered`, using the Server-returned resolved ActorId/name. Legacy ActorId requests still require exact result TargetActorId equality; name requests begin with TargetActorId=0 and may legitimately receive the resolved nonzero ActorId. Missing targets create no optimistic history.

Verification: Server Core **44756567741 PASS** covers the extended opcode98 contract, authenticated name resolver, resolved-result Gateway correlation and legacy ActorId mismatch rejection. Client Core **44756567680 PASS** covers codec/reducer/UI and the real two-Client Map15→Map13→Map15 TLS gate: MultiHeroTwo on Map13 sends by character name `MultiHeroOne`; the Map15 recipient receives authenticated delivery, the sender receives the Server-resolved ActorId/current generation/name, and later movement/return remains healthy. The same run passes PlayerShop, reconnect, combat, AOI, audited NPC portal, formal Host and two actual main-scene Godot clients. Windows/private-asset GUI parity remains NOT VERIFIED.


### 2026-11-20 authenticated online Party membership and Party Chat (opcodes 100–103; chat reuses 92/93)

Party is an **online-only Server-owned membership session** layered on the existing authenticated Gateway/TLS/chat path. It does not create a second social socket and does not add SQLite party persistence. A Party survives authoritative map transfer on the same connection generation, but disconnect removes that exact member generation; if the leader disconnects or leaves, leadership deterministically transfers to the first remaining member, and the final member leaving dissolves the party. V1 is bounded to 8 members; this cap is an implementation bound, not a claim that the original game's exact party-size rule has been verified.

| Opcode | Direction | Bytes | Purpose |
| --- | --- | ---: | --- |
| 100 PartyCommand | Client → Server | 37 + optional UTF-8 target name | MapEpoch, PartySequence, Action, PartyId, TargetActorId, then an optional character-name tail used only by Invite. Invite provides exactly one selector: ActorId (legacy 37-byte form) or normalized character name. Sender identity is never sent by the Client. Actions are Invite / Accept / Reject / Leave / Kick / Promote. |
| 101 PartyResult | Server → Client | 38 | Reliable result for the exact PartySequence/action/target. |
| 102 PartyInvite | Server → Client | 46 + UTF-8 name | Reliable invite containing the Server-owned inviter ActorId, current ConnectionEpoch and display name. |
| 103 PartySnapshot | Server → Client | 37 + Σ(18 + UTF-8 name) | Reliable revisioned membership snapshot with PartyId, LeaderActorId and up to 8 current exact-generation members. A newer zero-member snapshot clears membership. |

Gateway binds the sender ActorId/ConnectionEpoch and rejects stale MapEpoch or replayed PartySequence before the command enters a bounded 16-command party worker. Invite-by-name is resolved only against `WorldResultRouter`'s currently connected authenticated PlayerIdentity using the same Form-C/trimmed, ordinal character-name semantics as persistence; missing or ambiguous names fail closed as TargetUnavailable, and the PartyResult returns the resolved ActorId. The Client cannot provide the target ConnectionEpoch. `PartyCoordinator` owns PartyId allocation, pending invites, membership, exact member connection generations, leader authority and revision. Invite creates a singleton party when the inviter has no current party; only the current leader can invite from an existing party, kick or promote. Accept requires the exact pending PartyId/inviter and revalidates that the inviter is still the current online leader on the same connection generation. Promotion invalidates outstanding invites from the old leader. Kick/leave sends a cleared snapshot to the removed current connection and a new snapshot to remaining members. Stale old-socket disconnect cleanup cannot remove a newer connection generation.

Party Chat does not invent another message wire format: Client opcode92 `ChatCommand` uses `ChatChannel.Party(3)` and Server opcode93 `ChatDelivery` carries the authenticated sender identity. Nearby and Party share the same per-socket ChatSequence replay gate; Whisper remains its direct opcode98 command but shares the same sequence owner. Party Chat intentionally ignores AOI and same-map visibility, but the sender must be a current exact-generation party member and recipients come only from the current party snapshot. A remote member whose reliable queue is backpressured is closed rather than silently losing a supposedly reliable Party message.

Pending Party invitations have an explicit Server-only **60-second monotonic lifetime** (Stopwatch timestamp, no Client timestamp). The private pending-invite record binds the **recipient's exact ConnectionEpoch**, in addition to the inviter generation and PartyId. A reconnecting recipient cannot accept an invitation addressed to its previous socket, even when it knows the old PartyId/inviter; the Server responds `InviteStale`. Expired/old-generation pending invites are discarded when accepting, rejecting, or re-inviting the same target (lazy bounded cleanup; there is no polling timer). Crucially, a late disconnect callback from an old socket may remove only an invitation for that **same target ConnectionEpoch**, not one issued to the replacement connection. This policy is V1 runtime safety, **not a verified original-game invitation timeout**.

Additional verification: Server Core [44742204787](https://github.com/kevin840307/SGTest/actions/runs/44742204787) **PASS** after generation/expiry tests and TLS-auth delegate compatibility repair. Targeted C# tests cover old target reconnect + forged acceptance rejection, new invite surviving old teardown, accepted new-generation invite, deterministic 61s expiry and re-invite/reject, plus all prior Server Core gates. Client Core [44741805286](https://github.com/kevin840307/SGTest/actions/runs/44741805286) **PASS** after the coordinator safety change; no Godot protocol/opcode schema changes were needed.

Godot keeps Party membership across MapEpoch transfer while clearing only a pending Party command. PartyResult/Invite/Snapshot are strict generation/sequence/revision reducers; no Client-side optimistic membership math is allowed. The SessionPanel exposes invite, accept/reject, leave, kick, leader promotion and Party Chat, while member names/leader/status come only from Server snapshots/results.

Verification: Server Core **44739756995 PASS** on functional HEAD `efcc52c6`, including formal Host/fixture builds, the registered Party wire/Gateway/lifecycle checks (invite→accept→3 members→promote→kick→leader disconnect→auto-transfer→final dissolve), existing authoritative checks and hard-kill recovery. Client Core **44739756959 PASS** on the same HEAD. Targeted trace: `PARTY_WIRE_PASS command=100 result=101 invite=102 snapshot=103 max_members=8 crossmap_chat=true nonmember=STALE leader_promote=true clear=true`. The real two-Client Map15→Map13→Map15 TLS Gate first creates Party 1 on Map15, then actor B transfers to Map13 while observer A remains on Map15 and AOI has despawned; both retain Party membership and B's Party Chat sequence 3 reaches A. Trace: `CLIENT_CROSSMAP_PARTY_READY`, `CLIENT_CROSSMAP_PARTY_CHAT_RECEIVED`, `party_crossmap=PASS`, and Server `CROSSMAP_FIXTURE_PASS ... moves=11 party_count=0` after both sockets disconnect. The same run passes real SessionPanel UI, Whisper isolation, reconnect, Trade/PlayerShop/PvP/combat, audited NPC portal, formal Host and two actual main-scene Godot clients.

### 2026-11-21 Server-owned learned SkillBook (opcode 104)

| Opcode | Direction | Bytes | Purpose |
| --- | --- | ---: | --- |
| 104 SkillBookSnapshot | Server → Client | 23 + 5*N | CharacterId, StartIndex, Complete and up to 256 rows of original SkillID(i32)+Level(u8). V1 total is bounded to 4095 learned skills. |

The Client never sends skill level. SQLite schema v22 owns `character_skills`, keyed by the authenticated persistent character and original SkillID. Non-empty snapshots are emitted after CharacterChoose/Inventory baseline and before WorldReady; the reducer requires exact CharacterId, contiguous StartIndex, unique SkillIDs and level 1..255. A completed snapshot is immutable for that login generation. To preserve existing V1 frame ordering for characters with no learned skills, the Server emits no opcode104 for an empty book; WorldReady is the explicit barrier that resolves “no opcode104 received” to an authoritative empty book. If a partial non-empty SkillBook exists when WorldReady arrives, the Client fails closed.

The default real TLS fixture stores original SkillID 28101 at Lv3 before authentication. Godot must reduce `skill_book[28101]=3` and `skill_book_ready=true` before movement proceeds. Server Core **44862370339 PASS** validates schema v22 persistence/reopen and the compact-binary contract; Client Core **44862556115 PASS** validates the targeted reducer plus real C#→TLS→Godot non-empty baseline and all downstream E2E. This opcode establishes level authority only; Cure/Buff effect rules and resource/HP settlement remain separate Server gameplay work.

Authenticated online invite-by-name is implemented and covered by [Server Core 44749493509](https://github.com/kevin840307/SGTest/actions/runs/44749493509) and [Client Core 44750589961](https://github.com/kevin840307/SGTest/actions/runs/44750589961) **PASS**; the latter uses the character name `MultiHeroTwo` in the real two-Client TLS cross-map Party Gate. This is a runtime usability contract, not proof of the original distance/UI rules. Not verified by this slice: original party-size/distance rules, party EXP/drop sharing, party buffs/assist semantics, persistent/offline parties, original Windows/private-asset party UI. Guild/Faction/Warband membership and routing remain future work.
