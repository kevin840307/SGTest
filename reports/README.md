# 實測與素材驗證（2026-11-14）

## 素材覆蓋

原版 Data1.dat SHA-256：`9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42`。解包後套用原版散檔覆蓋。

75,591 個 SPR → 4,821 個實體／部件資源組、282,990 影格、6,854 張紋理，另有 5,557 個音效／圖片／資料檔。

所有 282,990 個影格均從生成圖集重建並比對原 SPR 解碼結果；來源覆蓋、原點、方向區塊與 alias 均通過。錯誤數：0。

## 原生 Godot 載入

RTX 3050、Compatibility/OpenGL；熱 OS 快取，計時不含程式啟動。每列為一次原生載入，較完整的 15 次 CPU 解碼樣本見 atlas_benchmark/results.json。RGBA 為紋理像素容量，未計驅動額外配置。

| 素材 | 排法 | 張數 | RGBA MiB | Godot 解碼／建立紋理 ms |
| --- | --- | ---: | ---: | ---: |
| enemy/en004a | entity | 1 | 10.31 | 61.32 |
| enemy/en004a | direction | 5 | 4.00 | 36.90 |
| enemy/en023a | entity | 1 | 57.41 | 162.66 |
| enemy/en023a | direction | 5 | 18.30 | 91.63 |
| enemy/en050b | entity | 1 | 2.50 | 20.11 |
| enemy/en050b | direction | 5 | 1.44 | 26.47 |

因此採混合預設：小素材（唯一裁切 RGBA ≤ 1.5 MiB）合成一張，較大素材依方向分張。兩種方式都維持相同方向的動作在同一區塊；可用 --layout 明確覆寫。門檻是這批樣本的初始策略，並非所有硬體的最優保證。

## 多人與實際畫面

ENet：Map13、8 個 headless 客戶端、1,000 隻怪、5,000 個掉落，持續 30 秒。通過：True；每位客戶端均收到位置、動作與背包更新。

略過前三個一秒樣本後，伺服器 CPU 中位數 27.7%，客戶端 41.9%；尖峰 RSS 分別 106.7／144.2 MiB。CPU 百分比以一個核心為 100%，含 Windows console 啟動器及其實際 Godot 子程序，不能解讀為整台電腦百分比。

原生單客戶端 1440×900、伺服器同樣 1000 怪／5000 掉落，RTX 3050、上限 60 FPS；暖機 2 秒後測 10 秒：平均 58.50 FPS，影格間隔 P50 16.67ms、P95 20.28ms。末影格 draw calls 134。使用單調時鐘量測，非 Godot 平滑 delta。

這些是本機開發版測試，非冷磁碟、網際網路丟包或多種硬體測試。完整遊戲功能、角色裝備與外觀自訂、其他地圖及持久化仍待開發。

原始 JSON：asset_inventory.json、asset_validation.json、network_load.json、rendered_load.json、rendered_probe.json、atlas_benchmark/results.json、atlas_benchmark/rendered.json。

## 雙向戰鬥同步

2 位獨立 ENet 觀察客戶端、15 秒，扣血／同一角色倒地／復活均收到：True／True／True。整體通過：True。此情境沒有掉落，因此不以背包 marker 作通過條件；背包另外由壓測及原子拾取測試驗證。

## 原版地圖重建

Map13：230,400 個地磚索引、865 種共用地磚、367 個物件，其中 17 個有多影格。地磚紋理 3.40 MiB；重建與地磚像素還原比對耗時 0.483 秒，通過：True。索引檔逐位元保留、碰撞資料與地圖尺寸互相驗證；伺服器不載入紋理。

原生地圖物件對照畫面：總計 230,400 格中，僅建立 1,488 個可見地磚實例；8 個物件共用 2 組紋理，物件 RGBA 合計 2.43 MiB，含角色的畫面 draw calls 為 14。此為局部場景證據，不代表整張地圖同時載入。
