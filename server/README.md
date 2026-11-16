# SGTest Server — .NET 8 重建（碰撞／導航＋手動 World Tick 核心）

新版 Server 是**從零重建**，不還原舊 SGO 原型。`src/SGTest.Server` 目前提供 Map13／Map15 原版權威碰撞驗證、有界 A*，以及不依賴 Wire／Godot 的 **單寫者手動 `ServerWorld.Tick()`** 與經序號／世代檢查的移動命令。**尚無可執行 Server Host、實際 30Hz 排程、Socket／Wire、登入認證、WorldReady／AOI、DB／怪物／戰鬥或連線 Client**；不代表遊戲已可上線。

## 正式內容契約

`World/BlockingGrid.cs` 載入唯一 Converter 的 `maps/<map_id>/world.json` (v3) + 原始 `blocking.dat`（8 bytes 小端寬高 + 寬×高 raw bytes）。map_id、collision_cell=16、world pixel size、檔案長度和 world.json 中的 `collision_sha256` 必須完全一致；遇到舊 schema／錯地圖／缺少原始檔／被修改或越界時直接失敗。Server 不讀圖片、Atlas，也不使用 Godot 的 collision 做權威依據。

本階段僅將原始 raw flag **0 視為可走**；Map13/15 目前原始值為 0、6、7，6/7 禁止通行。特殊地形／機甲跨越權限尚待直接驗證 Old 原版，不沿用舊原型的假設；未知非0 flag 全拒絕。世界座標用 floor(x/16,y/16)，不 clamp 出界和非有限值。沿線碰撞採 supercover，恰好越過格點角落需兩個側鄰格皆可走，避免穿牆／穿斜角。

`World/MapNavigator.cs` 的 A* 採 8 鄰居、cost 10/14、octile heuristic、確定性 tie-break，含起終格的路徑只供未來權威 World 消費。每次搜尋上限預設 4096 expanded cells，超限／無路／端點禁止通行明確回傳失敗**而非順移到終點**。建立 MapNavigator 以地圖為單位重用；`ServerWorld` 已對每次 Tick 處理的路徑要求設上限，並以 MapEpoch 拒絕舊命令。真正 30Hz host、Actor/AI 排程與 MapEpoch+revision 網路確認仍待實作，不能每隻怪每 Tick 無限制重算。目前 A* 還是簡單的 per-query scratch allocation，尚未做高密度 Monster profiler，非最終 AI 選型或 20人/300怪 Gate。

## 測試（無外部 NuGet 測試套件）

```powershell
# 只測合成格式、拒絕案例、斜角與 A*；CI 不需要私有原圖
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release

# 本機額外核實兩張 ORIGINAL Map13／Map15，資產位於 .gitignore 的 client/assets
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release -- --assets "C:\SGO\SRC\SGTest\client\assets"
dotnet build server/src/SGTest.Server/SGTest.Server.csproj -c Release
```

2026-11-16 的初步 Blocking/A* 基準：合成＋雙圖原版 **10 組 PASS**（已保留於現行更完整的 24 組測試中）。Map13／Map15 各 960×960，原始 source SHA 匹配，靜態雙向傳點目的地座標對應可走格並有鄰格路徑（**不是實際傳點**）。128 個 deterministic 候選目的地／圖的獨立 query trace，Map13 77 個可走目標／77 成功、Map15 119/119 成功，無超出 1500 cell 預算；樣本 query P95 Map13 59.5µs／Map15 26.0µs，managed bytes/attempt 約10.2KB／10.9KB。**此為當次私有資料／单程序查詢時間，不是 Server Tick P95/P99、300 怪／多人效能或網路延遲。**

所有原版資料、圖片、PCK、測試輸出都只保留在未追蹤的私有目錄；本 repo 只提交格式解析／導航程式和合成驗收。下一項：原版特殊 terrain/Blocking 旗標核實、固定 Tick 的權威路徑排程、movement command validation、限流與 map scope；Wire 仍須完成 REB020 公平選型。


## REB021-WORLD-CORE（2026-11-16：手動模擬核心，非已啟動 Server）

`src/SGTest.Server/World/ServerWorld.cs` 在已驗證的 immutable `BlockingGrid`／map-local `MapNavigator` 之上新增單一權威 World。只有建構者所在的 thread 能更動 Actor 狀態或呼叫 `Tick()`；只有 `TryPostMove` 是非阻塞的多生產者入口（CAS 預留容量＋ConcurrentQueue）。並未啟動 socket／背景 Timer，`Tick()` 必須由未來固定時鐘 Host 推進，**Godot Rendering FPS 不決定世界邏輯**。

- **連線和權限邊界**：`TryTrustedRegister`、`TryTrustedTransfer` 和 `TryTrustedDisconnect` **只供已驗證身份、已確認原版 NPC/portal 授權的 Server 內部呼叫**；現在還沒有 Auth/Gateway，外部用戶絕不能取得呼叫權。重新登入重發 ConnectionEpoch，可信轉圖重發 MapEpoch、清除路徑並拒絕舊圖命令。這不等於 MapChange／WorldReady／AOI 已實作。
- **Client 請求邊界**：`MoveTargetCommand(ActorId, ConnectionEpoch, MapEpoch, MovementSequence, TargetX, TargetY)` 只請求目標，不接受直接設定位置。World 先驗證 ConnectionEpoch、MapEpoch、嚴格遞增的 uint movement sequence（0 拒絕），再以原始 Map Collision／A* 權威決定路線。重放、非法座標、目標阻擋、超過 A* 預算都以 `MoveReceiptStatus` 回覆；錯誤輸入不會取代目前合法路徑。現階段不處理 sequence wrap：接近 uint 最大值前須重連／協定協商。
- **固定步進**：每 `Tick()` 最多處理 `MaxCommandsPerTick` 條命令、最多 `MaxPathRequestsPerTick` 次 A*；每 Actor 按 ID 穩定排序推進最多 `MovementPixelsPerTick`，每段再用 `IsDirectClear` 防止穿牆。**8 px/手動 Tick 是測試預設，不是原版人物速度**；正式 30Hz runner、速度／武器／騎乘能力、動作節奏均需從原版核實。此處不啟動 NPC AI、Combat、Drop、網路。
- **防漏狀態**：`LastMovementSequence`（已消費的最高輸入序號）與 `MovementActionSequence`（最後成功授權的 Move／Stop）刻意分離；非法新命令不會誤標目前仍在進行的舊移動。即使 Stop 目的地仍在當前碰撞格、位置沒有改變，亦會增加 `EntityRevision` 並出現在 Tick Changed，供未來其他 Client 收到可靠停止狀態。Tick 回傳有界 `Receipts`／`Changed` 清單，但**尚無正式持久可靠事件佇列／傳輸確認／重同步機制**。
- **預算**：預設 MaxActors=128、MaxKnownActorIds=4096（限制 tombstone 數量）、MaxPendingCommands=512、MaxCommandsPerTick=64、MaxPathRequestsPerTick=8、MaxPathExpandedCells=2048、MovementPixelsPerTick=8。Queue 滿時 `TryPostMove=false`；當 Tick path quota 滿時回 `TickRouteBudgetExhausted`，不悄悄吞掉或無限制累積。Gateway 必須對拒絕／慢客戶端／重試負責，且要另加 per-connection 公平預算。以上都是第一版可調安全上限，不是已完成負載選型。

重跑（已無任何私有資料依賴的合成 checks；原圖部分在 `.gitignore` 中）：

```powershell
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release
dotnet run --project server/tests/SGTest.Server.Checks/SGTest.Server.Checks.csproj -c Release -- --assets "C:\SGO\SRC\SGTest\client\assets"
```

2026-11-16 .NET 8 Release：**21 組純合成 PASS；額外 3 組真圖相關 Gate，合計 24/24 PASS**。涵蓋 MPSC 256 producer input 的32-entry cap、每Tick 8/6 等不同命令／path quota、wrong owner thread、連線／地圖世代 tombstone、重送／失效／無路／A* budget、正確繞牆、Stop 不移動時的 revision、同一輸入但不同呈現讀取頻率的 StateHash 一致。真原版 Map13/15 驗證包含 20 個**測試用假 Actor** 佇列 burst：一次20條命令在6路徑查詢上限下 6 Accepted／14 明確拒絕；都保持可走位置，**不是20條真 socket、多客戶端 AOI，也沒有300隻怪 AI或Gameplay E2E**。

仍未驗證：原版行走速度與 special terrain、能否穿玩家/NPC、真人點地板 A* 細節、可取消的 async path job、WorldReady／可靠資料通道、30Hz P95/P99、20人/300怪/500掉落真負載、Client FPS 0/30/60／AFK 以及重連後完整狀態回補。下一階段是 Host／身份綁定和 bounded reliable events、固定時鐘與真雙圖 Actor 資產顯示，仍依先前 ALT001 比較 Wire 選型。
