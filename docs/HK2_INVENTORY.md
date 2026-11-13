# HK2 原始客戶端實體檔案盤點

日期：2026-11-13（Asia/Taipei）  
來源：`C:\SGO\Stargate Online IE HK2`（本機唯讀；報表在忽略的 `reports/hk2-inventory.json`）  
證據等級：DIRECT，僅限實體目錄、檔案位元組與 SHA-256；尚未證明遊戲內動作語意或授權。

## 可重跑命令

```powershell
py tools/inventory_client.py 'C:\SGO\Stargate Online IE HK2' reports/hk2-inventory.json
py -m unittest tools.tests.test_inventory_client
```

盤點結果：41,741 個實體檔，合計 1,333,254,792 bytes。主要分類：Player 37,410、Map 1,081、MS 1,156、Enemy 823、MA 605、Effect 277、Interface 192、NPC 121、Audio 11。副檔名：SPR 40,224、MAP 249、SIF 186、ACT 178、ITF 20、ITD 4、MP3 11。這是檔案數，不能當作完整動作／方向數。

前 6 bytes 為 `43 2e 44 2e 45 2e` (`C.D.E.`) 的 SPR 有 10,760 個，說明必須依檔頭分流解碼，而非只看 `.spr`。`Data1.dat` 為 585,022,714 bytes，SHA-256 `9ae08a8a7bb78472...`；尚未解析其內容，不能斷定實體目錄已包含所有封包資源。`Client.bin`、`SGOClient.bin` 同時存在，版本與實際啟動路徑待查。

HK2 有 20 個 `.itf` 實體檔，修正先前「本機沒有後期 ITF」的假設；其內容與金黃色 UI 的對應關係尚未驗證。`Player` 目錄含 A/B/R/G/Y 五組大量檔案及 draw-order `.dat`，方向、裝備層次與幀時長仍需從 ACT/SIF/原始客戶端載入邏輯確認。報表不進 Git，亦不含圖像或音訊位元組。

下一步：AS002 追查原版讀檔與 ACT/SIF 動作語意；AS003 建立逐分類完整性矩陣；AS004 用已驗證的 SPR 結構做保真影格輸出，再比較 Godot 貼圖組合策略。
