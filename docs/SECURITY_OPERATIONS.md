# 資安、營運、GM、儲存與擴充政策

## 1. Session/防作弊/封包安全
WebSocket 用 TLS、認證 token/SessionEpoch、有效期、斷線即失效、心跳/重連握手、序列化上限、每包長度與欄位驗證、授權先於世界/資產動作。輸入視為不可信：速度/位置/攻擊距離/技能 CD/物品與金錢全部 Server 決定。
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
- 開發：可重建 PostgreSQL fixtures；正式：定期備份、PITR、索引/資料遷移、schema compatibility、Rollback plan。
- 新版 Client/DataCatalog 發布帶版本+hash、內容資產合法性清單、遠端版本最低需求。
- 單體部署後才依 MapActor/Channel 的真容量測試拆 Pod/多節點；跨實例 handoff 使用 ownership epoch/租約、Outbox/去重、防止雙重人物與怪物。
- 24h soak、故障注入：DB暫停、單個 WS 慢用戶、Server重啟、網路斷線、AOI 大量變更，須在負載中維持資產不重複/玩家恢復一致。

## 6. 外部素材/反編譯授權
SGO/TestGoGo repo 的公開可見不等於內容可合法複製/改作/發布。SGTest 原創實作、原創/合法授權資產、參考的資料與原始 ID 額外保留出處與權利稽核。不能先把舊 .lst/圖檔/音樂放公開 repo 再補查授權；該限制列為 P0 Gate。
