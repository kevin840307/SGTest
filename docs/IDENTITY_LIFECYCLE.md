# F001 Identity Lifecycle（帳號／角色／重連）

日期：2026-11-14。狀態：**F001 PASS**。

## 完成範圍

- Account：建立帳號、登入、登出。
- Character：角色列表、建角、最多4角色、全域不分大小寫唯一名稱、選角。
- World lifecycle：WebSocket connect 不直接 Spawn；只有成功選角才建立 `World.Player`。
- Reconnect / takeover：同帳號新登入先移除舊 World Entity，再讓新 Session 取得帳號；舊 Client 可靠收到 `session_replaced`。
- Identity expiry：每次成功驗證取得新的 Server `SessionEpoch`；logout/takeover 清除 AccountId、CharacterId、Ready、SessionEpoch 與 AOI cache，晚到的 world command 不再具有權限。
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
2. register/login：成功後取得 AccountId + SessionEpoch，Server 回角色列表。
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
- Same-account lifecycle（SQLite）：A register/create/select → B takeover → C takeover+logout；三個 Client 使用同一 AccountId/CharacterId，SessionEpoch 各自不同；Server replacement=2；`/health players` 為 **1 → 1 → 0**；PASS。
- 20-client SQLite first-login storm（final rerun）：20/20 在約 **1.55s** 內完成註冊/建角/選角；tick=300 P95/P99 **2.207/7.596ms**；0 early exit；PASS。
- 目前完整 main Client final smoke：login→register→create→select→map ready→prediction/snapshot，Render 約60.00FPS、physics/input 約30.00Hz、queue P99 **30.009ms**、0 Client/Server error；PASS。

## 邊界

- 此 F001 完成定義不包含 Email、忘記密碼、2FA、正式營運 TLS/反暴力破解；這些屬後續 Security/Operations。
- 角色完整 stat/inventory/equipment/position persistence 會隨 F002/F008/F005+ 的資料模型逐步加入；F001 目前持久化的是 Account + Character identity。
- `SessionEpoch` 是 process-lifetime 身分版本；Server restart 會終止所有舊 socket，因此新 process 可重新起始 epoch。
- 原版逐格視覺 AS005 與媒體授權 AS008 與本功能無關，仍保持獨立 Gate。
