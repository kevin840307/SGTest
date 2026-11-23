# SGTest AGENTS.md — Prompt v11 對齊版

本檔適用於整個 `C:\SGO\SRC\SGTest` Repository。所有 Agent／Session 在修改 SGTest 前都必須遵守。

## 0. 最高原則：Playable Main 與 HK 可觀察體驗優先

SGTest 的成功標準不是「程式很多、測試很多」，而是目前正式 `main` 真的能被玩家正常遊玩，並持續收斂到 HK 原版 Stargate Online 的可觀察體驗。

優先級不可顛倒：

1. **Playable Main**：登入、人物外觀、移動、換圖、HP/MP、個人資料、核心 UI、戰鬥、基本多人必須真的可玩。
2. **玩家 5 分鐘內能遇到的 P0/P1 問題**。
3. **目前正在做的單一玩家功能完整收斂**：功能邏輯 + Server Authority + Persistence + Client + HK UI + native 實機 + 原版 A/B（適用時）一起完成。
4. **HK 核心 UI 忠實度**：登入、選角／創角、主 HUD、個人資料等玩家最先看到的 UI 不得長期停留在臨時／自創畫面。
5. 資料一致性、Server Authority、持久化、重大同步問題。
6. **只有目前功能完成閉環後，才能開始下一個功能。**
7. HK 其餘 UI／數值／動畫／手感忠實度。
8. 效能、記憶體、資源與長時間穩定性。
9. 文件與研究整理。

不得把 Server checks、protocol coverage、schema/class/file/commit 數量、headless/fixture/synthetic E2E PASS，或尚未真正可玩的 Pet／Mecha／Guild 等功能單獨當成玩家成果。

**若使用者正常玩 5 分鐘就能發現明顯基礎問題，代表上一輪驗收失敗。核心玩家 Smoke 有失敗時，先修核心可玩性，不得優先擴新功能。**

### 0.1 Single-Feature Closure Loop：功能 + UI 同步收斂，禁止發散

**每次只鎖定一個玩家可觀察功能，完整做完後才進下一個。功能與 UI 是同一個交付物，不能拆成「先做功能、以後再補 HK UI」。**

「單一功能」以玩家操作結果定義，不以程式模組大小定義。例如：Login、Character Select、Character Create、Character Profile / Attribute Allocation、Movement、Map Transition、Normal Attack、Skill Cast、Inventory、Equipment、NPC Shop、Pet Summon、Pet Equipment、Mecha Equip、Trade、Party、Friend、Stall、Quest、PvP。

Pet、Mecha、Guild 這種大系統可以拆成多個玩家可觀察功能，但**不能同時開很多未收斂切片**。

每個功能固定走完整閉環：

1. **先看原版**：用 Paradise／HK `.lst`／Old／原版資料確認功能行為與 UI；能實機就先取得 800×600 截圖／錄影與操作流程。
2. **定義完成條件**：列出正常流程、錯誤流程、Server Authority、Persistence、Multiplayer（適用時）、HK UI、A/B 要求。
3. **Server / Data**：完成權威規則、資料、Persistence、交易／競態安全（適用時）。
4. **Client / State**：完成 protocol、reducer、state ownership、錯誤處理。
5. **HK UI 同輪完成**：同一功能的 Window／Panel／Button／Icon／文字／交互必須同步對齊 HK；不得先放 generic/debug/placeholder UI 就跳下一功能。
6. **Targeted automated tests**：驗規則、protocol、persistence、edge cases。
7. **Native Windows 真操作**：真 Server + 非 headless Godot，用滑鼠鍵盤完整操作該功能。
8. **多人實測**：功能涉及其他玩家時，以至少兩個真 Client 實測。
9. **原版 A/B**：可驗證項目做 Paradise/HK A/B；無法確認的精確原版差異才標 `NOT VERIFIED`。
10. **回歸核心 Smoke**：確認本功能沒有破壞登入、人物、HP/MP、移動、換圖、個人資料與基本多人。
11. **Feature Matrix / TODO / Commit / Push**：證據寫完、狀態正確、main 同步後才算本功能結束。
12. **再選下一個功能**。

禁止下列發散模式：

- Login 還是臨時 UI，就跑去做 Pet／Guild。
- Pet 裝備只有 Server/Protocol PASS、UI 還是 generic，就宣稱完成並開始 Mecha。
- 同時開 Character Profile、Trade、Quest、PvP 四個半成品。
- 因為某功能 UI 難做，就先建立另一套功能逃避收斂。
- 用「之後統一做 Original UI」把每個功能的 HK UI 債務累積到最後。

若只剩**目前確實無法取得的原版精確數值／素材證據**，可明確標 `NOT VERIFIED` 後繼續；但可實作的功能、UI、操作、持久化與多人部分不得藉此留半成品。

---

## 1. HK UI 忠實度是正式 Gate，不是最後美化

UI 不是「功能先做完，以後再換皮」。玩家從第一秒看到的登入畫面開始，就屬於 HK 忠實重製的一部分。

### 1.1 核心 UI 還原優先順序

依玩家接觸順序優先：

1. **登入畫面**
2. **選角／創角**
3. **主世界 HUD**
4. **個人資料／人物屬性**
5. **背包／裝備**
6. **NPC／商店／銀行／倉庫／修理**
7. **技能／戰鬥相關 UI**
8. **Pet / Mecha**
9. **Party / Trade / Friend / Clan / Stall / Quest / PvP**
10. 其他 HK 可確認 UI

如果登入、選角、主 HUD、個人資料等核心畫面仍明顯是 placeholder、自創版、測試面板或與 HK 結構差異很大：

- UI 狀態必須維持 `IN PROGRESS`
- 不得宣稱「Original UI 完成」
- 不得只因功能能按就標 `VERIFIED` / `COMPLETE`
- 新功能 UI 開發前，先評估是否應把核心 UI 差距列為更高優先 blocker

### 1.2 HK UI A/B 要求

涉及 UI 的工作必須優先用可執行原版／Paradise、HK 反編譯與原版素材建立對照。

比較至少包含：

- 整體構圖與層級
- Window / Panel 位置與尺寸
- Font
- Color
- Background / Frame
- Button 樣式
- Icon
- Input field
- Hover / Pressed / Disabled
- Tooltip
- Scroll
- Z-order
- Hotkey
- Drag/Drop
- 對齊與間距
- 800×600 原版呈現
- 寬螢幕延伸策略

**目標是對 HK 可觀察結果做 1:1 忠實重製。只要原版證據存在，就不得以「大致相似」「同風格」「現代化替代」當完成。功能正確但畫面明顯不是 HK，只能算功能 IMPLEMENTED，不能算 UI VERIFIED。**

登入畫面特別要求：

- 必須實際開 Paradise／可驗證原版參照
- 擷取 800×600 對照
- 比較背景、Logo/Title、登入欄位、按鈕、版本／提示文字、焦點順序、錯誤提示、第二密碼流程（適用時）
- SGTest 實機以相同視窗比例 A/B
- 不允許以「先用現代 UI，最後再改」長期跳過

### 1.3 原版 PNG／SPR 素材優先：能解析就禁止重畫

Repository 已有原版 UI 解析管線：

`C:\SGO\SRC\SGTest\tools\ui_art\sync_original_ie_ui.py`

此工具會使用正式 asset-converter 的：

- `tools.asset_converter.source.extract_client.read_index / lzss`
- `tools.asset_converter.source.runtime_spr.decode_spr`

從原版 `Interface/*.spr` 或 `Data1.dat` 解析 UI Sprite，輸出 PNG 到本機 private UI 目錄。現有清單已包含例如：

- `ACCOUNT_LOGIN`
- `SELECT_CHARACTER`
- `CREATE_CHARACTER`
- `COMMAND`
- `HPBAR`
- `BG_ITEM`
- `BG_CHARACTER_INFO`
- `BG_SKILL`
- `BG_SHOP`
- `BG_BANK`
- `BG_REPAIR`
- `BG_TRADE`
- `BG_Equip`
- `MINIMAP_*`
- `HOTKEY_*`
- 多組按鈕與 multi-frame SPR

因此 UI 實作遵守以下素材優先級：

1. **原版可解析素材**：直接從 HK 原版 `.spr` / `Data1.dat` 解析出 PNG／frame，保留原像素、尺寸、alpha、frame 次序；這是第一優先。
2. **原版鬆散 PNG／已授權本機素材**：直接使用，不重新繪製。
3. **原版只能從執行畫面觀察**：以截圖／錄影、`.lst`、座標與尺寸證據重建，並保留 A/B 證據。
4. **完全沒有原版證據**：才允許暫時重建；必須標 `NOT VERIFIED`，不得宣稱與 HK 一樣。

禁止：

- 原版 PNG / SPR 可取得時自行畫一張「類似」素材。
- 用 `build_retro_terminal.py` 這類 author-owned／自創 skin 取代已有 HK 原版 UI，然後宣稱 Original UI 完成。
- 為了視覺統一而重新上色、改比例、改 border、改 icon、改 alpha、平滑放大原版素材，除非 HK 實機證據就是如此。
- 將原版 private `.spr`、`Data1.dat` 或解析出的私人素材提交到 public Git。

原版素材保持唯讀；解析輸出只存在授權的本機 private 路徑。公開 Git 只保存解析程式、manifest/metadata、可重現流程與不含私有素材的驗證碼。

### 1.4 反編譯驅動的 UI／素材還原：先找邏輯，再找資源名

HK Client 反編譯：

`C:\SGO\Stargate Online IE HK\SGOClient.exe.lst`

除了確認 gameplay 邏輯，也要主動把反編譯當成 **UI 與素材索引**。當某個功能／畫面要還原時，優先從 `.lst` 追：

- 建立／開啟／關閉該視窗的函式
- resource loader / sprite loader / texture loader 呼叫
- 硬編碼或資料表中的 SPR／BMP／PNG／Interface resource 名稱
- frame index / animation sequence
- x/y、width/height、anchor、offset
- button/control ID
- hover / pressed / disabled state
- click / double-click / right-click handler
- keyboard / hotkey / Esc / Enter flow
- tooltip / text ID / message ID
- Server packet / event → UI state transition
- loading / map change / logout / reconnect lifecycle
- sound / animation trigger
- 同一資源在其他畫面的共用關係

找到資源名稱後，立即回查：

1. `Interface/*.spr` loose files。
2. `Data1.dat` index。
3. 現有 `sync_original_ie_ui.py` / asset-converter 是否已能解析。
4. 若工具清單未包含該 SPR，但 decoder 已支援，**擴充解析清單／manifest，而不是自行重畫。**
5. 將解析出的 private PNG/frame 與 `.lst` 取得的尺寸／位置／狀態邏輯一起套入 SGTest。
6. 最後用 Paradise/HK 可執行畫面做 A/B 驗證。

因此每個 UI 功能的證據鏈應盡量形成：

`HK .lst 邏輯/資源名 → Interface/Data1.dat 原素材 → decoder 解析 PNG/frame → Godot 使用 → Paradise/HK A/B`

如果 `.lst` 明確指出載入某個原版資源，**不得因目前 SGTest 尚未匯入該檔，就改用自創圖替代**；先擴充工具把原資源解析出來。

若反編譯與 Paradise 的可觀察行為有差異，以目標 HK 版本證據優先，並明確記錄版本差異；不得把較舊 Paradise 行為硬套成 HK。

### 1.5 行為也要 1:1：不能只對齊圖片

HK 忠實度同時包含 **外觀 + 行為 + 時序 + 狀態切換**。每個 UI／功能 A/B 必須比較：

- 開啟／關閉條件
- 左鍵／右鍵
- double click（若原版有）
- hover / pressed / disabled
- focus / Tab order
- Esc / Enter / Hotkey
- drag / drop
- modal / non-modal
- window stacking / Z-order
- 選取後的 highlight
- tooltip 出現條件與內容
- Server 回覆前／後的 UI state
- loading / transition
- success / error flow
- map change / logout / reconnect 後狀態
- animation frame / timing
- sound trigger（可驗證時）
- 同功能多人可觀察結果（適用時）

**只有圖片對了但按鈕、流程、時序、狀態切換和 HK 不同，仍然不能標 UI/Behavior VERIFIED。**

若 HK 行為可由 Paradise 實機觀察，優先實機 A/B；Paradise 無法驗證才使用 HK `.lst`、Old、原版資料交叉確認。無法確認時標 `NOT VERIFIED`，不得自行設計後當成原版行為。

---

## 2. 唯一正式程式碼與 Main-Only Git

正式工作目錄：

`C:\SGO\SRC\SGTest`

正式 GitHub：

`https://github.com/kevin840307/SGTest`

正式分支：

`main`

本機 `main` 與 `origin/main` 必須代表同一套正式程式碼。

### 每次開工前

先確認：

- `git status --short`
- branch / HEAD
- `git fetch origin`
- 比較 `main` / `origin/main`
- local branches
- `git worktree list`
- 可存取 worktree / Session WIP 的 status、HEAD、diff

若發現其他 Session／AI 殘留：

1. 判斷 `main` 是否已包含其有效成果。
2. 可安全辨識且應保留、尚未進 `main` 的成果，**先整合回 main**。
3. 依 Git 歷史使用 safe fast-forward / merge / cherry-pick。
4. 解衝突後跑受影響測試。
5. 確認成果已在 `main` 後，只有 clean 且確定不再使用的 branch/worktree 才可刪除。
6. dirty、來源不明或疑似仍使用中的 WIP 必須保護；不能安全整合就保留並回報。

禁止：

- `git reset --hard`
- `git clean -fd`
- force push
- 直接覆寫未知 WIP
- 刪除其他 Session 未提交工作
- 為方便另開長期 feature/fix/temp/release branch
- 為避衝突建立第二份正式 clone/worktree

一般測試隔離使用普通 temp directory、isolated DB、isolated build output，不建立 Git worktree。

### 每次交付前

確認：

- branch = `main`
- 已重新 `git fetch origin`
- 本機 HEAD = `origin/main`
- `git rev-list --left-right --count main...origin/main` = `0 0`
- 本次變更已 commit/push
- 本 Session 自建 branch/worktree/tmp clone 已清
- 其他 Session dirty WIP 未被破壞

---

## 3. 每次工作固定流程

1. 讀本檔、`todo.txt`、`docs/FEATURE_MATRIX.md`，依任務補讀 `SPEC.md`、Architecture、Test Plan、研究證據。
2. 完成 Git / other-session 安全檢查。
3. 判斷目前 `main` 的核心玩家 Smoke 是否可信。
4. 有核心可玩 bug：先重現 → 找根因 → 修正 → targeted tests → native Windows 重驗。
5. 有核心 HK UI 明顯偏離：依第 1 節優先修正，不得長期用 placeholder 當正式 UI。
6. 核心 Smoke 全綠後，只選 **一個玩家可觀察功能**，依 Single-Feature Closure Loop 鎖定範圍。
7. 先取得該功能的原版行為 + HK UI 證據，再同輪完成 Server / Client / Persistence / UI。
8. 開發中先跑 narrow targeted tests。
9. 該功能完成後跑必要 integration / multiplayer / regression。
10. 強制跑 native Windows 真操作；多人功能強制雙 Client。
11. 做 Paradise / HK / Old / SGOAuto A/B；無法確認的精確原版差異標 `NOT VERIFIED`。
12. 回歸核心 Smoke，確認沒有破壞既有可玩流程。
13. 更新 TODO / Feature Matrix / 必要文件。
14. Review diff。
15. Commit / fetch / 安全整合 / push `main`。
16. 驗證本機與 GitHub `main` 完全同步。
17. **只有以上全部完成，才選下一個功能。**

原版研究時間盒化。找不到可信規則／數值時：

- 標 `UNKNOWN` / `NOT VERIFIED`
- 建立可替換、fail-closed 邊界
- 不猜數值
- 不讓單一未知常數阻塞整個系統

---

## 4. 核心玩家 Smoke Gate — Client 工作硬門檻

只要本輪碰到 Client、UI、Renderer、輸入、玩家呈現、移動、戰鬥或世界狀態，就必須用真正可見的 Windows Client。

可見 Godot：

`C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe`

Console / headless：

`C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe`

正式 Client：

`C:\SGO\SRC\SGTest\client`

測試必須使用：

- 目前正式 `main` 的 Server / Client
- 隔離 DB
- 安全未占用 port
- 真滑鼠／鍵盤／Computer Use
- 不殺未知 Server / Godot
- 不覆寫其他 Session 資料

headless、mock、fixture、test scene、`pressed.emit()`、直接 handler 或直接改 node property 都不能代替實機驗收。

### 4.1 登入與人物外觀

實際走：

`啟動 → HK 登入畫面 A/B → 登入 → 選角 → 進世界`

登入畫面先確認 HK 對齊程度，再進入角色。

進世界後原地至少 10 秒、不移動，確認：

- 正確人物／髮型／衣服／裝備／名稱
- 紙娃娃 layer 正確
- 不先變預設／占位人物
- 不等第一次走路才恢復真正角色

再測 `走路 → 停下 → 再走`，外觀不能因 action/state 切換重設。

### 4.2 移動與換圖

實際測：

- 點地面
- 長按
- 連續點擊
- Retarget
- Stop
- 轉角
- 障礙
- 繞路 / A*
- Server correction
- 停止再走

不得有明顯順移、回朔、原地跑步或輸入鎖死。Server 保持移動權威。

至少完成一次真實傳點／換圖。新 WorldReady 後立即確認：

- 點地面可走
- 長按可走
- 可 Stop
- 可重新下指令
- Camera / HUD / 世界輸入正常

不得殘留 input lock、pending map、stale MapEpoch、movement sequence lock、overlay/modal、focus lock、舊 AOI、舊 selection。

**換圖後不能走直接視為核心 blocker。**

### 4.3 HP / MP 真值

登入後同時確認：

- HUD Current/Max HP
- HUD Current/Max MP
- 個人資料 HP/MP
- Server authoritative state

實際造成 HP / MP 變化（受擊、技能、喝水、恢復等可用路徑），確認 UI 與 Server 同步。換圖後、重登後再確認。

### 4.4 個人資料與配點

至少確認目前應存在的：

- 角色名
- 等級
- 職業／身份
- EXP
- HP / MP
- STR / INT / DEX / VIT / AGI
- 剩餘屬性點
- 其他已實作角色資料

若可配點：

- 真正點至少一個 STR/INT/DEX/VIT/AGI
- Server 驗證
- UI 回寫
- 剩餘點數更新
- 重登確認

若不可配點：

- 按鈕正確 disabled
- UI 明確顯示狀態
- 不能「按鈕存在但點了沒反應」

空白個人資料或假按鈕不算完成。

### 4.5 核心 UI

至少開／關並實際操作：

- 個人資料
- 背包
- 裝備
- Pet
- Mecha
- 本輪相關主要面板

檢查 focus、hover、tooltip、scroll、selection、disabled、text clipping、click area、Z-order、modal、關閉／重開、世界輸入是否恢復。

### 4.6 戰鬥 Smoke

版本已有戰鬥時，至少實際打一隻怪：

- 選怪
- 接近
- 攻擊距離
- 普攻
- 技能
- 命中 / MISS
- 傷害數字
- 受擊
- HP 更新
- 怪物死亡
- target 清除
- 掉落 / EXP / 重生

不能只看 Server 數值。

### 4.7 換圖後資料、登出／重登

換圖後重新確認人物外觀、HP/MP、個人資料、背包、裝備、Pet/Mecha、本輪功能狀態。

WorldReady / map reset 不得把 profile、stats、presentation 或 UI state 清成預設。

再實際：

`登出 → 回登入 → 同角色重新登入`

確認外觀、HP/MP、個人資料、背包／裝備、Pet/Mecha、持久化狀態、UI node lifecycle、pending action、map/input lock、resource lifecycle。

### 4.8 雙 Client 真多人 Smoke

至少兩個 **非 headless** Godot Client，同地圖登入，確認：

- 互相 Spawn
- 名稱／外觀
- 移動 / Stop / Retarget
- 離開／回到 AOI
- Despawn
- 換圖
- 登出／重登

至少操作一個目前已實作的玩家互動。

涉及多人同步、戰鬥或互動的工作，必須用第二 Client 肉眼看另一人的畫面。

Trade / Party / Friend / PvP / Stall 等未完成時維持 `IN PROGRESS` / `NOT VERIFIED`，不得因 wire/headless PASS 就宣稱正常。

### 4.9 Runtime / Console Gate

整個流程監看：

- Windows 未回應
- 黑畫面
- 超長 loading
- UI 卡死
- Godot error
- 影響遊戲的 warning
- Server exception
- 斷線 / reconnect loop
- assertion
- 明顯 resource leak

啟動曾出現未回應也要調查，不可因最後恢復就忽略。

---

## 5. 原版與研究來源 — 關鍵路徑不可刪

### HK 目標 Client 反編譯

`C:\SGO\Stargate Online IE HK\SGOClient.exe.lst`

主要目標版本證據，用於 UI、操作、動畫、地圖、渲染、移動、戰鬥、packet/event semantics、state transition、人物與裝備外觀。

不需逐行移植組語；玩家可觀察行為不明、現實作與原版矛盾、或需確認狀態機／數值／時序時再深入。

### Old Server 與原版資料

`C:\SGO\Recovered\Old`

可研究 Server 反編譯、原版 DB/data、Map/Blocking/Collision、Monster AI、Combat/Skill、EXP/Drop、Item/Equipment、NPC/Shop、Trade/Stall、Clan/Friend、PvP、Network semantics。

Old 與 HK 不一定同版。結論分 `CONFIRMED` / `INFERRED` / `UNKNOWN`，不得把推論寫成原版確定行為。

### Paradise 舊版實機

`C:\SGO\SGO Paradise\program\client.exe`

測試帳號：

- username: `test01`
- password: `test01`
- 第二組密碼: `0000`

原版 Server 通常已由使用者啟動，但每次仍確認狀態。

Paradise 比 HK 舊；目前不適合用來驗擺攤、社團、好友，但可驗與 HK 共通的登入、選角、UI、地圖、移動、戰鬥、動畫、NPC、物品、世界呈現。

Paradise 無法使用某功能，不代表 SGTest 可以省略 HK 對應功能。

### SGOAuto

`C:\SGO\SGOAuto`

可研究後用於原版座標、HP/MP、地圖、NPC、玩家／怪物、動作狀態、移動、測試操作與狀態記錄。

不假設 memory address 可跨版本。正式 SGTest Runtime 不得依賴 SGOAuto 或原版 process memory。

### 二創參考

`C:\SGO\Recovered`

`C:\SGO\NewVersion\Recovered2`

可參考資料模型、UI、動畫、素材解析、資源管理、強化、遊戲機制、封包處理。

玩家可見行為、玩法與數值仍以 HK 證據優先。所有原版／二創來源唯讀。

### Godot

可見 Runtime：

`C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe`

Console / headless：

`C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe`

headless 可驗邏輯與 regression，但永遠不能代替 native UI / Renderer / UX。

### 正式 Converter

`C:\SGO\SRC\SGTest\tools\asset_converter`

正式產線只維護這套 Converter，不建立第二條互相漂移的正式產線。

---

## 6. 原版 EXE / SGTest A/B

涉及可觀察原版行為時：

1. 啟動 Paradise／可執行原版參照。
2. 登入並到等價角色／地圖狀態。
3. 真實操作目標功能。
4. 截圖或錄影。
5. 必要時用 SGOAuto 讀取狀態。
6. 啟動 SGTest Server + Godot Client。
7. 重播相同操作。
8. 比較結果。

比較 UI Position/Size、Font/Color/Icon、Tooltip/Hover、Drag/Drop/Hotkey、Z-order、Item 顯示／數值、Animation、Movement、Attack/Hit、Skill、World state、操作結果。

原版 UI 優先 800×600。

走路、攻擊、受擊、技能、怪物追擊、AOI 等動態行為用連續影格／錄影，不可只看單張 screenshot。

Paradise 無法驗的 HK 功能改用 HK `.lst`、Old Server、原版資料與可信來源。

不能實機確認就標 `NOT VERIFIED`。不可虛構 A/B PASS。

---

## 7. 雙地圖核心 MVP 基準

現階段代表性可玩基準仍使用：

- Map15 主城
- Map13 尼西亞台地

這兩張圖限制的是測試資料量，不是玩法完整度。

### Map15

驗登入／創角／選角、HK UI、Spawn、NPC、商店、銀行、倉庫、修理、裝備、強化、背包、聊天、交易、玩家商店、Pet、Mecha、UI／資源生命週期。

### Map13

驗 Blocking、A*、長按走路、AOI、Attack/Hit/Death、Monster AI、技能、MISS、死亡復活、Drop/Pickup、PvP 地圖規則。

### 雙向跨圖

至少兩個真 Client 來回 Map15 ↔ Map13：

- Server 傳點／權限
- MapEpoch
- WorldReady
- 舊 AOI 清除
- 新圖 Spawn
- 資源 unload/prefetch/load
- 換圖後仍可移動
- logout/relogin/reconnect
- 狀態持久化

不得把單圖 load test 當成跨圖 PASS。

---

## 8. 不能違反的架構契約

### 程式結構

- Server／Client 優先依遊戲功能命名，如 `Map/map`、`Npc/npc`、`Pvp/pvp`。
- World 保有權威 state transition / write ownership。
- Gateway/Connection 負責 authenticated binding、rate/backpressure/dispatch。
- Wire 只做 versioned DTO/codec。
- Policy 保持可測純規則。
- Identity/Persistence 負責 durable transaction，不反向決定 gameplay。
- Client reducer/presentation 不回滲 Server。
- 採 modular monolith；沒有實測瓶頸不拆微服務/ECS/事件總線。
- 不過度抽象，不重複 Manager/Loader/Cache。

### Wire

WIRE001 已選定單一 little-endian compact-binary V1。

正式 Runtime 不加入 MessagePack／Protobuf 第二套。後續在同一契約完成 TLS/Auth、ConnectionEpoch identity binding、bounded parser/queue、ACK/WorldReady/reconnect。

只有新證據顯示重大問題才重開選型，並重跑相同 Gate。

### Server Authority

Server 權威管理：

- Character / Stats / HP / MP
- Movement / Blocking.dat Collision
- Monster AI
- Combat / Skill
- Item / Money / Equipment
- Trade / Stall / PvP
- EXP / Drop
- Persistence

外部 DB I/O 不得卡 World Tick。

### Server 命名與規則

- Domain、Wire、Persistence、Host CLI/config identifier/enum/token/key 使用英文 semantic naming。
- 中文／其他語系只在 Client/UI/localization 或人類文件。
- Pink/Red 等是 semantic state，不以顯示 RGB 當 gameplay rule。
- 時間、距離、倍率、上限、cooldown、狀態轉換不可散落 magic number，集中於 Policy/Options/static readonly/const。
- 測試引用同一公開契約或明確 fixture，不複製規則常數。

### AOI

- Server 以權威 map/position + bounded interest/viewport 過濾玩家、怪物、掉落。
- 先建立 subscription/baseline 再發 Delta。
- Spawn/Despawn、戰鬥、物品、交易可靠事件不可被快照/coalescing 吞掉。
- 避免全圖廣播與每 Tick 無意義重複狀態。

### Client

network transport、message codec、state reducer、prediction、render/UI 職責分離。

AFK / Unfocus / Render FPS 只能改畫面／音效，不可節流可靠事件、收包與固定遊戲邏輯。

### Movement / Animation

- Server Blocking.dat 權威 A*。
- 長按 1 秒鎖定移動；左／右鍵停止，右鍵選單不能失效。
- Move 不能錯誤覆蓋 Attack/Hit/Death。
- 保留 direction、ACT timing、trim/origin、flip、paper-doll draw order、world Y-sort。
- 動畫播放速度不得因 Move packet frequency 或角色移速錯誤加速。

---

## 9. 素材與資源

正式 Converter：

`C:\SGO\SRC\SGTest\tools\asset_converter`

一個原素材一個 logical/physical Asset；Frame 高度排序、POT 尺寸只是 Asset 內策略。

禁止跨素材合圖。怪物目錄使用 `Enemy/`，不得多一層 `generation/`。

保留 Asset Identity、Direction、Action、Frame、Duration、Origin、Pivot、Trim、Alpha、Draw Order。

資源支援 preload、lazy load、cache、texture reuse、owner/reference lifecycle、map change、logout、AFK、Unfocus、release、restore。

可用 Atlas、Texture Pages、PCK 或 Godot 適合格式，但不得破壞畫質、sprite identity、animation、draw order、alpha、gameplay visibility。

`.cache` 是可重建輸出，不可含唯一程式碼或 Git worktree。清理前保護其他 Session WIP。

未有發佈權時不可把原圖、音效、反編譯文本複製進公開 Git。

---

## 10. Item / Equipment / Data

正式模型：

`Item Definition + Item Instance + Enhancement Level`

不要以每個 +1/+2 不同 Item ID 作為正式架構。

Item instance 可含 Definition ID、Instance UID、Enhancement Level、Durability、Additional Stats、Stack、Ownership。

玩家看到的名稱、圖示、強化、能力、限制、交易結果必須正確。

資料驅動：Item、Equipment、Skill、Monster、NPC、Shop、Drop、EXP、Enhancement、Quest。

Server 負責權威數值與交易一致性。

---

## 11. 完整玩法範圍

最終必須覆蓋 HK 可確認的：

- 登入／登出
- 創角／選角
- 人物屬性、HP/MP、配點
- 地圖／傳點／移動
- 戰鬥／技能／Buff/Debuff
- 怪物／掉落／拾取
- NPC／商店
- 背包／裝備／強化／修理
- 倉庫／銀行
- 玩家交易／玩家擺攤
- 組隊／社團／好友
- 任務
- PvP
- Pet
- Mecha
- 死亡／復活
- GM
- 其他 HK 可確認功能

每個系統必須有適用的 Client、Server、Persistence/DB、UI、玩家操作、Multiplayer、Regression，不能只有按鈕或外觀。

---

## 12. Feature Matrix

`docs/FEATURE_MATRIX.md` 是唯一正式功能完成度矩陣。

狀態：

`NOT STARTED / IN PROGRESS / IMPLEMENTED / TEST FAILED / NOT VERIFIED / VERIFIED / COMPLETE`

規則：

- `IMPLEMENTED` 只代表程式存在。
- `VERIFIED` 必須有實際測試證據。
- 玩家可見功能沒跑 native Windows Client，不能因 headless PASS 標 `VERIFIED`。
- HK UI 明顯未對齊，即使操作可用，也不能把 UI 欄位標 `VERIFIED`。
- `COMPLETE` 必須滿足適用的 Client、Server、UI、數值、Persistence、Single、Multiplayer、Edge、Regression、Native Windows、Original A/B。
- 任一必要條件缺失就不能 `COMPLETE`。

`todo.txt` 是當前工作優先級；若 TODO、Matrix、實機證據衝突，採最保守狀態。

---

## 13. 測試與效能

自動化維護：

- Unit
- Integration
- Protocol
- E2E
- Multiplayer
- Visual Regression
- Performance
- Long-running Stability

**自動化 PASS 不是玩家體驗 PASS。**

代表性效能場景：

- 單人
- 多怪
- 多人
- 多人戰鬥
- 大量掉落
- 換圖
- 登出重登
- AFK / Unfocus
- 多開
- latency / jitter
- 長時間遊玩

量測：

- Client Frame P95/P99
- Server Tick P95/P99
- CPU / RAM / RSS
- GPU / VRAM
- Draw Calls
- Network throughput
- Packet Queue Age
- Resource load time

不得透過降低 gameplay correctness、Server Authority、必要同步、畫質或正常玩家可見內容來假裝效能改善，除非該行為是明確產品設定。

重大架構／演算法若有 2～3 個實質候選，才執行公平同工作負載比較；結果差異不顯著時選較簡單、低耦合方案，不無限實驗。

---

## 14. OpenChatX / Windows 授權與安全

使用者已授權 SGTest 一般可逆開發操作，不需每次重問。

可直接：

- 讀／搜／改本機檔案
- PowerShell / CMD
- Git
- dotnet build/test
- Godot
- 啟動 SGTest Server / Client
- Windows Computer Use
- screenshot / log / performance
- SGOAuto
- 原版 EXE
- test scripts

必須：

- 保護其他 Session
- 原版／二創研究來源唯讀
- 不刪未知檔案
- 不殺未知程序
- 不改其他人／正式使用中的 DB
- 不洩漏私人素材
- 高風險、不可逆操作需另外授權

桌面工具不可用時：

- 完成其他可驗證工作
- 明確標 native Gate 未驗
- 不得假裝已實機測試

---

## 15. 根因優先

相同 bug 反覆出現時，優先查：

- state ownership
- lifecycle
- WorldReady / MapEpoch
- player profile
- authoritative stats
- UI binding
- packet ordering
- pending action
- input lock
- AOI baseline
- resource owner
- duplicate implementation

典型例：

- 登入先變預設、走路才恢復：查 profile/presentation readiness/late asset registration，不只加 delay。
- 換圖後不能走：查 map lifecycle/input lock/WorldReady/pending movement/stale epoch，不只強制 enable input。
- HP/MP 錯：查 authoritative stats owner/snapshot/reducer/UI binding，不只改 Label。
- STR 等按鈕無作用：查 UI → intent → Server Authority → result → reducer → UI 整條鏈。
- 登入畫面不像 HK：先做 Paradise/HK A/B，查正式 auth shell/layout/artwork/style owner，不以臨時 Test UI 長期替代。

修正後增加 regression test，但仍需 native 實機。

---

## 16. 程式／文件路由

- `tools/asset_converter/`：唯一正式素材 Converter。
- `client/`：正式 Godot Client。
- `server/`：正式 C#/.NET 8 Server。
- `docs/FEATURE_MATRIX.md`：唯一正式功能完成度。
- `todo.txt`：目前工作優先級。
- `docs/`：規格、架構、測試證據與研究。
- 歷史研究文件只作證據，不為配合現行設計重寫歷史原文。

修改 Protocol、AFK、AOI、素材格式、UI Gate、測試 Gate 時，同步核對 SPEC／Architecture／TEST_PLAN／TODO／Feature Matrix。

每次完成記錄：

- Before / After
- 修改檔案
- 測試命令與結果
- Native Windows 驗收內容
- A/B 證據（適用時）
- Commit SHA
- 未解風險 / NOT VERIFIED

---

## 17. 每輪交付 Gate

正式交付前確認：

1. 核心 Smoke 無已知失敗。
2. 核心登入／選角／HUD／人物資料的 HK UI 狀態沒有被錯標完成。
3. Targeted tests PASS。
4. 必要 integration / multiplayer / regression PASS。
5. 玩家可見變更已跑 native Windows Client。
6. 多人相關已用雙 Client。
7. 動態 UI／動畫／移動有連續觀察。
8. 可比原版的功能已 A/B，否則明確 `NOT VERIFIED`。
9. Godot / Server 無本輪新增重大 error。
10. TODO / Feature Matrix / 必要文件更新。
11. Diff review 完成。
12. branch = `main`。
13. fetch 後安全整合其他新成果。
14. push `origin/main`。
15. 本機 HEAD = `origin/main`。
16. `main...origin/main` = `0 0`。
17. 本 Session 暫存 Git branch/worktree/clone 已清。
18. 其他 Session dirty WIP 未被破壞。

---

## 18. 每輪成果回報

最後必須用玩家角度回答：

- 實際開遊戲測了什麼？
- 登入／選角／HUD 與 HK 的 A/B 進度是什麼？
- 哪個原本壞掉的玩家流程現在真的好了？
- 哪個新功能現在真的可玩？
- 雙 Client 實際驗了什麼？
- 原版 A/B 驗了什麼？
- 還有哪些已知可見問題？
- 哪些仍是 `NOT VERIFIED`？
- 最終 `main` SHA 是什麼？

測試數量、protocol 數量、schema 數量只能當證據，不能單獨當玩家成果。

**永遠記住：SGTest 的成功標準是「使用者打開目前 main，就能明顯感覺比上一輪更完整、更穩、更接近 HK 原版」，不是「Repository 裡看起來做了很多」。**
