# SGTest — 新版產品規格（2026-11-15 重建基線）

**狀態：規格已整理，Client/Server 皆尚未實作。**

本文件是產品範圍與原則的**唯一簡版入口**。完整工程規格見 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)、[docs/CLIENT_ARCHITECTURE.md](docs/CLIENT_ARCHITECTURE.md)、[docs/SERVER_ARCHITECTURE.md](docs/SERVER_ARCHITECTURE.md)、[docs/RUNTIME_PROTOCOL.md](docs/RUNTIME_PROTOCOL.md)、[docs/ASSET_ARCHITECTURE.md](docs/ASSET_ARCHITECTURE.md)；新工作只以 [todo.txt](todo.txt) 追蹤。

舊版多階段長規格及歷史完成資訊已收錄 [docs/research/FEATURE_DECISION_HISTORY.md](docs/research/FEATURE_DECISION_HISTORY.md#source-spec-md)，不再和新版狀態混合。

## 目標

全新開發後期《星夢 Online》風格 2D MMORPG 的 Client/Server，**優先人物移動、動畫、方向、受擊與多人同步的正確及流暢**。資源低、載入快、維護簡單，對比 Recovered Model1 必須在同負載下測量。

## 硬性決策

- Godot 4 Client + .NET 8 Server，功能導向 modular monolith，拒絕過度分組／空白多層架構。
- 原版 SPR/ACT/SIF、地圖／Blocking／NPC／Enemy／DrawOrder 由獨立 Converter 產生私有 Runtime 資產；未授權圖資不進公開 GitHub。
- 每個原始素材（Enemy、NPC、Player part、Equipment、Scenery…）獨立 Asset identity，**不跨素材合 Atlas**。素材目錄名保持 Enemy，且 assets 下不增加 generation。
- 推薦原始素材 Height-Dense P0 + Godot imported VRAM Texture + 按需 ResourceLoader；1px padding / POT bucket 只能因實測選用。
- Enemy Renderer 優先評估**共用 SpriteFrames + 可見實例 AnimatedSprite2D**，所有 Player/Enemy/Scenery/Drop 同一 world Y-sort。玩家紙娃娃獨立驗證。
- Network I/O、Client reducer、Server World Tick 不隨 UI FPS、AFK/Unfocus/失焦、縮小而降低。動畫速度與角色移動速度解耦。
- Server 最終決定移動合法性、碰撞、攻擊、命中/MISS/死亡、掉落、物品 UID、金錢和交易。DB 與 Send 不在 Map Tick 同步阻塞。

## 功能範圍

P0：登入/角色、Map13/Blocking、8 向移動與多人 AOI、怪物 AI、普攻/技能/受擊/死亡/復活、掉落拾取、新金色 UI、多解析度、AFK/Unfocus、性能與穩定測試。

P1：裝備/強化、NPC 商店/倉庫/銀行、任務、技能樹、Pet/Mecha、交易/社交；所有紙娃娃與動畫動作保留原版語意。

P2：PVP 名稱顏色、玩家商店／Housing／公會與擴充系統。

詳 [docs/GAMEPLAY.md](docs/GAMEPLAY.md)；目前**全部新版功能均為未實作**，歷史測試只供重建基準參照。

## 驗收

- 同原始素材、同時序、同解析度及 Y-sort 的 Recovered 對照；
- 真實 20–100 玩家、300–1000 Enemy、多掉落，Packet trace／Input-to-Render／Frame P95/P99／RSS／Texture／Draw Calls；
- 多解析度、各種動作/方向和紙娃娃前後順序；30 次切圖/登入/AFK 沒有遞增記憶體；
- Windows Release + 外部 PCK 與 macOS 真機載入；
- 每個完成項目都有可重現測試，未跑必標示 NOT VERIFIED。

**唯一進度文件：todo.txt。**