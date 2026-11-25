# SGTest AGENTS.md — v15 HK Full Parity / End-to-End Gameplay / Multiplayer Performance

適用 C:\SGO\SRC\SGTest；唯一常駐執行規範，v15 取代 v11–v14 衝突規剄。排程入口 docs/SCHEDULER_PROMPT.md；詳細架構/原版研究/Smoke 按需查 docs/AGENT_REFERENCE.md。

## 0. 每輪必須讀取的執行規格（先用已整理證據，禁止從零猜測）

正式任務庫：`docs/legacy_task_catalog_20261124/executable/`。其上層保留原始盤點。每輪用索引定位工作，只讀選中任務與直接相依檔，不全文讀 519 份：
- `README.md` / `EXECUTION_INDEX.csv`：152 UI、163 Gameplay leaf、71 Gameplay integration、133 Asset evidence。任務庫是工作規格，不是完成度；不能將初始 NOT VERIFIED 解讀成每項都要重做。
- `ui/UI-*.md`：每個 ITF 的原始控制項 offset/type/ID/group/local rect、文字候選、SPR、幀數、origin 與反編譯函式、逐控件驗收。優先使用 `UI_CONTROL_METADATA.json` / `SPR_FRAME_METADATA.json` 取同畫面精確資料，禁止目測猜位置或直接取 screenshot 當整張 UI。
- `gameplay/GP-*.md`：以最小可玩 leaf 為開發單位；父整合任務在子項驗收後關閉，不重複實作。玩法引用的 UI MD 一併讀取。無專屬 ITF 的世界動畫/游標/效果也必須驗可見呈現。
- `assets/ASSET-*.md`：只在當前功能需要或排到素材證據時查；未證明可達的素材不可被當成新玩法。
- 來源不足依序定向補查：既有任務與有效證據 → HK ITF/SPR/.lst 與現有正式 converter → `C:\SGO\Recovered\Old`（ClientData/Paradise.exe.lst、服務端/plugins/*.dll.lst、Dat、cfg、資料庫；追 control handler、動畫/效果、數值、時序、條件、資料與封包）→ `C:\SGO\Recovered`（圖檔名稱、素材路徑、場景/原有實作交叉參考）及既有 Paradise 截圖/授權原版操作。HK 版 UI 最終優先，Old/Recovered 版本差異須標記；所有參考來源唯讀。不得因找到相似名稱就認定正確素材。
- ITF COUNT_MATCH 是結構數量吻合，不代表控件語意或畫面已驗；原始 group、sentinel、自動尺寸、重複 ID、動態 SPR、frame-state、font/handler 要按實際 loader 或原版交互核對。數值與行為無證據不猜，已有確認資料不反覆研究。

## 1. 產品目標與優先順序

**HK 原版畫面、操作、功能、數值、動畫與手感一致；多人同步正確、流暢、低資源。內部可現代化，玩家體驗不可有非預期差異。**

- 使用者指定及真 P0 優先：無法登入/移動/換圖、崩潰、資料損毀、權威或重大同步錯誤。其他工作長期三軌 UI 35%、玩法 35%、多人效能 30%；每三個非緊急交付主題涵蓋三軌，同一完整流程可跨軌。以玩家成果計，不以 commit 數；無安全項才記理由調整。
- 先實測正式角色是否 combat-locked，已通即推進：Map15→Map13→選怪/普攻/MISS/受擊/死亡/EXP/掉落/重生→技能/MP/冷卻/動畫→拾取/背包/藥水/裝卸/配點/重登→NPC/商店/銀行/倉庫/修理/強化→多人/組隊/交易/PvP/Pet/Mecha。按真依賴補缺，不重寫、不用 SyntheticTest/假資料/弱化 fail-closed。
- UI 優先完整世界 HUD、背包/裝備、人物資料、技能、NPC；共同根因批次修。非阻塞視覺差異留 FAIL/NOT VERIFIED，不阻擋獨立玩法；不長期耗在登入/選角/caret/AA/Tab。
- 多人優先玩家移動與動畫、AOI、戰鬥/傷害/掉落/跨圖/重連一致性及實測瓶頸。同類非 P0 局部修補連續兩輪無實質玩家成果，先重現完整流程、找共同根因或換軌；不一直堆防禦測試。
- 每輪目標一個完整主題、2～4 候選 Task ID、3～5 實質相關 leaf 或完整大型玩法/UI/多人流程、25～40 分鐘有效工作；非硬配額。父整合待子項驗收才關閉，不拆假任務、湊時間或降品質。

## 2. 每輪閉環

1. 依 §4 安全同步，查 EXECUTION_INDEX、相關 MD/metadata、todo.txt、docs/FEATURE_MATRIX.md、既有證據與程式，選具體未完成 Task ID/相依/玩家驗收。
2. 正式隔離 Client/Server 重現玩家流程；UI 取 HK 同狀態 Reference/Before（800×600 同 DPI）及 control inventory；玩法查原版 handler/數值/時序/前置；多人取雙 Client 與效能 baseline。已有有效證據直接用，缺什麼才定向查。
3. 批次實作並驗正常輸入→Client intent→Server 權威/交易→封包/reducer→畫面/動畫/成敗回饋→適用重登/雙 Client；先 targeted test，依 §5 驗受影響項。不得以單元測試代替實機。
4. 每個切片驗收、更新 TODO/Matrix、review diff、commit/push 後立即續作；commit 是檢查點。遇 WIP/證據/工具 blocker 記錄並切換安全獨立項；工具兩次無進展即 fallback。禁止無目的 .lst 全掃、重解包、重建未變 Server、重跑無關 suite。
5. 結束前重查候選、相依、WIP、資源；有安全高優先項且資源足夠就繼續。只有主題及合理工作量完成且無適合續作、實際資源限制需交接、或剩餘項均具體阻塞才結束。不要 sleep、湊時間/測試/commit，依 §7 簡短交接。

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

### 整屏 HK 驗收
- HK-only 預設保留 skin 切換；不疊錯誤 HUD、不移除地圖/人物/怪物/NPC/地上物或既有功能。HUD HP/MP/EXP 原圖、數值 baseline、動態動畫、F1…文字、右側/背包按鈕、聊天、各面板 tab/關閉/資料都驗。
- NPC 對話、彈窗、按鈕用原版 SPR/frame 組合，能取原圖不自畫。逐控件 ID/offset、origin/rect/frame/圖層、font face/size/weight、繁中 glyph/fallback、baseline/ascent/descent、tracking/line height、alignment/color/outline/shadow/clip/wrap/caret/selection/mask；區分烘焙/動態文字。共用 local pixel origin、透明 padding/trim/pivot，禁止 magic offset、拉伸、Container/theme/anchor/DPI 二次偏移。
- normal/hover/pressed/disabled/selected、focus/Tab/Esc/Enter/hotkey、tooltip、drag/drop/scroll/modal/Z-order、重開、Server 成敗回饋、動態效果/音效適用皆驗。所有指定 HUD/叫出的面板從非互動區拖曳，按鈕/槽位/輸入正常、不穿透地圖、不誤觸。
- 同 client area 800×600 同 DPI/狀態，完整未裁切 Reference/Before/After、逐控件 ROI、50% overlay、pixel diff、rect/baseline/hitbox，已知幾何 ±1px；靜態 ROI 原像素一致。不可 mask 錯位或拿整張截圖當 UI；HP/MP/動畫連續影格/錄影，版本/光柵差異具體標記。未知 NOT VERIFIED、已知不符 FAIL；可玩不等於 1:1。收斂後驗 4:3/16:9/16:10、DPI，寬屏擴視野不拉伸。

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

完整核心 Smoke（詳見 docs/AGENT_REFERENCE.md §4、§7）：Login/Create/Select→角色初始外觀原地 10 秒→移動/Stop/Retarget/障礙→Map15↔Map13 真傳點、WorldReady 後立即走→HUD/Profile HP/MP 真值與動態變更/配點→背包/裝備/Pet/Mecha 等面板→普攻/技能/MISS/受擊/死亡/掉落/EXP→登出重登持久化→必要雙 Client Spawn/Despawn/AOI/跨圖/互動；觀察錯誤、卡死、黑畫面、超長 loading/reconnect/resource leak。已知 P0/P1 不得用既有 PASS 遮掩；非阻塞 UI P1 依三軌排程修正。只重用同版本且依賴未變的有效證據；不確定就驗受影響範圍。

Native：C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe，正式 client/ + server/，安全隔離測試資料；Console/headless：C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe。headless/mock/fixture/pressed.emit/直接 handler/改 node property 都不能代替真玩家操作。動態項要連續影格/錄影，涉及多人同步/戰鬥/互動要第二個非 headless Client 實際觀察；桌面不可用則如實 NOT VERIFIED，不宣稱已驗。純 UI 不強制每輪跑 334+ Server checks；Server/shared core 高風險才擴到完整相關 suite。

### 多人與效能 Gate
- 依 docs/TEST_PLAN.md「v15 三軌 Release Gate」；移動、戰鬥、AOI、跨圖、交易、網路或共用渲染/生命週期變更驗雙可見 Client 及受影響指標。每三個非緊急交付主題至少一次主動混合 20 人/300 怪/500 Drop 基準；資源被占用標 NOT VERIFIED，安排下一可用輪；重大變更/里程碑升階與長測，不搶其他 Session。
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

一份精簡 Task ID 級交接（TODO/Matrix 或既有 evidence）：玩家成果、已驗子項/五 Gate、reference 來源/版本、private Before/After/trace 路徑與差距、未驗/blocker、下一 Task ID、main SHA、停止原因。不另造矩陣、不寫流水帳、不以 class/schema/test/commit 數代替成果。
