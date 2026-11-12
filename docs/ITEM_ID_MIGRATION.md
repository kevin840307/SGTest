# 物品編碼重設與舊 ID 遷移 — 必做

狀態：設計規格；舊 Weapon.itd/Equip.itd 為非 UTF-8/二進位資料，尚未逐筆解析；**沒有產生真實完整新舊 ID 清單**。TODO A003/A004 負責解析、測試、正式產表。不能臆測 +1/+2 在每個檔案必然是相鄰數字。

## 1. 使用者強制要求

- 所有裝備種類皆能有強化等級（依 slot/類型配置上限、成功率、花費及增益），不再需要為「長劍 +1、長劍 +2…」產生不同 ItemDefinitionId。
- 保留只有一個基本裝備定義，強化屬性存在每個個別裝備 ItemInstance。
- 刪除舊 +1/+2/+3 等強化變體的獨立編碼，後方物品編碼**往前排、填補空洞**，形成新遊戲緊密排序的定義 ID（一次性初始生成）。
- Old 來源 ID 仍保留在獨立 ImportMapping/Audit 資料中作查核，不能在 runtime 以 ID 加減法尋找 +N 物品。
- 所有 NPC、怪物掉落、任務、商店、技能消耗、裝備效果、登入資料、舊 SQL 引用必須一起依映射轉換；遇不確定必須 quarantine、報錯而不是對錯物品。

## 2. 新資料模型

ItemDefinition：
 id（int，發布前從1密集排序；發布後永久穩定）
 key（不可變字串，例如 equip.sword.basic_01）
 category / equip_slot / subtype / base_stats / icon_resource / render_resource
 can_equip / can_enhance（所有真正裝備都 true）/ enhancement_profile_id / max_stack

ItemInstance：
 uid（雪花或 UUID，與 definition 無關）
 owner / location(inventory, equipped, bank, ground, trade, house)
 definition_id / enhancement_level（>=0）/ durability / rolled_affixes / bind_state / version
 quantity（可堆疊非裝備）/ created_at

EnhancementProfile：
 equip_slot_category、max_level、cost_curve、stat_curve、success_rate、failure_rule、special_effect；與 definition id 無關。所有裝備至少有 default profile，可依類別 override。
單獨強化同一類別的兩把劍，應各有獨立 level/耐久/UID；不可修改 ItemDefinition 或其他玩家裝備。

## 3. 初始新 ID 生成演算法

輸入：已解析、已校驗、具有 source_category、old_id、name、type、variant_label、base_relation、stat_delta、source_sha 的 OldItemRecord。
步驟：
1. 類別/檔案編碼解碼清洗：Big5/CP950/二進位結構由專用 decoder，Unicode 正規化僅用於名稱比較，不把名稱當唯一 key。
2. 建 legacy compound key = (source_catalog, source_category, old_id)；不得假設不同檔案 old_id 全域唯一。
3. 確定性識別真正「強化 +N 變種」：需靠原版欄位、對應數據、效果/分類、原始流程核對；**僅看到字串尾巴 +1 不能視為證據**；重名/異名/碰撞進 quarantine。
4. 選唯一 Base ItemDefinition：如果原物件是合法基礎款保留；將確認的 +N 變體變成 LegacyAlias(oldId → baseKey, enhancementLevel=N)，不建立新 Definition。
5. 所有保留的基礎定義依 (原始全域排序明確 index, category tiebreak, old_id) 決定穩定順序；若沒有原始全域排序，預設以 (old_id, source_catalog, source_category) 排，須稽核衝突。
6. **僅第一次初始匯入**指派新 id = index+1，所有有效 base definition ID 緊密連續、無洞。這會使原本 +N 變體後面的定義向前遞補。
7. 輸出 ItemDefinitions.json、LegacyItemMap.csv/json、Unresolved.csv、MigrationReport.md、轉換摘要/hash，並以內容 SHA 給出可重現版本。
8. 正式版本發佈後 ID 凍結！新增項目 append 新 id；移除定義保留 tombstone，不能再因後續項目移除重新排號，否則破壞已持久化物品及封包。

## 4. 純示例（人工構造，不是 Old 真正 ID）

| 舊ID | 舊名稱 | 新定義ID | 新強化級別 | 備註 |
|---|---|---|---|---|
| 100 | 短劍 | 1 | 0 | 來源基本裝備 |
| 101 | 短劍+1 | 1 | 1 | 不佔新 ID |
| 102 | 短劍+2 | 1 | 2 | 不佔新 ID |
| 103 | 戰斧 | 2 | 0 | 後方前移 |
| 104 | 戰斧+1 | 2 | 1 | 不佔新 ID |
| 105 | 藥水 | 3 | 0 | 不可裝備/不可強化 |

新背包可同時存在：
 ItemInstance(uid=A, definition=1, enhancement=0)
 ItemInstance(uid=B, definition=1, enhancement=2)
 ItemInstance(uid=C, definition=2, enhancement=5)
原版來源 102 的既有物件轉成 (definition=1, enhancement=2)；新遊戲內沒有獨立的 102 強化定義。

## 5. 交叉引用與遷移安全

- Inventory/Equip/Bank/PlayerShop/Drop/Mail/GM commands/Quest/NPC Shop/Shop price/Mecha/Card/Pet Equip/Hotkey/Trade/Repair/ItemLog 全面掃描。
- 先建立 dry-run 重寫表，不直接寫正式 DB；差異 diff 必須列出來源 ID、原名稱/種類、候選新 ID、強化級別及所有引用位置。
- Reference Resolution Policy：同來源多個候選、同 ID 不同名稱/類型或缺原 Item 定義時，產 ERROR/Unresolved，不以同數字與新 ItemId 相等或最近 ID 猜測。
- SQL 載入採 transactional staging→驗證數量/外鍵→atomic swap；若資料轉換只用參考而不遷移正式舊服玩家，也應跑「資產守恆與映射完整度」測試。
- 新遊戲全新角色不一定要匯入舊玩家資料，但依然需要 Old 定義 ID/內容引用的完整轉換報告。

## 6. 測試/驗收 Gate

- 不同時區/作業系統/原始資料順序相同輸入 → bit-identical mapping。
- 真 +N 變體可還原 Base + N；非變體包含 + 符號的名稱不得錯誤合併。
- 相同名稱不同種類/來源不可被合併；同一舊 ID 多來源需 compound key。
- 0,1,2,5,20 等強化 level 能獨立存在且公式/上限一致；非裝備不可意外強化。
- ID 除已排除 alias 外無重複、無洞；每個引用可解析到合法 Definition 或明確 unresolved。
- 任務/掉落表/商店/GM/保存/登入/交易/銀行與裝備 UI 同步不因重編造成錯物、錯價、重複發放。
- 一次完成後匯入新項目不能重新排列舊項目；已發布資料版本升級以 Migration/Tombstone 達成。

## 7. 來源

- Old/服務端/Dat/Item/Weapon.itd、Equip.itd、General.itd、Recovery.itd、Booster.itd、pet.ITD
- Old/ClientData/Item/Item.itd、Item_cht.itd、Item_en.itd
- Old/資料庫/game.sql（equip/ItemID/ItemIdx、generalitem 等實體）
- 舊版 source current client/Items 與 server/Infrastructure/GameData/CurrentClientDataCatalog.cs 可參考欄位，不沿用有歧義的歷史 ItemID。
- 來源 SQL game.sql 中可見 equip 表 ItemID 與 ItemIdx，尚不足推論 +1/+2 真正公式，須查實際 Item.dat 與反組譯流程。