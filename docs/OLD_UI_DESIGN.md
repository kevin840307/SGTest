# 後期《星夢 Online》金黃色介面 — Godot 原生重新開發

狀態：設計時期 **已由使用者選定**；原版 UI 精確版面、圖資、授權尚待確認。

## 唯一選定的視覺基準
- **後期星夢 Online 金黃色 UI**；早期星空之門藍色 UI 不採為正式版本。
- 另提供可切換的現代 UI 排版；與金色版共用資料模型與互動命令，切換時不重建遊戲連線或改變世界 Tick。
- 歷史三個時期：https://www.2000fun.com/thread-5046304-1-1.html
- 後期遊戲畫面（角色屬性、物品、底部聊天、金色視窗）：https://forum.gamer.com.tw/C.php?bsn=6325&snA=8758
- 另一張舊遊戲畫面：https://forum.gamer.com.tw/Co.php?bsn=06325&sn=40585
- 以上是研究來源連結，不授權將截圖或原版圖像直接放入 SGTest。

## 硬性架構原則
- **Godot Control/Container/Theme/.tscn 全新製作**；不複製 TestGoGo 的 GameHUD、LoginScreen、背包、技能或其他 UI 程式碼。
- 外觀概念：暖金框、米黃面板、深褐資訊底、細金屬線、少量藍綠狀態條。真正 RGB、邊框像素和圖示需與獲合法使用權的原版素材/實機截圖核對。
- 僅 UI 遵循 Old 風格；網路、MapActor、資料存取、世界渲染完全保持新架構。
- UI 預設 1280×720，支援 800×600、1024×768、1366×768、1440×810、1440×900 與使用者調整大小；寬度目標上限約 1440 像素。不硬綁原版 800×600，UI/文字縮放與世界鏡頭縮放獨立。
- 驗收包含高 DPI、文字縮放、超寬邊緣、縮放相機及重開 UI 後的布局狀態；遊戲 AOI 必須覆蓋實際 viewport 加緩衝。
- 元件以共用 Godot Theme/StyleBoxFlat 作初期可替換資源；確認原版面板/按鈕圖資可用後使用 StyleBoxTexture/NinePatchRect 取代，維持相同 UI 控件架構。
- HK2 的 [ITF 引用盤點](HK2_UI_REFERENCES.md) 已見缺件；金色版允許以 Godot 原生 StyleBox、NinePatch、字型與自製圖形重建相近風格，功能完整性不依賴每張原版 UI SPR。原版圖資僅在使用權與版面語意驗證後接入。

## 主要視窗與優先順序
1. HUD：左下 HP/MP/EXP、底部偏左聊天、右上小地圖、右側主選單、下方技能快捷欄；位置參考後期遊戲畫面。
2. 登入、選伺服器（若有）、角色建立、選角、載入畫面。
3. 角色資訊/屬性、背包/裝備欄、技能/快捷欄、NPC 對話。
4. 商店、銀行、交易、寵物、機甲、設定、任務、社交。
5. 後期擴充收藏/小屋等採同一 Theme，不綁定舊封包與過時編碼。

## 視覺與操作驗收
- 先獨立製作可在 Godot 編輯器打開的 Gold UI Gallery Scene，**不能把它當成正式畫面還原**。
- HUD 不與遊戲 Camera/World Node 混在一起，UI State 只讀從 Server 推導的模型。
- 所有視窗需要 Focus、拖動、最小化/關閉/重開、點空白處合理處理與保存位置；原版實際操作行為需有截圖/影片證據。
- 無版權原圖時用自製圖形/文字作工程占位，不得將未授權原版 UI 素材或第三方畫面塞入 GitHub。
- Headless 檢查能匯入 Scene、建立 UI、驗證金色 Theme；視覺比對及 RAM/VRAM 測量則由真正 GPU runner/本機執行。

## 原版 UI 檔案狀況
TestGoGo 的 server/LEGACY_EXE_UI_RESOURCE_INDEX.md 引用 2009 IDA 的 105 個 .itf 名稱，例如 001_Account_Login、004_Select_Character、006_StatusLite、007_HOTKEY_yl、008_MINIMAP_S、013_Character_info、022_equip、027_Skill、029_SYSTEM。
Old/ClientData/Interface 沒有實體 .itf；目前本機 HK2 有 20 個實體 .itf，但 [引用盤點](HK2_UI_REFERENCES.md) 顯示 122 筆 SPR 引用中 59 筆無同名實體檔。這仍不足以宣稱 UI 圖像、版面與互動狀態齊全；須追 .itf 欄位、載入規則與缺件來源。
