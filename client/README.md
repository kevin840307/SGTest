# Client 工作區

這個資料夾保留給獨立的 Godot 4 Client；目前 M0 只完成原版素材研究與本機轉換器，尚未建立正式遊戲場景。Godot 專案、Client 測試與 UI 資源將只放在 `client/`，不引用 Server 專案的內部類別或資料庫。

初始切片依 [SPEC.md](../SPEC.md)、[架構](../docs/ARCHITECTURE.md)與 [素材策略](../docs/ASSET_RUNTIME_STRATEGY.md) 實作：版本化協定接收、世界狀態 reducer、依 viewport/zoom 的可見渲染、人物動作與紙娃娃、可切換金色／現代 UI。網路與固定邏輯不隨畫面 FPS 或失焦降頻。
