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
現行命令（實作於 tools/inventory_client.py）：
python tools/inventory_client.py "C:\SGO\Stargate Online IE HK2" reports/hk2-inventory.json

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


## 2026-11-13 現況：有原始組語／二進位證據，不等於動畫完整還原

完整分析見 [LEGACY_ASSET_EVIDENCE_20261113.md](LEGACY_ASSET_EVIDENCE_20261113.md)。
來源依據：TestGoGo `0f37c5268ceeac1144ed13556f3aeb191b93711e`，`Paradise.exe.lst` Git blob `16710727227dc5ff8d623ea2acab17cc468fc129`。

- 已直接定位 `xnFileRead` / `xnSpr::LoadSpr` / `DecodeFrameLine` 與群組資料、LZSS 的函式及地址。已在四個原版 NPC SPR 上驗證 footer frame count、frame offsets、每影格寬高／原始 anchor、逐列偏移及三類 RLE token，結構性測試合計 627 列無未識別 token。
- 已確認 `NPC203.sif` 含 SPR 檔名及四個與 SPR 影格標頭對應的整數；但 SIF 額外旗標與精確定位語意未確認。
- `.act` 是原版多處搭配 `.spr` 使用的動作描述檔名，尚缺實際原件，**未解出方向、動作、逐格時長或完整裝備疊圖**。原始 `.itf` 尚未提供；`.itd` 為資料表來源，不應拿來補動作資訊。
- 已新增唯讀掃描器 `--header-bytes`（預設 16，0～64）、XN SPR 解析器、輸出原始 raw anchor + 未知欄位為 null 的 JSON，以及獨立 Tkinter Viewer。當 Viewer 用 100ms 預覽播放時，並不是聲稱原版動畫 100ms。
- `RGB565` 只是目前的暫用 16-bit 色彩解釋；沒有原版截圖像素比對以前，不得宣稱 PNG 色彩／Alpha「完全相同」。
- 盤點報表 schema 已升至 2；所有未授權原件與轉換圖片均只能保留在本機，不得提交至 SGTest 公開 Git。

下一個 Session **先接 AS001**，在合法持有的原版後期 Client 安裝目錄執行：

```powershell
python tools/inventory_client.py "C:\SGO\Stargate Online IE HK2" reports/hk2-inventory.json
python tools/legacy_spr_decoder.py --input "C:\SGO\OriginalClient\NPC\NPC203.spr" --output "C:\SGO\reports\npc203"
python tools/legacy_asset_viewer.py "C:\SGO\reports\npc203\animation.json"
python -m unittest discover -s tools/tests -p "test_*.py" -v
```

以上路徑是使用範例，**不是已驗證存在的本機原件路徑**。按 AS002 → AS003 → AS004... 繼續追 trace，沒有完整角色 SPR+ACT/後期 UI 原件與使用權以前，M0 Gate 不可通過，禁止正式 Godot Client/Server/Atlas 實作。
