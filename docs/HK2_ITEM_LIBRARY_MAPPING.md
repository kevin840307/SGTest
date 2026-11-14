# HK2 物品定義與地上圖號稽核

日期：2026-11-14。範圍是 AS003 素材對應；**不是**新物品 ID 匯入或強化編碼轉換。來源為乾淨 `C:\SGO\Stargate Online IE HK2`，反編譯 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 僅作唯讀參考；參考分支 `TestGoGo/feature/local-server-net8` 為 `0f37c5268ceeac1144ed13556f3aeb191b93711e`。

## 原版參考證據

- `CSGOCore::InitItemLib` `.text:00404D5E`～`0040647E` 讀 `Item\item.itd`，每筆有 1 byte 類型 tag。case 0／1／2／3／4／6 的 payload 長度分別為 1093／1200／1151／1149／1151／329 bytes；每個 case 將一個 `ushort` 寫入 `MotherItem+0x0A`（圖號）、下一個 `ushort` 寫入 `MotherItem+0x0C`（定義 ID）。不同 case 在原始記錄中的圖號位移分別是 10／13／6／6／6／6 bytes，均包含 tag。這是參考版本組語的 DIRECT 讀取結論；HK2 程式是否完全同版仍待比對。
- `CSGOCore::GetItemByID` `.text:00403AC1`～`00403B75` 以 `MotherItem+0x0C` 搜尋。地上物品建立流程 `.text:004D87CA`～`004D8845` 以物品 ID 查定義，讀 `MotherItem+0x0A`，**減 1**後交給 `CSGOItem::SetPicIndex`；後者 `.text:004DB690`～`004DB6A8` 存在物件 `+0xCC`。`CSGOItemDrawLogic::InitItem` `.text:004CCD17`～`004CCDA9` 載入 `Interface\ground.spr`，使用該圖索引。故新定義 ID 不可直接當影格索引。

## 可重跑稽核

```powershell
py tools/inspect_item_library.py 'C:\SGO\Stargate Online IE HK2' local-assets/item-library-audit.json
py -m unittest discover -s tools/tests -q
```

`tools/inspect_item_library.py` 使用既有 CDE 解封裝器，依上述固定長度逐筆掃描，未知 tag／截斷則停止；完整定義明細只寫入 git 忽略的私有 JSON，命令列只印摘要。HK2 `Item/item.itd` SHA-256 `585aeec6943830b2cde7eee42294014f0bdc80a1e852e7ca723a48da35547fb2`，解封裝後 8,236,329 bytes；**9,096／9,096 筆**剛好耗盡輸入，tag 計數：0=5239、1=1291、2=1、3=45、4=85、6=2435。共有 9,083 個不同定義 ID，13 個 ID 重複，重複項保持在私有報告，不擅自選一筆作正式定義。

`Interface/ground.spr` SHA-256 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6`，500 幀；`Interface/ground1.spr` SHA-256 `04af9e76170d71b49c4cc81442bcec5c9ea3d494b14922904de411fe647136b4`，另 500 幀。9,096 筆圖號都在 **1～765**：8,049 筆可落在 `ground.spr` 0～499 幀；另 1,047 筆若以連續分段假設計算，會落在 `ground1.spr` 0～264 幀；超出兩檔總範圍 0 筆。**`ground1` 的執行時選檔規則尚未在反編譯路徑證實**，因此報告稱 `source_candidate`，不得把這 1,047 筆標為已驗證可顯示。

這個結果修正前一輪的「`ground.spr` 500 幀就是完整物品圖」可能誤讀。先前 25／500 圖同場 benchmark 仍只測 `ground.spr` 本身，效能數據成立但覆蓋面不代表全部定義。正式 Map Asset Palette 需要先釐清 `ground1` 選檔、重複 ID 優先順序、地圖怪物掉落表與實際畫面；新物品 ID 及舊 `+N` 別名整理必須在固定映射與相容性 fixture 後另行處理。
