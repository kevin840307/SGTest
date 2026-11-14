# 已知地圖怪物 MapAssetPalette 與 1024 Atomic Atlas

日期：2026-11-14。狀態：本機內容與 GPU 實測通過；正式 Client StreamingManager 尚未實作。

## 為什麼按地圖載入

從 Old Server Dat/Map/MapXX/Enemy.dat 與 Enemy/Enemy_Data.csv 直接編譯後，目前 17 張具有 Enemy.dat 的已知地圖合計只用 29 種怪物 visual；單張地圖只有 1～10 種，即使出生點可達 149 個。Entity 數量不能等同 unique texture 數量。

tools/build_enemy_map_palette.py 輸出每 map 的 enemy ID、sprite、spawn count；tools/compile_map_asset_palette.py 再把 visual 與已驗證 atlas catalog 接起來，輸出每張地圖需要預載的 group/page 與頁面 RGBA 面積。

## 29 種 known-map visual 驗證

- 29 groups / 145 actions / 1,160 logical facings / 8,792 playback steps。
- atomic 1024：32 pages / 62,287,496 RGBA bytes / 1,160/1,160 action-facing 單頁。
- dense 1024：32 pages / 63,770,056 RGBA bytes，但有 9/1,160 action-facing 跨頁。
- 需要 2 頁的 visual 為 EN038A/EN051A/EN068A；atomic 下每個實際 action-facing 仍只使用單頁。

Atomic v3 使用 block-level MaxRects：先保證每個原 SPR 區塊不可拆，再把多個完整 SPR co-pack 到頁面，因此保留動作／方向 locality，又不等於一 SPR 一 texture。

## 每張地圖工作集

reports/runtime-enemy-palettes.json 本機結果：Map13 為 78 spawns / 9 visuals / 9 pages / 約 10.620 MiB atlas RGBA。17 張地圖全部 complete=true；已知地圖約 1～10 visual，頁面工作集約 3.305～17.441 MiB。

這些是 atlas page 像素面積，不等於 Windows process working set；Godot 自身、framebuffer、UI、地圖等另計。

## Map13 GPU A/B（RTX 3050 / Godot 4.7.2 / OpenGL / 1440×900）

| 場景 | 方案 | Pages | RGBA | Load median | Frame P95 | Draw P95 | Video Mem monitor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 78 monsters | dense | 9 | 11,879,204 | 95.016 ms | 8.002 ms | 151 | 34,538,031 |
| 78 monsters | atomic v3 | 9 | 11,135,572 | 94.839 ms | 7.990 ms | 151 | 33,545,907 |
| 300 monsters | dense | 9 | 11,879,204 | 94.895 ms | 7.133 ms | 373 | 34,538,031 |
| 300 monsters | atomic v3 | 9 | 11,135,572 | 79.213 ms | 7.118 ms | 373 | 33,545,907 |

Frame 差異落在抖動範圍，不宣稱 atomic 讓 FPS 顯著變快；可確認 locality 正確、draw calls 沒增加，且此工作集 Video/Texture memory 約少 1 MB。Load 仍受 OS cache/順序影響。

## 決策

怪物 runtime 候選採 map-aware + per-visual atomic 1024 優先：只預載目前 map palette；同 visual 共用 Texture/FrameTable/Material；方向鏡像在 metadata/runtime 做；若單一 SPR 在 1024 無法維持 locality，再依量測提升 2048，不預設 4096。正式 StreamingManager 的 GPU upload p99、切圖、登出100次仍 NOT VERIFIED。
