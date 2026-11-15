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

## 不能違反的架構契約

- **資產**：一個原素材一個獨立 logical/physical Asset（Enemy / NPC / Scenery / Player part / Equipment 等）；Frame 高度排序、POT 尺寸只是同一 Asset 內策略。禁止跨素材合圖，怪物目錄使用 `Enemy/`，不得多一層 `generation/`。
- **來源**：`Old`、`Recovered`、`TestGoGo` 為唯讀研究來源；未有發佈權時不可把原圖、音效、反編譯文本複製進公開 Git。
- **Client**：network transport、message codec、state reducer、prediction、render/UI 分工明確；AFK/Unfocus/Render FPS 只能改變畫面/音效，不可節流可靠事件、收包與固定遊戲邏輯。
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
