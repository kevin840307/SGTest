# 架構檔案 TODO

[回架構與現有目錄](ARCHITECTURE.md#實際目錄與入口)。此頁按預計責任列出未完成檔案或既有檔案的缺口。列出的新路徑是命名建議，會隨第一個可執行切片調整。`todo.txt` 記錄交付和驗證進度。

| 區域 | 檔案 TODO 或現有接點 | 開始拆分時的驗收 |
|---|---|---|
| Client Session 指令入口 | [x] `client/session/client_session.gd` 的巨大 `_process_first_intent` 按功能提取單一 FIFO Handler 方法 | 不新增 State/Queue/Transport；真 TLS PlayerShop、Quest、Trade、PvP 及社交回歸為驗收 |
| Server SQLite Schema／Combat | [x] `Identity/SqliteIdentityStore.Schema.cs`、`.Combat.cs` 及 `IdentityContracts.cs` 承接獨立責任 | 保留 Schema v23／WAL／migration backup／combat transaction；Server 265 checks |
| Server Wire 分區 | [x] `WireProtocol.Combat.cs`、`.Identity.cs`、`.Inventory.cs`、`.Shop.cs`、`.World.cs` 按功能承接 Codec | V1 Opcode/Header 不變；Client 跨端 golden vectors 與 Server wire Gate |
| Server Auth binding | [x] `IdentityTlsAuthorization.Binding.cs` 承接身分＋World Actor 綁定 | 原密鑰／身分／Actor cleanup、真 TLS Auth／重連 E2E 保持 |
| Server Gameplay Gateway 瘦身 | [ ] `IdentityTlsAuthorization.cs` 仍有巨大 socket/gameplay 流程，逐步抽出經驗證的 domain command gate | 不新增可繞過身份／MapEpoch 的 dispatch 路徑；Server TLS 全量回歸 |
| Server Inventory 物品命令 | [x] `Inventory/InventoryCommandHandler.cs` 接手 Pickup、Equipment、Discard 三項可信 UID 操作；不新增第二個 inventory owner | 沿用同一 gameplayGate／WorldResultRouter、SQLite commit-first／AOI 掉落取消順序與可靠回覆；Server TLS、物品回歸及架構 Gate |
| Server 玩家復活與恢復／技能 | [x] `Combat/PlayerRecoveryHandler.cs` 掌管 Respawn／Recovery；`Skill/SkillCommandHandler.cs` 掌管一般技能 MP／可靠提交 | 同一 authenticated gameplayGate、Combat AI suspend／drain／resume、可靠結果先於傷害、SQLite 權威與 TLS 全量回歸 |
| Server NPC 修理／強化流程 | [x] `Npc/NpcItemModificationHandler.cs` 合併同一個 auth gate 下的修理與強化服務命令；Gateway 僅組裝與指派 | 共用同一 `gameplayGate`／`WorldResultRouter`，保留 UID／Actor／MapEpoch／來源驗證；Server Checks 267/267 PASS |
| Server NPC 商業服務 | [x] `Npc/NpcEconomyHandler.cs` 集中商店、倉庫、銀行的可信 NPC 操作；Gateway 保留組裝／授權入口，裝備穿脫獨立 | 同一 `gameplayGate`／`WorldResultRouter`／SQLite owner；267/267 Server Checks、跨端 TLS/UI、經濟類測試；不新增 queue |
| Server NPC 任務 | [x] `Npc/NpcQuestHandler.cs` 承接任務開啟、選項、權威拒絕／確認結果；和 NPC 金流分離 | 保留 QuestCatalog、SQLite 進度與唯一 auth gameplay gate；Server Quest TLS／Client Quest UI 回歸 |
| Server PvP 命令責任 | [x] `Pvp/PvpStateHandler.cs` 處理保護／白名模式與 GM 名稱；`Pvp/PvpCombatHandler.cs` 處理普攻／玩家技能。避免單一 590 行 PvP 巨類 | 同一 gameplayGate／Router、原 mode／attack／GM sequence gate、SQLite／Combat owner、PvP TLS／GM audit 回歸 |
| Client Reducer 分區 | [ ] `client/world/world_state.gd` 按 feature group 拆事件解析／處理，但保留唯一可重播 Reducer | 0/30/60 FPS、一致序號、重連／跨圖、可靠事件、TLS E2E |
| Client WorldReady 跨圖重設 | [x] `_reset_map_combat_and_item_commands`、`_reset_map_npc_services`、`_reset_map_social_and_movement` 在同一 reducer 依原順序整理 | 新 MapEpoch 清除 pending／AOI／NPC；保留持有背包裝備及 revision；舊圖忽略，同圖 resync 不重置；真 Godot 測試 |
| Client HP 權威序列去重 | [x] `_apply_authoritative_damage` 統一 PlayerDamage／PvpDamage 的 HP／序號／Tick 驗證；兩種 opcode 維持各自解碼及來源標記 | PvP→怪物→PvP 混合傷害、重播／跨來源跳號、HP 來源標記；Godot Wire 與真 TLS 回歸 |
| Client 選角重設／物品規則 | [x] `_reset_character_actions_and_combat`、`_reset_character_economy_state`、`_reset_character_world_state` 分組；修理與強化共用 `_item_matches_inventory_or_equipment` | 單一 WorldState；Bank／Storage／Repair／Enhancement／Trade／PvP／Skill／NPC Portal／AOI 及選角殘留清除共 10 組 Godot 回歸 PASS |
| Client NPC | [ ] `client/npc/npc_interaction_panel.gd` 承接 App／SessionPanel 既有 NPC 選單與操作 | 開啟、取消與離開互動範圍，Server 仍驗距離及權限 |
| Client 社交 | [x] `client/party/party_panel.gd` 管邀請及隊伍；`client/chat/chat_panel.gd` 管頻道與訊息顯示 | 本機 Godot 真 TLS/UI 邀請、密語、聊天、隊伍快照與重連流程 PASS；Server authority 未變動 |
| Client 玩家商店 | [x] `client/player_shop/player_shop_panel.gd` 承接販售、瀏覽與購買 | Godot 真 TLS SessionPanel UI 回歸 PASS，Server UID／買賣雙方仍維持既有權威；私有 GPU 未驗 |
| Client 任務 | [x] `client/quest/quest_panel.gd` 承接任務開啟、選項、進度顯示 | 指令驗證仍由 SessionPanel 提供；真 TLS Quest UI 回歸 PASS |
| Client NPC 銀行 | [x] `client/npc/bank_panel.gd` 自有 UI 控制項、快照呈現及操作回饋；SessionPanel 僅保留 action callback | 真 Godot TLS/UI bank_open/deposit/withdraw、跨端 Server 權威回歸 PASS |
| Client PvP／GM 操作 UI | [x] `client/pvp/pvp_panel.gd` 接手模式切換、選人攻擊、技能目標及 GM 名稱狀態顯示 | Godot TLS/UI pvp_ui/pvp_attack_ui/gm_pvp_ui PASS；資格檢查、Sequence/MapEpoch 和 Server audit 留在原權威路徑 |
| Client NPC 經濟 UI | [x] `client/npc/npc_shop_panel.gd`、`client/npc/storage_panel.gd`、`client/npc/repair_panel.gd`、`client/npc/enhancement_panel.gd` 分別持有控件及快照；`SessionPanel` 僅注入唯一 `item_catalog` 與既有 submit callback | 真 Godot TLS/UI 商店、倉庫、修理、強化與銀行回歸 PASS；UID、金錢、成功率皆 Server-owned |
| Server 聊天 | [x] `server/src/SGTest.Server/Chat/ChatRouting.cs` 集中附近／隊伍／密語收件者選擇；TLS／命令校驗仍留 Gateway | 267/267 Server checks：跨圖密語／隊伍聊天、AOI 非訂閱隔離、過期 Binding、可靠回覆 PASS |
| Server／Client 寵物 | [ ] Server `Pet/PetRuntime.cs` 預定掌管召喚、跟隨與成長；Client `pet/pet_presentation.gd` 接已確認的狀態 | 召喚／收回、掉線、跟隨、持久化與戰鬥回歸 |
| Server／Client 機甲 | [ ] Server `Mecha/MechaState.cs` 預定掌管乘降與能力轉換；Client `mecha/mecha_presentation.gd` 接角色畫面 | 狀態互斥、斷線回復、移動與裝備限制 |
| AFK／失焦 | [ ] 在 `client/app/main.gd` 和 `client/world/world_view.gd` 接生命週期。共用策略成形後評估 `client/afk/afk_presentation.gd` | 收包不中斷、資產釋放與恢復、同機多開量測 |
| 全量資料 | [ ] 擴充 `tools/asset_converter/content/` 的現有入口及契約測試 | Converter 可重跑、缺料明確報錯、發行包內容一致 |
| 發行 | [ ] 擴充現有 Host、Client、`runtime_pack.py` 與 CI 入口 | 雙圖 MVP、全量地圖、多開、壓力與跨平台發行依 TEST_PLAN |
| 好友 | 暫緩 | 依目前範圍保留在規劃 |

確立資料所有權後再新增檔案。常一起修改的規則放在一處；實際出現多組責任才加子目錄。Server 繼續由既有資料交易處理跨玩法原子性，Client 顯示快照並送操作意圖。
