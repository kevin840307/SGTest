# SGTest 資料模型、ID 與交易一致性（初版 SQLite）

F001 已建立初版 SQLite migration：`account`、`character`、`schema_migration` 已有實際 DDL、WAL/FK/busy-timeout 與 unit/integration/E2E tests，詳 `docs/IDENTITY_LIFECYCLE.md`。下列其餘資產／交易／任務表仍是後續設計骨架，必須各自以 migration + contract/integration tests 落地後才算完成。

## 1. ID 原則

- account_id：獨立帳號
- character_id：持久玩家角色
- session_id / session_epoch：短期連線身份，重登須更新 epoch
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
