# 架構檔案 TODO

[回架構與現有目錄](ARCHITECTURE.md#實際目錄與入口)。此頁按預計責任列出未完成檔案或既有檔案的缺口。檔案樹裡的空白 `.todo` 標示預定落點與語言。進入開發時才改成正式腳本。未確定需要獨立程式的維護點直接列於此頁。`todo.txt` 記錄交付和驗證進度。

| 區域 | 檔案 TODO 或現有接點 | 開始拆分時的驗收 |
|---|---|---|
| Client Session 指令入口 | [x] `client/session/client_session.gd` 的巨大 `_process_first_intent` 按功能提取單一 FIFO Handler 方法 | 不新增 State/Queue/Transport；真 TLS PlayerShop、Quest、Trade、PvP 及社交回歸為驗收 |
| Client Session 入列互斥政策 | [x] `client/session/intent_admission_policy.gd` 整合原 `_queue_intent` 重複 pending／queued 擋單規則，FIFO 與導航取代仍在原 ClientSession | Godot 101 pending + 219 queued blocker 矩陣、Stop barrier、Token／Queue 上限、真 TLS NPC／Trade／跨圖、Linux CI |
| Client Session idle snapshot | [x] `client_session.gd` logic-thread 控制閘直接讀取 WorldState 欄位，僅首次進入世界／重連完成時發布 | 200 次閒置掃描完整快照建立僅 2 次，FPS 0/30/60/90、AOI、跨圖、重連、交易與真 TLS CI [PASS](https://github.com/kevin840307/SGTest/actions/runs/44907012903)；公開 snapshot／Queue／Wire 不變 |
| Client UI versioned snapshot | [x] `ClientSession.snapshot_since(last_revision)` 在同一 mutex 取得版本與深複製快照，`SessionPanel._process` 跳過無異動完整刷新，但保留技能待決 10 秒提示 | 快照隔離、登入失敗 Stub、重連版本、Godot 真 TLS/UI、雙人交易／跨圖、FPS Gate [PASS](https://github.com/kevin840307/SGTest/actions/runs/44907012903)；CPU／RSS 實際改善尚 NOT VERIFIED，既有 `snapshot()` API 不變 |
| Client Session Stop／追怪待決輪詢 | [ ] `client_session.gd` 兩條 `await_stop`／`await_move` 邏輯執行緒內只讀唯一 `_world`，避免完整 AOI `snapshot()` 複製 | 本機 Godot Stop barrier、追怪／手動取消、AOI Resync、0/30/60/90 FPS、單一 FIFO 互斥矩陣及新增 400 次待決輪詢無快照複製 PASS；完整 Client CI 待驗，不更改 WorldState／Server 權威 |
| Server SQLite Schema／Combat | [x] `Identity/SqliteIdentityStore.Schema.cs`、`.Combat.cs` 及 `IdentityContracts.cs` 承接獨立責任 | 保留 Schema v23／WAL／migration backup／combat transaction；Server 265 checks |
| Server Wire 分區 | [x] `WireProtocol.Combat.cs`、`.Identity.cs`、`.Inventory.cs`、`.Shop.cs`、`.World.cs` 按功能承接 Codec | V1 Opcode/Header 不變；Client 跨端 golden vectors 與 Server wire Gate |
| Client Wire Frame 基礎去重 | [x] `network/frame_codec.gd` 專管純 framing；`wire_codec.gd` 繼續持有唯一 V1 constants／opcode／payload | 37 個 Encoder 改共用 header writer、無個別 raw Header writer；固定與可變長度 fail-closed、golden bytes、World／Combat／TLS 回歸 |
| Server Auth binding | [x] `IdentityTlsAuthorization.Binding.cs` 承接身分＋World Actor 綁定 | 原密鑰／身分／Actor cleanup、真 TLS Auth／重連 E2E 保持 |
| Server Gateway 連線進出職責 | [x] `GatewayContracts.cs` 抽穩定可信命令契約；`GatewayConnection.cs` 保唯一綁定／Queue，`.Ingress.cs` 收包、`.ReliableOutput.cs` 回覆、`.AoiOutput.cs` 可靠可視事件 | 保留完全相同的 connection/binding gate、WorldReady ACK、MapEpoch、反壓入列、收發順序；Server 全量 checks、Godot 真 TLS 多端 |
| Server WorldResultRouter 事件／回覆分區及關閉 | [x] `WorldResultRouter.cs` 保留 12 條 bounded Channel 與唯一 Router；`.GameplayIngress.cs` 接收 gameplay／AOI 事件、`.ReliableReplies.cs` 單一鎖分派、`.WorldTicks.cs` 單一 Tick 消費者 | 先 Changed 再 Receipt、背壓 fail-closed；補齊 Identity／PvP 3 條 AOI Channel shutdown completion；Server 與真 Godot TLS、多端 E2E |
| Server TLS 指令 Worker 重複流程 | [x] `Gateway/TlsCommandWorker<T>` 承接 19 類一般命令同構的 bounded FIFO／單讀者／取消／錯誤處理；`TlsWireConnection` 保留原 Socket 與特殊 NPC／Skill Worker | 每類容量不變、只有原本的 21 個命令隊列／處理者、Worker 故障與正常取消測試、271 Server Checks、真 Godot TLS E2E |
| Server TLS Gameplay 回呼介面 | [ ] `TlsWireConnection.RunServerAsync` 仍有 21 個玩法 `handle*` 型別回呼；有證據顯示更改能降低耦合時才引入單一不可變的 dispatch 契約 | 不增加 Gameplay Gate／Route，不更動 MapEpoch／UID／Server authority，所有 TLS E2E |
| Server Gameplay Gateway 瘦身 | [x] `IdentityTlsAuthorization.cs` 保留連線生命週期及組裝；功能命令由對應模組處理 | 保留身分、MapEpoch、原先的鎖與回覆順序；Server TLS 全量回歸 PASS |
| Server Inventory 物品命令 | [x] `Inventory/InventoryCommandHandler.cs` 接手 Pickup、Equipment、Discard 三項可信 UID 操作；不新增第二個 inventory owner | 沿用同一 gameplayGate／WorldResultRouter、SQLite commit-first／AOI 掉落取消順序與可靠回覆；Server TLS、物品回歸及架構 Gate |
| Server 玩家復活與恢復／技能 | [x] `Combat/PlayerRecoveryHandler.cs` 掌管 Respawn／Recovery；`Skill/SkillCommandHandler.cs` 掌管一般技能 MP／可靠提交 | 同一 authenticated gameplayGate、Combat AI suspend／drain／resume、可靠結果先於傷害、SQLite 權威與 TLS 全量回歸 |
| Server NPC 修理／強化流程 | [x] `Npc/NpcItemModificationHandler.cs` 合併同一個 auth gate 下的修理與強化服務命令；Gateway 僅組裝與指派 | 共用同一 `gameplayGate`／`WorldResultRouter`，保留 UID／Actor／MapEpoch／來源驗證；Server Checks 267/267 PASS |
| Server NPC 商業服務 | [x] `Npc/NpcEconomyHandler.cs` 集中商店、倉庫、銀行的可信 NPC 操作；Gateway 保留組裝／授權入口，裝備穿脫獨立 | 同一 `gameplayGate`／`WorldResultRouter`／SQLite owner；267/267 Server Checks、跨端 TLS/UI、經濟類測試；不新增 queue |
| Server NPC 任務 | [x] `Npc/NpcQuestHandler.cs` 承接任務開啟、選項、權威拒絕／確認結果；和 NPC 金流分離 | 保留 QuestCatalog、SQLite 進度與唯一 auth gameplay gate；Server Quest TLS／Client Quest UI 回歸 |
| Server PvP 命令責任 | [x] `Pvp/PvpStateHandler.cs` 處理保護／白名模式與 GM 名稱；`Pvp/PvpCombatHandler.cs` 處理普攻／玩家技能。避免單一 590 行 PvP 巨類 | 同一 gameplayGate／Router、原 mode／attack／GM sequence gate、SQLite／Combat owner、PvP TLS／GM audit 回歸 |
| Client Reducer 分區 | [ ] `client/world/world_state.gd` 按 feature group 拆事件解析／處理，但保留唯一可重播 Reducer | 0/30/60 FPS、一致序號、重連／跨圖、可靠事件、TLS E2E |
| Client WorldReady 跨圖重設 | [x] `_reset_map_combat_and_item_commands`、`_reset_map_npc_services`、`_reset_map_social_and_movement` 在同一 reducer 依原順序整理 | 新 MapEpoch 清除 pending／AOI／NPC；保留持有背包裝備及 revision；舊圖忽略，同圖 resync 不重置；真 Godot 測試 |
| Client TLS 交易測試 ACK 競態 | [x] `test_client_trade_tls.gd` 兩端皆等 WorldReady ACK 完成，才開始可靠交易 | Linux CI `actor request` 早於 ACK 的拒絕；Godot×2 + SQLite 雙邊交易真 E2E 重跑 3 次 PASS，遊戲權限不放寬 |
| Client HP 權威序列去重 | [x] `_apply_authoritative_damage` 統一 PlayerDamage／PvpDamage 的 HP／序號／Tick 驗證；兩種 opcode 維持各自解碼及來源標記 | PvP→怪物→PvP 混合傷害、重播／跨來源跳號、HP 來源標記；Godot Wire 與真 TLS 回歸 |
| Client 選角重設／物品規則 | [x] `_reset_character_actions_and_combat`、`_reset_character_economy_state`、`_reset_character_world_state` 分組；修理與強化共用 `_item_matches_inventory_or_equipment` | 單一 WorldState；Bank／Storage／Repair／Enhancement／Trade／PvP／Skill／NPC Portal／AOI 及選角殘留清除共 10 組 Godot 回歸 PASS |
| Server Gateway 傳點及玩家商店 | [x] `Npc/NpcPortalHandler.cs` 管共享鎖下的跨圖協調；`PlayerShop/PlayerShopCommandHandler.cs` 管單連線重播及過期回覆 | 真雙 Client TLS 傳點與玩家商店交換 PASS |
| Client 聊天來源規則 | [x] `chat/chat_delivery_policy.gd` 純判定可見角色、隊伍、密語來源；世界 Reducer 保留順序、快照與有界歷史 | Chat Wire、Party Wire、FPS 獨立 PASS |
| Client App 驗收入口 | [x] 測試驅動移至 `client/tests/test_online_main_gate.gd`，由它實例化正式 `app/main.tscn` | 雙 Client 仍從正式場景進入，驗收失敗可重現 |
| Client NPC | [ ] `client/npc/npc_interaction_panel.gd.todo` 承接 App／SessionPanel 既有 NPC 選單與操作 | 開啟、取消與離開互動範圍，Server 仍驗距離及權限 |
| Client 社交 | [x] `client/party/party_panel.gd` 管邀請及隊伍；`client/chat/chat_panel.gd` 管頻道與訊息顯示 | 本機 Godot 真 TLS/UI 邀請、密語、聊天、隊伍快照與重連流程 PASS；Server authority 未變動 |
| Client 玩家商店 | [x] `client/player_shop/player_shop_panel.gd` 承接販售、瀏覽與購買 | Godot 真 TLS SessionPanel UI 回歸 PASS，Server UID／買賣雙方仍維持既有權威；私有 GPU 未驗 |
| Client 任務 | [x] `client/quest/quest_panel.gd` 承接任務開啟、選項、進度顯示 | 指令驗證仍由 SessionPanel 提供；真 TLS Quest UI 回歸 PASS |
| Client NPC 銀行 | [x] `client/npc/bank_panel.gd` 自有 UI 控制項、快照呈現及操作回饋；SessionPanel 僅保留 action callback | 真 Godot TLS/UI bank_open/deposit/withdraw、跨端 Server 權威回歸 PASS |
| Client PvP／GM 操作 UI | [x] `client/pvp/pvp_panel.gd` 接手模式切換、選人攻擊、技能目標及 GM 名稱狀態顯示 | Godot TLS/UI pvp_ui/pvp_attack_ui/gm_pvp_ui PASS；資格檢查、Sequence/MapEpoch 和 Server audit 留在原權威路徑 |
| Client NPC 經濟 UI | [x] `client/npc/npc_shop_panel.gd`、`client/npc/storage_panel.gd`、`client/npc/repair_panel.gd`、`client/npc/enhancement_panel.gd` 分別持有控件及快照；`SessionPanel` 僅注入唯一 `item_catalog` 與既有 submit callback | 真 Godot TLS/UI 商店、倉庫、修理、強化與銀行回歸 PASS；UID、金錢、成功率皆 Server-owned |
| Server 聊天 | [x] `server/src/SGTest.Server/Chat/ChatRouting.cs` 集中附近／隊伍／密語收件者選擇；TLS／命令校驗仍留 Gateway | 267/267 Server checks：跨圖密語／隊伍聊天、AOI 非訂閱隔離、過期 Binding、可靠回覆 PASS |
| Server／Client 寵物 | [ ] Server `Pet/PetRuntime.cs` 已掌管 durable roster/召喚/收回與 owner-scoped AOI identity；opcode116 提供 Server-owned movement delta。`WorldAoi` 已加入 one-tick authoritative trail baseline：Pet 不再 same-tick 鏡像 owner X/Y，而是下一個 World Tick 沿主人已通過 Server movement/Blocking 驗證的 waypoint 前進；transfer 立即 re-anchor 並清舊圖 target。Client `WorldState.pet_entities` 已接 spawn/delta/despawn generation gate，`pet/pet_presentation.gd` 已正式消費權威座標、共用 `PositionHistory` 做 bounded interpolation、MapEpoch／Pet generation reset，且不 extrapolate；`WorldView` 已接此 presenter。HK source PetID+EvolID→Enemy outlook 已由 `petcli.ITD` loader evidence CONFIRMED；目前新增 `sgtest.pet_definition_bridge/v1`，以 SGTest-owned `100001..100024` 明確對應 source PetID `5100..5123`，不直接重用原版 ID。`pet-runtime-catalog` 只接受已 audit evidence + 完整 runtime atlas，Client `PetVisualCatalog` / `WorldView` 依 AOI 可見 Pet 動態 acquire atlas 並注入 presenter；catalog 缺失仍維持既有 fail-closed 不畫，catalog 存在但格式/identity 不可信則拒絕載入。46 組原始 Enemy SPR+ACT 已通過 NS/FA source gate；private archive 已同步至 7,893 groups／75,591 SPR 且 missing/extra=0，363 組既有 runtime + Pet 聯集 publication PASS，46/46 Pet runtime atlases 再通過 production gate。`EN115A` 僅 NS/FA partial verified，ND1 全動作 normalization 仍 fail-closed | 召喚／收回、掉線、持久化、movement delta、one-tick authoritative trail 與 presentation position interpolation 已有回歸；DefinitionId bridge/runtime catalog loader 的 modern-ID isolation、exact evolution、fail-closed 已有 Python/Godot 回歸。Old Server 可確認 Pet 有獨立 OnUpdate/UpdatePet，但 HK 精確跟隨距離／速度／偏移／獨立 A* 仍 NOT VERIFIED；HK source visual identity 與 NS/FA source animation已有直接 evidence；NS/FA 作為 Pet idle/attack 仍僅 INFERRED；46 組正式 runtime atlas publication 已 PASS，但 Pet action 動態 A/B、EN115A ND1 全動作語意、戰鬥數值、成長仍待驗證與實作 |
| Server／Client 機甲 | [ ] Server `Mecha/MechaState.cs.todo` 預定掌管乘降與能力轉換；Client `mecha/mecha_presentation.gd.todo` 接角色畫面 | 狀態互斥、斷線回復、移動與裝備限制 |
| AFK／失焦 | [ ] 在 `client/app/main.gd` 和 `client/world/world_view.gd` 接生命週期。共用策略成形後評估 `client/afk/afk_presentation.gd.todo` | 收包不中斷、資產釋放與恢復、同機多開量測 |
| 全量資料 | [ ] 擴充 `tools/asset_converter/content/` 的現有入口及契約測試 | Converter 可重跑、缺料明確報錯、發行包內容一致 |
| 發行 | [ ] 擴充現有 Host、Client、`runtime_pack.py` 與 CI 入口 | 雙圖 MVP、全量地圖、多開、壓力與跨平台發行依 TEST_PLAN |
| 好友 | IN PROGRESS | schema v35 bilateral persistence、authenticated add/remove/refresh、online/offline/relog presence 與 Classic 真好友清單已完成；HK invite/accept、上限、分類/ignore、Original UI A/B 待驗 |

確立資料所有權後再新增檔案。常一起修改的規則放在一處；實際出現多組責任才加子目錄。Server 繼續由既有資料交易處理跨玩法原子性，Client 顯示快照並送操作意圖。
