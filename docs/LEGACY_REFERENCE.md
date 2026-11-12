# Old 反編譯程式、舊 Client/Server/地圖/資料庫研究指引

## 1. 適用範圍及版本

來源入口：https://github.com/kevin840307/SGO/tree/feature/local-server-net8
目前導向：https://github.com/kevin840307/TestGoGo/tree/feature/local-server-net8
2026-11-12 核對 SHA：aee7ec7fff9e446452655cf6d2be460dfd7a88f1
這份 SHA 是本次文件研究錨點，非永久最新版本；每次接手重新抓最新 HEAD 並檢視變更。

需分清：
- ORIGINAL DIRECT：直接在 *.lst/原始二進位/SQL 中可定位/重現的證據
- REPO AUDIT：來源 repo docs 裡先前人的調查結論，未在本次全部重算
- CURRENT IMPLEMENTATION：來源現有 Godot/.NET 程式，不等於原版正確行為
- DESIGN DECISION：SGTest 新設計，不能假裝 Old 有相同資料模型
- UNVERIFIED：缺原始封包 timestamp/有效測試資產/編碼解析的推論

## 2. 指定必查路徑

Old/ClientData/Paradise.exe.lst：2004 原始 Client 可見的反組譯清單；CSGOMap::MessageMap、CSGOChar::SetMoveloop、CSGOChar::MoveStep、CSGOEnem::movelogic、CSGOMap::OnCharSyncDest/OnEnemyMove/OnCharSyncPosition；需要繼續查動作/方向/紙娃娃/State 更新鏈。
Old/服務端/Core.dll.lst：CDServer::InitThread（FPS_BASE=25、FPS_CURRENT=10、FPS_CLIENT=50 在既有 audit 記錄）、CDServer::run 更新排程。
Old/服務端/plugins/SGOMapService.dll.lst：CDMapService2D::OnPlayerMove/OnNPCMove/Update/SendSyncDestPac/SendSyncPosition，以及視野/傳送事件。
Old/服務端/plugins/SGOPlayer.dll.lst：SGOPlayer::Update、CDPlayerSys::Update/UpdateClient/UpdateOwnClient；戰鬥/方向/角色資料。
Old/服務端/Dat/Map/Map13、Map15：Blocking.dat、Enemy.dat、NPC.dat、safe_area.dat；Map07 等多張地圖可驗證例外。
Old/服務端/Dat/Item：Weapon.itd、Equip.itd、General.itd、Recovery.itd、Booster.itd；老資料不是單純 UTF-8 文本。
Old/ClientData/Item：Item.itd、Item_cht.itd、Item_en.itd，需確定性 decoder。
Old/資料庫/game.sql：equipment/inventory 等 schema；與當前 server 資料與 migration 交叉核對。
來源 client/ 的 player.gd、network_player.gd、scripts/remote_player_motion.gd、scripts/remote_player_state_policy.gd、scripts/network_manager.gd、scripts/network_entity_manager.gd。
來源 server/ 的 SharedMapAiTicker.cs、ServerBroadcastHub.cs、Transport/WebSockets、SqliteGameStore 及對應 Smoke/LoadProbes。
來源 docs/multiplayer-legacy-movement-audit.md、docs/legacy-movement-parity-20261111.md、docs/remote-player-hit-motion-20261111.md、docs/multiplayer-motion-v2-20261111.md、docs/legacy-paradise-lst-vs-godot-density.md。

## 3. 目前已有證據及限制

| 主題 | 既有證據 | 證據層級 | 新設計意義 |
|---|---|---|---|
| 原版 Client 連續步進 | Paradise.exe CSGOChar::MoveStep 0x004BEA62、CSGOEnem::movelogic 0x004C9D69；既有 docs 解讀 | REPO AUDIT | 新角色依 path/速度 + Server timeline，不以每個 Move 包直接瞬移 |
| 原版封包分類 | A106/A107/A108/A10D，相關函式地址於 docs/multiplayer-legacy-movement-audit.md | REPO AUDIT | 開始/移動路段與權威 correction 分離；不直接使用舊 type ID |
| 原版頻率 | Core.dll 預設 Current FPS=10、Client FPS=50 | REPO AUDIT | 這是更新 Tick，**不能據此說每100ms必有一包** |
| 現有 Server | SharedMapAiTicker 用250ms共用 monster AI；部分持鎖/SQLite Damage write 曾造成尖峰 | CURRENT IMPLEMENTATION/LOG AUDIT | 新版由 Map Actor單寫者 + DB非熱路徑，而非只調頻率 |
| 現有 Remote Motion | Godot remote_player_motion.gd 使用 SnapshotSpanSeconds/SpeedTime、有界外推及命中動畫保護 | CURRENT IMPLEMENTATION | 可參考算法與重現案例，但新設計改成 ServerTick 序列 timeline |
| 地圖網格 | 已有 game-editor README 指 floor(world/16)、flag mask、cell*16+8 | REPO AUDIT | 需要 Old Blocking.dat 真實位元/座標/例外 fixture |
| 原版渲染 | Paradise.exe.lst 觀察到 Direct3DCreate8、Sprite VM Cache、index/vertex buffer | REPO AUDIT | 原版使用獨立較低階資源層；**不能因此推論實際 FPS/批次數量** |
| Item +N ID | 舊 Item.itd 為二進位/非 UTF8，需要解析 | UNVERIFIED | 不能用「連號就代表+1」直接把物品合併 |
| 攻擊/面向 | 舊反組譯與素材方向意義尚未逐函式核對 | UNVERIFIED | 請優先執行 A002，明確方向數/面向/Action 結束條件 |

## 4. 分析交付形式

每項研究生成 evidence/motion.csv、evidence/items.csv、evidence/maps.csv（先用原創/脫敏 fixture），包含 source_repo、source_commit、source_path、function/address/record_offset、field、observed、inference、confidence、next_test。
以文件「事實 / 推論 / 設計」三欄撰寫。不能單憑網路文章、先前 ChatGPT 回答或反編譯函式名推導完整事實。
若 Old 串流與實際封包頻率資料不存在：可仍設計更好的新版本、設假設/負載/模擬測試，不能聲稱「原版確定每X毫秒送包」。

## 5. 研究順序

R1 玩家（自己的和其他人的）位置/目的地/停止/方向/跑走/攻擊/受擊/死亡/順移阻擋／相機 AOI。
R2 Monster巡邏/追擊/觸發距離/受擊/死亡/物件更新。
R3 Item +N ID結構與物品/強化/任務/掉落/商店索引。
R4 Map Blocking/Enemy/NPC/safe area 與繪圖資源格式。
R5 Pet/Mecha/Inventory/Shop/Quest/PVP、遷移與參考設計。

## 6. 法律與維護

目前未確定來源倉庫及相關舊遊戲資產授權；GitHub 可見並非可重用授權。以研究觀察、功能需求及自行撰寫乾淨介面/演算法，不在 SGTest 散布大型 .lst、素材/圖片/音樂、舊商標或未授權文本。授權確定後，亦要記來源/授權/範圍/公告義務。