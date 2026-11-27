# SGTest AGENTS.md — v21.1 玩家實測優先 / 原版文字精準 / 真功能互動

適用 C:\SGO\SRC\SGTest；唯一正式執行規範。v21.1 取代先前 v11–v21 衝突規則。docs/SCHEDULER_PROMPT.md 與 C:\ChatGPTScheduler\prompt.txt 是**精簡執行入口**，本檔保留完整來源、安全、架構與驗收契約；最新玩家實測回饋永遠高於舊 COMPLETE/PASS、里程碑與自動測試結論。詳細架構/原版研究/Smoke 按需查 docs/AGENT_REFERENCE.md。

## 0A. 2026-11-27 玩家實測強制覆蓋（最高優先）

最新正式手動測試已由玩家明確判定：**登入後 UI 不像原版、怪物血條 UI 錯誤、打怪邏輯錯誤、戰鬥 UI 錯誤。** 因此所有受影響的舊 `COMPLETE/PASS` 都只能視為歷史技術證據，不能再當目前完成狀態。

### 狀態優先權
`最新玩家實測 > Windows native visible 同狀態 A/B > 原版 source evidence > automated/headless/AES gate > 舊 todo/FEATURE_MATRIX`

只要玩家回報「不一致／錯誤／不能用」：
- 受影響功能立即視為 `TEST FAILED / IN PROGRESS`，即使 todo/matrix 尚未改字也一樣。
- 下一輪先重現玩家看到的同一畫面/操作，再修正式 Client/Server。
- 必須用最新正式 Windows Client 做同狀態原版 A/B；沒有 native A/B 只能 `NOT VERIFIED`，不得重新標 COMPLETE。
- 禁止用「之前 gate PASS」「功能有回封包」「測試有數字」反駁玩家實測。
- 修正後同步把 todo.txt / docs/FEATURE_MATRIX.md 的假 COMPLETE 降級或改回真實狀態，避免 scheduler 再跳過。

### 當前唯一修復序列
在以下三項依序完成前，**禁止切去傳點 600、NPC/Quest 或其他新功能**：

1. **P0-HOTFIX-1 登入後整屏 HK UI 1:1**
   - 玩家進世界第一眼看到的完整 800×600 baseline：底部 HUD、HP/MP/WP/EXP/Level/金錢、聊天、快捷列、右側 COMMAND、玩家/NPC/怪物名稱與目前預設可見浮動 UI。
   - 每個 control 必須對 HK ITF 的 ID/group/rect/state、原 SPR/frame、origin/trim/pivot/alpha、font/size/weight/baseline/color、normal/hover/pressed/disabled/selected、hitbox/focus/drag/Z-order。
   - **文字先做 existence audit：原版該位置沒有文字就不得自行新增；原版有文字才允許顯示。** 原版只有 icon/SPR 的按鈕禁止疊自製 Label/caption/tooltip/debug text；原版文字已烘焙在 SPR/背景時禁止再疊動態 Label。
   - 原版有動態文字時，內容/繁中用字/標點/font face/size/weight/color/outline/shadow/alignment/clip/wrap/local rect/baseline/ascent/descent 必須依來源；位置以 ITF rect + draw/font handler 為準，不靠 magic offset。
   - 任何原版沒有的額外文字、開發者提示、entity id、HP debug、測試 caption、重複/錯字都必須移除；逐畫面檢查缺字、多字、雙字、錯位、裁切、baseline。
   - 原圖存在就不得自畫、不得 placeholder、不得相似圖替代、不得用魔術 offset 疊到「看起來差不多」；寬屏只能擴世界，不拉伸 800×600 UI。
   - 不能只驗 scene node rect；必須看最後 Windows 畫面。

2. **P0-HOTFIX-2 怪物血條／選怪／戰鬥 UI**
   - 查清原版怪物何時顯示 HP、血條 SPR/frame/尺寸/相對座標、target/mouse-over/selected 狀態、名稱/等級（原版有才做）、HIT/MISS/傷害數字、受擊/死亡/重生的顯示與消失。
   - 多怪時 UI 必須綁正確 entity；不可漂移、黏錯怪、死亡後殘留。
   - **禁止通用 ProgressBar、自畫矩形或開發者 debug bar 冒充原版怪物血條。**

3. **P0-HOTFIX-3 原版雙向戰鬥＋真互動**
   - 玩家正常鍵鼠完整流程必須是：`正確 hit-test 選怪 → 必要時走到合法距離 → 停步/面向 → 原版攻擊動畫 → 正確時點送 intent → Server authority HIT/MISS/damage → 怪物受擊/死亡 → 正確 UI`。
   - **怪物不能只是木樁。** 玩家命中或觸發原版 AI 條件後，怪物須鎖定正確玩家、依 source-backed 規則追擊/停止/轉向、進入合法攻擊距離後按原版 cadence/animation 攻擊，Server authority 決定 HIT/MISS/damage，玩家 HP/受擊/死亡 UI 正確更新。
   - 點擊座標/camera transform/sprite bounds/透明區 hit-test 不得偏移；超距不能直接打中；需要接近時走正常移動流程。
   - attack range/cadence/recovery/animation timing 必須有原版證據；**玩家或怪物攻擊/技能動畫未完成時不得無限重複送 intent。**
   - 怪物 target/chase/retaliation/range/cadence/target-lost 必須 source-backed；禁止測試腳本直接扣玩家 HP 冒充 AI。
   - Move/Attack/Hit/Death 狀態不可互相蓋掉造成滑步、瞬移、消失、連點爆發。
   - HIT/MISS/damage/HP/death/respawn 全由 Server authority；雙 Client 看同一隻怪與同一場戰鬥必須一致；換圖/重登不能殘留 target/HP bar/combat state。
   - **目前正式畫面中玩家看得到、點得到、原版有 handler 的互動入口都必須真的可用：** 按鈕/分頁/快捷鍵不能 no-op；NPC/世界物件須依原版距離/條件觸發真對話或動作；玩家選取/右鍵/好友/組隊等既有入口須對正確 actor 且有真 Server 結果；怪物須能選/接近/攻擊/反擊；已存在的掉落/拾取流程須有正常 hit-test/authority。未知尚未實作的新系統不因此擴 scope。

### UI/戰鬥/互動完成 Gate
畫面像但功能錯 = FAIL；功能可用但畫面不像 = FAIL；**多一個原版不存在的字也算 FAIL。** 完成前至少需要：
- 原版 Reference 同狀態。
- 最新正式 Windows Client After 同狀態。
- 同 client area / DPI / 狀態，檢查 rect、baseline、hitbox；可安全取得時做 overlay/pixel diff。
- 逐區做文字 existence audit：原版該有的字存在且位置/字型/baseline 正確；原版沒有的字完全不存在；烘焙文字與動態 Label 不可重複。
- HP bar、HIT/MISS、受擊、玩家攻擊與怪物攻擊動畫用連續影格或錄影，不可只拿單張靜態 PASS。
- 正常鍵鼠，而不是 pressed.emit / direct handler / fake snapshot。
- 對目前可見且原版有 handler 的入口逐一真操作：按鈕不能 no-op、NPC/玩家/怪物/世界物件互動要有正確結果。
- 戰鬥須雙向：玩家能正常打怪；符合原版 AI 條件時怪物會追擊/反擊/打玩家；玩家 HP/HIT/MISS/死亡回饋正確。
- 適用時雙 Client、換圖、重登。

## 0. 每輪必須讀取的執行規格（先用已整理證據，禁止從零猜測）

每輪處理 UI/世界呈現/玩法，先按 [原版畫面與功能參考索引](docs/ORIGINAL_REFERENCE_INDEX.md) 找當前 Task 的原圖與缺圖狀態，實際開啟相符原圖。沒有截圖也必須補證、實作、驗功能；不限定已有圖片，不遍讀或反覆整理全索引。

正式任務庫：`docs/legacy_task_catalog_20261124/executable/`。其上層保留原始盤點。每輪用索引定位工作，只讀選中任務與直接相依檔，不全文讀 519 份：
- `README.md` / `EXECUTION_INDEX.csv`：152 UI、163 Gameplay leaf、71 Gameplay integration、133 Asset evidence。任務庫是工作規格，不是完成度；不能將初始 NOT VERIFIED 解讀成每項都要重做。
- `ui/UI-*.md`：每個 ITF 的原始控制項 offset/type/ID/group/local rect、文字候選、SPR、幀數、origin 與反編譯函式、逐控件驗收。優先使用 `UI_CONTROL_METADATA.json` / `SPR_FRAME_METADATA.json` 取同畫面精確資料，禁止目測猜位置或直接取 screenshot 當整張 UI。
- `gameplay/GP-*.md`：以最小可玩 leaf 為開發單位；父整合任務在子項驗收後關閉，不重複實作。玩法引用的 UI MD 一併讀取。無專屬 ITF 的世界動畫/游標/效果也必須驗可見呈現。
- `assets/ASSET-*.md`：只在當前功能需要或排到素材證據時查；未證明可達的素材不可被當成新玩法。
- 來源不足依序定向補查：既有任務與有效證據 → HK ITF/SPR/.lst 與現有正式 converter → `C:\SGO\Recovered\Old`（ClientData/Paradise.exe.lst、服務端/plugins/*.dll.lst、Dat、cfg、資料庫；追 control handler、動畫/效果、數值、時序、條件、資料與封包）→ `C:\SGO\Recovered`（圖檔名稱、素材路徑、場景/原有實作交叉參考）及既有 Paradise 截圖/授權原版操作。**本輪實作每個選中功能，盡可能對到反編譯的原版入口/OnUpdate/Click/Key/Draw/封包 Handler、完整文案與其字型及實際狀態；禁止只看 PNG 就猜邏輯。** HK 版 UI 最終優先，Old/Recovered 版本差異須標記；所有參考來源唯讀。不得因找到相似名稱就認定正確素材。
- ITF COUNT_MATCH 是結構數量吻合，不代表控件語意或畫面已驗；原始 group、sentinel、自動尺寸、重複 ID、動態 SPR、frame-state、font/handler 要按實際 loader 或原版交互核對。數值與行為無證據不猜，已有確認資料不反覆研究。

## 1. 產品目標與強制里程碑（Playable First）

**交付真正可玩的正式 Godot Client + .NET Server；HK 原版畫面、操作、功能、數值、動畫與手感一致，多人正確流暢低資源。** 使用者實測優先於過期 PASS；已知 FAIL 不得用測試數、commit 數、fixture 截圖或其他功能 PASS 遮掩。

### 當前工作焦點：2026-11-27 玩家回報 HOTFIX（優先於所有 M1–M6 與舊 COMPLETE）

**每輪直接修正式程式，不要只補測試或研究。當前只依 §0A 的順序工作：整屏 UI 圖片＋文字 1:1 → 怪物血條/選怪/戰鬥 UI → 原版雙向戰鬥＋目前可見真互動。前一項沒有 Windows 同狀態 A/B 前，不得切下一項。**

- 登入／創角／選角本身已可用，只做必要回歸；登入後整屏任何仍與 HK 原版不一致的 HUD、聊天、快捷列、COMMAND、名稱/頭頂資訊、浮動面板都重新視為 FAIL。
- 聊天、好友、社團、背包、人物/裝備、機甲、小商店、系統等舊 COMPLETE 只保留其 authority/persistence 技術證據；若其可見畫面落在玩家目前指出的「UI 不一致」範圍，UI Gate 必須重新 A/B，不能因舊 COMPLETE 跳過。
- Map15↔Map13 10 次穩定可保留為已驗技術基線，但**傳點 600 暫停**；除非它直接阻塞目前三個 HOTFIX 的重現/驗收，否則不得插隊。
- 怪物與戰鬥現在不是「有 78 隻、有 HIT/MISS 就算完成」；必須同時修正原版血條/選怪/傷害呈現與完整玩家攻擊時序，Server source-backed authority 和 Client 原版 presentation 缺一不可。
- 技能、EXP、loot、NPC 等後續功能只有在三個 HOTFIX 完成後才恢復排程；不能用它們製造新的可玩成果來避開目前 FAIL。


- 2026-11-25 玩家驗收：登入/創角/選角已完成，凍結主動開發（僅回歸修復）；第二次密碼輸入框仍錯位。換圖兩次會卡住、無怪物/普攻/技能、聊天沒有氣泡、頻道/好友操作未通、快捷鍵/背包文字錯位、無測試物品/裝備動態外觀/機甲。以上保留為 OPEN/FAIL，直到同一流程重新實測通過，不得因局部修改關閉。
- P0：崩潰、卡圖、資料損毀、安全/權威及重大同步錯誤立即處理；**上方當前工作焦點是最新使用者指定，優先於下表 M1–M6 原有排程**。下表是完整產品與驗收依賴，不是逐輪必須照順序排隊；不從數百 Task 隨意挑容易完成的小修。任務庫只是證據/驗收索引。
- 不設定每輪 UI/玩法/多人百分比配額；在當前焦點內優先完成玩家可直接看到的完整流程，必要同步、權威與可靠事件一起做。M1–M6 用於追蹤依賴及長期完整產品範圍，不得用「尚未輪到該里程碑」推遲聊天、好友、裝備、社團、連續換圖或怪物/技能等當前需求。

| 里程碑 | 必須達成的玩家流程（以 TODO/Matrix 實測狀態為準） |
|---|---|
| M0 登入基線 | 登入/創角/選角已獲玩家確認；只做必要 smoke 和回歸，不再研究已完成 UI。第二次密碼保留未修，按共用 input renderer 修正。 |
| M1 統一正式測試環境 | 建立單一可重複啟動入口、來源驗證/manifest/capability preflight、獨立穩定測試 DB/帳號/角色；可觀察載入的怪物、技能、EXP/loot、item/equipment/pet/mecha 等功能及缺口；接續 M2 的必要資料必須真正載入。 |
| M2 地圖＋戰鬥 | Map15↔Map13 真傳點至少連續 10 次、每次 WorldReady 後立即正常移動/操作，無卡死；正式可見怪物、選怪/追擊、普攻/MISS/受擊/死亡/EXP/掉落/重生、學習/施放技能/MP/冷卻/動畫。優先 GP-012-01 combat profile / verified population/runtime 接續與 GP-008 跨圖卡死。 |
| M3 背包＋裝備＋快捷鍵 | Server 真實物品/UID/圖示/文字、拾取/藥水/拖移/裝卸、裝備在人物身上的方向與動態 paper doll、快捷鍵配置/移除/頁面/F1–F12 真使用、配點/重登持久化。 |
| M4 基礎 UI 與社交 | 第二密碼輸入、HUD、快捷鍵/包包及人物文字共用 renderer；聊天輸入→Server→本人及對方歷史/頭頂氣泡，頻道/原版大小視窗切換、好友新增/刪除/頁籤/黑名單/線上狀態、拖移/捲動。NPC 及基本 UI 正常操作；不能把背景/圖示/空 callback 當功能。 |
| M5 寵物＋機甲 | 正式角色可取得、召喚/搭乘、裝卸/卡片/升級、移動、方向/動作、攻擊技能、屬性/HP 等 UI 與 Server 權威、重登；NPC 商店/銀行/倉庫/修理/強化等直接依賴一併打通。 |
| M6 多人同步與效能 | 雙可見 Client 在同一正式測試環境互看、戰鬥/掉落/跨圖/交易/組隊/重連一致，依 §5 做混合密度基準與 soak，後續完整 §6 全玩法（社團/任務/PvP 等）及 Release Gate。 |

### 強制接續與禁止逃避
- 開局先讀最新**當前工作焦點**、TODO/Matrix 與上輪 OPEN/FAIL/NOT VERIFIED，優先接續最早尚未做完的玩家可操作流程；從原 Task ID 找直接依賴與驗收證據。M1–M6 與舊狀態不能蓋過最新使用者指令。完成的登入等只做必要回歸；不能以微調 caret/AA/Tab、無關封包防禦或增加測試作主題。
- 同一核心阻塞跨輪保留，直到真玩家可操作且適用 Gate 達標。若缺來源，定向追 原版→converter→verified manifest→正式 loader/runtime→Server 啟動→Client 畫面，使用已驗證規則先打通最小完整可玩切片；不能停在「抽出欄位」或「已 fail-closed」。禁止猜數值、繞過權威或把測試資料升為正式玩法。
- M1 是持續支援的測試環境，不要求先完成所有尚未開發玩法才能進 M2；只要當前切片的必要能力可驗就立即推進，之後逐步補 capability。使用者指定缺陷與獨立直接依賴可並行，尤其共用 input/font/rect、聊天/好友、跨圖；不得以「目前不是這個里程碑」拒絕高優先使用者問題。真阻塞改做同一里程碑或相鄰核心依賴，列明原因、缺證據、下一具體動作；工具兩次失敗用 §2 fallback，不無限重試。
- UI 必須有畫面、文字存在性、座標、字型、baseline、狀態、命中、實際操作、資料/動畫/成敗回饋。**原版沒有文字的地方不得自行加字；原版有文字才按 source rect/font/draw handler 顯示。** 原圖與 ITF 不符、文字多出/重複/錯位、按鈕 no-op、未連 Server、假測試物品都不算完成。第二次密碼/快捷鍵/背包等共同錯位先查實際 rect、anchor、theme minimum、glyph baseline/clip、共用 renderer，避免每輪加魔術偏移。
- 每輪至少交付一個可觀察的新玩家成果（或重現並實質解除最早核心 blocker），完成後立即續作。目標 25～40 分鐘有效開發、完整垂直切片優先，非硬配額；不以 task/leaf/commit 數計量，不 sleep 湊時間。若客觀限制無法交付，照實標未完成，留下可重現失敗、已安全提交進度與下一個可執行步驟；下一輪優先恢復，不將技術前置稱為玩家成果。

## 2. 每輪閉環與統一測試環境

### M1 啟動與資料契約（每輪開局先查）
- 唯一正式 repo/main。建立/沿用單一可重複的本機 playtest 啟動入口（正式 Server build + Windows Godot client + HK preflight），記錄 SHA/build、source manifests 及 hash、content/PCK、map、DB、port、PID、角色與功能設定；重啟預設保留使用者測試帳號/角色/資料，先備份。開發測試用相同 capability/內容設定的隔離 DB/port，不搶占使用者當前手動測試環境；不同 profile 必須清楚標記。
- 已知 2026-11-25 使用者環境 127.0.0.1:17877 的 AES_HOST_READY 顯示 monster_maps=[]、monsters=0、verified_skill/equipment/mecha/loot/shop rules=0、EXP 未啟用；這只證明程序 listening，不是可玩。開局讀最新 Server ready/log、實際載入 population/manifest schema、gameplay_ready、可信角色 combat slot、item/skill/loot/equipment/appearance/mecha 規則；不要盲目沿用舊 EXE 或隨意手寫啟動參數。
- 啟動入口要提供明確的 capability 摘要與當前玩家流程必要項檢查；缺必要玩法回報 FAIL/NOT READY，列缺失檔案/規則/程式及下一步，不把空白 Server 稱為完整。允許標示 limited profile 供獨立 UI 診斷，但不得用於整體 gameplay PASS。**只先接通當前選中功能的必要可信資料與正式 runtime**（聊天/社交/HUD 不應因尚未有完整怪物數值而停止；打怪/技能則不能繞過怪物與技能的權威準備條件）；M2–M5 其餘能力隨可玩流程逐步補齊，不為了數量虛造資料。
- 使用可重複、隔離且明確標示的本機測試帳號/角色與 Server 權威 provision/GM/seed 流程，提供已驗證真實物品 ID、instance/UID、技能、裝備、寵物、機甲及可測目標，遵守交易/持久化與權限；可測發放不等於正式掉落規則，不把測試資料混入產品預設或公開憑證/DB。禁止 client fixture 冒充玩家包包，不因測試環境缺資料而反覆改 UI 空殼。
- 目前使用者測試程序（含 port 17877、既有 DB）視為受保護；不得自動停止、改 DB 或重啟，除非使用者要求或明確交接已閒置。測試時先用同設定的隔離埠，交付再告知如何切換。

### 執行順序
1. 依 §4 安全同步、只清理確認閒置的舊測試資源、接續安全 WIP。**先選 §1 當前工作焦點中最早未通的真玩家流程**，參考 TODO/Matrix、失敗紀錄與對應 Task ID，建立明確驗收步驟；按需讀原版 ITF/SPR/.lst/handler/metadata 及直接相依，不遍讀 catalog。
2. 依 M1 核對正式 runtime capability，重現失敗。原版 UI 先查既有同狀態圖與 ITF/SPR；玩法查真正阻塞的 handler/數值/時序/資料鏈。取得 Before、Server/Client log 與實際症狀；重用有效證據。
3. 修改正式程式/資料產線並做 targeted regression，再走 正常輸入→Client intent→Server 權威/交易→封包/reducer→可見畫面/動畫/成敗回饋→適用重登/雙 Client。跨圖追 WorldReady/MapEpoch/ACK/lifecycle，聊天追輸入/focus→send→權威 routing→history/bubble。不得僅觸發 pressed.emit 或載入假 snapshot 宣稱功能成功。
4. 按 §5 風險分級驗收；更新既有 TODO/Matrix、實際玩家成果/未驗、review diff，明確 commit/push main 後立即續作。來源/manifest/防禦性修正只算前置，不得用來取代 player acceptance。禁止無目的 .lst 全掃、重解包、重建未變 Server、重跑無關 suite。已知同類錯位批次修共用根因，不需逐控件反覆開新任務。
5. 結束前重查當前里程碑與依賴、資源/安全 WIP，能繼續就繼續。只有真實資源限制、主題合理完成或剩餘均具體阻塞才結束。無實機證據的項目如實 NOT VERIFIED，標明下一輪首項；不將 HEADLESS/fixture PASS 轉成玩家 PASS。依 §7 交接。

### Godot MCP（優先使用實際可用工具，不假定能力）
- 使用者已安裝 Godot MCP。每輪需要 Godot 時先發現已連線工具、確認 Godot executable/version 與 project.godot 路徑指向正式 `C:\SGO\SRC\SGTest\client`；若工具提供 get_project_info / run_project / get_debug_output / stop_project / scene 操作等，按實際工具契約使用，不臆造工具或能力。
- 優先利用可用 MCP 查 scene/tree、Control 的實際 rect/anchor/offset/minimum size、theme/font、scale、clip、mouse_filter、z-index、Texture/Animation、runtime error；若 MCP 不提供這類檢查，使用正式 Godot script、Remote scene/debugger、OpenChatX 或 Windows 可見 Client，不能因 MCP 功能缺失卡住。
- MCP 啟動/停止限本輪隔離 Client；不得搶占其他 Session editor/process/port/DB。MCP 的編輯與測試必須反映到正式 repository；不要建平行 demo scene 冒充主遊戲。若無法連線或連續兩次失敗，記錄原因立即 fallback，不重複安裝或無限重試。
- MCP/headless、debug tree、直接觸發 signal/handler 只屬診斷或自動測試；可見 Windows Client + 真 Server + 玩家實際鍵鼠 + 原版畫面/行為 A/B 仍為正式驗收。不得宣稱工具能看到未實際取得的畫面。

## 3. HK UI Gate

### 原版來源
- HK 目標：C:\SGO\Stargate Online IE HK\SGOClient.exe.lst；ITF：C:\SGO\Stargate Online IE HK\output\Interface（含 control ID、rect、state、資源名）。
- 素材固定搜尋順序：C:\SGO\Stargate Online IE HK\Data1_PNG2 → Data1_PNG → Data1.dat（缺失才補解）→ HK Interface/loose files → 其他授權參考。確認原名、尺寸、frame、alpha、origin、trim、pivot、版本；原圖能取得禁止重畫。
- 唯一正式 converter：tools/asset_converter/；UI 解析入口 tools/ui_art/sync_original_ie_ui.py。以現有工具/manifest 補缺，不建第二套正式產線。
- **截圖先查** C:\SGO\SGO Paradise\program\ScreenShot。缺同狀態則可直接啟動 C:\SGO\SGO Paradise\program\client.exe，以既有授權測試帳號實際操作，不重問已提供的資料。只在安全本機測試環境輸入憑證；不記錄到公開 Git/log/報告。Paradise 是舊版；版本衝突以 HK ITF/.lst/Data1 為準，不把舊版 screenshot 當 HK 像素真相。
- HK .lst 只追當前視窗、資源 loader、frame/control ID、字型建立/繪字、狀態和 handler；不足時才深入。Old Server/data：C:\SGO\Recovered\Old；二創參考：C:\SGO\Recovered、C:\SGO\NewVersion\Recovered2；SGOAuto：C:\SGO\SGOAuto（原版狀態觀察、導航、重現操作；必要時唯讀研究，優先正常輸入，正式 Runtime 不依賴原版 process memory/地址）。來源全部唯讀。

### 原版缺圖與功能一致性（強制）
- docs/ORIGINAL_REFERENCE_INDEX.md 涵蓋已有圖及缺圖組，包括社團所有管理子窗、玩家小商店/擺攤、NPC 買賣、雙方交易、銀行/倉庫/修理/強化、寵物/機甲、任務/PvP 等全部 catalog 功能。索引只是導航，不是 scope 限制或新狀態矩陣；新增原版圖隨相關 Task 補入。
- 實際開相關原圖；缺同狀態用授權原版 Client 正常操作補圖/錄影，保留原始畫面、版本/狀態/入口；安全上不能取得時依 HK ITF/SPR/PNG/.lst、Old Server .lst/Dat/cfg/DB、Recovered 等定向補證，按索引 §3。補圖放 git-ignored .cache/ui-parity/reference/<TaskID>/，原版唯讀；不得假造實拍、擅動其他玩家/真實帳號資產或搶使用者桌面。重用有效證據，不另做大盤點；不能因無圖跳過功能或整輪只補圖。
- 每一畫面及可達控件同時還原 **外觀 + 完整原版功能**：入口/條件、事件/資料/規則、輸入命中、Server 權威與成敗回饋、正常/取消/拒絕/重開/換圖/斷線/權限/併發、適用雙 Client 及重登。未接通（空 Callable、假 disabled、只切圖示、fixture 假資料）不得視為完成；本地設定亦驗實際效果。對照原版行為而非照圖猜功能。
- Reference/Before/After 與真玩家操作證據依既有 Task ID 記錄 UI/Gameplay 各 Gate、適用 Multiplayer/Performance/Persistence。畫面像但功能不對、功能可用但畫面錯，均不可標 COMPLETE；缺圖時按已驗來源先實作可玩切片，未知 NOT VERIFIED 並接續，不因逐像素差異阻塞獨立玩法。

### 整屏 HK 驗收
- HK-only 預設保留 skin 切換；不疊錯誤 HUD、不移除地圖/人物/怪物/NPC/地上物或既有功能。HUD HP/MP/EXP 原圖、數值 baseline、動態動畫、F1…文字、右側/背包按鈕、聊天、各面板 tab/關閉/資料都驗。
- NPC 對話、彈窗、按鈕用原版 SPR/frame 組合，能取原圖不自畫。逐控件 ID/offset、origin/rect/frame/圖層、font face/size/weight、繁中 glyph/fallback、baseline/ascent/descent、tracking/line height、alignment/color/outline/shadow/clip/wrap/caret/selection/mask；區分烘焙/動態文字。共用 local pixel origin、透明 padding/trim/pivot，禁止 magic offset、拉伸、Container/theme/anchor/DPI 二次偏移。
- normal/hover/pressed/disabled/selected、focus/Tab/Esc/Enter/hotkey、tooltip、drag/drop/scroll/modal/Z-order、重開、Server 成敗回饋、動態效果/音效適用皆驗。所有指定 HUD/叫出的面板從非互動區拖曳，按鈕/槽位/輸入正常、不穿透地圖、不誤觸。
- 同 client area 800×600 同 DPI/狀態，完整未裁切 Reference/Before/After、逐控件 ROI、50% overlay、pixel diff、rect/baseline/hitbox，已知幾何 ±1px；靜態 ROI 原像素一致。不可 mask 錯位或拿整張截圖當 UI；HP/MP/動畫連續影格/錄影，版本/光柵差異具體標記。未知 NOT VERIFIED、已知不符 FAIL；可玩不等於 1:1。收斂後驗 4:3/16:9/16:10、DPI，寬屏擴視野不拉伸。

## 4. Git / Session / 資源安全（不可降低）

唯一正式路徑 C:\SGO\SRC\SGTest，遠端 https://github.com/kevin840307/SGTest，branch main。開始前快速確認狀態；有真正差異才深入其他 worktree/commit graph/diff。已知、有效且安全的其他成果依歷史 fast-forward/merge/cherry-pick；來源不明或仍使用中 WIP 保護，不覆寫、不刪除；已停止的 SGTest WIP 依下列授權接續及安全整合，不冒認作者。衝突影響本輪就避開或安全整合，不能自作主張破壞。

禁止 reset --hard、clean -fd、force push、覆寫未知 WIP、另建長期 branch 或第二份正式 clone/worktree；測試使用普通 temp、獨立 DB/port/build output。保護其他 Session 程序及使用中的資料；高風險/不可逆另需授權。原圖、SPR/Data1、解包輸出、原版字型/光柵、截圖、反編譯全文、音效、帳密皆不提交 public Git；可提交解析程式、非私有 metadata 與證據摘要。private evidence 放 git-ignored 本機路徑（例如 .cache/ui-parity/），不無故清除仍可重用的證據。

交付前 diff review，僅 commit 本輪及明確安全整合成果，fetch、非 force push main；確認 branch main、HEAD=origin/main、git rev-list --left-right --count main...origin/main 為 0 0；依下列授權清理本輪及已確認的舊輪次測試資源。既有 WIP 依下列規則收斂；僅具體阻塞或活躍部分可保留並回報。

### WIP 接續與收斂 main（使用者已授權）
- 開局必查 git status/diff、未追蹤檔、相關 Task ID、歷史 commit/交接、檔案及 Session 活動；不可把全部 dirty/untracked 一律永久標受保護 WIP。若為上一輪或已結束 Session 的 SGTest 成果，確認無活躍編輯/測試衝突後優先接續；使用者指定既有 WIP 收斂優先於新增非 P0 任務。
- 依功能與相依拆成可驗收切片（如 HUD/技能/素材、NPC/背包/交易、對應測試），審查完整 diff 與新增檔；保留原成果，補缺、修衝突、跑適用測試/實機 Gate。必要時先做本機私有備份（含 untracked，不公開私有素材/憑證）；不得 reset/clean/stash 覆蓋或盲目 git add -A。確認安全且通過適用驗收的切片，以明確檔案或 hunk commit 到 main、push、fetch 驗 0/0；如僅部分驗收，按真實 Gate 保守標記，安全可提交的程式仍可階段性交付，不宣稱 COMPLETE。
- 每輪至少處理一個可安全接續的既有 WIP 切片，直至收斂；只在確實活躍、來源/用途不明、依賴未解或驗收失敗時保留對應檔案，逐組記錄具體原因、下一動作。不能只回報「受保護 WIP」就結束或反覆避開。不得把未審核程式、生成物、私有資產、帳密或測試 DB 提交；其他 Session 正在修改的部分不搶占，先處理獨立切片。

### 每輪測試資源清理（使用者已授權）
- 開局必查上一輪/更舊 SGTest 測試 EXE、Godot 測試視窗、dotnet Host、Bot/Probe、PID/啟動時間/命令列/專案路徑/port/連線與 Session 使用狀態。確認是已結束輪次的閒置測試程序後，直接正常關閉；必要時僅停止已核對 PID 與身分的程序，確認視窗及 port 釋放，不把歷史殘留當作實機 blocker。使用中或身分不明者保護，不能只憑名稱或時間殺程序。
- 可刪除已停止、無引用且可重新建置的上一輪/舊輪次專用測試 EXE/DLL/build output（限已確認的隔離 temp/.cache 測試目錄，逐路徑核對，不遞迴清整個 .cache）。禁止刪正式 Godot/遊戲/Server 執行檔、原版素材、DB、截圖/trace/證據、WIP、系統服務及 Scheduler/ChatGPT。
- 每輪記錄自建測試 PID、啟動時間、用途、DB/port/輸出路徑；結束前保存驗收證據，再關閉本輪測試 Client/Server/Bot、確認釋放 port，移除不再使用的可重建暫存 EXE。若須留給下一輪，明確交接用途與身分；不得無故留常駐程序。

## 5. 風險分級驗收（不降品質、不重複做無關工作）

| 級別 | 變更 | 必須驗收 |
|---|---|---|
| T0 | 只改 Prompt/文件 | diff、規範一致性、Git；不啟動遊戲 |
| T1 | 單畫面 UI/素材/字型/座標 | parse/targeted test + 真 Server/可見 Client 同狀態 Before/After A/B + 受影響控件、資料、開關與鄰近流程；不重跑完整 Server suite |
| T2 | 共用 UI/renderer/input/auth/session/lifecycle | T1 + 所有直接受影響畫面與代表性核心 smoke；多人受影響才雙 Client |
| T3 | Server Authority/Protocol/Persistence/AOI/跨圖/高風險共享核心/里程碑或可信 baseline 失效 | 相關全量自動化、完整核心 native smoke、必要雙 Client、原版 A/B 與持久化驗收 |

完整核心 Smoke（詳見 docs/AGENT_REFERENCE.md §4、§7）：Login/Create/Select→角色初始外觀原地 10 秒→移動/Stop/Retarget/障礙→Map15↔Map13 真傳點、WorldReady 後立即走→HUD/Profile HP/MP 真值與動態變更/配點→背包/裝備/Pet/Mecha 等面板→普攻/技能/MISS/受擊/死亡/掉落/EXP→登出重登持久化→必要雙 Client Spawn/Despawn/AOI/跨圖/互動；觀察錯誤、卡死、黑畫面、超長 loading/reconnect/resource leak。已知 P0/P1 不得用既有 PASS 遮掩；非阻塞 UI P1 依 §1 里程碑與當前投入方向修正；玩家已回報的 FAIL 必須重新開啟。只重用同版本且依賴未變的有效證據；不確定就驗受影響範圍。

Native：C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe，正式 client/ + server/，安全隔離測試資料；Console/headless：C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe。**預設背景執行** Git/編譯/Server/Headless 測試，使用獨立 DB/port；不可對使用者正在操作的前台桌面送 SendInput/搶焦點/最大化視窗。真鍵鼠與可見 Godot 的正式 A/B Gate 需在獨立 Windows VM/隔離桌面，或確認前台安全閒置且不干擾後才做；未取得則標 NOT VERIFIED，保留重現步驟並繼續本輪能安全完成的程式工作，不用 headless 冒充真 UI Gate。headless/mock/fixture/pressed.emit/直接 handler/改 node property 都不能代替真玩家操作。動態項要連續影格/錄影，涉及多人同步/戰鬥/互動要第二個非 headless Client 實際觀察；桌面不可用則如實 NOT VERIFIED，不宣稱已驗。純 UI 不強制每輪跑 334+ Server checks；Server/shared core 高風險才擴到完整相關 suite。

### 多人與效能 Gate
- 依 docs/TEST_PLAN.md「v15 三軌 Release Gate」；移動、戰鬥、AOI、跨圖、交易、網路或共用渲染/生命週期變更驗雙可見 Client 及受影響指標。M1–M5 基礎可玩鏈未通時，優先當前流程的雙 Client/同步正確性與受影響性能；混合 20 人/300 怪/500 Drop 正式基準在可信玩法具備後立即建立，不用空白或合成負載代替。M6 起每三個非緊急交付主題至少一次主動混合基準；資源被占用標 NOT VERIFIED，安排下一可用輪；重大變更/里程碑升階與長測，不搶其他 Session。
- 雙 Client 互看位置、動畫、HP/傷害/MISS/EXP/掉落/物品；AOI 不閃爍/殘影/重複，正常移動不順移、不忽快忽慢，合法傳送/重同步例外留 trace。權威 tick、插值/prediction、動畫解耦，可靠事件不丟不亂序；效能不能犧牲正確性。
- 同硬體/畫質/解析度/場景/密度/網路比較 Before/After，保留原始 trace/錄影與 SHA；超預算定位 CPU/GPU/GC/DB/AI/AOI/queue/網路。不猜測、不用測試數冒充流暢或容量達標。

## 6. 架構與完整產品契約（詳細規格按需讀）

- 正式 Godot Client + C#/.NET 8 Server；modular monolith，依遊戲功能命名。World 權威 state/write owner；Gateway authenticated binding/rate/backpressure/dispatch；Wire DTO/codec；Policy 純規則；Identity/Persistence durable transaction，DB I/O 不阻塞 World Tick；Client transport/reducer/prediction/presentation 分離。
- WIRE001 單一 little-endian compact-binary V1，不並行引入 MessagePack/Protobuf；TLS/Auth、ConnectionEpoch、bounded parser/queue、ACK/WorldReady/reconnect。Server 權威角色/HP/MP/移動/Blocking.dat A*/怪物/戰鬥/技能/物品/貨幣/交易/EXP/掉落/PvP/持久化。英文 semantic domain identifier，顯示顏色不作 gameplay rule；規則常數集中。
- AOI 先 baseline 再 Delta，可靠 Spawn/Despawn/戰鬥/交易/物品事件不可被 coalescing 吞掉；禁止全圖無意義廣播。移動支援 prediction/reconciliation/remote interpolation、長按1秒鎖定及左右鍵停止且右鍵選單不失效；Move 不覆蓋 Attack/Hit/Death；動畫速度與封包/角色移速解耦。
- 素材保留 identity/direction/action/frame/duration/origin/pivot/trim/alpha/draw order/paper doll/world Y-sort；一個原素材一 logical/physical asset，禁止跨素材合圖，Enemy/ 不多 generation/；支援 cache/preload/lazy/reuse/unload/restore。AFK/Unfocus/FPS 只動渲染/音效，不節流可靠收包或遊戲邏輯。效能提供 Before/After（frame/tick P95/P99、CPU/RAM/RSS/GPU/VRAM、draw calls、queue age、load）；不得犧牲正確性/畫質冒充改善。
- Item Definition + Item Instance + Enhancement Level，不以每個 +1/+2 分 ID；UID、ownership、stack/durability/stats、交易原子性；資料驅動 Item/Equipment/Skill/Monster/NPC/Shop/Drop/EXP/Enhancement/Quest。
- 最終完整玩法仍涵蓋登入創角、屬性配點、地圖移動、戰鬥技能、Buff/Debuff、掉落拾取、NPC商店、裝備強化修理、銀行倉庫、交易擺攤、組隊社團好友、任務、PvP、Pet、Mecha、死亡復活、GM；適用 Authority/Persistence/UI/多人/回歸全部要完成。Map15/13 是測試基準，不限正式玩法。
- 詳細契約見 docs/AGENT_REFERENCE.md 與 SPEC.md、docs/ARCHITECTURE.md、docs/TEST_PLAN.md 等相關文件；只在變更涉及該契約時讀/同步，不重寫歷史證據。相同 bug 優先查 owner/lifecycle/epoch/binding/ordering/resource，而非補 delay 或假數值。

## 7. 狀態與交接

todo.txt 是工作優先序，docs/FEATURE_MATRIX.md 是唯一正式功能矩陣；沿用 NOT STARTED / IN PROGRESS / IMPLEMENTED / TEST FAILED / NOT VERIFIED / VERIFIED / COMPLETE。五 Gate 分列 PASS/FAIL/NOT VERIFIED/N/A（N/A 說理由）：UI（HK A/B）、Gameplay（正常操作/權威/數值）、Multiplayer（雙 Client/可靠事件）、Performance（量測）、Persistence（重登/交易）。COMPLETE 須所有適用 Gate、Client/Server、Edge/Regression、原版行為及功能一致；未測不可標 PASS，TODO/Matrix 與實機衝突取保守狀態。

在 todo.txt 最前面維護一份短 ACTIVE MILESTONE（沿用既有 TODO，不另造矩陣）：目前 M 編號/玩家路徑、最近一次真實結果、最早 OPEN/FAIL blocker、下一個具體可執行 Task ID/動作、依賴及 main SHA。每輪開局接續，禁止只加歷史流水帳或遺失未驗項。FEATURE_MATRIX 保存各功能與 Gate 狀態；里程碑只有適用的完整玩家路徑通過才前移。交接只報玩家成果、已驗/未驗、五 Gate、reference/private Before/After/trace、環境 capability、下一步、main SHA、停止原因；不以 class/schema/test/commit 數代替成果。
