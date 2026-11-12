# 玩法系統模組規格（Gameplay）

分階段做到可玩的 RPG，**先穩定多人移動/面向/攻擊/受擊，再大量擴充**。各模組具 Domain/Application/Protocol/Storage/Tests 合約。可研究 Old 和現版，不能把旧封包或資料 model 當新架構。

## 1. 角色、帳戶與成長
Account 多 Characters；認證、選角、角色版本、線上狀態、斷線重連/轉圖/死亡；角色屬性 HP/MP/SP、職業、等級、經驗、點數、Buff/Debuff 與面向；重連保證只有一個具權威的 active session。成長數值 Data Driven，Server 以版本化表核算，每次升級不漏算多個等級。

## 2. 戰鬥與傷害
- 普攻：選目標、移動到攻擊距離、前置條件、Server 命中/MISS/閃避/爆擊/防禦/傷害、事件與動畫時間線。
- 攻擊距離依武器 definition 的可配置數值及狀態加成，不因只是「劍/槍」就擅自修改；未裝備與一般近戰劍初始可設相同基礎距離，實際公式先參考 Old 再 ADR 決定。
- Server 冷卻與技傷判定，Client 只預覽施法/移動；Hit/Miss/Death 結果所有觀察者一致，重複封包不重複扣血。
- 技能類型：指定目標/非指定目標/範圍/持續效果/治療/Buff/Debuff/召喚/被動與特殊連動。多目標 AOE 要在 MapActor 統一驗證每隻怪是否死亡、掉落/EXP/任務更新僅一次。
- Animation action 不是 Combat 決定 HP 的延遲，HP 真相立即生效，視覺可按 clip 播放延後效果。
- 長時間持續攻擊時 AI/DB 不得占用玩家移動事件的單一大鎖。

## 3. 怪物
MonsterDefinition(種類/等級/行為/掉落/地形/朝向動畫)；MonsterRuntime(currentPos/hp/target/threat/path/mode/respawn/revision)；AI狀態：Idle/Wander/Notice/Chase/Attack/ReturnHome/Death/Respawn。
- 每個怪物只在 MapActor 擁有一份狀態。
- 追擊超過指定 leash range 或時間/路徑不通就返回，不能追整張圖；無目標時錯峰閒置巡邏，不應 300怪同一 Tick 同時啟動動畫。
- 受傷、被擊殺與多人同擊一次結算；不可穿牆、遠距普攻、幽靈怪物。
- AOI 近遠 Snapshot/事件同步；漫遊路段方向依 Server/path 確定，Clip 播放週期與 movement speed 分離。

## 4. 物品、裝備、強化
- 定義與實例分離：所有裝備類型都能有 EnhancementLevel；原來舊 +1/+2 派生 ItemID 不存在於新 DataCatalog；必要之「+強化級」只用顯示名稱/數值計算。
- 基礎物品為裝備/消耗品/任務/材料/貨幣/寵物裝備/機甲部件，各自有 stack/slot/bind/attr rules。
- 強化交易：備料/扣款、檢查上限/成功率、按設計失敗處理、回寫 ItemInstance.version、事件 Outbox、冪等；支持個別裝備強化不同等級。
- 背包裝備/卸下/修理/賣出/交易同時操作時不能複製物品、產生負數數量或重複付款。

## 5. NPC、任務、商店、倉庫
NPC Definition/Placement 分離，任務對話流程允許 Say/Ask/Choice/QuestStart/QuestAdvance/Reward/OpenShop/Teleport；Quest state machine 在 Server，Client 顯示對話；執行腳本需隔離 API（可選受限 DSL/Lua sandbox），不可讓內容作者執行任意 SQL/作業系統指令。
商店/銀行/倉庫/攤位具獨立 request id + atomic DB transaction；多人同時買一件商品最多一人成功。商店/交易營運日誌不得包含秘密資訊。

## 6. 寵物、機甲、交通
PetInstance(UID、owner、level/exp、equip/skills、active)；Server PetRuntime 追隨、繞障、攻擊。MechaInstance/parts/battery/stone/level/appearance：裝備、卸下、升級、變換外觀與移速。
Client 的人物步伐/機甲履帶/受擊動畫使用獨立 playback parameters，不把角色世界速度直接乘動畫 fps；同圖多人看見相同走向與動作，裝備/變身需增量刷新外觀。

## 7. 社交/PVP/小屋（後期）
- Chat/party/friends/clubs：資料權限與 global channel 不適用地圖 AOI 篩選；有跨 Map EventHub。
- PVP：地圖是否允許攻擊、顏色狀態藍/白/粉/紅、紅名殺人與保護規則、閃粉快恢復之時序、GM 名稱顏色指令；Server 以狀態轉移表和時鐘判斷。
- Housing：獨立 Instance/Zone，建築/家具 Definition 與 Instance/placement 分離；授權角色才能進入/改動；家具長期儲存而不是 Client scene cache。
- 登出/斷線/切圖/死亡不能遺失當前資料、重複發放獎勵或永久卡住 trading session。

## 8. 優先級與驗收
P0：登入、移動、其他人動畫/方向、怪/攻擊/MISS/Hit/Death/掉落、AOI/切圖/重連。
P1：完整物品/強化、技能、任務/NPC、交易/倉庫、寵物/機甲。
P2：PVP、Housing、公會進階、活動、多 Channel。
每功能至少：Domain unit + wire contract + DB integration（如持久資產）+ 真人 Client/Server E2E；任何 CI 缺工具應顯式 SKIP，不能當作 PASS。
