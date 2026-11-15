# 原版素材來源與驗證契約

**狀態：原始格式研究與 Converter 需求。不是新 Client／Server 的實作進度。**

## 唯讀研究來源

- 原始後期《星夢 Online》客戶端：Data1.dat、loose override，SPR/ACT/SIF/ITF/ITD 與 Map/NPC/Enemy/Blocking 資料。
- Recovered／Old／TestGoGo：對照原版載入、動畫、方向、分層與協定的唯讀參考。
- 原版／衍生圖片及音效只在**本機私有**來源／local-assets/／client/assets/ 產生，不能因擁有安裝檔就假設有公開發行權。

## Converter 的必要步驟

1. 盤點來源檔案相對路徑、大小、Header、SHA256、Archive+loose 覆蓋關係，不猜副檔名代表的格式。
2. SPR 解碼需驗證 Frame count／pixel／origin／透明度／Palette／壓縮；保留失敗的輸入與錯誤位置。
3. ACT/SIF/DrawOrder 與 8 Facing、動作、時間、事件、flip、trim/origin 和紙娃娃前後關係完整保留。
4. 地圖 Terrain/Scenery 與 Enemy 保持分離；Blocking.dat 原位元組網格保留，Server population 才決定怪物出生與外觀 ID。
5. 每個原素材獨立 logical/physical identity，僅相同 Asset 的方向／動作／幀可以進同一 Atlas。
6. Godot imported Texture + external PCK 對不同桌面 GPU 格式都要在目標平台驗證，不能因 Windows 可用就宣稱 macOS 已驗收。
7. 該來源若沒有使用／再散布權，GitHub 僅放解析轉換工具、人工 fixture 與書面研究，不上傳原始或轉換貼圖音效。

## 歷史證據與現況

原型歷史盤點曾記錄 75,588 SPR、282,968 Frame、7,890 Groups；這是先前的研究基準，**新版必須重新跑 Converter Coverage 才可當新結果**。

先前從 Paradise.exe.lst、Old/ClientData／HK2 進行的 Parser、RLE、RGB565、ITF、ACT、SIF 逆向與 SHA 證據，已集中整理到 [原始格式研究彙編](research/LEGACY_SOURCE_RESEARCH.md)。其中每份舊文件的原文和 SHA 都可追溯；原始的分離文件亦可從 Git commit f755a47 取回。

## 使用

~~~powershell
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

建議先以 synthetic fixture 驗證 decoder／錯誤處理，再以完整私有來源做 archive coverage 和 Origin/ACT 語意驗證。Converter 不應依賴尚未重建的 Godot Client／C# Server。