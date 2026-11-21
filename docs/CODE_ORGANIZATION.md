# SGTest — 命名、模組分類與責任邊界契約

> 適用於唯一正式 main。這是目前程式碼的維護規範，不是要求新增空目錄或改寫所有舊功能。

## 設計原則

- 維持 **Godot Client + .NET Server + Python Converter 的功能型單體（modular monolith）**，以實際資料／責任所有權分類，不依技術名詞疊出 `Core/Manager/Service/Utils` 空階層。
- 分離 **使用者意圖 / Client 狀態 / Client 呈現 / Server 業務規則 / Persistence / Wire**；Client 不能自行結算位置、生命值、交易或物品 UID。
- 名稱描述「責任」，而非「誰呼叫它」。避免 `Helper`、`CommonManager`、`DataProcessor2` 等難以判斷 owner 的名稱。檔案過長是檢查訊號，不是必須拆分的充分條件。
- API／資料庫／Protocol 的語義 identifier 使用**穩定英文**；繁體中文僅用於使用者畫面、語系資源及文件。Server PvP 傳 `Blue/White/Pink/Red` 語義狀態，不傳依語系硬寫的顏色文字或 RGB 玩法規則。
- 新功能先擴充現有功能 owner，確有不同測試、資料來源或生命週期後才拆分。拆分不可產生平行的新 Session、WorldReducer、Socket、AssetStore、DB 或 Wire 協定。

## 命名規範

| 層 | 檔案／目錄 | 型別及方法 | 範例 |
|---|---|---|---|
| Server C# | PascalCase，功能資料夾 PascalCase | 型別與公開方法 PascalCase、參數 camelCase、私有欄位 `_camelCase`、async 方法 `Async` | `Trade/PlayerTradeCoordinator.cs`、`TryDecodeTradeSnapshot` |
| Server Wire | `WireContracts.cs` 唯一宣告 enum/DTO；`WireProtocol.<Feature>.cs` 分區實作同一個 partial class | `<Feature><Action>Wire`，codec `Encode*` / `TryDecode*` | `WireProtocol.Economy.cs`、`PvpAttackResultWire` |
| Client GDScript | 小寫 `snake_case.gd`，資料夾 snake_case | 變數／方法 snake_case、內部欄位／方法前綴 `_`、enum/常數 `UPPER_SNAKE_CASE` | `player_shop/player_shop_panel.gd`、`_refresh()` |
| Client 畫面 | 同功能目錄下 `*_panel.gd` / `*_presentation.gd` | Panel 接收唯讀快照並回送意圖，不直接接收 TCP bytes 或寫 WorldState | `quest/quest_panel.gd` |
| Converter Python | 小寫 `snake_case.py`、Python type PascalCase | 函式 snake_case，避免跨素材共享可變快取 | `content/build_maps.py` |

**範圍內名稱可以簡化**：在 `client/player_shop/player_shop_panel.gd` 內使用 `inventory_picker`、`seller_picker` 和 `status_label`，不必每個欄位再次加 `player_shop_`。跨模組公開 protocol、持久化欄位和功能 class 則保留明確領域名稱。

## 功能歸屬（目前實際檔案）

| 業務 | Client | Server | 權威與生命週期 |
|---|---|---|---|
| Session | `session/client_session.gd`、`session/session_panel.gd` | `Gateway/`、`Identity/` | ClientSession 唯一收送和 Reducer 排程；Gateway 認證綁定 |
| World/AOI | `world/world_state.gd`、`world/world_view.gd` | `World/`、`Visibility/` | Server 單寫者權威；Client snapshot 不依賴 FPS |
| Inventory/Equipment | `inventory/inventory_panel.gd` | `Inventory/InventoryCommandHandler.cs`、`Identity/SqliteIdentityStore.*.cs` | Gateway 發送可信指令，Inventory 驗證 Actor／UID／世代；SQLite 結算 |
| Bank | `npc/bank_panel.gd` | `Npc/`、`Identity/SqliteIdentityStore.Bank.cs` | 面板顯示餘額、呼叫既有 Session 意圖；款項提交與限額 Server-owned |
| NPC Shop | `npc/npc_shop_panel.gd` | `Npc/`、`Identity/SqliteIdentityStore.Shop.cs` | 商品價格／買賣／錢包由 Server 核准，不與 `player_shop/` 混淆 |
| Storage | `npc/storage_panel.gd` | `Npc/`、`Identity/SqliteIdentityStore.Storage.cs` | 僅選取 Server 快照中的 UID，整組存取由 Server 結算 |
| Repair | `npc/repair_panel.gd` | `Npc/`、`Identity/SqliteIdentityStore.Repair.cs` | 耐久／費用／扣款只由 Server 判定 |
| Enhancement | `npc/enhancement_panel.gd` | `Npc/`、`Identity/SqliteIdentityStore.Enhancement.cs` | 成功率／等級／扣款由 Server 決定，UI 不擲骰 |
| Trade | `trade/trade_panel.gd` | `Trade/PlayerTradeCoordinator.cs` | 雙方 gate + 原子 SQLite commit |
| Player Shop | `player_shop/player_shop_panel.gd` | `PlayerShop/` | UI 僅提交販售／查看／購買意圖 |
| Quest | `quest/quest_panel.gd` | `Npc/NpcQuestCatalog.cs`、`Identity/SqliteIdentityStore.Quest.cs` | 任務進度與合法選項由 Server 判定 |
| Chat | `chat/chat_panel.gd` | `Chat/ChatRouting.cs`、`Gateway/`、`Party/` | Gateway 驗證身份與序號，ChatRouting 依 AOI／隊伍／線上名稱路由可靠訊息 |
| Party | `party/party_panel.gd` | `Party/PartyCoordinator.cs` | 邀請、隊長、成員更新由 Server 決定 |
| PvP/GM | `pvp/pvp_panel.gd` 與原有名稱呈現模組 | `Pvp/`、`Gateway/` | UI 只選擇模式／AOI 玩家／技能 ID，授權與 PvP 攻擊／GM audit 由 Server 判定 |
| Skills/Combat | `session/` 意圖、`world/` reducer、`player/` 呈現 | `Skill/`、`Combat/`、`Monster/` | 技能規則、MP、HP、傷害及死亡 Server-owned |
| Wire | `network/wire_codec.gd` | `Wire/WireContracts.cs`、`Wire/WireProtocol.*.cs` | 唯一 V1 協定及 C#／Godot golden vectors |

## Client Wire V1 的 Framing／Payload 邊界

- **`client/network/wire_codec.gd` 是唯一 Client V1 契約入口**：`VERSION`、`HEADER_BYTES`、`MAX_FRAME_BYTES`、所有 `OPCODE_*`、功能封包長度與各 payload 編解碼仍在此宣告；外部 `WorldState`／`ClientSession`／`ClientTransport` 的既有 API 不改動。
- `client/network/frame_codec.gd` 專責一般 little-endian Frame Header 的讀寫、長度前綴及固定／可變長度驗證。它由 `WireCodec` 注入 V1 常數，**不重新宣告 Opcode、協定版本、封包大小或另一條 socket／佇列**。37 個既有指令 Encoder 由重複的 Header 建置改為呼叫唯一 `_writer`；只抽無狀態二進位格式，沒有新增玩法規則。
- 名稱 `FrameCodec` 是網路封包層職責，不是 `WireManager`。暫不為了分類把全部玩法 Codec 拆成 12 個僅剩轉接方法的檔案；等 payload 多到能獨立測試且不需要複製 Opcode／frame 契約時再按功能拆。驗收須保留跨語言 golden bytes、大小端、錯誤 header fail-closed、AOI／WorldReady、交易與 TLS。

## WireProtocol 物理分檔

- `WireContracts.cs`：所有穩定 Opcode／wire enum／DTO。變動值須有跨端測試，不能為重構重新編號。
- `WireProtocol.cs`：封包上限、共用 Header、基礎 World／戰鬥／登入及共用讀寫工具。
- `WireProtocol.Economy.cs`：倉庫、裝備、銀行、修理、強化。
- `WireProtocol.Social.cs`：交易、玩家商店、聊天、隊伍、玩家身分。
- `WireProtocol.Pvp.cs`：PvP／GM 封包，狀態仍是語義值。
- `WireProtocol.Quest.cs`：任務指令與對話快照。
- `WireProtocol.Combat.cs`：普攻、技能、傷害／復活以及戰鬥視覺事件。
- `WireProtocol.Identity.cs`：帳號登入／註冊、創角／選角協定。
- `WireProtocol.Inventory.cs`：背包、技能書、補品、丟棄和拾取。
- `WireProtocol.Shop.cs`：NPC 商店買賣和快照。
- `WireProtocol.World.cs`：WorldReady、AOI 實體和怪物快照。

所有檔案宣告同一個 `public static partial class WireProtocol`；只為程式碼導航分區，不是分成新的 wire 實作或傳輸層。共用 Frame header／限制／驗證仍在 `WireProtocol.cs`；不得在各功能分區複製 Opcode 或 Frame header 處理。

## Persistence 與 Gateway 責任拆分（2026-11-21）

- `Identity/IdentityContracts.cs` 唯一持有帳號、角色與持久化 CombatProfile 的穩定 DTO，不在資料庫實作檔內混合公開契約。
- `Identity/SqliteIdentityStore.Schema.cs` 集中原本約 734 行的 Schema v23 建立與遷移流程，包含遷移前備份及現有 WAL／單一 transaction；與 `SqliteIdentityStore.Backup.cs` 的備份程序分工。原操作順序未重寫。
- `Identity/SqliteIdentityStore.Combat.cs` 承接角色 CombatProfile、HP／MP 的原子資料存取。其他 `SqliteIdentityStore.<Feature>.cs` 已依功能分區，但**所有 partial 都是相同資料庫 owner**，不建立第二個連線池或交易所有者。
- `Gateway/IdentityTlsAuthorization.Binding.cs` 承接認證帳號與 World Actor 的綁定程序；`IdentityTlsAuthorization.cs` 仍持有現有 TLS socket 與唯一 gameplay gate，保留同一授權狀態及可靠事件順序。
- `Gateway/GatewayContracts.cs` 放置 Gateway 與玩法 Handler 共用的公開 `Trusted*Command`、`ConnectionBinding`、`GatewayConnectionOptions`、結果與 `GatewaySendStatus`；與既有 `IdentityContracts.cs` 的「契約獨立於實作」命名習慣一致，但**不**複製 Wire V1 opcode／binary DTO。
- `Gateway/GatewayConnection.cs` 保留一條已認證連線的**唯一** trusted binding、`_bindingGate`／`_receiveGate`、bounded `WireStreamParser`、`ReliableFrameQueue`、Close／可靠隊列消費；`GatewayConnection.Ingress.cs` 負責 Client Frame 接收、trusted command 映射、GameplayReady、WorldReady ACK／Resync；`GatewayConnection.ReliableOutput.cs` 負責一般可靠結果（NPC、錢物、技能、PvP、社交等）的驗證與編碼；`GatewayConnection.AoiOutput.cs` 負責 WorldReady、AOI Spawn／Delta／Despawn、視野紀錄與換圖可視清理，**都使用同一個 bounded reliable FIFO**。這是 C# 正常的 `partial class` 按 IO 責任分檔，不是新增另一個 Connection、AOI owner、Store、Queue 或 Thread。
- `GatewayConnection` 本身是 transport-independent，真正 TLS socket／登入授權仍歸 `TlsWireConnection`／`IdentityTlsAuthorization`；不應更名成 `GatewayManager`，也不把 `GatewayContracts` 埋回 2000 行的連線實作。AOI 輸出另成 partial 的原因是它唯一會在成功入列後更新可視集與 WorldReady ACK gate；與其他僅發送結果的 reliable method 有不同副作用與維護需求。
- `Gateway/TlsCommandWorker.cs` 將 TLS session 內原本 **19 組**完全同構的 bounded `Channel<T>`、`TryWrite`、單讀者、例外取消與關閉／等待工作合併為一種泛型 Worker；**實際上仍每個命令型別一個各自容量的 FIFO**（Respawn=4、Chat=32、其他=16），不新增集中式佇列、Socket、World 狀態或新 Gameplay Gate。`TlsWireConnection.cs` 保留 NPC 指令的可靠結果回覆與 Skill 在乾淨關線時排空的特殊生命週期，不強迫走相同取消行為。這個名稱比 `CommandManager` 更具體，類別位於既有 `Gateway/`，因 19 個真實消費者而非裝飾性拆檔。
- 此切片**沒有減少 TLS 入口的玩法型別回呼數量**，只是安全統一每命令的背壓／取消實作；未來若收斂 `RunServerAsync` 的大量 `handle*` 參數，應採窄且不可變的 per-session dispatch contract，不能製造第二個 Auth/MapEpoch 檢查路徑。縮行數不代表 CPU／GC／VRAM 已改善。
- `Gateway/WorldResultRouter.cs` 維持**唯一** `WorldResultRouter` 的連線索引、12 條 bounded Channel 建立、選角可信綁定／跨圖與統計；`WorldResultRouter.GameplayIngress.cs` 接收已驗證的技能／普攻、AI 視覺、復活及 Resync 等要求；`WorldResultRouter.ReliableReplies.cs` 在**原 `_gate`** 之下查找連線後轉發所有可靠結果，絕不自行補發或再次序列化；`WorldResultRouter.WorldTicks.cs` 唯一消費 Server World Tick、先保存 Changed 再送 Receipt、背壓即終止，維持原 `RunAsync` 單一消費者契約。全部是同一個 C# `partial` 物件而不是多個 Router 或 Channel owner。
- **Router 結束生命週期修正**：原來 `RunAsync.finally` 漏了 `PlayerIdentityUpdates`、`PlayerPvpCombatVisuals`、`PlayerPvpSkillVisuals` 三條 AOI Channel 的 `TryComplete(fault)`。現在和其餘 Channel 一起完成，避免正常停機或故障時下游讀取者持續等待；`WorldResultRouterChecks.SourcePump` 與 Python 架構測試保護這個契約。這項是實際關閉完整性改善，非只是縮減原檔行數。
- **命名判斷**：保留遊戲網路常用 `WorldResultRouter` 與 `GatewayConnection`，在既有 `Gateway/` 下用 `GameplayIngress`（已綁定的玩法事件，不是 socket 解碼）、`ReliableReplies`（世代驗證後派送）、`WorldTicks`（唯一 World 事件消費）分辨實際生命週期。不可合成 `WorldManager`、`MessageBus` 或新增獨立 AOI Queue；未證實的 FPS／CPU 改善不得因檔案變短而聲稱達標。
- `Inventory/InventoryCommandHandler.cs` 收攏「撿取掉落物、裝備穿脫、背包丟棄」三個物品 UID 操作；與 `Identity/SqliteIdentityStore.*` 的資料庫交易 owner、World/AOI 掉落可視狀態及 authenticated socket 的同一 `gameplayGate` 分工。成功撿取仍先 SQLite commit 再通知 AOI；拒絕時不消除掉落物。這是有三個實際職責的遊戲領域目錄，不是新增另一個 Inventory 狀態倉儲、可變世界或 `InventoryManager`。
- **Recovery 不合入 Inventory**：`Combat/PlayerRecoveryHandler.cs` 同時處理死亡復活和恢復道具：暫停／排空 Combat AI、HP／道具 DB 原子結算、先把可靠結果排入 FIFO 再恢復 AI；共用原 gameplay gate，但與單純 UID 轉移不同生命週期。
- `Skill/SkillCommandHandler.cs` 處理一般技能施放之權威 preflight、MP／戰鬥停駐、狀態基線與安全恢復；獨立於 `Pvp/PvpCombatHandler.cs` 的 PvP 玩家目標權限與傷害。兩者共用原 Server Combat／SQLite／Wire 契約，沒有新的 Skill Runtime／Socket／Queue。
- `Npc/NpcItemModificationHandler.cs` 合併「修理／強化」這一組共用登入世代、玩法鎖與物品 UID 結算生命週期的 command handler。由每個 authenticated socket 的 Gateway 注入**原 gameplayGate 與原 WorldResultRouter**，在各流程進行目前綁定／Actor／MapEpoch 驗證；它不建立第二個 queue、database 或新的 socket。
- `Npc/NpcEconomyHandler.cs` 負責 NPC 商店、個人倉庫、銀行三個既有可信命令流程。它們共享 NPC 地圖／距離驗證、Character／Account 錢物權威、ConnectionEpoch／MapEpoch 與同一 socket gameplay gate；原 DB owner、價格／存取規則、可靠 result 與 FIFO 不變。**不**合入 PlayerShop／Trade（需跨角色鎖與不同生命週期），也不與 ItemModification 合併成全能 `NpcManager`。
- `Npc/NpcQuestHandler.cs` 專責 NPC 任務開啟與選項提交。任務的規則來源 `NpcQuestCatalog` 與 SQLite 進度 owner 不變；它與 NPC 經濟的價格／資金交易不是同一領域，不應硬合併。仍使用經驗證的 Actor／MapEpoch／QuestSequence 與原 gate。此獨立檔有實質可信選項處理及狀態結果，非命名用 façade。
- `Pvp/PvpStateHandler.cs` 負責 PvP 可攻擊模式及 GM 名稱狀態、序號重播與可靠發布；`Pvp/PvpCombatHandler.cs` 負責普攻／玩家技能、幾何驗證、MP／傷害結算及戰鬥續行。兩者**共享原 authenticated socket 的 gameplayGate 和 WorldResultRouter**，分別持有原本每次連線的 mode／GM 與 attack sequence gate；PvpCrimePolicy、PvpMapPolicy、Combat／SQLite 仍是規則 owner。不建立第二個 PvP 狀態或交易通道。
- **命名與分類決策**：原規劃單一約 590 行 `PvpCommandHandler` 雖較 Gateway 改善，仍混合不同的權限／變更原因；改以常見遊戲領域名稱 `PvpStateHandler`／`PvpCombatHandler`，均留在現有 `Pvp/`，避免新增 PvP 狀態／戰鬥子目錄或更多 `Manager`。Public Wire enum 與封包名稱一律不為重構重新命名。
- **命名反思**：`NpcRepairCatalog`／`NpcEnhancementCatalog` 代表 NPC 位置與服務可用性；`RepairItemCatalog`／`EquipmentEnhancementCatalog` 則代表經驗證的物品規則。兩者不是重複的 Catalog，不應為了「合併」而混成一個 `NpcManager`。`NpcEconomyHandler` 比三個只有單一方法／單一目錄的 Handler 易導航，也比所有 NPC 功能揉成一大檔更有聚力。`IdentityTlsAuthorization` 的名稱確實低估了目前其他玩法職責，但必須先把玩法 Handler 徹底解耦，才適合變更公開類別名稱。
- 遷移／授權相關重構必須通過 Server 全量 checks（本輪 267/267）（尤其 SQLite upgrade、WAL backup、TLS Host、雙方交易與 Epoch），而非僅憑 C# 可編譯宣稱正確。

## NPC 相關 UI 分組決策（避免過度拆分）

- 五個 NPC 操作面板統一在 `client/npc/`，與 Server `Npc/` 功能語意對應。**合併的是檔案導航與 UI 功能領域，不是合併五種規則或一個大類別**。這些 Panel 都有不同顯示資料及互動流程；合併成 `NpcManager` 或 `EconomyHelper` 會增加耦合。
- `NpcShopPanel` 與 `PlayerShopPanel` 是不同業務；保留前者明確的 NPC 前綴，避免與玩家擺攤混淆。`BankPanel`／`StoragePanel`／`RepairPanel`／`EnhancementPanel` 是常見遊戲 UI 名稱，不改成模糊 `System` 或 `Manager`。
- 不要為單一 UI 面板建立單獨的頂層功能資料夾；當功能成長到需要獨立場景、測試、資產或多個協調模組，才考慮從 `npc/` 拆出。`ClientSession` 保留唯一意圖隊列，所有權威規則仍在 Server。

## Client UI 模組的最小契約

- `SessionPanel` 負責登入頁組裝、共用快照、UI 生命週期及必要的提交前驗證；功能面板不直接輪詢 Transport，也不可自行變更 WorldState。
- `InventoryPanel`、`BankPanel`、`NpcShopPanel`、`StoragePanel`、`RepairPanel`、`EnhancementPanel`、`TradePanel`、`PlayerShopPanel`、`QuestPanel`、`ChatPanel`、`PartyPanel`、`PvpPanel` 由各自目錄持有自己的 Control、顯示快取與事件回呼。
- 面板從 Session 收到 `apply_snapshot`（或已定義的窄回呼）；連線／換角色時 `reset`；所有成功／失敗狀態以 Server 回覆為準。
- NPC 經濟類 `*_panel.gd` 只持有 Controls、Picker、文字與 `Callable` action；`SessionPanel` 仍保留 `submit_<feature>_*` 的身分、Epoch／pending／UID 操作約束。`item_catalog` 由單一組裝入口注入，不在各 Panel 建立重複 Loader。
- 影響多人一致性的 `MapEpoch`、`ConnectionEpoch`、Reliable FIFO、UID/amount、交易 commit 與回放順序都不能因 UI 重構修改。

## Server 聊天與 Client Reducer 職責（2026-11-21）

- `server/Chat/ChatRouting.cs` 只接收經 Gateway 綁定的 `TrustedChatCommand`、現有 `WorldResultRouter` 及可選 `PartyCoordinator`。負責 AOI Nearby、Party 成員及線上角色名稱／ActorId 密語路由；**不**取得 socket、密碼或 Client IP，沒有第二個驗證入口或 queue。
- `client/world/world_state.gd` 仍是唯一封包 Reducer 與快照 owner。選角成功後重設依「操作／戰鬥」、「背包／經濟」、「世界／PvP」三組方法；換圖後的 `WorldReady` 重設另依「戰鬥／物品指令」、「NPC 服務」、「社交／移動」三組方法依**原順序**執行。注意兩種 reset 的契約不同：新角色需清空舊物品；新地圖需保留已持有的 `inventory_items`、`equipment_items`／slot 及 revision，僅關閉舊圖 pending／NPC／AOI。修理與強化 UID 位置驗證共用唯一規則，不產生第二份可變遊戲狀態。
- `WorldState` 將普通怪物傷害 `PlayerDamage` 與玩家 PvP 傷害 `PvpDamage` 的 HP／DamageSequence／ServerTick／MaxHP／不可憑空回血驗證統一於 `_apply_authoritative_damage`。兩種 opcode 各自解碼並維持原有失敗代碼，只有 PvP 對外追加 `source_kind=pvp`；同一角色不應有兩條 HP 序列或重播記憶。新測試以 PvP→怪物→PvP 交錯傷害確認。`WorldState` 仍屬大型模組；日後拆純解析器或值物件需保留 MapEpoch、可靠序號與 Tick 的唯一寫入者。架構測試不得取代真 TLS、重連與多 Client 驗收。

## ClientSession 指令分派契約

- `ClientSession._process_first_intent` 只依 `intent.type` 分派到同一個物件持有的 `_process_<feature>_intent`；包括 Party、Chat、Quest、Trade、PlayerShop、PvP、銀行、倉庫與裝備等。仍然只在原有傳輸／Reducer 排程中處理 **一個 FIFO 隊首**。
- 同一隊首的消費只能使用既有 `_peek_intent`、`_pop_intent`、`_pop_intent_token`、`_replace_first_intent`；成功送出後仍須依原有 `WorldState.mark_*_sent` 更新 `pending sequence`，不能先移除後送出。
- Handler 不能新增獨立 socket、Thread、Timer 或第二條任務佇列；改變資格檢查、地圖 epoch、角色 actor binding、重播與退款邊界時必須有對應真 TLS E2E。
- 名稱採 `_process_player_shop_intent`、`_process_quest_intent`，保留 `kind` 參數只供同一系列下多種動作使用；避免單一 600 行函式長期累積業務邏輯。

## ClientSession 入列政策（2026-11-21）

- `session/client_session.gd` 保留唯一 `_intents` FIFO、`_intent_token`、Mutex、Transport、重連清空、導航取代及隊首消費。指令入列的靜態互斥判定改由**同一 `session/` 目錄**的 `intent_admission_policy.gd` 提供純函式 `allows(kind, published, queued)`；它沒有 Thread、Mutex、Transport、WorldState 或另一份 Queue／State。
- `IntentAdmissionPolicy` 將經濟操作的共同佇列互斥清單，以及 Bank／Repair／Equipment／Storage／Shop／道具／PvP／GM／Quest 的不同 pending 需求集中在可測試的政策內。特別保留原先的不對稱行為：排隊中的 `shop_open`／`storage_open`／`bank_open` 不阻擋後來的 Recovery／Discard，而排隊中的 Recovery／Discard 仍阻擋新的 NPC 開啟操作。**這次是無功能變更重構，不自行改成對稱互斥**；往後若決定改善操作競態，要獨立需求及 E2E 檢驗。
- 名稱使用常見 Command Admission／FIFO 語彙 `IntentAdmissionPolicy`，不命名為 `SessionManager`、`QueueManager` 或 `CommonUtils`；也不將每種 NPC 經濟命令拆成額外資料夾。Godot tests 應驗 pending/blocker 全矩陣、原不對稱規則、Stop barrier、token 單調及佇列上限；Host/Server 依然握有權威結算。

## 尚未完成、不得混淆為已實作

- `client/world/world_state.gd` 與 `client/session/client_session.gd` 仍是大型責任集中點，需按 opcode owner 和可靠事件順序逐片重構，不能把單一 Reducer 拆成各自可亂序更新的物件。
- `server/Gateway/IdentityTlsAuthorization.cs` 的玩法 Handler 還可移至明確的協調模組，但需維持 authenticated gameplay gate 和跨角色交易鎖順序。
- `SqliteIdentityStore` schema 遷移應逐版獨立稽核，尤其 pre-migration backup、WAL／持久化設定與 crash recovery；禁止只有檔名漂亮卻改壞交易原子性。
- Windows 真正 1/2/4/8 開、AFK、GPU、CPU/RSS/VRAM、World Tick P95/P99 尚待完整驗證，不可拿 Linux headless 或 synthetic load 宣稱 Release PASS。

## 自動化驗收

`python -m unittest tools.tests.test_architecture_conventions -v` 驗證：
1. 功能面板是否放對目錄。
2. GDScript `snake_case` 命名。
3. `preload("res://...gd")` 來源路徑是否存在。
4. Wire enum 與 partial owner 唯一性。
5. C# 與 Godot 共用 Opcode 是否保持相同數值。

保留原本 Host Release、Server 全量 checks（本輪 267/267）、Godot UI/TLS、雙 Client／cross-map 等完整 Gate；命名測試 **不能替代** 任何遊戲行為和效能測試。

## 命名與分層再審查

| 名稱 | 決定 | 理由 |
|---|---|---|
| `Map`、`World`、`Visibility` | 保留 | 地圖提供地形與碰撞資料，World 擁有權威模擬，Visibility 管每條連線的 AOI。三者更新頻率及測試方式不同。 |
| `network`、`session`、`Gateway`、`Wire` | 保留 | Client 將 socket 與 codec 放在 network，登入、選角及重連在 session。Server Gateway 負責連線與派發，Wire 負責版本化封包。兩邊用語按實際用途。 |
| `Inventory`、`Npc`、`Trade`、`PlayerShop` | 保留 | 背包操作屬角色本人，NPC 服務依距離授權，Trade 依賴兩方確認，PlayerShop 依賴上架及購買生命週期。把 UI 放在功能旁，SQLite 交易仍由同一 owner 提交。 |
| `Handler`、`Policy`、`Coordinator` | 依副作用使用 | Handler 接命令，Policy 做純判定，Coordinator 負責跨玩家或跨流程的順序。 |
| `WorldState` | 保留單一 reducer | 無副作用的規則可移到功能模組，像 `ChatDeliveryPolicy`。訊息順序、復位與快照仍由同一處寫入。進一步拆分以可明確列出輸入、輸出的功能為單位。 |

本輪把 NPC 跨圖的戰鬥暫停、舊交易結束、商店清理和世代續接整理到 `NpcPortalHandler`。玩家商店的單連線重播處理移到 `PlayerShopCommandHandler`，結算仍由原 Coordinator 管理。Gateway 保留登入、斷線、授權和組裝。這兩個 Handler 沿用同一連線、路由與鎖，檔名直接指向實際責任。新的預留檔案以零位元組 `.todo` 放在預計位置。

Godot 的 `snake_case` 與相近功能放一起的做法可參考 [Godot 專案組織指南](https://docs.godotengine.org/en/4.7/tutorials/best_practices/project_organization.html)。

正式入口 `app/main.gd` 處理組裝、視窗與遊戲輸入。在線雙端驗收由 `tests/test_online_main_gate.gd` 實例化原本的 `main.tscn`。測試角色、環境密碼與測試里程碑各自在測試目錄維護。
