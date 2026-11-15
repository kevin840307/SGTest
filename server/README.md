# C# / .NET 8 Server

入口：`server/SGTest.Server.csproj`。

```powershell
dotnet run --project server/SGTest.Server.csproj -c Release -- --map=13
dotnet run --project server/tests/SGTest.Server.Tests.csproj -c Release
```

目前結構：

```text
Accounts/       authentication / character identity
Networking/     ClientConnection / Wire / SnapshotCodec
Persistence/    SQLite durable storage
World/          WorldHost / World / CollisionGrid / Population
```

舊 `Application/Domain/Infrastructure/Transport` 與 `Session` 命名已移除。Network connection 類別為 `ClientConnection`，連線世代為 `ConnectionEpoch`。

Server 使用原版 `blocking.dat` compact grid；不載入 Client PNG/Atlas。固定 World simulation 不依賴 Client FPS。

完整規則見 `../docs/SERVER_ARCHITECTURE.md`。
