# SGTest 開發與 AI Session 交接規則（2026-11-15 重建版）

## 現況與使用者決策

使用者決定**保留規格、研究證據、素材轉換程式與必要測試，刪除舊 Client/Server 原型，依文件重新設計實作**。不得自 Git 歷史還原舊 Client 或 Server 當成新版本，也不得以歷史 PASS 宣稱現在能運行。

本版取代此前 AGENTS.md 中「舊原型已實作／禁止開始原型」等互相矛盾的工作階段狀態描述。仍須參考 `SPEC.md` 與歷史文件的功能需求；當前工作優先級以 `todo.txt` 為準，功能驗收狀態以唯一正式 `docs/FEATURE_MATRIX.md` 為準；若兩者或測試證據衝突，採較保守狀態。

## 開工前

1. 先 `git fetch`，確認正式工作目錄目前位於 `main`，比較本機 `main` 與 `origin/main`，並查看 `git status --short`、`git branch`、`git worktree list`，避免踩到其他 Session 未提交的工作。
2. 先讀 `README.md`、`docs/ARCHITECTURE.md`、`docs/CODE_ORGANIZATION.md`、`docs/CLIENT_ARCHITECTURE.md`、`docs/SERVER_ARCHITECTURE.md`、`docs/FEATURE_MATRIX.md`、`todo.txt`；依任務補讀相關素材／原版證據文件。
3. **一般開發不得為了避開其他 Session 而另建 feature/fix/temp branch、Git worktree 或第二份 SGTest clone。** 若發現其他 Session／AI 遺留的 branch、worktree 或 detached worktree，先檢查其 HEAD、status、diff 與 `main` 是否已包含其成果；只要能安全確認有尚未進 `main` 的有效成果，必須先安全整合進正式 `main`，完成必要衝突處理與測試後才繼續新開發。對 dirty、來源不明或仍可能在使用中的 WIP，不得 reset、clean、覆寫或刪除；無法安全整合時保留並明確回報。
4. 領取一項具體 TODO，先寫預期行為、指標、驗收測試，再動手；完成才勾選，未跑寫 NOT VERIFIED。
5. 確保拆成小 commit，commit 僅包含自己變更的檔案或已明確安全整合的既有成果；每次更新 TODO 與架構決策證據。
6. **能在 GitHub 驗證就先用 GitHub。** Server-only／純公開 repo 工作直接以正式 `main` + GitHub Actions（目前 `.github/workflows/server-core.yml`）跑 build/checks，再考慮 Remote Desktop；只有 Godot Windows 實機、private assets、原版安裝檔、GPU/畫面或 GitHub 無法執行的測試才使用 Remote Desktop Commander。

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

- **功能目錄**：Server／Client 優先依遊戲功能命名，對應 `Map/map`、`Npc/npc`、`Pvp/pvp` 等現有領域。Server namespace 與該功能目錄一致。預設一層功能目錄，按責任、狀態及一起修改的範圍決定是否拆分。兩端按實際職責分層，共用的小型型別可以留在主要責任檔案；尚無獨立程式的功能先記到架構導航的 TODO，等第一個可執行切片進入時再建立正式功能目錄。World 保有狀態寫入權，Gateway 負責連線及路由，Wire 保持協定邊界，Client App 負責場景組裝。功能以窄輸入／結果接入，於實際需要時才加入可替換介面；沿用 [架構模組表](docs/ARCHITECTURE.md#功能模組)。

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
- `docs/` 為規格、研究證據與決策；`docs/FEATURE_MATRIX.md` 是唯一正式功能完成度矩陣，`todo.txt` 是現在的工作優先級；`docs/research/FEATURE_DECISION_HISTORY.md#source-todo-pre-rebuild-20261115-txt` 只作歷史。
- 每次完成記錄：任務 ID、行為 Before/After、檔案、測試命令與結果、Commit SHA、未解風險。
- **文件同步**：修改協定、AFK、AOI、素材格式或測試 Gate 時，同步核對 SPEC／對應 ARCHITECTURE／TEST_PLAN／TODO；歷史研究彙編僅供證據，不為修正現行設計而改寫歷史原文。

## 多 Session 安全與殘留整合

未提交檔案不屬於清理任務可隨意覆蓋的垃圾。每次開始開發都要盤點正式 repo 的 local branches、`git worktree list`，以及可存取 worktree 的 `git status`。若發現其他 Session／AI 留下的 branch、worktree、detached HEAD 或暫存 clone：

1. 先判斷 `main` 是否已包含其 Commit／Diff；已完全包含者才視為「可清理殘留」。
2. 若存在 **尚未進 `main`、但可安全辨識且應保留的有效成果**，必須先把成果安全合併／整合進正式 `main`，解決衝突並跑受影響測試；不得直接刪掉後重做。
3. 若殘留 worktree 是 dirty、仍有未提交檔案、來源不明、疑似仍被其他 Session 使用，或無法判定哪些內容應保留，禁止 `reset --hard`、`clean -fd`、強制覆蓋或刪除；保留現況並明確回報阻礙。
4. 當且僅當已確認必要成果全部存在於正式 `main`、worktree/branch 已 clean 且不再使用，才移除該殘留 worktree／local branch／暫存 clone。
5. 本 Session 自己若因工具限制而不得不建立一次性測試目錄，必須在本次工作結束前移除；一般情況不得建立 Git worktree 或長期 branch。

本次已整合的 Height-Dense/PCK 只從唯一 `main` 維護，不要另建需要長期同步的轉換器分支。

## 高吞吐量完成模式（2026-11-23 使用者決策）

目前目標是讓重複執行 N 次後，SGTest 盡快從「局部很深」收斂成「完整可玩的 MMORPG」，再逐項提高 HK 原版忠實度。**先擴大功能覆蓋率，再提高每項功能的驗證深度**；但登入、資料一致性／遺失、安全、Server Authority、重大多人同步或會污染正式資料的問題，仍必須立即完整驗證，不得為速度降級。

1. **一次推進完整功能群，不要一輪只補一個微小切片。** 同一系統應連續完成能安全完成的多個能力，例如 Pet EXP → Pet Skill → Pet 裝備／卡片 → Pet AI；每輪盡可能形成可玩、可持久化、可多人驗證的垂直功能群，而不是為了小 commit 人為停止開發。
2. **開發中先跑受影響的專項測試，完整回歸集中到交付 Gate。** 不要每個小修改都重跑整套 Server／Godot／素材回歸；累積一批同系統修改後，在準備正式 commit／push 前跑一次必要的完整 suite。若專項測試失敗，先在最小範圍修正與重跑。
3. **原版／反編譯研究必須時間盒化。** 搜尋一輪仍找不到可信規則、數值或公式時，標記 `UNKNOWN`／`NOT VERIFIED`，建立 fail-closed 或可替換的規則邊界後繼續下一能力。不得猜原版數值，但也不得因單一未知常數阻塞整個系統。
4. **文件集中更新。** TODO、Feature Matrix、SPEC／Architecture／Test evidence 以一個可交付功能群為單位集中更新一次，不要每完成一個微能力就中斷實作去做文件 bookkeeping。
5. **優先完成玩法覆蓋，不主動花整輪補小型 architecture 洞。** 除非是 blocker、可靠性／安全／權威性問題或會讓後續功能大量返工，否則優先推進主要玩法。當前整體方向可依實際 TODO／依賴調整，但應朝 Pet → Mecha → Guild → Friend → Stall → Quest → PvP 完整玩法 → Original UI → 效能／記憶體持續推進，而不是長期停留在基礎設施微調。
6. **原版差異可驗證但不阻塞時先保留狀態。** 例如精確攻速、掉落倍率、Pet EXP 真值未取得，不應讓整個系統停住；先做到可玩、可存、可同步，該差異保留 `NOT VERIFIED`，之後集中做 HK／Paradise／Old A/B 收斂。
7. **每輪優先提升可玩完整度。** 「最有價值的工作」應優先解釋為：解除不能玩／不能登入／資料風險／嚴重同步問題，其次完成一個大型玩法系統的多個缺口；不要因為某個小問題容易驗證，就反覆選擇小型基礎工作。
8. **品質 Gate 不取消，只改成分層與批次執行。** 不得刪測試、降低驗收門檻、把未執行測試寫成 PASS、用假資料冒充原版證據，或省略正式交付前必要的回歸與 Git 同步。

## Delivery cadence / validation tiers

Keep quality gates, but do not repeat expensive validation without a reason.

1. **Tier A - ordinary code/server/docs changes:** run the narrowest relevant test first, build only affected projects, then run the relevant GitHub Actions workflow once on the final branch head before merge. Do not run Godot/PCK/private-asset/full-archive tests for server-only work.
2. **Tier B - rendering/private source/platform behavior:** use Remote Desktop only when the result depends on Godot Windows, private original assets/recovered executable evidence, GPU rendering, PCK/Release packaging, or platform-specific behavior. Run the minimum targeted private test, then the normal public CI gate.
3. **Tier C - architecture/algorithm choice:** use ALT001 only when multiple materially different production approaches are plausible. Compare them once under the same representative workload, choose one production path, document the decision, and do not rerun the benchmark unless a later change can materially alter the result.
4. Batch repository reads/edits/tests where practical. Do not repeatedly fetch the same files or rerun a full suite after every small edit. A failed targeted test should be fixed and rerun at the narrowest scope first.
5. Update TODO/spec/architecture/test evidence once per deliverable slice, immediately before final CI/merge. Never trade correctness, security, fail-closed behavior, concurrency safety, source fidelity, or reproducible tests for speed.

## 可見 UI／Windows 實機驗收 Gate（2026-11-23 使用者決策，最高優先級）

**凡是玩家看得到、點得到、操作得到的 Client 變更，都必須在交付前跑真正的可見 Windows Godot Client；headless、fixture、mock、單元測試或自動 Scene 測試只能當前置 Gate，不能取代 UI／UX 實機驗收。**

1. **強制範圍**：只要本輪修改 Client UI、HUD、登入／選角／創角、背包、裝備、Pet、Mecha、社交／交易／商店／任務／PvP 面板、Renderer、動畫、移動呈現、滑鼠／鍵盤輸入、解析度／縮放／視窗生命週期或任何玩家可觀察行為，就必須執行本 Gate。純 Server／純文件且完全不影響玩家可見行為才可略過。
2. **必須使用當前正式 main 的真 Runtime**：先完成必要自動測試，再以目前 `main` 建置／啟動真 .NET Server（使用隔離測試 DB／資料，不碰其他 Session），並以 `Godot_v4.7.2-stable_win64.exe` **非 `--headless`** 啟動正式 `client`。不得只跑 test script、截一張預製 Scene 或用 fixture 假資料就宣稱 UI PASS。
3. **必須像玩家一樣操作**：使用真滑鼠／鍵盤／Computer Use 走完整路徑；至少涵蓋登入 → 選角／創角（若相關）→ 進入世界 → 開啟本輪受影響面板 → 實際按鈕／列表／選取／拖放或核心互動 → 關閉再開 → 狀態變化 → 登出／重登確認。禁止只呼叫 `pressed.emit()`、直接呼叫 handler 或只檢查 node property 取代人工操作。
4. **UI 實機必查**：至少觀察啟動卡頓／Windows「未回應」、遮擋、裁切、重疊、Z-order、焦點、Tab／Scroll、hover、disabled 狀態、文字截斷、按鈕可點區、視窗切換、面板關閉／重開、logout teardown、錯誤彈窗與 Godot/Server console error。改到 layout／viewport 時至少檢查 800×600 與目前常用寬螢幕解析度；不能只在單一尺寸看起來正常。
5. **動態行為必須動態驗收**：移動、AOI、攻擊／受擊、技能、Pet／Mecha 跟隨、動畫、傷害數字、玩家同步、切圖等，不得用單張截圖代替。至少實際操作一段連續流程；多人同步相關功能至少開兩個真 Client 視窗，除非本輪只改純單人 UI。
6. **保留可追溯證據**：必要時保存實機截圖／短錄影、Console log、Server log、版本 SHA 與重現步驟；私人素材與帳號資料不得提交公開 Git。原版／Paradise/HK 能執行時，涉及原版 UI／手感的功能應同步做 A/B；無法執行就維持 `NOT VERIFIED`，不得用程式推測取代。
7. **看到一個可見 bug 就不能交付**：只要實機操作發現本輪功能或既有主流程有明顯 UI／UX／Runtime regression，本輪狀態回到 `IN PROGRESS`／`TEST FAILED`；先修、重跑 targeted automated tests，再重新跑可見實機 Gate。不得因 headless／C# checks 全 PASS 就宣稱 `VERIFIED`／`COMPLETE`。
8. **使用者要求「看目前成果」時直接開給他看**：應啟動真 Server + 真可見 Client、切到前景並提供可登入的隔離測試環境；不要只回覆啟動指令或說「之後會測」。啟動本身若出現超長 loading、未回應、黑畫面或初始化錯誤，也算實機缺陷並需記錄／修正。
9. **多 Session 安全仍優先**：不得為了實機驗收殺掉來源不明的 Godot／Server、覆寫他人 DB、搶既有 port 或清除未知暫存；一律另用安全 port／隔離 DB，除非已確認既有程序就是本 Session 建立且可安全重啟。

### 每輪 Client 交付必跑：最低可玩 Smoke Gate

**只要本輪有任何 Client／UI／Renderer／輸入／玩家呈現修改，除了測本輪功能，還必須用真可見 Client 跑以下核心路徑。這是跨系統回歸 Gate，不能因「本輪沒改那裡」而省略。任一項失敗，本輪不可交付。**

1. **登入後靜止外觀**：登入選角進世界後至少原地停留 10 秒，不按移動鍵；角色外觀、髮型、衣服、裝備、名稱不得退回預設或占位外觀。接著走路、停下、再走，外觀不得因動作切換才突然恢復。這一項專門防止「剛登入是預設人物，走路後才變成自己的角色」。
2. **換圖後仍可操作**：至少完成一次 Map15 ↔ 另一張已支援地圖的真傳點／換圖，確認新地圖 WorldReady 後可以點地面走路、長按走路、停止、重新下指令；不得殘留 input lock、pending map、舊 epoch 或透明遮罩導致不能走。
3. **HP／MP 真值與變化**：登入後 HUD 與角色資料面板的 Current/Max HP、MP 必須和 Server authoritative state 一致；再實際造成 HP 或 MP 變化（受擊、技能、喝水／恢復等可用路徑），確認 HUD、個人資料、Server 狀態同步更新，不能只驗證初始文字存在。
4. **個人資料完整可用**：打開角色／個人資料視窗，至少確認角色名、等級、職業／可得身份欄位、EXP、HP、MP、主要屬性 STR／INT／DEX／VIT／AGI 等應存在資料都有顯示；可分配屬性時實際點擊至少一個可用按鈕並驗證 Server 結果，不能分配時必須有正確 disabled／剩餘點數狀態，不能空白或有按鈕卻無作用。
5. **基礎面板開關**：背包、裝備、Pet、Mecha 及本輪相關主要面板至少各開啟／關閉一次，確認不互相遮死、不失焦消失、不產生無法關閉的 modal，不因切換導致 HUD 或世界輸入永久失效。
6. **換圖後資料仍正確**：換圖完成後重新檢查角色外觀、HP/MP、個人資料、背包／裝備核心狀態，避免 WorldReady／map reset 把 presentation 或 player profile 清成預設值。
7. **登出／重登入**：登出回登入畫面，再用同角色重登；確認外觀、HP/MP、個人資料、可持久化狀態正常恢復，且舊 UI node／pending action 不殘留。
8. **至少雙 Client 多人 Smoke**：啟動兩個非 headless 真 Client，兩者登入同地圖，確認彼此 Spawn、名稱／外觀、走路、停止、離開視野再回來、換圖／登出 Despawn。至少做一次可用的玩家互動或本輪涉及的多人功能；若 Trade／Party／Friend／PvP 等尚未完成，保持 `NOT VERIFIED`／`IN PROGRESS`，不能因 wire test PASS 就說多人正常。
9. **戰鬥 Smoke**：若目前版本已有戰鬥，至少實際打怪一次，確認目標選取、接近／攻擊距離、普通攻擊或技能、受擊、HP 變化、MISS／傷害數字、死亡／清除目標的可見流程沒有明顯錯誤；本輪碰到戰鬥或同步時必跑雙 Client 觀察他人動作。
10. **實機 Console Gate**：上述流程中持續檢查 Godot error/warning、Server exception、連線斷開、Windows 未回應。任何會影響玩家體驗的錯誤都算失敗；不得只看畫面「好像能動」。

**最低可玩 Smoke Gate 的目的不是增加形式測試，而是保證每次 main 的可見成果至少維持「登入正常、人物資料正常、能走、換圖後能走、HP/MP 正確、核心 UI 可操作、雙 Client 基本同步正常」。若連這些都未通過，就不能對使用者說目前版本已有明顯進步。**

## Main-Only Git policy（2026-11-23 使用者決策，最高優先級）

**SGTest 正式開發一律以 `C:\SGO\SRC\SGTest` 的本機 `main` 與 `origin/main` 為唯一開發／整合／交付主線。** 一般開發禁止建立新的 feature/fix/temp/release branch、Git worktree、第二份 clone 或其他長期正式工作目錄；不得把「避免衝突」當成另開 worktree/branch 的理由。

每次開始工作時必須先 `git fetch origin`，檢查正式 `main`、local branches、`git worktree list` 及各可存取 worktree 的 status。若發現其他 Session／AI 的殘留，遵守以下順序：

1. 先比較該 branch/worktree 的 HEAD、commit graph、diff 與正式 `main`，確認哪些成果尚未存在於 `main`。
2. **只要存在可安全辨識且應保留的有效成果，先合併／整合回正式 `main`，再開始新的功能開發。** 依歷史關係採安全 fast-forward、merge 或 cherry-pick；禁止 force push、硬 reset 或以覆寫方式「整合」。
3. 合併後必須解決衝突、跑受影響測試並確認 `main` 的結果完整；不得因合併麻煩而丟棄別人的有效成果或重新做一份。
4. 若殘留是 dirty/uncommitted、來源不明、疑似仍在執行中的 Session，或無法判斷內容是否完整，先保護原狀，不得刪除；能安全完成並保存其成果時才整合，否則明確回報 blocker。
5. **清理必須在整合之後。** 只有確認必要成果已全部存在於正式 `main`、branch/worktree clean 且不再使用，才移除該殘留 local branch、worktree 或暫存 clone。
6. 本 Session 不得留下自己建立的 branch/worktree/tmp clone；一次性非 Git 測試目錄可使用，但任務結束前應移除。

每次交付前執行 Git 清理 Gate：確認目前分支為 `main`、重新 fetch、`main...origin/main` ahead/behind 為 `0/0`、本機與遠端 SHA 相同、正式工作樹沒有本次任務遺留的未提交程式修改，且本次建立的暫存 Git branch/worktree 全部已清理。其他 Session 尚未安全整合的 dirty WIP 可以暫留，但必須明確列出，下一輪優先處理整合，不得把它永久當正常狀態。
