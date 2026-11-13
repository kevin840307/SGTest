# HK2 地圖物件 SIF 側檔

2026-11-13：乾淨 HK2 共 186 個 `.sif`，全部在 `Map/` 下；55 個以現有 CDE 包裝，解開後與其餘 131 個共用同一結構。每份依序是 1-byte ASCII SPR 名稱長度、該名稱、1-byte 原始旗標、4 個 little-endian signed 32-bit 數值。186/186 嚴格長度解析成功；旗標 1 有 175 份、0 有 11 份。186 個名稱中，184 個在同資料夾有實體 SPR；`Map/BUILDING/SPYGLASS_HOUSE_01_IN.sif` 與 `_OUT.sif` 指向的 `SPYGLASS_HOUSE_IN.spr`、`SPYGLASS_HOUSE_OUT.spr` 在乾淨 HK2 全樹都不存在。

原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` `.text:00548746`～`005488D8` 直接讀取前兩個長度位元組並尋找 `.sif`。四個數值是否是碰撞框、遮擋框、座標或尺寸，尚未由原版程式證實。`tools/convert_assets.py` 現輸出 `.sif.json`，只保留 `raw_values`，`coordinate_semantics` 為 `null`，避免將未知座標意義寫進 Godot 地圖。

驗證：`py -m unittest tools.tests.test_sif`、全 HK2 186/186 `parse_sif` 成功、186/186 `.sif.json` 已匯出至本機 `local-assets/hk2-frames`。待做：追原版如何使用四個數值及旗標，並將這兩個缺圖列入地圖視覺缺件表。
