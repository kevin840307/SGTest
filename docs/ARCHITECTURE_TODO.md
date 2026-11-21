# 架構檔案 TODO

[回架構與現有目錄](ARCHITECTURE.md#實際目錄與入口)。此頁按預計責任列出未完成檔案或既有檔案的缺口。列出的新路徑是命名建議，會隨第一個可執行切片調整。`todo.txt` 記錄交付和驗證進度。

| 區域 | 檔案 TODO 或現有接點 | 開始拆分時的驗收 |
|---|---|---|
| Client Session 指令入口 | [x] `client/session/client_session.gd` 的巨大 `_process_first_intent` 按功能提取單一 FIFO Handler 方法 | 不新增 State/Queue/Transport；真 TLS PlayerShop、Quest、Trade、PvP 及社交回歸為驗收 |
| Server SQLite Schema／Combat | [x] `Identity/SqliteIdentityStore.Schema.cs`、`.Combat.cs` 及 `IdentityContracts.cs` 承接獨立責任 | 保留 Schema v23／WAL／migration backup／combat transaction；Server 265 checks |
| Server Wire 分區 | [x] `WireProtocol.Combat.cs`、`.Identity.cs`、`.Inventory.cs`、`.Shop.cs`、`.World.cs` 按功能承接 Codec | V1 Opcode/Header 不變；Client 跨端 golden vectors 與 Server wire Gate |
| Server Auth binding | [x] `IdentityTlsAuthorization.Binding.cs` 承接身分＋World Actor 綁定 | 原密鑰／身分／Actor cleanup、真 TLS Auth／重連 E2E 保持 |
| Server Gameplay Gateway 瘦身 | [ ] `IdentityTlsAuthorization.cs` 仍有巨大 socket/gameplay 流程，逐步抽出經驗證的 domain command gate | 不新增可繞過身份／MapEpoch 的 dispatch 路徑；Server TLS 全量回歸 |
| Client Reducer 分區 | [ ] `client/world/world_state.gd` 按 feature group 拆事件解析／處理，但保留唯一可重播 Reducer | 0/30/60 FPS、一致序號、重連／跨圖、可靠事件、TLS E2E |
| Client NPC | [ ] `client/npc/npc_interaction_panel.gd` 承接 App／SessionPanel 既有 NPC 選單與操作 | 開啟、取消與離開互動範圍，Server 仍驗距離及權限 |
| Client 社交 | [x] `client/party/party_panel.gd` 管邀請及隊伍；`client/chat/chat_panel.gd` 管頻道與訊息顯示 | 本機 Godot 真 TLS/UI 邀請、密語、聊天、隊伍快照與重連流程 PASS；Server authority 未變動 |
| Client 玩家商店 | [x] `client/player_shop/player_shop_panel.gd` 承接販售、瀏覽與購買 | Godot 真 TLS SessionPanel UI 回歸 PASS，Server UID／買賣雙方仍維持既有權威；私有 GPU 未驗 |
| Client 任務 | [x] `client/quest/quest_panel.gd` 承接任務開啟、選項、進度顯示 | 指令驗證仍由 SessionPanel 提供；真 TLS Quest UI 回歸 PASS |
| Client NPC 服務 | [ ] 先以 `client/session/session_panel.gd` 為遷移入口，商店、銀行、倉庫、修理、強化可各自操作後再定檔 | map epoch、關閉／返回及 Server 拒絕情境 |
| Server 聊天 | [ ] `server/src/SGTest.Server/Chat/ChatRouting.cs` 集中附近與密語的對象選擇，Party 維持隊伍狀態 | 同／跨圖路由、權限與可靠送出 |
| Server／Client 寵物 | [ ] Server `Pet/PetRuntime.cs` 預定掌管召喚、跟隨與成長；Client `pet/pet_presentation.gd` 接已確認的狀態 | 召喚／收回、掉線、跟隨、持久化與戰鬥回歸 |
| Server／Client 機甲 | [ ] Server `Mecha/MechaState.cs` 預定掌管乘降與能力轉換；Client `mecha/mecha_presentation.gd` 接角色畫面 | 狀態互斥、斷線回復、移動與裝備限制 |
| AFK／失焦 | [ ] 在 `client/app/main.gd` 和 `client/world/world_view.gd` 接生命週期。共用策略成形後評估 `client/afk/afk_presentation.gd` | 收包不中斷、資產釋放與恢復、同機多開量測 |
| 全量資料 | [ ] 擴充 `tools/asset_converter/content/` 的現有入口及契約測試 | Converter 可重跑、缺料明確報錯、發行包內容一致 |
| 發行 | [ ] 擴充現有 Host、Client、`runtime_pack.py` 與 CI 入口 | 雙圖 MVP、全量地圖、多開、壓力與跨平台發行依 TEST_PLAN |
| 好友 | 暫緩 | 依目前範圍保留在規劃 |

確立資料所有權後再新增檔案。常一起修改的規則放在一處；實際出現多組責任才加子目錄。Server 繼續由既有資料交易處理跨玩法原子性，Client 顯示快照並送操作意圖。
