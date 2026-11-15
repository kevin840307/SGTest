# SGTest — Clean-slate 測試與完工標準

**狀態：測試規格。舊 Client/Server 的 PASS 不能沿用。**

## MVP001 — 單地圖、完整功能與多人負載的驗收矩陣（全部尚未實作）

**僅限制使用一張 Map13，不省略任何可在單圖驗收的主要原版玩法**；禁止以 Recovered PASS、合成封包性能或單一 happy path 代替新版 Client/Server 的實測。

- **MVP-MAP**：唯一 Converter 提取 Map13 真實 terrain／Scenery／Blocking／metadata／PCK；尺寸、位置、原點、遮擋／Y-sort、相機、不同解析度、載入／卸載與來源覆蓋 PASS。
- **MVP-ACCOUNT/WALK**：帳號／多角色／創角選角／登入登出／重連、世界進入、點地板／長按 1 秒／點怪追逐／A*／障礙角／停止操作、Server 路徑合法性與速度驗證、狀態持久化／重試。
- **MVP-AOI/MOTION**：至少兩個真 Godot Client；雙向 Spawn/Despawn、WorldReady/MapEpoch、AOI 視野變化、不同解析度、位置插值、本人與他人的八方向／鏡像／Move/Stop/Attack/Hit/Death/Skill、人物／寵物／機甲動畫；可靠事件、視野外圍及上線瞬間不順移。
- **MVP-AI/COMBAT**：多種大小／動畫／方向的原版 Enemy；個體 AI 可站立、分散決策／隨機巡邏／追擊／脫戰，不能成批動作；普攻、單體／範圍技能、Buff、MISS、Damage、HP、死亡／復活、怪物掉落、拾取，Server Authority、對象可見權限與序號正確。
- **MVP-ITEM/ECONOMY**：背包／物品 UID／堆疊／使用／裝備／卸裝／強化／修理、金錢、NPC 商店／倉庫／銀行、地面物品 Spawn/Despawn/Pickup，確實測試錯誤輸入、物品競爭拾取、交易原子性／重送／斷線一致性。
- **MVP-SYSTEM**：寵物召喚／跟隨／攻擊／收回、機甲裝備／卡片／升級／屬性、技能效果及 CD、玩家交易／玩家商店 AOI、聊天、PvP 地圖規則／藍白粉紅紅名、GM 指令與授權；原版功能規則需有多種代表資料的完整測例，不能只測 UI 開窗。
- **MVP-NET/SECURITY**：Wire 正式選型前 ALT001 方案比較，Server-side permission/validation、bounded queue／backpressure、缺序／重連 resync、可靠事件冪等及惡意封包；FPS 改變不能改變 Client reducer／Server Tick。
- **MVP-RESOURCE/LOAD**：逐步提高到**同一張圖 20 位連線玩家／300 怪／500 掉落**，至少 2 個真 Godot Client 測方向／畫面，其餘 Bot 明確標示；測稀疏與密集 AOI、AI 時序、bytes/sec、Codec/Queue P95/P99、World Tick、CPU、RSS、VRAM、draw calls、Frame P95/P99，1/2/4 開、FPS 0/30/60、AFK/Unfocus、至少 10 次重載／登入恢復。
- **MVP-CROSSMAP-LIMIT**：單張地圖無法完成的跨圖／傳點／多地圖 AOI E2E，先驗證 MapEpoch/WorldReady 與轉圖契約／偽資料測試，明確標記 **E2E NOT VERIFIED**；MVP 後加第二張真圖再補完整驗收。
- **MVP-GATE**：以上所有**適用於單張地圖的**必要案例 PASS，主要功能按原版規則、Server 權威、正確方向／動作、多玩家／多怪／多掉落和低資源都已驗證；文件與 TODO 同步、ALT001 有公平數據，才可宣稱 MVP 完成。

**MVP 後的完整內容與規模 Gate**：逐張地圖／真實跨圖傳點、全部原版怪物／NPC／裝備／技能／物品與素材覆蓋、100 人／1000 怪／5000 掉落、同機 8 開、Windows/macOS Release。MVP 小場景不能直接外推為全圖 PASS；擴量須重新測 ALT001。好友功能暫緩。

## ALT001 — 多方案公平比較／決策 Gate

適用於正式 Wire、AOI、Monster AI／A*、同步補間、Renderer、Atlas/PCK、AssetStore、AFK／多開與持久化等有合理替代方案的重大開發決策。

1. **功能 Gate**：所有候選先通過同一組原版／Recovered 行為、權威判定、可靠事件、AOI／Spawn/Despawn、動畫／碰撞、安全與資源生命週期回歸，不正確者淘汰。
2. **對等負載**：現行 baseline（若存在）與 1～2 種合理候選使用同一素材、Map/Blocking、事件 Trace、負載分布（稀疏／密集）、解析度、VSync、Build、硬體與測試時長；納入正常／AFK、多開與重連／換圖等狀態。
3. **可重複測量**：先暖機，合理時至少 3～5 輪；統一記錄 P50/P95/P99/Max、變異、input-to-render、World Tick、Queue Age、bytes/sec、AOI fanout、CPU／alloc／GC、RSS／VRAM、載入／卸載／恢復與玩家畫面正確性。Microbenchmark 不可冒充真實 E2E。
4. **決策**：功能正確且資源／安全滿足需求者，選擇玩家體感和尾延遲較佳、總體成本可接受方案；差異不顯著則選簡單／好維護者；不准藉縮小 AOI、丟可靠封包、降低 Server Tick 或畫質換取假改善。
5. **交付**：在受影響的**既有文件**記錄候選與比較表、測試命令、數據、選擇與限制；必要 Probe 保留在現有 tools/tests。整合唯一正式實作、更新 TODO 並跑回歸後，才向下一個有依賴的任務前進。
6. **例外**：沒有實質替代方案的小修正只需基本回歸，不為滿足流程而浪費時間；無法公平測試時標記 NOT VERIFIED，不能宣稱勝出。

## Gate 0 — Converter（目前唯一正式素材產線；另有獨立測試 Probe）

- SPR/Data1 原始來源覆蓋、錯誤來源 fail-closed、ACT/SIF/DrawOrder/Origin/flip。
- Asset ID 唯一、不同 Enemy／Map scenery／Player parts **禁止跨素材共圖**。
- Map13 world／population／blocking 檔案相容。
- Height-Dense P0、VRAM import、external PCK 已整合且有獨立 Smoke；**完整來源覆蓋、Windows Release 和 macOS 實機仍未驗證**。
- 在**沒有 client/project.godot 和 server/*.csproj**的 clean-slate 基線仍可執行純 Converter 測試。

~~~powershell
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

## Gate 1 — Protocol + World

- C#、GDScript 共享已版本化 fixtures，非法封包 reject，Epoch／revisions 不混用；格式由實測選型，**不預設 JSON 或 MessagePack**，須驗證封包頭拆包／黏包與 byte-for-byte fixture。
- Packet trace 可重播，相同輸入得相同狀態 hash。
- Server Single Writer 與 DB/socket I/O 隔離；阻塞外部儲存不影響 Map tick。

### 初步 Wire / AOI 性能 Probe（非 Gate 1 驗收）

~~~powershell
dotnet run -c Release --project tools/tests/wire_aoi_probe/WireAoiProbe.csproj
& "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --headless --path . --script tools/tests/wire_aoi_probe/verify_binary.gd
~~~

檢查 RESULT ... PASS 與 WIRE_GODOT_GOLDEN_PASS；使用人工資料，已驗證 Binary Golden Fixture／Spatial Grid 與 brute-force 等價、Hysteresis 和密集 AOI 反例。**未**驗證正式 Server/WebSocket、多機、真實封包延遲、MessagePack/Protobuf、Map13 E2E；結果與限制記於 [Wire Contract](RUNTIME_PROTOCOL.md)。

## Gate 2 — 視覺、戰鬥與 AOI

- 地圖、Player/Equipment、Enemy、Scenery/Drop 共用 World Y-sort。
- 原版 ACT、Origin、Direction、flip、DrawOrder 的指定回歸測例 **0 筆不一致**；另列出未覆蓋的素材／動作，不把未測情境算 PASS。
- Attack、Hit、Death、Move 的 action priority 不混亂。
- 20/100 玩家與 300/1000 Enemy 真實 Spawn/Despawn、攻擊／死亡、換圖、掉落拾取。

## Gate 3 — 效能與生命週期

- 正常可選 FPS 不低於 30（測 30/45/60/90）；Unfocus／最小化／AFK 可減少或停止**畫面渲染**，但不能改變可靠 State、WorldReducer 或 Server Tick。
- Render P95/P99、CPU、RAM、Texture/VRAM、load latency 與 Model1 **同規格測試**。
- 30 次登入登出／換圖／AFK 與多開。記憶體沒有逐輪上升趨勢。
- 不同解析度／縮放與 Pixel/UV edge alias artifact 測試。

## Gate 4 — 發行

- Windows Release 可執行，external PCK 無需主 PCK 全量打包；Mac 上實測 ResourceLoader／壓縮 texture / .app 配置。
- 所有版本號／Checksum／manifest 一致，不能使用測試產物冒充正式 build。
- 所有未執行的 Gate 必須明確標示 NOT VERIFIED。

工作順序與完成狀態在根目錄 todo.txt，歷史測試與結果在 [research/INDEX](research/INDEX.md)。
## 新增的必要驗收案例

- **PATH001**：Map13／台地階梯／障礙角 A*，單擊地板、點擊怪物繞障、追逐目標移動、斷線 correction，Client/Server 都不能穿牆；Blocking.dat 必須以原始資料驗證。
- **HOLD001**：長按 1 秒後持續導航，左／右鍵停止、右鍵選單正常；UI 操作、移速變更、傳點、受擊、死亡、AFK、切圖都能取消並重新取得正確狀態。
- **FPS001**：完整同一組 packet trace，Render=0／30／60／90、正常／Unfocus／最小化／AFK，Client State hash、Command sequence、可靠事件和 Server Tick 不變。刻意阻塞 Renderer 不得阻塞 Network。
- **AFK001**：切 AFK 後按資產引用清單驗證非必要世界節點／Texture 釋放，量測與正常模式相比的 Process RSS、Texture/VRAM、CPU/GPU 與恢復延遲（**先定基線，不憑空宣稱 95% 可卸載**）；世界內容繪製趨近 0，網路仍完整；退出 AFK 復原 AOI、動作與 WorldReady，不得有舊圖或事件重播。
- **MULTI001**：1、2、4、8 個 Client 同機混合正常與 AFK，測各程序 Peak RSS、CPU／GPU、帶寬、queue age、輸入到畫面 P95/P99 和斷線／恢復。比較前須包含同一玩家/怪物密度與圖形品質。
- **LOAD001**：20／100 玩家、300／1000 Enemy、500／5000 Drop、動作與技能高峰，Server World Tick/AOI/Send Queue 分別 profiling；幀率與網路完整性分開評估。
- **RESTART001**：30 次登入、傳點換圖、AFK 進出，記錄 RSS/Texture/Node/Cache owners 趨勢；Mac 與 Windows Release 外部 PCK 各自驗證。
- **QUEUE001**：可靠事件／命令有界 queue 過載、慢接收者、網路抖動與重連；驗證無 World Tick 阻塞、沒有無限 RAM 成長，缺序能拒絕或 resync，冪等交易／拾取不重複結算。
- **AOI001**：不同解析度、相機 zoom、多人同時登入與可見邊界，雙向 Spawn/Despawn、快照基準與新舊 MapEpoch 交錯；Server 對客戶端 viewport 要有限幅與限頻。
- **AOI002**：同地圖 20/100 人、300/1000 怪、500/5000 掉落，比較每玩家 AOI 過濾後 vs 全圖廣播基準的 bytes/sec、updates/sec、fanout、CPU／alloc、queue age 與視野邊界抖動；未變動實體不重送，可靠傷害／拾取結果不遺失，並驗證多解析度及 AFK 重訂閱。
- **WIRE001**：固定包頭／length-prefix + 緊湊二進位、MessagePack、Protobuf 或其他合理方案使用相同 golden fixture 比較 Godot ↔ C# 的每訊息 bytes、serialize/parse P95/P99、CPU／alloc／GC、端序、拆包黏包、版本、異常輸入、安全／維護成本；僅證據充分後定版。
- **WIRE002**：使用正式 AOI／combat／drop trace，測 high-frequency snapshots 的 coalescing/batching、transport head-of-line／backpressure、慢 Client、連線與跨圖恢復；不能只用單一微基準宣稱正式網路更快。
- **GAME001**：第二階段裝備強化、NPC 商店／銀行／修理、技能、寵物／機甲、交易／玩家商店、PvP 顏色／GM 權限逐功能建立可重跑合約、多人及持久化測例。

任何腳本未實際執行時記錄 **NOT VERIFIED**，不可把先前 isolated Monster Benchmark 當成完成上述 Gate。

## 共同效能指標與公平比較

新 Client/Server 對 Recovered Model1 必須在相同原版素材、地圖、
可見實體數、解析度、縮放、VSync 和 GPU 設定下，以各自完整 Renderer
比較，不能用簡化 Sprite 畫面推估完整遊戲 FPS。

每次測試記錄：input-to-render latency、frame P50/P95/P99/Max、
超過當次目標幀預算的長幀數（30 FPS 約 33.3ms、60 FPS 約 16.7ms、90 FPS 約 11.1ms）、CPU/GPU 開銷、draw calls、Process Peak RSS、
Godot Texture/Video memory（**不等同實體 dedicated VRAM**）、
載入與卸載時間、Server World Tick P95/P99、封包佇列 age、
AOI fanout、可靠事件延遲、動畫方向/受擊/死亡/位移錯誤。

歷史隔離基準（**不是新版本 PASS**）：12 種 Enemy × 2000 隻，
Recovered P95 6.576ms / RSS 219.8MiB，舊 SGTest 壓縮 Sprite
P95 5.602ms / RSS 178.7MiB；Map13 視覺替身混合測試
Recovered P95 7.470ms、舊 SGTest P95 6.900ms。
完整方法留於 [歷史效能研究](research/ASSET_RENDERER_EVIDENCE.md)。

選型先排除畫面錯誤、權威事件遺失、過量記憶體；
其餘以玩家可感知的流暢度和穩定 P95/P99 為先，
**不得透過降低 Network／Client reducer／Server Tick 換取假 FPS**。
