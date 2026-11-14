# PERF002：Runtime 記憶體與實際 Windows release

2026-11-15。工作負載固定為 Map13、1440×900、Render 60 FPS、20連線（1個真視窗＋19個headless）、300怪／1000掉落、40秒，暖機2秒、frame取樣35秒。RTX3050／Compatibility OpenGL。不是20個有畫面的Client；30～90 FPS與背景隔離完整矩陣沿用既有RUNTIME206，本次沒有重測或改動其頻率。

## 引用與診斷的實際修正

修改前弱引用probe證實：載入後worker仍保留最後1個CPU Image；owner釋放／purge後仍有1份metadata及1個Image，直到worker close才消失。原因是永久解碼迴圈的函式frame保留區域變數。改用每job、每page會返回的函式後，GPU提交完成的CPU Image為0，owner釋放後metadata／Image均為0，worker不必退出。證據：`reports/perf002_lifetime_before.json`、`reports/perf002_lifetime_after.json`。

GPU idle poll原先每幀追加telemetry，長時間掛機會持續增長，diagnostics也複製該陣列。現在idle不記樣本，decode／submit／commit各保留最近2048筆及累積計數；一般diagnostics只讀summary。probe覆蓋10000次idle、ring覆寫／順序／累計與summary省略陣列；基準工具若ring不足以涵蓋區間會明確失敗，不以截斷資料宣稱完整。

## Atlas 與打包實測

下表RAM使用Windows `peak_wset`，含啟動尖峰；取樣RSS另存原始報告，不再以低頻取樣低於200MiB就宣稱峰值通過。程序樹peak加總是保守上限，各程序峰值未必同時發生。

| 執行方式／Atlas | OS峰值 MiB | Frame P95／P99 ms | Draw Calls P95 |
|---|---:|---:|---:|
| 開發引擎2048，修正引用後三次 | 221.80～224.00（FAIL） | 19.141～19.473／20.468～21.127 | 250～291 |
| release2048，早期打包對照 | 217.08（FAIL） | 18.968／19.772 | 271 |
| release2048，loose對照 | 198.27 | 18.607／19.031 | 269 |
| release2048，同版packed對照 | 199.80 | 18.484／18.874 | 248 |
| release1024，loose對照 | 185.43 | 18.509／19.132 | 285 |
| release1024，packed第1次 | 189.11 | 18.535／19.155 | 298 |
| release1024，packed第2次 | 184.95 | 18.602／19.294 | 307 |
| release1024，packed第3次 | 185.55 | 18.459／18.863 | 312 |

原始JSON位於`reports/perf002_after*`、`reports/perf002_release*`。波動本身需保留，不能只挑199.80而刪掉217.08。1024在packed／loose均較低，三次packed均低於200MiB且frame通過25／40ms及10%退化門檻。Draw Calls較多，不能宣稱所有指標改善；實際軌跡／可見數受排程影響，Draw Calls不是固定畫面逐像素A/B。相同seed、實體量、素材語意及解析度沒有下降。

Map bundle由2頁變5頁：18,569,428→18,572,476 RGBA bytes；session由1頁變2頁：4,390,912→4,428,064 bytes。Engine texture約46.04→46.08MB，增加很小。降低的是觀測到的程序RAM；較小page的decode／driver工作空間是推論，未定位分配器，不聲稱量到了實體VRAM。release的Godot static memory counter為0（不提供該診斷），不能解讀為零記憶體。開發引擎仍超200MiB，與正式release分開記錄。

決策：僅Runtime bundle優先512／1024候選，在5% RGBA容忍範圍內減少頁數；若原SPR atomic block放不下，使用最小有效較大尺寸2048或4096。512可容納且成本合適仍可選；不強制全部1024、不同方向不要求同頁、同原SPR不拆頁。離線archive維持既有策略。map／session／on-demand仍是唯一公開生命週期分類。

1024候選30次map async載／卸（另2次暖機）PASS：frame P95 16.826／P99 16.923ms，texture handles、engine texture、Store resident與RSS回收通過。原始記錄見`reports/perf002_1024_lifecycle/lifecycle.json`。正式預設由原版來源重建後，15個raw檔通過打包SHA檢查：release峰值187.09MiB、60.003FPS、frame P95/P99 18.549/19.200ms、Draw Calls P95 244、Client CPU中位數24.6%一核心。正式map async30次load P95 130.829ms、decode P99 22.048ms、GPU submit P95/P99/max 1.506/1.591/1.649ms、frame P95/P99/max 16.822/16.971/17.083ms；回收後RSS最後10次對最初10次中位數增加0.707MiB、全部回收gate PASS，見`reports/perf002_production/summary.json`。這是map資源串行載卸測試，非完整登入登出或任意切圖取消風暴；GPU sync wait未強制量測，仍NOT VERIFIED。

## 可重跑命令

```powershell
python tools/asset_converter/cli.py build 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --runtime-only
python tools/build_client.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/benchmark_runtime_baseline.py .cache/client-release/SGTest.exe --case multiplayer --exported-client --output reports/perf002_production
python tools/benchmark_runtime_baseline.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --case lifecycle --loader async --output reports/perf002_production
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --path client --script tests/probe_asset_store_lifetime.gd -- --assert-released --report=C:/SGO/SRC/SGTest/reports/perf002_lifetime_after.json
```

`build_client.py`從任何目錄執行，使用repo內preset及templates，實際建立EXE＋PCK後啟動release檢查raw PNG／JSON／BIN的SHA及完整runtime source inventory。基準測量前重做檢查，拒絕舊PCK。raw PNG維持原始bytes，由Runtime有界decode／GPU提交，不進Godot貼圖importer。可用`--assets <候選根目錄> --output <本機exe>`建立相同storage的候選。build artifacts留.cache；原版／衍生媒體與PCK不上Git。

首次需官方Godot4.7.2 templates：從[官方release](https://github.com/godotengine/godot/releases/tag/4.7.2-stable)下載`Godot_v4.7.2-stable_export_templates.tpz`及SHA512-SUMS，核對SHA512為`ca4d71c4d7b81dfc15d1a98baa07534aa95b03fdda78a0075b06672e1648d2e5f40980c9adc28d23e1b92e732ee7bf3461997aa804af74ec2fcd7a93ccb84079`，只將`templates/windows_release_x86_64.exe`與`templates/windows_debug_x86_64.exe`解壓至`.cache/godot-templates/`。helper另驗證兩個template SHA256，版本不符會停止；不改全機安裝。

尚存範圍：完整Recovered功能、原版Golden視覺與公開媒體權利沒有因此完成；physical driver VRAM仍NOT VERIFIED。此輪串行30次不能推論任意取消情境；後續獨立 [PERF003](PERF003_CANCELLATION.md) 已重現並修正取消queue／部分載入帳目問題，另有自製fixture及native回歸。
