# SGTest — 多人 MMORPG 重建規格（2026-11-15）

> **唯一正式維護位置：`C:\SGO\SRC\SGTest`，唯一正式開發分支：`main`。**
> 此版本以文件與可獨立執行的原版素材轉換器作為重建起點。**舊 Godot Client、舊 Server 程式與測試原型全部移除；新的 Client/Server 尚未完成。**
> 實作進度只看 [todo.txt](todo.txt)，不能將過去 Recovered／舊 SGTest 的 benchmark 當作新版遊戲 E2E PASS。

## 1. 系統目標與技術選擇

重建《星夢 Online》風格 2D 即時多人 MMORPG：多玩家、多怪物、多掉落、頻繁移動與攻擊時仍保持即時、畫面正確、資源低且容易維護，支援同機多開與幾乎不渲染的 AFK 掛機模式。

- **Client：Godot 4（Windows / macOS）**。先完成後期原版金色風格 UI，不複製舊 TestGoGo 介面；其他 Theme 暫不列入第一階段，以免過度設計。
- **Server：.NET 8 / C#**；權威的地圖、走路、碰撞、戰鬥、AI、背包／物品與交易。資料儲存初期 SQLite，應用程式、網路、資料存取彼此隔離。
- **設計方式：功能導向 modular monolith**，高聚力、低耦合；不要預先建立重複的 Manager／Engine／Cache、不同素材類型專用 Loader，或空白多層分組。Client、Server 透過明確封包／資料契約整合，不共享可變狀態。
- **Server single-writer world authority**：同張地圖的可變 State 有明確單寫者；DB I/O、Socket Send、慢路徑查詢不能阻塞固定 Tick。
- **有界佇列與恢復**：所有網路／World 命令／視覺事件佇列都要有容量、延遲指標與過載政策；可合併過時的非可靠移動快照，**不得默默丟失金錢、物品、交易、角色狀態、權威戰鬥結果等可靠結果**。無法保證連續性時須觸發重同步或安全斷線，不能無限堆積記憶體。
- **Wire 不綁特定資料格式**：JSON、MessagePack、Protobuf、自訂 packet header + binary payload 都只是候選，先對照原版／Recovered 與實測 bytes/sec、編解碼延遲、CPU／配置／GC、Godot/.NET 實作成本，再選單一正式協定；不因好寫就把高頻即時封包預設為 JSON。

## 1.0 MVP：Map15 主城 + Map13 台地，功能完整且真實跨圖

**MVP 正式涵蓋兩張原版地圖**：Map15（主城：登入、選角、NPC／商店／銀行／倉庫、裝備、交易、玩家商店、社交等）與 Map13（尼西亞台地：Blocking／A*、多人／怪物／掉落、攻擊／技能、AI、AOI／同步）。須先核對原版兩圖 NPC/怪物／傳送位置，不能將角色定位或原版地圖規則自行編造。2026-11-15 已由唯一 Converter 實測 Map15／Map13 原始 Terrain、Blocking、Scenery 與 NPC 靜態傳點資料，另解析 82 筆原版 Enemy Spawn、10 種怪物的 SPR/ACT（557 動畫 Frame）並以正式 Height-Dense Builder 試轉 10 張獨立 Atlas；詳見 `todo.txt` 的 MVP001 子項。完整 7,890 素材 Archive audit 與兩圖 242 Asset／2,269 Frame 的定向 Catalog 已 PASS。雙圖 Runtime content／外部 PCK 資源 Gate 已 PASS（244 Texture）。**仍未完成 NPC 動畫／動態規則、真 Renderer／GPU 資產生命週期、Server 權威 Spawn/Blocking 與跨圖 Client/Server E2E**。

**兩圖必須真實互通**：Server 權威 Map Change、MapEpoch、WorldReady、AOI 舊圖清理／新圖快照、AssetStore 地圖切換資源釋放與預取、位置合法性、重連／返回已掉落物。要測 Map15 → Map13 → Map15 與多人同時換圖，不能將跨圖 E2E 延後 MVP。

兩圖 MVP 必須完整實作且依原版驗證主要玩法：登入／創角／多角色／角色選擇、A*／長按與傳點、Player／Enemy／Pet／Mecha 八方向／ACT／DrawOrder、AOI／移動補間、怪物獨立 AI、普攻／單體／範圍技能、MISS／死亡復活、Item UID／背包／裝備／強化／修理、NPC 商店／倉庫／銀行、地面掉落／撿取、寵物與機甲／卡片／升級、玩家交易／玩家商店／聊天、PvP 名稱規則／GM、SQLite 持久化、AFK／Unfocus、多開及錯誤／斷線／多人競態。**功能完整度不等於只做示範按鈕或單一 happy path；好友暫緩。**

依序：Map15 與 Map13 原版素材／Blocking → Godot 主城地圖與相機／Y-sort → .NET 8 Server／公平選型 Wire → 登入／角色／Server 移動 → Map13 與兩個真 Client AOI／方向動畫 → 雙向 Map15↔Map13 傳點 → 多種怪物／Combat／技能／Drop → 兩圖完整遊戲／社交／經濟系統 → AFK、AssetStore／FPS 解耦及性能 Gate → MVP PASS。先做小型可重跑切片，合格再擴後續依賴。

MVP 負載：Map13 **同張地圖**逐級驗證到 20 名連線玩家／300 Enemy／500 Drop（至少兩個真 Godot Client，其他 Bot 必須明確標記）；Map15 多人 NPC／玩家商店交互，雙圖同時有人及往返切圖，測 AOI／Tick／可靠事件／傳輸／CPU／RAM／VRAM／P95/P99。**100 玩家／1000 怪／5000 掉落、全地圖及全部原版內容**才是 MVP 後擴量 Gate，不能外推 MVP 成果。

## 1.1 多方案選型與逐步開發契約

- **存在多個合理方案時，先研究、實測、選整體最佳解，再開始依賴該方案的新功能。** 涵蓋 Wire、AOI、怪物 AI／路徑、同步補間、Renderer、素材 Atlas/PCK、AssetStore、AFK、持久化等。
- 至少比較現行 baseline（若存在）和有實質差異的候選；同資料、同原版素材／地圖、同可見實體／負載、同解析度與硬體，包含稀疏／密集、多人、多怪、多掉落、AFK、多開；保留多輪測試證據。
- **先排除錯誤方案**：原版玩法、Server 權威、可靠戰鬥／交易、正確 AOI、動畫、碰撞、安全與資源回收是硬性條件；再看玩家體感、P95/P99、CPU／RAM／VRAM、頻寬、GC、資產載入與維護成本。
- 實測差異不明顯時優先採簡單、可驗證、低耦合方案；正式只保留一個維護路徑，不因優化而增加多套 Loader、Converter 或持續存在的替代 Client/Server。
- 每次選型的候選、指標、比較數據、結果與未驗證項目更新到現有 SPEC／架構／TEST_PLAN／TODO；隔離微基準不能當成真實遊戲 E2E。詳細規則見 AGENTS.md 與 docs/TEST_PLAN.md 的 ALT001。

## 2. 資產轉換（必須保留已驗證方案）

正式唯一入口 `tools/asset_converter/cli.py`，原版素材完整性檢查及 Runtime PCK 打包是同一套 Converter 的兩個輸出階段：

```text
原始 HK2 Data1.dat / SPR / ACT / SIF / Server Dat（唯讀）
  -> 解析、來源 SHA256、逐 Frame 原點／像素驗證
  -> **Per-Asset Height-Dense P0 Atlas**（同一素材 Frame 高降序、寬降序）
  -> 每個來源 Asset 自己的 Atlas 和 logical-view metadata
  -> 自動選非固定寬高，單頁 ≤4096×4096，超量才同素材分頁
  -> Godot 匯入 Texture2D（場景圖像 VRAM compression；UI／細小圖示 lossless）
  -> **獨立 external assets.pck**，Client 以 ResourceLoader / AssetStore 載入
```

不得跨素材合併：Enemy、NPC、Player part、Equipment、Item、Pet、Mecha、Scenery、Effect、UI 各自以原 Asset ID 為邊界。正式怪物目錄只用 `client/assets/Enemy/<id>/`，不增加 `generation/`。動畫資料必須保留 ACT tick、Action、8 方向／鏡像、Trim、Origin、來源幀序與事件。P0 如發現真實壓縮邊緣污染改 P1，POT／MaxRects／強制尺寸分桶不是預設策略。

`tools/asset_converter/atlas/dense_atlas.py` 是正式 Builder；`tools/asset_converter/runtime_pack.py` + `runtime_pack_packer.gd` 是 PCK 輸出。舊版 Benchmark 方法與結果封存在 docs/research／Git 歷史，不作第二條正式產線；未來新版 Client/Server 必須另做真實負載測試。Windows/macOS 共用 logical ID／metadata；平台 GPU 格式與 macOS Release **仍須實機驗證**。

## 3. Client 與 Renderer 必須彼此隔離

```text
Network I/O / Codec / Bounded Queue
         ↓
Input Command + Sequenced World Events
         ↓
Client WorldState Reducer / Prediction / Reconciliation
         ↓ (不可回頭控制前兩層的速度)
World Rendering / Animation / UI / Audio
```

- 收封包、命令、WorldState Reducer 與伺服器 Tick **完全不依賴畫面 FPS**。Render 0／30／60／90、縮小視窗、Unfocus 或 AFK 均不能降低可靠事件的完整性；使用固定邏輯更新、事件序號、MapEpoch／EntityRevision，避免順移與舊圖殘影。
- **Enemy Renderer 首選：每種 Enemy 共用 `SpriteFrames`、每個可見實體獨立 `AnimatedSprite2D`，配合 Height-Dense 壓縮貼圖和 ResourceLoader。** 要實作 Origin 校正、ACT 時間、Flip、Animation Interrupt；所有 Monster、Player、NPC、地圖場景與掉落物同一 World Y-sort，不能只改怪物渲染破壞互相遮擋。
- Player 紙娃娃、裝備、寵物、機甲與掉落物需分別對照原版 DrawOrder；不能因單一 Enemy benchmark 快就直接全部套用。
- 同機多開時，每個 Process 都有私有 Session/State。共享 Texture/Animation 只在同一 Client 內按 Asset ID 引用計數；禁止把同一怪物的不同方向當不同獨立素材重載。

## 4. AFK／Unfocus／多開

- **AFK 基本不渲染遊戲內容（World Render=0）**，音效關閉，只保留可選極簡摘要。盡可能釋放非必要地圖 Sprite/Texture、特效與其他可重建 GPU 資源；必要 Network／WorldReducer、玩家狀態、可靠傷害/MISS/物品/交易／死亡事件持續處理。
- 從 AFK 回復必須遵循 MapEpoch、WorldReady 快照與 AssetStore acquire/cancel/release，不能復活上一張地圖的資產引用或 AOI 實體。
- Unfocus 在效能頁提供開關，可降 Render 頻率但不影響網路；**最小化視窗的低渲染／可卸載資源永遠生效**。
- 同機 1/2/4/8 開混合正常＋AFK 測 CPU、Process Peak RSS、Texture／GPU、封包 Queue Age、輸入到畫面 P95/P99。不能用丟可靠封包換取省資源。

## 5. 原版行為、A* 與多人即時性

- Server 使用原版 **Blocking.dat** 做地圖合法性與權威 A*；Client 可用相同網格局部規劃、路徑預測與 correction。阻擋角、台地階梯、追怪距離與重規劃需保留。
- **地板長按約 1 秒鎖定走路**，左／右鍵或重新點地板取消，右鍵選單不得受影響。Client 不得依 Render FPS 重送 Move 封包；攻擊、受擊、死亡、換圖都能中斷／釋放鎖定。
- 移動與攻擊距離使用原始遊戲數值／公式，不以武器種類硬編碼；移動速度不應讓角色腳步或機甲動畫倍速播放。
- **AOI 是核心網路傳輸契約**：Server 依 Map、權威位置、受限的 viewport／interest 範圍和適當緩衝，只發送觀察者需要的 Player、Enemy、Drop 等；用 map-local spatial grid/index 避免全圖掃描／廣播。進出以可靠 Spawn/Despawn 和 WorldReady 同步，持續用有變化才發的 Delta、批次與舊位置合併減少傳輸，視野邊界使用 margin/hysteresis；可靠戰鬥／拾取／交易結果仍按權限送達。多怪物巡邏／追擊、掉落物和廣播必須有界預算；Slow Packet、World Tick、Monster AI、AOI、Send Queue 分開量測。
- **參考來源（唯讀）**：`C:\SGO\Recovered\client`（動畫／UI／A*／長按）、原版 `Old` 及 `Paradise.exe.lst`、原版 Server 反編譯，以及原始遊戲安裝目錄。可以移植行為與測試，不讓新版 Client/Server 的編譯依賴 Recovered／Old。

## 6. 已驗證證據與尚未完成的 Gate

歷史隔離測試證據：[docs/research/ASSET_RENDERER_EVIDENCE.md](docs/research/ASSET_RENDERER_EVIDENCE.md)；不要把它們當新版 Client 的 FPS。舊 Map13 混合場景中，12 種 Enemy ×2000，Recovered vs SGTest Sprite 的 P95 分別是 **7.470ms / 6.900ms**、P99 **10.361ms / 7.728ms**、Process Peak RSS **219.8MiB / 176.6MiB**。原模型少數單一超大型怪物仍較快，且當時以玩家替身／掉落標記測試。

- **已完成**：獨立素材轉換器模組、Height-Dense 單素材 Builder 測試、PCK Policy 測試、原版動畫與渲染隔離 benchmark、文件／清理。
- **未完成（NOT VERIFIED）**：新版 Client/Server 真正連線、Map13 Server A*、紙娃娃與多人同步、AFK／切圖 30 次後記憶體、多開 1/2/4/8、Windows Release 外部 PCK、macOS 實機及 20/100 人＋300/1000 怪＋500/5000 掉落的 E2E 壓測。
- 每項新任務都依 [docs/TEST_PLAN.md](docs/TEST_PLAN.md) 定義可重現測試，完成後才更新 [todo.txt](todo.txt)。主要架構：[Client](docs/CLIENT_ARCHITECTURE.md)、[Server](docs/SERVER_ARCHITECTURE.md)、[Assets](docs/ASSET_ARCHITECTURE.md)、[Converter](docs/ASSET_CONVERTER.md)。

## Client 資源模組進度（2026-11-15）

單一 AssetStore 有界 metadata worker／threaded Texture、owner-epoch 去重／取消與釋放已實作，正式 PCK 的完整雙圖 palette 每輪 30 次、三輪 headless 回歸 PASS。job 並行 1／2 同 trace 比較依正確性選 1。詳細 API、CPU／RSS／pump 指標與失敗候選見 CLIENT_ARCHITECTURE／TEST_PLAN；這不是可玩 Client，也未驗證 Renderer／GPU 釋放或網路 FPS 解耦，MVP002／REB012 保持未完成。

## 地形 Renderer 小階段（2026-11-16）

Map15／Map13 原始 tile 地形已有唯一正式 Terrain Renderer；三種合理候選同圖／同trace／同Atlas／同解析度原生OpenGL各三輪、GPU截圖與獨立CPU合成零差異後，按準備／Frame tail／CPU/RSS／引擎資源與維護成本選可視tile區域繪製。數據與限制見 CLIENT_ARCHITECTURE；尚未完成地物／人物共同Y-sort、原版pivot／動畫、WorldState／Server與多人E2E，MVP002維持未完成。

2026-11-16 資料修正：world v3保留原版建築父子關係（Map15=1833根+15子），撤回先前「15筆缺Z」解讀；根記錄XY後為child_count，世界z=0。舊v2須由唯一Converter重建runtime/PCK，AssetStore拒絕v2。原版建築前緣排序／室內外顯示尚未實作，完整景物層與MVP仍未完成；見ASSET_CONVERTER的DIRECT證據及TEST_PLAN。

2026-11-16 景物資料進度：共用scenery v1定義已保留BLG兩appearance、SIF模式／幾何與建築前緣，補入12個alternate素材；正式Catalog現在254獨立Asset。72 tests、雙圖PCK／30次AssetStore循環／Terrain 16張像素回歸PASS。景物遮擋Renderer與切換時間仍未完成；詳ASSET_CONVERTER／CLIENT_ARCHITECTURE。

2026-11-16 實作狀態：原版root排序離線編譯與靜態景物／Actor前緣插入、原點／trim、兩appearance及室內子物件快照已實作並做原生GPU比較；正式選集中Canvas。這不是依Render FPS推動動畫／世界邏輯，frame與inside由外部明確輸入，目前沒有完整動畫時鐘、入口fade／自身ghost、Server或多人Client。Scenery主Gate維持未完成；證據／取樣限制／選型數據見CLIENT_ARCHITECTURE與TEST_PLAN。

2026-11-16 景物時序切片：8個原版ACT已發布default25／50Hz時間metadata，涵蓋雙圖61個多幀景物實例；呈現以明確elapsed updates取樣，支援非零起始index與長空白幀。建築fade按原版256counter／每Step減16／alpha clamp255及原始疊層順序繪製，不依Render FPS累進。入口／版本狀態、dynamic action speed、NPC／Player完整時序與事件、P0壓縮品質、Server／多人仍未完成；詳CLIENT_ARCHITECTURE／TEST_PLAN。

2026-11-16 建築焦點切片：已接連續fixed-update／epoch保護的inside呈現狀態與快照，保留原版last-wins、ShowInside／SetInOut差異、可見counter及室內Actor／家具額外排序。此為原版reference幾何與顯示規則實作，不是Server入口／碰撞或正式相機／多人Client完成；精度與驗證見CLIENT_ARCHITECTURE／TEST_PLAN。

2026-11-16 Atlas座標契約：Runtime頁面以右／下透明留白對齊4倍數，維持每素材獨立Height-Dense P0、Frame rect／origin／trim與原texel，不讓Godot壓縮器以整頁resize改變座標。archive頁不變，atlas v9記錄Runtime與Archive各自尺寸／SHA；舊輸出需重新export及pack。254頁候選實測與殘留codec畫質限制見ASSET_ARCHITECTURE；此修正不代表MVP／Mac／Release完成。
