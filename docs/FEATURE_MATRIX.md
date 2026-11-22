# SGTest Feature Matrix

本檔是 SGTest 唯一正式 Feature Matrix。功能完成度以此檔、`todo.txt`、對應 SPEC/Architecture/Test Plan 與實際測試證據共同判定；若彼此衝突，以較保守狀態為準。

狀態只允許：`NOT STARTED`、`IN PROGRESS`、`IMPLEMENTED`、`TEST FAILED`、`NOT VERIFIED`、`VERIFIED`、`COMPLETE`。

`COMPLETE` 必須同時滿足該功能適用的 Client、Server、UI、Persistence、Unit/Integration/Multiplayer/Visual/A-B/Regression Gate；沒有執行的 Gate 一律不得視為 PASS。此矩陣自 2026-11-22 建立，既有歷史功能先採保守遷移，不因「已有程式碼」自動升級為 COMPLETE。

| Feature ID | 功能名稱 | HK 預期行為 | 證據來源 | Client | Server | UI | Persistence | Unit Test | Integration Test | Multiplayer Test | Visual Test | A/B Test | BUG / 限制 | Commit SHA | 狀態 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| MOVE-LOCAL | 本地玩家移動 / Prediction | 點擊、長按、繞障、固定速度、Server 權威校正，操作感接近 HK | `todo.txt` PLAYER-LOCAL-MOVEMENT-PREDICTION；`docs/CLIENT_ARCHITECTURE.md` | 有 | 有 | 有 | N/A | PASS（既有） | PASS（合成） | 部分 | NOT VERIFIED（私有真 GPU） | NOT VERIFIED | 真 Map13/15 長時間、WAN/多人畫面仍缺 | —（歷史遷移） | IN PROGRESS |
| MOVE-REMOTE-INTENT | 遠端玩家 route intent / 平滑同步 | 其他玩家由合法移動意圖連續走，Stop/Retarget/傳點/戰鬥狀態正確，不忽快忽慢 | `docs/RUNTIME_PROTOCOL.md`；`docs/TEST_PLAN.md`；`todo.txt` PLAYER-REMOTE-MOVE-INTENT-AOI | reducer + route presentation 已有 | 已有 | Blocking route 已接 | N/A | PASS | PASS | PASS（20 AES synthetic） | NOT VERIFIED（private PCK/source metadata mismatch） | protocol A/B PASS；原版動態 A/B 未做 | 96px bounded re-anchor 為暫定策略；Windows 真雙 Client、障礙實機、WAN/soak 未驗 | `ec043cf` | IN PROGRESS |
| AOI-PLAYER | 玩家 AOI Spawn/Delta/Despawn/Resync | 只看到視野內正確玩家，換圖/重連不殘影，可靠事件不丟 | `docs/RUNTIME_PROTOCOL.md`；`docs/TEST_PLAN.md` | 有 | 有 | 有 | N/A | PASS | PASS | PASS | 部分 | NOT VERIFIED（HK 實機） | Windows 真視覺與長時間 soak 未完整驗 | `3aa00d8`（最新移動切片） | VERIFIED |
| AOI-MONSTER-DROP | 怪物與地上物 AOI | 可見範圍內 Spawn/Delta/Despawn，無重複 UID/跨區污染 | `docs/TEST_PLAN.md` MVP008 load gates | 有 | 有 | 有 | 部分 | PASS | PASS | PASS（20/300/500 synthetic） | NOT VERIFIED（Windows 真素材） | NOT VERIFIED | synthetic load 不等於完整戰鬥/DB 掉落結算 | —（歷史遷移） | VERIFIED |
| AUTH-CHARACTER | 登入、註冊、角色列表/創角/選角/登出 | 流程與 HK 對玩家可見行為一致，角色資料不越權 | `SPEC.md`；`docs/TEST_PLAN.md` | 有 | 有 | 有 | 有 | PASS | PASS（TLS） | 部分 | NOT VERIFIED（完整 HK UI A/B） | NOT VERIFIED | Original UI/完整角色外觀 A/B 尚未完成 | —（歷史遷移） | IN PROGRESS |
| COMBAT-PVE | 普攻、MISS、傷害、死亡、經驗、掉落、重生 | 打怪完整循環與 HK 數值/距離/節奏一致 | `todo.txt`；Combat 相關測試與 protocol docs | 部分 | 有 | 部分 | 部分 | 部分 PASS | 部分 PASS | 部分 PASS | NOT VERIFIED | NOT VERIFIED | 攻擊距離、動畫/受擊、真數值與 HK A/B 尚未完整 | —（歷史遷移） | IN PROGRESS |
| SKILL-BUFF | 技能、BUFF、範圍/單體技能 | 技能效果、動畫、冷卻、數值與 HK 一致 | `todo.txt`；Skill protocol/tests | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | 全技能資料/視覺/數值尚未完整盤點 | —（歷史遷移） | IN PROGRESS |
| ITEM-EQUIP | 背包、物品、裝備、耐久、強化 | 名稱/圖示/能力/限制/交易結果正確；內部採 Definition+Instance+Enhancement | `SPEC.md`；`todo.txt`；Inventory/Equipment/Enhancement tests | 部分 | 部分 | 部分 | 有 | 部分 PASS | 部分 PASS | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | HK 全物品資料與 UI A/B 尚未完成 | —（歷史遷移） | IN PROGRESS |
| ECON-NPC | NPC 商店、倉庫、銀行、修理 | 權威交易、金錢/物品原子性、重登保存 | `docs/TEST_PLAN.md`；Shop/Storage/Bank/Repair protocol/tests | 部分 | 部分 | 部分 | 有 | 部分 PASS | 部分 PASS | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | 原版完整 UI/數值/錯誤邊界未全驗 | —（歷史遷移） | IN PROGRESS |
| ECON-PLAYER | 玩家交易、擺攤 | 雙方交易一致、擺攤庫存/金錢原子性、AOI 正確 | `todo.txt`；Trade/PlayerShop protocol/tests | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | 十人併購/支付等歷史缺口需重新 Gate | —（歷史遷移） | IN PROGRESS |
| SOCIAL-PARTY | 組隊 | 邀請/加入/離開/隊長/狀態同步與 HK 一致 | Party protocol/tests；`todo.txt` | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | 完整 HK 操作流程 A/B 未完成 | —（歷史遷移） | IN PROGRESS |
| SOCIAL-FRIEND | 好友 | 新增/刪除/線上狀態/重登一致 | HK/Old；`todo.txt` | 未完整盤點 | 未完整盤點 | 未完整盤點 | 未完整盤點 | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | Paradise 無法驗證，需 HK/Old 證據 | — | NOT VERIFIED |
| SOCIAL-CLAN | 社團 | 建立、加入、離開、權限、成員、公告、持久化 | Clan protocol/tests；HK/Old；`todo.txt` | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | Paradise 無法驗證；HK UI/完整權限仍需盤點 | —（歷史遷移） | IN PROGRESS |
| PVP | PvP / 名稱狀態 | 地圖條件、藍白粉紅紅名、傷害/死亡/同步符合 HK | PVP protocol/tests；`todo.txt` | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | 名稱閃爍/完整規則/多人 A/B 未完成 | —（歷史遷移） | IN PROGRESS |
| QUEST | 任務 | NPC 對話、條件、選項、進度、獎勵與持久化 | Quest protocol/tests；HK/Old | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | NOT VERIFIED | 全 HK quest data/流程尚未盤點完成 | —（歷史遷移） | IN PROGRESS |
| PET-MECHA | 寵物、機甲 | 召喚/外觀/裝備/卡片/升級/同步與 HK 一致 | `todo.txt`；既有 smoke 結果 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | 部分 | NOT VERIFIED | NOT VERIFIED | Pet 外觀等歷史 FAIL 需回歸 | —（歷史遷移） | IN PROGRESS |
| UI-ORIGINAL | HK Original UI | 800×600 忠實；4:3/16:9/16:10 正確 Anchor；顯示真實資料 | HK client / Paradise / Recovered2；UI docs/todo | 部分 | N/A | 部分 | N/A | 部分 | 部分 | 部分 | NOT VERIFIED（完整 A/B） | NOT VERIFIED | 遊戲內浮動 UI、商店/社團/物品等仍有差距 | —（歷史遷移） | IN PROGRESS |
| RESOURCE-PERF | Godot 資源/記憶體/效能 | 換圖/登出/AFK/Unfocus 可釋放且恢復快，畫質不降 | `docs/TEST_PLAN.md` process baseline；`todo.txt` | 部分 | 部分 | N/A | N/A | 部分 | 部分 | 部分 | NOT VERIFIED（Windows 真 GPU） | N/A | RSS/VRAM/1-2-4 多開/長時間未達 Release Gate | —（歷史遷移） | IN PROGRESS |
| RELEASE | 正式 Release Gate | Client+Server 全主要功能、測試、效能、HK 體驗及 Git 同步全達標 | 本矩陣 + `todo.txt` + TEST_PLAN | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 | 未完成 | 上述多項仍 IN PROGRESS/NOT VERIFIED | — | NOT STARTED |

## 維護規則

1. 每次正式功能修改必須更新受影響列；新增獨立玩法時新增 Feature ID，不另建第二份 Feature Matrix。
2. `IMPLEMENTED` 只表示程式路徑存在；`VERIFIED` 表示該列聲明的適用 Gate 有可重跑證據；`COMPLETE` 仍需 HK 可確認項目的 A/B 與所有適用 Release Gate。
3. 測試若失敗，狀態至少降為 `TEST FAILED` 或 `IN PROGRESS`，不得只在文字備註中隱藏。
4. Paradise 無法驗證的好友/社團/擺攤等功能，必須用 HK client 反編譯、Old Server、正式資料或其他可信證據建立規格；不可虛構 A/B PASS。
5. Commit SHA 欄只填已存在的 commit；本檔後續每次更新應優先填實作 commit，而不是文件 commit。
