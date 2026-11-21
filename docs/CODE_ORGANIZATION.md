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
| Inventory/Equipment | `inventory/inventory_panel.gd` | `Identity/SqliteIdentityStore.*.cs` | UID／交易只由 Server 決定 |
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
- `Npc/NpcItemModificationHandler.cs` 合併「修理／強化」這一組共用登入世代、玩法鎖與物品 UID 結算生命週期的 command handler。由每個 authenticated socket 的 Gateway 注入**原 gameplayGate 與原 WorldResultRouter**，在各流程進行目前綁定／Actor／MapEpoch 驗證；它不建立第二個 queue、database 或新的 socket。
- `Npc/NpcEconomyHandler.cs` 負責 NPC 商店、個人倉庫、銀行三個既有可信命令流程。它們共享 NPC 地圖／距離驗證、Character／Account 錢物權威、ConnectionEpoch／MapEpoch 與同一 socket gameplay gate；原 DB owner、價格／存取規則、可靠 result 與 FIFO 不變。**不**合入 PlayerShop／Trade（需跨角色鎖與不同生命週期），也不與 ItemModification 合併成全能 `NpcManager`。
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
- `client/world/world_state.gd` 仍是唯一封包 Reducer 與快照 owner。選角成功後重設依「操作／戰鬥」、「背包／經濟」、「世界／PvP」三組方法；移動源碼但保留狀態更新順序。修理與強化 UID 位置驗證合用一條規則，不產生另一份可變遊戲狀態。
- `WorldState` 仍屬大型模組；日後拆純解析器或值物件需保留 MapEpoch、可靠序號與 Tick 的唯一寫入者。架構測試不得取代真 TLS、重連與多 Client 驗收。

## ClientSession 指令分派契約

- `ClientSession._process_first_intent` 只依 `intent.type` 分派到同一個物件持有的 `_process_<feature>_intent`；包括 Party、Chat、Quest、Trade、PlayerShop、PvP、銀行、倉庫與裝備等。仍然只在原有傳輸／Reducer 排程中處理 **一個 FIFO 隊首**。
- 同一隊首的消費只能使用既有 `_peek_intent`、`_pop_intent`、`_pop_intent_token`、`_replace_first_intent`；成功送出後仍須依原有 `WorldState.mark_*_sent` 更新 `pending sequence`，不能先移除後送出。
- Handler 不能新增獨立 socket、Thread、Timer 或第二條任務佇列；改變資格檢查、地圖 epoch、角色 actor binding、重播與退款邊界時必須有對應真 TLS E2E。
- 名稱採 `_process_player_shop_intent`、`_process_quest_intent`，保留 `kind` 參數只供同一系列下多種動作使用；避免單一 600 行函式長期累積業務邏輯。

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
