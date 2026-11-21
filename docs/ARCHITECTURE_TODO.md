# 架構檔案 TODO

[回架構與現有目錄](ARCHITECTURE.md#實際目錄與入口)。此頁按預計責任列出未完成檔案或既有檔案的缺口。列出的新路徑是命名建議，會隨第一個可執行切片調整。`todo.txt` 記錄交付和驗證進度。

| 區域 | 檔案 TODO 或現有接點 | 開始拆分時的驗收 |
|---|---|---|
| Client NPC | [ ] `client/npc/npc_interaction_panel.gd` 承接 App／SessionPanel 既有 NPC 選單與操作 | 開啟、取消與離開互動範圍，Server 仍驗距離及權限 |
| Client 社交 | [ ] `client/party/party_panel.gd` 管邀請及隊伍；`client/chat/chat_panel.gd` 管頻道與訊息顯示 | 隊伍路由、密語、換圖與斷線回歸 |
| Client 玩家商店 | [ ] `client/player_shop/player_shop_panel.gd` 承接販售、瀏覽與購買 | 庫存 UID、過期商品、買賣雙方結算 |
| Client NPC 服務 | [ ] 先以 `client/session/session_panel.gd` 為遷移入口，商店、銀行、倉庫、修理、強化可各自操作後再定檔 | map epoch、關閉／返回及 Server 拒絕情境 |
| Server 聊天 | [ ] `server/src/SGTest.Server/Chat/ChatRouting.cs` 集中附近與密語的對象選擇，Party 維持隊伍狀態 | 同／跨圖路由、權限與可靠送出 |
| Server／Client 寵物 | [ ] Server `Pet/PetRuntime.cs` 預定掌管召喚、跟隨與成長；Client `pet/pet_presentation.gd` 接已確認的狀態 | 召喚／收回、掉線、跟隨、持久化與戰鬥回歸 |
| Server／Client 機甲 | [ ] Server `Mecha/MechaState.cs` 預定掌管乘降與能力轉換；Client `mecha/mecha_presentation.gd` 接角色畫面 | 狀態互斥、斷線回復、移動與裝備限制 |
| AFK／失焦 | [ ] 在 `client/app/main.gd` 和 `client/world/world_view.gd` 接生命週期。共用策略成形後評估 `client/afk/afk_presentation.gd` | 收包不中斷、資產釋放與恢復、同機多開量測 |
| 全量資料 | [ ] 擴充 `tools/asset_converter/content/` 的現有入口及契約測試 | Converter 可重跑、缺料明確報錯、發行包內容一致 |
| 發行 | [ ] 擴充現有 Host、Client、`runtime_pack.py` 與 CI 入口 | 雙圖 MVP、全量地圖、多開、壓力與跨平台發行依 TEST_PLAN |
| 好友 | 暫緩 | 依目前範圍保留在規劃 |

確立資料所有權後再新增檔案。常一起修改的規則放在一處；實際出現多組責任才加子目錄。Server 繼續由既有資料交易處理跨玩法原子性，Client 顯示快照並送操作意圖。
