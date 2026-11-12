# SGTest .NET10 Authoritative Multiplayer Prototype

**已提交最小可編譯主幹（未經此環境 .NET/Godot 實際執行測試）。**

## 執行
.NET 10 SDK（Windows/Ubuntu）：
~~~
dotnet build server/SGTest.Host/SGTest.Host.csproj -c Release
dotnet run --project server/SGTest.Host/SGTest.Host.csproj
dotnet run --project server/tests/SGTest.Domain.Specs/SGTest.Domain.Specs.csproj -c Release
~~~
Godot 4.7.2 Client：在另一個終端執行 \`godot --path client\`，多開視窗會看到其他人的佔位圓點和行走方向。左鍵命令移動、右鍵停止。

本機：\`ws://127.0.0.1:5678/ws\`，\`http://127.0.0.1:5678/health\`。

## 現有行為
- 單 MapActor 固定30Hz逐 tick 權威行走，玩家移動指令只修改 MapActor 狀態，無 SQL/Socket 同步阻塞。
- 玩家 Join/Leave 及已在行走者的重入快照；Server 移動 180單位/秒，分段開始/停止/最後位置；八方向。
- WebSocket 每連線有獨立有界可靠 FIFO queue，慢客戶端斷線讓重新登入走快照，不堵住 MapActor。
- Client WorldState 依 map epoch/entity revision/movement sequence 拒絕陳舊 Move/Spawn。
- 網路傳遞是 **demo-only JSON v0**，不是正式安全協定；未實作真認證、DB、碰撞、AI、強化、商店、傷害或怪物。

## 重要限制
- **只綁 127.0.0.1**，連線匿名，沒有正式登入授權；不能上線對外提供服務。
- 尚無 PostgreSQL/DI repository、版本化 Protobuf/MessagePack，B002/003 尚在進行。
- 本地沒有 .NET10 SDK，GitHub workflow 執行結果必須確認；未測不得標 PASS。
- Engine tick 目標30Hz 只是第一輪基準，後續要在混合20人+300怪下確定合理性。
