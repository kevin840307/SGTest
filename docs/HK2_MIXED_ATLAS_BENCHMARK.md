# HK2 多種類 Atlas 負載初測

日期：2026-11-14。承接 [單外觀候選](HK2_ATLAS_CANDIDATES.md) 與 [多尺寸素材策略](ASSET_STREAMING_AND_RENDERING.md)。本機只使用乾淨 `C:\SGO\Stargate Online IE HK2` 由 `tools/build_atlas_groups.py` 重新解碼與分組；輸出的 PNG／JSON 媒體均在 Git 忽略的 `local-assets/`，不推送。

## 可重跑工作集

選 8 組 Enemy：`EN114A, EN089A, EN121A, EN116A, EN023A, EN084A, EN111A, EN125A`，依轉換 manifest 的裁切矩形總面積排序，避免先前排序前八組多數太小的偏差。每組取所有同前綴 SPR，共用該外觀 atlas；另取 `Player/A/Cloth/A-Back07` 59 SPR 和排序前 25 個 `MS` 小型裝備視覺。`MS` **只是 500 個掉落物視覺的尺寸代理**，尚未證明是原版物品圖示。此測試不含實際角色紙娃娃其他層、物品 ID、網路或戰鬥。

`tools/select_mixed_benchmark_groups.py` 直接從乾淨 HK2 解碼所有 Enemy SPR、按裁切矩形面積排名，產生上述 34 個 group key 的 UTF-8 清單及私有報告。對每個候選上限重跑：

```powershell
py tools/select_mixed_benchmark_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy-groups.txt
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy/1024 --group-list local-assets/mixed-heavy-groups.txt --atlas-side 1024
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_mixed_render.gd -- local-assets/mixed-heavy/1024 1920 1080
```

同法改成 2048、4096，另可把視窗參數改為 `3840 2160`。Godot 4.7.2／OpenGL Compatibility、NVIDIA GeForce RTX 3050，Y 座標排序繪製 20 個玩家代理、300 隻 8 種怪、500 個 25 種小圖代理。每回合 160 幀，前 40 幀暖機，後 120 幀取 frame p95 和 draw calls。1080p 每尺寸以交錯順序跑三回合，載入耗時及 frame p95 下表為三回合中位；GPU 記憶體監測值以後續同設定的一次 Godot 監測為準，**含引擎／視窗開銷，不是純 atlas VRAM**。

| 最大頁邊 | 頁數 | 實際 RGBA 面積 bytes | PNG bytes | 1080p 載入中位 ms | Draw calls p50 | Frame p95 中位 ms | Godot Video Mem 監測 bytes |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1024 | 87 | 195,550,316 | 31,693,952 | 715.061 | 892 | 7.987 | 285,515,459 |
| 2048 | 44 | 206,281,432 | 31,440,178 | 658.716 | 892 | 7.986 | 299,891,971 |
| 4096 | 34 | 224,978,452 | 31,243,536 | 702.356 | 892 | 7.911 | 324,815,867 |

4K 視窗可建立，單次 frame p95 為 1024=7.934ms、2048=7.931ms、4096=7.995ms；Godot Video Mem 監測分別為 335,281,859／349,658,371／374,582,267 bytes。這次各候選的 draw calls 都相同，影格差距落在試跑波動內。4096 比 1024 減少 53 頁、PNG 少約 0.45 MB，但 Godot Video Mem 監測多約 39.3 MB；全域採 4096 沒有這組證據支持。2048 載入中位約少 56 ms，但比 1024 多約 14.4 MB Video Mem。載入測試是一次性全載且受 OS 檔案快取影響，並非有界串流或 GPU 上傳尖峰。

**暫定策略**：小型且一頁能放下的外觀用 1024；超過 1024 的幀或經實景測得 1024 分頁載入尖峰的外觀可選 2048；4096 只在單一外觀確實需要且可見工作集／GPU 記憶體預算容許時使用。這是素材包裝候選，正式 Map Asset Palette 仍需原版地圖刷怪、實際掉落圖、ACT 動作時序、紙娃娃疊層和多解析度可見範圍證據。

## 素材釋放原語試跑

`tools/benchmark_atlas_lifecycle.gd` 將同一 34 組貼圖載入、放掉所有 ImageTexture 引用、各等待 3 個渲染幀，重複 10 次。本機 Godot Video Mem monitor 基線為 22,858,115 bytes；1024／2048／4096 載入時分別為 283,441,859／297,818,371／322,742,267 bytes，**每次釋放後均回到同一基線，10 次後增長 0 bytes**。Texture Mem monitor 也回到 16,317,963 bytes。可重跑：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_atlas_lifecycle.gd -- local-assets/mixed-heavy/1024 10
```

這只證明獨立 ImageTexture 釋放在此測試中沒有累積；未來的 AssetCatalog、地圖切換、玩家外觀引用、Godot ResourceLoader 快取及 AFK 生命週期還未實作，不能宣稱整個 Client 沒有漏資源。

未測：正式資源管理器的地圖切換與 GPU/RAM 回收、相同資產跨地圖共用、混合玩家裝備、實際 500 掉落圖示、網路事件即時同步、原版影格定位及 Recovered 同機對照。不能從這份渲染代理負載推論 Client／Server 遊戲已達標。
