# Historical SGTest reference — Runtime Protocol Evidence

> REFERENCE ONLY. Archived from the pre-rebuild codebase on 2026-11-15.
> These are exact historical texts, NOT the current client/server implementation or tests.
> Use the current ARCHITECTURE.md and todo.txt for decisions.
> Original individual documents remain recoverable from git commit f755a47.

## Contents

- [CLIENT_RUNTIME_ISOLATION.md](#source-client-runtime-isolation-md)
- [DATA_MODEL.md](#source-data-model-md)
- [IDENTITY_LIFECYCLE.md](#source-identity-lifecycle-md)
- [NETWORK_AND_MOTION.md](#source-network-and-motion-md)
- [NORMALIZED_ANIMATION_DEFINITION.md](#source-normalized-animation-definition-md)
- [PERF002_MEMORY.md](#source-perf002-memory-md)
- [PERF003_CANCELLATION.md](#source-perf003-cancellation-md)
- [RUNTIME_DIRECTION_AND_MOTION.md](#source-runtime-direction-and-motion-md)
- [RUNTIME_ISOLATION_VERIFICATION.md](#source-runtime-isolation-verification-md)
- [SECURITY_OPERATIONS.md](#source-security-operations-md)

---

<a id="source-client-runtime-isolation-md"></a>

## CLIENT_RUNTIME_ISOLATION.md

*Historical source: `docs/CLIENT_RUNTIME_ISOLATION.md` — SHA-256 `20db3fec3c74718912d69bf095e75b445c5ecfdb29d6e92aeab282d6cda9c644`*

# SGTest Client ↔ Server / 邏輯 / UI / 渲染完全分離契約

版本：2026-11-14。**ACCEPTED 架構原則；完整 FPS／背景隔離 IMPLEMENTATION NOT VERIFIED**。目前已有 Godot Client 與獨立 C#／.NET 8 Server 原型。本文件為工程和回歸測試的硬性限制。

## 1. 不變條件（Hard Invariant）

**UI FPS 增加或降低、World FPS 增加或降低、畫質、失焦、最小化、AFK 都不得加快或減慢 Client↔Server 傳輸、收包、解碼、事件處理、輸入指令送出、移動、戰鬥、撿物或 Server Tick。**

- Server 使用獨立的 ServerSimClock，權威位置、碰撞、傷害、掉落、交易不能依 Client FPS 變動。
- ClientNetworkPump 負責 WebSocket I/O、decode、verify、heartbeat、outbound writer。排程必須與渲染 `_process(delta)`、UI 動畫或畫面刷新的 callback **解耦**。
- ClientStateReducer 以 ServerSeq/SimTick/MapEpoch/EntityRevision/ActionId 排序、驗證、套用 authoritative State，維護 packed runtime records。不得由 `AnimationPlayer` 或 `_draw()` 推進。
- ViewProjection 輸出不可變 VisualSnapshot/ViewModel；WorldRenderer 與 UIRenderer 只讀取投影，不能直接持有 socket 或呼叫 DB。
- Client Input/點地板/選怪/按快捷鍵經 CommandGateway 轉成 typed request，按協定即時傳送；只允許無權威的視覺預測。
- **30 FPS 不表示 network 30Hz，120 FPS 不表示 network 120Hz**。控制面板的 FPS 只能控制呈現工作率，不能控制網路或 State 工作率。
- **World movement speed 不等於 ACT action speed**：Paradise reference 的 `CSGOChar::SetSpeed` 與 `CELxnObj::SetActionSpeed→xnAction::SetSpeed` 是兩條獨立路徑；未收到明確 action-speed 語意時，不得拿移動速度去乘 `AnimationPlayer.speed_scale` 或任何 clip rate。詳 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。

## 2. 四種獨立時間軸

| Clock / Component | 擁有內容 | 排程來源 | 與 UI FPS 的關係 |
| --- | --- | --- | --- |
| ServerSimClock | MapActor world/combat authoritative simulation | Server monotonic fixed-step | 無關 |
| ClientNetworkPump | WebSocket poll/receive/send/heartbeat/decode | IO wakeup / independent schedule | 無關 |
| ClientStateClock/OrderedEventReducer | 命令結果、事件因果、local input state | Queue event + independent fixed step（如需） | 無關 |
| VisualClock | Player pose、動畫插值、FX、HUD/Control repaint | ServerTime estimate + rendering time | 可以降頻，但不回寫 State |

獨立排程**不等於**每一個都要建立一個 OS thread。可是若 Godot 背景/最小化預設會節流主循環，實作必須實測並提供不受 render callback 降速影響的 IO/Reducer 排程（例如安全的 worker + queue），而不是只把 network code 從一個 node 移到另一個 node。Godot SceneTree/Node/Resource 必須遵守執行緒安全規則，不在背景 thread 直接改 UI。

## 3. 責任拓撲

```text
SERVER: MapActor (authoritative sim clock)
     ^     |
     |     v
CLIENT: NetworkPump + Codec + OutboundCommandWriter
              |                ^
              v                |
      Bounded Ordered Queues   CommandGateway <-- UI / local input
              |
      OrderedEventReducer (seq, epoch, barriers)
              |
      PackedWorldState (NO visual objects)
              |
      Immutable ViewModel / VisualSnapshot
              |
         ViewProjection
         /            \
  WorldRenderer      UIRenderer
  Atlas/VisualPool   Theme/Control
         \            /
          VisualClock / FPS / AFK visual policy
```

Socket receive、decode、可靠事件套用禁止從 UI `_process`、`_physics_process` 的**視覺更新頻率**來驅動；固定 `_physics_process` 也必須證實失焦/縮小時仍保持期望節奏。若使用 background worker，UI 只從线程安全 snapshot 消費，不跨 thread 改 CanvasItem。

## 4. 多人/怪物/掉落的事件語意

- Network ingress 按協定驗證、seq/epoch 去重；可靠事件 Spawn、Despawn、Stop、Teleport、Hit、Miss、Death、Pickup、Trade 不可因 FPS、資源未載入或 render backlog 丟棄。
- 對同 Entity MotionSeq 的過期 movement snapshot 可安全 coalesce，但**不能跨 Stop/Teleport/Death barrier**；不要用單純 latest-message-wins 覆蓋重要動作。
- Server 傳移動路段 start/end/tick/speed/facing 和 ActionId/start time；Render 依時間取得 pose。遠端 Spawn 必須以最新位置和當前時間狀態出現，不能從 (0,0) 或舊位置補播。
- 收到 Hit/Death 時 Texture 尚未載入也先套用 State。完成資源預載後從最新 VisualSnapshot 顯示，不回放中間落後封包。
- Socket/事件 Queue 有明確 bounded capacity、queue age metrics、backpressure/resync 策略；不可把可靠事件當作「效能優化」丟棄，也不可無限積壓。
- Client 預測只控制畫面，本地延遲補償不能回寫 Server 撞牆、普攻距離、命中/傷害、拾取。

## 5. FPS/失焦/縮小/AFK 行為矩陣

| 模式 | Network / Decode / Reducer / Command | Server | UI / World Rendering / Audio |
| --- | --- | --- | --- |
| 60 FPS 正常 | 正常獨立節奏 | 不變 | 一般更新 |
| 30～90 FPS（含45／75／90） | **相同協定節奏、狀態結果與事件因果** | 不變 | 按指定 FPS 取樣呈現 |
| UI 動畫降頻（若支援） | **完全不受影響** | 不變 | 非必要 UI animation 可降頻 |
| 失焦 / 最小化 | **不可停止/降頻** | 不變 | 僅減少或跳過視覺與音效 |
| AFK | **不可停止/降頻** | 不變 | 可卸載 world visual，保留 compact state |
| 低畫質 | **不可改變 logic / packet** | 不變 | 圖集品質/FX 降級、可視優先序 |

如果 `Engine.max_fps` 在 Godot 實際上連帶降低了某個 network 驅動 callback，就算分成不同 script 也**不合格**。需要獨立 pump 或其他已測證的機制。UI 的局部動畫限速也不能宣稱是獨立的底層 swap FPS。任何畫質檔都不能讓玩家/技能/掉落的 Server 狀態停止更新。

## 6. 素材與狀態生命週期分開

- AssetCatalog/MapAssetPalette/StreamingManager 只服務 Renderer；Loading queue 的容量、priority、cache eviction 不得和 Server reliable events 混用。
- P0 優先載入玩家當前動作，P1 戰鬥中的怪，P2 近場，P3 掉落圖示、P4 低頻裝飾；這只是**視覺資源優先級**，不能使掉落/戰鬥事件更晚進 State。
- AFK 可釋放 World Atlas/VisualPool/FX、關音效，但 Server session、NetworkPump、Reducer、輸入指令和合法自動行為不能因此暫停。
- 回復 AFK/縮小時以現在 State 和 ServerTimeline 重建 Visual，必要時顯示進度但不中斷 Network IO。
- UI Theme/HUD 持有自己的 resources，與 Map/Monster/Drop assets 的 refcount/owner 分離；切圖/登出不留下過期 world references。

## 7. 回歸與實測必備（尚未執行）

1. 同一 packet trace、相同 server seed，比較 30/45/60/75/90 FPS + UI animation 限速：RX/TX 數、可靠事件次序、Reducer processed event hash、HP/XY/Combat/Drop state hash **必須一致**（視覺影格可不同）。
2. 前景→失焦→最小化→AFK→回復：`NetworkPollIntervalP95/P99`、`RxQueueOldestAgeP95/P99`、`ReducerDelayP95/P99`、`OutgoingCommandDelayP95/P99` 不得隨渲染 FPS 降低而系統性增加。
3. 同幀 128 封包 + 0/50/100/250/750ms jitter、20 玩家+300 怪+500 掉落，驗證 Stop/Teleport/Death barrier、Miss/Hit 正確、旁觀者動作、遠邊進 AOI 不瞬移。
4. 大型 Texture 首次 async 載入、GPU upload、切圖、登出100次：不能因資源未就緒丟 reliable events 或讓 IO queue 無限擴大；記 GPU spike 和資源釋放。
5. 記錄 FPS、focused/minimized/AFK、OS/GPU、ServerSeq、packet trace hash、queue metrics、frame P95/P99、RAM/VRAM；GitHub headless 邏輯測試不等於 Windows 真 GPU PASS。

**以上全為規格與驗收設計，實作與測試：NOT VERIFIED。** 配合 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md)、[ARCHITECTURE.md](ARCHITECTURE.md) 與 [TEST_PLAN.md](TEST_PLAN.md)。

## 8. 低功耗多開與實際缺口（2026-11-14）

使用者觀察測試畫面仍有頓挫，尚未定位原因。一般多人場景的平均FPS不能代替載入、每角色位移與背景模式量測。最新順序及門檻以 [SPEC §0.4](../SPEC.md) 為準；不能為省電降低同步頻率。

現有程式檢查（非完整通過證據）：

- `client/scripts/network_client.gd::_run` 的 WebSocket poll／收送在獨立 worker，bounded queue 與畫面分開。
- `client/scripts/main.gd::_physics_process` 才消化收包、decode／dispatch及執行 `_client_step`。主循環卡住或背景節流仍可能延後state／command；需實測並改為獨立邏輯排程與安全快照。
- `client/scripts/main.gd::_process` 仍發送時鐘ping，屬明確待移出的網路排程。此處角色插值、camera與繪圖可留在呈現層；ACT取樣不改時間軸。
- `client/project.godot` 的30Hz物理設定只代表設定值，未證明5FPS、失焦、最小化時仍及時處理state／input。不可直接調 Engine.max_fps 就宣稱低功耗模式完成。

新增驗收：5／10／15／30／45／60／75／90FPS，以及停止世界繪製但保留邏輯；單開及2／4／8開，記錄每程序與總CPU／RAM／GPU。前景→失焦→最小化→掛機→恢復期間，記錄實際logic/input/snapshot/tick頻率、queue P99、因果／狀態hash、獨立實體位移與資源恢復。低FPS畫面本身較不連續是預期，但不可造成Server或其他觀察者同步頓挫。全部新增矩陣目前 **NOT VERIFIED**。

一般遊玩正式支援範圍為 **30～90 FPS**（使用者2026-11-14指定）；5／10／15FPS屬獨立低功耗驗收模式。兩者都不能改變Client↔Server傳輸／邏輯頻率。120FPS舊矩陣僅為額外壓測，不屬目前正式支援承諾。

---

<a id="source-data-model-md"></a>

## DATA_MODEL.md

*Historical source: `docs/DATA_MODEL.md` — SHA-256 `51222be716f5793861b606ef47e0203131edd9d34c943d7a127bfb09b684b062`*

# SGTest 資料模型、ID 與交易一致性（初版 SQLite）

F001 已建立初版 SQLite migration：`account`、`character`、`schema_migration` 已有實際 DDL、WAL/FK/busy-timeout 與 unit/integration/E2E tests，詳 `docs/IDENTITY_LIFECYCLE.md`。下列其餘資產／交易／任務表仍是後續設計骨架，必須各自以 migration + contract/integration tests 落地後才算完成。

## 1. ID 原則

- account_id：獨立帳號
- character_id：持久玩家角色
- session_id / connection_epoch：短期連線身份，重登須更新 epoch
- map_definition_id / channel_id / map_epoch：地圖定義、負載分組、換圖/重建版本
- entity_id / entity_revision：世界暫態物件身份/版本（不可當背包物品 ID）
- item_definition_id：跨全伺服器共用且封版後不可重排的物品種類
- item_instance_id (UUID/ULID)：每件物理物品唯一資產 ID，與+N強化無關
- command_id / operation_id / event_id：每種資產/戰鬥動作唯一冪等身份
- stable_content_key：檔案/內容來源通用名稱，不把本地路徑當唯一 identity

## 2. 關鍵表

Account(id, credential_hash, state, created_at, version)
Character(id, account_id, name, job, level, exp, equipment_snapshot_version, current_map_id, x,y, facing, reborn_map, last_saved_at, version)
CharacterStat(character_id, hp, mp, sp, allocations, buffs_version)
ItemDefinition(id, stable_key UNIQUE, category, equip_slot, enhancement_profile_id, data_hash, schema_version)
EnhancementProfile(id, slot_category, max_level, success_table_key, cost_rule_key, stat_rule_key, failure_rule_key)
ItemInstance(uid, definition_id FK, owner_type, owner_id, location, enhancement_level, durability, affix_blob, bound, quantity, version)
InventorySlot(character_id, slot_index, item_uid UNIQUE, version)
EquipmentSlot(character_id, slot_key, item_uid UNIQUE, version)
BankSlot(account_or_character_id, slot_index, item_uid UNIQUE, version)
GroundItem(uid, map_instance_id, x, y, quantity, protection_owner, expires_at, version)
ShopListing(id, seller_character_id, item_uid, price, state, version)
Trade(id, a_character_id, b_character_id, state, version)
TradeEntry(trade_id, item_uid, amount, locked)
Wallet(owner_id, currency_type, amount, version)
Ledger(id, operation_id UNIQUE, debit_wallet, credit_wallet, amount, created_at)
QuestProgress(character_id, quest_id, version, stage, state_blob)
PetInstance(uid, character_id, definition_id, level, exp, state_blob)
MechaInstance(uid, character_id, definition_id, level, exp, parts_blob)
HouseInstance(id, owner_id, template_id, access_policy, version)
HouseFurnitureInstance(uid, house_id, furniture_definition_id, x,y, rotation, version)
LegacyItemMapping(source_repo_sha, source_catalog, old_id, new_definition_id, enhancement_level, evidence, status)
OutboxEvent(id, operation_id, aggregate_id, event_type, payload, published_at, retries)

## 3. 實體責任

- 持久化真相（初版 SQLite）：玩家/背包/錢/強化/交易/任務與可持久化房屋配置；所有變更必須經授權/交易/版本檢查。Domain/Application 只依 repository ports，不能直接使用 SQLite 型別或方言。
- MapActor 真相：瞬間怪物/玩家/掉落存在、座標、戰鬥、追擊、AOI/Chat 附近觀察關係；必要持久資料用事件與 repository 持久化。不能在每個 ClientSession 重複一套怪。
- Client 僅快取：顯示狀態、預期動畫/插值時間、畫面可見物件。Client 預測位置不寫回 Server 權威。

## 4. DB 操作安全模式

所有資產變更需要 operation_id；BEGIN TRANSACTION→驗證 owner/鎖定條件/數量/版本→一次修改金錢與物品→建立 Outbox→COMMIT→回應 immutable receipt。同一 operation_id 重試回傳同結果，不再扣款。
- SQLite 啟用 WAL、foreign_keys、明確 busy timeout 和單寫入排程；資產事務保持短且原子。位置背景批次若遇 writer busy 可重試，角色換圖/登出 flush 與資產提交不得直接丟棄。
- Provider 抽象以「原子資產操作、讀取快照、位置 flush、Outbox」等用例契約為單位，不暴露 SQL 字串或 `DbConnection` 到 Domain；SQLite 與未來其他 SQL provider 跑同一組契約測試。
- 買一件剩1庫存的商品：10人同時買，最多1成功、其他明確失敗。
- 同UID 不能同時處於 Inventory+Equipment+Bank+Shop+Ground；唯一鍵/交易層雙重保護。
- 交易與取消/斷線競速：同一操作結果不能一半成功；賣家下線不得殘留可再次販售的鎖定物品。
- 失敗回復：Outbox 重試/去重；不可在沒有 commit 的情況下給 Client 顯示確定獲得物品。
- 位置低頻寫入可最終一致，但進/出圖以及登出必須明確 flush/恢復；交易金錢必須強一致。
- 寵物/機甲/技能/任務讀取及 GameData 目錄進程級快取，避免每個 AI Tick 讀 DB。
- ServerTick/Position/CombatEvent 有各自 monotonic seq，不以資料庫 update time 充當世界時鐘。

## 5. 初始與後續資料遷移

初版可以新建完全新的帳號/物品 DB；Old schema 與舊角色資料並非必須直接匯入。若導入舊商品 catalog，經明確乾淨導入器生成 ItemDefinitions + LegacyItemMapping，稽核所有引用。
新 ItemDefinition ID 第一次 import 依 docs/ITEM_ID_MIGRATION.md 做密集生成，正式版本凍結後只能 append、tombstone，不可重排，需保證原有玩家持久資料不失效。

## 6. 測試

資產守恆（Item UID/數量/金錢）、併發交易、防重放、跨圖/下線恢復、被回收掉落不能撿兩次、強化失敗 Rollback、角色重連、DB 隔離與回放一致；端對端必實際接初版 SQLite（含 WAL writer 爭用）。未來更換 provider 時，同一契約測試與資料遷移測試必須在新資料庫再次通過。

---

<a id="source-identity-lifecycle-md"></a>

## IDENTITY_LIFECYCLE.md

*Historical source: `docs/IDENTITY_LIFECYCLE.md` — SHA-256 `2b404eadc0b6ee7e6c910073dd71c8edf80ad035bf69e0a44a93f350255f7e71`*

# F001 Identity Lifecycle（帳號／角色／重連）

日期：2026-11-14。狀態：**F001 PASS**。

## 完成範圍

- Account：建立帳號、登入、登出。
- Character：角色列表、建角、最多4角色、全域不分大小寫唯一名稱、選角。
- World lifecycle：WebSocket connect 不直接 Spawn；只有成功選角才建立 `World.Player`。
- Reconnect / takeover：同帳號新登入先移除舊 World Entity，再讓新 Session 取得帳號；舊 Client 可靠收到 `session_replaced`。
- Identity expiry：每次成功驗證取得新的 Server `ConnectionEpoch`；logout/takeover 清除 AccountId、CharacterId、Ready、ConnectionEpoch 與 AOI cache，晚到的 world command 不再具有權限。
- Persistence：正式 Server 預設 SQLite；Memory store 只保留給 deterministic/performance benchmark。

## Server 分層

`server/Domain/Identity.cs` 只定義 identity use-case contract 與 `AccountService`，不依賴 SQLite 型別。`IAccountStore` 提供原子操作：

- `FindCredentials(normalizedUsername)`
- `TryCreateAccount(...)`
- `Characters(accountId)`
- `Character(accountId, characterId)`
- `CreateCharacter(..., maximumCharacters)`

`server/Infrastructure/SqliteAccountStore.cs` 是初版 provider。密碼由 `AccountService` 使用 PBKDF2-SHA256、每帳號16-byte random salt、32-byte hash；password hash 不進 WorldState。

## SQLite v1

正式 Server 預設資料庫：`server/data/sgtest.db`（Git ignored），可用 `--db=<path>` 覆蓋；`--accounts=memory` 僅供明確測試。

Provider 啟用：

- `journal_mode=WAL`
- `foreign_keys=ON`
- `busy_timeout=5000`
- `synchronous=NORMAL`
- 單 provider write gate + SQLite transaction

初始 migration 建立 `schema_migration`、`account`、`character`。`normalized_username` 與 `normalized_name` 都有 UNIQUE；角色上限與名稱唯一性在同一寫入 transaction 內驗證。World 30Hz tick 不直接做 PBKDF2 或 SQLite。

## Auth 工作排程

register/login 密碼雜湊不在 World actor 執行。每 Session 同時最多一個 auth job，全 Server 最多4個 PBKDF2 workers；完成結果再投回 bounded WorldHost command queue，由單一 writer 套用 Session/World 狀態。

因此登入尖峰不會把密碼雜湊時間灌進 30Hz Map/World tick。

## Wire v2

Identity 使用 op10～14；詳 `docs/RUNTIME_PROTOCOL.md`。流程：

1. connect：只建立 Session。
2. register/login：成功後取得 AccountId + ConnectionEpoch，Server 回角色列表。
3. create/select：select 成功才送 op0 Hello + op1 World。
4. Client 驗證相同 map/content/population 後送 op24 WorldReady。
5. 此後才接受 input/attack/pickup 等 world command。
6. logout/disconnect/takeover：先 LeaveWorld，再失效 identity。

## Client

`client/scripts/auth_ui.gd` 提供登入／建帳／角色列表／建角／進入遊戲／登出。World HUD 在真正選角進圖前保持隱藏。

Benchmark Bot 不使用後門：它也走 login → account_not_found → register → create → select → WorldReady 的相同協定。

## 驗證證據

- C# server/domain/provider：376 checks PASS（包含 Memory race、SQLite WAL/FK、provider reopen persistence、SQLite concurrent duplicate character）。
- `client/tests/test_auth_ui.gd`：PASS。
- Full Server restart persistence：第一次 `registered` 並建立 AccountId=1/CharacterId=1；Server 完整重啟後直接 `authenticated`，仍為相同 AccountId/CharacterId；PASS。
- Same-account lifecycle（SQLite）：A register/create/select → B takeover → C takeover+logout；三個 Client 使用同一 AccountId/CharacterId，ConnectionEpoch 各自不同；Server replacement=2；`/health players` 為 **1 → 1 → 0**；PASS。
- 20-client SQLite first-login storm（final rerun）：20/20 在約 **1.55s** 內完成註冊/建角/選角；tick=300 P95/P99 **2.207/7.596ms**；0 early exit；PASS。
- 目前完整 main Client final smoke：login→register→create→select→map ready→prediction/snapshot，Render 約60.00FPS、physics/input 約30.00Hz、queue P99 **30.009ms**、0 Client/Server error；PASS。

## 邊界

- 此 F001 完成定義不包含 Email、忘記密碼、2FA、正式營運 TLS/反暴力破解；這些屬後續 Security/Operations。
- 角色完整 stat/inventory/equipment/position persistence 會隨 F002/F008/F005+ 的資料模型逐步加入；F001 目前持久化的是 Account + Character identity。
- `ConnectionEpoch` 是 process-lifetime 身分版本；Server restart 會終止所有舊 socket，因此新 process 可重新起始 epoch。
- 原版逐格視覺 AS005 與媒體授權 AS008 與本功能無關，仍保持獨立 Gate。

---

<a id="source-network-and-motion-md"></a>

## NETWORK_AND_MOTION.md

*Historical source: `docs/NETWORK_AND_MOTION.md` — SHA-256 `d7a50e0c6b33c5ae5ffd91af233de3c6070b2450139feaf19241806141c065fd`*

# 多人同步、移動、方向與動畫 — 第一優先規格

本文件是 SGTest 最重要的實作契約。初版根據現有 SGO 分析文件與 Godot 源碼/業界同步設計；原版逐函式稽核尚需 TODO A002 完成，未知部分不可假裝已確認。

## 1. 舊版證據（不可誤讀）

參考 TestGoGo docs/multiplayer-legacy-movement-audit.md、docs/legacy-movement-parity-20261111.md、docs/remote-player-hit-motion-20261111.md：
- Paradise.exe.lst：CSGOChar::SetMoveloop / MoveStep、CSGOEnem::movelogic，顯示目的地/速度分離、客戶端持續位置步進，而不是等每一個位置封包直接跳點。
- 2004 Client 系統 Frame 參考50Hz；Core.dll.lst 的 Server Update 設定參考10Hz；**Update Tick ≠ 真實封包發送頻率**，沒有帶 timestamp 的原始封包擷取就不能確定 10 包/秒。
- 舊封包語意例：0xA106 player destination、0xA107 char destination、0xA108 enemy move、0xA10D authoritative position correction；新遊戲借用「Destination 與 Correction 分開」的設計思想，不沿用老協定值。
- 舊 Godot Client remote_player_motion.gd 利用 SpeedTime/已累積位置外推；remote_player_state_policy.gd 防止 Movement 取消攻擊/受擊；仍可能被 Server lock/DB queue 卡頓/外圍 AOI 影響。
- 精確 8方向/4方向動畫與轉向策略、封包方向欄位仍要做直接 .lst 與素材對照，**不能只因圖檔有八方向就推論 wire 協定**。

## 2. 訊息設計（初稿）

每包 Header：
 ProtocolVersion / ConnectionEpoch / MapEpoch / ServerTick / ServerTimestamp / StreamSequence / TraceId / MessageType
每個 EntityEvent：
 EntityId / EntityRevision / EventSequence / CausalityId（必要時）/ EntityKind
MoveIntent：
 EntityId / MovementSequence / SegmentId / StartX,Y / EndX,Y / StartServerTick / DurationTicks / NominalSpeed / Facing / PathVersion
MoveCorrection：
 EntityId / MovementSequence / CorrectedX,Y / CorrectedServerTick / Reason
Stop：
 EntityId / MovementSequence / X,Y / ServerTick / Facing / AnimationIntent=Idle
ActionStarted：
 ActionId / EntityId / ActionType(Attack,Hit,Skill,Death,Emote) / Facing / ServerTick / ClipKey / Duration / TargetId / InterruptPolicy
CombatResolved：
 CombatEventId / ActorId / TargetId / DamageOrMiss / NewHP / ServerTick / Sequence
Spawn：
 EntityId / EntityRevision / WorldState baseline / MapEpoch / InitialAction / InitialFacing / MovementSegmentIfActive
Despawn：
 EntityId / EntityRevision / MapEpoch / Reason
Teleport/MapTransfer：
 BarrierSequence / SourceMapEpoch / TargetMapEpoch / TargetPosition / SnapshotId

資料具備可驗證型別、長度、向量有效值、速度界限，不能接受 NaN/Infinity/跨圖版本倒退。Move 合併不跨 Spawn/Stop/Action/Teleport/Death/Despawn barrier；相依事件先於引用它的事件；對離線重連的 Server snapshot 提供最新完整 state。

## 3. 玩家優先時序與渲染

**權威 WorldState 與顯示位置分開**：
- 遠端玩家擁有最近 N 個具 ServerTick 的 path/position samples（初始 3–4 個；依 jitter p95 調整）。
- RenderTime = estimatedServerNow - interpolationDelay；初始可測 100–150ms，不直接固定為最終值。當有包可夾時依 server timeline 對兩個相鄰 sample 插值；沒有相鄰樣本時沿可信 velocity 限時外推（初始50–150ms上界可測）。
- 若收到的是移動目的地/路段，按 Server 開始時間與路段速度持續推進；不是每次收到包就重啟 tween，也**不是**用 WebSocket 接收 gap 當移速。
- 更新 position 時檢查 MovementSeq、MapEpoch、SegmentId，晚到/重複 Move 不得讓畫面回頭。大跳躍只對被認可的 Teleport/MapChange/Respawn 瞬移，錯誤過大須標 diagnostic。
- 客戶端做的是**只影響畫面**的 smooth/servo；攻擊距離、碰撞/撿物/怪物 AI 使用 Server Authoritative 座標。手感需要可選 Local Input Prediction + Server Reconciliation，但不能擅自預測傷害或拾取成功。
- 對外部玩家（非自己角色）優先保持連續位置；性能負載時先縮減掉落物畫面製作而非暫停 remote player updates。

## 4. 方向 Facing 是獨立一等狀態

每個 Entity：
- authoritative_facing（Server 動作或移動依據），visual_facing（映射到紙娃娃/朝向的可視值）
- moving_direction（路段向量），action_facing（攻擊/受擊/技能的臨時動作鎖）
- sprite direction index（4/8/更多按素材和 Old 直接查核），flip_h flag（對應圖集規則）
方向更新規則：
1. StartMove/Turn 或路段 heading 改變 → 設 Facing；不每次小幅浮點座標修正就反覆翻轉。
2. Stop 後保留最後合法面向。
3. Attack/Hit/Skill ActionStarted 可指定面向並鎖到規格化的 action end 或被更高優先事件打斷。
4. Late Move/Idle 只更新 motion state，不可重播/覆寫一秒內正進行的一次性攻擊/受擊動作。
5. 死亡/傳送等高優先事件可以打斷任何動畫；重生恢復 Spawn 的初始 Facing。
6. 本地/遠端/怪物/機甲不因移動速度快而加速動畫播放。此點已有 Paradise 直接證據：`CSGOChar::SetSpeed .text:004BF3B5` 只寫 world-speed 欄位，不呼叫 ACT action-speed；ACT speed 走獨立 `CELxnObj::SetActionSpeed` 路徑。clip rate 只能由 verified ACT timing + 明確 action-speed 語意控制，詳 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。

## 5. Animation State Machine（不可由 NetworkManager 直接設動畫）

分成：
- Locomotion：Idle/Walk/Run/Chase/Knockback（循環或持續狀態）
- Action：Attack/Hit/Skill/Cast/Emote/Death（一次性，ActionId）
- Overlay：名稱、傷害浮字、Buff 特效、HitFlash（獨立，不阻塞位置）
事件優先：Teleport/Death > explicit interrupt > one-shot attack/hit/cast > locomotion idle/walk。實際 Attack vs Hit 是 interrupt/mask 策略表，需以原始動畫語意及 UX 測試裁定，不能永遠丟棄 Hit。
ActionId 用來去重，同 Action 不重播。受擊/傷害必須分離：CombatResolved 改變即時 HP；HitVisual 可聚合/排隊以避免每幀重置，但不漏顯重大受擊。Miss 也需有對應可見事件/浮字。
Remote Player 始終維持移動位置計算，即便 Action 蓋過 Walk Clip，動畫結束後依 movement state 恢復 Walk/Idle。

## 6. AOI/Queue/Rate Policy

- Server 空間網格 + Enter/Exit 雙閾值；Client 向 Server 提供 Camera world view 四角（依實際 Canvas transform），加 margin/zoom buffer；Server 授權受限上限，防止惡意超廣訂閱。
- 視野擴張時先回完整 Entity Snapshot，再送後續 Move delta；於同 MapEpoch/EntityRevision 設 barrier；離開時 Despawn，不留下幽靈。
- Player / combat events 優先網路**及 Client 渲染處理**。需保證 per-entity causal ordering（Spawn before Move, Hit before Death/loot 等依 domain），不可在兩條佇列直接造成跨類亂序。
- Move 可以以最新同 entity+epoch coalesce，保留 SegmentStart+ServerTick/Durations；Stop/Correction/Teleport/Attack/Hit/Death 是明確屏障。
- 重要可靠事件（傷害、死亡、資產/交易、拾取結果）必送、去重、重連重同步；WebSocket 本身單連線可靠有序，但 Application 的多 queue/async tasks 仍須自行維持語意。
- 訂閱與存活世界狀態分離；遠方怪物可能不渲染，但 Server 仍保持正確 AI/交戰/資產期限（按遊戲設計）。

## 7. 典型出錯案例必測

1. 100ms Server move +60 FPS render：20人不同方向連續走，無每包原地停住/反覆變速。
2. 100/250/750ms + jitter/reorder/duplicate、同 physics frame 100包：位置不倒退、Facing 不閃爍。
3. Stop→Move 同批到達：Stop barrier 不丟、無假速度 sprint。
4. Walk→Attack→Move→Hit→Move→Death：每動作只播必要一次、HP 真值一致、Facing 正確。
5. 新玩家於 Camera 邊緣/AOI 擴張進入：Spawn position 是現在狀態，不由零點移過來。
6. MapSwitch→舊 Move/Attack/Drop 延遲到達：不得復活舊圖實體。
7. Monster 改目標/被牆擋/追擊中脫戰：路徑與方向一致，視覺與 Server 掉落位置不分裂。
8. FPS30/60/120、AFK/Unfocus/minimized：同時間封包/HP/位移/拾取進度不變。
9. 20bots+300怪+500掉落：玩家 visual latency 與 facing accuracy 不被低優先 visual spawn 壓住。
10. 連續受擊/普攻 MISS/致命一擊：其他玩家也看見正確傷害及死亡，不只技能有反應。

## 8. 外部研究參考（設計原理，非 Old 事實）

- Godot fixed vs render process：https://docs.godotengine.org/en/stable/tutorials/scripting/idle_and_physics_processing.html
- Godot RenderServer 低階物件：https://docs.godotengine.org/en/stable/tutorials/performance/using_servers.html
- Godot CPU optimization：https://docs.godotengine.org/en/stable/tutorials/performance/cpu_optimization.html
- Valve Source SDK interpolation historical implementation：https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/interpolatedvar.h

上述文件說明可行原理，不表示特定數值適合本遊戲；要經 CI + 真 Client 實測。

---

<a id="source-normalized-animation-definition-md"></a>

## NORMALIZED_ANIMATION_DEFINITION.md

*Historical source: `docs/NORMALIZED_ANIMATION_DEFINITION.md` — SHA-256 `6fa9c80b5d80228f3ee13aad12421a23615e0a4d561ebcea0be95d8fc157af3a`*

# Normalized Animation Definition v2（ASSET201）

日期：2026-11-14。狀態：**ASSET201 PASS / AS005 視覺逐格驗證仍 NOT VERIFIED**。

## 目的

把動畫語意從 Atlas 排版中抽離。Runtime 不應從 page/UV、圖片檔名或 Render FPS 推導行為；標準定義只描述：

- entity / kind
- behavior
- 8 logical facings
- source clip alias / mirror
- source frame / source index
- pivot / trim / pixel hash
- ACT timeline / loop / raw event
- character draw-order raw layers / expanded z-order slots

Atlas、page、region、streaming owner、GPU handle 屬於後續 ASSET202～204，不進此契約。

## Before → After

Before：
- 舊 runtime asset JSON 主要服務 Atlas，方向 alias、ACT timing 與人物 z-order 分散。
- 9 個 Map13 monster 舊 runtime 與 reference-normalized facing mapping 合計有 182 個差異；不能把舊 alias 當 source of truth。
- 人物 definition 只有 `draw_order_key`，Runtime 若要排序仍需重新解讀 legacy `CharDrawOrder.dat`。

After：
- `tools/animation_definition.py` 輸出 `sgtest.animation` **schema v2**。
- 每個 character facing 同時保存 `draw_order_key`、原始 `draw_layers` 與驗證後 `draw_slots`。
- `Body+2 / L-Arm+2 / R-Arm+2 / Leg+2` 等雙槽位依已稽核規則確定性展開；未知 token fail closed。
- v1 definition 明確拒絕，避免缺 z-order 的資料靜默進入新 Runtime。
- `client/scripts/animation_definition.gd` 只讀 immutable definition；相同 clip + flip + draw metadata 才共享 descriptor。
- 動畫取樣使用 verified ACT fixed timeline，不依 Render FPS 或 world movement speed 累積。

## v2 核心資料

`frames[]`：
- `id`
- `source`
- `source_index`
- `size`
- `pivot`
- `trim`
- `pixel_sha256`

`behaviors[code]`：
- `legacy_action_id`
- `act_key`
- `raw_loop_flag`
- `loop`
- `timeline[]`：`frame_index / raw_time / default_ticks`
- `events[]`
- `facings[1..8]`

每個 facing：
- `clip`
- `source_facing`
- `flip_h`
- `draw_order_key`
- `draw_layers`
- `draw_slots`

Monster / single-view 沒有人物 z-order 時，draw 欄位必須為 null；不能混入半套資料。

## 真素材驗證

`tools/audit_normalized_definitions.py` 以 `SGTest/.cache/client` 的私有原始素材重建：

- 9 個 Map13 monster
- Boy 6 個 core paper-doll parts
- Boy explicit single-view death
- 共 16 definitions
- 992 logical facings
- 624 character draw-order views
- `normalized_z_order_verified=true`
- 0 schema failure
- 舊 runtime facing mapping 與 normalized reference 有 182 個差異，完整保留在本機 audit report，不自動接受舊值

另以 `tools/audit_character_standard.py` 對四種族 core 資料重跑：

| Race | Sources | Logical states | Issues | Missing draw-order |
|---|---:|---:|---:|---:|
| A | 282 | 104 | 0 | 0 |
| B | 282 | 104 | 0 | 0 |
| G | 282 | 104 | 0 | 0 |
| R | 235 | 104 | 0 | 0 |

這證明 core source/action/facing/draw-order 契約可確定性重建；**不等於原版畫面逐格視覺已驗證**。

## 回歸

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
cd tools
py -m unittest test_animation_definition -q
cd ..
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_animation_definition.gd
py tools/audit_character_standard.py .cache/client local-assets/player-core-audit.json
py tools/audit_normalized_definitions.py --source .cache/client --runtime-assets client/assets --output reports/normalized --report reports/normalized_definition_audit.json
```

本次結果：
- `tools/tests`: 89 PASS
- `test_animation_definition`: 10 PASS
- Godot normalized definition consumer: PASS
- normalized true-asset audit: 16/16 PASS
- four-race core audit: complete=true

## 邊界

- AS005：原版同版本畫面、pivot、mirror、alpha、紙娃娃實際遮擋逐格比對仍未完成。
- AS008：原版媒體公開發布權仍阻塞；原始/衍生圖片不提交 Git。
- v2 目前是資料契約；舊 Runtime atlas 不直接被「就地升級」。ASSET202/203 會用同一 v2 definition 比較 Atlas 候選，ASSET204 才建立正式 Runtime store。
- 182 個 old-runtime direction differences 不是自動修圖結果；它們代表新 Runtime migration 必須採 reference-normalized mapping 並保留視覺驗收 Gate。

---

<a id="source-perf002-memory-md"></a>

## PERF002_MEMORY.md

*Historical source: `docs/PERF002_MEMORY.md` — SHA-256 `cfd142b7e9224584278ba0005c58622bb02155276f99a3a949978f0e8b094d9a`*

# PERF002：Runtime 記憶體與實際 Windows release

2026-11-15。工作負載固定為 Map13、1440×900、Render 60 FPS、20連線（1個真視窗＋19個headless）、300怪／1000掉落、40秒，暖機2秒、frame取樣35秒。RTX3050／Compatibility OpenGL。不是20個有畫面的Client；30～90 FPS與背景隔離完整矩陣沿用既有RUNTIME206，本次沒有重測或改動其頻率。

## 引用與診斷的實際修正

修改前弱引用probe證實：載入後worker仍保留最後1個CPU Image；owner釋放／purge後仍有1份metadata及1個Image，直到worker close才消失。原因是永久解碼迴圈的函式frame保留區域變數。改用每job、每page會返回的函式後，GPU提交完成的CPU Image為0，owner釋放後metadata／Image均為0，worker不必退出。證據：`reports/perf002_lifetime_before.json`、`reports/perf002_lifetime_after.json`。

GPU idle poll原先每幀追加telemetry，長時間掛機會持續增長，diagnostics也複製該陣列。現在idle不記樣本，decode／submit／commit各保留最近2048筆及累積計數；一般diagnostics只讀summary。probe覆蓋10000次idle、ring覆寫／順序／累計與summary省略陣列；基準工具若ring不足以涵蓋區間會明確失敗，不以截斷資料宣稱完整。

## Atlas 與打包實測

下表RAM使用Windows `peak_wset`，含啟動尖峰；取樣RSS另存原始報告，不再以低頻取樣低於200MiB就宣稱峰值通過。程序樹peak加總是保守上限，各程序峰值未必同時發生。

| 執行方式／Atlas | OS峰值 MiB | Frame P95／P99 ms | Draw Calls P95 |
|---|---:|---:|---:|
| 開發引擎2048，修正引用後三次 | 221.80～224.00（FAIL） | 19.141～19.473／20.468～21.127 | 250～291 |
| release2048，早期打包對照 | 217.08（FAIL） | 18.968／19.772 | 271 |
| release2048，loose對照 | 198.27 | 18.607／19.031 | 269 |
| release2048，同版packed對照 | 199.80 | 18.484／18.874 | 248 |
| release1024，loose對照 | 185.43 | 18.509／19.132 | 285 |
| release1024，packed第1次 | 189.11 | 18.535／19.155 | 298 |
| release1024，packed第2次 | 184.95 | 18.602／19.294 | 307 |
| release1024，packed第3次 | 185.55 | 18.459／18.863 | 312 |

原始JSON位於`reports/perf002_after*`、`reports/perf002_release*`。波動本身需保留，不能只挑199.80而刪掉217.08。1024在packed／loose均較低，三次packed均低於200MiB且frame通過25／40ms及10%退化門檻。Draw Calls較多，不能宣稱所有指標改善；實際軌跡／可見數受排程影響，Draw Calls不是固定畫面逐像素A/B。相同seed、實體量、素材語意及解析度沒有下降。

Map bundle由2頁變5頁：18,569,428→18,572,476 RGBA bytes；session由1頁變2頁：4,390,912→4,428,064 bytes。Engine texture約46.04→46.08MB，增加很小。降低的是觀測到的程序RAM；較小page的decode／driver工作空間是推論，未定位分配器，不聲稱量到了實體VRAM。release的Godot static memory counter為0（不提供該診斷），不能解讀為零記憶體。開發引擎仍超200MiB，與正式release分開記錄。

決策：僅Runtime bundle優先512／1024候選，在5% RGBA容忍範圍內減少頁數；若原SPR atomic block放不下，使用最小有效較大尺寸2048或4096。512可容納且成本合適仍可選；不強制全部1024、不同方向不要求同頁、同原SPR不拆頁。離線archive維持既有策略。map／session／on-demand仍是唯一公開生命週期分類。

1024候選30次map async載／卸（另2次暖機）PASS：frame P95 16.826／P99 16.923ms，texture handles、engine texture、Store resident與RSS回收通過。原始記錄見`reports/perf002_1024_lifecycle/lifecycle.json`。正式預設由原版來源重建後，15個raw檔通過打包SHA檢查：release峰值187.09MiB、60.003FPS、frame P95/P99 18.549/19.200ms、Draw Calls P95 244、Client CPU中位數24.6%一核心。正式map async30次load P95 130.829ms、decode P99 22.048ms、GPU submit P95/P99/max 1.506/1.591/1.649ms、frame P95/P99/max 16.822/16.971/17.083ms；回收後RSS最後10次對最初10次中位數增加0.707MiB、全部回收gate PASS，見`reports/perf002_production/summary.json`。這是map資源串行載卸測試，非完整登入登出或任意切圖取消風暴；GPU sync wait未強制量測，仍NOT VERIFIED。

## 可重跑命令

```powershell
python tools/asset_converter/cli.py build 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --runtime-only
python tools/build_client.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/benchmark_runtime_baseline.py .cache/client-release/SGTest.exe --case multiplayer --exported-client --output reports/perf002_production
python tools/benchmark_runtime_baseline.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --case lifecycle --loader async --output reports/perf002_production
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --path client --script tests/probe_asset_store_lifetime.gd -- --assert-released --report=C:/SGO/SRC/SGTest/reports/perf002_lifetime_after.json
```

`build_client.py`從任何目錄執行，使用repo內preset及templates，實際建立EXE＋PCK後啟動release檢查raw PNG／JSON／BIN的SHA及完整runtime source inventory。基準測量前重做檢查，拒絕舊PCK。raw PNG維持原始bytes，由Runtime有界decode／GPU提交，不進Godot貼圖importer。可用`--assets <候選根目錄> --output <本機exe>`建立相同storage的候選。build artifacts留.cache；原版／衍生媒體與PCK不上Git。

首次需官方Godot4.7.2 templates：從[官方release](https://github.com/godotengine/godot/releases/tag/4.7.2-stable)下載`Godot_v4.7.2-stable_export_templates.tpz`及SHA512-SUMS，核對SHA512為`ca4d71c4d7b81dfc15d1a98baa07534aa95b03fdda78a0075b06672e1648d2e5f40980c9adc28d23e1b92e732ee7bf3461997aa804af74ec2fcd7a93ccb84079`，只將`templates/windows_release_x86_64.exe`與`templates/windows_debug_x86_64.exe`解壓至`.cache/godot-templates/`。helper另驗證兩個template SHA256，版本不符會停止；不改全機安裝。

尚存範圍：完整Recovered功能、原版Golden視覺與公開媒體權利沒有因此完成；physical driver VRAM仍NOT VERIFIED。此輪串行30次不能推論任意取消情境；後續獨立 [PERF003](PERF003_CANCELLATION.md) 已重現並修正取消queue／部分載入帳目問題，另有自製fixture及native回歸。

---

<a id="source-perf003-cancellation-md"></a>

## PERF003_CANCELLATION.md

*Historical source: `docs/PERF003_CANCELLATION.md` — SHA-256 `b839df3721ad2cc1786a737456a23a8441d698a916ef8b6b13cb85bdc584cf3b`*

# PERF003：取消佇列與部分載入回收

2026-11-15。延續919a7c8的正式release記憶體驗收，補充串行30次載卸沒有覆蓋的取消／失敗邊界。

## 重現與修正

| 實際重現 | Before | After |
|---|---:|---:|
| worker忙碌時連續1000次acquire／release | entry=0但queued jobs=1000 | queued jobs=0 |
| 兩頁fixture第一頁上傳後取消 | entries=0但resident帳目殘留4096bytes | resident=0、Texture弱引用存活=0 |
| 第一頁上傳、第二頁不存在，失敗後釋放 | entries=0但resident帳目殘留4096bytes | 失敗立即resident=0；釋放後仍0、Texture存活=0 |

Fixture是自製32×32白色圖片，不依賴原版。Before原始JSON是`reports/perf003_before.json`；After headless與native是`perf003_after.json`、`perf003_after_native.json`。Native After另修復同一manifest後重新載入實際圖片，兩種路徑都能成功重試／清回0；不可由此推論Before一定有實際Texture洩漏，Before直接證明的是帳目錯誤及queue殘留。

實作維持一個decoder、主執行緒GPU提交及原本network／logic排程。mutex保護active token集合、待解碼queue與ready queue；取消時一起移除。publish在相同mutex內確認token仍有效，拒絕晚到結果；job／page開始也確認token，不繼續解碼舊map剩餘頁。已開始的單頁PNG decode不做不安全中斷。成功最後一頁及錯誤處理都結束token。所有淘汰／取消／錯誤共用page引用及resident帳目清理，沒有以`max(0,...)`掩蓋負帳目。

零byte的metadata／error事件同樣受128-item上限約束，避免只限制Image bytes。單張超出ready budget的page只在queue空時進入；worker另可持有一個in-flight page，不能把ready budget稱為整個程序RAM上限。

## 回歸與實測

`test_runtime_asset_cancellation.gd`連跑三次PASS：128 live jobs時拒絕新增、取消1000 generations沒有殘留、失效token不能publish、priority順序正確、128個零byte事件也有backpressure、等待Image byte budget時取消不阻塞下一owner、只提交新generation三頁、close在fixture中<500ms及同Store restart。這個500ms結果只屬自製fixture，未承諾任意超大PNG可被即時中斷。

實際Windows release重建並核對source／raw asset SHA後，1440×900／20連線（1視窗+19headless）／300怪／1000掉落／40秒：59.997FPS、OS peak RSS184.88MiB、frame P95/P99/max18.884/19.573/21.288ms，Client CPU中位數27.9%一核心。對前一正式版本P95/P99增幅約1.8%／1.9%，低於10%門檻；不能宣稱單次差異是CPU變快或變慢。

30次map async載卸（另2次暖機）：load P95 131.832ms、GPU submit P95/P99/max1.433/1.775/1.885ms、frame P95/P99/max16.774/16.908/17.202ms，handles／texture／Store／RSS／static recovery全部PASS；卸載後最後10次與最初10次RSS中位數差0.195MiB。原始JSON與摘要位於`reports/perf003_production/`。既有monster/scenery/paper-doll/shared-store回歸及native Image／metadata lifetime probe PASS。

另提供`tools/benchmark_asset_worker_idle.py`，以同一headless程序、10FPS、無world／network／圖片，分開測0／1／8個idle decoder，包含Windows console launcher及真正engine child，使用OS CPU累積時間。短窗口小值可能量化為0，不能解讀零CPU；此實驗不是8個遊戲Client，也不證明多開CPU改善。原始記錄`reports/perf003_idle.json`；worker目前仍使用既有idle polling，本次沒有宣稱idle CPU最佳化。

## 重跑

```powershell
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script tests/test_runtime_asset_cancellation.gd
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --path client --script tests/probe_asset_cancel_queue.gd -- --assert-bounded --report=C:/SGO/SRC/SGTest/reports/perf003_after_native.json
python tools/benchmark_asset_worker_idle.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/build_client.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/benchmark_runtime_baseline.py .cache/client-release/SGTest.exe --case multiplayer --exported-client --output reports/perf003_production
python tools/benchmark_runtime_baseline.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --case lifecycle --loader async --output reports/perf003_production
```

本次沒有修改同步頻率或重跑全部RUNTIME206矩陣；physical driver VRAM仍NOT VERIFIED。完整Recovered地圖傳點／NPC／裝備等功能與AS005原版人工Golden／AS008公開媒體權利仍未完成。

---

<a id="source-runtime-direction-and-motion-md"></a>

## RUNTIME_DIRECTION_AND_MOTION.md

*Historical source: `docs/RUNTIME_DIRECTION_AND_MOTION.md` — SHA-256 `b971cf8789538807906b09383fff71a5b7dad3b3bee1140d2b3342a30f2bfb0b`*

# Runtime 方向與移動呈現契約

日期：2026-11-14。狀態：**DIRECTION RUNTIME FIX IMPLEMENTED / LIVE VISUAL HUMAN CHECK STILL REQUIRED**。

## 問題

使用者實機觀察：人物與怪物方向錯誤，移動會卡頓、順移，尤其畫面外圍更容易看到。

這不是單一 Server Heading 數字錯誤。Server 的八方位數值本來就是：

`+X=6, +X+Y=7, +Y=8, -X+Y=1, -X=2, -X-Y=3, -Y=4, +X-Y=5`。

Recovered 的 `GameConstants.DIR_STRINGS = [6,7,8,1,2,3,4,5]` 與 Server 一致。真正缺口有三個：

1. 舊 `EntityAtlas` 只在 requested clip 不存在時才查 alias；若 physical clip 剛好存在，就會蓋掉原版 action-specific DirectionMapping。
2. remote snapshot 每次都直接覆寫 facing，走路邊界 jitter 與 attack/hit snapshot 交錯會讓動畫反覆翻面。
3. Server AOI 固定 900px，小於 1440x1080 視窗加 Client cull margin 的最遠可視角落（約 1078px）；Actor 還在畫面邊緣時就可能 Despawn，之後重新 Spawn 到新座標，看起來像瞬移。

## 原版 / Recovered 證據

- 人物：`Paradise.exe.lst` `CSGOCharDrawLogic::DirectionMapping` `.text:004C263F～004C283A`；方位 5/6/7 重用 3/2/1 並鏡像，4/8 另依 action 分支。
- 怪物：`CSGOEnemDrawLogic::SetDirection` `.text:004CB708～004CB7E8`；不同 action index 對 2/4/6/8 的來源方位與 mirror 規則不同。
- 既有可重跑純函式：`tools/legacy_direction.py`。
- Recovered `client/scripts/game_constants.gd`：`DIR_STRINGS = ["6","7","8","1","2","3","4","5"]`。
- Recovered `monster_entity.gd::_direction_from_vector()`：45° sector，presentation 可用約 28° hysteresis 避免邊界翻向。
- Recovered `remote_player_state_policy.gd`：movement direction 不應在 attack/hit one-shot lock 期間覆蓋 action facing。
- Recovered `remote_player_motion.gd`：Server position 是 authority，但 visible position 用 bounded dead reckoning / catch-up，不重播每個封包已經過去的歷史路段。

## Runtime After

### 1. logical view 永遠優先於 physical clip

`tools/runtime_animation_semantics.py` 由 `sgtest.animation/v2` 編譯每個 requested logical clip：

- canonical physical clip
- `flip_h`
- verified ACT `timeline_ticks`
- loop
- full ACT key
- character draw-order metadata

`client/scripts/entity_atlas.gd` 先查 `logical_views[requested]`，只有沒有 normalized contract 的舊資產才 fallback 到舊 direct/alias 流程。

因此像：

- `EN010A_NS2` physical SPR 即使存在，logical facing 2 仍可依怪物原版規則重用 `EN010A_NS1`。
- `B-BODY_AS4` physical SPR 即使存在，人物 action AS 的 logical facing 4 仍依原版 `DirectionMapping` 使用 `AS3 + flip`。

目前 per-asset converter 直接在 manifest/definition 發布時保留 normalized animation semantics；舊 `apply_normalized_runtime_semantics.py` 與 lifecycle bundle rebuild 流程已移除。

### 2. authoritative facing 與 presentation facing 分離

`client_replica.gd` 同時保留：

- `facing`：Server authoritative protocol state。
- `visual_facing`：畫面使用的方向。

走路時 `visual_facing` 由實際 authoritative position displacement 推導，並用 28° hysteresis；idle 保留最後方向。可靠 attack/hit/death event 取得 presentation facing 所有權，movement snapshot 不會在 one-shot 中途翻面。

`legacy_facing.gd` 集中維護八方位 convention，local prediction、remote replica 與 attack debug vector 不再各寫一份方向公式。

### 3. remote position 不因 attack/hit 直接 snap

舊 `position_at()` 在 attack/hit active 時直接回傳最新 `state.p`，會在進入戰鬥瞬間把可見角色跳到 authoritative endpoint。

現在 attack/hit 保持同一個位置 timeline；只有 spawn / death / respawn 是 teleport barrier。Remote walk 使用 120ms interpolation delay + 最多 250ms bounded extrapolation，presentation speed capped 650px/s。這不修改 Server position、碰撞、攻擊距離或拾取判定。

z-sort 改用 `rendered_players/rendered_monsters` 的 y，而不是 authoritative endpoint y，避免 body 平滑但 draw-order 仍在封包節奏跳動。

### 4. AOI 覆蓋最大正式視窗

Client 最大視窗 1440x1080；現行 draw cull margin 為 x=100、y=160，所以最遠可見角落約：

`sqrt((720+100)^2 + (540+160)^2) ≈ 1078px`。

Server 改為：

- enter radius = 1100px
- already-seen exit radius = 1250px

Player、monster、drop 與 reliable action event 使用同一 bounded interest policy。這不是全圖廣播；exit hysteresis 只避免邊界來回 Spawn/Despawn。

### 5. 預設 Render FPS = 60

`client/project.godot` 設 `application/run/max_fps=60`。Physics/Client input/Server simulation 仍是 30Hz；Render FPS 不改網路、戰鬥與 authority 節奏。

## 回歸

目前 deterministic 回歸：

- Python `tools/tests`: 89 PASS。
- Python normalized/runtime semantic/report tests: 18 PASS。
- C# Release build: 0 warning / 0 error。
- C# server tests: 350 PASS（含八方位與 AOI enter/exit boundary）。
- Godot 8 組 headless tests 全 PASS：animation definition、directions、monster visuals、navigation、prediction、replica、runtime asset store、wire。
- `test_directions.gd` 實際檢查 Map13 `EN010A_NS2 -> NS1`，及人物 `B-BODY_AS4 -> AS3 + flip`。
- `test_replica.gd` 檢查 walking vector direction、28° hysteresis、attack event facing ownership、attack 不 snap、death 仍 snap。

### 20-client 真多人 smoke

目前工作樹以預設生成資產、RTX 3050、1440×900、60FPS cap，執行 20 clients／300 monsters／1000 drops／40 秒：

- execution PASS，`source_files_unchanged_during_run=true`。
- 平均 60.00 FPS；frame P95 23.53ms、P99 25.58ms、max 34.82ms。
- draw-call P95 414。
- 20/20 client `snapshot_received`、20/20 `prediction_ready`，0 `ERROR`／`SCRIPT ERROR`。
- prediction `max_correction_px=0`，observed `max_pending=7`。
- Server 啟動大量連線/世界暖機時 tick=300 曾出現 P95/P99 30.15/83.69ms；穩定後 tick=600/900/1200 的 P99 為 5.90/7.32/7.40ms。啟動尖峰保留為後續維運/載入優化項目，不把它算成穩態通過而忽略。

此 smoke 能證明新 AOI／presentation smoothing 沒讓一般多人 frame/tick 穩態爆掉，但**不能用自動數值取代使用者實機肉眼確認方向與順移是否完全消失**。

## 還沒有宣稱完成的部分

- AS005 原版同版本實機逐格畫面仍 NOT VERIFIED；這次是反編譯 contract + deterministic runtime regression，不代替人工視覺比對。
- 完整 30/45/60/75/90 FPS、5/10/15 掛機、失焦/最小化/恢復、1/2/4/8 開 trace matrix 仍屬 RUNTIME206。
- ASSET204 async Runtime store 正由另一個 Session 在同一工作樹實作；方向/同步修正不得降低 network/state 消化頻率。

## 重跑

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
cd tools
py -m unittest test_animation_definition test_runtime_animation_semantics -q
cd ..
.\.cache\dotnet\dotnet.exe build server\SGTest.Server.csproj -c Release
.\.cache\dotnet\dotnet.exe run --project server\tests\SGTest.Server.Tests.csproj -c Release
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_directions.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_replica.gd
```

---

<a id="source-runtime-isolation-verification-md"></a>

## RUNTIME_ISOLATION_VERIFICATION.md

*Historical source: `docs/RUNTIME_ISOLATION_VERIFICATION.md` — SHA-256 `20206701ba3c7d5e3c0f02592df409245857208b11989b5d015d095be50b060f`*

# Client Runtime Isolation Verification — RUNTIME206

日期：2026-11-14。狀態：**RUNTIME206 PASS / physical driver VRAM 與 120FPS 額外壓測仍非正式 Gate**。

## 1. 目標

Render FPS、低功耗、失焦、最小化與多開只能改變呈現成本，不能改變：

- Client fixed logic/input cadence
- socket RX/TX 與 packet reducer cadence
- ServerSimClock
- authoritative state reduction
- reliable action ordering
- Server simulation

正式一般遊玩 Render cap：30～90FPS，預設60FPS；標準選項30/45/60/75/90，自訂上限允許30～90。低功耗5/10/15是 **VisualClock**，不是 Engine/main-loop FPS。

## 2. Before：直接把 Engine 壓到5FPS會壞

最初 probe 直接 `--max-fps 5`：

- physics 平均仍約29.96Hz，但 Godot 在每個 render frame 內補跑多個 physics catch-up。
- socket→main reducer queue P95/P99 約180/181ms。
- max incoming depth 47。
- TX 約50.7Hz，因 input command 也被 catch-up physics burst 一起送出。

這正是低FPS時「畫面一格一格、遠端角色/怪物跳一下」的結構性原因。平均 physics=30Hz 並不能證明 fixed logic 與 Render 已隔離。

## 3. After：固定 Logic 30Hz，Visual 獨立節流

### 3.1 Engine/main-loop 下限

- `client/project.godot` 預設 `application/run/max_fps=60`。
- Runtime 強制：若外部/project cap 落到1～29，會拉回30。
- `--render-fps=N` 支援30～90。
- `--visual-fps=5|10|15` 只改世界呈現頻率，Engine/logic保持30。
- 直接再次故意傳 `--max-fps 5`：實際 physics/engine/visual均≈30Hz，input≈30Hz，queue P99 33.1ms，不再回到180ms burst。

### 3.2 ServerSimClock / ping

- `ServerSimClock.advance()` 已從 `_process()` 移到 `_physics_process()` fixed logic path。
- ping 也由 fixed logic排程，不再依 Render callback。
- Render只讀 `server_time`，不再推進模擬時鐘。

### 3.3 Presentation policy

- 正常前景：依 Render cap呈現。
- 手動 low-power：Visual 5/10/15，logic/network維持30Hz。
- Unfocus預設：世界呈現降到15FPS，不卸載資源。
- `--unfocus-low-power=0`：失焦仍維持正常呈現。
- Minimize：不論Unfocus開關，世界呈現停止；asset store、network、state reducer、fixed logic仍運作。
- `--no-world-render`：顯式停止世界 projection/redraw，不斷包、不卸載。

## 4. Runtime Trace

`--runtime-trace` 僅測試使用，記錄：

- physics / engine frame / visual update Hz
- input command Hz
- RX/TX packet rate
- socket receive→reducer drain queue age P95/P99/max與max depth
- snapshot/owner/action counts
- prediction seq/tick/pending
- authoritative own relative position
- window focus/mode
- Godot texture/video/static memory
- 每150 Server tick deterministic authoritative `state_checkpoint` hash

state hash只包含排序後的 authoritative players/monsters/drops；不含 render interpolation、VisualClock或GPU狀態。

## 5. 正式30/45/60/75/90：同 Server / 同 input / 同 state

同一台 Server 同時掛五個 Client，全部 `bot-index=1`，deterministic input sequence相同；Render cap 使用正式 Client `--render-fps=30/45/60/75/90` 路徑。共同 Server tick 150 / 300 / 450 的 authoritative hash **全部完全一致**。

| Cap | Physics Hz | 實際 Engine frame Hz | Input Hz | RX Hz | TX Hz | Queue P99 ms |
|---:|---:|---:|---:|---:|---:|---:|
| 30 | 29.997 | 29.997 | 29.997 | 20.998 | 30.998 | 27.894 |
| 45 | 30.004 | 45.009 | 30.004 | 20.999 | 31.005 | 20.287 |
| 60 | 29.999 | 59.995 | 29.999 | 20.998 | 30.999 | 33.024 |
| 75 | 30.003 | 59.988 | 30.003 | 20.999 | 31.003 | 22.742 |
| 90 | 30.003 | 59.992 | 30.003 | 20.999 | 31.003 | 21.270 |

75/90在本機實際只有約60FPS，是60Hz/VSync測試環境限制；軟體接受75/90 cap，但不把硬體受限的60FPS冒充成90FPS實測。最重要的fixed logic/input/RX/TX/state hash完全不受Cap影響。

## 6. Low-power 5/10/15 / no-world：同 Server state hash

同一 Server 同時掛 normal30、Visual5、Visual10、Visual15、no-world 五個 Client；共同 tick 150 / 300 / 450 state hash全部一致。

| Mode | Physics Hz | Visual Hz | Input Hz | RX Hz | TX Hz | Queue P99 ms |
|---|---:|---:|---:|---:|---:|---:|
| normal30 | 30.002 | 30.003 | 30.002 | 20.999 | 31.002 | 33.618 |
| visual5 | 29.999 | 5.000 | 29.998 | 20.997 | 30.998 | 33.319 |
| visual10 | 30.002 | 10.001 | 30.002 | 20.999 | 31.002 | 32.892 |
| visual15 | 29.999 | 15.000 | 29.999 | 20.997 | 30.999 | 30.141 |
| no-world | 30.003 | 0.000 | 30.003 | 20.999 | 31.003 | 30.202 |

因此低功耗是Presentation downgrade，不是降低Client↔Server同步頻率。

## 7. 真 Windows Unfocus / Minimize / Restore

使用非-console Godot executable取得真HWND，Windows API實際切換視窗狀態；不是只模擬旗標。

### Production預設

- Minimize成功：trace明確 `window_mode=MINIMIZED`、world visual≈0，physics/input≈30Hz；restore後回正常。
- Unfocus成功：`window_focused=false`、world visual≈15FPS，physics/input≈30Hz；restore後回正常。
- 前景baseline與背景Client同Server，tick150/300/450 authoritative hash全部一致。
- minimize/background兩Client queue P99均<34ms；unfocus同樣<34ms。

### Unfocus開關關閉

`--unfocus-low-power=0`：

- 真失焦仍維持約30FPS呈現，state hash一致。
- 真最小化仍停止world render，state hash一致。

因此「Unfocus可關閉、Minimize永遠生效」已有真視窗測試。

## 8. no-world重負載資源

300 monsters + 500 drops，30FPS engine：

| 指標 | 正常世界渲染 | no-world |
|---|---:|---:|
| Physics Hz | 30.000 | 30.041 |
| Visual Hz | 30.000 | 0.000 |
| RX Hz | 123.03 | 124.56 |
| Queue P99 ms | 33.04 | 32.92 |
| Client CPU（單核心%中位數） | 20.15 | 14.50 |
| Client RSS peak MiB | 205.12 | 204.55 |
| Texture monitor MiB | 43.48 | 40.27 |
| Video monitor MiB | 51.68 | 48.42 |

CPU中位數約下降28%，同步指標沒有下降。這符合「省渲染，不靠斷包/降logic省資源」。

## 9. 1/2/4/8原生Client worst-case多開

刻意關閉自動Unfocus，讓所有視窗都走60FPS呈現路徑；300 monsters + 500 drops。這比一般背景多開更嚴格。

| Clients | Physics範圍 Hz | Queue P99 max ms | CPU合計（cores%） | RSS合計 peak MiB | Texture counter sum MiB | Video counter sum MiB |
|---:|---:|---:|---:|---:|---:|---:|
| 1 | 30.000 | 33.289 | 37.06 | 203.68 | 42.35 | 50.55 |
| 2 | 29.993～30.004 | 33.307 | 70.06 | 403.99 | 83.16 | 96.03 |
| 4 | 29.989～29.997 | 32.912 | 187.78 | 807.66 | 166.32 | 192.05 |
| 8 | 29.987～30.019 | 33.301 | 333.65 | 1621.84 | 332.63 | 380.09 |

全部Client prediction/snapshot ready，0 Server error，reducer cadence沒有因多開掉速。CPU/RAM/Godot GPU counters近似隨Client數增加；這是實際成本，不隱藏。

## 10. Packet burst / reliable ordering

- synthetic 128-packet完整MessagePack burst：NetworkClient一次drain+decode **0.879ms**，遠低於2秒Gate。
- `test_replica.gd` 已驗證 stale snapshot不覆蓋newer reliable action、duplicate/stale action revision不回滾、death barrier不被舊snapshot復活。
- Transport為WebSocket/TCP ordered reliable path；同Server state-hash與live多人測試未出現packet/order ERROR。

## 11. Gate結論

RUNTIME206正式Gate：

- 一般30～90 Render cap：PASS（75/90實際render受60Hz/VSync硬體限制，logic/state仍PASS）。
- 預設60FPS：PASS。
- low-power Visual5/10/15：PASS。
- Engine cap<30防誤用：PASS，自動拉回30。
- no-world：PASS。
- 真Unfocus/Minimize/Restore：PASS。
- Unfocus toggle / Minimize always-on：PASS。
- same-server state hash跨FPS/low-power/background：PASS。
- queue P99 <50ms：PASS；正式矩陣最差<38ms，背景<34ms，多開<34ms。
- input fixed≈30Hz：PASS（0怪cadence/state-hash矩陣）。
- 1/2/4/8多開：PASS。
- 128 packet burst<2s：PASS。

所以 **RUNTIME206 可標完成**。

## 12. 保留限制

- Physical driver VRAM沒有直接driver counter；表中GPU是Godot各process的texture/video monitor，明確標示不是實體VRAM。
- 120FPS只保留額外壓測，不是正式支援Gate。
- cold-disk與不同GPU/螢幕refresh rate不是本次矩陣；75/90需要高刷新率/對應VSync策略才能實際觀察到>60 render FPS。
- AS005原版同版本逐格人工視覺與AS008發布權仍未完成，與Runtime isolation是不同Gate。

## 13. 重跑

```powershell
cd C:\SGO\SRC\SGTest

# 正常FPS同Server state hash
py tools/benchmark_runtime_fps_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18

# Low-power/no-world同Server state hash
py tools/benchmark_runtime_lowpower_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18

# 真Windows background + state hash
py tools/benchmark_runtime_background_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --seconds 18
py tools/benchmark_runtime_background_concurrent.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --disable-unfocus --seconds 18

# no-world resource A/B
py tools/benchmark_runtime_isolation.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --fps 30 --seconds 15 --monsters 300 --drops 500
py tools/benchmark_runtime_isolation.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --fps 30 --seconds 15 --monsters 300 --drops 500 --no-world

# 1/2/4/8 worst-case native clients
py tools/benchmark_runtime_multiopen.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --counts 1 2 4 8 --seconds 15 --monsters 300 --drops 500

# synthetic queue burst
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_network_queue.gd
```

## 14. Completion rerun（2026-11-14）

在正式標記 RUNTIME206 完成前，於目前 `main` 工作樹重新執行全部 Gate；以下數字是本次重跑，不以較早一次量測取代：

- deterministic regression：Python `test_*.py` 41 PASS；C# server 353 checks PASS；Godot 9 組 headless tests PASS；128-packet burst drain/decode 0.888ms。
- 30/45/60/75/90 同 Server：tick 150/300/450 authoritative hash 全一致；physics/input 29.967～30.001Hz；queue P99 最差 38.992ms。
- Visual 5/10/15/no-world：實際 visual 約 5.066/9.934/15.000/0Hz；physics/input 仍約30Hz；state hash 全一致；queue P99 最差 33.312ms。
- 真 Windows background：production minimize/unfocus 與 `--unfocus-low-power=0` 兩輪皆 PASS，restore 可觀察，state hash 一致；queue P99 最差約33.25ms。
- 故意 `--max-fps 5`：runtime guard 將 engine/physics/visual/input 維持約30.002Hz，queue P99 24.344ms，證明不能用低 Engine FPS 破壞固定邏輯。
- no-world 300 monsters / 500 drops：normal CPU 中位數 18.44% → no-world 10.07%（單核心百分比），texture monitor 43.48→40.27MiB；RSS peak 204.55→207.72MiB，該次 RAM 有噪聲，原樣記錄不宣稱下降。
- worst-case native multi-open（全部 `--unfocus-low-power=0`）：1/2/4/8 client 全 PASS；8開 physics 29.957～30.012Hz、queue P99 33.22ms、incoming depth 35、0 Server error；Client RSS sum peak 1619.04MiB、CPU sum median 384.79%。

### 玩家可用設定

現有 HUD 的「效能」區已直接提供 Render FPS 30/45/60/75/90，預設60；Unfocus 節能 checkbox 可開關。這些控制只改 `Engine.max_fps`／VisualClock presentation policy。Minimize 仍永遠停止世界呈現且不受 Unfocus toggle 影響；physics/input/network reducer/Server 固定30Hz。

上述 completion rerun 的 JSON 保留於本機 `reports/*_verify.json`（reports 為私有量測輸出，不提交原始媒體）。

---

<a id="source-security-operations-md"></a>

## SECURITY_OPERATIONS.md

*Historical source: `docs/SECURITY_OPERATIONS.md` — SHA-256 `16c601ea05ef391506ac3854a336045b3649c40166588a098cad527115cabbeb`*

# 資安、營運、GM、儲存與擴充政策

## 1. Session/防作弊/封包安全
WebSocket 用 TLS、認證 token/ConnectionEpoch、有效期、斷線即失效、心跳/重連握手、序列化上限、每包長度與欄位驗證、授權先於世界/資產動作。輸入視為不可信：速度/位置/攻擊距離/技能 CD/物品與金錢全部 Server 決定。
反重放：AuthNonce、requestId/operationId、角色 version，所有資產操作冪等。封包包含 server/client schema version，禁止舊版本隱形解讀錯欄位。

## 2. 玩家操作與攻擊驗證
每個帳號/角色有 command quota（移動、攻擊、聊天、拾取、交易分別配置）；過載進 bounded queue，重大事件不能被默默丟棄，需明確 backpressure/斷線/重同步策略。移動路徑/速度/阻擋由 Server 檢查，Client 只表示 intent；防飛天、瞬移、遠距攻擊。GM 指令獨立權限；不能單靠 Client 顯示 GM 選單就是管理者。

## 3. GM 與 Admin UI
管理功能：查看玩家/Map/Channel/Socket、倍率（分 EXP/Drop/Money）、公告、補償/救援、傳送、角色外觀/名字狀態、帳號封禁、健康指標及近期異常；全操作以 GM identity + reason + event ID 審計。
AdminUI 與 GameGateway 分離授權、URL 和角色；本機開發可限 127.0.0.1，部署需 TLS/VPN/Auth；不得讓公開匿名 /admin 改倍率、創造物品或執行任意 command。
Debug 與 Release 記錄級別分開；不可輸出 Token/密碼/SQL secrets。

## 4. 監控
低成本聚合 Metrics：各 Map Tick/P50/P95/P99、CurrentSessions/MapEntities、DB Query/CommitMs、WS Queue Age/SendGate/SocketWrite、Client PacketAge/DecodeBudget、Memory WorkingSet/VRAM/GC、Drop/Item/trade counters、Action errors、Spawn/Despawn/ghost。
每類事件保留 ring buffer 最近 N 筆、限量 telemetry；不要每60FPS寫 log。重要資產/GM 審計使用可查詢持久化紀錄，其餘 debug 可以採樣。

## 5. 備份與部署
- 開發：可重建隔離 SQLite fixtures；正式初版：使用 SQLite Online Backup API／一致快照、備份驗證與復原演練，不能只複製仍在寫入的主 `.db`；索引/資料 migration、schema compatibility 與 rollback plan 要版本化。未來換支援 PITR 的 provider 再制定對應備份策略。
- 新版 Client/DataCatalog 發布帶版本+hash、內容資產合法性清單、遠端版本最低需求。
- 單體部署後才依 MapActor/Channel 的真容量測試拆 Pod/多節點；跨實例 handoff 使用 ownership epoch/租約、Outbox/去重、防止雙重人物與怪物。
- 24h soak、故障注入：DB暫停、單個 WS 慢用戶、Server重啟、網路斷線、AOI 大量變更，須在負載中維持資產不重複/玩家恢復一致。

## 6. 外部素材/反編譯授權
SGO/TestGoGo repo 的公開可見不等於內容可合法複製/改作/發布。SGTest 原創實作、原創/合法授權資產、參考的資料與原始 ID 額外保留出處與權利稽核。不能先把舊 .lst/圖檔/音樂放公開 repo 再補查授權；該限制列為 P0 Gate。

---

