# SGTest AGENTS.md — v14 Task-driven Delivery / HK Full-screen Parity

適用於整個 C:\SGO\SRC\SGTest。此檔是唯一常駐執行規範；舊 v11–v13 Prompt 或詳細參考中的衝突規則不得覆蓋本檔。docs/SCHEDULER_PROMPT.md 是排程入口。詳細架構、原版研究、完整 Smoke 清單保留在 docs/AGENT_REFERENCE.md，依工作需要查閱，不要求每輪讀全文。

## 0. 每輪必須讀取的執行規格（先用已整理證據，禁止從零猜測）

正式任務庫：`docs/legacy_task_catalog_20261124/executable/`。其上層保留原始盤點。每輪用索引定位工作，只讀選中任務與直接相依檔，不全文讀 519 份：
- `README.md` / `EXECUTION_INDEX.csv`：152 UI、163 Gameplay leaf、71 Gameplay integration、133 Asset evidence。任務庫是工作規格，不是完成度；不能將初始 NOT VERIFIED 解讀成每項都要重做。
- `ui/UI-*.md`：每個 ITF 的原始控制項 offset/type/ID/group/local rect、文字候選、SPR、幀數、origin 與反編譯函式、逐控件驗收。優先使用 `UI_CONTROL_METADATA.json` / `SPR_FRAME_METADATA.json` 取同畫面精確資料，禁止目測猜位置或直接取 screenshot 當整張 UI。
- `gameplay/GP-*.md`：以最小可玩 leaf 為開發單位；父整合任務在子項驗收後關閉，不重複實作。玩法引用的 UI MD 一併讀取。無專屬 ITF 的世界動畫/游標/效果也必須驗可見呈現。
- `assets/ASSET-*.md`：只在當前功能需要或排到素材證據時查；未證明可達的素材不可被當成新玩法。
- 來源不足依序定向補查：既有任務與有效證據 → HK ITF/SPR/.lst 與現有正式 converter → `C:\SGO\Recovered\Old`（ClientData/Paradise.exe.lst、服務端/plugins/*.dll.lst、Dat、cfg、資料庫；追 control handler、動畫/效果、數值、時序、條件、資料與封包）→ `C:\SGO\Recovered`（圖檔名稱、素材路徑、場景/原有實作交叉參考）及既有 Paradise 截圖/授權原版操作。HK 版 UI 最終優先，Old/Recovered 版本差異須標記；所有參考來源唯讀。不得因找到相似名稱就認定正確素材。
- ITF COUNT_MATCH 是結構數量吻合，不代表控件語意或畫面已驗；原始 group、sentinel、自動尺寸、重複 ID、動態 SPR、frame-state、font/handler 要按實際 loader 或原版交互核對。數值與行為無證據不猜，已有確認資料不反覆研究。

## 1. 目標與工作選擇

**先讓玩家真的能玩完整遊戲循環，同時逐步達到 HK 1:1；禁止登入像素級細節長期阻塞其他玩法。**

- P0：無法登入／移動／換圖、崩潰、資料損毀、權威或重大多人錯誤，先修；只修阻塞本輪可玩流程的問題。字型 AA、caret、Tab 細節等非阻塞差異留 UI backlog，不冒充已驗。
- 預設 **Gameplay-first + UI Parity 雙軌**。每三個非緊急有效交付切片至少兩個推進未完成的核心玩法，一個集中改善遊戲內 UI；沒有可做的 UI 差距可改為玩法。跨 Session 以最近 TODO/Matrix/commit 的實際成果判斷，不以啟動次數計算。已可登入進世界的 AUTH 不得自動成為下一輪主題，除非阻塞玩法、使用者指定或排到 UI 輪。
- 玩法按可玩鏈選最早尚未可玩的環節，先檢查現有實作，補缺而非重寫：① Map15→Map13 正式新角色可戰鬥（優先 source-backed combat profile、資料、權威規則，解除真正 blocker，不用 SyntheticTest、假數值或關閉 fail-closed）→ ② 選怪/靠近/普攻/MISS/受擊/擊殺/EXP/掉落/重生 → ③ 技能/MP/冷卻/動畫/單體與範圍 → ④ 撿取/背包/使用藥水/裝卸/屬性與重登 → ⑤ NPC 商店/金錢/銀行/倉庫/修理/強化 → ⑥ 多人 AOI/組隊/交易 → ⑦ Pet/Mecha/其他玩法。可依實際依賴調整，例如背包是戰鬥前置就先做。
- 每輪以完整可玩主題（3～5 個實質相關 leaf 為目標）或整個 UI 畫面為首要交付；同一功能的相關 leaf、同屏多個控件或共同根因應批次實作與驗收。完成首項立即接續相依或獨立下一項，有安全時間與資源就繼續，不以只改一個 Label、加一個測試、建立空殼、寫規劃或湊 commit 當交付。不要為追數量縮水測試；若僅部分可驗，保留明確未完成項。切片要真的能正常操作、權威結果正確、適用持久化及多人一致。
- 不得把 UI 與功能拆成互不相連的假交付：玩法 leaf 的最小真實入口、狀態顯示、成功/失敗回饋是該玩法驗收必要項；UI 任務必須可從正式遊戲觸發，所有適用按鈕/輸入/拖曳/資料更新能運作。可玩但視覺未對齊只能記可玩 gate 通過、UI 未通過；不能標整體 COMPLETE。
- UI 軌優先整個世界 HUD（含地圖、角色、NPC 可見性、HP/MP、快捷鍵、底部/右側功能入口）、背包/裝備、人物資料、技能、NPC 對話；按最大可見差異與共同根因批次修復。同畫面原圖、字型、文字位置、hitbox、交互均保留 HK 1:1 最終 Gate；非阻塞未對齊差異記入明確 backlog，仍 IN PROGRESS/NOT VERIFIED，**不阻止其他核心玩法交付**。同一非阻塞 UI 切片最多連續一輪；除非使用者指定，不連續耗在登入/選角/創角。
- 證據不足或其他 Session 正在處理同切片，記錄具體 blocker/下一步，改做獨立的最高優先未完成切片。禁止以缺精確 glyph/AA 證據為理由停住所有遊戲開發。未知正式數值不得猜，仍 fail-closed；不得弱化權威、安全或持久化。
- 使用者明確指定工作優先。內部可現代化，玩家外觀、操作、流程、時序、數值與動畫依 HK 可驗證證據。完整功能與 UI 的 COMPLETE 標準不變；階段性可玩交付與最終 1:1 是不同 gate。

## 2. 每輪快速閉環

1. 合併一次 Git/Session 檢查：status、branch/HEAD、fetch、ahead/behind、branch/worktree；只讀相關 TODO、Feature Matrix、上一輪有效證據及受影響程式。先確認沒有其他 Session 正在處理相同切片或操作同一桌面/DB/輸出。
2. 從 EXECUTION_INDEX 選具體 Task ID；讀 MD、metadata、既有程式與 TODO/Matrix，列出未完成核取項目、相依性與本輪可驗收輸出。玩法先真實重現玩家流程；UI 先取得同狀態原版與正式 SGTest Before（800×600、同 DPI），並排/overlay/diff；整屏盤點差異，非只看本次想修的單一控件。
3. 玩法先對照原版 handler、輸入/前置/合法性、Server 權威變更、封包/reducer、UI 及適用存檔和多人。UI 由任務庫直接形成 control inventory（ID+offset、資源/frame、rect、font/baseline、hitbox、state），逐項比對 scene/node 實際屬性與畫面，找出共同根因。已有有效證據直接沿用；缺什麼才定向反編譯。
4. 儘快實作，不要先跑無關全量測試。開發中用 targeted parse/test、現有安全隔離 Host/Client 與工具快速迭代；可一次修改一批相關控件，之後立即取得 After 比對，不等整個大系統完成才看 UI。
5. 以修改後的可見 Windows Godot + 真 Server + 真滑鼠/鍵盤驗實際玩家操作與權威結果，適用持久化/多人及原版行為 A/B；UI 變更做 Reference/Before/After 並驗 hitbox/動態資料。非 UI 修改不用重新取得登入逐像素對照。依第 5 節驗受影響範圍。
6. 留精簡 Task ID 級交接（每個本輪 leaf/UI 的已實作、可玩 gate、UI gate、未驗/阻塞、證據、下一項），更新 TODO/Matrix，review diff、commit/push main、fetch 驗 0/0。父整合待所有相依 leaf 完成才關閉；依 §1 選下一輪，不因 AUTH 尚 IN PROGRESS 而自動重複。

### 每輪工作量與退出條件（持續交付，不以單一小修正結束）
- 以約 25～40 分鐘的有效實作、實機驗收與修正迭代為目標，不是硬性時間配額或等待時間。能更快完成完整功能則繼續下一項；不要 sleep、拖延、虛耗工具、重複測試或為湊分鐘而工作。平台資源與安全限制優先。
- 每輪開始依 EXECUTION_INDEX、TODO/Matrix、現有證據選一個可玩主題及 2～4 個具體候選 Task ID（含可替代的獨立高優先項）。目標是 3～5 個實質、可驗收的相關 leaf，或一個具完整玩家操作、同屏控件及原版對照的整體 UI/大型玩法流程；不可把單一 bug 拆成多個假交付，也不可為追數量縮水驗收。
- 每個切片實作、驗收、更新矩陣與安全 commit 後，立即再查相依/剩餘差距並開始下一個切片；commit/push 是可恢復的檢查點，不是本輪結束訊號。優先整合完整玩家路徑與 UI，避免跨輪反覆只修 AOI/怪物單一邊角而其他可做核心玩法或 UI 無進展；仍依雙軌比例及真 P0 優先。
- 結束前必須實際重新檢查剩餘候選 Task ID、其相依、WIP 與可用資源。只有完成本輪完整主題及合理工作量且已無合適安全續作、已達平台/工具/時間資源限制需要安全交接、或剩餘可做項均有具體阻塞/風險時才輸出最終回覆。首項完成、單一測試 PASS、一次 commit、非阻塞視覺未知或遇到單一 blocker 都不是停止理由；有獨立項可做且資源足夠就繼續。
- 每輪最終回報實際玩家成果、已實作/已驗收 Task ID、各 Gate、main SHA、仍未驗項、明確停止原因及下一個可執行 Task ID。禁止宣稱未驗成果 COMPLETE；若受限無法達成目標，如實回報，不硬湊數量。

### Godot MCP（優先使用實際可用工具，不假定能力）
- 使用者已安裝 Godot MCP。每輪需要 Godot 時先發現已連線工具、確認 Godot executable/version 與 project.godot 路徑指向正式 `C:\SGO\SRC\SGTest\client`；若工具提供 get_project_info / run_project / get_debug_output / stop_project / scene 操作等，按實際工具契約使用，不臆造工具或能力。
- 優先利用可用 MCP 查 scene/tree、Control 的實際 rect/anchor/offset/minimum size、theme/font、scale、clip、mouse_filter、z-index、Texture/Animation、runtime error；若 MCP 不提供這類檢查，使用正式 Godot script、Remote scene/debugger、OpenChatX 或 Windows 可見 Client，不能因 MCP 功能缺失卡住。
- MCP 啟動/停止限本輪隔離 Client；不得搶占其他 Session editor/process/port/DB。MCP 的編輯與測試必須反映到正式 repository；不要建平行 demo scene 冒充主遊戲。若無法連線或連續兩次失敗，記錄原因立即 fallback，不重複安裝或無限重試。
- MCP/headless、debug tree、直接觸發 signal/handler 只屬診斷或自動測試；可見 Windows Client + 真 Server + 玩家實際鍵鼠 + 原版畫面/行為 A/B 仍為正式驗收。不得宣稱工具能看到未實際取得的畫面。

### 效率與中斷規則
- 建議前 5 分鐘完成安全檢查與選定差距、約 10 分鐘取得可比較畫面；大部分時間用於修正與實機迭代。時間盒是停止重複研究的提醒，不是降低品質的理由。
- 禁止無目的全文掃描 .lst、每輪重新解包、重建未變 Server、重新研究已確認常數、重跑未受影響的大型 suite、寫長篇流水帳。
- 工具連續兩次無實質進展就切換既有安全工具或縮小範圍。優先重用可識別且隔離的本輪 Server、可重現測試路徑、既有素材與截圖；不得搶占其他 Session 的桌面/port/DB 或殺未知程序。只在程式、素材、版本、狀態或依賴變化使證據失效時重驗。
- 無法取得可信證據時標 UNKNOWN/NOT VERIFIED；原版數值 fail-closed，不猜。不能因 AA 或一個未知常數阻塞所有已確認可修差距。無可安全提交成果時回報具體 blocker，不以研究、文件、測試數量充數。

## 3. HK UI 硬性 Gate（圖片、文字、操作同一交付物）

### 原版來源
- HK 目標：C:\SGO\Stargate Online IE HK\SGOClient.exe.lst；ITF：C:\SGO\Stargate Online IE HK\output\Interface（含 control ID、rect、state、資源名）。
- 素材固定搜尋順序：C:\SGO\Stargate Online IE HK\Data1_PNG2 → Data1_PNG → Data1.dat（缺失才補解）→ HK Interface/loose files → 其他授權參考。確認原名、尺寸、frame、alpha、origin、trim、pivot、版本；原圖能取得禁止重畫。
- 唯一正式 converter：tools/asset_converter/；UI 解析入口 tools/ui_art/sync_original_ie_ui.py。以現有工具/manifest 補缺，不建第二套正式產線。
- **截圖先查** C:\SGO\SGO Paradise\program\ScreenShot。缺同狀態則可直接啟動 C:\SGO\SGO Paradise\program\client.exe，以既有授權測試帳號實際操作，不重問已提供的資料。只在安全本機測試環境輸入憑證；不記錄到公開 Git/log/報告。Paradise 是舊版；版本衝突以 HK ITF/.lst/Data1 為準，不把舊版 screenshot 當 HK 像素真相。
- HK .lst 只追當前視窗、資源 loader、frame/control ID、字型建立/繪字、狀態和 handler；不足時才深入。Old Server/data：C:\SGO\Recovered\Old；二創參考：C:\SGO\Recovered、C:\SGO\NewVersion\Recovered2；SGOAuto：C:\SGO\SGOAuto（原版狀態觀察、導航、重現操作；必要時唯讀研究，優先正常輸入，正式 Runtime 不依賴原版 process memory/地址）。來源全部唯讀。

### 整屏 HK 還原與目前使用者指定驗收
- 正式玩家畫面預設只顯示 HK UI，其他 skin/切換功能保留但不在 HK 模式同時疊加；不要為了移除錯誤 HUD 而移除地圖、人物、怪物、NPC、地上物、原本可用功能或權威狀態。驗進圖/換圖/登出重登。
- 優先清整個世界畫面的偏差：HUD HP/MP/EXP 原始素材與動畫/更新節奏、數值文字 baseline，F1…等快捷鍵文字與槽位、右側功能按鈕（包含背包）位置/大小/hitbox、聊天、地圖、人物/NPC。面板逐個開啟核對背景、關閉/返回、各 tab、文字、資料及操作。
- NPC 對話框、彈窗及按鈕優先由對應原版 SPR/幀組合，不得以自行繪製的通用 Panel/StyleBox 代替能取得的原始圖。HP/MP 等動態項需連續影格驗證消耗、恢復與動畫，不能只比一張靜態圖。
- HK 遊戲內 HUD/可移動面板（包含呼叫出的 UI）按已指定拖曳契約：按住非互動區可拖動，真正可點擊按鈕、物品槽、文字輸入、scroll/hotkey 互動不被拖曳吞掉；拖動不得觸發地面移動、點擊穿透或按鈕誤觸。核對開關、重開、疊層與 resize，依原版或使用者指定行為；不把右側可點擊按鈕本身做成拖曳區。
- 每份 UI MD 以整畫面為單位：對所有可見控件逐列核對來源、實際 rect/字體/幀、狀態、資料、hitbox；先對齊背景 origin/座標轉換及共用 font，再修個別差異。對尚未解析語意的項目標明缺證據；禁止為了對齊一個按鈕造成其他 UI 移位。
- 驗收至少：完整未裁切的 HK reference/Before/After、每個異常區 ROI、幾何/文字/hitbox 結果與正常互動、最小/最大或空值/滿值（適用）、開關/拖曳與原狀態。明顯未對齊不可用高全圖相似度、遮罩、headless 或測試通過掩蓋。已知可修差距未改完，UI gate 仍 FAIL/IN PROGRESS，後續排程應明確續作而非無期限留 backlog。

### 幾何、字型、渲染與交互
- 所有固定 HK UI 使用同一面板 local pixel coordinate system；背景、sprite、文字、輸入、按鈕、tooltip、hitbox 共用 origin。保留透明 padding/trim/pivot；避免 Container/minimum-size/theme margin/anchor/stretch/DPI 二次偏移。禁止逐 Label 無證據 magic offset、拉伸、重新上色、generic/retro/modern skin。
- 先辨別原圖已烘焙文字與動態文字，不重疊 Label。每個可見元件核對 x/y/w/h、原始 frame/層級、font face/family/size/weight、繁中 glyph/fallback、tracking/line height、baseline/ascent/descent、alignment、color、outline/shadow、clip/wrap、caret/selection/password mask；長短字與動態數值不可跑位。已知原版幾何目標 **±1 原始像素**，不能用相似字型宣稱一致。
- normal/hover/pressed/disabled/selected、focus/Tab、Esc/Enter/hotkey、drag/drop、scroll、tooltip、modal/Z-order、開關與重開、Server response、錯誤流程、換圖/重登及動畫/音效（適用時）必須對齊可驗證原版行為；圖像正確但點擊/流程錯仍 FAIL。
- 每輪涉及 UI 視覺改動時保存完整 reference、Before、After；做對應 ROI crop、50% overlay、pixel diff、rect/baseline/hitbox guide；同狀態同 client area 800×600，排除不可控動態區但保留未遮罩原圖，不得 mask 掉錯位或以全畫面高相似度掩蓋局部錯誤。不能只用 PNG、headless/test scene 或測試斷言當實機證據；不得用整張截圖當假 UI。
- 靜態 ROI 以原版像素一致為目標；記錄可解釋版本/光柵化差異。字型、位置、素材、hitbox 明顯不符就是 TEST FAILED/IN PROGRESS；缺精確 HK 證據 NOT VERIFIED。只有改善不代表已 1:1；不得把未驗視覺標 VERIFIED/COMPLETE。非阻塞 UI 差異不擋獨立玩法切片。800×600 收斂後驗適用 4:3/16:9/16:10、DPI；寬螢幕擴世界視野，不拉伸 UI/角色/地圖。

## 4. Git / Session / 資源安全（不可降低）

唯一正式路徑 C:\SGO\SRC\SGTest，遠端 https://github.com/kevin840307/SGTest，branch main。開始前快速確認狀態；有真正差異才深入其他 worktree/commit graph/diff。已知、有效且安全的其他成果依歷史 fast-forward/merge/cherry-pick；dirty、來源不明或仍使用中 WIP 保護，不覆寫、不提交為自己成果、不刪除。衝突影響本輪就避開或安全整合，不能自作主張破壞。

禁止 reset --hard、clean -fd、force push、覆寫未知 WIP、另建長期 branch 或第二份正式 clone/worktree；測試使用普通 temp、獨立 DB/port/build output。保護其他 Session 程序及使用中的資料；高風險/不可逆另需授權。原圖、SPR/Data1、解包輸出、原版字型/光柵、截圖、反編譯全文、音效、帳密皆不提交 public Git；可提交解析程式、非私有 metadata 與證據摘要。private evidence 放 git-ignored 本機路徑（例如 .cache/ui-parity/），不無故清除仍可重用的證據。

交付前 diff review，僅 commit 本輪及明確安全整合成果，fetch、非 force push main；確認 branch main、HEAD=origin/main、git rev-list --left-right --count main...origin/main 為 0 0；只清本輪自建且不再需要的暫存程序/檔案。工作樹有受保護 WIP 可保留並回報。

## 5. 風險分級驗收（不降品質、不重複做無關工作）

| 級別 | 變更 | 必須驗收 |
|---|---|---|
| T0 | 只改 Prompt/文件 | diff、規範一致性、Git；不啟動遊戲 |
| T1 | 單畫面 UI/素材/字型/座標 | parse/targeted test + 真 Server/可見 Client 同狀態 Before/After A/B + 受影響控件、資料、開關與鄰近流程；不重跑完整 Server suite |
| T2 | 共用 UI/renderer/input/auth/session/lifecycle | T1 + 所有直接受影響畫面與代表性核心 smoke；多人受影響才雙 Client |
| T3 | Server Authority/Protocol/Persistence/AOI/跨圖/高風險共享核心/里程碑或可信 baseline 失效 | 相關全量自動化、完整核心 native smoke、必要雙 Client、原版 A/B 與持久化驗收 |

完整核心 Smoke（詳見 docs/AGENT_REFERENCE.md §4、§7）：Login/Create/Select→角色初始外觀原地 10 秒→移動/Stop/Retarget/障礙→Map15↔Map13 真傳點、WorldReady 後立即走→HUD/Profile HP/MP 真值與動態變更/配點→背包/裝備/Pet/Mecha 等面板→普攻/技能/MISS/受擊/死亡/掉落/EXP→登出重登持久化→必要雙 Client Spawn/Despawn/AOI/跨圖/互動；觀察錯誤、卡死、黑畫面、超長 loading/reconnect/resource leak。已知 P0/P1 不得用既有 PASS 遮掩；非阻塞 UI P1 依雙軌排程修正。只重用同版本且依賴未變的有效證據；不確定就驗受影響範圍。

Native：C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe，正式 client/ + server/，安全隔離測試資料；Console/headless：C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe。headless/mock/fixture/pressed.emit/直接 handler/改 node property 都不能代替真玩家操作。動態項要連續影格/錄影，涉及多人同步/戰鬥/互動要第二個非 headless Client 實際觀察；桌面不可用則如實 NOT VERIFIED，不宣稱已驗。純 UI 不強制每輪跑 334+ Server checks；Server/shared core 高風險才擴到完整相關 suite。

## 6. 架構與完整產品契約（詳細規格按需讀）

- 正式 Godot Client + C#/.NET 8 Server；modular monolith，依遊戲功能命名。World 權威 state/write owner；Gateway authenticated binding/rate/backpressure/dispatch；Wire DTO/codec；Policy 純規則；Identity/Persistence durable transaction，DB I/O 不阻塞 World Tick；Client transport/reducer/prediction/presentation 分離。
- WIRE001 單一 little-endian compact-binary V1，不並行引入 MessagePack/Protobuf；TLS/Auth、ConnectionEpoch、bounded parser/queue、ACK/WorldReady/reconnect。Server 權威角色/HP/MP/移動/Blocking.dat A*/怪物/戰鬥/技能/物品/貨幣/交易/EXP/掉落/PvP/持久化。英文 semantic domain identifier，顯示顏色不作 gameplay rule；規則常數集中。
- AOI 先 baseline 再 Delta，可靠 Spawn/Despawn/戰鬥/交易/物品事件不可被 coalescing 吞掉；禁止全圖無意義廣播。移動支援 prediction/reconciliation/remote interpolation、長按1秒鎖定及左右鍵停止且右鍵選單不失效；Move 不覆蓋 Attack/Hit/Death；動畫速度與封包/角色移速解耦。
- 素材保留 identity/direction/action/frame/duration/origin/pivot/trim/alpha/draw order/paper doll/world Y-sort；一個原素材一 logical/physical asset，禁止跨素材合圖，Enemy/ 不多 generation/；支援 cache/preload/lazy/reuse/unload/restore。AFK/Unfocus/FPS 只動渲染/音效，不節流可靠收包或遊戲邏輯。效能提供 Before/After（frame/tick P95/P99、CPU/RAM/RSS/GPU/VRAM、draw calls、queue age、load）；不得犧牲正確性/畫質冒充改善。
- Item Definition + Item Instance + Enhancement Level，不以每個 +1/+2 分 ID；UID、ownership、stack/durability/stats、交易原子性；資料驅動 Item/Equipment/Skill/Monster/NPC/Shop/Drop/EXP/Enhancement/Quest。
- 最終完整玩法仍涵蓋登入創角、屬性配點、地圖移動、戰鬥技能、Buff/Debuff、掉落拾取、NPC商店、裝備強化修理、銀行倉庫、交易擺攤、組隊社團好友、任務、PvP、Pet、Mecha、死亡復活、GM；適用 Authority/Persistence/UI/多人/回歸全部要完成。Map15/13 是測試基準，不限正式玩法。
- 詳細契約見 docs/AGENT_REFERENCE.md 與 SPEC.md、docs/ARCHITECTURE.md、docs/TEST_PLAN.md 等相關文件；只在變更涉及該契約時讀/同步，不重寫歷史證據。相同 bug 優先查 owner/lifecycle/epoch/binding/ordering/resource，而非補 delay 或假數值。

## 7. 狀態、交接與回報

todo.txt 是工作優先順序，docs/FEATURE_MATRIX.md 是唯一正式功能矩陣。NOT STARTED / IN PROGRESS / IMPLEMENTED / TEST FAILED / NOT VERIFIED / VERIFIED / COMPLETE。IMPLEMENTED 不等於玩家可玩；VERIFIED 需真實適用證據；COMPLETE 要所有適用 Client/Server/UI/數值/Persistence/Single/Multiplayer/Edge/Regression/native/Original A/B。已知差異或缺證據不得錯標；TODO/Matrix 與實機衝突以保守狀態為準。

每輪只需一份精簡交接紀錄（TODO/Matrix 或既有 evidence，包含實際 UI-/GP-/ASSET- Task ID、子項已完成/未完成、Gameplay Gate 與 UI Gate 各自結果；不另造第二份正式矩陣）：畫面/state、reference 版本/來源、private Before/After 路徑、主要 ROI/rect/font/diff 變化、實際操作/測試、未解項及下一個具體動作、commit。不要每次寫長篇文件或重跑有效測試。報告用玩家角度：修前/修後、原版對照、實機驗了什麼、仍有何問題、哪些 NOT VERIFIED、main SHA；未驗雙 Client 就明示不適用/未驗。不能以新增 class/schema/commit/test 數量充當玩家成果。
