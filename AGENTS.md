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
6. **能在 GitHub 驗證就先用 GitHub。** Server-only／純公開 repo 工作優先以 branch/PR + GitHub Actions（目前 `.github/workflows/server-core.yml`）跑 build/checks，再考慮 Remote Desktop；只有 Godot Windows 實機、private assets、原版安裝檔、GPU/畫面或 GitHub 無法執行的測試才使用 Remote Desktop Commander。

## 雙地圖 Map15 + Map13、功能完整的 MVP（不是功能示範）

**MVP = Map15 主城 + Map13 尼西亞台地兩張真實地圖、原版主要功能完整、多人／多怪／多掉落順暢且正確。** 限制的是「全世界所有地圖／怪物／物品的資料量」，不是功能；好友依使用者要求暫緩。

- **先地圖**：先驗證原版 Map15 與 Map13 的 Map／Scenery／Blocking.dat／Map metadata／座標／相機／八方向；兩張圖所需的 Player、NPC、Enemy、Drop 等真素材逐步使用唯一 Converter 產出。已確認原版兩張 Map 檔及 Recovered 兩張 scn 皆存在，但尚未驗證完整資料／轉換。
- **先主城 Map15**：登入／創角選角、Spawn、NPC 商店／銀行／倉庫／修理、裝備／強化／背包、聊天／玩家交易／玩家商店、寵物／機甲及 UI／資產生命週期等在可控場景驗收；以真實 NPC／原版規則為準，若地圖內容不符須記錄。
- **再台地 Map13**：Blocking／A*／長按走路、多人 AOI、八方向／Attack/Hit/Death、怪物各自獨立 AI／追擊、技能／MISS／死亡復活、掉落 Spawn/Pickup、PvP 地圖規則（依原版可否打架），逐項通過 Server Authority、異常／競態與視覺 Gate。
- **雙圖跨圖必測**：至少兩個真 Godot Client 來回 Map15 ↔ Map13；Server 驗證傳點／權限，MapEpoch／WorldReady／AOI 舊圖清除、新圖 Spawn／資源卸載／預取／載入、重新登入／重連與狀態持久化皆需 PASS，不能只靠 fixture。
- **完整功能而非 happy path**：登入／多角色、移動／地圖／傳點、物品 UID、裝備／強化、商店銀行倉庫、技能／Buff／死亡、Pet／Mecha／卡片／升級、玩家交易／玩家商店／聊天、PvP 名稱／GM 等可在兩張圖測的主要系統，需對照原版並有正常／錯誤／多人競態／重試與 Server Authority 驗收。
- **效能按階段升級**：先 2～4 人／少量實體跑通方向與行為，再 Map13 同圖提升至 20 名連線（可含明確標示 Bot）、300 Enemy／500 Drop（含稀疏／密集）；Map15 驗證多人 NPC／商店／玩家商店 AOI，雙圖同時活躍並驗證換圖成本。至少兩個真 Godot Client 驗證畫面、1/2/4 開及 AFK；記錄 CPU/RSS/VRAM、Network bytes/sec、Queue/World Tick/Frame P95/P99。
- **測完方案再往下**：依 ALT001 對有實質差異的 Wire/AOI/AI/Atlas/Renderer/Cache 等用相同資料實測、選可維護的最佳解，MVP 通過後擴張時再驗證一次。
- **MVP 後**才擴全部地圖／全部原版資料與 100 Player／1000 Enemy／5000 Drop、8 開及跨平台完整發行。不得把單一 Map13 大負載當作雙圖傳點 PASS，或把兩圖 PASS 當全地圖 PASS。

逐步切片：兩張原版地圖資料 → Map15 地圖與基礎 UI／登入 → Server／移動 → Map13 A*／多人 AOI → **雙向傳點** → 原版主要系統完整 → 多怪／多掉落／效能 → MVP Gate；每一階段補測試、同步 SPEC／TODO，未跑標 NOT VERIFIED。

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

- **功能目錄**：Server／Client 優先依遊戲功能命名，對應 `Map/map`、`Npc/npc`、`Pvp/pvp` 等現有領域。Server namespace 與該功能目錄一致。預設一層功能目錄，按責任、狀態及一起修改的範圍決定是否拆分。兩端按實際職責分層，共用的小型型別可以留在主要責任檔案；只有已確認功能才用 README／TODO 預留位置。World 保有狀態寫入權，Gateway 負責連線及路由，Wire 保持協定邊界，Client App 負責場景組裝。功能以窄輸入／結果接入，於實際需要時才加入可替換介面；沿用 [架構模組表](docs/ARCHITECTURE.md#功能模組)。

- **資產**：一個原素材一個獨立 logical/physical Asset（Enemy / NPC / Scenery / Player part / Equipment 等）；Frame 高度排序、POT 尺寸只是同一 Asset 內策略。禁止跨素材合圖，怪物目錄使用 `Enemy/`，不得多一層 `generation/`。
- **來源**：`Old`、`Recovered`、`TestGoGo` 為唯讀研究來源；未有發佈權時不可把原圖、音效、反編譯文本複製進公開 Git。
- **Client**：network transport、message codec、state reducer、prediction、render/UI 分工明確；AFK/Unfocus/Render FPS 只能改變畫面/音效，不可節流可靠事件、收包與固定遊戲邏輯。
- **Wire 選型**：2026-11-16 WIRE001 已依公平實測選定單一 little-endian compact-binary V1；正式 Runtime 不加入 MessagePack／Protobuf 第二套。後續直接在此契約上完成 TLS/Auth、ConnectionEpoch identity binding、bounded parser/queue、ACK/WorldReady/reconnect；只有新證據顯示重大問題時才重開選型，並必須重新跑相同 Gate。
- **AOI 優先**：Server 先以權威 map/position + 受限 viewport/interest 過濾玩家、怪物、掉落物，先訂閱再發 Delta；可靠 Spawn/Despawn、戰鬥、物品、交易事件不得被合併快照吞掉。避免全圖廣播、群體同步怪物動作及每 Tick 無意義的重複狀態。
- **動畫**：不因 Move 事件不當覆蓋 Attack/Hit/Death；保留方向、ACT timing、trim/origin、flip、紙娃娃 draw order、地圖物件與所有角色共同 Y-sort。
- **Server**：authority 管理位置、Blocking.dat 碰撞、戰鬥、物品、金錢、交易；World 單寫者設計，外部 DB I/O 不能卡住 Tick；網路與遊戲規則分離。
- **Server 規則／命名契約**：Domain、Wire、Persistence、Host CLI/config 的識別字、enum、token、key 一律使用英文 semantic naming；中文／其他語系只存在 Client/UI/localization 或人類文件，不可作為 Server gameplay 判斷條件。像 PvP 的 Pink/Red 是 semantic enum state，不直接 hardcode 顯示色/RGB。玩法時間、距離、倍率、上限、cooldown、狀態轉換等不可散落 magic number，必須集中在對應 subsystem 的 `Policy`／`Options`／`static readonly`／`const`，測試亦引用同一公開契約或用明確 fixture 參數，不複製規則常數。
- **Server 分層契約**：`Host` 只做 composition/config/CLI adapter；`Gateway/Connection` 做 authenticated binding、rate/backpressure/dispatch；`Wire` 只做 versioned DTO/codec；`World/Coordinator` 執行 authoritative domain state transition；`Policy` 保持可測的純規則；`Identity/Persistence` 負責 durable transaction，不反向決定 gameplay；Client reducer/presentation 不回滲 Server。這是本專案採用的常見 modular-monolith game-server 寫法，除非有實測瓶頸，不為「像大型遊戲」而拆微服務/ECS/事件總線。
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

## Delivery cadence / validation tiers

Keep quality gates, but do not repeat expensive validation without a reason.

1. **Tier A - ordinary code/server/docs changes:** run the narrowest relevant test first, build only affected projects, then run the relevant GitHub Actions workflow once on the final branch head before merge. Do not run Godot/PCK/private-asset/full-archive tests for server-only work.
2. **Tier B - rendering/private source/platform behavior:** use Remote Desktop only when the result depends on Godot Windows, private original assets/recovered executable evidence, GPU rendering, PCK/Release packaging, or platform-specific behavior. Run the minimum targeted private test, then the normal public CI gate.
3. **Tier C - architecture/algorithm choice:** use ALT001 only when multiple materially different production approaches are plausible. Compare them once under the same representative workload, choose one production path, document the decision, and do not rerun the benchmark unless a later change can materially alter the result.
4. Batch repository reads/edits/tests where practical. Do not repeatedly fetch the same files or rerun a full suite after every small edit. A failed targeted test should be fixed and rerun at the narrowest scope first.
5. Update TODO/spec/architecture/test evidence once per deliverable slice, immediately before final CI/merge. Never trade correctness, security, fail-closed behavior, concurrency safety, source fidelity, or reproducible tests for speed.

## Git branch policy (2026-11-16 user decision)

**Keep only `main` as the active delivery branch.** Do not create any new feature/work branches for ordinary GitHub-connected work. Prepare isolated Git objects, read current `main` immediately before updating, and publish with a non-force fast-forward `update_ref` guarded by the exact expected SHA. If `main` advanced, re-check overlapping changes and redo the affected checks; never overwrite concurrent work. Run the required GitHub Actions on `main` and fix failures. A branch deletion tool is not exposed by the current GitHub connector; do not claim old branches are gone or delete another Session's WIP. The legacy worktree/branch instructions above still apply when genuinely sharing a Windows worktree with uncommitted changes, but do not imply creating a remote branch.
