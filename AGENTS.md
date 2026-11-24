# SGTest AGENTS.md — v13 Playable Gameplay / UI Parity

適用於整個 C:\SGO\SRC\SGTest。此檔是唯一常駐執行規範；舊 v11/v12 Prompt 與詳細參考中的全量流程或 UI-only 優先順序不得覆蓋本檔。docs/SCHEDULER_PROMPT.md 是排程入口。詳細架構、原版研究、完整 Smoke 清單保留在 docs/AGENT_REFERENCE.md，依工作需要查閱，不要求每輪讀全文。

## 1. 目標與工作選擇

**先讓玩家真的能玩完整遊戲循環，同時逐步達到 HK 1:1；禁止登入像素級細節長期阻塞其他玩法。**

- P0：無法登入／移動／換圖、崩潰、資料損毀、權威或重大多人錯誤，先修；只修阻塞本輪可玩流程的問題。字型 AA、caret、Tab 細節等非阻塞差異留 UI backlog，不冒充已驗。
- 預設 **Gameplay-first + UI Parity 雙軌**。每三個非緊急有效交付切片至少兩個推進未完成的核心玩法，一個集中改善遊戲內 UI；沒有可做的 UI 差距可改為玩法。跨 Session 以最近 TODO/Matrix/commit 的實際成果判斷，不以啟動次數計算。已可登入進世界的 AUTH 不得自動成為下一輪主題，除非阻塞玩法、使用者指定或排到 UI 輪。
- 玩法按可玩鏈選最早尚未可玩的環節，先檢查現有實作，補缺而非重寫：① Map15→Map13 正式新角色可戰鬥（優先 source-backed combat profile、資料、權威規則，解除真正 blocker，不用 SyntheticTest、假數值或關閉 fail-closed）→ ② 選怪/靠近/普攻/MISS/受擊/擊殺/EXP/掉落/重生 → ③ 技能/MP/冷卻/動畫/單體與範圍 → ④ 撿取/背包/使用藥水/裝卸/屬性與重登 → ⑤ NPC 商店/金錢/銀行/倉庫/修理/強化 → ⑥ 多人 AOI/組隊/交易 → ⑦ Pet/Mecha/其他玩法。可依實際依賴調整，例如背包是戰鬥前置就先做。
- 每輪一個有玩家意義的垂直切片，可同批修相互依賴的 Server/Client/UI/資料/測試；有餘力且前一切片驗收完成可接續下一個，不為湊輪次停工。切片要真的能正常操作、權威結果正確、適用持久化及多人一致；不要求整個大型系統或全遊戲像素完美才切換。
- UI 軌優先世界 HUD、人物資料、戰鬥/技能、背包/裝備、NPC；按最大可見差異與共同根因批次修復。同畫面原圖、字型、文字位置、hitbox、交互均保留 HK 1:1 最終 Gate；非阻塞未對齊差異記入明確 backlog，仍 IN PROGRESS/NOT VERIFIED，**不阻止其他核心玩法交付**。同一非阻塞 UI 切片最多連續一輪；除非使用者指定，不連續耗在登入/選角/創角。
- 證據不足或其他 Session 正在處理同切片，記錄具體 blocker/下一步，改做獨立的最高優先未完成切片。禁止以缺精確 glyph/AA 證據為理由停住所有遊戲開發。未知正式數值不得猜，仍 fail-closed；不得弱化權威、安全或持久化。
- 使用者明確指定工作優先。內部可現代化，玩家外觀、操作、流程、時序、數值與動畫依 HK 可驗證證據。完整功能與 UI 的 COMPLETE 標準不變；階段性可玩交付與最終 1:1 是不同 gate。

## 2. 每輪快速閉環

1. 合併一次 Git/Session 檢查：status、branch/HEAD、fetch、ahead/behind、branch/worktree；只讀相關 TODO、Feature Matrix、上一輪有效證據及受影響程式。先確認沒有其他 Session 正在處理相同切片或操作同一桌面/DB/輸出。
2. 選定玩法或 UI 切片。玩法先真實重現正常玩家流程的阻塞、檢查既有 authority/資料/Client，訂出可玩驗收條件；UI 先取得同狀態原版與正式 SGTest Before（800×600、同 DPI），並排/overlay/diff。
3. 玩法先釐清 HK 行為/必要資料與 Server→Client→UI 的最短完整鏈；UI 建立簡短 pixel spec（資源/frame、rect、font/baseline、hitbox、state）。已有有效證據直接沿用；缺什麼才查什麼。先修最大共同根因，再批次修可確認的差異。
4. 儘快實作，不要先跑無關全量測試。開發中用 targeted parse/test、現有安全隔離 Host/Client 與工具快速迭代；可一次修改一批相關控件，之後立即取得 After 比對，不等整個大系統完成才看 UI。
5. 以修改後的可見 Windows Godot + 真 Server + 真滑鼠/鍵盤驗實際玩家操作與權威結果，適用持久化/多人及原版行為 A/B；UI 變更做 Reference/Before/After 並驗 hitbox/動態資料。非 UI 修改不用重新取得登入逐像素對照。依第 5 節驗受影響範圍。
6. 留精簡證據、更新 TODO/Matrix（含玩法/UI 軌與下一個可玩阻塞）、review diff、commit/push main、fetch 驗 0/0。依 §1 選下一輪，不因 AUTH/其他 UI 尚 IN PROGRESS 而自動重複。

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

每輪只需一份精簡交接紀錄（TODO/Matrix 或既有 evidence）：畫面/state、reference 版本/來源、private Before/After 路徑、主要 ROI/rect/font/diff 變化、實際操作/測試、未解項及下一個具體動作、commit。不要每次寫長篇文件或重跑有效測試。報告用玩家角度：修前/修後、原版對照、實機驗了什麼、仍有何問題、哪些 NOT VERIFIED、main SHA；未驗雙 Client 就明示不適用/未驗。不能以新增 class/schema/commit/test 數量充當玩家成果。
