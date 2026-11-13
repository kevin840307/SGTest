# HK2 介面檔的 SPR 引用盤點

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（本機 HK2 檔頭、解包後字串與實體檔名）；版面語意仍未知。

HK2 `Interface` 有 20 個 ITF，其中 5 個以 `C.D.E.` 封裝、15 個明文。`tools/inspect_itf.py` 對兩種格式只抽取明確出現的 `.spr` 名稱，不猜控件座標與切片規則。可重跑：

```powershell
py tools/inspect_itf.py 'C:\SGO\Stargate Online IE HK2'
py -m unittest tools.tests.test_inspect_itf
```

20 個 ITF 共出現 122 筆 SPR 引用（同名在不同 ITF 重複計算）；按不分大小寫檔名比對，63 筆在 `Interface` 實體檔可找到，另外 59 筆在 HK2 任何實體 SPR 目錄都找不到。這些缺件可能在 `Data1.dat`、外部更新包或別的版本；尚無證據可選定原因。`006_StatusLite_G.itf` 解包後包含 `O:\Star_Gate_Online\Working\617Beta\Client\Interface\...` 內部路徑，屬版本線索，不能視為發行版本或權利證明。

因此金色舊版 UI 只能先做版面研究與原創 Control/Theme；不能宣稱 20 個 ITF 足以完整還原所有畫面。現代 UI 與金色 UI 需共用狀態／命令，並依實際解析度與 viewport 佈局。參考 [SPEC.md](../SPEC.md) 的 F002/F008 與 ADR-013。
