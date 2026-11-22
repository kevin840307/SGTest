# SGTest — Clean-slate 測試與完工標準

**2026-11-22 AES-CBC Transport Gate（使用者指定替換）**：正式 `serve` 使用固定 Key/IV 的 AES-256-CBC/PKCS7 二進位外層 Record，Client/Server 共用唯一 `WireProtocol` V1。新驗證：Godot 4.7.2 ↔ C# Golden 與真 TCP Echo `AES_CBC_TRANSPORT_PROBE_PASS`、C# 正式 Host 自 CLI 建立帳號並經新 AES 路徑登入／選角／WorldReady／MoveReceipt `IDENTITY_ADMIN_AES_CHECKS PASS`、Server Host/Checks Release build 0 warning/error、listener-only 合成 Gate 9/9 PASS。執行 `dotnet run --project tools/tests/aes_transport_probe/AesTransportProbe.csproj -c Release`、`dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --identity-admin-only --aes`。尚缺真 Godot UI + 完整 AES 玩法回歸／多人壓力及正式 CI；歷史 TLS 實驗僅作 regression。CBC 固定 Key/IV 且無 MAC 造成無完整性驗證、重複模式洩漏，不能等同 TLS 保護，不可把此安全弱點標記 PASS。


2026-11-22 creation persistence gate: `CharacterCreationSettingsChecks` exercises all 35 valid allocations and invalid distributions, Unicode name width/four-character secret validation and redaction, atomic settings/look/wallet rollback, concurrent slot allocation, salted hashes, v24 backup/migration, reopen, disabled/foreign account rejection and concurrent durable lockout using an injected clock. `IdentityTlsAuthorizationChecks` also rejects protected characters selected through the legacy proof-free frame without creating a World actor. Release Host and Checks builds: zero warnings/errors. Full Server checks 286/286 PASS with `SGTEST_CHECKS_HOST_DLL` pointing to isolated `.cache/creation-settings-host/SGTest.Server.Host.dll`; Python converter/conventions 125/125 PASS. This gate does not verify forthcoming Wire/Client fields, source UI, creation hair choices or actual protected-character login; those remain NOT VERIFIED.

2026-11-22實際UI巡覽：先啟動獨立正式Host（private兩圖content、獨立identity、localhost測試憑證、account-registration enabled、合法Map15出生點），再執行 `Godot --path client --script res://tests/test_native_ui_walkthrough.gd -- --host 127.0.0.1 --port <TEST_PORT> --tls-name localhost --trusted-cert <CERT_PEM> --walk-dir <PRIVATE_REPORT_DIR>`。新建測試帳號及角色會持久寫入該測試DB，請勿指向正式帳號DB。使用正常Input事件而非pressed.emit或Session stub，原生畫面10張；四個分頁以滑鼠／鍵盤操作（窄面板末頁使用TabBar鍵盤導航），只測顯示與切換，不宣稱背包空白時的物品操作或未配置NPC交易。正式流程與資源回收PASS。`test_recovered2_login_reference.gd -- <READ_ONLY_LoginScreen.tscn> <PNG> static-login` 和既有 `test_recovered2_local_ui.gd -- <DIR> <PACK> profiles static-login` 可產生同黑底原參照／實作畫面；1152×620登入窗(436,150,280,320)逐像素RGB差異0。原Scene不執行原Script，私人影片／Theme／來源TSCN均不入Git。UI lifecycle／skins回歸PASS。完整創角髮型/配點/名稱/第二密碼、Recovered2世界HUD／社團／人物狀態及HK2切換仍未完成。

2026-11-22 B/G/A private GPU gate：`python client/tests/run_world_view.py <Godot> --profiles --pack .cache/character_profiles.pck --assets .cache/character_creation_assets --report reports/character_profiles_gpu.json`。八方向×NS/NR×三種族，共16張圖／48 poses，固定16 updates；verifier強制完整覆蓋集合，不以截圖數量單獨當PASS。原SPR180 frames及origin／crop、ACT frame、flip與六部件順序，超容差0、resources全0、engine error0 PASS。手動mutation frame／flip_h／part_slot各拒絕；既有雙圖12張與B人物20張回歸及Python123 PASS。只使用既有唯一Converter產出的私有22-asset PCK，沒有正式包替換。非全動畫幀、AA/NH/ND或多人同步／效能比較證據。

2026-11-22 shared appearance profile gate：`test_player_profiles.gd` 覆蓋 B/G/A 混合、髮型共享 metadata、固定走路／插值、各 profile AA/ND、換 race 中斷不支援 AA、失敗註冊原子性與未知外觀不 fallback；已加入 Client CI。Windows targeted tests 與 Python123 PASS。`python client/tests/run_world_view.py <Godot> --players --report reports/player_profiles_regression.json` 原 B GPU20張／91來源frame、超容差0、資源歸零 PASS。`Godot --path client --rendering-method gl_compatibility --script res://tests/test_recovered2_local_ui.gd -- <capture-dir> <private-22-asset-pack> profiles` 實際 B/G/A hair00/color0 預覽切換及9張畫面／scope釋放 PASS，來源 rules 是 fixture，不能推論全量原版合法集合或多人外觀。G/A 全方向逐像素及負載效能 NOT VERIFIED。

## MVP007-PVP-AUTHORITATIVE-COMBAT-CRIME-NAME-AOI — 玩家互打／罪名／名稱顏色（完成垂直切片）

2026-11-19：已完成 Map13 玩家普通攻擊的 authenticated Server authority 閉環。Client 僅送 TargetActorId；Server 綁定 Actor/Connection/Profile，核對 MapEpoch、PK map、存活、距離、Blocking 直線與 cooldown，傷害採來源驗證的 `max(1, Attack - Defense/5)`，並透過唯一 `PlayerCombatSettlement` 寫入 durable HP。受害者 `PvpDamage(80)` 與 Monster damage 共用 damage sequence，攻擊者 `PvpAttackResult(79)` 只接受 committed receipt，重送不得造成第二次傷害。

犯罪狀態依 Recovered 證據實作：普通白名被攻擊後攻擊者 Pink 30 秒，最後 3 秒閃爍；殺死普通白名後 Red；攻擊 Pink/Red 不額外升罪名。SQLite v17 保存 base Blue/White、Red 與 Pink deadline。可靠 `PlayerIdentity(81)` 在 WorldReady/Spawn 後傳送 character name、authenticated generation 與 PvP presentation state；Godot 對未 Spawn identity、generation conflict、same-revision conflict fail-closed/resync，Despawn/換圖清除。名稱由共用 WorldCanvas 批次繪製，不配置每玩家 Label Node；Pink 閃爍只屬 presentation，不回寫 gameplay。

真雙 Godot Client TLS Gate：Map13 兩位白名玩家，攻擊者 ATK60、受害者 DEF10/HP100；第一次 committed damage=58，受害者 HP42，觀察端收到 Pink identity；第二次 committed damage=42，受害者死亡，觀察端收到 Red identity；C# fixture 最終核對 SQLite HP0、2 hits/100 damage/1 death、攻擊者 Red、clean EOF 與無殘留 Actor/Session。Server Core [44538435399](https://github.com/kevin840307/SGTest/actions/runs/44538435399) PASS；Client Core [44539729904](https://github.com/kevin840307/SGTest/actions/runs/44539729904) **61/61 PASS**。

**未驗收/不得宣稱完成**：第三方旁觀者的 PvP attack/hit/death presentation、玩家目標技能、原版 Red redemption/罪名解除算法、完整持久 RBAC/離線管理、Party 原版 EXP/掉落/距離/人數/輔助規則、Guild/Faction/PvP map 完整例外、Windows 私有素材畫面與真機互動。Party 的 online membership/leader/disconnect/跨圖 Party Chat 已由 2026-11-20 authenticated Party slice 驗證，不屬此未驗收清單。

## MVP003-IN-GAME-CHARACTER-CREATE — 登入後創角／刷新列表／直接選角（完成切片）

2026-11-18：compact-binary V1 新增 TLS pre-gameplay `CharacterCreate(15)`／`CharacterCreateResult(16)`。Client 只提交 **slot + UTF-8 name**；不得提交 ActorId、ConnectionEpoch、MapId、X/Y、屬性或戰鬥數值。`IdentityTlsAuthorization` 在 Auth 成功後維持同一 TLS Session，允許空 CharacterList 進入 create/choose loop；成功使用唯一 `SqliteIdentityStore.CreateCharacterAsync` 持久化，再回 CreateResult + **重新查 DB 後的 authoritative CharacterList**。名稱／slot 衝突也刷新列表而不要求重新登入。新角色只有在 Host 顯式設定 `--character-spawn-map/x/y` 且 authoritative Blocking 驗證可走時才允許建立；未設定回 `Unavailable` 且 DB 0 mutation。

Godot `WireCodec → WorldState.STATE_CREATING_CHARACTER → ClientSession → SessionPanel` 已接通：選角頁可輸入名稱與0..7 slot，送出期間鎖定避免重複 create，成功列表刷新後自動選中新角色，可立即按「進入世界」。Server 真 loopback TLS E2E 驗證 **空帳號→建立 CreatedHero→重名衝突→列表仍唯一→同 socket 選新角色進 World**，另驗證無 server spawn policy 必須拒絕。Client Core [44318844930](https://github.com/kevin840307/SGTest/actions/runs/44318844930) PASS；Server 功能 Core [44320038624](https://github.com/kevin840307/SGTest/actions/runs/44320038624) **PASS total=157**，三個 Release build 0 error、checkpoint hard-kill recovery PASS。

**後續公開帳號註冊已完成**；尚未完成原版 Race/Career/外觀／能力值創角選項、原版確切出生點／出生規則與刪角。現有 `character-create` 管理 CLI 保留；遊戲內創角不取代部署 provision。MVP003 主項仍需其餘原版功能／私有真圖驗收後才能整體 [x]。

**CI 開發節奏**：一般 `Server core checks` 不再每次功能修改重跑 20P/300M/500D 密集／持續負載；重型場景保留在 `SGTest.Server.Checks --load-only` 與手動 `Server load checks` workflow。功能 Core 仍跑 Wire/TLS/Auth/SQLite/World/AOI/Combat、安全 fail-closed 及 hard-kill checkpoint。分離是為了避免無關壓測抖動阻擋功能開發，並未把可靠性錯誤改成忽略。

**狀態：測試規格。舊 Client/Server 的 PASS 不能沿用。**

## MVP001 — 雙地圖 Map15 + Map13、完整功能與多人壓測（來源資料部分完成，E2E 未完成）

**兩張真圖都需要正式 Godot 4.7.2 + .NET 8 C# Client/Server E2E**；原版／Recovered 只有證據作用，既有 Converter／Wire microbenchmark 不等於遊戲 PASS。

- **MVP-MAP15**：原版 Map15 地形／Scenery／Blocking／metadata、NPC／主城互動位置、正確原點／Y-sort／八方向素材、camera／解析度、唯一 Converter + external PCK，Godot 真圖顯示與 ResourceLoader／AssetStore Acquire/Release。
- **MVP-MAP13**：原版 Map13 地圖／Blocking 台地階梯、地物／人口／Enemy 出生位置／掉落資源、相機／渲染／碰撞、AssetStore、來源覆蓋與原版畫面一致性；與 Map15 使用同一地圖載入及資源路徑。
- **MVP-ACCOUNT/WALK**：帳號／多角色／創角選角／登入登出／重連、Server A*／路徑合法性、點地板／長按約 1 秒／點怪追逐與停止，正確八方向／移動／攻擊優先級及跨地圖 Spawn。
- **MVP-CROSSMAP**：**Map15 → Map13 → Map15 真實雙向傳點**；Server 權威驗證、MapEpoch／WorldReady、AOI 清除舊圖、在目標圖收到新 snapshot、角色位置、怪物／Drop 恢復、Texture/Node 釋放及新圖載入，至少兩真 Client 同時／錯開切圖、重登與斷線恢復，不可有舊實體殘影／異地封包污染。
- **MVP-AOI/MOTION**：至少兩個真 Client，Map15 主城與 Map13 的雙向 Spawn/Despawn、視野／解析度／camera、Player/Enemy/Pet/Mecha Facing、Move/Stop/Attack/Hit/Death/Skill、動作固定 ACT timing、不在視野外圍順移、可靠事件與有界佇列。
- **MVP-AI/COMBAT**：Map13 多種尺寸／行為 Enemy，可站立、獨立 NextDecisionTick、分散巡邏／追擊／脫戰，不集體同步；普攻、單體／範圍技能、Buff、MISS、Death／Respawn、掉落與拾取；Server 判定和多人觀看一致。
- **MVP-ITEM/ECONOMY**：Map15 NPC 商店／倉庫／銀行／修理、物品 UID／背包／強化／金錢、玩家交易／商店；Map13 地面 Drop/Pickup，含併發拾取、餘額／UID 原子性、重送／非法操作／斷線一致性。
- **MVP-SYSTEM**：Pet／Mecha 裝備卡片升級、技能效果與 CD、聊天／交易／玩家商店、PvP 原版地圖權限／名稱顏色／GM 指令，兩圖適用場景依原版規則測試，Server Authority 及錯誤／多人競態，好友暫緩。
- **MVP-WIRE/SECURITY**：ALT001 方案比較、Godot↔C# golden fixture、header/framing／replay／invalid input、Epoch/Sequence、bounded queue、慢 Client/backpressure、AFK／FPS 0/30/60 還能可靠同步。
- **MVP-RESOURCE/LOAD**：Map13 同圖逐步到 **20 連線玩家／300 Enemy／500 Drop**（至少兩個真 Godot Client 觀察動畫，Bot 明確標記）；Map15 多玩家互動、兩地圖同時活躍、往返切圖至少 10 次、1/2/4 開、AFK/Unfocus，稀疏／密集 AOI、AI 與 Protocol FPS 影響。量測 bytes/sec、Queue Age、World Tick/Frame P95/P99、Input latency、CPU、Process RSS、Texture/VRAM、GC、載入／釋放與可靠事件；不能將 Bot Server 負載等同所有真 Client 畫面 PASS。
- **MVP-GATE**：以上全部可重跑且正確性／功能完整度／資源 Gate PASS，Map15／Map13 雙向真實傳點必須完成，ALT001 選型有資料，文件／TODO 跟上後才准宣稱 MVP 完成。

**MVP 後的全圖／全種類／更大負載**：其他地圖與完整傳點網絡、所有原版 NPC/Enemy/裝備/技能、100 Player／1000 Enemy／5000 Drop、多開 8、Windows/Mac 完整發行；MVP 不能代替全量驗收。

## MVP008-SUSTAINED-DENSE-MOVE — 20 人持續移動＋300 怪 AI＋500 Drop（部分完成）

2026-11-18：在已通過的 dense 20P/300M/500D 真 TLS Snapshot Gate 上直接延長同一測試生命週期。所有20個獨立 SQLite 帳號完成 Auth/CharacterChoose/Inventory/WorldReady、收齊 19 peers＋300 Monster＋500 Drop 後先同步 barrier，再**每人連續16次**送正式 `MoveTarget`（總計320次），每次必須等自己的 `MoveReceipt` 才送下一次；移動期間持續解碼 `SelfState`、其他玩家 `EntityDelta`、`MonsterDelta`，任何新的 Spawn/Despawn/Drop 重播、錯 Actor/Monster ID、Router backpressure、World result overflow 或漏掉 MoveReceipt 都讓 Gate 失敗。

第一輪 sustained CI 暴露一個真 Server 容量問題：`WorldLimits.MaxPathRequestsPerTick=8`，20 Client 在同 Tick 同時移動時，只有前8個可做 A*，其餘會得到 `TickRouteBudgetExhausted`。這不是 AOI/Gateway 丟包，因此沒有把測試改成接受失敗；正式 World default 改為 **32**（仍 bounded；Monster AI 仍用獨立 `MaxPathQueriesPerStep`），符合 MVP 20 concurrent Player 的明確容量。另修正測試 barrier：任何單一 Client 提前 fault 會立即冒泡，不再被共享 50s timeout 隱藏。修改：`b1c0e5bf`（sustained Gate）、`85f63802`（可觀測 counters）、`57427ae4`（Player A* budget 8→32）、`0bb339ad`（fail-fast test barrier）。

[GitHub Server Core run 44308433676](https://github.com/kevin840307/SGTest/actions/runs/44308433676) **PASS**，`SERVER_WORLD_CHECKS PASS total=160`，Release builds 0 errors。`SERVER_AUTHENTICATED_SUSTAINED_20_TLS_PASS` 單輪輸出：

| 指標（Ubuntu CI / synthetic Map115 / 20 .NET TLS clients） | 實測 |
|---|---:|
| 初始 Peer / Monster / GroundDrop Spawn | 380 / 6,000 / 10,000 |
| 持續 MoveTarget→MoveReceipt | **320 / 320** |
| 持續階段 MonsterDelta / PlayerDelta | **3,082 / 5,915** |
| 累計 gameplay framed bytes | **1,822,636 B** |
| MoveReceipt latency P50 | **33.27 ms** |
| MoveReceipt latency P95 / P99 | **37.14 / 37.23 ms** |
| Router backpressure failures | **0** |
| Host skipped deadlines / result overflow | **0 / 0** |
| AOI frames queued / Listener reliable frames sent | 26,969 / 27,269 |
| Monster runtime ticks / changed snapshots | 74 / 850 |
| Host max TickWork | 4,154 µs |

**邊界**：這是短時間 burst/sustained movement Gate，不是數分鐘 soak；沒有玩家普攻/技能、怪物實際傷害落 DB、死亡/重生、500 次 SQLite Drop spawn/pickup，也沒有 Godot renderer、Windows GPU、WAN。`MoveReceipt` latency 是本機 TLS request→authoritative receipt，不是 Input-to-Render。4.154ms 是單寫者 World Tick 的當次 max，不含 AOI Pump/MonsterRuntime 獨立執行時間。Player path budget 32 是依 MVP 20 concurrent movement 的實測容量修正，不代表100人後仍適用；擴到100 Player 時需重新跑 ALT/負載 Gate。MVP008 整體仍 [ ]。

## MVP008-AUTHENTICATED-DENSE-20P-300M-500D — 完整認證＋密集同視野可靠 AOI Snapshot Gate（部分完成）

2026-11-18：擴充既有 `server/tests/SGTest.Server.Checks/TlsGatewayListenerChecks.cs` 的 `AuthenticatedTwentyClients`，新增 `mixedLoad:true,denseLoad:true`；原本純20玩家同視野與 20 分群稀疏300怪500Drop 兩種測試仍分別保留。單一 **Map115 synthetic open-grid (420×240 cells)**，20 位各具獨立 SQLite 帳號/角色的 .NET TCP/TLS Client，在同一座標 `(3200,2000)` 附近 Auth→CharacterChoose→InventorySnapshot→WorldReady ACK→MoveTarget/MoveReceipt。真 `TlsGatewayListener` + `IdentityTlsAuthorization` + 30Hz `ServerWorldHost` + `WorldResultRouter` + 單一 `MonsterWorldCoordinator`/`WorldAoiPump` + bounded `WorldAoi` 為唯一 Server 路徑，未修改 production Wire 或 Client、未增加第二套 AOI。

**所有 20 位同時接收相同 dense viewport**：300 筆 MonsterSourceIndex（25列×12行、間距24px）和 500 件 GroundDrop（25列×20行、間距16px），另有其他19位玩家；每個觀察者須解碼到**19 個唯一 EntitySpawn + 300 個唯一 MonsterSpawn + 500 個唯一 GroundDropSpawn**，且所有角色/Monster/Drop ID 必須合法、沒有重複，允許同一時刻的合法 MonsterDelta/PlayerDelta。單個 viewport 最多含819個外部實體，Test AOI `MaxVisiblePerObserver=1024`、`MaxPendingEventsPerObserver=4096`，保持既有有界容器；正式 Gateway 的 256-frame 可靠 queue、單一 Pump 與 TLS writer 不變，要求 20 Actor/Session/ListenerConnections 同時存活，結束後全部釋放，任何封包佇列溢出 fail-closed 使 CI FAIL。

首次 [GitHub Server Core run 44303958406](https://github.com/kevin840307/SGTest/actions/runs/44303958406) **PASS**，Release .NET 8 builds 0 errors，`SERVER_WORLD_CHECKS PASS total=159`（含全部舊回歸及 Hard-kill DB checkpoint）。該次 `SERVER_AUTHENTICATED_DENSE_20_TLS_PASS` 單輪數據：

| 指標（Ubuntu CI／SyntheticTest／20 .NET TLS Clients） | 實測 |
|---|---:|
| 同時已登入 TLS/World Actor | 20 / 20 |
| 每 Client 可見的玩家／Monster／Drop | 19／300／500 |
| 完成的玩家 EntitySpawn／MonsterSpawn／GroundDropSpawn（20 Client 合計） | **380／6,000／10,000** |
| PlayerDelta／MonsterDelta frames（讀取完整快照前） | **1,082／1,190** |
| MoveReceipt | **20／20** |
| Client 實際解碼 compact-binary gameplay bytes | **1,198,396 B** |
| 登入至完成密集 AOI/MoveReceipt P50 | 527.93 ms |
| 同項 P95／P99 | **528.20／562.17 ms** |
| Listener Admission rejections / Host skipped deadlines | **0／0** |
| 單寫者 Host Max TickWork | 68 µs |

**嚴格限制**：本次 500 件 Drop 是**合成 AOI 預載，不是 500 次 SQLite 掉落或多人搶撿**；300 隻 AI 參與每 Tick 的 Server 協調及即時狀態更新，但**不代表 20 人同時高頻輸入攻擊／技能、怪物死亡／重生／撿物、連續長時間混合操作**已通過。此測試只需所有 Client 收到完整快照＋一次合法移動回覆，然後清理；P95/P99 是單次環境的「連線→認證→選角→完整快照」耗時，非 Input-to-Render／長時間更新延遲／WAN RTT。Host MaxTickWork **不含**獨立 AOI Pump/MonsterRuntime 的 CPU 時間，Gameplay Frame/VRAM/Windows 真 PCK/RSS 與 1/2/4 開、AFK/Unfocus 仍未驗。密集 AOI 使用 Test AOI 配置上限，若有容量問題正式 Server 應先證明可靠性後再調整，不能透過丟 reliable frame 偽造效能。**僅此 Server dense snapshot Gate [x]；整體 MVP008、MVP009、Release 仍 [ ]。**

## MVP008-AUTHENTICATED-MIXED-20P-300M-500D — 真認證 + 混合負載（部分完成）

2026-11-18：擴充已驗證的 `TlsGatewayListenerChecks.AuthenticatedTwentyClients(root)` 為 `mixedLoad: true`，不新增一套正式通訊或 Server 路徑。合成 Map115 420×240 collision cells，20 位玩家在 5×4 遠距分群，每群 **1 名獨立 SQLite Auth/CharacterChoose 真 TLS 玩家、15 隻經 synthetic verified population 載入的權威 Monster、25 個 synthetic AOI boot GroundDrop**，共計 20／300／500。

透過同一個 `ServerWorldHost(PublishIdleTicks=true)`、唯一 `WorldResultRouter` + `MonsterWorldCoordinator` + `WorldAoiPump`、原版相容 binary Wire、正式 `TlsGatewayListener` 的 Auth／角色歸屬／Inventory／WorldReady ACK、MoveTarget/Receipt，測試每位玩家精確收到自己的 15 MonsterSpawn 和 25 GroundDropSpawn，拒絕任何其他分群 EntitySpawn/Delta、MonsterSpawn/Delta、GroundDropSpawn 或重複 UID；20 名登入者都收到移動 ACK，等全部測試 Client 讀取完成後再次確認同時 20 ActiveSessions/ActiveActors/ListenerConnections，最後每個全部回到 0。保留原本 20 同視野純玩家互看 19 peers 的測試，不改其行為。這項合成測試只做分群隔離，**不是同視野全 300 怪／500 Drop 同場顯示**。

驗證：[GitHub Server Core run 44301225147](https://github.com/kevin840307/SGTest/actions/runs/44301225147) **PASS**，全部 C# Release 編譯 0 error，`SERVER_WORLD_CHECKS PASS total=158`（含舊全部回歸），Hard-kill DB checkpoint recovery 也通過。實測輸出 `SERVER_AUTHENTICATED_MIXED_20_TLS_PASS`：

| 指標（Ubuntu CI／.NET 測試 Client／合成內容，單輪） | 實測 |
|---|---:|
| 身分認證的獨立帳號與同時 Session | 20／20 |
| 同場 synthetic authority Monster | 300 |
| 同場 AOI GroundDrop | 500 |
| TLS Client 收到本區 MonsterSpawn／GroundDropSpawn | 300／500 |
| 越區玩家可見／重複或外區 Monster、Drop | 0 |
| MoveReceipt | 20／20 |
| Listener Admission rejections／Host missed Tick Deadline | 0／0 |
| 網路串流還原後累計 binary frame bytes（不含 TLS 傳輸 overhead） | 63,696 B |
| 登入→收齊各自本區 40 實體 + MoveReceipt，P50 | 402.75 ms |
| 同項 P95／P99 | 568.99 ms／569.06 ms |
| World Host 單寫者 max Tick Work | 77 µs |

**邊界**：未啟動 Godot renderer、Windows GPU、私有 Map13/15 原版素材或完整帳號背包大量品項。500 GroundDrop 是 synthetic AOI 預載、**不是 500 次 SQLite 掉落／撿取／結算**；300 Monster AI 接在單一 Server Pump，但未以全員高頻攻擊／掉落瞬變測試長時間穩定性。77µs 的 Host Tick Work 不包含獨立 AOI Pump/MonsterRuntime 的計算時間。P95/P99 含本機 TCP/TLS 握手、SQLite 登入、選角、獨立本地 AOI 快照與 MoveReceipt，不是輸入到渲染延遲；20 客戶端是 .NET TLS 測試程式，不是 20 個 Godot 視窗。本分群稀疏負載與只有玩家的舊同視野測試是不同 trace；**另見上方 MVP008-AUTHENTICATED-DENSE-20P-300M-500D，現已額外證實密集同視野的真認證及完整 AOI 快照收取 PASS**，但未含長時間高頻戰鬥、資料庫動態掉落或 Godot 畫面。完整 MVP008 (1/2/4 Godot、Windows RAM/VRAM/GPU、AFK、真地圖、Server AI/AOI P95/P99/密集高頻戰鬥、資料庫持久化重放及 20 Godot/WAN) **仍 [ ]**。

## MVP008-AUTHENTICATED-20-TLS — 20 位真正認證的合成多人 Host Listener 測試

2026-11-18：`server/tests/SGTest.Server.Checks/TlsGatewayListenerChecks.cs` 新增完整 20 帳號／20 不同角色透過**正式** `TlsGatewayListener`、`IdentityTlsAuthorization`、SQLite 身分資料庫、World Host、Router、AOI Pump 的 loopback TLS 1.2/1.3 驗證。不同於前次的 pre-bound 20 Test Actor，本項每個 Client 都必須進行真 TLS 握手、帳密驗證、自己角色選角、本人 InventorySnapshot、Server 世代 WorldReady/WorldReadyAck、正常 MoveTarget/MoveReceipt，並收到 **19 名其他角色**的 AOI EntitySpawn；所有 20 位必須同時在線時做檢查，不能靠先離線再陸續上線的偽 20 人。所有 client 最終 clean disconnect，Listener/World/Router 活躍 Actors/Routes/Addresses 全回 0，沒有 admission 拒絕。沒有改 Wire、正式認證與世界模組邏輯，只延伸原先 Server Check。

首次 [GitHub Server CI 44298449563](https://github.com/kevin840307/SGTest/actions/runs/44298449563) **PASS**：Release .NET 8 三個 build 0 warning/0 error；`SERVER_WORLD_CHECKS PASS total=157`，另含既有 hard-kill position checkpoint recovery。測試結果：

| 指標（單次 Ubuntu CI，20 個 .NET TLS Client + SyntheticTest 4×4 地圖） | 結果 |
|---|---:|
| 獨立帳號／不同角色 | 20／20 |
| 同時存活且授權的 TLS Session/World Actors | 20／20 |
| Client 解碼的遠端 `EntitySpawn`（每人 19） | 380／380 |
| Server 授權 MoveReceipt（每人1） | 20／20 |
| Test Client 接收 framed compact-binary bytes | 56,748 B |
| 含帳密/TLS、選角、直到看到 19 peers 的延遲 P50 | 557.30ms |
| 同項延遲 P95／P99 | 588.58ms／689.58ms |
| Listener per-address/capacity 拒絕 | 0 |
| 斷線後 Listener/World/Router active sessions | 0 |

本段數據仍屬 **20 位正式認證玩家、但沒有怪物及掉落物的同視野場景**；另見上方 `MVP008-AUTHENTICATED-MIXED-20P-300M-500D` 在稀疏 20 分群下完成正式認證與怪／掉落同場測試。兩者不是同一負載 trace，不能混用 P95/P99；密集同視野 20 人＋300 怪＋500 Drop 的**完整 AOI snapshot 已另由上方新 Gate 驗證**；Godot 真畫面、多開/AFK、Windows 真素材、RSS/VRAM/Frame P95/P99、WAN RTT 與長時間可靠事件風暴仍 NOT VERIFIED。`login_to_full_AOI` 是每 Client 從 TCP connect 開始，直到收完 19 peers + 本人移動回覆，不是操作指令延遲或 Server Tick P99；單次 CI 無硬性效能門檻。

## MVP008-MIXED-TLS-RESYNC — 20 條真實 TLS 的 300 怪／500 Drop AOI 回補（部分完成）

2026-11-18：正式 `WorldAoiPumpChecks.TwentyPlayersThreeHundredMonsters` 在原有 20 Actor/300 Monster/500 GroundDrop 分區權威壓測全部通過後，啟動**20 條真正 loopback TCP/TLS 1.2/1.3 socket**，透過相同的 `TlsWireConnection.RunServerAsync`、`GatewayConnection`、`WorldResultRouter`、`WorldAoiPump` 與單一 30Hz `ServerWorldHost`。Fixture 內直接綁定既有 trusted Test Actor，故**不是 20 組帳密、完整登入／Host listener 壓測，也沒有 20 個 Godot 進程**。Client 在每條 TLS stream 發送真 `WorldReadyAck`（之前觀測到的 WorldReady tick），等待全部 ACK Gate 打開，再送 `WorldResyncRequest`；Server AOI 唯一 owner 重建 WorldReady/SelfState/本區 Spawn，Client 以 `SslStream.ReadExactlyAsync` 逐一解碼 compact-binary 封包，嚴格驗證每角色恰好 15 筆本地 MonsterSpawn 和 25 筆本地 GroundDropSpawn、無外區 UID、重複/錯包拒絕、20 個 socket 未中途退出、終止均為 TLS 1.2/1.3 的正常結束。

這條測試不另外提供第二套 gameplay/authority，也不修改正式 Wire。於一次新的 ubuntu-24.04 / .NET 8 Release GitHub [Server core run 44297192595](https://github.com/kevin840307/SGTest/actions/runs/44297192595) **PASS**（正式 Server Core + Host + LoginFixture Release build 0 warning/0 error；156/156 Server Checks；原有 checkpoint crash 測試 PASS）。本次輸出：

| 指標 | 單次 CI 實測 |
|---|---:|
| 同張 synthetic Map 玩家／怪／掉落 | 20／300／500 |
| 同時 TLS connection | 20 |
| 透過 TLS Client 實際解碼 frame 數 | 1,032 |
| 解碼 compact-binary payload，含各種 AOI Delta | 66,756 bytes |
| 每條 resync 至收到完整本地 baseline P50 | 38.16ms |
| 每條 resync 至收到完整本地 baseline P95 | 38.62ms |
| 每條 resync 至收到完整本地 baseline P99 | 38.88ms |
| 外區污染／重播 baseline UID | 0 |

延遲是從各 TLS Client 發出請求開始至讀完本區 AOI baseline 結束的同機時間，20 個請求按順序寫入但接收是並行。數值含 TCP/TLS、AOI 排程、發送及 client 接收，沒有 WAN 延遲、Godot 渲染／GPU／input-to-render、角色物件轉場、完整 `IdentityTlsAuthorization` 帳密／ServerListener Admission。Client stream 中允許同時混入已驗證的 MonsterDelta，不把它當成怪物 Spawn 重複。此次資源與時序為單次 CI，不能設為效能硬門檻，也無法推論 20 人密集同視野／300 怪攻擊／500 件 DB 持久結算時 P99。**MVP008 仍 [ ]**；下一階段需完全認證 20 真連線、混合密集負載、Windows 真 Godot/PCK GPU、多開及完整 CPU/RSS/VRAM＋Tick/Pump/Frame P95/P99。

## MVP008-MIXED-AOI-LOAD — 20 玩家／300 怪／500 掉落同場 Server 管線驗證（部分完成）

2026-11-18：沿用唯一 `server/tests/SGTest.Server.Checks/WorldAoiPumpChecks.cs` 的 20 Player / 300 verified synthetic Monster、30Hz `ServerWorldHost` / `WorldResultRouter` / `MonsterWorldCoordinator` / `WorldAoiPump` 測試，新增同 Map 500 筆 test-only `GroundDropRecord`，在 AOI Owner 啟動前 seed，沒有新 Server 實作或第二套 AOI。20 個 Player 分成 5×4 隔離群，每群 15 Monster + 25 Drop，AOI 可見限制 64、Observer 上限 64、每次每 Observer 最多送64 frame，確保權威單一源與可靠 FIFO。每個 Observer 驗證精確 15 本地 Monster + 25 本地 Drop，跨群的 UID/SourceIndex 不可進入自己的 reliable queue；後續 900ms AI 更新不能重發不變的地面物；統計 World Tick、AI per-step、佇列 frames/bytes、實際 compact binary output bytes。舊 20 viewer × 500 全部可見的獨立 AOI 測試仍保留，這項只驗證新的三種實體**同時存在**於分群負載。

首次 [GitHub Server core run 44295641203](https://github.com/kevin840307/SGTest/actions/runs/44295641203) **PASS**：`SERVER_WORLD_CHECKS PASS total=156`（包括全部原 Server regressions），修正前缺命名空間的第一輪 CI `44295504409` 曾編譯失敗，已以 `249ed773` 修正。當次輸出：

| 指標（單次 Ubuntu Runner、SyntheticTest） | 實測 |
|---|---:|
| 玩家／怪物／地上 Drop | 20／300／500 |
| 初始 AOI MonsterSpawn／GroundDropSpawn | 300／500；外區污染0 |
| 無更新 Drop 重播 | 0 |
| 後續約900ms MonsterDelta | 455 |
| 全部 Receiver 初始 encoded-frame bytes | 59,894 B |
| 觀測期 encoded-frame bytes | 35,490 B |
| 同一時刻單 Receiver reliable FIFO 最大 frames／bytes | 30／2,340 B |
| AI 每步最多處理怪物數 | 64 |
| Host 當次 ticks／最長 TickWork | 48／271µs |
| Host SkippedDeadlines／OutputOverflow | 0／0 |
| 混合觀測窗 wall | 904ms |

**量測邊界**：encoded-frame bytes 是已編碼的 Server FIFO payload，不包括 TLS／TCP、握手與 Godot client；各 Receiver 的 bootstrap bytes 是累積總量。`host_max_tick_us` 是單寫者 `ServerWorld.Tick` 及其輸出發布工作，**不包含**獨立執行的 `WorldAoiPump`／`MonsterRuntime`／SQLite 或 Client 顯示，因此不能當完整 Server Tick+AI 的 P95/P99 或宣布每 Tick 均低於 271µs。`host_ticks` 包含測試建立期間，不應以 48 ticks／904ms 直接推算 Hz。此 **2026-11-18 早期 Server AOI 直連測試**本身未做多輪統計、混合密集同視野、20 條真 TLS、真 Windows GPU、多開/AFK 或完整 300 怪/500 Drop 戰鬥持久結算；密集同視野＋真 TLS Snapshot 驗收結果應引用上方更新的獨立 Gate。這只是 MVP008 的 **spatial+safety mixed-load slice**，MVP008 / REB030 / RELEASE 仍 [ ]，需依 ALT001 在相同環境補實際 20 TLS client、真 Resource/Frame/Server owner P95/P99、網路 bytes/sec 與多輪負載。

## MVP008-PROC-BASELINE — 正式 Host + 兩個 Godot 主場景 CPU/RSS 程序基線（部分完成）

2026-11-18：新增 `client/tests/process_telemetry.py`，不用外部 Python 依賴，以 Linux `/proc` 或 Windows Process API 每 50ms 量測真正的 Host/Godot 子程序的 Peak RSS（OS resident high-water）、當前 RSS、user+kernel CPU seconds。正式 `run_online_main_gate.py` 支援 `--metrics-report`，沿用登入、TLS、AOI、怪物、Map15→Map13→Map15、資源釋放的 Host+兩個實際 `app/main` 場景流程。報告包含各程序取樣數、觀測存活時間、合成 PCK/測試來源 SHA、PASS/FAIL，不記錄憑證、密碼或私人檔案路徑。Windows `_console.exe` 必須找到並量測 sibling 真正的 Engine `.exe`，不能拿 wrapper 當遊戲 RSS。CI 每輪保存 14 天 Artifact，沒有任意設置 CI runner RSS 硬門檻。

可重跑命令（Godot 4.7.2、.NET 8、OpenSSL；不需私有原圖）：

```bash
python -m unittest client.tests.test_process_telemetry -v
python client/tests/run_online_main_gate.py /path/to/Godot_v4.7.2-stable_linux.x86_64 --timeout 70 --metrics-report reports/online_main_process_metrics.json
```

首次 [GitHub Client core run 44294339322](https://github.com/kevin840307/SGTest/actions/runs/44294339322) **PASS**：2 項 sampler unit tests PASS；正式 Host、兩個 Godot 主場景及既有 TLS/AOI/跨圖/技能回歸 PASS；`online-main-process-baseline` Artifact ID=14422168612 已成功上傳。Ubuntu 24.04 / Godot 4.7.2 Linux headless / 合成外部 PCK，觀察到：

| 實際子程序 | Peak RSS（獨立 OS 高水位） | CPU seconds | 成功取樣次數 |
|---|---:|---:|---:|
| 正式 .NET 8 Host（合成雙圖） | 92.43 MiB | 1.07 | 69 |
| Godot actor `app/main` | 129.30 MiB | 0.53 | 43 |
| Godot observer `app/main` | 129.52 MiB | 0.55 | 57 |

限制：三個 Peak RSS 是**各自不同生命週期的峰值**，不得相加當成同時整機 RSS；CPU seconds 不是 CPU 百分比，50ms 取樣無法保證捕捉每個短暫尖峰。這只是 Linux headless、合成素材、兩個角色的真程序量測基線，**不是 Windows 原版 UI/GPU、20 玩家／300 Enemy／500 Drop、AFK/Unfocus/1-2-4 多開、FPS P95/P99、OS dedicated VRAM、Server Tick P95/P99、網路吞吐或與 Recovered 公平比較**。MVP008 / MVP-RESOURCE-LOAD Gate 仍 [ ]，不能直接將 129 MiB 當成完整 Windows Client RAM。

## ALT001 — 多方案公平比較／決策 Gate

適用於正式 Wire、AOI、Monster AI／A*、同步補間、Renderer、Atlas/PCK、AssetStore、AFK／多開與持久化等有合理替代方案的重大開發決策。

1. **功能 Gate**：所有候選先通過同一組原版／Recovered 行為、權威判定、可靠事件、AOI／Spawn/Despawn、動畫／碰撞、安全與資源生命週期回歸，不正確者淘汰。
2. **對等負載**：現行 baseline（若存在）與 1～2 種合理候選使用同一素材、Map/Blocking、事件 Trace、負載分布（稀疏／密集）、解析度、VSync、Build、硬體與測試時長；納入正常／AFK、多開與重連／換圖等狀態。
3. **可重複測量**：先暖機，合理時至少 3～5 輪；統一記錄 P50/P95/P99/Max、變異、input-to-render、World Tick、Queue Age、bytes/sec、AOI fanout、CPU／alloc／GC、RSS／VRAM、載入／卸載／恢復與玩家畫面正確性。Microbenchmark 不可冒充真實 E2E。
4. **決策**：功能正確且資源／安全滿足需求者，選擇玩家體感和尾延遲較佳、總體成本可接受方案；差異不顯著則選簡單／好維護者；不准藉縮小 AOI、丟可靠封包、降低 Server Tick 或畫質換取假改善。
5. **交付**：在受影響的**既有文件**記錄候選與比較表、測試命令、數據、選擇與限制；必要 Probe 保留在現有 tools/tests。整合唯一正式實作、更新 TODO 並跑回歸後，才向下一個有依賴的任務前進。
6. **例外**：沒有實質替代方案的小修正只需基本回歸，不為滿足流程而浪費時間；無法公平測試時標記 NOT VERIFIED，不能宣稱勝出。

## Gate 0 — Converter（目前唯一正式素材產線；另有獨立測試 Probe）

- SPR/Data1 原始來源覆蓋、錯誤來源 fail-closed、ACT/SIF/DrawOrder/Origin/flip。
- Asset ID 唯一、不同 Enemy／Map scenery／Player parts **禁止跨素材共圖**。
- Map13 與 Map15 的原版 `world.json`／`terrain.png`／`tiles.bin`／`blocking.dat`／NPC 來源相容；**population v3、定向 Atlas／雙圖 PCK 資料載入已通過下列分項；權威 Server／真畫面仍未驗證**。
- **MAP-DATA001（2026-11-15 PASS）**：兩張原始地圖一鍵輸出；Map13 367 Object、Map15 1,848 Object（其中 15 筆缺少原始 Z，須標記）、兩圖 Tile Pixel Round-trip PASS；原版 Server Blocking.dat SHA 匹配，雙圖重跑及反向指定建置時 10/10 私有輸出檔案 SHA 不變。原始 Enemy source evidence 追加後現行 Python 共 57 項 PASS，真遊戲 E2E NOT VERIFIED。
- **MAP-ENEMY001（2026-11-15 source-only PASS）**：唯讀 Old Server 原版 Map13 78 Spawn／9 種、Map15 4 Spawn／1 種，與 Data1 解出的 10 種 ACT/SPR、5 動作、8 方向及 source SHA 一一對應。輸出 `population_evidence.json` 只保留原始值且 `stats_verified=false`，缺來源及未知 Enemy fail closed，雙圖反向重跑 SHA 2/2 一致；原 Builder 獨立編譯 10 頁／557 Frame PASS。**正式 catalog、完整 PCK、權威 Server AI／Combat／Spawn、Godot 真畫面 NOT VERIFIED**。
- Height-Dense P0、VRAM import、external PCK 已整合且有獨立 Smoke；**完整來源覆蓋已通過 ARCHIVE001；Windows Release 和 macOS 實機仍未驗證**。
- 在**沒有 client/project.godot 和 server/*.csproj**的 clean-slate 基線仍可執行純 Converter 測試。

~~~powershell
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

## Gate 1 — Protocol + World

- C#、GDScript 共享已版本化 fixtures，非法封包 reject，Epoch／revisions 不混用；格式由實測選型，**不預設 JSON 或 MessagePack**，須驗證封包頭拆包／黏包與 byte-for-byte fixture。
- Packet trace 可重播，相同輸入得相同狀態 hash。
- Server Single Writer 與 DB/socket I/O 隔離；阻塞外部儲存不影響 Map tick。

### 初步 Wire / AOI 性能 Probe（非 Gate 1 驗收）

~~~powershell
dotnet run -c Release --project tools/tests/wire_aoi_probe/WireAoiProbe.csproj
& "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --headless --path . --script tools/tests/wire_aoi_probe/verify_binary.gd
~~~

檢查 RESULT ... PASS 與 WIRE_GODOT_GOLDEN_PASS；使用人工資料，已驗證 Binary Golden Fixture／Spatial Grid 與 brute-force 等價、Hysteresis 和密集 AOI 反例。MessagePack/Protobuf 公平 codec 對比已於 2026-11-16 的 WIRE001-CODEC 補齊；**仍未**驗證正式 Socket/TLS、多機、真實網路延遲、正式 AOI/Map13 E2E。結果與限制記於 [Wire Contract](RUNTIME_PROTOCOL.md)。

**REB020-FRAMING-PROBE（2026-11-16，合成測試 PASS）**：沿用既有 Wire/AOI probe，32-byte Move fixture 的全部33拆包切點、100組seeded三封包黏包／拆包、5非法length、3非法version/opcode/payload-width、半包reset與合法後非法拒絕，.NET 8與Godot 4.7.2皆PASS。此 parser 仍是 test-only framing evidence，不等於正式 Socket parser。

**WIRE001-CODEC（2026-11-16，選型 PASS；Transport E2E 仍 NOT VERIFIED）**：同一 deterministic 4,000-event Move／Combat／Drop／Spawn trace 比較 compact binary、MessagePack 3.1.12、Google.Protobuf 3.36.2；三者 C# round-trip 與 Godot Move cross-runtime fixture 都 PASS。平均 framed bytes/event=35.40／28.61／29.39；.NET compact P50 encode/decode=0.080/0.118µs，MessagePack=0.273/0.382µs，Protobuf=0.230/0.287µs；Godot Move fixture compact=1.205/0.922µs、MessagePack test codec=2.135/3.506µs、Protobuf test codec=4.021/5.278µs。依 CPU／GC／Godot dependency／維護成本選 compact binary 為唯一正式 Runtime codec。完整數據與限制見 RUNTIME_PROTOCOL。

**GATEWAY001（2026-11-16，transport-independent Server boundary PASS）**：`GatewayConnectionChecks` 驗證 5 組：①Client Move 經 trusted ActorSnapshot 綁定 ActorId/ConnectionEpoch，封包不能偽造身份；②兩個黏包 frame 中第二個遭 World ingress backpressure 時完整保留，Resume 只投遞一次；③Client 傳 server-only MoveReceipt、壞 length、超固定 input buffer 都 fail closed；④reliable egress 以 frame/byte 雙上限 FIFO，不 DropOldest，跨 ConnectionEpoch/未更新 future MapEpoch 拒絕；⑤trusted transfer 只允許同 ConnectionEpoch 的 MapEpoch 單調前進，Close 釋放佇列且不再收送。World receipt 同時新增 ConnectionEpoch/MapEpoch 並驗證 stale command generations。GitHub Actions `Server core checks` 在 Ubuntu 24.04 / .NET 8.0.428 實跑：Server build PASS 0 warning/error、Host build PASS 0 warning/error、**36/36 synthetic checks PASS**。第一次 CI 抓到既有 `WorldHostChecks` 6 個 nullable warning；已用明確 null unwrap 修正，沒有降低 `TreatWarningsAsErrors`。真正 Account Auth/CharacterChoose、WorldReady/ACK/reconnect/20真連線仍 NOT VERIFIED。

**TLS001（2026-11-16，真 loopback TCP+TLS Server session PASS；production Auth/listener/Godot E2E NOT VERIFIED）**：GitHub Actions Ubuntu 24.04 / .NET 8 使用記憶體內 test-only self-signed cert 啟動真 `TcpListener/TcpClient/SslStream`。4項新增檢查：①TLS handshake 後 Auth/CharacterChoose callback 未完成前，即使 Client 已送 Move frame，gameplay callback 仍不可被呼叫；授權後同一 Move 由 trusted Actor/ConnectionEpoch 綁定並透過 TLS 回傳 MoveReceipt；②Auth callback 回 null 時 session 拒絕且 gameplay 0次；③直接送 plaintext compact-binary 時 TLS handshake fail、Auth 0次，沒有降級；④World ingress 前2次 backpressure、第三次成功時同一完整 Move 不丟失/不重複，且等待有最大時間。Server/Host build 0 warning/error，總檢查 **40/40 PASS**。這不是 production certificate、Account password、CharacterChoose wire、listener deployment 或 Godot Client TLS PASS。

## Gate 2 — 視覺、戰鬥與 AOI

- 地圖、Player/Equipment、Enemy、Scenery/Drop 共用 World Y-sort。
- 原版 ACT、Origin、Direction、flip、DrawOrder 的指定回歸測例 **0 筆不一致**；另列出未覆蓋的素材／動作，不把未測情境算 PASS。
- Attack、Hit、Death、Move 的 action priority 不混亂。
- 20/100 玩家與 300/1000 Enemy 真實 Spawn/Despawn、攻擊／死亡、換圖、掉落拾取。

## Gate 3 — 效能與生命週期

- 正常可選 FPS 不低於 30（測 30/45/60/90）；Unfocus／最小化／AFK 可減少或停止**畫面渲染**，但不能改變可靠 State、WorldReducer 或 Server Tick。
- Render P95/P99、CPU、RAM、Texture/VRAM、load latency 與 Model1 **同規格測試**。
- 30 次登入登出／換圖／AFK 與多開。記憶體沒有逐輪上升趨勢。
- 不同解析度／縮放與 Pixel/UV edge alias artifact 測試。

## Gate 4 — 發行

- Windows Release 可執行，external PCK 無需主 PCK 全量打包；Mac 上實測 ResourceLoader／壓縮 texture / .app 配置。
- 所有版本號／Checksum／manifest 一致，不能使用測試產物冒充正式 build。
- 所有未執行的 Gate 必須明確標示 NOT VERIFIED。

工作順序與完成狀態在根目錄 todo.txt，歷史測試與結果在 [research/INDEX](research/INDEX.md)。
## 新增的必要驗收案例

- **MAP-DATA002**：ObjectList root name+XY+child_count／children name+XY；count包含children，world v3保留parent_index，z=0。Map15=1833根+15子／5父建築，Map13=367根。舊「15筆缺Z」解讀已撤回，DIRECT位址與遷移見ASSET_CONVERTER。測試負／溢出count、截斷、編碼、尾端垃圾、非BLG父物件與錯誤巢狀count；合成trim/origin bounds及來源順序／絕對XY不變。原版特殊建築遮擋與真Renderer **NOT VERIFIED**。
- **PATH001**：Map13／台地階梯／障礙角 A*，單擊地板、點擊怪物繞障、追逐目標移動、斷線 correction，Client/Server 都不能穿牆；Blocking.dat 必須以原始資料驗證。
- **HOLD001**：核心 headless/ClientSession Gate 已驗證 1000ms latch、游標跟隨、24px/80ms repath、400ms blocked retry、權威 stop barrier、受擊/攻擊/死亡/transfer/reconnect 取消，且完整 TLS/formal main scene 回歸 PASS；仍需原版 10-frame autorun cursor runtime asset、UI/技能 cursor precedence、移速變更、AFK/Unfocus 與 Windows GUI 真輸入，故總項未關閉。
- **FPS001**：完整同一組 packet trace，Render=0／30／60／90、正常／Unfocus／最小化／AFK，Client State hash、Command sequence、可靠事件和 Server Tick 不變。刻意阻塞 Renderer 不得阻塞 Network。
- **AFK001**：切 AFK 後按資產引用清單驗證非必要世界節點／Texture 釋放，量測與正常模式相比的 Process RSS、Texture/VRAM、CPU/GPU 與恢復延遲（**先定基線，不憑空宣稱 95% 可卸載**）；世界內容繪製趨近 0，網路仍完整；退出 AFK 復原 AOI、動作與 WorldReady，不得有舊圖或事件重播。
- **MULTI001**：1、2、4、8 個 Client 同機混合正常與 AFK，測各程序 Peak RSS、CPU／GPU、帶寬、queue age、輸入到畫面 P95/P99 和斷線／恢復。比較前須包含同一玩家/怪物密度與圖形品質。
- **LOAD001**：20／100 玩家、300／1000 Enemy、500／5000 Drop、動作與技能高峰，Server World Tick/AOI/Send Queue 分別 profiling；幀率與網路完整性分開評估。
- **RESTART001**：30 次登入、傳點換圖、AFK 進出，記錄 RSS/Texture/Node/Cache owners 趨勢；Mac 與 Windows Release 外部 PCK 各自驗證。
- **QUEUE001**：可靠事件／命令有界 queue 過載、慢接收者、網路抖動與重連；驗證無 World Tick 阻塞、沒有無限 RAM 成長，缺序能拒絕或 resync，冪等交易／拾取不重複結算。
- **AOI001**：不同解析度、相機 zoom、多人同時登入與可見邊界，雙向 Spawn/Despawn、快照基準與新舊 MapEpoch 交錯；Server 對客戶端 viewport 要有限幅與限頻。
- **AOI002**：同地圖 20/100 人、300/1000 怪、500/5000 掉落，比較每玩家 AOI 過濾後 vs 全圖廣播基準的 bytes/sec、updates/sec、fanout、CPU／alloc、queue age 與視野邊界抖動；未變動實體不重送，可靠傷害／拾取結果不遺失，並驗證多解析度及 AFK 重訂閱。
- **WIRE001**：固定包頭／length-prefix + 緊湊二進位、MessagePack、Protobuf 或其他合理方案使用相同 golden fixture 比較 Godot ↔ C# 的每訊息 bytes、serialize/parse P95/P99、CPU／alloc／GC、端序、拆包黏包、版本、異常輸入、安全／維護成本；僅證據充分後定版。
- **WIRE002**：使用正式 AOI／combat／drop trace，測 high-frequency snapshots 的 coalescing/batching、transport head-of-line／backpressure、慢 Client、連線與跨圖恢復；不能只用單一微基準宣稱正式網路更快。
- **GAME001**：第二階段裝備強化、NPC 商店／銀行／修理、技能、寵物／機甲、交易／玩家商店、PvP 顏色／GM 權限逐功能建立可重跑合約、多人及持久化測例。

任何腳本未實際執行時記錄 **NOT VERIFIED**，不可把先前 isolated Monster Benchmark 當成完成上述 Gate。

## 共同效能指標與公平比較

新 Client/Server 對 Recovered Model1 必須在相同原版素材、地圖、
可見實體數、解析度、縮放、VSync 和 GPU 設定下，以各自完整 Renderer
比較，不能用簡化 Sprite 畫面推估完整遊戲 FPS。

每次測試記錄：input-to-render latency、frame P50/P95/P99/Max、
超過當次目標幀預算的長幀數（30 FPS 約 33.3ms、60 FPS 約 16.7ms、90 FPS 約 11.1ms）、CPU/GPU 開銷、draw calls、Process Peak RSS、
Godot Texture/Video memory（**不等同實體 dedicated VRAM**）、
載入與卸載時間、Server World Tick P95/P99、封包佇列 age、
AOI fanout、可靠事件延遲、動畫方向/受擊/死亡/位移錯誤。

歷史隔離基準（**不是新版本 PASS**）：12 種 Enemy × 2000 隻，
Recovered P95 6.576ms / RSS 219.8MiB，舊 SGTest 壓縮 Sprite
P95 5.602ms / RSS 178.7MiB；Map13 視覺替身混合測試
Recovered P95 7.470ms、舊 SGTest P95 6.900ms。
完整方法留於 [歷史效能研究](research/ASSET_RENDERER_EVIDENCE.md)。

選型先排除畫面錯誤、權威事件遺失、過量記憶體；
其餘以玩家可感知的流暢度和穩定 P95/P99 為先，
**不得透過降低 Network／Client reducer／Server Tick 換取假 FPS**。

## ARCHIVE001 — 完整私有素材重新驗證（2026-11-15 PASS）

正式 Builder 重新產出 7,890 groups／75,588 SPR／282,968 Frame／7,871 頁，逐幀像素／origin 比較及 sidecar 613 ACT／1,165 SIF／9 DrawOrder 成功；後續唯一 CLI verify 來源／metadata／頁面 SHA、幀 identity、PNG 幾何與 sidecar replay PASS，0 error。測試命令、index SHA 與限制見 [Converter](ASSET_CONVERTER.md#archive-索引一致性mvp001-archive-integrity)。主 checkout 58 項 Python tests PASS。Archive PASS 不包含所有動畫語意／壓縮邊緣／PCK／Renderer／Client/Server；其餘 Gate 保持 NOT VERIFIED。

## CATALOG001 — 雙圖定向發布（2026-11-15 PASS，僅資料）

完整 Archive audit 為前置，原版 Map13／Map15 真實參照發布 242 個獨立 Atlas／242 頁／2,269 Frame，兩次反向指定 map ID 的 517 個檔案 SHA 完全一致。完整 62 Python tests PASS；新回歸涵蓋 full export、選取 scope、未知 ID／空集合 fail closed、未選 Asset 損毀也不可繞過完整 audit、managed stale removal／人工修改保留與確定性。命令及資料範圍見 [Converter](ASSET_CONVERTER.md#雙圖定向-catalogmvp001-catalog-scope)。**未驗證 NPC timing／全部動畫語意、真圖 ResourceLoader、PCK、Renderer 與 Client/Server E2E**。

## RUNTIME-CONTENT001 — 雙圖內容契約（2026-11-15 PASS，非遊戲 E2E）

唯一 CLI `build --runtime-only --extracted-source .cache/asset_converter/source --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 13 --map 15` 真實來源輸出每圖 6 檔；Map13 3 NPC／78 Enemy Spawn，Map15 52 NPC／4 Enemy Spawn；反向 map 順序重跑 12 檔 SHA 全一致。全部 NPC Atlas 參照存在；population v3 拒絕 v2／猜測 stats、保留原始 Spawn 及 Flags 並明示 gameplay_ready=false。65 tests PASS；新增 synthetic map/NPC staging 失敗時不覆寫舊輸出、population 不採未核實 stats 的整合回歸。PCK ResourceLoader、NPC timing／互動、權威 World／A*／Combat、多人 E2E 仍 NOT VERIFIED。

## PCK001-REAL-MAPS — 空白 Godot 真圖資源（2026-11-15 PASS）

既有 smoke 工具支援 --assets client/assets --map 13 --map 15，沿用唯一 Builder，244/244 Texture ResourceLoader 尺寸 PASS（242 Atlas＋2 Terrain）；526 個封裝 raw file SHA 相符，242 BPTC／242 ASTC 變體匯入 PASS。原 synthetic Enemy＋lossless UI 2/2 回歸 PASS。實際空白 Godot verifier 收到錯誤的 Map13 Blocking expected SHA 時 exit 3，拒絕且無成功旗標。Python suite 67 PASS。可重跑命令／PCK SHA／本機報告見 ASSET_CONVERTER 的「真實雙圖 external PCK」。這不是實際遊戲渲染／VRAM 或 P0 邊緣驗證，也不是 Server Spawn／A*／Combat／多人 E2E／Release。

## ASSETSTORE001 — Client 資源 owner 基礎（2026-11-15 PASS，headless 範圍）

`python client/tests/run_asset_store.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --compare --rounds 3 --report reports/client_asset_store_engine_comparison.json`：正式 PCK、完整 palette、各候選 fresh process 三輪。選定 Texture job=1 三輪皆完成每輪 30 次 Map15 221／Map13 63 Asset 載入／釋放，無引擎 ERROR／RID leak；相同 trace 的 job=2 三輪失敗（不以腳本單獨 PASS 抵銷引擎錯誤）。共用 Texture instance／readonly、最後引用 weak ref、在途取消／過期 epoch、容量／RGBA budget／無效 metadata 均有驗證；每輪 final owner/assets/jobs/bytes=0。來源與 metrics 見 CLIENT_ARCHITECTURE，raw logs／sources SHA／實際 engine CPU／Peak RSS 由 runner 留在 reports/。Python Converter 67 tests PASS。

Store pump 僅是呈現資源交接；測試不代表 Network／WorldReducer FPS 解耦已通過。實際視窗／GPU／VRAM 釋放、memory slope、Renderer／動畫／AFK／多人 E2E 均 NOT VERIFIED。新 client/project.godot 僅供資源模組與測試，尚無 main scene。

## TERRAIN001 — 原生 GPU 地形（2026-11-16 PASS，地形範圍）

`python client/tests/run_terrain.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/terrain_decision.json`：三候選 fresh process 各三輪；同雙圖／PCK／Atlas、1280×720和1920×1080、90暖機＋240固定trace樣本、40遠距跳轉。16 viewport capture／輪候選，獨立CPU原始tile合成比對，144張全部0差異，native engine無error/leak；WH／raw u16、原版 signed-int16 +1 wrap（含 0xffff→PIC0）、重複／未知ID／尾端bytes／越界fixture PASS。共用Mesh builder優化後重測，依資源／準備／tail選正式 terrain.gd，兩Mesh僅在tests。詳CLIENT_ARCHITECTURE選型表。原生GPU圖、rawlog、source/PCK SHA均留reports/，不進Git。

### TERRAIN002 — 原版 PIC source-index 回歸（2026-11-16 PASS）

使用者回報 Map15 有地磚看似位置錯誤後，直接核對原版 `Paradise.exe`：`ReadTileArray/DrawTile` 將 MapBlock signed int16 +1，`CDDxTile::LoadTile/Convert` 在略過 PIC 8-byte header 後用該 ushort index 讀 32×32 RGB565。新增 `tools/tests/test_map_tiles.py` 以四張不同 RGB565 tile 驗證 raw `(0,1,0xffff,2)` 必須對應 PIC `(1,2,0,3)`，並要求 runtime `tile_source_rule=int16_plus_one_wrap`；舊 direct-index manifest 由 AssetStore/TerrainData fail closed。真資料差異掃描：Map15 230,400/230,400 格、Map13 230,393/230,400 格的 direct-index source tile 與原版規則不同。

實跑：Python Converter **82/82 PASS**；私有 Map13/15 `build --runtime-only` PASS（Map13 866 raw ID、Map15 976、pixel_round_trip=true）；external PCK smoke PASS＝256 textures／508 codec extents，SHA256 `5ecee8d59fcde47554766b01c9c752b0c43784ef9ec1dafc6cf6ff2c0638c1ac`；正式 Godot terrain draw 16 capture 全0 pixel mismatch；World-view 12 capture／38 original SPR frame、max pixels over tolerance=0、final AssetStore counts全0；World Canvas一輪無 engine/validation error。報告在 ignored `reports/map15_tile_fix_*`，不進 Git。此修正不改 row-major XY placement／Renderer algorithm，因此不重做三候選效能選型；角色、多人與 Server E2E 仍 NOT VERIFIED。

Python Converter 67 tests PASS。這不代表地物／角色共同Y-sort／pivot、NPC timing／Animation、Server碰撞／A*、角色／多人畫面、AFK資源回基線或完整MVP002／MVP E2E通過；GPU engine counters 不直接當 OS VRAM 驗收。

### 2026-11-16 world v3階層回歸

Converter 68 tests PASS；雙圖runtime-only PASS；新PCK 242 BPTC／242 ASTC匯入PASS，SHA256 `5ae7608c8b756983efc186d2e162c9dc29bbb36c362bf03f97602e42cd1a74a6`。AssetStore 30次palette循環／3競態情境PASS；正式terrain draw同雙圖720p/1080p的16張GPU capture與CPU合成0像素差異PASS。首輪因TerrainData仍檢查v2而FAIL，修正及新增v2拒絕案例後重跑PASS；最終私有報告 `reports/scenery_hierarchy_terrain_final.json`。此輪未重做三候選效能比較，因只修正資料schema／階層且未改繪製策略，不產生新的效能勝出結論。CI、建築前緣遮擋、室內外／動畫與多人E2E仍NOT VERIFIED。

### 2026-11-16 景物metadata與alternate回歸

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 72 PASS。新增synthetic SIF舊16／17 byte、mode保存、BLG四strings／兩點集／front clamp、無效count／NUL／截斷、絕對runtime路徑、SIF與SPR衝突、definition共用及兩appearance覆蓋；runtime checker對缺alternate Atlas拒絕。來源probe確認兩圖所有definition source hash／manifest clip／Atlas page hash，四個map檔重跑byte完全一致，`reports/scenery_metadata_validation.json`。

同一CLI export-assets --map 13 --map 15完整Archive audit PASS，再runtime-only雙圖PASS（10.16秒）。runtime_pack PASS：254 BPTC／254 ASTC，PCK SHA見ASSET_CONVERTER。原命令 `run_asset_store.py --report reports/scenery_metadata_asset_store.json` 30次雙圖palette循環PASS（Map15=233含新alternate／Map13=63；final counts全0），`--script tests/test_asset_store_epochs.gd --report reports/scenery_metadata_epochs.json` 三競態PASS；`run_terrain.py --rounds 1 --mode draw --report reports/scenery_metadata_terrain.json` 16張GPU capture獨立CPU像素比對0差異、無engine error/leak。這次只回歸選定draw策略，未重做三候選benchmark。

首輪新增SIF測試曾錯把17-byte tail少1 byte當必定非法（實際可等同合法16-byte舊格式），FAIL後改為無歧義截斷並記錄hash完整性限制；不把錯誤fixture當產品錯誤或PASS。CI、原版建築前緣遮擋／同Y／室內外fade／動作時間、Server／多人E2E均NOT VERIFIED。

### 2026-11-16 World靜態景物／Actor前緣切片

- Python `python -m unittest discover -s tools/tests -p "test_*.py" -q`：75 PASS。原版對稱斜前緣2916點邊界chart、同Y穩定、三組root／子物件分離、互相要求交換的建築與1025輪上限；native同一chart、Actor插入與空白frame／重複root拒絕。world_order_validation.json確認雙圖順序可重算，runtime-only及新版PCK PASS。
- `python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/world_render_final.json`：集中Canvas與重用Sprite2D各fresh-process三輪，兩圖／720p與1080p、同camera trace及明確inside／frame快照。每map每round 240有效樣本／80跳轉；合計144 captures排序／座標／UV／texture identity及CPU合成PASS，110原始SPR frame origin與未壓縮Atlas crop逐byte一致。兩候選engine error/leak=0、Store final counts=0、Texture counter回同viewport baseline；source SHA前後未變。
- GPU oracle只允許nearest精確texel邊界1e-5的取樣上下界及2色階rounding，實際所有capture超界最大1、pixels_over_tolerance=0。**不能寫為原始圖與GPU RGB零差異**。錯誤命令順序與destination+1拒絕，整圖位移1px有789,194 pixel超界；私有world_render_negative_controls.json。初期型別／空白幀／未重現block-aligned底層尺寸與sampler tie的pilot FAIL，不列PASS。
- ALT001選Canvas：準備P99 Map15=1.223–1.460ms／Map13=0.541–0.626ms；Sprite=1.478–1.791／0.688–0.793。Frame P99與RSS無穩定全面勝負，選較少節點／較簡單Canvas，不宣稱所有硬體或動態怪物最佳。完整表、first-view／CPU／RSS／engine memory與限制見CLIENT_ARCHITECTURE。此輪network不適用，OS dedicated VRAM NOT VERIFIED。

尚存Gate：動畫／NPC timing與事件、出入口fade cadence／自身ghost、P0（含Godot非4倍數Atlas頁面的匯入取樣品質）、逐視野預取ALT、多開／Render0／資源斜率、A*／Server／多人E2E／Mac／Release仍NOT VERIFIED。MVP002-SCENERY-CONTRACT維持未完成。接手可用`--mode canvas --rounds 1`快速重跑正式路徑；全量三輪命令如上。

### 2026-11-16 景物預設時序／fade回歸

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 79 PASS。新增ACT字面action綁定／缺key／duplicate／index越界／非零起幀／長hold／raw0保存、scoped publish嵌入與缺ACT不猜FPS；現行唯一CLI export-assets完整Archive audit再PASS（372.769秒），8 normalized_scenery_act／10 Enemy／7 Player／229 source-clips-only，254 Asset不變。兩個HK2 binary重新SHA與既有timing audit完全相同；這是版本核對，並非啟動原版動態視覺驗證。

`python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --timing --report reports/scenery_time_verified.json`：正式Canvas與重用Sprite候選各1次Native PASS，84 captures／候選（雙圖／兩解析度／3相機／7組inside與elapsed狀態），總168，source SHA前後一致，127原始SPR幀嚴格origin與未壓縮Atlas crop驗證，命令順序／alpha／frame／UV／texture identity及GPU合成通過。仍採既有1e-5 texel邊界與2色階rounding規則；新增modulate只對source alpha取floor/ceil的1色階界以涵蓋GPU浮點alpha，實際max_outside_sampler_envelope=2、pixels_over_tolerance=0，不宣稱原圖RGB零差異。

Native fixture包含逐fixed-update counter對照2000步、30/60/120與AFK不取樣／巨幅時間跳轉、nonloop最後幀／completion、min1 update／fractional錯資料／不支援speed profile拒絕、per-placement phase與相同Atlas的BLG／static角色隔離。source資料證實61個多幀景物實例皆為static root。FISH01實測updates0/8→frame1，9→2，81/560→空白frame10，561→1。fade0/1/8/15/16、進／出target重設、children只隨原版分支顯示。錯alpha240改255立即被拒絕，reports/scenery_time_negative_controls.json。

兩候選退出Store counts全0、texture counter回13,373,731 bytes、engine error/leak=0；峰值Process RSS約222.35／221.60MiB（包含更多capture／readback，非steady-state改善證據）。`run_world.py --mode canvas --report reports/scenery_time_endpoints_verified.json` 原24端點回歸PASS；`run_asset_store.py --script tests/test_asset_store_epochs.gd --report reports/scenery_time_epochs.json` 三競態情境PASS。PCK SHA與來源profile見ASSET_CONVERTER；此輪不重新選Renderer、不將含PNG的wall time當Frame效能。CI NOT VERIFIED。

尚存：入口與inside事件phase／版本去重、自身ghost、visible-only原版phase政策、ACT token callback／dynamic speed、NPC／Player完整Animator、P0品質／非4倍數壓縮頁面、視野預取／AFK實機、多開／Server／多人E2E。時間取樣是純呈現函式，沒有把render呼叫當權威fixed tick。

### 2026-11-16 建築focus狀態／室內Actor分流

`python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --focus --report reports/building_focus_release.json`：Canvas與重用Sprite2D各1次Native PASS，兩圖／720p與1080p／3組進出路徑（含Map15 Court root486及child487）／7個fixed時點，總168 captures、105個source SPR幀。每張先用Python獨立u/v投影矩形重播focus trace，核對selected／counter／epoch／update，再比對所有commands及GPU composite。相機中心隨focus走訪、同畫面fixture_actor與outside_actor分走室內額外清單／main；不是兩個真Client或多人網路驗收。source SHA前後不變，engine error/leak=0，Store counts全0，Texture counter均回13,373,731 bytes；同既有sampler envelope＋alpha量化規則，max error=2，不稱原圖RGB零差異。PCK未改，沿用02d05d9a…6cbb9d4。

Native fixture：2501點half-open diamond邊界（61×41）、Manhattan1000/1001、兩組非空門檻（點座標刻意放遠證明未誤用）、同區不重啟counter、A→B、重疊last-wins、不可見exit/resume、Snapshot獨立、失敗configure atomic、clear tombstone／新epoch／duplicate／stale／gap／倒序／未知candidate拒絕；插值Actor±0.25保留連續幾何。build_focus拒絕舊epoch／錯expected update。`test_building_focus.gd` headless另做400次fixed update、30/60/120/0呈現採樣，完整trace一致（Render0仍有assertion用snapshot，不是實機AFK／network測試），source hash紀錄reports/building_focus_sampling.json。

負向controls（reports/building_focus_evidence.json）：錯selected／counter在state replay即拒絕；省略selected room的第二次child draw，35→34 commands而拒絕；Actor移到地板之前的錯誤順序也拒絕。兩候選Peak RSS約225.11／221.61MiB，含capture/readback，不宣稱perf提升或OS dedicated VRAM回基線。正式仍用既有Canvas選型。

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 79 PASS；`run_world.py --mode canvas --report reports/building_focus_legacy.json` 原24端點回歸PASS；AssetStore epochs三情境PASS（reports/building_focus_epochs.json）。初輪focus測試舊手動frame1覆蓋ACT frame輸入，已排除並重跑；只以building_focus_release作完整新狀態／室內分流證據。CI NOT VERIFIED。

未驗證：HK2入口函式逐指令等版／實際舊Client動態畫面、真正相機controller／Player／傳點、odd viewport／zoom、正式Wire版本／Server碰撞與權限／多人E2E；目前重現的是可定位reference的呈現規則。P0／視野預取／完整World lifecycle與實機AFK等Gate不因此完成。

### 2026-11-16 Runtime Atlas block extent回歸

MVP002-ATLAS-BLOCK-EXTENT：相同254素材／2281幀的原P0、右下block4 P0、幀邊extrude1＋block4頁codec比較完成，logical／physical不一致239→0；P0與block4壓縮base-level大小相同，合成MAE9.04731→1.40433。extrude1無明顯整體改善、大小+2.14%，選block4 P0。詳細表與來源機制見ASSET_ARCHITECTURE；比較工具的PASS不是全素材codec畫質核准。原始資料與readback PNG僅存private reports/.cache。

- `python -m unittest discover -s tools/tests -p "test_*.py" -q`：81 PASS，含1×1／51×102／已對齊／4095邊界、原texel（含alpha0的RGB）／透明擴展／冪等、Archive追溯、scope重跑與錯dimension／偽block4拒絕。
- 唯一CLI完整Archive audit＋export PASS（364.554秒）；獨立驗證254頁、239頁補邊、2281幀rect／size／origin／trim／source全部保持，PNG來源區域逐byte相同、擴展透明。Archive index SHA仍bd6b8a63…68ea55；reports/atlas_block_publication_validation.json。RGBA保守預算+740,816 bytes，不能當作GPU增量。
- `smoke_runtime_pack.py Godot --assets client/assets --map 13 --map 15 --output .cache/client-release/assets/assets.pck --report reports/atlas_block_pck_final.json`：550 raw SHA、256 Texture載入、254 BPTC＋254 ASTC的508個stored codec extent PASS。後者以CompressedTexture2D.load／get_image核對尺寸，**不是ASTC／Mac GPU畫質驗收**。完整命令見ASSET_CONVERTER；新PCK SHA8423f141880ab2c3dae673be7070b11fa1629614c25e516cd9e346bbec735f44，86,456,568 bytes，兩次pack同SHA。
- 負向合成51×102 logical／52×104 physical在正式PCK verifier exit5拒絕，無成功旗標（atlas_block_physical_rejection.json）。額外ASTC probe初次ResourceLoader.load內部ctex失敗並timeout；修正用CompressedTexture2D.load後才成功，不能使用初次結果當PASS。
- `run_world.py Godot --focus --report reports/atlas_block_world.json`：兩候選168 GPU captures、105原始SPR幀、source SHA不變、engine error/leak=0；原sampler envelope＋2色階界不變、實際max2、超界pixel=0。每圖釋放Texture回13,373,731 bytes、Store全0。此輪只回歸、不重選Renderer或宣稱Frame／RSS效能提升。
- `run_asset_store.py Godot --report reports/atlas_block_asset_store.json`：30次雙圖完整palette循環PASS、final全0、peak預算95,818,176 bytes。git diff --check PASS，CI NOT VERIFIED。

尚存：細節codec最大色差247，花草／蝴蝶／EN064A等逐素材畫質政策、linear／zoom、Mac／Release、可操作相機／Player、Server／多人E2E仍NOT VERIFIED；MVP002與完整素材品質Gate維持未完成。原始source texel無損與GPU有損codec不可混成同一個零差異宣稱。


### MVP002-CAMERA-ENTRY（2026-11-16，檢視器驗證 PASS，完整 Client NOT VERIFIED）

`python client/tests/run_world_view.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --report reports/world_view_native.json` 在原版私有雙圖 PCK 實跑：Godot 4.7.2 OpenGL3／RTX3050，12 張 Map15/13、1280×720 與 1001×701 × 三focus擷取；CPU 獨立驗證原始 SPR frame、rect／origin／trim、WorldDrawList 順序、UV 與 compressed nearest sampler，38 distinct source frame，0 over-tolerance pixel、max outside envelope=1。相機 world↔screen、奇數尺寸／clamp、epoch拒絕、連續小數移動、快速切圖／UI按鈕／中鍵／失焦與 Store drain 皆 PASS；final assets/scopes/jobs/rgba_bytes=0，source files SHA 未變。單進程 wall5.75s／CPU5.219s／PeakRSS226,193,408 bytes，**非正式遊戲FPS／效能結論**。另外原先 Canvas Renderer 24 張 GPU 回歸 PASS、81 Python Tests PASS。PCK、報告與影像只留私人本機。

僅證明雙圖 Viewer；完整 WorldState、人物操作／動畫、Server Authority、網路／AOI、AFK、壓力／多開、Windows/Mac Release 仍 NOT VERIFIED；MVP002 和 MVP 整體不能因本項通過標記 PASS。

### REB021-BLOCKING-NAV（2026-11-16，Server Collision 與 A* 模組 PASS，Server E2E NOT VERIFIED）

`dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj`：合成 9 組，含嚴格 v3 SHA/geometry、來源被修改/截斷/增加bytes/舊版拒絕、非有限與邊界世界座標、raw flags 0/6/7/3 fail-closed、可走及斜角封閉、隔牆繞門與全封閉、A* 每次 expanded budget。加上 `-- --assets "C:\SGO\SRC\SGTest\client\assets"` 核實兩張原始資料，**10 組 PASS**；Map13／Map15 均 960×960、源檔 SHA-256 與 v3 world.json 一致，雙向原版 NPC 靜態傳點目標所在 cell 可走但**不是實際傳點測試**。合成到真圖的 128 seeded nearby route targets／圖，實際可走目的地77／119，所有可走查詢 A* PASS、無越過1500展開預算，兩圖 query P95 59.5／26.0µs，managed bytes/request 約10.2／10.9KB；**隔離 micro profile，不是 30Hz World Tick、網路同步或高密度 Monster 測試**。原圖／PCK 仍在 ignored private paths。來源樣本的 raw flag 0／6／7 在正式 Server 先以 0 可走；3 特殊權限仍 NOT VERIFIED。

World Tick、Server-authoritative MapEpoch、MovementSequence、A* path request 排隊／重試／AI負載、斷線／權限、真 Client/Server 移動與跨圖驗收仍 NOT VERIFIED；本子項只完成準確、失敗封閉的第一段碰撞導覽核心。

### REB021-WORLD-CORE（2026-11-16，手動 World Reducer PASS，正式 Server NOT VERIFIED）

用 `dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj` 可無私有素材重跑 21/21 合成 checks；`-- --assets "C:\SGO\SRC\SGTest\client\assets"` 加測真原版兩張Blocking和可信 MapEpoch transfer trace，共 **24/24 PASS**。本輪 `ServerWorld` 測試覆蓋：bounded 32-slot MPSC（256個 parallel 嘗試僅32 accepted）、每Tick命令／路徑查詢上限、wrong owner thread 異常、ConnectionEpoch／MapEpoch 應用和 tombstone、新登入與 stale disconnect、MovementSequence 0/舊值拒絕、非法目標消耗序號但不竄改已授權移動、合法 Stop 位置不變仍發 EntityRevision 變化、A* 分段移動與不穿角／不穿牆、budget fail closed、相同 input trace 在不同 presentation read 採樣時 StateHash 完全一致。原 Map13 基於**20個模擬 Actor（非連線）**一次投遞 20 Move，MaxPathRequestsPerTick=6 下 6 Accepted／14 TickRouteBudgetExhausted，未處理的 Actor 不移動；20個 Actor 位置均通過原版 Blocking 验證。Map13→Map15→Map13 trusted transfer 使用原版 static portal 目的地，只驗證 epoch 與權威位置，不代表 NPC 權限或 Client WorldReady／雙向真實傳點。

保留現有 8 pixel/手動Tick 設定為暫時測試參數，**不是原版走路速度或真正30Hz Timer**。未測正式 Socket/身份、AOI、Map Tick P95/P99、慢接收者／可靠ACK、MapEpoch snapshot、20真玩家、300 Enemy/500 Drop/AFK/多開；此模組不得用來勾選 MVP003 或 REB021 主項。測試命令／限制詳 `server/README.md`。

### REB021-HOST-CADENCE（2026-11-16，30Hz 離線 Host PASS；網路與多玩家仍 NOT VERIFIED）

`dotnet build -c Release server/host/SGTest.Server.Host/SGTest.Server.Host.csproj` 編譯 PASS、0 warning/error。`dotnet run -c Release --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj` 合成27/27 PASS（舊21組 + 新6組），`-- --assets "C:\SGO\SRC\SGTest\client\assets"` 私有原版Map13+Map15資料再加4組＝31/31 PASS；新增不可依賴壁鐘的 FixedTickCadence 過期時鐘跳過測試、30Hz專屬 Thread 真 Move/Stop/拒絕、可信跨圖世代與舊封包拒絕、異常停止與拒絕重啟、容量1的慢消費者輸出佇列 fail-closed 不丟已發布回覆、可信控制過載立即拒絕／關閉時所有受理Task完成。原版 Map13+15 的真 Host 在一個獨立 Thread 上讀同SHA碰撞資料並測可信轉圖，非遊戲 NPC 權限測試。

`dotnet run -c Release --project server/host/SGTest.Server.Host/SGTest.Server.Host.csproj -- --content "C:\SGO\SRC\SGTest\client\assets" --seconds 5` 原圖真正計時実測：wall 5020.14ms、151ticks、30.079Hz、Skipped0、MaxTickWork1219µs、Published0、OutputOverflow0、Actors0、Pending0，**無 socket、無 AI、無人員，沒有 Tick P95/P99 或遊戲延遲保證**。新Host可執行，但不等於完整的可登入 Server；禁止用此案例代表20個真 Client、300 Enemy、500 Drop 或 AFK 功能。正式每玩家AOI／ReliableQueue／ACK／Snapshot、傳輸加密／授權、AI／DB 與跨圖 WorldReady 仍待驗收。

**IDENTITY001 (2026-11-16, SQLite/Argon2id core PASS; wire login NOT VERIFIED):** Release checks create disposable real SQLite databases and verify: (1) Argon2id random-salt PHC roundtrip, wrong-password and hostile-cost rejection; (2) case-stable account uniqueness/authentication and absence of plaintext password bytes in DB/WAL files; (3) two Character slots, Unicode NFC name collision, server-owned spawn values and persistence after reopen; (4) concurrent same-slot creation produces exactly one winner; (5) disabled accounts return `Disabled` only after the correct password, while a wrong password stays `InvalidCredentials`; (6) unknown schema version fails closed. Missing accounts execute a dummy Argon2id verify. Together with existing Wire/Gateway/TLS/World/Host checks, local Release total is **46/46 PASS**. Server and Host Release builds are 0 warning/error. Auth/CharacterChoose wire messages, login rate limiting/session token policy, production listener/certificate and Godot E2E remain NOT VERIFIED.

**IDENTITY-BACKUP001 (2026-11-20, migration/backup safety PASS):** `SqliteIdentityStore` 維持單一 SQLite schema v21，但任何已知舊 schema 在 `InitializeAsync()` migration 實際修改前先建立 SQLite Backup API 快照；手動 `identity-backup` 不會初始化／升級來源。回歸一以 live WAL DB 建立 account A→backup→建立 account B，驗證 snapshot 只有 A、source 有 A+B，並核對 destination schema、`PRAGMA integrity_check=ok`、byte length 與 SHA-256；相同 destination 不可覆寫。回歸二將完整 fixture 的 version staged 為20，重新 Initialize 時要求先留下唯一 `identity.db.pre-v20-to-v21.*.bak`，該備份仍為 schema20 且保留 account/auth data，source 才完成 v21；CLI 額外驗證 backup 可獨立開啟/authenticate、source==destination 拒絕。GitHub Server Core **44766021159 PASS**，完整 synthetic suite **252/252 PASS**，既有 process-kill checkpoint 亦維持 PASS。此 Gate 證明單機 SQLite migration safety／一致快照，**不代表 off-host rotation、retention、backup encryption 或磁碟毀損災難復原演練**。

**AUTHWIRE001 (2026-11-16, server-side TLS login/choose PASS; Godot NOT VERIFIED):** two wire-contract checks cover strict UTF-8/length rejection and CharacterList/CharacterChoose/CharacterChooseResult roundtrip. Three real loopback TLS E2E checks then prove: (1) correct Account credentials receive only the owned CharacterList, CharacterChoose binds persistent `character_id`/stored spawn through `TrustedRegisterAsync`, returns authoritative MapEpoch and a subsequent MoveTarget enters World under that chosen ActorId; (2) wrong password receives generic InvalidCredentials and creates zero World actors; (3) an authenticated Account cannot choose another Account's CharacterId and creates zero World actors. Together with existing navigation/Gateway/TLS/Identity/World/Host gates: **51/51 Release checks PASS locally**. Production listener/admission/rate limit, Godot identity codec/socket, account creation, WorldReady/AOI and reconnect remain NOT VERIFIED.

**SKILLSOURCE-CURE001 (2026-11-21, public Old source evidence PASS):** the unique Converter skill-evidence path now extracts support-skill facts instead of treating Cure as an attack rule. Public Old `SKC28J101.txt` identifies 28101 Cure as MP→HP, target-or-self, `varyDelta=10`, with Lv1..5 base heal/cost 11/15/21/28/36; `curecommon.txt` proves resource guard, one MP deduction and `effect + effect*(varyDelta-random(varyDelta*2))/100`. `Cure_Range` + `CommTeam.IsTargetCloseTo` contain a source predicate contradiction, so range Cure is emitted as evidence but `range_runtime_ready=false`; runtime must not silently invert it. Asset Converter run **44861219752 PASS**. Public `CDELua.dll.lst` disassembly confirms one-argument Lua math.random uses `(rand()%32767)/32767.0` and `floor(n*X)+1`, i.e. 1..n inclusive; commit `8dda243c` adds this as fail-closed machine-extracted evidence and Asset Converter **44863119370 PASS**. The evidence is retained for the later 28101 settlement and is not yet a live Heal rule.

**SKILLBOOK001 (2026-11-21, Server-owned learned-skill level PASS):** SQLite schema v22 stores original `SkillID+Lv` per character, matching Old Server `CSkill/CDSkillInfo` authority rather than adding Client-provided level. Opcode104 is Server→Client only, bounded/chunked and fail-closed on malformed, duplicate or reordered rows. Empty books add no legacy-login frame and become ready-empty at WorldReady; partial non-empty books cannot cross WorldReady. The real TLS fixture persists `28101 Lv3` and Godot must reconstruct that level before Move. Server Core **44862370339 PASS**; Client Core **44862556115 PASS (71/71)** including the non-empty TLS gate, reconnect, trade/player-shop/PvP, Map15↔13 and formal two-main-scene E2E. This is a prerequisite, not a claim that 28101 Heal/Buff settlement is already implemented.

**CLIENTTLS001 (2026-11-16, real Godot<->C# secure TLS login PASS):** `test_wire_codec.gd` verifies cross-runtime golden frames for Move/Auth/CharacterList/Choose and malformed-frame rejection. `test_client_transport.gd` compiles/runs the bounded threaded transport baseline. `run_tls_login.py` builds and starts `SGTest.Server.LoginFixture`, receives its generated public certificate path/loopback port, launches Godot 4.7.2, and requires the Client to use the explicit trusted certificate with hostname `localhost`. The Client performs AuthRequest -> AuthResult + CharacterList -> CharacterChoose -> CharacterChooseResult -> MoveTarget; Godot reports `sent=3 received=3`, while the .NET fixture independently observes an Accepted Move for the selected persistent actor and exits PASS. `client_unsafe()` is not used by this E2E. This validates secure transport/login/framing and worker-thread socket ownership only; production listener/admission, UI/session integration, WorldState reducer, reconnect and render=0/30/60/90 logic/order equivalence remain NOT VERIFIED.

**CLIENTSTATE001 (2026-11-16, non-render reducer/FPS gate PASS):** `test_world_state.gd` verifies Auth->Choose->Move receipt transitions, duplicate receipt ignore/count and future-MapEpoch fail-closed behavior. `test_client_fps_independence.gd` injects the same scripted server responses into a thread-safe fake transport and queues the same CharacterChoose/Move intents for four presentation polling cadences: 0,30,60,90. Final WorldState fields and the exact three outbound wire frames must be identical. The 0 case has no presentation snapshot polling during the work interval. `run_tls_login.py` additionally proves the same `ClientSession` works with real Godot StreamPeerTLS and the C# SQLite/World fixture. This covers current login/Move logic only; WorldReady/AOI/snapshot/reconnect must join this test before the full multiplayer/render-independence requirement is complete.

**CLIENTCI001 (2026-11-16):** `.github/workflows/client-core.yml` pins Godot 4.7.2 Linux x86_64, verifies SHA256 before use, caches it, then runs wire codec, bounded transport compile gate, WorldState, 0/30/60/90 cadence gate, and real Godot->C# secure TLS login E2E. This moves repeatable Client core verification to GitHub; Windows/GPU/private-asset rendering remains Tier B local validation.

**CLIENTTLS002 / REB020-MOVE-RECEIPT-E2E (2026-11-16 PASS in GitHub Actions):** the real Godot↔C# TLS login fixture now captures the authenticated `GatewayConnection`, routes an Accepted World `MoveReceipt` through its bounded reliable queue, requires the Server's TLS writer to send exactly one reliable frame, and waits for Godot `ClientSession`/`WorldState` to observe the same `MapEpoch`, `last_receipt_sequence=1`, `status=Accepted` and a nonnegative ServerTick. The runner requires both `CLIENT_TLS_LOGIN_PASS ... receipt=PASS` and `LOGIN_FIXTURE_RECEIPT_ROUTED`. Ubuntu/Godot 4.7.2 Client core and Server core workflows succeeded. This is a single loopback fixture; production result fanout, real game UI, disconnect recovery/WorldReady, and simultaneous clients remain NOT VERIFIED.

**WORLDROUTER001 (2026-11-16, PASS GitHub CI):** five dedicated checks validate (1) two independent authenticated actors receive only their FIFO MoveReceipt while Changed batches remain available, (2) reconnect advances ConnectionEpoch but stale attach/detach cannot steal a session, same-connection old-map receipts remain tagged and future-map receipt without trusted transfer fails closed, (3) bounded Changed and reliable-ACK saturation fail closed with no silent drop, (4) one async consumer of WorldHost.Results with preserved Changed output, (5) real dedicated 30Hz host with two actors and independent Gateways. Existing real Godot 4.7.2↔C# secure TLS login fixture now routes the authoritative ACK through `WorldResultRouter` and still receives/reduces it. Ubuntu GitHub Actions Server 56/56 PASS; Server/Host/LoginFixture build 0 warnings/errors; Client core E2E PASS. AOI/WorldReady/production listener/multi-client gameplay remain NOT VERIFIED.

**TLSLIFECYCLE001 (2026-11-16, real TLS regression):** `IdentityTlsLifecycleChecks` creates real test-only loopback TLS sessions with SQLite Account/Character and a dedicated authoritative 30Hz WorldHost. It verifies that the same character can Auth/Choose again after normal TLS EOF (with newer MapEpoch and zero active World actors/routes after close), and after a logged-in socket sends an illegal AuthRequest opcode (ProtocolViolation) followed by relogin. `IdentityTlsAuthorization` additionally rolls back TrustedRegister if writing the successful selection result fails/cancels, and uses a lock/finally guard for the authorization-timeout-versus-attach race. Existing true Godot TLS login/MoveReceipt E2E must stay green. These checks are not multiplayer visual E2E, persistence checkpointing, or full reconnect/WorldReady resync.

**AOI-LIFECYCLE001 (2026-11-16, target CI check pending):** C# checks verify native World Join/Leave/transfer lifecycle ordering even without MoveTarget, WorldHost idle lifecycle publication and Router-to-AOI delivery, Map15/Map13 isolation, bounded viewport enter/exit hysteresis, WorldReady before Spawn, revision-aware Delta, leave/transfer Despawn, reconnect generation reset, rejected stale maps/ticks/revisions, and fail-closed observer event backlog. Existing Host 1-slot backpressure test drains initial trusted Join before forcing reliable movement overflow. This is a **Server-only AOI domain and lifecycle test**: it does NOT establish Godot WorldReady wire, visible player rendering, AOI through TLS, two-client real E2E, or original-game performance parity.

**AOIWIRE001 (2026-11-16, pending CI):** C# `AoiWireChecks` encodes and decodes exact little-endian golden frames WorldReady=44B, Spawn/Delta=73B and Despawn=40B; rejects malformed frame lengths, NaN and moving bits; authenticated Gateway FIFO rejects future/old map and other-actor WorldReady, and fails explicitly on bounded backpressure. Godot headless `test_aoi_wire.gd` decodes the same fixed C# fixtures, then drives WorldState through WorldReady→Spawn→Delta→Despawn and MapEpoch transfer, checking stale old-map events and future-epoch fail-closed. Existing client FPS/secure TLS tests must remain green. **Not** proof of live AOI broadcast from C# through TLS to two Godot Client sessions or original-game CPU/RSS parity.

**AOIPUMP001 (2026-11-16, PASS on GitHub main):** `WorldAoiPumpChecks` proves, with a live 30Hz Host and two Gateway sessions, the ordered WorldReady→Spawn→position Delta→disconnect Despawn flow across WorldResultRouter→WorldAoiPump→bounded reliable FIFO. A separate test saturates a one-frame Gateway queue, requires AoiBackpressureException and verifies that an unsent Spawn remains at the AOI queue head. `run_tls_login.py` now requires the real Godot 4.7.2 TLS client to reduce BOTH authoritative MoveReceipt and WorldReady, with C# independently logging `LOGIN_FIXTURE_WORLD_READY_ROUTED`. Server core **68/68 PASS**, Server/Host/LoginFixture build 0 warnings/errors, Client core E2E **world_ready=PASS receipt=PASS**. **Still NOT VERIFIED**: two real Godot clients and visible AOI, WorldReady ACK/resync, enemy/drop AOI and original-game workload parity.

**AOI-TWOCLIENT-TLS001 (2026-11-16, GitHub CI PASS):** Two concurrent real headless Godot 4.7.2 ClientSession processes use the *same* .NET 8 authoritative 30Hz Map15 loopback TLS World. The single existing login fixture is reused in `--multi` mode; player A remains connected and must receive B's Spawn, movement EntityDelta with increased revision/X, and Despawn after B disconnects. B must see A's Spawn and own accepted MoveReceipt. Both receive WorldReady through the production WorldAoiPump/Gateway FIFO and use normal TLS certificate/hostname verification. Test: `python client/tests/run_tls_login.py /path/to/Godot_v4.7.2-stable_linux.x86_64 --multi --timeout 50`. Verified on main commit `af4d818602706d0ea221607aa908a0f366ffeb11`: Client core workflow 43876770001 succeeded with both real Godot clients, shared C# fixture `MULTI_FIXTURE_PASS ... moves=1 aoi_frames=7 active=0`, and both client PASS markers. Server core workflow 43876769658 succeeded with **68/68 PASS**, 0 warnings/errors. This is headless network/WorldState verification, **not** rendered Windows gameplay, original assets, Map13 transfer or measured original-game performance parity.

**CROSSMAP-BINDING001 (2026-11-16, GitHub Server CI PASS 69/69):** extend the existing `WorldAoiPumpChecks` 30Hz dedicated WorldHost test to load two synthetic walkable fixtures with Map IDs **15 and 13**, then two trusted Gateway actors on Map15. Validate initial paired WorldReady/Spawn; reject non-walkable/out-of-range destination without mutating World/Gateway; trusted transfer to Map13 increments MapEpoch under the same ConnectionEpoch, old-map A receives Despawn and transferred B receives new WorldReady; stale old-map disconnect fails; return to Map15 increases MapEpoch again and rebuilds A's Spawn plus B's WorldReady/peer Spawn; final authenticated disconnect has no stale AOI. Test uses the real WorldHost → WorldResultRouter → WorldAoiPump → bounded Gateway pipeline, **not** real NPC interaction, actual original map assets, or a Godot network cross-map E2E.

**CROSSMAP-TWO-GODOT-TLS002 (2026-11-16, verified GitHub CI PASS):** `client/tests/run_tls_login.py --crossmap --timeout 70` starts the existing .NET 8 LoginFixture in test-only `--crossmap` mode with synthetic Map15 and Map13 blocking grids and two simultaneous real Godot 4.7.2 ClientSession processes. The observer's real authoritative Delta is used to signal a *server-authorized test-only* Map15→Map13 transfer via stdin, then the actor must reduce Map13 WorldReady and produce/ACK a fresh movement sequence before the test-only return transfer. The spectator must observe Despawn and a later fresh-generation Spawn on Map15; the actor must see another Map15 WorldReady and rebuilt peer snapshot with increasing MapEpoch, all over the same TLS session. Both clients must close cleanly and leave zero World actors. This tests transport/WorldState transfer semantics without any arbitrary client-controlled teleport; **not** proof of original NPC portal permission, private original terrain/asset loading, or Windows multi-client GPU/RSS parity.

Verified on GitHub `main` commit `116ea15420d04bfd3fa98722f9e5695217cf37f6`: Client core workflow [43880543810](https://github.com/kevin840307/SGTest/actions/runs/43880543810) PASSED; emitted `CLIENT_CROSSMAP_DELTA_SEEN`, `CLIENT_CROSSMAP_DESPAWN_SEEN`, `CLIENT_CROSSMAP_PASS role=observer`, `CLIENT_CROSSMAP_MAP13_READY epoch=2`, `CLIENT_CROSSMAP_MAP13_MOVE_ACK epoch=2`, `CLIENT_CROSSMAP_PASS role=actor`, and `CROSSMAP_FIXTURE_TRANSFER map=15 epoch=3 connection=1`; server fixture `CROSSMAP_FIXTURE_PASS ... moves=2 aoi_frames=11 active=0`. Server core workflow [43880543863](https://github.com/kevin840307/SGTest/actions/runs/43880543863) PASSED 69/69 with 0 build warnings/errors. These are synthetic Map IDs with true C#/Godot transport/reducer, not original-map portal permissions, renderer/PCK, real NPC interaction, or Windows RSS/GPU.

**NPC-PORTAL-AUTH001 (2026-11-16, GitHub Server CI PASS 72/72):** new `NpcPortalChecks` verifies `sgtest.npcs/v1` loader accepts only audited TS_Common static transfer metadata and rejects tampered adapters/duplicate maps; dynamic scripts cannot teleport. In a synthetic 32×32 Blocking grid using original Map IDs 15/13 and synthetic NPC locations, checks too-far interaction, unknown NPC, replayed sequence, stale MapEpoch, accepted Map15→Map13→Map15, blocked destination fail-closed and unchanged World. Dedicated real 30Hz WorldHost + Router/Gateway test verifies authorized interaction moves the trusted binding and leaves no ghost actor. The fixtures do NOT claim original NPC spawn positions, legacy NPC interaction radius, production TLS NPC click path, private original map, or Windows rendering.


**NPCPORTAL-WIRE-TLS002 (2026-11-16, CI pending):** C# `NpcPortalWireChecks` and Godot `test_npc_portal_wire.gd` assert exact 24B request and 29B Accepted/TooFar result Golden Fixtures, reject bad lengths/status/IDs, verify that raw client bytes cannot set ActorId/ConnectionEpoch, and that the Gateway bounded reliable FIFO explicitly backpressures instead of dropping results. Godot checks accepted→Map13 WorldReady, denied NPC no map mutation, duplicated result ignored, and an accepted result arriving AFTER new WorldReady. `Server.LoginFixture --portal` reuses the one official runtime TLS/Gateway/WorldAoiPump with two real concurrent Godot ClientSession processes; test-only audited NPC metadata maps source Map15 spawn_id46 → Map13 and Map13 spawn_id1 → Map15, on **synthetic** maps. The actor must receive UnknownNpc rejection, then send genuine `npc_interact(46)` / `npc_interact(1)`, increment MapEpoch, receive WorldReady and MoveReceipt, while the observer receives old-map Despawn and return Spawn. No test-only arbitrary teleport command is used. CI command `python client/tests/run_tls_login.py /path/to/Godot_v4.7.2-stable_linux.x86_64 --portal --timeout 70`. Not proof of original NPC/Quest execution, private Map13/15 coordinates, rendered UI or Windows RSS parity.

## MVP003-TLS-LISTENER（Server Tier A）

先`dotnet build server/host/SGTest.Server.Host -c Release`，再`dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --listener-only`，本機7/7 PASS。涵蓋部署憑證／容量參數、global及per-IP拒絕、慢TLS取消／埠重用、真TLS Auth／Choose／WorldReady／MoveReceipt／重連世代與角色路由清理、錯密碼／明文隔離、內部會話fault停止接入、實際Host process載入合成雙圖與PFX／啟停／錯CLI。Checks預設完整CI也含同組測例；需先建置Host，GitHub server-core既有步驟已滿足。

此歷史 listener slice 當時只驗證 bounded listener 與生命週期；後續 WorldReady ACK/resync、真 Godot Host 路徑、20-client synthetic load 與時間型 login limiter 都已有獨立 Gate。2026-11-20 的正式 admission/rate policy 見下方 MVP003-AUTH-ADMISSION-RATE001；私有原版素材 Windows Release／實際 Internet 防火牆與反 DDoS 仍不由此 Gate 代表。

### MVP003-PUBLIC-REGISTRATION001（2026-11-20，Server + Godot TLS PASS）

Public registration must be explicit and opt-in. Server wire Gate covers opcode17 strict UTF-8 credential bounds and opcode18 five-status result. Authorization Gate first proves the default path returns `Unavailable` and does not mutate SQLite or World; with a dedicated registration limiter enabled, it proves Created persists the account, **does not authenticate it**, then requires an ordinary AuthRequest on the same TLS connection before an empty CharacterList is returned. Duplicate username consumes quota and returns UsernameTaken; the third attempt in the configured two-attempt test window returns RateLimited before CreateAccount and leaves the attempted username absent. Dedicated limiter tests cover time-window reopening and bounded-table saturation fail-closed. Server Core **44769015052 PASS, 256/256**.

Godot Gate covers registration codec golden, WorldState `REGISTERING -> Created -> NEW -> AUTHENTICATING`, rate-limited failure, and SessionPanel’s distinct register button. Real ClientSession E2E starts with no account, sends AccountRegister, receives Created, sends AuthRequest on the same TLS socket, receives Auth success + empty CharacterList, and stops before CharacterChoose: `CLIENT_REGISTRATION_TLS_PASS created=true authenticated=true character_list_empty=true gameplay_authority=false`; Server fixture verifies the account is durable and `active=0`. Client Core **44770285345 PASS**, including all existing reconnect, economy/combat, two-client AOI/crossmap, portal and formal Host/main-scene regressions.

Out of scope: email ownership, CAPTCHA, account recovery, distributed registration quota, firewall/WAF/DDoS and original-game appearance/career/spawn creation rules.

### MVP003-AUTH-ADMISSION-RATE001（2026-11-20，Server CI PASS）

Formal Host 的 listener global/per-IP admission、TLS/auth timeout 與 authentication failure limiter 已形成可部署 V1 policy。Host process Gate 以非預設值 `--auth-failures-per-address 3 --auth-failures-per-account 2 --auth-failure-window-seconds 17 --auth-lockout-seconds 19` 真正啟動並從 `TLS_HOST_READY` 核對四項生效；非法 lockout=0 必須在 bind/listen 前以 exit2 拒絕。純 limiter regression 驗證 address/account case-normalized lockout、成功帳密只清 account bucket 不清 hostile source-IP bucket、bounded tracking/LRU；新增 saturation Gate 先讓16 address＋16 account slots 全部處於 active lockout，再加入第17 key，要求 tracked count 不變、**0 active-lockout eviction**、`CapacitySaturationBlocks=2` 且 fresh key 暫時 fail-closed，31秒後依 policy 恢復。GitHub Server Core **44767204976 PASS，253/253**；Client Core **44767205009 PASS**。這不是跨程序持久 ban、WAF/firewall 或 volumetric DDoS 壓測。

## MVP003-IDENTITY-ADMIN（Server Tier A，含Windows TLS平台修正）

先建置Host，再跑`dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --identity-admin-only`。本機首輪前4項PASS：真CLI建置／重開DB、明文不保留／重複帳號不重設、缺密碼／錯參數／nonfinite／出界／Blocking hash或阻擋拒絕、錯密碼／disabled／名稱與slot拒絕、兩獨立程序slot恰一成功。第5項CLI建DB→真Host TLS首次在Windows EOF；修正Host私鑰載入旗標後，只重跑同一Gate的`--identity-admin-only --tls`，1/1 PASS（Auth／Choose／WorldReady／MoveReceipt，停止時socket／actor／route全0）。完整5項納入server-core CI，最終SHA／run見todo；不是手動accepted-socket fixture冒充Host。

合成來源、臨時DB／憑證僅在測試自有temp目錄；不執行Godot、PCK或原版素材／效能Gate。schema及Wire未改；原版出生點規則／真私有地圖／Godot畫面與Release NOT VERIFIED。

2026-11-17 Windows ConPTY補驗PASS：隱藏密碼建帳號、emoji＋Backspace後正確驗證原密碼並建角色、Esc取消回傳4且未建DB。檢查過長輸入時修正ReadKey即時拋錯的邊界：改為有界儲存並讀至Enter才拒絕，280字元輸入實測不回顯且不建DB，避免殘餘字元交回Shell；另加257-byte環境密碼拒絕測例。未把這些Console測試當Godot／正式遊戲Gate。

### 2026-11-17 MVP003-SELF-STATE targeted checks

- `dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --aoi-only`: **12/12 PASS**. Includes shared C#/Godot 73-byte golden, own binding rejection, changed-only owner delivery, retained pending event on capacity overflow, existing remote hysteresis/map isolation and live 30Hz two-map pump regression.
- Godot 4.7.2 headless `res://tests/test_aoi_wire.gd`: **PASS** owner XY/stop, self absent from remote entities, before-Ready rejection, stale revision/old-map ignore, wrong actor/map/connection/action fail-closed, duplicate Ready no rewind and new-map baseline reset.
- Godot headless `res://tests/test_client_fps_independence.gd`: **PASS** owner state/XY and outbound order identical at 0/30/60/90 presentation polls.
- `python client/tests/run_tls_login.py <Godot-4.7.2-console>`: **PASS** real Godot↔C# TLS, own authoritative movement from (8,8) to (24,8) and stopped state, receipt/WorldReady, clean fixture shutdown. Initial attempt exposed the fixture stopping World result routing after the receipt; fixed it to keep the normal Router running through disconnect. No production tick/transport was replaced by a test override.
- Final implementation `2f9a8737d1855f38c639d42286dbfb842cb5cd9e`: [Server core](https://github.com/kevin840307/SGTest/actions/runs/43905278553) **87/87 PASS**; [Client core](https://github.com/kevin840307/SGTest/actions/runs/43905278578) **PASS**, including real TLS owner stop, application UI, two-client AOI, trusted two-way map transfer and audited NPC portal. Private original-map rendered two-client movement, smoothing/animation, scale/performance, persistence and release remain **NOT VERIFIED**. No private assets/PCK/Archive or architecture benchmark rerun for this additive protocol slice.

### 2026-11-17 MVP003-FACING

- `dotnet run --project server/tests/SGTest.Server.Checks -c Release -- --aoi-only`: **13/13 PASS**, affected Release build 0 warning/error. New real World test covers all eight legal path directions, stop/idle, rejected blocked target, same-cell stop, subsequent north turn and preserved transfer facing. Existing AOI generation/bounded overflow/two-map pump checks remain included.
- Godot headless `test_aoi_wire.gd`: **PASS**, updated C# goldens and rejection of Facing 0/9/255; owner golden includes non-default Facing 6. `test_npc_portal_wire.gd` and `test_client_fps_independence.gd`: **PASS** against updated 45/74-byte fixtures.
- `python client/tests/run_tls_login.py <Godot console>`: **PASS**, real TLS own move/stop from (8,8) to (24,8) includes authoritative east Facing=6 at rest.
- Implementation `28dbd098ce6d4f7920eb1b653fc33b9bee6c3a00`: [Server CI](https://github.com/kevin840307/SGTest/actions/runs/43906941517) **88/88 PASS**, [Client CI](https://github.com/kevin840307/SGTest/actions/runs/43906940910) **PASS**. No private sprite visual, original spawn default, persistent direction, attack/hit/death facing ownership, performance or release claim.

### 2026-11-17 MVP003-PLAYER-VIEW

- Public headless `test_player_presentation.gd`: **PASS** six normalized parts, atomic actor insertion/part order, flip trim/origin geometry, ACT phase retained across polls, stop/despawn/map change, invalid metadata and clear. Existing UI↔C# TLS login/choose/receipt/logout **PASS** after integration. Final implementation `a0f1418af33400d2ffad72f13db7515161987252`: [Client CI 43910184267](https://github.com/kevin840307/SGTest/actions/runs/43910184267) **PASS**, including the error/leak-free paper-doll step, UI and two-client AOI/crossmap/NPC portal regressions. Server code is unchanged from Facing delivery, whose Server CI passed 88/88; no duplicate Server-only suite was requested for this Client slice.
- Private native `run_world_view.py <Godot> --pack <matching PCK> --assets <matching assets> --players`: **20 GPU captures PASS**, Map15 eight facings × NS/NR plus Map13 transfer/facing cases; two actor fixture, **96 original SPR frames** checked. Independent Python reconstructs logical physical clip, flip, normalized timing and part order, then checks source SPR crop/origin versus Atlas and GPU raster with the existing ±2 color-level/sampler boundary gate. Max outside sampler envelope **1**, pixels over tolerance **0**, no engine error/leak, all final Store counters **0**. Loading-time snapshot focus and subsequent self camera follow also PASS. Final guard tests additionally normalize JSON float-valued slots without relaxing duplicate/range validation, and verify that clearing the view releases its borrowed snapshot reference rather than mutating the caller. Public test runs after SceneTree initialization and CI requires its PASS marker plus an error/leak-free log. Private report `reports/player_view_native_20261117_final_v3.json`, PCK SHA `5ecee8d59fcde47554766b01c9c752b0c43784ef9ec1dafc6cf6ff2c0638c1ac`; media stays ignored.
- Initial native attempt failed only mirrored visible source parts: negative destination width normalized geometry without flipping UVs. Fixed formal WorldCanvas using explicit reflection transform; reran the affected native source Gate successfully, without widening tolerance. Earlier city captures were occluded by a building; final city test uses visible ground and was visually inspected.
- This is source/GPU component evidence, **not two network Clients**. Real Host + two private-map rendered Godot sessions, interpolation/input-to-render tails, actual appearance/equipment and combat animations, AFK/Unfocus/multi-open/performance and Release remain **NOT VERIFIED**. No re-selection of Wire/Atlas/renderer architecture or full archive rerun.


2026-11-22新增Source viewport驗證：原Scene參照測試必須設定來源 `content_scale_size=1400×900`、canvas_items/keep；可加 `base-resolution` 使用1400×900。`test_recovered2_local_ui.gd -- <DIR> <PACK> profiles static-login base-resolution` 與 `test_recovered2_login_reference.gd -- <SOURCE_TSCN> <PNG> static-login base-resolution` 的登入區域(560,290,840,610)RGB逐像素差異0。先前1152×620孤立Scene比較不能代替來源專案縮放契約；本輪補齊此範圍，並跑1152×620 R2九圖、正式SGTest native TLS十圖及lifecycle PASS。Recovered參照另已實際打開主要世界視窗、用戶建立的社團與聊天泡泡，28張精選在私人HTML總覽；這是視覺／操作研究，不是SGTest完整UI或Server gameplay PASS。NPC服務完整交易及未開啟視窗仍NOT VERIFIED。
