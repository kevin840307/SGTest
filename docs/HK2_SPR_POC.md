# HK2 SPR 保真解碼 PoC 與貼圖分組初測

日期：2026-11-13（Asia/Taipei）。原始素材及匯出圖僅存於本機 `local-assets/`，不進公開 Git。使用 `tools/convert_assets.py` 自行讀取乾淨 HK2；不依賴 Recovered 的程式碼或已匯出圖片。

## 使用

```powershell
py -m pip install -r tools/requirements.txt
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames --category Enemy --limit 20
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-atlas --category Enemy --limit 20 --layout atlas --atlas-side 1024
py -m unittest tools.tests.test_convert_assets
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_asset_probe.gd -- local-assets/hk2-frames/Enemy/EN050C_ND1.json
```

不加 `--category`、`--limit` 可掃描所有 `.spr`；單檔失敗會列出前 20 個錯誤並以非零狀態結束。重跑以來源 SHA-256 和輸出檔存在性略過未變檔。輸出是每 SPR 的 PNG 與 JSON；JSON 記錄來源相對路徑／SHA-256、幀順序、原始寬高、原始 origin、透明裁切範圍和 atlas 區域。`C.D.E.` 使用 AES-256-CBC 與 raw DEFLATE 解開後再走同一 SPR 解碼器。沒有證據的 animation、facing、duration、draw-order、events 一律輸出 `null`，不可從檔名臆造。

## 目前實測

HK2 `Enemy` 前 20 個 SPR：分幀與 1024／2048 atlas 模式皆 20/20 成功；`Enemy/EN050C_ND1.spr` 是 C.D.E. 樣本，解出 5 幀。人工建構的 2×1 RGB565/RLE fixture 驗證紅色、不透明／透明像素與負原點；來源圖與匯出圖尚未對原始遊戲畫面做視覺校驗。

擴大試跑：`Enemy` 全部 769/769 成功；`Player`、`Interface`、`Map`、`Effect` 各取排序前 20 個 SPR，均 20/20 成功。HK2 所有 40,224 個 SPR 尚未批次跑完，不能宣稱全檔支援。

Godot 4.7.2 headless 的 `tools/godot_asset_probe.gd` 已讀取 C.D.E. 樣本匯出的 5 頁／5 幀及 atlas 樣本的 1 頁／12 幀，所有區域都在貼圖範圍內。這驗證 Godot 可讀格式與 manifest 邊界，尚不等於 GPU 渲染或多人效能驗證。

| 20 SPR / 141 幀 | PNG 數 | PNG 磁碟總量 | 解碼 RGBA 面積估計 |
|---|---:|---:|---:|
| 分幀原尺寸 | 141 | 4,591,039 bytes | 18,583,284 bytes |
| 每 SPR atlas，1024 | 20 | 4,414,383 bytes | 22,229,404 bytes |
| 每 SPR atlas，2048 | 20 | 4,355,315 bytes | 27,434,780 bytes |

這是 PNG 尺寸及像素面積估計，**不是** Godot VRAM、draw calls 或 FPS。單純合圖在此樣本增加 19%～48% 的 RGBA 面積；因此目前預設分幀，`atlas` 僅用於 A/B，沒有將全部怪物合成一張大圖。長期候選是按角色／怪物及同時出場的動作分成受控小組，配合可見範圍載入和卸載；需先查 ACT/SIF、方向、裝備疊層，再在同等場景／怪物數下量測。動作資料未確定前，無法宣稱動畫正確或比 Recovered 更快。
