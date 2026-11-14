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

## HK2 Client.bin 的 ground page 直接證據

2009 HK2 `Client.bin` SHA-256 `9b45569a0f619b5bf7d99f122c6ed12ae44a1e54576686d88de4fae72dd13147` 內同時存在 `Interface\\ground.spr` 與 `Interface\\ground%d.spr`。反組譯 `0x004F4ECC～0x004F4F54` 直接顯示：物件內的 **zero-based** picture index 若小於 500，載入 `ground.spr`；否則以 signed divide 500 得商數，使用 `ground%d.spr` 格式化檔名，再以 `index - quotient*500` 作 frame index。2008 `SGOClient.bin`（SHA-256 `6e64368ad43605469246fb5692fd42d8eaa1a7ed0a13aa9a1efac518d46cb3be`）在 `0x004E7042～0x004E709E` 也有同一個 ÷500／format-string 流程。這是目標 HK2 binary 的 DIRECT 證據，不再依賴 2004 Paradise 對 `ground1` 的缺失。

因此 item.itd 的 one-based picture number 先減 1 後，正式公式為：`page = index // 500`、`frame = index % 500`；page 0 使用 `Interface/ground.spr`，page N>0 使用 `Interface/groundN.spr`。目前 HK2 實體只有 `ground.spr` 與 `ground1.spr`，兩者各 500 幀，SHA-256 分別為 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6`、`04af9e76170d71b49c4cc81442bcec5c9ea3d494b14922904de411fe647136b4`。9,096 筆真定義重跑後為 **8,049 → ground.spr、1,047 → ground1.spr、0 越界**；最大 picture number 765。工具現在輸出已驗證的 `source/source_frame`，並會掃描連續 `groundN.spr`、用實際 frame count 做 bounds check。

## 重複 ID 隔離

13 個 duplicate definition ID 中，`10305` 的兩筆都映射 `ground.spr` frame 298；其餘 **12 個 ID（32374、32375、32870～32879）有不同地上圖**。參考 Paradise 的 `GetItemByID 0x00403AC1～0x00403B75` 對已排序的 MotherItem array 呼叫 `CELArray<MotherItem*>::BinarySearch`；該 template 在 `0x00409AF0～0x00409B23` 直接委派 CRT `bsearch`。對相同 key，這不足以提供可移植的 first/last precedence 契約，因此工具明列 `duplicate_ground_visual_conflicts` 且維持 `duplicate_lookup_precedence_verified=false`。正式匯入不得任選其中一筆。

這個結果關閉先前的 `ground1` 選檔技術缺口，但先前 25／500 圖 benchmark 仍只涵蓋 `ground.spr`。下一步的物品 MapAssetPalette 應同時納入 ground/ground1 與真掉落共現；12 個 duplicate visual conflicts 在取得 HK2 同版本 lookup 實證前保持 quarantine。新物品 ID 及舊 `+N` 別名整理仍須依固定映射與相容性 fixture 另行處理。
