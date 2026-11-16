# SGTest Server — .NET 8 重建（碰撞／導航／權威 World＋離線 30Hz Host）

新版 Server 是**從零重建**，不還原舊 SGO 原型。`src/SGTest.Server` 目前提供 Map13／Map15 權威碰撞/A*、單寫者 `ServerWorld`、30Hz Host、compact-binary V1、trusted Gateway，以及真 loopback 驗證過的 **TLS 1.2/1.3 單連線 TCP session**。Production Host 仍不開 listener，也沒有內建測試帳密；真正 Account Auth/CharacterChoose、WorldReady／AOI、DB／怪物／戰鬥或 Godot 連線 Client 仍未完成。

## 正式內容契約

`World/BlockingGrid.cs` 載入唯一 Converter 的 `maps/<map_id>/world.json` (v3) + 原始 `blocking.dat`（8 bytes 小端寬高 + 寬×高 raw bytes）。map_id、collision_cell=16、world pixel size、檔案長度和 world.json 中的 `collision_sha256` 必須完全一致；遇到舊 schema／錯地圖／缺少原始檔／被修改或越界時直接失敗。Server 不讀圖片、Atlas，也不使用 Godot 的 collision 做權威依據。

本階段僅將原始 raw flag **0 視為可走**；Map13/15 目前原始值為 0、6、7，6/7 禁止通行。特殊地形／機甲跨越權限尚待直接驗證 Old 原版，不沿用舊原型的假設；未知非0 flag 全拒絕。世界座標用 floor(x/16,y/16)，不 clamp 出界和非有限值。沿線碰撞採 supercover，恰好越過格點角落需兩個側鄰格皆可走，避免穿牆／穿斜角。

`World/MapNavigator.cs` 的 A* 採 8 鄰居、cost 10/14、octile heuristic、確定性 tie-break，含起終格的路徑只供未來權威 World 消費。每次搜尋上限預設 4096 expanded cells，超限／無路／端點禁止通行明確回傳失敗**而非順移到終點**。建立 MapNavigator 以地圖為單位重用；`ServerWorld` 已對每次 Tick 處理的路徑要求設上限，並以 MapEpoch 拒絕舊命令。離線 30Hz Host 已可實跑；Actor/AI 決策排程與 MapEpoch+revision 網路確認仍待實作，不能每隻怪每 Tick 無限制重算。目前 A* 還是簡單的 per-query scratch allocation，尚未做高密度 Monster profiler，非最終 AI 選型或 20人/300怪 Gate。

## 測試（無外部 NuGet 測試套件）

```powershell
# 只測合成格式、Gateway/世代/拒絕案例、斜角、A*、30Hz Host；CI 不需要私有原圖
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release

# GitHub 也會由 .github/workflows/server-core.yml 在 Ubuntu/.NET 8 跑 Server/Host build + 上述 synthetic checks

# 本機額外核實兩張 ORIGINAL Map13／Map15，資產位於 .gitignore 的 client/assets
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release -- --assets "C:\SGO\SRC\SGTest\client\assets"
dotnet build server/src/SGTest.Server/SGTest.Server.csproj -c Release
```

2026-11-16 的初步 Blocking/A* 基準：合成＋雙圖原版 **10 組 PASS**（已保留於現行更完整的 24 組測試中）。Map13／Map15 各 960×960，原始 source SHA 匹配，靜態雙向傳點目的地座標對應可走格並有鄰格路徑（**不是實際傳點**）。128 個 deterministic 候選目的地／圖的獨立 query trace，Map13 77 個可走目標／77 成功、Map15 119/119 成功，無超出 1500 cell 預算；樣本 query P95 Map13 59.5µs／Map15 26.0µs，managed bytes/attempt 約10.2KB／10.9KB。**此為當次私有資料／单程序查詢時間，不是 Server Tick P95/P99、300 怪／多人效能或網路延遲。**

所有原版資料、圖片、PCK、測試輸出都只保留在未追蹤的私有目錄；本 repo 只提交格式解析／導航／權威 World／正式 Wire/Gateway/TLS session 核心與可重跑驗收。WIRE001 + Gateway + 真 loopback TLS session 已有 CI Gate；下一個網路 Gate 是真正 Account Auth/CharacterChoose + production listener/certificate lifecycle，之後接 ACK/WorldReady/reconnect。特殊 terrain/Blocking 旗標與原版速度仍須另外核實。


## REB021-WORLD-CORE（2026-11-16：手動模擬核心，非已啟動 Server）

`src/SGTest.Server/World/ServerWorld.cs` 在已驗證的 immutable `BlockingGrid`／map-local `MapNavigator` 之上新增單一權威 World。只有建構者所在的 thread 能更動 Actor 狀態或呼叫 `Tick()`；只有 `TryPostMove` 是非阻塞的多生產者入口（CAS 預留容量＋ConcurrentQueue）。並未啟動 socket／背景 Timer，`Tick()` 必須由未來固定時鐘 Host 推進，**Godot Rendering FPS 不決定世界邏輯**。

- **連線和權限邊界**：`TryTrustedRegister`、`TryTrustedTransfer` 和 `TryTrustedDisconnect` **只供已驗證身份、已確認原版 NPC/portal 授權的 Server 內部呼叫**；現在還沒有 Auth/Gateway，外部用戶絕不能取得呼叫權。重新登入重發 ConnectionEpoch，可信轉圖重發 MapEpoch、清除路徑並拒絕舊圖命令。這不等於 MapChange／WorldReady／AOI 已實作。
- **Client 請求邊界**：`MoveTargetCommand(ActorId, ConnectionEpoch, MapEpoch, MovementSequence, TargetX, TargetY)` 只請求目標，不接受直接設定位置。World 先驗證 ConnectionEpoch、MapEpoch、嚴格遞增的 uint movement sequence（0 拒絕），再以原始 Map Collision／A* 權威決定路線。重放、非法座標、目標阻擋、超過 A* 預算都以 `MoveReceiptStatus` 回覆；錯誤輸入不會取代目前合法路徑。現階段不處理 sequence wrap：接近 uint 最大值前須重連／協定協商。
- **固定步進**：每 `Tick()` 最多處理 `MaxCommandsPerTick` 條命令、最多 `MaxPathRequestsPerTick` 次 A*；每 Actor 按 ID 穩定排序推進最多 `MovementPixelsPerTick`，每段再用 `IsDirectClear` 防止穿牆。**8 px/手動 Tick 是測試預設，不是原版人物速度**；正式 30Hz runner、速度／武器／騎乘能力、動作節奏均需從原版核實。此處不啟動 NPC AI、Combat、Drop、網路。
- **防漏狀態**：`LastMovementSequence`（已消費的最高輸入序號）與 `MovementActionSequence`（最後成功授權的 Move／Stop）刻意分離；非法新命令不會誤標目前仍在進行的舊移動。即使 Stop 目的地仍在當前碰撞格、位置沒有改變，亦會增加 `EntityRevision` 並出現在 Tick Changed，供未來其他 Client 收到可靠停止狀態。Tick 回傳有界 `Receipts`／`Changed` 清單，但**尚無正式持久可靠事件佇列／傳輸確認／重同步機制**。
- **預算**：預設 MaxActors=128、MaxKnownActorIds=4096（限制 tombstone 數量）、MaxPendingCommands=512、MaxCommandsPerTick=64、MaxPathRequestsPerTick=8、MaxPathExpandedCells=2048、MovementPixelsPerTick=8。Queue 滿時 `TryPostMove=false`；當 Tick path quota 滿時回 `TickRouteBudgetExhausted`，不悄悄吞掉或無限制累積。Gateway 必須對拒絕／慢客戶端／重試負責，且要另加 per-connection 公平預算。以上都是第一版可調安全上限，不是已完成負載選型。

重跑（已無任何私有資料依賴的合成 checks；原圖部分在 `.gitignore` 中）：

```powershell
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release -- --assets "C:\SGO\SRC\SGTest\client\assets"
```

2026-11-16 .NET 8 Release：**21 組純合成 PASS；額外 3 組真圖相關 Gate，合計 24/24 PASS**。涵蓋 MPSC 256 producer input 的32-entry cap、每Tick 8/6 等不同命令／path quota、wrong owner thread、連線／地圖世代 tombstone、重送／失效／無路／A* budget、正確繞牆、Stop 不移動時的 revision、同一輸入但不同呈現讀取頻率的 StateHash 一致。真原版 Map13/15 驗證包含 20 個**測試用假 Actor** 佇列 burst：一次20條命令在6路徑查詢上限下 6 Accepted／14 明確拒絕；都保持可走位置，**不是20條真 socket、多客戶端 AOI，也沒有300隻怪 AI或Gameplay E2E**。

仍未驗證：原版行走速度與 special terrain、能否穿玩家/NPC、真人點地板 A* 細節、可取消的 async path job、WorldReady／可靠資料通道、30Hz P95/P99、20人/300怪/500掉落真負載、Client FPS 0/30/60／AFK 以及重連後完整狀態回補。下一階段是身份／連線綁定、bounded reliable events／AOI 和真雙圖 Actor 資產顯示，仍依先前 ALT001 比較 Wire 選型。

## REB021-HOST-CADENCE — 獨立可執行 30Hz Host（2026-11-16）

`server/host/SGTest.Server.Host` 現在是**真的可啟動／停止的 .NET 8 Console Host**，嚴格從唯一 Converter 的私人 `maps/{13,15}/world.json + blocking.dat` 載入原版資料。啟動後不開任何 port、不提供登入、完全不處理網路；Host 在與 Godot/UI FPS 無關的專屬 Thread 上建立 `ServerWorld`（單寫者 owner 必須同 Thread），使用 Stopwatch 單調時鐘驅動 30Hz。沒有角色時仍進 Tick，固定 30Hz 是**排程候選，不代表原版人物移動速度／正式 10Hz Snapshot**。

```powershell
# 在專案根目錄；原始資產位於 .gitignore 私有 client/assets
dotnet run -c Release --project server/host/SGTest.Server.Host/SGTest.Server.Host.csproj -- --content "C:\SGO\SRC\SGTest\client\assets" --seconds 5

# 無原圖也能執行合成 Host／失效保護測試
dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj

# 原始雙圖互動狀態額外檢查
dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -- --assets "C:\SGO\SRC\SGTest\client\assets"
```

`ServerWorldHost.cs` 以 `FixedTickCadence` 計算每輪 deadline；掉隊時**跳過時鐘週期並累積 SkippedDeadlines**，不在一幀無上限補跑造成群體同步或 CPU 尖峰。等待下一輪用 WaitHandle 不 busy-spin；停止立即喚醒、清空／拒絕未處理的可信控制請求。排程中的可信註冊／登出／跨圖指令經有界 Channel 在 World owner Thread 套用；不可信移動需經未來認證 Gateway 綁定 ActorId 才能呼叫 TryPostMove。所有命令的處理與 Tick 回覆與畫面 FPS 無關。

權威 `WorldTickResult` 透過**有界輸出 Channel** 供未來傳輸層讀取，Receipts/Changed 不使用 DropOldest。讀者太慢導致寫滿會觸發 `WorldHostBackpressureException`、Host **fail closed**，而不是無聲丟失事件；上層必須關閉連線、驗證後重新取得 WorldReady snapshot。**目前還沒有正式 Gateway／可靠 ACK／慢連線隔離**，這種 fail-closed 是明確的安全邊界，**不等於玩家斷線恢復已完成**。Stop 之前已提交但尚未處理的 Move 不保證得到回覆，未來傳輸層必須以 disconnect/resync 拒絕沿用該連線的未知結果。可信控制流程只可由已授權伺服器內部使用，不可暴露為 Client RPC。

實測 Windows／.NET 8 Release、RTX3050 同機雙圖私有格網、**沒有玩家／怪物網路負載**：`--seconds 5` wall=5020.14ms、151 Tick、實際30.079Hz、SkippedDeadlines=0、max tick work=1219µs、PublishedResults=0、OutputOverflow=0、activeActors=0；此為**單次 idle smoke，不是 20玩家／300怪的P95/P99**。新增的 cadence/生命周期/慢消費者錯誤/可信控制預算與真圖 Host 測試使新版 Server 統計達合成27/27、加上雙圖原版共31/31 PASS。正式 30Hz Tick/snapshot/AOI 與大量玩家AI負載仍待 ALT001 及 E2E 選型。

## 2026-11-16 Identity persistence/auth core

`src/SGTest.Server/Identity/` now provides SQLite schema v1 for Account/Character and bounded Argon2id password verification. The DB/KDF path is deliberately outside the authoritative 30Hz World thread. Account names are normalized for identity, character slot/name uniqueness is enforced by SQLite constraints, schema mismatch fails closed, and passwords are never stored plaintext. Character records persist server-authorized map/position and a stable `character_id`; a future TLS `CharacterChoose` layer will convert only a server-owned selected character into `TrustedRegisterAsync`/`GatewayConnection` identity.

This is **not** a public login endpoint yet. Production listener/certificate setup, Auth/CharacterChoose wire messages, session-token/rate-limit policy, selected-character binding, save/reconnect lifecycle and Godot login E2E remain NOT VERIFIED. Current local Release evidence: Server + Host builds 0 warnings/errors; Server checks 46/46 PASS.

## 2026-11-16 TLS Auth/CharacterChoose binding

The server can now perform the complete **server-side** pre-gameplay identity flow over an already accepted TLS socket: AuthRequest, AuthResult, owned CharacterList, CharacterChoose, trusted World registration and Gateway binding. Persistent `character_id` becomes ActorId only after ownership verification; ConnectionEpoch is still never accepted from the Client. Wrong passwords and foreign-character selections never create a World actor.

This is not yet an Internet-facing login server: `SGTest.Server.Host` still does not open a production listener, certificate deployment/admission/rate limiting are pending, and the Godot client does not yet implement these identity frames. Current local Release gate: **51/51 checks PASS**.

## 2026-11-16 World results → authenticated Gateway

`Gateway/WorldResultRouter.cs` consumes `ServerWorldHost.Results` outside the 30Hz World owner thread. It routes per-actor reliable MoveReceipts to the correct authenticated Gateway, prevents old connections from stealing new sessions, and retains Changed batches for the next AOI slice through a bounded channel. Over-capacity Changed/reliable output is fail-closed and requires supervisor/reconnect recovery, not DropOldest. The test-only real Godot TLS login fixture now uses this same router. GitHub Server checks 56/56 PASS and Client core TLS E2E PASS. **Still needed:** runtime AOI consumer, WorldReady/delta wire, production listener/admission, cross-map resync and two-client E2E.

## 2026-11-16 Login session cleanup and same-character reconnect

For authenticated TCP clients use `IdentityTlsAuthorization.RunAuthenticatedSocketAsync(...)`: it composes Account/CharacterChoose + Router attach + mandatory TLS gameplay + safe Router detach and authoritative World disconnect. Do **not** call bare `TlsWireConnection.RunServerAsync` with a real authenticated World session without wiring equivalent session cleanup. Only trusted World generations can release the matching character; a stale disconnect cannot remove a new login. On failure after registration but before the successful choose response is committed, the actor is rolled back. The new real TLS checks cover normal disconnect/relogin and invalid gameplay opcode/relogin. Actual login UI, character state save, production listener/rate limit, AOI/WorldReady/resync still remain pending.
