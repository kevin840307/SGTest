# SGTest Client 資源基礎

**目前正式 Client Transport（2026-11-22）：** `network/client_transport.gd` 使用 TCP + `network/aes_cbc_frame.gd`（AES-256-CBC/PKCS7、固定 Key 與 IV），不使用 TLS；須以 `SGTEST_AES_KEY_HEX`／`SGTEST_AES_IV_HEX` 環境變數提供與 Server 相同的 32B/16B 配置。登入介面沒有憑證參數。

> 舊版 TLS 文件及測試記錄屬 2026-11-21 之前的歷史證據，不適用現行 Serve。固定 IV／共用 Key／無 MAC 不能抵抗密文竄改、可見重複模式；詳 `server/README.md` 的安全取捨。


Godot 4.7.2 單一 Client，沿用 AssetStore／External PCK 與既有地形景物 Renderer。正式 threaded TLS／WorldState 已接登入、選角與權威位置／方向；在線 WorldView 新增暫定 B 基礎六部件紙娃娃、logical view／ACT／鏡像、共同排序及自身相機跟隨。**平滑內插、正式角色外觀／裝備同步、戰鬥與主要多人玩法仍未完成**。新程式沒有取回舊 prototype。

## 驗證

先依 [Converter](../docs/ASSET_CONVERTER.md) 建置私有 external PCK，再從 repo 根目錄執行：

```powershell
python client/tests/run_asset_store.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"
# 同 PCK／同完整雙圖 palette trace，fresh process 各三輪比較
python client/tests/run_asset_store.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --compare --rounds 3 --report reports/client_asset_store_engine_comparison.json
```

啟動器檢查 Godot exit code、腳本報告與完整引擎錯誤日誌，任何 ERROR／RID leak 都會使該候選 FAIL。Windows 傳入 console exe 時，會使用同目錄實際 engine exe 量測 CPU／Process Peak RSS，避免誤量 console wrapper。每輪私有 JSON／log 留在 reports/；PCK 留在 .cache/，不進 Git。

## Store API

- `open_pack(path, optional_sha256)` 掛載 external PCK，僅讀 Catalog 索引。Godot 無安全 unmount，程序內拒絕改掛另一個 digest。
- `begin_scope(owner)` 取得新的 epoch；同 owner 舊 scope 自動結束。`acquire(owner, epoch, asset_id)` 對每組 owner／asset 為冪等，未知 ID、過期 epoch、容量超限會回 Error。
- Atlas 以正式 Catalog logical ID 查找；`maps/15`／`maps/13` 使用唯一 Converter 的既有 `maps/<id>/world.json` 契約，包含 terrain／tiles／Blocking／NPC／population 參照資料，不把其他 Sprite 複製成 map bundle。
- `pump(max_commits=2)` 僅供呈現端 main thread 呼叫。背景 CPU metadata worker 上限 2；Texture threaded job 預設 1。每次 pump 最多取出指定數量的完成 Texture 結果；實際 GPU 上傳排程由 Godot／driver 決定，GPU 時間硬預算仍需真 Renderer Gate。
- `get_asset(owner, epoch, asset_id)` 於 ready 時取得唯讀 manifest／extra／pages。已 ready Asset 的新 owner 可立即取得，`asset_ready` signal 表示 load transition；消費者應在 acquire 後先查 get_asset，再等待尚未 ready 的 signal。
- `release`／`end_scope` 移除自己的引用，不影響其他 owner；Scene/Renderer 必須先放下 Texture／資料引用。取消中的 ResourceLoader job 仍須持續 pump 排空；取消結果不送給舊 epoch。沒有額外 warm Texture cache。
- 容量預設 256 active asset／scope；RGBA 保守估算預算 256 MiB（頁面 w×h×4），不等於 VRAM。不得由此 pump 驅動 Network、reducer 或 World Tick。

2026-11-15 完整 palette 換圖測試：每輪 30 次，Map15 221／Map13 63 assets；一 Texture job 三輪全通過、無引擎 error／RID leak。共用 Texture instance、最後一個 consumer 釋放後 weak ref 消失、epoch 取消、容量／byte budget、錯 metadata 均有回歸。兩並行 job 在 Godot headless dummy backend 三輪皆失敗，預設採一個。詳 [Client 架構](../docs/CLIENT_ARCHITECTURE.md)。真 Renderer／GPU 記憶體基線／AFK／多人與跨圖 E2E 仍 NOT VERIFIED。

快速 epoch／callback 競態回歸：`python client/tests/run_asset_store.py "Godot console exe" --script tests/test_asset_store_epochs.gd --report reports/client_asset_store_epochs.json`。測同地圖在 CPU 與 Texture 工作階段重取、ready callback 釋放自己與其他 owner，三種情境 PASS。

## 地形 Renderer（2026-11-16）

正式唯一路徑 `rendering/terrain.gd`：可視 tile 區域 Canvas 繪製，搭配 `terrain_data.gd` 無損讀取原始 WH/u16 tiles.bin／Atlas 對應。從 AssetStore 的 maps/<id> ready data 產生 TerrainData，configure 新 Node，依相機的世界可視 Rect2 呼叫 set_view_rect；可視 tile 矩形沒變就重用 Canvas commands。相機左上角採原始 world pixel，不改地圖／角色座標。離圖放下 renderer／TerrainData 引用後再結束 Store scope。

```powershell
# 單一正式實作的 Native OpenGL GPU／像素回歸
python client/tests/run_terrain.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 1 --mode draw --report reports/terrain_validation.json
# 同 Atlas／地圖／相機 trace，三種候選各三輪；會開啟自動結束的縮圖測試視窗
python client/tests/run_terrain.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/terrain_comparison.json
```

兩種 Mesh 策略只在 tests/，共用相同 Mesh builder；不是平行 Client。以 RTX 3050／OpenGL3、1280×720 與 1920×1080，三候選各三輪都通過 16 張 viewport 擷取／輪與獨立 CPU 合成圖精確像素比對（共144張，重複相同測例）；正式區域繪製的資源／首次呈現／跳轉 tail 較均衡。完整數據、失敗的測試分類修正與限制見 CLIENT_ARCHITECTURE。截圖與 reports 留在本機。這只呈現地形；地物／角色共同 Y-sort、pivot、NPC timing、遊戲同步與完整 client main scene 仍待完成。

2026-11-16 資料修正：world v3保留原版建築父子關係（Map15=1833根+15子），撤回先前「15筆缺Z」解讀；根記錄XY後為child_count，世界z=0。舊v2須由唯一Converter重建runtime/PCK，AssetStore拒絕v2。原版建築前緣排序／室內外顯示尚未實作，完整景物層與MVP仍未完成；見ASSET_CONVERTER的DIRECT證據及TEST_PLAN。

2026-11-16 景物資料進度：共用scenery v1定義已保留BLG兩appearance、SIF模式／幾何與建築前緣，補入12個alternate素材；正式Catalog現在254獨立Asset。72 tests、雙圖PCK／30次AssetStore循環／Terrain 16張像素回歸PASS。景物遮擋Renderer與切換時間仍未完成；詳ASSET_CONVERTER／CLIENT_ARCHITECTURE。

## 靜態景物與角色交錯驗證

```powershell
python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --rounds 3 --report reports/world_render_final.json
# 只跑正式Canvas的回歸
python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --mode canvas --report reports/world_render_canvas.json
```

正式`rendering/world_order.gd`處理原版front交換／Actor插入；`world_draw_list.gd`由唯讀world、同一AssetStore的資產與明確frame／inside快照建立可見命令；`world_canvas.gd`只繪製，不載入資產或推進時鐘。先clear renderer／draw list再結束AssetStore scope。固定景物排序只在Converter離線計算，Client不每幀bubble-sort整圖。Sprite2D比較候選只在tests/，重用節點且與Canvas消耗相同命令，不是第二套正式Renderer。

3輪／兩圖／兩解析度／室內外及frame0/1，144 captures的排序／座標／UV／texture identity與獨立CPU合成通過；110個來源SPR幀的origin及未壓縮Atlas crop逐byte一致。GPU壓縮影像取樣允許明確的1e-5 texel邊界不確定性與2色階rounding，實際超界最大1；不能稱為原圖與壓縮GPU完全0差異。錯排序／位置+1／整圖位移1px的負向控制均拒絕。動畫時序／建築fade與入口、自身遮擋ghost、zoom／多開／AFK、多人Server仍NOT VERIFIED。

## 景物時序與fade

```powershell
python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --timing --report reports/scenery_time_verified.json
```

`world_draw_list.frames_at_update(fixed_now, started_at)`按8個原版ACT的raw時間回傳景物幀，再傳給build的frames參數；completed fixed updates由owner提供，不累加Render delta。build的transitions參數可提供`{placement_index:{inside:true,updates:8}}`，按原版primary／children／alternate順序及255-clamped alpha繪製中間狀態。沒有ACT的素材不會自訂FPS；BLG不因共享同Atlas而誤啟動static action。這些是呈現函式，不代表入口偵測、WorldState／網路或完整動畫事件已完成。

## 建築焦點走訪

```powershell
python client/tests/run_world.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --focus --report reports/building_focus_release.json
& "C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe" --headless --path client --script res://tests/test_building_focus.gd -- "C:\SGO\SRC\SGTest\reports\building_focus_sampling_result.json"
```

Visibility owner按連續fixed update及同epoch呼叫advance；候選由draw_list.visible_main_indices(view)取得，snapshot交給build_focus並檢查owner期望update。此為純視覺：相機focus決定屋頂內外，沒有改Server Blocking／碰撞。室內Actor與家具在selected建築後排序，室外Actor留一般main list；角色範例仍是fixture NPC，不是可玩或已連線角色。source與邊界／選擇規則見CLIENT_ARCHITECTURE。

## Atlas頁面座標更新

2026-11-16 Publisher新增`height_dense_p0_block4_v1`：Frame間仍P0，只補頁右／下透明留白至4倍數，保持來源texel與rect／origin／trim。需依ASSET_CONVERTER的block4命令重新export＋pack，才能配合新manifest。PCK smoke同時驗證logical／physical extent；World GPU驗證精度不放寬。細節codec畫質與Mac仍待驗證。


## 雙圖檢視入口（2026-11-16；MVP002-CAMERA-ENTRY）

以下為離線檢視切片的歷史範圍；目前main另有登入／選角／登出介面，見文末MVP003-LOGIN-UI。

已新增正式的最小 Godot `client/app/main.tscn`（由 `project.godot` 的 main_scene 指向），**僅供原版 Map15／Map13 地圖及景物檢視**。它透過唯一 AssetStore 與已選定的 Terrain/World Canvas 載入私有 `assets.pck`；Map15／Map13 按鈕是本地 Viewer 切換，不是權威 NPC 傳點。方向鍵平移、中鍵拖曳、Home 置中、視窗 resize；UI 可攔截操作，失焦清除按鍵，切圖使用 owner epoch 丟棄過時載入。沒有 Player 控制、帳號、Socket、權威 Blocking、遊戲 AOI、AFK 或正式操作功能。

```powershell
# 原始素材只在私人本機保留：先依 ASSET_CONVERTER 重建最新版 PCK
& "C:\SGO\Godot\Godot_v4.7.2-stable_win64.exe" --path client
# 如需指定自訂私人封裝：加上 -- --pack "C:\private\assets.pck"

# 真 Godot OpenGL 畫面、12 個 GPU 擷取、38 個原始 SPR 來源影格、獨立 CPU raster 驗證
python client/tests/run_world_view.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --report reports/world_view_native.json
```

2026-11-16 RTX3050／Godot 4.7.2 原生測試 PASS：Map13/15 × 1280×720、1001×701 × 多個 focus 共12張；獨立 `run_world.validate_capture` 檢查來源 sprite 的 origin／trim、命令排序／UV／像素 sampler envelope，12/12 沒有超容差像素（max outside envelope=1；容許的GPU取樣/rounding 不代表 RGB 0 差異）；38 個獨立 SPR frame 檢查。快速切換／epoch、鍵盤／中鍵／UI消費事件／失焦／camera奇數尺寸及 30/60/120 Hz 相同 pan 距離測例 PASS。AssetStore assets、scopes、jobs、rgba_bytes 清除回0；本測試 PeakRSS 226,193,408 bytes、CPU5.219秒／wall5.75秒（測試程式／非遊戲FPS/多人測試）。

原有 `run_world.py --mode canvas --rounds 1` 亦回歸 PASS，現行 Python suite 81/81 PASS。報告在 `reports/`，原圖、PCK 與 GPU 截圖均不得推送。下階段仍需玩家 actor/Paper-doll、權威 Server A*、網路收包與 Render FPS 解耦、多開及 AFK/Unfocus；Viewer PASS 不能當作可玩 Client 或正式 Release。

## 2026-11-16 threaded TLS login transport

`network/wire_codec.gd` now covers the Client-facing V1 login/character frames in addition to MoveTarget/MoveReceipt. `network/client_transport.gd` owns TCP/TLS on a dedicated Godot `Thread`; networking is therefore not polled by a render `_process()` callback. The worker has bounded outbound/event/receive memory and uses partial send/read calls rather than a blocking full-frame send. Production startup uses certificate validation (`TLSOptions.client()`); the explicitly unsafe helper is debug-build-only.

A real local E2E is available:

```powershell
python client/tests/run_tls_login.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"
```

The runner starts `SGTest.Server.LoginFixture`, loads its public test certificate into Godot as the trusted chain, and verifies TLS Auth -> CharacterList -> CharacterChoose -> Move. Both sides must print PASS. This is a transport/login gate only: the application scene/login UI, non-render WorldState reducer, reconnect/WorldReady and render=0/30/60/90 packet-order tests are still NOT VERIFIED.

## 2026-11-16 non-render WorldState / ClientSession

`network/world_state.gd` is a presentation-free protocol reducer. `network/client_session.gd` owns it on a dedicated logic thread and drains `ClientTransport` frames independently of render callbacks. Application/render code only submits bounded intents and reads copied snapshots. Current transitions cover Auth, CharacterList, CharacterChoose and MoveReceipt with MapEpoch/sequence fail-closed rules.

`test_client_fps_independence.gd` runs the same Auth -> Choose -> Move script at simulated render polling 0/30/60/90 and requires identical final state and exact outgoing frame order. The zero-render case performs no snapshot polling during the work interval, proving this current logic path is not advanced by render FPS. The real TLS runner now uses `ClientSession` too.

GitHub `Client core checks` downloads the official Godot 4.7.2 Linux standard binary, verifies its published SHA256, caches it, and runs codec/transport/WorldState/FPS plus the Godot<->C# secure TLS E2E. Real WorldReady/AOI/snapshot interpolation/reconnect and the app UI integration are still later gates.

## 2026-11-16 TLS MoveReceipt round-trip

`client/tests/run_tls_login.py` now requires the complete **server-to-client receipt return path**, not merely that the Server accepted Move. The C# fixture enqueues its authoritative MoveReceipt on the bound Gateway connection; the TLS writer sends it, and Godot `ClientSession`/`WorldState` must observe the accepted sequence and ServerTick before the test can PASS. GitHub CI passed this one-client secure loopback test; production reliable result routing/AOI/WorldReady, login UI and multi-client graphics remain pending.

## 2026-11-16 WorldReady real TLS E2E

`client/tests/run_tls_login.py` now requires the real Godot `ClientSession` to receive and reduce **WorldReady and MoveReceipt** on a certificate-validated TLS connection. The C# fixture starts the production `WorldAoiPump` path, routes the trusted lifecycle event through the existing Gateway reliable FIFO and independently logs `LOGIN_FIXTURE_WORLD_READY_ROUTED`. GitHub Client core E2E PASS. This confirms one Godot headless client’s protocol/WorldState path, **not** actual two-client drawing/animation, Enemy/Drop visibility, WorldReady ACK or reconnection resync.

## 2026-11-16 Cross-map two-client TLS regression

The existing `client/tests/run_tls_login.py` supports `--crossmap --timeout 70`, reusing the **single** `.NET 8 Server.LoginFixture` and two genuine concurrent Godot 4.7.2 `ClientSession` processes. The fixture uses only a *test-only trusted server control* for Map15→Map13→Map15 after actual Godot observation barriers; it does not give a Client an arbitrary teleport RPC. It asserts new `WorldReady` and monotonic MapEpoch, clear old entities, Map13 MoveReceipt with reset sequence, Map15 spectator Despawn and fresh-generation Spawn, and finally no ghost World actors. GitHub Client workflow **43880543810 PASS**; Server **43880543863 PASS 69/69**. Actual original portal/NPC validation, private original two-map rendering and Windows performance are NOT VERIFIED.


## 2026-11-16 audited NPC click network path

Once WorldReady has arrived, gameplay UI may call `ClientSession.npc_interact(spawn_id)`; do not send destinations or use a test-only transfer RPC. Successful/denied results appear in WorldState `last_npc_result_status`, `last_npc_result_map_epoch`, `pending_npc_sequence` and `waiting_for_new_map`. Server validates the actual NPC from private runtime Converter `npcs.json`. The headless CI gate exercises two real Godot processes and the test-only audited metadata via `client/tests/run_tls_login.py --portal --timeout 70`. This does not itself create a clickable NPC UI, load original private PCK, or confirm legacy NPC distance; those remain NOT VERIFIED.

## MVP003-LOGIN-UI：正式入口接現行Session

main的玩家登入畫面**只顯示帳號、密碼及登入操作，不提供 IP／Port 編輯欄位**。連線端點屬部署設定，仍由啟動參數 `--host`、`--port` 或內部預設值提供，不由 UI 風格決定或覆寫。TLS 憑證預設由系統信任鏈驗證；私人測試 CA 可透過 `-- --host 127.0.0.1 --port 7777 --tls-name localhost --trusted-cert C:/private/server-cert.pem --pack C:/private/assets.pck` 指定。帳號／密碼不透過命令列傳入，密碼提交後清空。7777須配合實際listener；現在可依server README啟動TLS `serve`，需預先建置可信帳號／角色DB，公開創角尚未接通。

ClientSession的兩個worker負責收包與Reducer；UI每50ms只讀已發布快照。收到WorldReady後沿唯一AssetStore載入對應map，連線期間本地Map按鈕不可切圖；地圖就緒時左鍵提交Server Move，尚未接玩家紙娃娃／平滑移動顯示。創角與正式NPC互動UI仍未實作。沒有私有素材時能完成登入，但不會假裝地圖已顯示。

```powershell
python client/tests/run_tls_login.py "C:/SGO/Godot/Godot_v4.7.2-stable_win64.exe" --app-ui --timeout 45
# 可選原生UI截圖（保留私人本機，非headless）
python client/tests/run_tls_login.py "C:/SGO/Godot/Godot_v4.7.2-stable_win64.exe" --app-ui --ui-screenshot reports/session_ui_login.png --timeout 45
```

Windows真TLS UI Gate與雙圖既有12張GPU／38原始幀回歸PASS。地形PCK必須含最新`int16_plus_one_wrap`契約；舊PCK會被拒絕。`run_world_view.py --pack PATH --assets MATCHING_PUBLISHED_ASSETS`可指定對應的只讀Oracle，避免改寫其他Session輸出。這些分別驗證UI網路路徑與Renderer回歸，尚非兩個真Client在原版地圖上的連線視覺E2E。

## 在線基礎玩家呈現（2026-11-17）

使用對應最新 Converter 契約的私有 PCK（包含 `PLAYER_PART_ASSETS`），啟動既有 main；在登入介面輸入帳密，選角後會載入 Server WorldReady 指定地圖並跟隨自身。範例：

```powershell
& "<Godot4.7.2>" --path client -- --pack "<assets.pck>" --host 127.0.0.1 --port 7777 --tls-name localhost --trusted-cert "<trusted.pem>"
```

未提供 trusted-cert 時使用系統正常 CA 驗證；不會停用憑證驗證。在線左鍵送 Server Move，方向鍵／中鍵／Home 的自由檢視僅限離線；登出、跨圖先清除畫面與角色引用，再結束原 scope。現有所有角色暫用 B 基礎六部件（含 hair00），尚不是帳號角色的正式外觀／裝備。資料更新與動畫只影響呈現，WorldState／收包仍在各自 worker；此版位置直接呈現權威更新，平滑內插為下一 Gate。

`test_player_presentation.gd` 是公開 synthetic headless Gate。私有 GPU/source Gate 沿用現有 runner：

```powershell
python client/tests/run_world_view.py "<Godot4.7.2-console>" --pack "<assets.pck>" --assets "<matching client/assets>" --players --report reports/player_view.json
```

此測試為兩個角色的呈現 fixture，不能稱為兩個連線 Client 或完整玩法驗收。
