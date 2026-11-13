# HK2 依圖片尺寸分組與 Godot 實測

日期：2026-11-14。來源僅為乾淨 `C:\SGO\Stargate Online IE HK2`；轉換頁面和實測 JSON 留在被 Git 忽略的 `local-assets/atlas-candidates/`。腳本與自造測試資料可推 Git，原版圖片及衍生圖不推送。

`tools/build_atlas_groups.py` 直接讀原始 SPR，保留每個來源 SHA-256、原始幀索引、尺寸、裁切、origin 及 atlas rect。Enemy／Player 暫用「原目錄＋第一個底線前的檔名前綴」聚合；其他類別逐 SPR 分組。此為**檔案局部性規則**，不是已驗證的動作、裝備或地圖同屏關聯。邊緣作 2px 延展，讓線性過濾下相鄰影格不易混色；工具逐幀比對 atlas 內容與解碼來源。頁面尺寸參數是**最大邊長**，匯出 PNG 裁至實際用到的範圍，所以 RGBA 面積按實際輸出計算。

可重跑（替換成自己的乾淨客戶端路徑）：

```powershell
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/atlas-candidates/1024 --prefix Enemy/EN023A --atlas-side 1024
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_group_atlas.gd -- local-assets/atlas-candidates/1024/Enemy/EN023A/group.atlas.json
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_atlas_render.gd -- local-assets/atlas-candidates/1024/Enemy/EN023A/group.atlas.json
py -m unittest discover -s tools/tests -q
```

同法對 `--atlas-side 512/1024/2048/4096` 重跑。以下三組各在本機跑五次 headless PNG 解碼中位，並用 Godot 4.7.2/OpenGL Compatibility、NVIDIA RTX 3050、1280×720、300 個相同種類但不同幀的繪製實例跑 160 幀（前 40 暖機，後 120 統計）。每組目前**只一次實機渲染試跑**，p95 約 8ms，不能宣稱統計顯著或多人場景已達標。headless `texture_create_median_ms` 不代表 GPU upload 時間。

| 來源組 | 最大邊 | 頁數 | 實際 RGBA 面積 | PNG bytes | PNG 解碼中位 ms | Draw calls p50 | Frame p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Enemy/EN023A（15 SPR／111 幀） | 512 | 28 | 19,818,496 | 4,349,779 | 62.134 | 372 | 7.999 |
| 同上 | 1024 | 6 | 20,720,672 | 4,273,636 | 64.485 | 372 | 7.995 |
| 同上 | 2048 | 2 | 21,815,500 | 4,204,432 | 56.925 | 272 | 8.013 |
| 同上 | 4096 | 1 | 20,710,080 | 4,138,764 | 65.011 | 73 | 7.997 |
| Player/A/Cloth/A-Back07（59 SPR／219 幀） | 512 | 2 | 1,396,860 | 264,053 | 3.667 | 174 | 8.000 |
| 同上 | 1024 | 1 | 1,448,400 | 260,199 | 3.929 | 73 | 8.070 |
| 同上 | 2048 | 1 | 1,665,420 | 257,346 | 3.910 | 73 | 8.006 |
| 同上 | 4096 | 1 | 1,680,960 | 255,101 | 4.146 | 73 | 8.017 |
| Map/God_building/GOD_CITY_001（1 SPR／1 幀） | 1024 | 不可用 | 單幀 1151×851 放不下 | — | — | — | — |
| 同上 | 2048 | 1 | 3,950,100 | 602,635 | 10.070 | 73 | 7.994 |
| 同上 | 4096 | 1 | 3,950,100 | 602,635 | 9.674 | 73 | 7.997 |

這三組的暫定選擇：小衣物用 **1024**（一頁，較 512 增約 51.5 KB RGBA，但 draw calls 少）；單張超過 1024 的地圖物件用 **2048**（4096 無收益）；整組 EN023A 若所有動作同時載入，**4096** 在此測試用一頁並使 draw calls 最少，但比 512 多約 0.89 MB RGBA。是否把待命／移動與死亡／稀有攻擊拆成不同生命週期，仍要查 ACT 與同屏使用關係；不能直接把 4096 用在所有怪物。

尚未驗證：Godot 實際 VRAM/上傳尖峰、多人紙娃娃混合、500 掉落、20 玩家／300 怪的多種類場景、4K／21:9／zoom、反覆切圖／登出回收、原版逐格定位及貼圖過濾 Golden。若頁面來源增加，即使單頁數值較好，也可能增加常駐資源；正式決策要看實際 Map Asset Palette 和上述完整場景。
