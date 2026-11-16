# SGTest Client 資源基礎

已新增 Godot 4.7.2 專案及單一 `assets_runtime/asset_store.gd`。目前可執行資源生命週期測試；地形與靜態景物／角色交錯 Renderer 已完成原生 GPU 切片；尚無遊戲 main scene、完整 World Renderer、網路／WorldState／登入／多人玩法。新程式沒有取回舊 prototype。

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
