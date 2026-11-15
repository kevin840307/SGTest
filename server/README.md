# Server（等待依規格重新開發）

舊 C#/.NET 8 Server 原型及其測試程式已自本重建基線刪除。此處目前**沒有 .csproj，也不能執行**。

新 Server 設計：[SERVER_ARCHITECTURE](../docs/SERVER_ARCHITECTURE.md)。Converter 仍可將 `world.json`、`population.json`、`blocking.dat` 輸出到 `server/content/`；那是私有產物，不代表 Server 已實作。
