# SGTest Client 資源基礎

已新增 Godot 4.7.2 專案及單一 `assets_runtime/asset_store.gd`。目前可執行資源生命週期測試；尚無遊戲 main scene、Renderer、網路／WorldState／登入／多人玩法。新程式沒有取回舊 prototype。

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
