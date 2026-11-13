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

## 2026-11-13 優先研究序列（AS001～AS009）
- Step 0：先調查**真正的原版 Client 安裝包/資料夾**，現有 Old/ClientData 零散 SPR/SIF/ITD 與 .lst 不是完整圖像庫。
- Step 1：對所有檔案輸出可重現唯讀 inventory，附 header bytes/sha/大小/版本證據；不發布原件。
- Step 2：回溯 Paradise.exe.lst 中 open/read/decompress/decode/animation/render 的完整呼叫鏈，標出來源函數/位址與證據層級。
- Step 3：先以一套完整人物方向與動作進行 decoder PoC，保留原始 frame duration、透明度、pivot、offset、裝備圖層與事件；每次失敗可復現。
- Step 4：建立獨立 Legacy Asset Viewer，原版影片/截圖逐格視覺校驗後才批次匯出 Godot metadata。
- Step 5：實測 Godot 分開圖和 Atlas 的 RAM/VRAM/Frame/DrawCalls，不預設合圖必較省；權利 Gate 未通過不發佈遊戲素材。
- 所有實作階段的狀態以 todo.txt AS001～AS009 為準，未驗證一律 NOT VERIFIED。

## 後續 AS007 候選：Adaptive Map-aware Asset Atlas（設計、尚未執行）

- 原版分離影格經合法且保真解碼後，才離線輸出 immutable canonical frame+metadata；可還原的 transparent trim、同像素影格去重、deterministic MaxRects；候選 Atlas：512、1024、2048、4096。
- 圖集不能只看單張圖片大小，要按 Map/Region 常見怪物/掉落物的 **co-occurrence、spawn frequency、Texture/Material、動作使用時機** 分群。500怪/8種、2000掉落/25種僅為示例，需由實際原版數據驗證。
- Map Asset Palette 預編譯 shared/common、map hot、warm、cold；同素材跨地圖共用；稀有 Boss 技能可分頁。同類多個實例只引用一套 Atlas/FrameTable。
- 低記憶體 Runtime：AssetCatalog、可視預載、非同步 IO/decode、有界 GPU Commit、RefCount + TTL/LRU + 進出邊界 hysteresis；切圖/登出釋放 World 資源。不能讓 loading queue 阻塞 ClientNetwork/Reducer。
- AS007 對同場景量測 PNG/SpriteFrames vs 固定圖集 vs adaptive、RAM/VRAM、TextureSwitch/DrawCalls、FrameP95/P99、載入尖峰、資源回收；**設計 NOT VERIFIED**。詳 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md) 與 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
