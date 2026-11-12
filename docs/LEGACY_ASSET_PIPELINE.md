# Old 原始遊戲素材 → Godot 匯入管線

狀態：原版格式與素材不完整。此文件是技術轉換契約，**不是已完成的匯入結果**。

## 已查到的公開參考資料
SGO feature/local-server-net8 目前導向 TestGoGo 同名分支。2026-11-13 檢查 Old/ClientData 的 Git tree：199 SPR、12 SIF、4 ITD、10 DAT、3 CSV、1 LST。主要 SPR 位於 NPC；Old/ClientData/Interface 有 skill/title/rank DAT，但無實體 ITF。現代 TestGoGo client/Assets 多為 .tres/JSON 與少量 PNG，部分 SpriteFrames 引用 Git 未提供的圖片，不能視為「Old 圖像完整原件」。

## 素材分類
- 原版角色 body、裝備、方向/動作、攻擊/受擊、NPC、怪物、掉落/物品/技能圖示。
- 原版地圖（畫面 Tile / Map Chunk）與 Blocking.dat / NPC.dat / Enemy.dat 分開。
- 後期金色 UI（ITF 描述檔、SPR/UI 貼圖、按鈕、邊框、快捷欄、字型）。
- BGM/SFX/背景影片/機甲/寵物，以及各語系本地化資料。

## 處理方式
1. 在擁有的原始遊戲資料夾進行唯讀盤點：相對路徑、大小、格式、副檔名、選擇性 SHA256；報表寫入 reports（gitignore）。
2. 依版本檢查魔數、壓縮、Palette、Alpha、Sprite Frame、方向、動作、Pivot/Offset、持續時間、裝備層序；**不能從副檔名就宣稱解析成功**。
3. 對已確認格式寫離線 Decoder → RGBA PNG + Metadata（JSON；來源 hash、幀順序、時間、方向、偏移）；保真輸出後才選 WebP/Atlas 壓縮，不能任意變色/去背。
4. 有明確的合法使用權後，才可以整理 SpriteAtlas、Godot SpriteFrames、AtlasTexture/Texture2D、Theme 及 NinePatch 素材；輸出與 Godot resource 生命週期獨立。
5. 每個 Parser 都有人工 fixture、錯誤輸入測試、Golden Hash、原版視覺並排校驗、方向與影格測試；失敗檔明列，不能默默忽略。
6. 建立 ResourceCatalog（登入/選角/地圖/角色/音效/UI 分組）、按需載入、離圖卸載、同 atlas 共用，避免 TestGoGo 既有 Godot 版的資源常駐問題。

## 唯讀盤點 CLI
目標命令（實作於 tools/legacy_asset_inventory.py）：
python tools/legacy_asset_inventory.py --root "C:\SGO\OldClient" --output reports/legacy-assets.json --sha256

不把原件自動上傳、搬動或解碼到公開 Repo；報表只包含相對路徑、大小、Hash，不包含原件。

## 合法使用與公開 Gate
擁有遊戲安裝檔或反編譯程式碼不等於可以對外發布原始貼圖、音樂及 UI。權利範圍必須另行確認：修改/轉換、私人測試、公開 repo、公開遊戲發行與第三方音訊授權各自不同。
權利未確認前，SGTest 可做原創 Godot UI、公開通用轉檔器、人工 fixture；正式舊素材只在使用者本機私有環境測試，公開 GitHub 不包含原件或轉換結果。

## 效能條件
原版素材不是全部預載到 RAM：地圖分區載入、角色紙娃娃共用 TextureAtlas、UI Theme 共用，舊 RenderTarget/Resource 在 logout/map switch 釋放；不降低網路/固定 WorldState/Combat 更新頻率來改善表面數據。