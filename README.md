# SGTest — 後期《星夢 Online》素材研究優先

**目前階段：M0 / 原版素材來源、格式與載入邏輯研究。** 2026-11-13 按要求清除尚不需要的 Godot、金色 UI Gallery、.NET Server Demo 及相應 CI；保留 Git 歷史，尚未正式實作遊戲。

## 現階段第一目標
找出原版 Client 完整資源：確認原始檔案/封包位置和版本，從 Paradise.exe.lst 分析讀檔及解碼，建立可重現 decoder + Legacy Asset Viewer。保留角色每個方向與每個動作分離的原始語意，驗證攻擊/走路/受擊/死亡影格、偏移與圖層。**完成後**再討論 Godot Atlas、動畫系統和 MMORPG 正式架構。

## 工作入口
- [SPEC.md](SPEC.md)：完整長期需求與第 7 節素材研究 Gate
- [todo.txt](todo.txt)：跨 Session 唯一任務進度，AS001 起
- [docs/LEGACY_ASSET_PIPELINE.md](docs/LEGACY_ASSET_PIPELINE.md)：解析、匯出與驗收流程
- [research/source_manifest.json](research/source_manifest.json)：已知舊 repo 檔案索引（不是完整 Client）
- [AGENTS.md](AGENTS.md)：協作/安全/測試約束

## 原版資料調查
- 參考（唯讀）：https://github.com/kevin840307/TestGoGo/tree/feature/local-server-net8
- 舊 Client 反編譯：Old/ClientData/Paradise.exe.lst
- 本機已提供 `C:\SGO\Stargate Online IE HK2`，41,741 個實體檔；[HK2 盤點](docs/HK2_INVENTORY.md)與 [SPR 解碼試跑](docs/HK2_SPR_POC.md) 已記錄。`Data1.dat` 與動作／方向語意尚未完成解析，不宣稱全部素材可用。
- 唯讀盤點命令：
  `python tools/legacy_asset_inventory.py --root "C:\\OldGame" --output reports/legacy-assets.json --sha256`
- 執行前需將路徑改為實際遊戲資料夾；reports/ 被 gitignore。勿上傳原版未授權素材或解包產物。

## 後續方向
Godot 4 Client / .NET Server / PostgreSQL / 多人同步與低資源設計均保留於 SPEC，**不在素材確認前先行實作**。
