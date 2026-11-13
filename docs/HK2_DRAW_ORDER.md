# HK2 人物疊圖順序證據

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 `Player/*DrawOrder.dat` 原始文字、`Paradise.exe.lst` 具體呼叫位址）。

`Player` 下有 9 個 `*DrawOrder.dat`。`CharDrawOrder.dat`、`R_CharDrawOrder.dat` 與兩份 ClubCloth 表各有 51 個不重複鍵；AZ/BZ/GZ/RZ/EM 表各 12 鍵。每行是 `動作方向鍵 : layer>layer>...`，例如 `NS1` 的圖層序列會依 `NS2/NS3/NS4/NS8` 改變，攻擊類鍵也有不同順序。`leg+2` 等後綴暫按原文保留；其語意尚未核對，不等於使用者要求移除的裝備強化 +2 物品 ID。

原版反編譯直接證據：`Paradise.exe.lst` `.text:00408049` 定義 `CSGOCore::LoadDrawOrderDat`；`.text:00408822` 和 `.text:0040882E` 將 `Player\CharDrawOrder.dat` 傳入該函式，並使用 `CSGOLibrary::GetCharAction` 回傳物件；`.text:00408894`／`.text:004088A0` 對 `R_CharDrawOrder.dat` 做同樣操作。這表示疊圖順序參與人物動作資料，不能把所有角色部位預烘成無法調序的一張固定圖。

`tools/convert_assets.py` 現在會把 9 份表逐行保真轉成私有輸出 `.draw_order.json`，記錄 source SHA、raw key 和 raw layer 序列。可重跑：

```powershell
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames --category Player --limit 20
py -m unittest tools.tests.test_convert_assets
```

下一步：追 `LoadDrawOrderDat` 對 `+N` 的解析與實際繪製優先級，核對 1/2/3/4/8 方位與鏡像規則，再用原版畫面驗證衣服、手臂、武器、盾牌在站立／行走／攻擊／受擊／死亡的遮擋順序。
