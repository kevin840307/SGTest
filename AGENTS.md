# SGTest 開發與 AI Session 交接規則（2026-11-15 重建版）

## 現況與使用者決策

使用者決定**保留規格、研究證據、素材轉換程式與必要測試，刪除舊 Client/Server 原型，依文件重新設計實作**。不得自 Git 歷史還原舊 Client 或 Server 當成新版本，也不得以歷史 PASS 宣稱現在能運行。

本版取代此前 AGENTS.md 中「舊原型已實作／禁止開始原型」等互相矛盾的工作階段狀態描述。仍須參考 `SPEC.md` 與歷史文件的功能需求；新版本狀態以 `todo.txt` 為準。

## 開工前

1. 先 `git fetch`，確認目前分支與遠端狀態，查看 `git status --short`，避免踩到其他 Session 未提交的工作。
2. 先讀 `README.md`、`docs/ARCHITECTURE.md`、`docs/CLIENT_ARCHITECTURE.md`、`docs/SERVER_ARCHITECTURE.md`、`todo.txt`；依任務補讀相關素材／原版證據文件。
3. 若與其他 Session 共用工作樹且有 WIP，**建立自己的 worktree/branch**，不要 stash、reset、clean、覆寫、刪除他人的未提交內容。
4. 領取一項具體 TODO，先寫預期行為、指標、驗收測試，再動手；完成才勾選，未跑寫 NOT VERIFIED。
5. 確保拆成小 commit，commit 僅包含自己變更的檔案；每次更新 TODO 與架構決策證據。

## 單地圖、功能完整的 MVP（不是功能示範）

**MVP = 一張可玩的真實 Map13 + 大部分原版功能完整運作 + 多人／多怪／多掉落正確與順暢。**
只延後「所有地圖、所有怪物及所有物品的內容量」與跨地圖實際場景，不延後單圖中已能運作的遊戲規則／功能。好友系統依使用者要求暫緩。

- **第一步一定先地圖**：核對 Map13 terrain/scenery、Blocking.dat、座標、原點、Y-sort、相機與原版八方向素材；使用唯一 Converter 有需要才逐類產出必要資產。來源不足需記錄並以證據選另一張可驗證的單地圖。
- **功能完整度不能用一條 happy-path 取代**：登入／多角色／建立選擇、斷線恢復、原版 A*／長按與人物／機甲／怪物方向動畫、多人 AOI、攻擊／受擊／技能／狀態／MISS／死亡復活、怪物獨立 AI、Drop／Pickup、物品／裝備／強化、NPC 商店／倉庫／銀行／修理、技能／寵物／機甲、玩家交易／玩家商店、聊天、PvP／名稱顏色及 GM 管理，都需按原版規則實作並有正常／錯誤／邊界及 Server Authority 測試。
- **只有地圖限制的例外**：跨圖傳點／多圖 AOI／跨圖入口等需第二張真地圖的實機驗收，MVP 先完成資料合約、原版規則與可重播 fixture，清楚標 NOT VERIFIED；MVP 後補第二張地圖的真 E2E，不能把 fixture 說成全圖驗收。
- **逐步垂直切片，不一次堆大專案**：Map13 Converter/Blocking → Godot Map Renderer → .NET 8 Wire/World/A* → 兩個真 Client AOI/方向／動畫 → 多種怪物／戰鬥／大量掉落 → 全部上述主要單圖功能逐項完成 → AFK／Unfocus／多開及 20 Player/300 Enemy/500 Drop 單圖壓測 → MVP Gate。
- **MVP 多人效能 Gate**：至少兩個真 Godot Client 做畫面 E2E；再遞增 2/4/20 名連線（可部分用明確標示的 Bot）、300 怪、500 掉落，含稀疏與密集、部分怪物靜止與獨立移動、多人互相走動／攻擊／拾取；1/2/4 開與 AFK。測 P95/P99、CPU/RSS/VRAM、World Tick、bytes/sec、Queue Age、畫面／方向與可靠事件。
- **MVP 後**才展開全部地圖／物種／品項、跨圖實機 Gate、50～100 Player/1000 Enemy/5000 Drop 與 macOS 等全量發行 Gate。MVP 選出的方案擴量後必須按 ALT001 再比較，不能宣稱永久最優。

每個系統先最小功能、再錯誤／斷線／多人邊界、再整合；未通過必要測試不得標 MVP 完成，所有文件與 TODO 隨程式即時更新。

## 多方案先比較、選最佳解、才繼續開發（每個重大決策的必要 Gate）

**不可找到第一個能用的方案就直接一路堆功能。** 封包／AOI、Server Tick 與 AI、A*、動作補間、Renderer、Atlas／PCK、AssetStore、AFK／多開或資料儲存等如果有多種合理做法：

1. **研究與候選**：先核對原版／Recovered 行為和現有正式實作，提出少數真正不同的方案（通常 2～3 種），包含當前 baseline（若存在）。不得製造一堆永久替代程式。
2. **正確性先於速度**：先驗證 Server 權威、遊戲功能、可靠事件／封包序號、安全、動畫／碰撞、AOI 和生命週期。錯誤方案即使較快也不得採用。
3. **公平實測**：用同一素材／地圖、人物／怪物／掉落密度、事件 Trace、解析度、硬體、Release 設定、視野及多開數，測稀疏、密集、壓力和 AFK；暖機、多輪重測、保留可重跑命令與原始結果。
4. **看整體表現**：比較 Input-to-Render、Frame/Server Tick P95/P99/Max、Queue Age、AOI bytes/sec、CPU／RSS／VRAM／GC、載入／卸載／恢復、可維護性及複雜度，不用假 FPS 或犧牲畫質／同步作弊。
5. **選最佳整體方案**：性能差異不穩定或不顯著就選較簡單、低耦合方案；只有實測證明混搭有益才混搭。記錄候選比較表、決策理由與限制到對應**既有**文件，不增加大量 MD。
6. **完成 Gate 才往下**：把選定方案整合回唯一正式程式／轉換腳本，跑回歸並同步 SPEC／TEST_PLAN／TODO；測不出結果就標記 NOT VERIFIED，不能稱為最佳。

小型、沒有實質替代方案的修改直接做必要回歸即可，不必為了比較而過度設計。重大選型要先比較，但**不可無止盡實驗，須持續交付一小步、提交再向下開發**。

## 不能違反的架構契約

- **資產**：一個原素材一個獨立 logical/physical Asset（Enemy / NPC / Scenery / Player part / Equipment 等）；Frame 高度排序、POT 尺寸只是同一 Asset 內策略。禁止跨素材合圖，怪物目錄使用 `Enemy/`，不得多一層 `generation/`。
- **來源**：`Old`、`Recovered`、`TestGoGo` 為唯讀研究來源；未有發佈權時不可把原圖、音效、反編譯文本複製進公開 Git。
- **Client**：network transport、message codec、state reducer、prediction、render/UI 分工明確；AFK/Unfocus/Render FPS 只能改變畫面/音效，不可節流可靠事件、收包與固定遊戲邏輯。
- **Wire 選型**：不預設 JSON 或 MessagePack。先觀察 Recovered/Old 原版格式，建立 C#／Godot 相同 fixture，實測 framing／header/binary/序列化方案的 bytes/sec、延遲、alloc/GC、跨平台和維護成本；正式只保留一套相容協定。
- **AOI 優先**：Server 先以權威 map/position + 受限 viewport/interest 過濾玩家、怪物、掉落物，先訂閱再發 Delta；可靠 Spawn/Despawn、戰鬥、物品、交易事件不得被合併快照吞掉。避免全圖廣播、群體同步怪物動作及每 Tick 無意義的重複狀態。
- **動畫**：不因 Move 事件不當覆蓋 Attack/Hit/Death；保留方向、ACT timing、trim/origin、flip、紙娃娃 draw order、地圖物件與所有角色共同 Y-sort。
- **Server**：authority 管理位置、Blocking.dat 碰撞、戰鬥、物品、金錢、交易；World 單寫者設計，外部 DB I/O 不能卡住 Tick；網路與遊戲規則分離。
- **維護性**：以少數功能模組構成 modular monolith；不要過度抽象、重複 Manager/Loader/Cache 或為分類而分類。
- **證據**：Recovered 與新 Client 比較時同工作負載、同素材、同 GPU 設定，報告 Process RSS、Texture metric、載入、P95/P99、Draw Calls、畫面與同步正確性。未完成發行測試一律不得稱 PASS。
- **移動／AFK**：Server Blocking.dat 權威 A*；長按 1 秒鎖定移動，左／右鍵停止且右鍵選單不能失效；參考 Recovered/Old 行為。AFK 基本 Render 0／釋放 GPU，網路／WorldReducer 與 FPS 獨立；同機多開需量測 CPU／RSS／GPU。
- **快取**：原始遊戲 Data1.dat 與轉換器原始程式是來源；`.cache` 是可重建輸出，不可含唯一程式碼或 Git worktree。清除前檢查所有 worktree、執行中的工作與私有來源；不可清理其他 Session 未保留的 WIP。

## 程式／文件路由

- `tools/asset_converter/` 是唯一正式轉換器。純 Asset 結構驗證不應依賴一個可執行的 Client / Server。
- `client/`、`server/` 為未來位置；新增檔案之前先對照對應 ARCHITECTURE 與當期 TODO，不複製舊原型大類別。
- `docs/` 為規格、研究證據與決策，`docs/research/FEATURE_DECISION_HISTORY.md#source-todo-pre-rebuild-20261115-txt` 只作歷史；`todo.txt` 才是現在的任務。
- 每次完成記錄：任務 ID、行為 Before/After、檔案、測試命令與結果、Commit SHA、未解風險。
- **文件同步**：修改協定、AFK、AOI、素材格式或測試 Gate 時，同步核對 SPEC／對應 ARCHITECTURE／TEST_PLAN／TODO；歷史研究彙編僅供證據，不為修正現行設計而改寫歷史原文。

## 多 Session 安全

未提交檔案不屬於清理任務可隨意覆蓋的垃圾。若有任何其他 Session 尚未提交的修改，先確認檔案所有權；本次已整合的 Height-Dense/PCK 只從唯一 `main` 維護，不要另建需要長期同步的轉換器分支。
