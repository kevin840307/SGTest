# 原版人物方位與水平鏡像

證據：`C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOCharDrawLogic::DirectionMapping`（`.text:004C263F`～`004C283A`）及 `SetDirection`（`.text:004C24EA`～`004C25B3`）。這是原版參考程式的直接證據；HK2 客戶端與該參考版本的二進位等同性仍待驗證。未把反編譯內容放進 Git。

原版將邏輯方位 5、6、7 分別取來源方位 3、2、1 並水平鏡像。方位 1、2、3 直接使用。方位 4、8 在原版數值 draw action 4～17 時分別取來源方位 3、1 並鏡像；其他 action 則保留 4、8 原圖。原版也將鏡像旗標獨立回傳，供繪圖使用（`CSGOChar::GetDirectionMapping`，`.text:004C0B6A`～`004C0BB0`）。

`tools/legacy_direction.py` 將映射表示為小型純函式，供素材檢視器及未來 client 使用；不猜 action 數值對應的名稱，也不預先鏡像輸出圖片。這可共用原始圖、減少轉換檔案與記憶體。鏡像時還須驗證 SPR origin 與裝備圖層的位置計算，不能只翻轉像素。

驗證：`py -m unittest tools.tests.test_legacy_direction`。待做：確認 action ID 與 ACT key 的對應、其他角色類型的方位路徑、實際遊戲畫面比對。
