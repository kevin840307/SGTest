# SGTest — 全新 2D MMORPG：高效能、低資源、可維護

> 規劃基準 v1.0｜2026-11-12｜目前狀態：設計文件已建立；遊戲程式、效能驗證與 CI 尚未實作。請勿將「目標」當成「PASS」。

## 專案定位

全新開發、獨立維護的 2D MMORPG。以 **Godot 4 Client + C#/.NET 10 Server + PostgreSQL** 為起點，重點是「大量玩家、怪物、掉落物同圖時仍正確且即時」，以及比現有 SGO Godot Client 更低資源、易維護。不是從 SGO Fork，也不沿用其歷史封包或裝備強化 ID。

**優先級：玩家自己操作與其他玩家動作／方向／普攻／受擊／死亡 > 戰鬥中的怪物 > 寵物 > 一般怪物 > 掉落物視覺 > 裝飾。** 這只限制不重要的「渲染與投遞時效」，不能丟棄資產、傷害、死亡或物品拾取的可靠遊戲事件。

## 文件入口

1. [SPEC.md](SPEC.md) — 產品完整規格、里程碑、需求與驗收
2. [todo.txt](todo.txt) — 跨 AI Session 接續工作清單（唯一進度來源）
3. [AGENTS.md](AGENTS.md) — 接手、檢查來源、測試、提交及避免併發覆寫的規則
4. [架構](docs/ARCHITECTURE.md)／[多人移動與動畫](docs/NETWORK_AND_MOTION.md)
5. [物品編碼/強化遷移](docs/ITEM_ID_MIGRATION.md)／[舊版來源查核](docs/LEGACY_REFERENCE.md)
6. [地圖/資料](docs/MAPS_AND_CONTENT.md)／[玩法模組](docs/GAMEPLAY.md)／[資料模型](docs/DATA_MODEL.md)
7. [效能預算](docs/PERFORMANCE.md)／[測試/CI](docs/TEST_PLAN.md)／[資安/營運](docs/SECURITY_OPERATIONS.md)
8. [架構決策 ADR](docs/DECISIONS.md)

## 參考專案（唯讀，不做舊版修補）

- 使用者給定： https://github.com/kevin840307/SGO/tree/feature/local-server-net8
- GitHub 目前重新導向： https://github.com/kevin840307/TestGoGo/tree/feature/local-server-net8
- 研究時的來源 commit：aee7ec7fff9e446452655cf6d2be460dfd7a88f1（2026-11-12 查詢）
- 核心參考：Old/ClientData（Paradise.exe.lst、Item/*.itd 等）；Old/服務端（*.lst、Dat/Map、Dat/Item、Quest）；Old/資料庫（*.sql）；目前 client/、server/、game-editor/、docs/、tests/。
- 舊版 IDA、二進位格式、素材可能有著作權限制，**不得未經授權複製進公開新 repo／發行版**。本倉庫先存「規格、證據、資料轉換程式與可重製的測試 fixture」，不搬運 Old 原件。

## 關鍵設計

- Server 是位置、阻擋、戰鬥、死亡、物品、交易、PVP 的唯一權威。
- Client「接收資料、管理壓縮狀態、產生可視表現」分層；大量掉落/怪物不一一建立昂貴 Node/Timer/Signal。
- 可移動玩家用獨立的時間軸插值/有限外推，Server 提供時間、移動路段、方向及狀態序列；Stop/Teleport/Hit/Death 是不可被移動合併覆蓋的邊界。
- Map Runtime 單擁有者（Actor / 單寫者）；DB IO 不可在地圖熱路徑/共同鎖內等待。
- AOI 訂閱、Server 邏輯世界、Client 圖像可見範圍三者各自獨立，Viewport/Zoom/超寬視窗不沿用 800×600。
- 物品全部重編新定義 ID；每件可裝備物品的強化可用欄位存於獨立實例，不用 +1/+2 變體 ID。
- 遠景渲染降級、AFK、失焦與最小化不能改變網路、戰鬥、移動、撿物等邏輯的時效。
- 先建立基準與回歸、後改性能；不能宣稱未實測的 RAM/FPS、多人低延遲已達成。

## 開發起點

目前沒有可執行 Client 或 Server。下一 Session 讀 AGENTS.md + todo.txt，從 A001 與 A002 的證據/資產授權檢查開始，接著建立最小工程與 CI。