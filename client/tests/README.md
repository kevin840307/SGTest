# Client P0 prototype（原創測試素材）

- `godot --headless --path client --import`
- `godot --headless --path client --script res://tests/world_state_test.gd`
- `godot --path client`：在伺服器啟動後左鍵移動、右鍵停止；未啟動 Server 時顯示離線測試畫面。

純資料 WorldState 與全圖單 CanvasItem 渲染分離，零外部素材，方向以線條表示；此階段還不是原版紙娃娃/地圖。網路協定目前是 **demo-only JSON v0**，正式 codec 尚待 A006。WorldState 單元測試不依賴真 Server、GPU。

目前 **未達成** 效能目標；尚未做 benchmark、背景模式、完整 session 驗證。連線到本機開發 Server，不得公開部署。

## 後期《星夢 Online》金色 UI Preview（獨立於多人 Demo）
- Godot 編輯器直接開啟 client/scenes/late_gold_ui_gallery.tscn；可調整各種 Control/Theme 元件。
- 啟動預覽：godot --path client res://scenes/late_gold_ui_gallery.tscn
- 無畫面測試：godot --headless --path client --script res://tests/late_gold_ui_test.gd
- 這是 SGTest **原創** Gold Theme/佈局 Gallery：角色狀態、聊天、小地圖、選單、背包欄位及快捷欄都是可更換的 Godot Control。沒有複製 TestGoGo UI，也沒有把舊素材硬塞進公開 repo。
- 目前並非完全符合原版像素；待取得原版 UI 圖檔/ITF/版權與更精確畫面證據後逐格校正。
