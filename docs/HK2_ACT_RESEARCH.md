# HK2 ACT 動作資料初步解析

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 原始位元組及 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 指定位址），語意推論另列 HYPOTHESIS。沒有把原件或反編譯文本複製入 Git。

`tools/inspect_act.py` 按可觀察的欄位邊界解析：ASCII 動作鍵、4-byte 未知值、2-byte 表頭、逐幀的兩個 32-bit 整數、可選事件 payload，以及末尾 `FF`。HK2 全部 178 個 `.ACT` 皆能完整走到結尾；測試 fixture 覆蓋循環旗標、幀資料、事件和截斷拒絕。

可重跑：`py tools/inspect_act.py 'C:\SGO\Stargate Online IE HK2'`；`py -m unittest tools.tests.test_inspect_act`。

`Enemy/EN023A.ACT` 的原始摘要：S=10 幀、W=10、D=2、A=11、H=1；S/W 的 raw flag=1，D/A/H=0。S/W 前幾幀的第二個整數為 6，H 為 3。相鄰 `EN023A_NS1.spr`、`EN023A_NW1.spr` 各有 10 幀，與 S/W 數量吻合，但**仍不能單憑此證明** S/W 字義、時間單位、方向鏡像和其他動作與 SPR 的完整對應。事件只保留原始 payload hex；沒有把其中字串當成音效或命中時點。

原版反編譯直接證據：`Paradise.exe.lst` `.text:004BDD40` 建立 `Player\%s\%s-Death.ACT` 路徑，`.text:004BDD77` 經虛函式讀取，`.text:004BDD84` 設死亡動作 callback；`.text:004B74C2` 也建立武器特效 ACT 路徑。這證明 ACT 用於動作流程，但時間倍率與圖層順序仍要追該函式實作。上述位址來自參考版本，與 HK2 二進位是否完全相同尚未校對。

下一步：追 ACT 播放器對 raw_time 的運算、調查 `Player/*` 的方向與 SIF／draw-order DAT、比對原版錄影，將確定的欄位併入轉換器的動畫 metadata。直到完成前，Godot 只能準確讀到影像幀，不能聲稱角色動作已還原。
