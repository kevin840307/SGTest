# Godot 4 Client

Godot 專案入口是本資料夾的 `project.godot`，不是儲存庫根目錄。先啟動 `server/` 的 C#／.NET 8 服務，再執行：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path client
```

客戶端場景、GDScript、shader、測試及視覺工具都在此目錄。`assets/generated/` 為本機原版轉換產物，不進 Git；從根目錄執行 `tools/rebuild_assets.ps1` 重建。

`scripts/network_client.gd` 的 worker 獨占 WebSocketPeer；`message_pack.gd` 負責協定編解碼；`client_replica.gd` 處理獨立實體狀態與歷史；`local_prediction.gd` 僅控制本地視覺預測。人物、怪物各自取樣動作起始時間，不等整批快照才一起移動。

預設 1280×720，視窗最大寬 1440；UI 可選尺寸、拖曳調整，F2 切換金色／現代樣式。Client 不包含伺服器模擬或資料庫。
