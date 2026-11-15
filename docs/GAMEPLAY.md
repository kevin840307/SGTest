# SGTest — 重新開發遊戲功能規格

**狀態：需求，不代表已實作。** 後期《星夢 Online》風格；遊戲結果由 Server 權威決定。詳細原始需求可追溯 SPEC.md 與 [research/FEATURE_DECISION_HISTORY](research/FEATURE_DECISION_HISTORY.md)。

## 開發優先順序

**人物動作與同步正確 > 戰鬥因果 > 資源與流暢度 > 畫面完整性 > 其他功能。**

| 階段 | 交付 | 必驗收 |
|---|---|---|
| P0 身分 | 登入、登出、創角／多角色、重連 | 過期連線不可污染新角色 |
| P0 World | 地圖13、Blocking、移動、傳點、可視 AOI | 路徑不穿牆、換圖清舊可見物 |
| P0 同步 | 多人 Spawn/Despawn/Move/Stop/Facing/Action | 位置歷史獨立、沒有同步腳步或瞬移 |
| P0 怪物 | 巡邏、追擊範圍、攻擊、脫戰、死亡與復活 | Server 權威、AI Tick 不阻塞 |
| P0 戰鬥 | 普攻／技能、CD、命中／MISS、傷害、受擊、死亡 | 對自己與旁觀者相同可靠事件 |
| P0 掉落 | 生成、可見、拾取、消失 | UID 原子更新，多人不重複取得 |
| P0 UI | 金色原版風格／可選現代 Theme | 不依賴 Server FPS；多解析度 |
| P0 效能 | AFK/Unfocus、Render FPS、畫質／顯示偏好 | 只影響呈現，不影響 Network/Reducer |

## 第二階段

- Equipment：每件裝備獨立定義、ItemInstance UID、通用強化數值；強化 +1/+2 不額外佔用新 ItemDefinition ID。
- NPC：商店／倉庫／銀行／修理／傳點／對話／任務。價格、持久化和交易具 Server 權威。
- Skill：單體／範圍、Buff/Debuff、主動／被動與動畫／聲音事件。
- Pet／Mecha：完整原始部位、卡片／升級／經驗、跟隨，移速不改變動畫播放速度。
- Social：交易、聊天、好友／公會、玩家商店／地圖 AOI。
- PVP：依地圖規則，藍／白／粉紅（含褪色）／紅名，以及 GM 管理。
- 管理與診斷：GM 命令、負載／排程／慢封包資訊，不耦合正式戰鬥邏輯。

## 基本契約

Server 管理座標與碰撞、所有金流物品、攻擊與死亡。Client 只負責呈現、預測／補間及輸入意圖。普通 Move 事件不得重啟已有效的 Attack、Hit、Death 動作；每個 Action 有 Sequence 與合理中斷優先序。

新 Client/Server 在每一個功能 Gate 完成前不得以舊測試結果標示 PASS。