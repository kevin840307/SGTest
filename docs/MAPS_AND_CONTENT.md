# 地圖、路徑、編輯器與內容資料設計

狀態：設計；Old binary 需離線解析校驗（A005）。**不擅自複製未授權 Map/*.scn、圖片與反編譯原始碼**。

## 1. 資料來源與行為參考

舊來源：
- Old/服務端/Dat/Map/MapXX/Blocking.dat：可行走格與特殊地形。
- MapXX/Enemy.dat：怪物位置、種類與 spawn 屬性。
- MapXX/NPC.dat：NPC/傳點位置及腳本參考。
- MapXX/safe_area.dat：地圖安全區域，是否存在依地圖不同而異。
- Old/服務端/Dat/Enemy/Enemy_Data.csv、Old/服務端/Dat/Quest/*.txt、Old/ClientData/...
- 參考來源的 game-editor/README.md 記有 world→grid：floor(world /16)，cell center=16*cell+8，blocking mask=0x07；此為目前專案稽核記錄，不保證每張 Old 地圖沒有例外，必須驗證。

## 2. 新格式與 ID

- MapDefinitionId：單一 immutable 新定義 ID + stable key（e.g. world.map13-like-plateau，正式名稱須原創）。LegacyMapId 僅存於 ImportMapping，避免新系統被舊 ID 限制。
- MapChunk：chunk_id、bounds、static tiles/occluders、collision grid、resource manifests；使用內容 hash 方便重用與增量快取。
- MapEntityPlacement：spawn_id（穩定）、npc_def_id/monster_def_id、x/y、facing、spawn_rule、respawn_group、script_id。
- MapRegion：safe zone、PVP zone、傳點、音效區、觸發邊界、Housing 門口。
- NavGrid / CollisionGrid：Server 與 Editor 相同資料來源及轉換工具；Client 只用讀取副本做視覺/路徑預覽，Server 最後驗證。

## 3. 座標與渲染

- 明確分離 WorldUnit、GridCell、RenderPixel、ScreenPixel；提供確定性 world↔cell 函式及物件中心偏移。
- 地圖大於 Viewport，客戶端根據 Camera WorldRect + 預載 margin 動態載入 chunk；遠圖只留索引與輕量資料，貼圖素材可依 chunk 受控卸載。
- 貼圖圖集依使用頻率及區域拆分，防止進一張地圖就載入全部資產；避免在每次 Spawn 上同步解壓大型貼圖。
- 避免地圖/障礙、人物紙娃娃與掉落物共用昂貴且常駐的 RenderTarget；必要時量測 viewport 開銷。
- Camera 高解析度/超寬/縮放：送 Server 的 world view rectangle 是依實際 canvas transform 反算，不假設舊 800x600。Server 有可接受的最大負載範圍，縮放過度時 Client 顯示限制提示，不能讓 Server 跳過實際已顯示的重點玩家。

## 4. 移動、阻擋、尋路

- Server movement intent -> path validator：terrain/footprint/斜角/穿牆/NPC距離/怪物 melee line-of-sight。
- 靜態地圖 Blocking 專用不變 grid；怪物 path A* 或 JPS 依 benchmark 選，按地圖與 AI 優先級限制擴展節點與重算頻率。
- 尋路結果帶 map state/path version，過期目標/新牆/新位置要重新校驗；不可因路徑 worker 延遲而直接瞬移。
- 怪物每個移動路段按 Server monotonic time 持續 advance，AOI 按當前權威位置，而非提前把 current 設成 end。
- Client local 主觀移動採可恢復的視覺預測/行走方向；不以 Client 位置進行怪物追擊或掉落。
- 原始 Map13 台地階梯、Map15 城內可用來構建現代化**自製** fixture 的幾何/動作測試。

## 5. 地圖編輯器（M5）

- 工具可先用 Tiled 或純 Web 編輯器，需 ADR 比較；輸出 canonical GameData JSON/compiled binary，不能直接改生產 DB。
- 支援 Tilemap/Collision/Monster/NPC/Spawn/PVP/SafeZone/Portal、可視化拖曳、框選、復原/重做、複製/貼上、圖層/格線/座標。
- 存檔前檢查地圖 bounds、collision blocked spawn、無效 NPC/Quest/Monster/Item reference、重複 spawn_id、傳點目的地及地圖循環。
- 支援 Map13-like fixture 實際障礙繞行、門檻、不同解析度預覽、map import dry-run、改動 diff。
- 匯入舊二進位由獨立 CLI 提供，以 fixture 保證 decode/encode 對等；沒有來源授權不要提交 Old 原檔。

## 6. 測試

- world ↔ grid 10000種隨機/邊界值 round-trip，行走/禁止格/斜角與新/舊資料一致。
- 500怪在近/遠/視野邊緣跨區域，AOI Spawn/Despawn、AI 位置與怪物受擊權威一致。
- Chunk 來回進出/登出100次、地圖任務/掉落不重複建立、不出現記憶體單向成長。
- 不同 zoom/超寬視窗下，玩家無突然從遠處跳入畫面。
