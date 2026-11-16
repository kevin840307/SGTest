# SGTest Server — .NET 8 重建（目前僅 World 碰撞／導航基礎）

新版 Server 是**從零重建**，不還原舊 SGO 原型。現在已有 `src/SGTest.Server` 的 .NET 8 Class Library，包含 Map13／Map15 權威碰撞資料驗證與有界 A* 路徑查詢。**還沒有正式 Server executable、Socket/Wire、World Tick、AOI、帳號、資料庫、Gameplay 或連線人物移動**；此程式不可宣稱已啟動 MMORPG Server。

## 正式內容契約

`World/BlockingGrid.cs` 載入唯一 Converter 的 `maps/<map_id>/world.json` (v3) + 原始 `blocking.dat`（8 bytes 小端寬高 + 寬×高 raw bytes）。map_id、collision_cell=16、world pixel size、檔案長度和 world.json 中的 `collision_sha256` 必須完全一致；遇到舊 schema／錯地圖／缺少原始檔／被修改或越界時直接失敗。Server 不讀圖片、Atlas，也不使用 Godot 的 collision 做權威依據。

本階段僅將原始 raw flag **0 視為可走**；Map13/15 目前原始值為 0、6、7，6/7 禁止通行。特殊地形／機甲跨越權限尚待直接驗證 Old 原版，不沿用舊原型的假設；未知非0 flag 全拒絕。世界座標用 floor(x/16,y/16)，不 clamp 出界和非有限值。沿線碰撞採 supercover，恰好越過格點角落需兩個側鄰格皆可走，避免穿牆／穿斜角。

`World/MapNavigator.cs` 的 A* 採 8 鄰居、cost 10/14、octile heuristic、確定性 tie-break，含起終格的路徑只供未來權威 World 消費。每次搜尋上限預設 4096 expanded cells，超限／無路／端點禁止通行明確回傳失敗**而非順移到終點**。建立 MapNavigator 應以地圖為單位重用，之後 World 要對 path requests 設定 Tick budget、路徑 revision/MapEpoch，不能每隻怪每 Tick 無限制重算。目前 A* 還是簡單的 per-query scratch allocation，尚未做高密度 Monster profiler，非最終 AI 選型或 20人/300怪 Gate。

## 測試（無外部 NuGet 測試套件）

```powershell
# 只測合成格式、拒絕案例、斜角與 A*；CI 不需要私有原圖
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release

# 本機額外核實兩張 ORIGINAL Map13／Map15，資產位於 .gitignore 的 client/assets
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release -- --assets "C:\SGO\SRC\SGTest\client\assets"
dotnet build server/src/SGTest.Server/SGTest.Server.csproj -c Release
```

2026-11-16：合成＋雙圖原版 **10 組 PASS**。Map13／Map15 各 960×960，原始 source SHA 匹配，靜態雙向傳點目的地座標對應可走格並有鄰格路徑（**不是實際傳點**）。128 個 deterministic 候選目的地／圖的獨立 query trace，Map13 77 個可走目標／77 成功、Map15 119/119 成功，無超出 1500 cell 預算；樣本 query P95 Map13 59.5µs／Map15 26.0µs，managed bytes/attempt 約10.2KB／10.9KB。**此為當次私有資料／单程序查詢時間，不是 Server Tick P95/P99、300 怪／多人效能或網路延遲。**

所有原版資料、圖片、PCK、測試輸出都只保留在未追蹤的私有目錄；本 repo 只提交格式解析／導航程式和合成驗收。下一項：原版特殊 terrain/Blocking 旗標核實、固定 Tick 的權威路徑排程、movement command validation、限流與 map scope；Wire 仍須完成 REB020 公平選型。
