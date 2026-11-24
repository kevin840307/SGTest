# SGTest — 詳細工程契約與驗收參考

本檔保留 v11/v12 的詳細 UI、架構與完整 Smoke 基準，僅按當前切片及風險查閱。**執行順序、驗收範圍、交付條件一律以根目錄 AGENTS.md 為準。** 本檔不是每輪必讀或全量執行清單；舊文字中的「每次」僅適用於相關風險與完整驗收，不得覆蓋 AGENTS.md 的分級規則。原章節編號保留，方便既有引用。

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

因此 UI／人物／怪物／特效等原版圖片一律依下列**硬性素材搜尋順序**（使用者已完成解包，先用現成圖，避免重複解包）：

1. **首選 HK `C:\SGO\Stargate Online IE HK\Data1_PNG2`**：優先搜尋子目錄的已解包 PNG／frame。
2. **其次 HK `C:\SGO\Stargate Online IE HK\Data1_PNG`**：PNG2 找不到、缺 frame 或資料不完整時回查。
3. **再次 HK 原始 `C:\SGO\Stargate Online IE HK\Data1.dat`**：兩處解包資料都不足時，才使用現有 converter / `sync_original_ie_ui.py` 從 HK 原始封包補解，維持原始像素、alpha、frame、origin、trim、尺寸與可回查的資源身分。
4. **最後才搜尋 HK 其他 loose folders**：例如 `C:\SGO\Stargate Online IE HK\Interface` 等原版目錄；再往 Paradise／其他已授權參考來源找。**不能因 folder 比較容易搜尋就跳過上述 HK Data1 來源**。
5. **只能從畫面觀察**：依 HK／Paradise 截圖、`.lst`、座標與尺寸證據重建並記錄 A/B。
6. **完全無證據**：才可暫時重建且標 `NOT VERIFIED`，不得宣稱 HK 一樣。

每一個資源要核對名稱、frame/動畫狀態、alpha、origin、比例與所屬版本；若 PNG2 中同名圖疑似損壞或不符 HK，記錄證據並回退到 PNG／Data1.dat，不得盲用。使用者已解包資料是**本機唯讀來源**；不得提交圖片、Data1.dat、解包輸出或原版私有素材到公開 Git。

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

找到資源名稱後，**優先以 HK 的 Data1 已解包內容搜尋**：

1. `Data1_PNG2`（最高優先）→ `Data1_PNG`。
2. 兩處沒有可信完整圖／frame 時才查 HK `Data1.dat` index，使用 `sync_original_ie_ui.py` / converter 補解。
3. 最後才查 HK `Interface/*.spr` 等 loose folder／其他版本參考。
4. 缺解碼項目時擴充 converter／manifest，**不得重畫替代**。
5. 核對來源版本、frame、alpha、origin、文字／控制項原始位置，再套入 Godot。
6. **優先使用 `C:\SGO\SGO Paradise\program\client.exe` 真正開遊戲做 A/B**；HK 與 Paradise 有版本差異時以 HK 資料及反編譯證據為準並註記。

證據鏈：`HK .lst 邏輯/資源名 → HK Data1_PNG2（缺才 Data1_PNG → Data1.dat → HK loose folder）→ Godot → Paradise 實機 A/B + HK 差異核對`

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

### 1.6 原版圖片與文字位置必須同步 1:1（P1 強制 Gate）

**原版 SPR／PNG 貼對，不等於 HK UI 對齊。禁止只套背景圖、再疊一批 Godot Label「補文字」而不修文字實際位置。** 文字、數字、輸入欄位、圖片、按鈕、hitbox 與動態狀態是同一個 UI 交付物；任一錯位都不得宣稱 HK UI 完成。

**立即優先清理已使用原版圖但文字仍偏移的核心 UI**：登入 → 選角／創角 → 主 HUD → 個人資料／STR 等屬性 → 背包／裝備 → 本輪玩家功能。不得以「有用原圖」跳過已知文字錯位，或先新增其他 UI 功能。

逐畫面按以下順序實作、驗收：

1. **有證據才定位**：從 HK `.lst` 追視窗、sprite/frame、文字控制 ID、原始 x/y/width/height、font/繪字函式；可執行原版／Paradise 以 800×600 相同狀態截圖，區分版本差異。
2. **確認文字來源**：檢查 PNG／SPR 是否已有烘焙文字，已有則不可重複疊 Label；只有原版實際動態文字（角色名、HP/MP、屬性、數字、輸入等）才新增對應動態繪字。
3. **共同座標系**：以原版面板的 800×600 local origin 統一圖片、文字、控制項與可點擊區域。核對 SPR 透明邊、trim/origin/pivot、frame 尺寸、TextureRect stretch、父子節點 transform、anchor/offset、Control min-size、Container、StyleBox margin、viewport stretch、DPI；修根因，不能每個 Label 獨立塞幾個 pixel magic offset。
4. **文字排版全還原**：核對字型／fallback、字級、字重、字距、line-height、baseline、左右／上下對齊、顏色、outline/shadow、換行、裁切、placeholder、cursor/focus。字體缺失要標差異，不得用單純移動文字掩蓋。
5. **動態與互動對齊**：姓名長短、HP/MP 數值更新、選角、輸入、hover、pressed、disabled、selection、tooltip、focus、關閉重開、換圖／重登後位置仍一致；顯示位置與 hitbox 不得錯開。
6. **真正 Windows A/B**：非 headless Godot 與原版在相同 800×600、相同 UI state 比較截圖；逐控制項記錄 ref/actual bounding rect、文字 baseline、Δx/Δy，利用 crop／透明疊圖／差分定位偏移。確認後再測 4:3／16:9／16:10 與縮放，不准 UI 文字相對原底圖漂移。
7. **硬性判定**：原版座標有確證時，800×600 靜態文字錨點、控制項以原始像素 ±1 為目標；字型光柵化差異要註記。任何明顯文字偏移、遮擋、重疊、裁切、點擊錯位皆是 `TEST FAILED` / `IN PROGRESS`，不能標 `VERIFIED` / `COMPLETE`。缺原版證據則標 `NOT VERIFIED`，不得假報 A/B PASS。
8. **保留證據與回歸**：記錄原版 resource/frame、HK 來源或對照圖、各 rect／baseline／diff、Godot 實機截圖、測試結果；加 visual regression/geometry tests（不代替實機）。私人原圖、私人截圖／反編譯資料不得提交 public Git。

**優先修原圖與文字共用的布局／縮放／座標根因，之後才做特例微調。只補字、不修位置、只用自動測試判 PASS，均屬未完成。**

#### 1.6.1 字型 + 文字幾何必須與原版一致（P1 硬性 Gate）

**「字有出現」不算完成；「位置差不多」也不算完成。只要原版證據存在，字型外觀與文字幾何都必須以 HK／可驗證原版為準，不能自行選一個相似字體或靠肉眼大概移到附近。**

每個玩家可見文字元件都要視為正式 UI 元件，至少核對並記錄：

- font family / face / 原始字型來源
- font size
- weight / bold 狀態
- glyph fallback 與繁中 glyph 實際來源
- letter spacing / tracking
- line height
- baseline
- ascent / descent（需要時）
- text rect 的 `x / y / width / height`
- horizontal / vertical alignment
- outline / shadow 的寬度、offset、alpha
- 顏色與 disabled / selected / hover 等狀態色
- clipping / ellipsis / wrap
- input placeholder
- caret 位置與高度
- selection highlight
- password mask glyph
- 數字欄位與動態文字更新後的 anchor / alignment

原版字型／文字證據的優先來源：

1. **HK `.lst`**：追 font 建立／選取、文字繪製函式、control/text ID、draw rect、x/y、baseline、format flag、color、shadow/outline 等。
2. **Paradise 既有截圖**：`C:\\SGO\\SGO Paradise\\program\\ScreenShot`。先搜尋是否已有相同畫面／狀態可直接量測；這是原版 UI、字型外觀、文字框、baseline、欄位間距的重要 A/B 證據。
3. **Paradise 真實 Client**：`C:\\SGO\\SGO Paradise\\program\\client.exe`。既有 ScreenShot 沒有需要的狀態時，Agent 已獲准自行啟動原版 Client、登入、操作到等價 UI 狀態，以 **800×600** 擷取新的參考畫面；不得因缺截圖就跳過字體／文字位置驗收。
4. HK `Data1_PNG2` / `Data1_PNG` / `Data1.dat` / SPR：確認底圖是否已烘焙文字、文字槽位、透明邊、frame origin 與實際可用區域。
5. Paradise 與 HK 有版本差異時，以 HK `.lst` / Data1 / HK 素材證據為最高權威，Paradise 只作可操作參照並註明差異。

**字型不可猜。** 若目前只能確認「看起來像某字體」而沒有可信 font family/face 或 glyph raster 證據：

- 狀態必須是 `NOT VERIFIED`
- 不得因字寬近似就標 HK 1:1
- 不得用 Arial、Noto、Microsoft JhengHei、系統預設字體或其他「相似字體」直接替代後宣稱完成
- 若原版字型檔只能作本機授權研究，可在本機驗證／載入，但不得把 private/proprietary font 檔提交 public Git，也不得將字型檔作為交付物散布
- 若最終必須 fallback，Feature Matrix / TODO 必須記錄「原版 font 未確認／fallback 使用中」及可見差異

**文字位置不可猜。** 所有位置都以原版 **800×600 原始像素座標** 為基準，先修共同座標系，再調文字。禁止：

- 每個 Label 個別加 `+2/-3 px` 之類無證據 magic offset 來掩蓋父節點／Container／DPI／trim 問題
- 用 Godot Container 自動排版後只看「差不多」
- 底圖換成原版 PNG，但保留舊自創 UI 的 Label 座標
- 只校準左上角，不校準 baseline、文字框寬高與右／中對齊
- 靜態文字對了，但 HP/MP、EXP、角色名、數字變長後就漂移
- screenshot 看起來接近，但 hitbox／input field／caret 與文字槽不同步

**驗收方法固定：**

1. 先從 `ScreenShot` 搜現有原版參考；不足時自行啟動 Paradise `client.exe` 補取相同 UI state。
2. 固定原版與 SGTest 都使用 800×600。
3. 對每個文字元件記錄原版 `rect(x,y,w,h)`、baseline、font evidence，以及 SGTest 實際 rect。
4. 做 crop / 透明疊圖 / pixel diff；必要時以字框、baseline guide 線輔助。
5. 靜態、hover、pressed、disabled、focus、輸入、數值變更、長短字串、關閉重開都要驗。
6. 800×600 有可信原版座標時，以 **文字 anchor / baseline / control rect 原始像素 ±1 px** 為目標；超出或肉眼可見偏移直接 `TEST FAILED / IN PROGRESS`。
7. 字型 family/face/size/weight 或 raster 明顯不同，即使 x/y 正確也不能 `VERIFIED / COMPLETE`。
8. 800×600 PASS 後再測 4:3 / 16:9 / 16:10 / DPI，文字相對原版面板不可因 anchor、stretch、Container 而漂移。

**核心 UI 特別嚴格**：登入、選角／創角、主 HUD、人物資料／屬性、背包／裝備只要有字體不符或文字錯位，就列為 P1 UI regression；不得以「功能可用」跳到下一功能。

---

### 1.7 Screenshot-first UI 驗收：差距必須可量測

每次 UI 切片以原版 reference → SGTest Before → SGTest After 三張同狀態、相同實際 client area 800×600 的可見畫面為基本證據。先用現成 Paradise ScreenShot，缺才開 client.exe，HK 版本證據優先。不得把素材 PNG 或 headless/test scene 當作實機畫面。

- 先記錄可比較靜態 ROI 與動態遮罩（只排除真正不可控的世界背景/時間/角色資料等，禁止遮掉錯位文字、控件或素材）；保留完整未遮罩畫面。以 side-by-side、50% overlay、pixel diff 找差異。
- 每個可見區塊核對資源/frame、原始尺寸/origin、面板 rect、文字 rect/baseline/font、顏色、層級、控制 hitbox。可信原版座標以 ±1 px 為目標；有明確證據的字型、圖示及交互狀態不可近似替代。
- 以原版靜態像素一致為目標；允許記錄可解釋的光柵化/版本差異，不允許用整張圖的高相似度平均掩蓋局部錯誤。報告每個主要 ROI 的差異及原因，不自創全局 PASS 門檻。
- 原版來源及實際渲染必須一一對應；禁止大張 screenshot 當 UI、把文字烘焙成假互動、用測試特例、僅改測試斷言或把 mask 當修復。
- After 未改善、仍有肉眼明顯偏差、點擊位置錯誤或無同狀態原版證據，不得標 VERIFIED/COMPLETE。若本輪只改善部分，明確列出修前/修後差距和剩餘項，保持 IN PROGRESS；下輪優先接續。
- 可重用 reference、ROI、座標 manifest、共用 font renderer 及既有工具；不為每個畫面創建獨立的布局框架。一次取得多個已開啟畫面之有效證據可供後續切片重用。

---

## 4. 核心玩家 Smoke Gate — Client 工作硬門檻

只要本輪碰到 Client、UI、Renderer、輸入、玩家呈現、移動、戰鬥或世界狀態，就必須用真正可見的 Windows Client。以下為完整 smoke 目錄；每輪依第 3.1 節決定適用範圍，並非所有 UI 修改都須全量重跑。

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

### Paradise 舊版實機 — 原版遊戲實機測試第一優先

原版可執行 Client：

`C:\SGO\SGO Paradise\program\client.exe`

原版既有截圖目錄：

`C:\SGO\SGO Paradise\program\ScreenShot`

**做 UI／字型／文字位置／行為 A/B 時，先主動搜尋 `ScreenShot` 是否已有目標畫面；已有就直接作為字型外觀、文字 rect、baseline、欄位間距、按鈕與面板位置的參考證據。既有截圖缺少所需 UI state 時，不要停在「沒有參考圖」：Agent 已獲准自行啟動 `client.exe`，用真滑鼠鍵盤登入並操作到等價狀態，在 800×600 補取截圖／錄影，再與 SGTest 做 overlay/diff。**

**需要測原版遊戲時，優先直接啟動這個 client.exe，以真滑鼠鍵盤操作；不要先以其他版本、Recovered2、headless 模擬或憑印象重建取代。** 原版 Client 用來取得畫面、流程、座標、字型、文字 baseline/rect 與行為 A/B；SGTest 仍須獨立以正式 Godot Client／Server 真實操作驗收。Paradise 無法驗的版本差異改從 HK `.lst` / Data1 素材與其他可信資料補證，不得以 Paradise 為 HK 版本唯一權威。

原版測試使用已授權本機測試帳號，從既有安全來源取得；不得將憑證寫入公開 Git、命令輸出、報告或截圖。

原版 Server 通常已由使用者啟動，但每次仍確認狀態。

Paradise 比 HK 舊；目前不適合用來驗擺攤、社團、好友，但可驗與 HK 共通的登入、選角、UI、地圖、移動、戰鬥、動畫、NPC、物品、世界呈現。

Paradise 無法使用某功能，不代表 SGTest 可以省略 HK 對應功能。

### SGOAuto — 原版記憶體觀察／控制與自動測試的重要參考

`C:\SGO\SGOAuto`

**SGOAuto 不只是座標、HP/MP 讀取工具；它包含對原版遊戲 Process Memory／狀態／輸入／行為控制的參考方式。** 當 Paradise 原版 UI 不知道如何點開、NPC 不知道怎麼對話、人物不知道如何前往指定地點或某段操作無法可靠重現時，應主動先研究 SGOAuto 既有程式碼與操作方法，不得直接判定「無法驗證」而跳過。

優先研究／重用的思路：
- 原版玩家座標、目前地圖、NPC／玩家／怪物物件、HP/MP、選取對象、目前動作或 UI 狀態的觀察與記錄。
- 如何靠原版記憶體資訊找到 NPC、辨識對話／商店／任務視窗的前置條件，再用原版正常操作流程開啟、切換、關閉 UI。
- 如何透過原版已有的移動、導航、目標選取與操作方式，讓人物走路、接近 NPC、點擊對話、傳點或重現指定場景。
- 針對原版功能製作可重複 A/B 操作步驟：先以 Paradise `client.exe` 可見實機操作與截圖／錄影為主，必要時輔以 SGOAuto 觀測記憶體狀態、決定操作座標及驗證結果。
- 研究 SGOAuto 的程式入口、定位方式、狀態讀取與操作路徑；找不到對應功能才改查 HK `.lst`、原版資料及其他證據。引用時記錄來源檔案／方法、適用版本、實測結果。

**實際操作原則**：SGOAuto 僅在使用者授權的本機原版測試環境輔助研究；優先正常玩家輸入／原有安全操作，避免不必要或不明的記憶體改寫、注入與可能破壞遊戲狀態的操作。原版／SGOAuto 程式碼維持唯讀；不可誤把外掛記憶體地址、私有結構或工具依賴移植成正式 SGTest Runtime。記憶體位址、物件 layout、行為呼叫與 UI 對應必須依版本驗證，不得假設 Paradise、HK、Recovered2 共用。

**最終仍須以可見 Paradise 實機的玩家可觀察行為、HK 版本證據與 SGTest Godot 真操作驗收，不得以「SGOAuto 顯示成功」取代 A/B。**

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
