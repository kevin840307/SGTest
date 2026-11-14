# Stargate Reborn / SGTest

目前交付原版素材重建工具與可執行的 Godot 多人客戶端／權威伺服器原型。已接入原版怪物與基本男性角色紙娃娃；人物六個部件及死亡圖集由所有實例共用，圖層順序讀取原版 CharDrawOrder.dat。這不是原遊戲全部功能已完成的版本。

原版來源為 `C:\SGO\Stargate Online IE HK2`。`C:\SGO\Recovered` 是其他人的重開發參考，`Old` 含原始資源／反編譯；生成素材仍直接從原版來源提取。`reports/source_comparison.json` 記錄 HK 與 HK2 的 41,708 個提取輸入逐檔 SHA-256 相同，現有像素驗證對 HK2 也適用。

## 素材策略

- 從原版 `Data1.dat` 解包，再以原版散檔覆蓋；不依賴 Recovered 的生成圖片，也不修改原版目錄。
- SPR 支援 RGB565 RLE、C.D.E. 加密包與 `Ver08` 8-bit 調色盤；保留所有影格索引、透明度、原點與裁切位移。
- 同一實體／裝備部件集中管理。同一實際方向的所有動作必須位於同一塊連續區域，或同一張方向圖；不切開方向區塊。
- `--layout auto`：唯一裁切影格的 RGBA 總量不超過 1.5 MiB 時合併一張；較大素材按實際方向分張。這是依三個怪物樣本的載入結果設定的初始策略，可重新測量調整。
- `--layout entity` 強制同一實體一張；`--layout direction` 每個實際方向一張。沒有方向的素材仍為一張。
- 試排 1024／2048／4096，輸出緊密矩形，不補成整張正方形。單一方向確實超出上限時才嘗試 8192／16384；不縮小原圖，超出支援範圍就報錯。
- JSON 的 `clips` 保留原 SPR 動作／方向／影格順序；`direction_regions` 標明每個方向區塊。相同方向內的重複影格共用像素。
- 鏡射與方位重用記在 `aliases`，不製造缺圖或複製鏡射圖。怪物待機採用 Recovered `monster_entity.gd::sync_animation` 的兩視角重用規則，其他方向採相同來源方向與穩定 fallback。
- ACT 保留原始影格／tick、loop、音效事件。播放端目前以明確的 50Hz tick 政策播放；尚未整合音效播放；已接入基本男性角色紙娃娃，完整外觀自訂仍待開發。
- 音效、圖片與表格由 `copy_support_assets.py` 處理，含 CDE 解包及 TGA 轉 PNG。地圖／表格保留解包資料，原型已接入 Map13 的地磚／物件／碰撞，其他地圖格式尚待驗證。

## 重建與執行

Python 3.10+；本機實測 Godot 4.7.2 Compatibility/OpenGL。

```powershell
python -m pip install -r requirements.txt
.\tools\rebuild_assets.ps1 -Client 'C:\SGO\Stargate Online IE HK2'
```

可分別執行：

```powershell
python tools/extract_client.py 'C:/SGO/Stargate Online IE HK2' .cache/client
python tools/build_assets.py .cache/client assets/generated --layout auto
python tools/copy_support_assets.py .cache/client assets/generated
python tools/build_maps.py .cache/client assets/generated --map 13
python tools/validate_assets.py .cache/client assets/generated --all-pixels
```

開兩個 PowerShell：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --max-fps 60 -- --server
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path . --max-fps 60
```

方向鍵移動，左鍵點怪攻擊、點掉落拾取。F2 切換面板外觀。遊戲內可選 960×540、1280×720、1440×810、1440×900，也可拖動視窗；上限 1440×1080、下限 640×360，支援不同長寬比。

預設使用已驗證的原版 Map13（尼西亞台地）。`--map=0` 可切換到不載入地圖的開發測試場地；`--combat-arena` 為扣血／死亡／復活同步的測試情境。其他地圖可交給 `build_maps.py --map N`，但本次只對 Map13 做過完整驗證。

Map13 的 230,400 個地磚索引共用 865 種 32px 地磚，打包後紋理約 3.4 MiB。地面僅對視窗附近格子建立 MultiMesh 實例；不為每格建立節點，也不把整張 15360×15360 地圖攤平成巨圖。367 個場景物件沿 BLG／STH 相對引用找原 SPR，17 個多影格物件採明確的 8 FPS 裝飾播放政策，以伺服器時鐘對齊；原檔沒有 ACT 時間表，故不宣稱這是原版精確播放速率。

地圖物件依可見範圍載入並共用紋理，未被畫面使用的紋理採 32 MiB LRU 預算；可見素材不會為了預算被反覆卸載。物件、怪物、人物依腳點深度排序。伺服器使用原版 16px Blocking.dat 格網驗證移動、攻擊與拾取，禁止穿牆與斜角穿越；掉落保留在屍體同側可達位置。地圖定義指紋與檔案 SHA-256 檢查可阻止不一致的客戶端資料。

伺服器預設只綁 localhost；區網測試可加 `--bind=0.0.0.0`，客戶端加 `--host=伺服器IP`。壓測可加 `--monsters=1000 --drops=5000`。目前是開發用協定，尚無登入、加密連線、存檔與正式部署流程。

生成資料使用 `.gdignore` 避免編輯器大量匯入。執行時直接讀 PNG／JSON；外部資源根目錄可用 `--assets=C:/game/assets/generated` 指定。正式匯出時必須另行配送這個資料夾；本次尚未驗證打包成單一 PCK。

## 結構

| 檔案 | 責任 |
| --- | --- |
| `scripts/server_world.gd` | 純模擬：移動、AI 追逐、攻擊、死亡、復活、掉落與拾取權威 |
| `scripts/main.gd` | ENet 連線、AOI 更新、輸入與原型 UI |
| `scripts/client_replica.gd` | 各實體獨立收包、版本與 AOI 淘汰、位置歷史插值 |
| `scripts/local_prediction.gd` | 本地輸入立即預測、確認序號重播與平滑校正 |
| `scripts/movement.gd` / `input_codec.gd` | 前後端共用碰撞移動與有界冗餘輸入 |
| `scripts/server_clock.gd` | RTT 補償、抗亂序且不倒退的動畫時鐘 |
| `scripts/spatial_grid.gd` | AOI／AI 鄰近查詢，減少每個玩家全表掃描 |
| `scripts/snapshot_codec.gd` | 24-byte 實體資料，含 HP 與 uint32 狀態版本；每包最多 800-byte payload |
| `scripts/entity_atlas.gd` | 多實例共享紋理、裁切原點、方向 alias 與 ACT 播放 |
| `scripts/paper_doll.gd` | 原版人物部件、原版圖層順序與八方向鏡射 |
| `scripts/action_policy.gd` | 一次性動作鎖定、可靠事件與快照時間精度、延遲事件淘汰 |
| `scripts/drop_renderer.gd` | MultiMesh 批次繪製掉落，資料變化時才重建 |
| `scripts/terrain_renderer.gd` | 原版地磚索引、視窗範圍 MultiMesh 與單張地磚圖集 |
| `scripts/map_objects.gd` | 可見物件、共用紋理與 LRU 快取 |
| `scripts/collision_grid.gd` | 原版碰撞格網、線段檢查、滑動與合法出生位置 |

模擬與輸入 30Hz、位置快照 10Hz；怪物 AI 每隻 5Hz，分成六組錯開判斷，追逐位置每個模擬步持續前進。快照切成 MTU 以下的小封包，各實體直接接受自己的新資料，毋須等整張世界快照湊齊。AOI 移出使用可靠 ID 淘汰及短期墓碑，舊包不會把離開的實體復活；攻擊事件、掉落差異與背包使用獨立可靠通道。掉落新增／拾取即時傳送，AOI 每 500ms 對帳。

本地人物使用同一個移動／碰撞函式立即預測，伺服器每 tick 最多消耗一個有序輸入，再按確認序號重播尚未確認的輸入；最近三筆冗餘傳輸，單次封包遺失不需停等。死亡、復活有獨立生命版本，舊輸入不延續到新生命；超過 500ms 未收到權威資料會停止新增預測。遠端人物與怪物各自保留最多八筆位置，延後 120ms 線性插值，缺包最多外推 100ms，移動受地圖碰撞限制。可靠戰鬥動作立即顯示，死亡／復活直接重置歷史。

動畫共享經 RTT 校正的單調時鐘，但每隻實體用自己的動作起始時間／步伐相位。共用圖集預先編譯 ACT 累積時間表和影格描述，以二分搜尋取得影格；不同實體相位不需要重掃 ACT 或重新配置影格字典。快取依 clip 和影格數有界，不依怪物數或時間增長，也不量化時間。

已加入角色 HP、怪物攻擊預備／命中、可打斷攻擊、倒地、三秒復活與兩秒出生保護。倒地時伺服器拒絕移動、攻擊及拾取，復活保留背包。可靠動作事件與快照共用每實體 uint32 版本，避免同 tick 傷害、封包亂序或浮點精度造成狀態倒退。

伺服器不載入圖像資源。客戶端目前共用一種原版怪物與基本男性角色資源。完整角色自訂、物品圖示映射、其他地圖／轉場、裝備、技能、任務與資料持久化仍待開發；怪物目前採直線追逐與碰撞滑動，尚未做障礙繞路尋徑。

## 驗證

```powershell
python -m unittest discover -s tools -p test_assets.py
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_world.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_combat.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_navigation.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_replica.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_prediction.gd
python -m unittest discover -s tools -p test_maps.py
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path . --resolution 1440x900 --script tools/map_preview.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_directions.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path . --resolution 1440x900 --script tools/direction_preview.gd
python tools/benchmark_assets.py .cache/client
python tools/benchmark_rendered.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 8 --seconds 30
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 2 --seconds 15 --combat-arena
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 2 --seconds 30 --impaired
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 2 --seconds 20 --impaired --combat-arena
python tools/visual_probe.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe
```

報告在 `reports/`。載入數據為熱 OS 檔案快取，不代表冷磁碟讀取；headless 多人測試不代表實際 GPU 幀率；`--impaired` 另使用實際 UDP 代理加入每向 70±25ms 延遲、5% 丟包及亂序，這是可重跑的本機網路退化測試，並不代表所有網際網路情況。客戶端可用 `--host=位址 --port=27888` 指定連線。可用 `validate_assets.py` 做每組固定抽樣，或 `--all-pixels` 對每個原 SPR 影格做像素還原檢查。

原始素材、解包快取與生成媒體不進 Git，避免將近 GB 級二進位輸出塞入歷史；可重跑的程式、設定與測試報告提交在 `master`。新增原版素材或改排法後重新執行重建即可。
