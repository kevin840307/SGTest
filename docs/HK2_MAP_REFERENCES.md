# HK2 地圖清單與實體檔缺口

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 `Map/Map*.map` 文字引用與本機檔案存在性），不推斷 `Data1.dat` 內部內容或原版尋路規則。

可重跑：`py tools/inspect_map_refs.py 'C:\SGO\Stargate Online IE HK2'`；`py -m unittest tools.tests.test_inspect_map_refs`。

219 份頂層地圖 manifest 的引用結果：Tiles 219/219 有檔、MapBlock Array 115/219、Object List 118/219、Blocking 121/219、Minimap 44/136、Zone 0/115。例：`Map13.map` 指向 `Map/Map13/MapBlock.map`、`ObjectList.dat`、`Blocking.dat`、`safezone.dat`，但 HK2 中沒有該 `Map13` 實體目錄。`Map1.map` 指向 `Minimap.jpg`，同目錄卻只有 `minimap.tga`，顯示版本／路徑差異。許多地圖的 Tiles 都指向共用 `Map/Map01/Map.pic`；目前尚未解析 PIC 格式。

因此 HK2 足以建立 SPR/ACT 原始影格管線，但**不能單靠實體目錄**直接聲稱可還原所有地圖畫面與 Server 權威碰撞。缺件可能在本機其他資料／封包中；需要追原始 Client 的搜尋與封包讀取路徑，並以合法來源的 Blocking資料做座標 fixture。多人 Server 的碰撞不能從畫面像素猜測。
