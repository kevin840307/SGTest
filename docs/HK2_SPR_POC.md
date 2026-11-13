# HK2 SPR 保真解碼 PoC 與貼圖分組初測

日期：2026-11-13（Asia/Taipei）。原始素材及匯出圖僅存於本機 `local-assets/`，不進公開 Git。使用 `tools/convert_assets.py` 自行讀取乾淨 HK2；不依賴 Recovered 的程式碼或已匯出圖片。

## 使用

```powershell
py -m pip install -r tools/requirements.txt
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames --category Enemy --limit 20
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-atlas --category Enemy --limit 20 --layout atlas --atlas-side 512
py -m unittest tools.tests.test_convert_assets
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_asset_probe.gd -- local-assets/hk2-frames/Enemy/EN050C_ND1.json
```

不加 `--category`、`--limit` 可掃描所有 `.spr` 與 `.ACT`；單檔失敗會列出前 20 個錯誤並以非零狀態結束。重跑以來源 SHA-256 和輸出檔存在性略過未變檔。輸出是每 SPR 的 PNG 與 JSON，以及每 ACT 的 `.act.json` 原始動作紀錄；SPR JSON 記錄來源相對路徑／SHA-256、幀順序、原始寬高、原始 origin、透明裁切範圍和 atlas 區域。`C.D.E.` 使用 AES-256-CBC 與 raw DEFLATE 解開後再走同一 SPR 解碼器。沒有證據的 animation、facing、duration、draw-order、events 一律輸出 `null`，不可從檔名臆造；ACT 的 `raw_time` 不擅自轉換為毫秒。

## 目前實測

HK2 `Enemy` 前 20 個 SPR：分幀與 1024／2048 atlas 模式皆 20/20 成功；`Enemy/EN050C_ND1.spr` 是 C.D.E. 樣本，解出 5 幀。人工建構的 2×1 RGB565/RLE fixture 驗證紅色、不透明／透明像素與負原點；來源圖與匯出圖尚未對原始遊戲畫面做視覺校驗。

先行試跑：`Enemy` 全部 769/769 成功；`Player`、`Interface`、`Map`、`Effect` 各取排序前 20 個 SPR，均 20/20 成功。

2026-11-13 全量更新：上述限制已解除。`py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames` 掃描 **40,224/40,224 SPR 與 178/178 ACT，零失敗**。其中前次已轉的 37,398 個 SPR 及 53 個 ACT 經 SHA-256 重核走快取；其餘 2,826 個 SPR、125 個 ACT 本次新轉。全量輸出是 153,115 張 PNG（558,642,293 bytes）及 40,402 個 JSON（27,765,411 bytes），僅存在被 Git 忽略的本機目錄。這證明 SPR/ACT 的已觀察格式可全量轉出，**不**證明動畫播放語意或 `Data1.dat` 內資源已覆蓋。如此多單張 PNG 也不應直接全部放入正式 Godot 專案匯入；保真輸出是 QA 中間層。

Godot 4.7.2 headless 的 `tools/godot_asset_probe.gd` 已讀取 C.D.E. 樣本匯出的 5 頁／5 幀及 atlas 樣本的 1 頁／12 幀，所有區域都在貼圖範圍內。這驗證 Godot 可讀格式與 manifest 邊界，尚不等於 GPU 渲染或多人效能驗證。

| 20 SPR / 136 幀 | PNG 數 | PNG 磁碟總量 | 解碼 RGBA 面積估計 |
|---|---:|---:|---:|
| 分幀原尺寸 | 136 | 4,566,864 bytes | 18,499,136 bytes |
| 每 SPR atlas，512 | 40 | 4,465,544 bytes | 19,150,180 bytes |
| 每 SPR atlas，1024 | 20 | 4,414,383 bytes | 22,229,404 bytes |
| 每 SPR atlas，2048 | 20 | 4,355,315 bytes | 27,434,780 bytes |

這是 PNG 尺寸及像素面積估計，**不是** Godot VRAM、draw calls 或 FPS。512 小 atlas 此樣本增加約 3.5% 的 RGBA 面積，並把貼圖數降至 40；1024／2048 增加約 20%／48%。因此目前預設分幀，`atlas` 僅用於 A/B，沒有將全部怪物合成一張大圖。長期候選是按角色／怪物及同時出場的動作分成受控小組，配合可見範圍載入和卸載；需先查 ACT/SIF、方向、裝備疊層，再在同等場景／怪物數下量測。動作資料未確定前，無法宣稱動畫正確或比 Recovered 更快。

Godot 4.7.2 headless 以 `tools/benchmark_asset_layout.gd` 在同一 20 SPR 樣本重複載入 5 次，Image PNG 解碼中位數：分幀 83.584ms（136 頁）、512 atlas 66.662ms（40 頁）；單次試跑且含 OS 檔案快取波動，僅支持此樣本 CPU 讀檔成本較低，**不**代表 GPU VRAM、draw calls、實際 300 怪 FPS。可重跑命令：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_asset_layout.gd -- local-assets/bench-frames/Enemy
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_asset_layout.gd -- local-assets/hk2-atlas-512/Enemy
```
