# SGTest — Clean-slate 測試與完工標準

**狀態：測試規格。舊 Client/Server 的 PASS 不能沿用。**

## MVP001 — 雙地圖 Map15 + Map13、完整功能與多人壓測（來源資料部分完成，E2E 未完成）

**兩張真圖都需要正式 Godot 4.7.2 + .NET 8 C# Client/Server E2E**；原版／Recovered 只有證據作用，既有 Converter／Wire microbenchmark 不等於遊戲 PASS。

- **MVP-MAP15**：原版 Map15 地形／Scenery／Blocking／metadata、NPC／主城互動位置、正確原點／Y-sort／八方向素材、camera／解析度、唯一 Converter + external PCK，Godot 真圖顯示與 ResourceLoader／AssetStore Acquire/Release。
- **MVP-MAP13**：原版 Map13 地圖／Blocking 台地階梯、地物／人口／Enemy 出生位置／掉落資源、相機／渲染／碰撞、AssetStore、來源覆蓋與原版畫面一致性；與 Map15 使用同一地圖載入及資源路徑。
- **MVP-ACCOUNT/WALK**：帳號／多角色／創角選角／登入登出／重連、Server A*／路徑合法性、點地板／長按約 1 秒／點怪追逐與停止，正確八方向／移動／攻擊優先級及跨地圖 Spawn。
- **MVP-CROSSMAP**：**Map15 → Map13 → Map15 真實雙向傳點**；Server 權威驗證、MapEpoch／WorldReady、AOI 清除舊圖、在目標圖收到新 snapshot、角色位置、怪物／Drop 恢復、Texture/Node 釋放及新圖載入，至少兩真 Client 同時／錯開切圖、重登與斷線恢復，不可有舊實體殘影／異地封包污染。
- **MVP-AOI/MOTION**：至少兩個真 Client，Map15 主城與 Map13 的雙向 Spawn/Despawn、視野／解析度／camera、Player/Enemy/Pet/Mecha Facing、Move/Stop/Attack/Hit/Death/Skill、動作固定 ACT timing、不在視野外圍順移、可靠事件與有界佇列。
- **MVP-AI/COMBAT**：Map13 多種尺寸／行為 Enemy，可站立、獨立 NextDecisionTick、分散巡邏／追擊／脫戰，不集體同步；普攻、單體／範圍技能、Buff、MISS、Death／Respawn、掉落與拾取；Server 判定和多人觀看一致。
- **MVP-ITEM/ECONOMY**：Map15 NPC 商店／倉庫／銀行／修理、物品 UID／背包／強化／金錢、玩家交易／商店；Map13 地面 Drop/Pickup，含併發拾取、餘額／UID 原子性、重送／非法操作／斷線一致性。
- **MVP-SYSTEM**：Pet／Mecha 裝備卡片升級、技能效果與 CD、聊天／交易／玩家商店、PvP 原版地圖權限／名稱顏色／GM 指令，兩圖適用場景依原版規則測試，Server Authority 及錯誤／多人競態，好友暫緩。
- **MVP-WIRE/SECURITY**：ALT001 方案比較、Godot↔C# golden fixture、header/framing／replay／invalid input、Epoch/Sequence、bounded queue、慢 Client/backpressure、AFK／FPS 0/30/60 還能可靠同步。
- **MVP-RESOURCE/LOAD**：Map13 同圖逐步到 **20 連線玩家／300 Enemy／500 Drop**（至少兩個真 Godot Client 觀察動畫，Bot 明確標記）；Map15 多玩家互動、兩地圖同時活躍、往返切圖至少 10 次、1/2/4 開、AFK/Unfocus，稀疏／密集 AOI、AI 與 Protocol FPS 影響。量測 bytes/sec、Queue Age、World Tick/Frame P95/P99、Input latency、CPU、Process RSS、Texture/VRAM、GC、載入／釋放與可靠事件；不能將 Bot Server 負載等同所有真 Client 畫面 PASS。
- **MVP-GATE**：以上全部可重跑且正確性／功能完整度／資源 Gate PASS，Map15／Map13 雙向真實傳點必須完成，ALT001 選型有資料，文件／TODO 跟上後才准宣稱 MVP 完成。

**MVP 後的全圖／全種類／更大負載**：其他地圖與完整傳點網絡、所有原版 NPC/Enemy/裝備/技能、100 Player／1000 Enemy／5000 Drop、多開 8、Windows/Mac 完整發行；MVP 不能代替全量驗收。

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
- Map13 與 Map15 的原版 `world.json`／`terrain.png`／`tiles.bin`／`blocking.dat`／NPC 來源相容；**population v3、定向 Atlas／雙圖 PCK 資料載入已通過下列分項；權威 Server／真畫面仍未驗證**。
- **MAP-DATA001（2026-11-15 PASS）**：兩張原始地圖一鍵輸出；Map13 367 Object、Map15 1,848 Object（其中 15 筆缺少原始 Z，須標記）、兩圖 Tile Pixel Round-trip PASS；原版 Server Blocking.dat SHA 匹配，雙圖重跑及反向指定建置時 10/10 私有輸出檔案 SHA 不變。原始 Enemy source evidence 追加後現行 Python 共 57 項 PASS，真遊戲 E2E NOT VERIFIED。
- **MAP-ENEMY001（2026-11-15 source-only PASS）**：唯讀 Old Server 原版 Map13 78 Spawn／9 種、Map15 4 Spawn／1 種，與 Data1 解出的 10 種 ACT/SPR、5 動作、8 方向及 source SHA 一一對應。輸出 `population_evidence.json` 只保留原始值且 `stats_verified=false`，缺來源及未知 Enemy fail closed，雙圖反向重跑 SHA 2/2 一致；原 Builder 獨立編譯 10 頁／557 Frame PASS。**正式 catalog、完整 PCK、權威 Server AI／Combat／Spawn、Godot 真畫面 NOT VERIFIED**。
- Height-Dense P0、VRAM import、external PCK 已整合且有獨立 Smoke；**完整來源覆蓋已通過 ARCHIVE001；Windows Release 和 macOS 實機仍未驗證**。
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

- **MAP-DATA002**：ObjectList root name+XY+child_count／children name+XY；count包含children，world v3保留parent_index，z=0。Map15=1833根+15子／5父建築，Map13=367根。舊「15筆缺Z」解讀已撤回，DIRECT位址與遷移見ASSET_CONVERTER。測試負／溢出count、截斷、編碼、尾端垃圾、非BLG父物件與錯誤巢狀count；合成trim/origin bounds及來源順序／絕對XY不變。原版特殊建築遮擋與真Renderer **NOT VERIFIED**。
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

## ARCHIVE001 — 完整私有素材重新驗證（2026-11-15 PASS）

正式 Builder 重新產出 7,890 groups／75,588 SPR／282,968 Frame／7,871 頁，逐幀像素／origin 比較及 sidecar 613 ACT／1,165 SIF／9 DrawOrder 成功；後續唯一 CLI verify 來源／metadata／頁面 SHA、幀 identity、PNG 幾何與 sidecar replay PASS，0 error。測試命令、index SHA 與限制見 [Converter](ASSET_CONVERTER.md#archive-索引一致性mvp001-archive-integrity)。主 checkout 58 項 Python tests PASS。Archive PASS 不包含所有動畫語意／壓縮邊緣／PCK／Renderer／Client/Server；其餘 Gate 保持 NOT VERIFIED。

## CATALOG001 — 雙圖定向發布（2026-11-15 PASS，僅資料）

完整 Archive audit 為前置，原版 Map13／Map15 真實參照發布 242 個獨立 Atlas／242 頁／2,269 Frame，兩次反向指定 map ID 的 517 個檔案 SHA 完全一致。完整 62 Python tests PASS；新回歸涵蓋 full export、選取 scope、未知 ID／空集合 fail closed、未選 Asset 損毀也不可繞過完整 audit、managed stale removal／人工修改保留與確定性。命令及資料範圍見 [Converter](ASSET_CONVERTER.md#雙圖定向-catalogmvp001-catalog-scope)。**未驗證 NPC timing／全部動畫語意、真圖 ResourceLoader、PCK、Renderer 與 Client/Server E2E**。

## RUNTIME-CONTENT001 — 雙圖內容契約（2026-11-15 PASS，非遊戲 E2E）

唯一 CLI `build --runtime-only --extracted-source .cache/asset_converter/source --server-dat "C:\SGO\Recovered\Old\服務端\Dat" --map 13 --map 15` 真實來源輸出每圖 6 檔；Map13 3 NPC／78 Enemy Spawn，Map15 52 NPC／4 Enemy Spawn；反向 map 順序重跑 12 檔 SHA 全一致。全部 NPC Atlas 參照存在；population v3 拒絕 v2／猜測 stats、保留原始 Spawn 及 Flags 並明示 gameplay_ready=false。65 tests PASS；新增 synthetic map/NPC staging 失敗時不覆寫舊輸出、population 不採未核實 stats 的整合回歸。PCK ResourceLoader、NPC timing／互動、權威 World／A*／Combat、多人 E2E 仍 NOT VERIFIED。

## PCK001-REAL-MAPS — 空白 Godot 真圖資源（2026-11-15 PASS）

既有 smoke 工具支援 --assets client/assets --map 13 --map 15，沿用唯一 Builder，244/244 Texture ResourceLoader 尺寸 PASS（242 Atlas＋2 Terrain）；526 個封裝 raw file SHA 相符，242 BPTC／242 ASTC 變體匯入 PASS。原 synthetic Enemy＋lossless UI 2/2 回歸 PASS。實際空白 Godot verifier 收到錯誤的 Map13 Blocking expected SHA 時 exit 3，拒絕且無成功旗標。Python suite 67 PASS。可重跑命令／PCK SHA／本機報告見 ASSET_CONVERTER 的「真實雙圖 external PCK」。這不是實際遊戲渲染／VRAM 或 P0 邊緣驗證，也不是 Server Spawn／A*／Combat／多人 E2E／Release。

## ASSETSTORE001 — Client 資源 owner 基礎（2026-11-15 PASS，headless 範圍）

`python client/tests/run_asset_store.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --compare --rounds 3 --report reports/client_asset_store_engine_comparison.json`：正式 PCK、完整 palette、各候選 fresh process 三輪。選定 Texture job=1 三輪皆完成每輪 30 次 Map15 221／Map13 63 Asset 載入／釋放，無引擎 ERROR／RID leak；相同 trace 的 job=2 三輪失敗（不以腳本單獨 PASS 抵銷引擎錯誤）。共用 Texture instance／readonly、最後引用 weak ref、在途取消／過期 epoch、容量／RGBA budget／無效 metadata 均有驗證；每輪 final owner/assets/jobs/bytes=0。來源與 metrics 見 CLIENT_ARCHITECTURE，raw logs／sources SHA／實際 engine CPU／Peak RSS 由 runner 留在 reports/。Python Converter 67 tests PASS。

Store pump 僅是呈現資源交接；測試不代表 Network／WorldReducer FPS 解耦已通過。實際視窗／GPU／VRAM 釋放、memory slope、Renderer／動畫／AFK／多人 E2E 均 NOT VERIFIED。新 client/project.godot 僅供資源模組與測試，尚無 main scene。

## TERRAIN001 — 原生 GPU 地形（2026-11-16 PASS，地形範圍）

`python client/tests/run_terrain.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/terrain_decision.json`：三候選 fresh process 各三輪；同雙圖／PCK／Atlas、1280×720和1920×1080、90暖機＋240固定trace樣本、40遠距跳轉。16 viewport capture／輪候選，獨立CPU原始tile合成比對，144張全部0差異，native engine無error/leak；WH／u16／65535空格、重複／未知ID／尾端bytes／越界fixture PASS。共用Mesh builder優化後重測，依資源／準備／tail選正式 terrain.gd，兩Mesh僅在tests。詳CLIENT_ARCHITECTURE選型表。原生GPU圖、rawlog、source/PCK SHA均留reports/，不進Git。

Python Converter 67 tests PASS。這不代表地物／角色共同Y-sort／pivot、NPC timing／Animation、Server碰撞／A*、角色／多人畫面、AFK資源回基線或完整MVP002／MVP E2E通過；GPU engine counters 不直接當 OS VRAM 驗收。

### 2026-11-16 world v3階層回歸

Converter 68 tests PASS；雙圖runtime-only PASS；新PCK 242 BPTC／242 ASTC匯入PASS，SHA256 `5ae7608c8b756983efc186d2e162c9dc29bbb36c362bf03f97602e42cd1a74a6`。AssetStore 30次palette循環／3競態情境PASS；正式terrain draw同雙圖720p/1080p的16張GPU capture與CPU合成0像素差異PASS。首輪因TerrainData仍檢查v2而FAIL，修正及新增v2拒絕案例後重跑PASS；最終私有報告 `reports/scenery_hierarchy_terrain_final.json`。此輪未重做三候選效能比較，因只修正資料schema／階層且未改繪製策略，不產生新的效能勝出結論。CI、建築前緣遮擋、室內外／動畫與多人E2E仍NOT VERIFIED。

### 2026-11-16 景物metadata與alternate回歸

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 72 PASS。新增synthetic SIF舊16／17 byte、mode保存、BLG四strings／兩點集／front clamp、無效count／NUL／截斷、絕對runtime路徑、SIF與SPR衝突、definition共用及兩appearance覆蓋；runtime checker對缺alternate Atlas拒絕。來源probe確認兩圖所有definition source hash／manifest clip／Atlas page hash，四個map檔重跑byte完全一致，`reports/scenery_metadata_validation.json`。

同一CLI export-assets --map 13 --map 15完整Archive audit PASS，再runtime-only雙圖PASS（10.16秒）。runtime_pack PASS：254 BPTC／254 ASTC，PCK SHA見ASSET_CONVERTER。原命令 `run_asset_store.py --report reports/scenery_metadata_asset_store.json` 30次雙圖palette循環PASS（Map15=233含新alternate／Map13=63；final counts全0），`--script tests/test_asset_store_epochs.gd --report reports/scenery_metadata_epochs.json` 三競態PASS；`run_terrain.py --rounds 1 --mode draw --report reports/scenery_metadata_terrain.json` 16張GPU capture獨立CPU像素比對0差異、無engine error/leak。這次只回歸選定draw策略，未重做三候選benchmark。

首輪新增SIF測試曾錯把17-byte tail少1 byte當必定非法（實際可等同合法16-byte舊格式），FAIL後改為無歧義截斷並記錄hash完整性限制；不把錯誤fixture當產品錯誤或PASS。CI、原版建築前緣遮擋／同Y／室內外fade／動作時間、Server／多人E2E均NOT VERIFIED。

### 2026-11-16 World靜態景物／Actor前緣切片

- Python `python -m unittest discover -s tools/tests -p "test_*.py" -q`：75 PASS。原版對稱斜前緣2916點邊界chart、同Y穩定、三組root／子物件分離、互相要求交換的建築與1025輪上限；native同一chart、Actor插入與空白frame／重複root拒絕。world_order_validation.json確認雙圖順序可重算，runtime-only及新版PCK PASS。
- `python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/world_render_final.json`：集中Canvas與重用Sprite2D各fresh-process三輪，兩圖／720p與1080p、同camera trace及明確inside／frame快照。每map每round 240有效樣本／80跳轉；合計144 captures排序／座標／UV／texture identity及CPU合成PASS，110原始SPR frame origin與未壓縮Atlas crop逐byte一致。兩候選engine error/leak=0、Store final counts=0、Texture counter回同viewport baseline；source SHA前後未變。
- GPU oracle只允許nearest精確texel邊界1e-5的取樣上下界及2色階rounding，實際所有capture超界最大1、pixels_over_tolerance=0。**不能寫為原始圖與GPU RGB零差異**。錯誤命令順序與destination+1拒絕，整圖位移1px有789,194 pixel超界；私有world_render_negative_controls.json。初期型別／空白幀／未重現block-aligned底層尺寸與sampler tie的pilot FAIL，不列PASS。
- ALT001選Canvas：準備P99 Map15=1.223–1.460ms／Map13=0.541–0.626ms；Sprite=1.478–1.791／0.688–0.793。Frame P99與RSS無穩定全面勝負，選較少節點／較簡單Canvas，不宣稱所有硬體或動態怪物最佳。完整表、first-view／CPU／RSS／engine memory與限制見CLIENT_ARCHITECTURE。此輪network不適用，OS dedicated VRAM NOT VERIFIED。

尚存Gate：動畫／NPC timing與事件、出入口fade cadence／自身ghost、P0（含Godot非4倍數Atlas頁面的匯入取樣品質）、逐視野預取ALT、多開／Render0／資源斜率、A*／Server／多人E2E／Mac／Release仍NOT VERIFIED。MVP002-SCENERY-CONTRACT維持未完成。接手可用`--mode canvas --rounds 1`快速重跑正式路徑；全量三輪命令如上。

### 2026-11-16 景物預設時序／fade回歸

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 79 PASS。新增ACT字面action綁定／缺key／duplicate／index越界／非零起幀／長hold／raw0保存、scoped publish嵌入與缺ACT不猜FPS；現行唯一CLI export-assets完整Archive audit再PASS（372.769秒），8 normalized_scenery_act／10 Enemy／7 Player／229 source-clips-only，254 Asset不變。兩個HK2 binary重新SHA與既有timing audit完全相同；這是版本核對，並非啟動原版動態視覺驗證。

`python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --timing --report reports/scenery_time_verified.json`：正式Canvas與重用Sprite候選各1次Native PASS，84 captures／候選（雙圖／兩解析度／3相機／7組inside與elapsed狀態），總168，source SHA前後一致，127原始SPR幀嚴格origin與未壓縮Atlas crop驗證，命令順序／alpha／frame／UV／texture identity及GPU合成通過。仍採既有1e-5 texel邊界與2色階rounding規則；新增modulate只對source alpha取floor/ceil的1色階界以涵蓋GPU浮點alpha，實際max_outside_sampler_envelope=2、pixels_over_tolerance=0，不宣稱原圖RGB零差異。

Native fixture包含逐fixed-update counter對照2000步、30/60/120與AFK不取樣／巨幅時間跳轉、nonloop最後幀／completion、min1 update／fractional錯資料／不支援speed profile拒絕、per-placement phase與相同Atlas的BLG／static角色隔離。source資料證實61個多幀景物實例皆為static root。FISH01實測updates0/8→frame1，9→2，81/560→空白frame10，561→1。fade0/1/8/15/16、進／出target重設、children只隨原版分支顯示。錯alpha240改255立即被拒絕，reports/scenery_time_negative_controls.json。

兩候選退出Store counts全0、texture counter回13,373,731 bytes、engine error/leak=0；峰值Process RSS約222.35／221.60MiB（包含更多capture／readback，非steady-state改善證據）。`run_world.py --mode canvas --report reports/scenery_time_endpoints_verified.json` 原24端點回歸PASS；`run_asset_store.py --script tests/test_asset_store_epochs.gd --report reports/scenery_time_epochs.json` 三競態情境PASS。PCK SHA與來源profile見ASSET_CONVERTER；此輪不重新選Renderer、不將含PNG的wall time當Frame效能。CI NOT VERIFIED。

尚存：入口與inside事件phase／版本去重、自身ghost、visible-only原版phase政策、ACT token callback／dynamic speed、NPC／Player完整Animator、P0品質／非4倍數壓縮頁面、視野預取／AFK實機、多開／Server／多人E2E。時間取樣是純呈現函式，沒有把render呼叫當權威fixed tick。

### 2026-11-16 建築focus狀態／室內Actor分流

`python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --focus --report reports/building_focus_release.json`：Canvas與重用Sprite2D各1次Native PASS，兩圖／720p與1080p／3組進出路徑（含Map15 Court root486及child487）／7個fixed時點，總168 captures、105個source SPR幀。每張先用Python獨立u/v投影矩形重播focus trace，核對selected／counter／epoch／update，再比對所有commands及GPU composite。相機中心隨focus走訪、同畫面fixture_actor與outside_actor分走室內額外清單／main；不是兩個真Client或多人網路驗收。source SHA前後不變，engine error/leak=0，Store counts全0，Texture counter均回13,373,731 bytes；同既有sampler envelope＋alpha量化規則，max error=2，不稱原圖RGB零差異。PCK未改，沿用02d05d9a…6cbb9d4。

Native fixture：2501點half-open diamond邊界（61×41）、Manhattan1000/1001、兩組非空門檻（點座標刻意放遠證明未誤用）、同區不重啟counter、A→B、重疊last-wins、不可見exit/resume、Snapshot獨立、失敗configure atomic、clear tombstone／新epoch／duplicate／stale／gap／倒序／未知candidate拒絕；插值Actor±0.25保留連續幾何。build_focus拒絕舊epoch／錯expected update。`test_building_focus.gd` headless另做400次fixed update、30/60/120/0呈現採樣，完整trace一致（Render0仍有assertion用snapshot，不是實機AFK／network測試），source hash紀錄reports/building_focus_sampling.json。

負向controls（reports/building_focus_evidence.json）：錯selected／counter在state replay即拒絕；省略selected room的第二次child draw，35→34 commands而拒絕；Actor移到地板之前的錯誤順序也拒絕。兩候選Peak RSS約225.11／221.61MiB，含capture/readback，不宣稱perf提升或OS dedicated VRAM回基線。正式仍用既有Canvas選型。

`python -m unittest discover -s tools/tests -p "test_*.py" -q` 79 PASS；`run_world.py --mode canvas --report reports/building_focus_legacy.json` 原24端點回歸PASS；AssetStore epochs三情境PASS（reports/building_focus_epochs.json）。初輪focus測試舊手動frame1覆蓋ACT frame輸入，已排除並重跑；只以building_focus_release作完整新狀態／室內分流證據。CI NOT VERIFIED。

未驗證：HK2入口函式逐指令等版／實際舊Client動態畫面、真正相機controller／Player／傳點、odd viewport／zoom、正式Wire版本／Server碰撞與權限／多人E2E；目前重現的是可定位reference的呈現規則。P0／視野預取／完整World lifecycle與實機AFK等Gate不因此完成。
