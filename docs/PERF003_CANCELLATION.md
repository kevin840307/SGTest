# PERF003：取消佇列與部分載入回收

2026-11-15。延續919a7c8的正式release記憶體驗收，補充串行30次載卸沒有覆蓋的取消／失敗邊界。

## 重現與修正

| 實際重現 | Before | After |
|---|---:|---:|
| worker忙碌時連續1000次acquire／release | entry=0但queued jobs=1000 | queued jobs=0 |
| 兩頁fixture第一頁上傳後取消 | entries=0但resident帳目殘留4096bytes | resident=0、Texture弱引用存活=0 |
| 第一頁上傳、第二頁不存在，失敗後釋放 | entries=0但resident帳目殘留4096bytes | 失敗立即resident=0；釋放後仍0、Texture存活=0 |

Fixture是自製32×32白色圖片，不依賴原版。Before原始JSON是`reports/perf003_before.json`；After headless與native是`perf003_after.json`、`perf003_after_native.json`。Native After另修復同一manifest後重新載入實際圖片，兩種路徑都能成功重試／清回0；不可由此推論Before一定有實際Texture洩漏，Before直接證明的是帳目錯誤及queue殘留。

實作維持一個decoder、主執行緒GPU提交及原本network／logic排程。mutex保護active token集合、待解碼queue與ready queue；取消時一起移除。publish在相同mutex內確認token仍有效，拒絕晚到結果；job／page開始也確認token，不繼續解碼舊map剩餘頁。已開始的單頁PNG decode不做不安全中斷。成功最後一頁及錯誤處理都結束token。所有淘汰／取消／錯誤共用page引用及resident帳目清理，沒有以`max(0,...)`掩蓋負帳目。

零byte的metadata／error事件同樣受128-item上限約束，避免只限制Image bytes。單張超出ready budget的page只在queue空時進入；worker另可持有一個in-flight page，不能把ready budget稱為整個程序RAM上限。

## 回歸與實測

`test_runtime_asset_cancellation.gd`連跑三次PASS：128 live jobs時拒絕新增、取消1000 generations沒有殘留、失效token不能publish、priority順序正確、128個零byte事件也有backpressure、等待Image byte budget時取消不阻塞下一owner、只提交新generation三頁、close在fixture中<500ms及同Store restart。這個500ms結果只屬自製fixture，未承諾任意超大PNG可被即時中斷。

實際Windows release重建並核對source／raw asset SHA後，1440×900／20連線（1視窗+19headless）／300怪／1000掉落／40秒：59.997FPS、OS peak RSS184.88MiB、frame P95/P99/max18.884/19.573/21.288ms，Client CPU中位數27.9%一核心。對前一正式版本P95/P99增幅約1.8%／1.9%，低於10%門檻；不能宣稱單次差異是CPU變快或變慢。

30次map async載卸（另2次暖機）：load P95 131.832ms、GPU submit P95/P99/max1.433/1.775/1.885ms、frame P95/P99/max16.774/16.908/17.202ms，handles／texture／Store／RSS／static recovery全部PASS；卸載後最後10次與最初10次RSS中位數差0.195MiB。原始JSON與摘要位於`reports/perf003_production/`。既有monster/scenery/paper-doll/shared-store回歸及native Image／metadata lifetime probe PASS。

另提供`tools/benchmark_asset_worker_idle.py`，以同一headless程序、10FPS、無world／network／圖片，分開測0／1／8個idle decoder，包含Windows console launcher及真正engine child，使用OS CPU累積時間。短窗口小值可能量化為0，不能解讀零CPU；此實驗不是8個遊戲Client，也不證明多開CPU改善。原始記錄`reports/perf003_idle.json`；worker目前仍使用既有idle polling，本次沒有宣稱idle CPU最佳化。

## 重跑

```powershell
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script tests/test_runtime_asset_cancellation.gd
& 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --path client --script tests/probe_asset_cancel_queue.gd -- --assert-bounded --report=C:/SGO/SRC/SGTest/reports/perf003_after_native.json
python tools/benchmark_asset_worker_idle.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/build_client.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe'
python tools/benchmark_runtime_baseline.py .cache/client-release/SGTest.exe --case multiplayer --exported-client --output reports/perf003_production
python tools/benchmark_runtime_baseline.py 'C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe' --case lifecycle --loader async --output reports/perf003_production
```

本次沒有修改同步頻率或重跑全部RUNTIME206矩陣；physical driver VRAM仍NOT VERIFIED。完整Recovered地圖傳點／NPC／裝備等功能與AS005原版人工Golden／AS008公開媒體權利仍未完成。
