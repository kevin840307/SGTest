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

## 2026-11-14 雙槽位展開實證

原版參考 `LoadDrawOrderDat` 在 `.text:004082D2`～`004084BC` 對文字 token 作不分大小寫的完整比較；`L-Arm+2` 依次 Append 槽位 **6、5**，`R-Arm+2` 為 **8、7**，`Body+2` 為 **10、9**，`leg+2` 為 **13、12**。不是先移除 `+2` 再解析。其他 token：Head=0、Hair=1、Bonnet=2、L-Shoulder=3、R-Shoulder=4、Back=11；Sword/L-Gun 共用14，R-Gun/Shield 共用15（`.text:00408183`～`004085C8`）。這是參考版本 DIRECT 槽位建立證據，兩槽位各自對應的身體／衣物來源與實際渲染仍待追。

`tools/audit_character_layers.py` 對乾淨 HK2 的 `CharDrawOrder.dat` 與 `R_CharDrawOrder.dat` 保留 raw token、來源 SHA 並展開成槽位序列；**2 表／102 行全部可解析**，未知 token 會報錯。其他騎乘／ClubCloth 表由不同載入路徑處理，未套用這份規則。可重跑：

```powershell
py tools/audit_character_layers.py 'C:\SGO\Stargate Online IE HK2' local-assets/character-layer-audit.json
py -m unittest tools.tests.test_audit_character_layers
```

此雙槽位語法必須保留；使用者要求去除的裝備強化 `+1/+2/+3` 是物品定義別名整理，不能套用到這裡。HK2 程式版本一致性、每個槽位的貼圖來源、人物 action ID 對應、原版畫面前後遮擋及鏡像位置仍 NOT VERIFIED。
