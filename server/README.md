# C#／.NET 8 Server

入口：`SGTest.Server.csproj`。從儲存庫根目錄執行：

```powershell
dotnet run --project server/SGTest.Server.csproj -c Release -- --map=13
# 純模擬測試
dotnet run --project server/tests/SGTest.Server.Tests.csproj -c Release
```

- `Domain/World.cs`：單寫入權威移動／AI／戰鬥／掉落／拾取，無 transport、Godot 或 DB 依賴。
- `Domain/CollisionGrid.cs`：碰撞、滑動、出生點與不可穿牆契約。
- `Application/WorldHost.cs`：單調固定 30Hz 模擬、有界 command queue、AOI 及事件發布；不等待網路 Send。
- `Transport/Wire.cs`：版本化 MessagePack v2 子集、大小／深度限制與數值驗證。
- `Transport/Session.cs`：有界非同步發送佇列；慢客戶端關閉並重連，不能無限堆積或靜默丟失戰鬥事件。
- `Program.cs`：ASP.NET Core WebSocket `/game`、`/health`、連線與命令速率限制。
- `content/`：由 `tools/export_server_content.py` 生成，只包含碰撞／怪物出生與種類及指紋，不包含圖片。

預設 `127.0.0.1:27888`，Map13使用原版78出生／9種類；指定`--monsters=`才改為壓測數量（種類仍來自Map13 palette）。怪物復活回自己的出生點並保留種類。參數 `--bind=0.0.0.0 --port=27888 --monsters=1000 --drops=5000 --map=13`。`--content=絕對路徑` 可指定部署資料。使用 workstation GC，避免小型單服因每核心 Server GC heap 增加記憶體。

目前為開發用服務；正式帳號驗證、SQLite 持久化、TLS 部署及完整 MMORPG 規則仍待實作。協定見 [RUNTIME_PROTOCOL.md](../docs/RUNTIME_PROTOCOL.md)。Server 不引用 Client 內部類別。
