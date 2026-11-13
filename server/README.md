# Server 工作區

這個資料夾保留給獨立的 .NET 10 Server；目前 M0 尚未建立正式服務。Server 程式、測試、負載工具與部署設定將只放在 `server/`。Client/Server 只透過版本化 wire 與內容契約交換資料，不共享可變領域物件。

初始切片依 [SPEC.md](../SPEC.md)與 [架構](../docs/ARCHITECTURE.md) 建立：WebSocket gateway、單寫入 MapActor、Server 權威座標／碰撞／戰鬥／怪物／多掉落／拾取、AOI 和序列事件。初版使用 SQLite；SQL、交易與 migration 僅在 Infrastructure provider，Domain/Application 對其他 SQL provider 保持獨立。可用地圖 Blocking 與權利範圍需先核對 [HK2 地圖缺口](../docs/HK2_MAP_REFERENCES.md)。目前本機只有 .NET 9 SDK，建立 .NET 10 專案前需安裝對應 SDK 並驗證 CI。
