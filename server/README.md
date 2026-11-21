# SGTest Server — .NET 8 重建（權威 World／TLS Host）

新版 Server 是**從零重建**，不還原舊 SGO 原型。現有權威 Blocking/A*、30Hz World、SQLite帳號／遊戲內創角／選角、compact-binary V1、TLS授權、WorldReady／AOI及NPC轉圖。Host `serve` 透過有界 TLS listener 接入同一組正式授權／Router／AOI；無內建帳密、明文或自動測試憑證。公開帳號註冊已完成為預設關閉的 opt-in TLS 功能；原版創角外觀／職業／預設出生規則及完整 Release 仍未完成。

## 持續運行的TLS Host（MVP003-TLS-LISTENER）

```powershell
# PFX必須有目前有效的private key；密碼由環境提供，不放在命令列或repo。
dotnet run --project server/host/SGTest.Server.Host -c Release -- serve --content C:/private/runtime --identity C:/private/identity.db --certificate C:/private/server.pfx --bind 127.0.0.1 --port 7777

# 啟用遊戲內創角（以下座標只是部署範例，不宣稱原版預設出生點）
dotnet run --project server/host/SGTest.Server.Host -c Release -- serve --content C:/private/runtime --identity C:/private/identity.db --certificate C:/private/server.pfx --bind 127.0.0.1 --port 7777 --character-spawn-map 15 --character-spawn-x 7872 --character-spawn-y 11296
# 可選：--max-connections 128 --max-per-address 8 --seconds 60
# 精準檢查先建置Host，再執行；只用合成地圖與臨時憑證，不需遊戲素材。
dotnet build server/host/SGTest.Server.Host -c Release
dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --listener-only
```

PFX密碼環境名稱為`SGTEST_TLS_PASSWORD`；未加密PFX可不設定。Host讀取唯一 Converter 產出的兩圖 world／blocking 及 `npcs.json`，缺失／無效內容拒絕啟動；不讀貼圖。Identity DB 不會自動建立帳號；帳號仍由下節管理命令 provision。若要允許登入後遊戲內創角，啟動時必須同時提供 `--character-spawn-map/--character-spawn-x/--character-spawn-y`；Host 會先確認該點屬於目前服務地圖且 authoritative Blocking 可走。未提供時 CharacterCreate 明確回 `Unavailable`。Godot 登入面板的 host／port／TLS 名稱與信任鏈須配合實際憑證。

預設只監聽127.0.0.1，Ctrl+C或期限到期停止；總連線上限包含握手、登入與清理中的Socket，每IP另有限額。Socket讀寫與KDF沿用既有有界實作，不在World鎖或Tick內執行。關閉順序為停止接入／排空授權Socket與可信Disconnect→停止World→停止Router/AOI，pump故障不會繼續提供半套服務。`TLS_HOST_READY`／`TLS_HOST_STOPPED`輸出實際endpoint與統計，無帳密。

本機7項targeted checks與受影響Release build PASS；main CI結果見todo。此為持續登入服務切片，不代表公開網際網路安全／壓測／Release完成；時間型登入速率限制、WorldReady ACK/resync與持久座標仍待驗證。下方限時無網路Host命令保留作smoke，並非serve模式。

## 怪物死亡後的 SQLite 掉落（MVP006 部分完成）

正式 `serve` 未帶 `--loot-rules` 時不會自行創造物品。只有已明確核對原版表格／規則的操作者，才可指定私人掉落規則文件：

```powershell
dotnet run --project server/host/SGTest.Server.Host -c Release -- serve --content C:/private/runtime --identity C:/private/identity.db --certificate C:/private/server.pfx --loot-rules C:/private/verified-loot-rules.json
```

文件格式是 JSON object：`kind="verified_monster_drop_rules"`、`version=1`、`rules_verified=true`、`gameplay_ready=true`、必填 `source_reference`（用於追溯人工核對的原版來源），及 `rules` array。每個 rule 必須提供 `map_id`、`enemy_id`、`slot`（0～31）、`item_definition_id`、`quantity`。規則必須匹配**已載入的 v4 可信怪物清單**，同 map/enemy/slot 不可重複；檔案最多1MiB、1024條規則，違規拒絕。**這一版每個列出的 slot 都是保證掉落，還沒有原版機率／抽樣規則。不要將任意 item ID 或測試規則直接視為原版掉落。** `source_reference` 是操作者自行聲明的來源，不是程式自動驗證原版的真實性。

權威普通攻擊殺怪後，唯一 AOI 消費者將死亡事件加入容量256的非阻塞結算佇列，背景 Worker 對**同次死亡所有 slot** 做一筆原子 SQLite transaction；同死亡 UID 重試不多發，任一 slot 衝突全體回滾。WorldGeneration 由同一 SQLite 每次啟動配置，避免重啟後怪物 revision 重複。容量滿或結算 Worker fault 視為服務異常，不採用 DropOldest；正常停機排空已排隊結算。**硬 kill 發生在入佇列到 DB commit 間的可靠死亡日誌尚未做，不能宣稱掉落完全 crash-safe。** 這是 Server 結算能力，**還沒有**正式 GroundDrop AOI／拾取封包／Godot 地上物與背包 UI。可信 source manifest 請勿提交私人原始遊戲資料到 Git。測試見 `NormalAttackChecks`、`GroundLootChecks`、`MonsterLootSettlementChecks`。

## 本機帳號／角色建置（MVP003-IDENTITY-ADMIN）

```powershell
# 由管理者分別完成兩個明確步驟；預設會在終端隱藏輸入密碼。
dotnet run --project server/host/SGTest.Server.Host -c Release -- account-create --identity C:/private/identity.db --username operator_01
dotnet run --project server/host/SGTest.Server.Host -c Release -- character-create --identity C:/private/identity.db --username operator_01 --content C:/private/runtime --name Hero --slot 0 --map 15 --x 7872 --y 11296

# 停服後建立不覆寫的一致 SQLite 快照；輸出含 schema／bytes／SHA-256。
dotnet run --project server/host/SGTest.Server.Host -c Release -- identity-backup --identity C:/private/identity.db --output C:/private/backups/identity-20261120.db

dotnet build server/host/SGTest.Server.Host -c Release
dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --identity-admin-only
# 若只需重驗PFX載入／真Host TLS路徑：同一測試加上 --tls
```

座標只是管理者明示的範例，必須在待部署Blocking可走範圍內，**不是原版預設出生點**。目前只准備Host已服務的Map13／15，slot0..7；CLI先驗證來源SHA、座標有限／邊界／flag0，再驗證帳密及disabled並呼叫既有Store。兩步各自原子，不是跨步驟交易；帳號建好而角色名稱衝突時，可修正後重跑character-create，不刪除或覆寫帳號。重複帳號不重設密碼，名稱／slot／角色數限制沿用同一 SqliteIdentityStore；目前正式 schema 為 v22；v22 新增 Server-owned `character_skills(SkillID,Lv)`。已存在的已知舊 schema 在 `InitializeAsync()` 真正 migration 前會先以 SQLite Backup API 建立 `identity.db.pre-v<old>-to-v22.<UTC>.<GUID>.bak`，備份成功並通過 schema／integrity 驗證後才繼續升級。

自動化須由祕密來源注入`SGTEST_ACCOUNT_PASSWORD`；命令不接受`--password`，stdout不輸出密碼，CLI結束後應清除呼叫端環境。非互動且未提供環境變數會拒絕；互動隱藏輸入可按Esc取消。本入口只供有本機DB存取權的管理者，不新增網路註冊／GM RPC或預設帳號。

`identity-backup` 是同一信任邊界下的本機 operator 工具，建議在正式 Host 停止後執行。它不呼叫 `InitializeAsync()`，因此不會先把舊 schema 升級再備份；destination 必須是新檔且不可與來源相同。SQLite backup 完成後會要求 `PRAGMA integrity_check=ok`、來源/目的 schema version 相同，再輸出檔案大小與 SHA-256。這提供 migration 前的可回復點與人工快照，**不代表已完成異機備份、保留週期、加密金鑰管理或災難復原演練**。歷史 Server Core `44766021159` 已驗證 v20→v21 backup primitive；目前 v22 migration／SkillBook durability 由 Server Core `44862370339` 再次通過完整 authoritative checks 與 hard-kill recovery。

### 本機 PvP 名稱狀態管理

Recovered 已確認 GM 可指定 `blue/white/pink/flash/red`。SGTest 目前只提供**本機 operator CLI**，不把管理權限暴露成遊戲 socket RPC：

```powershell
dotnet run --project server/host/SGTest.Server.Host -c Release -- pvp-name-set --identity C:/private/identity.db --character-id 123 --state red --operator gm-local-01 --reason "manual moderation" --event-id incident-20261119-001
```

`--state` 僅接受 stable Server tokens `blue|white|pink|flash|red`；中文／其他語系名稱只屬 UI/localization 或文件，不是 Server command contract，localized token 會被拒絕。每次命令必須提供 `--operator`、`--reason`、`--event-id`；schema v18 將狀態變更與 `pvp_name_admin_events` audit row 放在同一 SQLite transaction。相同 event-id＋相同內容重送回 `PVP_NAME_REPLAY` 且不重複修改；相同 event-id 改內容直接拒絕。此命令適合停服／直接 DB 管理；**它不會向已在線角色即時 fanout AOI 更新，也不是網路 GM 權限系統**。正式線上 GM command/auth/audit 已由後續 authenticated GM slice 實作，兩者仍維持不同 trust boundary。

退出碼0=建立／管理成功、2=參數／密碼格式／出生地或管理輸入拒絕、3=重複／帳密／角色政策／管理 event 衝突或未知角色拒絕、1=來源／DB錯誤、4=互動取消。已驗證DB重開、錯誤／disabled、跨程序slot競態，以及CLI建置DB→實際Host serve→TLS Auth／選角／WorldReady／MoveReceipt與清理。Windows互動終端另驗證隱藏輸入、emoji退格後帳密正確、Esc取消碼4／無DB、過長輸入無回顯／無DB；超限後持續讀至Enter才拒絕，不截斷或留下尾端給Shell。私有原版地圖與Godot直連此Host的畫面仍NOT VERIFIED。

## 正式內容契約

`Map/BlockingGrid.cs` 載入唯一 Converter 的 `maps/<map_id>/world.json` (v3) + 原始 `blocking.dat`（8 bytes 小端寬高 + 寬×高 raw bytes）。map_id、collision_cell=16、world pixel size、檔案長度和 world.json 中的 `collision_sha256` 必須完全一致；遇到舊 schema／錯地圖／缺少原始檔／被修改或越界時直接失敗。Server 不讀圖片、Atlas，也不使用 Godot 的 collision 做權威依據。

本階段僅將原始 raw flag **0 視為可走**；Map13/15 目前原始值為 0、6、7，6/7 禁止通行。特殊地形／機甲跨越權限尚待直接驗證 Old 原版，不沿用舊原型的假設；未知非0 flag 全拒絕。世界座標用 floor(x/16,y/16)，不 clamp 出界和非有限值。沿線碰撞採 supercover，恰好越過格點角落需兩個側鄰格皆可走，避免穿牆／穿斜角。

`Map/MapNavigator.cs` 的 A* 採 8 鄰居、cost 10/14、octile heuristic、確定性 tie-break，含起終格的路徑只供未來權威 World 消費。每次搜尋上限預設 4096 expanded cells，超限／無路／端點禁止通行明確回傳失敗**而非順移到終點**。建立 MapNavigator 以地圖為單位重用；`ServerWorld` 已對每次 Tick 處理的路徑要求設上限，並以 MapEpoch 拒絕舊命令。離線 30Hz Host 已可實跑；Actor/AI 決策排程與 MapEpoch+revision 網路確認仍待實作，不能每隻怪每 Tick 無限制重算。目前 A* 還是簡單的 per-query scratch allocation，尚未做高密度 Monster profiler，非最終 AI 選型或 20人/300怪 Gate。

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

This paragraph records the original 2026-11-16 identity-core baseline. The current Server has mandatory TLS Auth/CharacterChoose, formal listener/certificate admission, bounded login rate policy, selected-character binding, reconnect position persistence and Godot TLS E2E. Public self-service account registration and original source-verified character creation appearance/career/spawn rules remain separate work.

## 2026-11-16 TLS Auth/CharacterChoose binding

The server performs the complete **server-side** pre-gameplay identity flow over TLS: AuthRequest, AuthResult, owned CharacterList, optional CharacterCreate/CharacterCreateResult plus authoritative list refresh, CharacterChoose, trusted World registration and Gateway binding. Persistent `character_id` becomes ActorId only after ownership verification; ConnectionEpoch and creation spawn are never accepted from the Client. Wrong passwords, foreign-character selections, missing creation policy, invalid names/slots and SQLite conflicts fail closed.

This paragraph records the first Auth/CharacterChoose slice. Formal `SGTest.Server.Host serve` now opens the configured TLS listener, applies global/per-address concurrent admission and an address+account authentication failure limiter, and the Godot client implements these identity frames. Public account registration is now implemented by the later opt-in registration slice below; Internet-facing firewall/DDoS controls remain deployment concerns.

## 2026-11-20 Opt-in public account registration

Formal Host keeps public registration **disabled by default**. To expose explicit TLS account creation:

```powershell
dotnet run --project server/host/SGTest.Server.Host -c Release -- serve ... \
  --account-registration enabled \
  --registration-attempts-per-address 3 \
  --registration-window-seconds 600
```

The Client’s “建立帳號並登入” action sends opcode17 AccountRegisterRequest. Result opcode18 is one of Created / Unavailable / UsernameTaken / InvalidInput / RateLimited. Every registration attempt consumes the separate canonical-IP quota; bounded-table exhaustion is fail-closed. Ordinary Login never creates an account implicitly.

**Created is not authentication.** The same TLS connection must immediately complete the normal AuthRequest/AuthResult flow before CharacterList is sent. Only a later CharacterChoose can register a trusted World actor. This preserves the same ownership/Gateway authority path for operator-provisioned and public-created accounts, and reconnect uses only normal Auth rather than re-registering. Server Core **44769015052 PASS (256/256)** and Client Core **44770285345 PASS** include real TLS/Godot registration→Auth with an empty character list and zero gameplay authority.

This feature does not provide email verification, CAPTCHA, account recovery, cross-node quota persistence or Internet WAF/DDoS controls.

## 2026-11-20 Formal authentication admission / rate policy

`serve` applies two bounded layers before gameplay: `TlsGatewayListener` caps global and canonical-IP concurrent sockets, while `AuthenticationRateLimiter` gates AuthRequest failures by both source address and normalized account. Defaults are 12 failures/address, 6 failures/account, a 60-second failure window and 30-second lockout. Operator overrides are:

```powershell
dotnet run --project server/host/SGTest.Server.Host -c Release -- serve ... \
  --auth-failures-per-address 12 --auth-failures-per-account 6 \
  --auth-failure-window-seconds 60 --auth-lockout-seconds 30
```

All values are bounded and printed in `TLS_HOST_READY`; invalid values fail before listener startup. The limiter tables remain bounded (default 4096 addresses / 16384 accounts). Nonblocked least-recently-seen buckets may be evicted, but an **active lockout is never evicted to admit a new key**. If every slot in a scope is currently protected, admission temporarily fails closed for the configured lockout window instead of turning table pressure into a lockout bypass. Blocked attempts deliberately return the same `InvalidCredentials` status. Successful credential verification clears that account bucket but does not launder source-IP failures.

This is an in-process game-server safety boundary. Restart resets limiter memory, and it is not a replacement for network firewall, anti-DDoS, cross-node shared bans or abuse analytics. Server Core **44767204976 PASS (253/253)** includes capacity-saturation bypass regression and real Host non-default CLI policy startup; Client Core **44767205009 PASS**.

## 2026-11-16 World results → authenticated Gateway

`Gateway/WorldResultRouter.cs` consumes `ServerWorldHost.Results` outside the 30Hz World owner thread. It routes per-actor reliable MoveReceipts to the correct authenticated Gateway, prevents old connections from stealing new sessions, and retains Changed batches for the next AOI slice through a bounded channel. Over-capacity Changed/reliable output is fail-closed and requires supervisor/reconnect recovery, not DropOldest. The test-only real Godot TLS login fixture now uses this same router. GitHub Server checks 56/56 PASS and Client core TLS E2E PASS. **Still needed:** runtime AOI consumer, WorldReady/delta wire, production listener/admission, cross-map resync and two-client E2E.

## 2026-11-16 Login session cleanup and same-character reconnect

For authenticated TCP clients use `IdentityTlsAuthorization.RunAuthenticatedSocketAsync(...)`: it composes Account/CharacterChoose + Router attach + mandatory TLS gameplay + safe Router detach and authoritative World disconnect. Do **not** call bare `TlsWireConnection.RunServerAsync` with a real authenticated World session without wiring equivalent session cleanup. Only trusted World generations can release the matching character; a stale disconnect cannot remove a new login. On failure after registration but before the successful choose response is committed, the actor is rolled back. The new real TLS checks cover normal disconnect/relogin and invalid gameplay opcode/relogin. Actual login UI, character state save, production listener/rate limit, AOI/WorldReady/resync still remain pending.

## 2026-11-16 AOI output consumer

Use `WorldResultRouter.RunAsync(WorldHost.Results)` as the sole authoritative World-result reader and `WorldAoiPump.RunAsync()` as its sole ChangedBatch consumer. The pump retains pending AOI events until authenticated Gateway egress accepts them, and fails closed on overflow; don't bypass it with another all-entities broadcast. The test-only login fixture also invokes it and requires the Godot client to receive WorldReady over TLS. Github Server core 68/68 and Client core secure TLS PASS; production admission/supervision, ACK/resync, Enemy/Drop and two visual clients still incomplete.


## 2026-11-17 已提交地上物同步（MVP006 Ground AOI）

`WorldAoi` 現在與 Player/Monster 共用唯一 map-local bucket，從同一個觀察者 FIFO 向玩家可靠傳送 `GroundDropSpawn`（opcode 32、52 bytes）／`GroundDropDespawn`（opcode 33、32 bytes）；Client 只接收 Server 已落實 SQLite commit 的 DropId／MapId／ItemId／Quantity／XY。掉落本身位置不可變，所以閒置時不傳每 Tick Delta。Host 每次啟動從 SQLite 於 Tick 之外恢復未撿取記錄（上限及合成來源保護）；WorldReady、跨圖與 resync 重建當下可見列表。背景 Worker 以有界通知將成功提交的掉落送入同一個 AOI consumer，每 poll 最多處理8個已提交批次；此上限只控制背景同步工作，不影響30Hz ServerWorld 固定 Tick。Godot 現以輕量 Canvas 圓點代表掉落 UID，不會新增物品貼圖快取或每件地上物 Sprite Node；原版掉落素材尚待核實後替換呈現。C# / Godot 封包 golden、同圖/跨圖/Resync、20玩家/500掉落的確定性功能回歸已加入。

**玩家目前仍不能點地上物直接拾取。** 雖然 `SqliteIdentityStore.ClaimGroundDropAsync` 已有唯一 UID／原子背包結算／競態保護，Authenticated Gateway Pickup Wire、拾取結果、背景結算與由成功提交產生的 AOI RemoveGroundDrop 尚未串接；本輪 AOI 測試的手動 RemoveGroundDrop 不是正式玩法。重啟前尚未完成 SQLite commit 的死亡事件存在原先已註明的 crash gap。20/500 是功能數量 Gate，不是 CPU/記憶體/P95/P99 壓測。


## 2026-11-17 玩家權威拾取（MVP006 Pickup partial）

Godot 左鍵優先命中 AOI 的可見地上物標記（目前採 14 world px 近距點選），ClientSession 只傳 `GroundPickup` V1 **opcode 34／28 bytes**：`MapEpoch:int64, PickupSequence:uint32, DropId:int64`。TLS Server Gateway 依已認證 CharacterId/ConnectionEpoch 建 TrustedGroundPickup，不接受玩家自行指定座標、角色、道具 ID 或物品數。每連線有界 async Worker 會從 ServerWorld 單一 owner 讀取可信位置及 MapEpoch，與 NPC 換圖處理透過 per-session semaphore 序列化；不可用的 World/世代拒絕，死亡角色拒絕。

既有 SQLite `ClaimGroundDropAsync` 檢查同圖與距離（目前 96 world px：**暫定值，原版尚未核實**）、同掉落 UID 只能一角色領取，且背包 `item_instances` 產生持久物品 UID 與 Claim 在**同一 Transaction**。結果經同一可靠 Gateway FIFO 回覆 `GroundPickupResult` V1 **opcode 35／45 bytes**；只有真正 `Granted` 才向既有 AOI pump 發已領取 UID 通知，對可見玩家送 GroundDropDespawn；不在拾取點擊時樂觀刪除。Server Checks 加入 C#/Godot 互通 golden、偽造 Server result opcode 拒絕、真擊殺→DB Drop→Gateway Pickup→SQLite ItemUid→AOI 消失完整合成環境驗收。Server CI 44085408907 PASS。

Client WorldState 拒絕不同 UID／未送出的拾取結果、只允許一筆等待中的拾取，對相同操作重送不重發物品；`received_item_grants` **僅供目前登入拾取回饋，並非可跨登入讀取的背包快照**。Godot `SessionPanel` 顯示最新 Server 授權物品 UID、ItemId 和數量，不冒充原版物品名稱。正式全背包需另接 Server InventorySnapshot/增量同步，並載入核實後的原版物品圖與名稱。

仍須驗證：原版 96px 拾取距離、Windows GPU/GUI 真地圖、20玩家搶同一 UID 的 TLS 壓測、硬中斷在 DB commit 與 AOI remove notification 間的 reconciliation；同 session NPC transfer 已序列化，但瞬間怪物攻擊致死與拾取跨 worker 的完全線性化仍需實際設計與回歸。


### SQLite 拾取跨重啟冪等世代（2026-11-17）

正式 Host **每次啟動都配置**一個 SQLite 持久 `loot_world_generations.generation_id`，即使沒有 `--loot-rules` 也會配置，以便處理重啟前已持久存在的未拾取物。登入驗證仍使用原 ServerWorld 的 Actor.ConnectionEpoch／MapEpoch；但在 `ClaimGroundDropAsync` 的**資料庫冪等鍵**中，`pickup_connection_epoch` 記錄 `(DurableWorldGeneration << 32) | Actor.ConnectionEpoch`。因此重啟導致 in-memory connection epoch、MapEpoch、Client PickupSequence 均從 1 重新開始，也不會誤判上次登入的 StaleSequence；舊登入 Session 以相同鍵重送仍取回原 item_uid。此 DB generation 僅在可信 Host 建立，**不是 client 可操控的封包欄位**。測試 `GroundLootChecks.RestartConnectionGeneration`。


## 2026-11-17 SQLite 持久背包登入同步（MVP006 Inventory baseline）

在登入與角色歸屬認證後，`IdentityTlsAuthorization.AuthorizeAndBindAsync` 經已授權 `character.CharacterId` 從**同一 SQLite identity/item_instances** 載入目前所有物品，成功選角回覆及初始 HP 狀態後、WorldReady/其他 AOI 封包開始前，以原有受 TLS 保護的連線傳出 `InventorySnapshot`（V1 opcode36）分批快照。每個封包包含 `CharacterId:int64`、`StartIndex:int32`、`Complete:byte`、`Count:uint16`、每件 `ItemUid:int64`、`ItemDefinitionId:int32`、`Quantity:int32`、`SourceDropId:int64`。固定 header23 bytes、每 item24 bytes，批量128，至多4095件；**登入時若數量超過4095，會拒絕此次背包同步，不顯示錯誤的「完整清單」**。即使沒有物品也必須送 23 bytes 的 complete 空包。原來的 `GroundPickupResult` 仍作為權威即時增量，依 item_uid 去重合併；重新登入則從 DB 全新讀取，與上次臨時收到的物品清單無關。

Godot 可開啟「背包（N 件）」檢視物品編號、數量、UID，目前為保護 UI 只列出前80件，避免同時生成大量 Item Sprite。**此階段不推定原版物品名稱或貼圖，也尚未實作裝備/使用/丟棄、分類、交易、背包容量規則及完整分頁。** 初次登入/重連、跨圖與 Pickup Result 的唯一 UID 維護已加入 Godot 測試及正式 TLS Server 整合驗收；持久背包的超大數量資源消耗還需額外性能量測。驗證：Server CI 44089132226 PASS、Godot CI 44088959344 PASS。

## 2026-11-17 回復道具使用：來源明確核實才開放

可於既有 Host serve 指令後追加 --recovery-rules C:\private\verified_recovery.json。未指定則所有 RecoveryUse 請求返回 NoRule，絕不從 Client item_catalog.json、名稱或 icon_id 猜測補血值。Operator 必須人工查核原版來源並明確配置以下 JSON（數值僅作格式示意，不代表已證實的原版規則）：

~~~json
{
  "kind": "verified_recovery_item_rules",
  "version": 1,
  "rules_verified": true,
  "gameplay_ready": true,
  "source_reference": "operator-audited-original-item-effects",
  "rules": [{"item_definition_id": 1001, "restore_hp": 140, "required_level": 1}]
}
~~~

Server 收到 opcode37/28B RecoveryUse，只接受 mapEpoch、單調 useSequence 與 ItemUid。原本 TLS 認證/World Snapshot 供應真正角色/ConnectionEpoch；本連線有界16筆 async worker 與 NPC/Respawn/Pickup 共用 gate，Combat Owner 先暫停/排空怪物傷害。SQLite schema v4 將背包 stack 扣1、持久 HP 回復、獨立 replay receipt 合為一筆 Transaction；最後一件消耗後 UID 從背包移除，但重送紀錄保留。成功結算後更新 Combat projection，送 opcode38/45B reliable result，再恢復怪物傷害；拒絕未核實/不持有/滿血/死亡/不足等級等狀況時不更動道具或 HP。

驗證 Server CI https://github.com/kevin840307/SGTest/actions/runs/44109415226 PASS：真 TLS 登入→回復兩次→重送→用盡堆疊→重登持久 HP/Inventory；queued Monster Damage→回復→再攻擊；Host 用明確核實 JSON 啟動、未核實 JSON 拒絕。Client CI https://github.com/kevin840307/SGTest/actions/runs/44107865203 PASS，涵蓋只扣一次、MapEpoch、按鈕及 HP 變化。原始 ITD 公式、Cooldown、Buff、MP/SP 和真 Windows UI 仍未驗證。
