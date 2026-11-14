# Stargate Reborn / SGTest

目前交付原版素材重建工具與可執行的 Godot 多人客戶端／權威伺服器原型。已接入原版怪物圖集；人物目前使用簡單標記。這不是原遊戲全部功能已完成的版本。

## 素材策略

- 從原版 `Data1.dat` 解包，再以原版散檔覆蓋；不依賴 Recovered 的生成圖片，也不修改原版目錄。
- SPR 支援 RGB565 RLE、C.D.E. 加密包與 `Ver08` 8-bit 調色盤；保留所有影格索引、透明度、原點與裁切位移。
- 同一實體／裝備部件集中管理。同一實際方向的所有動作必須位於同一塊連續區域，或同一張方向圖；不切開方向區塊。
- `--layout auto`：唯一裁切影格的 RGBA 總量不超過 1.5 MiB 時合併一張；較大素材按實際方向分張。這是依三個怪物樣本的載入結果設定的初始策略，可重新測量調整。
- `--layout entity` 強制同一實體一張；`--layout direction` 每個實際方向一張。沒有方向的素材仍為一張。
- 試排 1024／2048／4096，輸出緊密矩形，不補成整張正方形。單一方向確實超出上限時才嘗試 8192／16384；不縮小原圖，超出支援範圍就報錯。
- JSON 的 `clips` 保留原 SPR 動作／方向／影格順序；`direction_regions` 標明每個方向區塊。相同方向內的重複影格共用像素。
- 鏡射與方位重用記在 `aliases`，不製造缺圖或複製鏡射圖。怪物待機採用 Recovered `monster_entity.gd::sync_animation` 的兩視角重用規則，其他方向採相同來源方向與穩定 fallback。
- ACT 保留原始影格／tick、loop、音效事件。播放端目前以明確的 50Hz tick 政策播放；尚未整合音效播放與完整角色紙娃娃。
- 音效、圖片與表格由 `copy_support_assets.py` 處理，含 CDE 解包及 TGA 轉 PNG。地圖／表格保留解包資料，原型尚未實作完整地圖格式與碰撞解析。

## 重建與執行

Python 3.10+；本機實測 Godot 4.7.2 Compatibility/OpenGL。

```powershell
python -m pip install -r requirements.txt
.\tools\rebuild_assets.ps1 -Client 'C:\SGO\Stargate Online IE HK'
```

可分別執行：

```powershell
python tools/extract_client.py 'C:/SGO/Stargate Online IE HK' .cache/client
python tools/build_assets.py .cache/client assets/generated --layout auto
python tools/copy_support_assets.py .cache/client assets/generated
python tools/validate_assets.py .cache/client assets/generated --all-pixels
```

開兩個 PowerShell：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --max-fps 60 -- --server
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path . --max-fps 60
```

方向鍵移動，左鍵點怪攻擊、點掉落拾取。F2 切換面板外觀。遊戲內可選 960×540、1280×720、1440×810、1440×900，也可拖動視窗；上限 1440×1080、下限 640×360，支援不同長寬比。

伺服器預設只綁 localhost；區網測試可加 `--bind=0.0.0.0`，客戶端加 `--host=伺服器IP`。壓測可加 `--monsters=1000 --drops=5000`。目前是開發用協定，尚無登入、加密連線、存檔與正式部署流程。

生成資料使用 `.gdignore` 避免編輯器大量匯入。執行時直接讀 PNG／JSON；外部資源根目錄可用 `--assets=C:/game/assets/generated` 指定。正式匯出時必須另行配送這個資料夾；本次尚未驗證打包成單一 PCK。

## 結構

| 檔案 | 責任 |
| --- | --- |
| `scripts/server_world.gd` | 純模擬：移動、AI 追逐、攻擊、死亡、復活、掉落與拾取權威 |
| `scripts/main.gd` | ENet 連線、AOI 更新、客戶端插值、輸入與原型 UI |
| `scripts/spatial_grid.gd` | AOI／AI 鄰近查詢，減少每個玩家全表掃描 |
| `scripts/snapshot_codec.gd` | 20-byte 實體資料；每包最多 800-byte payload |
| `scripts/entity_atlas.gd` | 多實例共享紋理、裁切原點、方向 alias 與 ACT 播放 |
| `scripts/drop_renderer.gd` | MultiMesh 批次繪製掉落，資料變化時才重建 |

模擬 30Hz、位置快照 10Hz、輸入約 15Hz（30Hz 模擬下的 50ms 節流）、怪物 AI 5Hz。位置分成 MTU 以下的小封包，限制未完成快照快取並拒絕舊序號；攻擊事件、掉落差異與背包使用獨立可靠通道。掉落新增／拾取即時傳送，AOI 進出每 500ms 對帳。怪物和玩家在每個顯示影格插值；畫面外怪物不送出繪製指令。

伺服器不載入圖像資源。客戶端目前共用一種原版怪物資源作為壓測／接線示範；完整角色、物品圖示映射、地圖、裝備、技能、任務、角色 HP／怪物攻擊玩家與資料持久化仍待開發。

## 驗證

```powershell
python -m unittest discover -s tools -p test_assets.py
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path . --script tools/test_world.gd
python tools/benchmark_assets.py .cache/client
python tools/benchmark_rendered.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 8 --seconds 30
python tools/visual_probe.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe
```

報告在 `reports/`。載入數據為熱 OS 檔案快取，不代表冷磁碟讀取；headless 多人測試不代表實際 GPU 幀率，也不是網際網路延遲／丟包測試。可用 `validate_assets.py` 做每組固定抽樣，或 `--all-pixels` 對每個原 SPR 影格做像素還原檢查。

原始素材、解包快取與生成媒體不進 Git，避免將近 GB 級二進位輸出塞入歷史；可重跑的程式、設定與測試報告提交在 `master`。新增原版素材或改排法後重新執行重建即可。
