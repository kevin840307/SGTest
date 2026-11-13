# HK2 ACT 動作資料初步解析

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 原始位元組及 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 指定位址），語意推論另列 HYPOTHESIS。沒有把原件或反編譯文本複製入 Git。

`tools/inspect_act.py` 按原版 `xnAction::Load` 與 HK2 位元組解析：ASCII 動作鍵、type 3 的 1-byte 循環旗標、type 2 幀表、逐幀的 frame index / raw duration 兩個 32-bit 整數、可選 type 4 事件（frame index + NUL 結尾 token），末尾 `FF`。HK2 全部 178 個 `.ACT` 皆完整解析；共 3,303 個幀紀錄、514 個事件，事件索引全在各自幀表內。

可重跑：`py tools/inspect_act.py 'C:\SGO\Stargate Online IE HK2'`；`py -m unittest tools.tests.test_inspect_act`。

`Enemy/EN023A.ACT` 的摘要：S=10 幀、W=10、D=2、A=11、H=1；S/W 循環，D/A/H 播完結束。S/W 前幾幀 raw duration=6，H=3。相鄰 `EN023A_NS1.spr`、`EN023A_NW1.spr` 各有 10 幀，與 S/W 數量吻合，但**仍不能單憑此證明** S/W 字義、方向鏡像和其他動作與 SPR 的完整對應。事件 token 保留原文；token 如何對應音效、命中或其他 callback 仍待查。

`Paradise.exe.lst` 的 `xnAction::Load`（`.text:005574CB`～`0055757B`）逐 tag 讀入上述欄位，把事件放在指定 frame index。`xnAction::Update`（`.text:005576FA`～`0055777D`）每次呼叫將剩餘計數減一；到期後前進一幀，循環旗標為 1 時回到幀 0，否則回傳結束狀態。Load 在 `.text:005574A3`、`005574AA` 設 40 與 25；`SetAction` 的 `.text:0055769D`～`005576A5` 以及 `Update` 的 `.text:0055775E`～`0055776A` 將 raw duration 乘 40 再除播放速度（預設 25，整數截斷），因此預設 6→9 次 Update 呼叫。`SetSpeed`（`.text:005576B0`～`005576BB`）可改速度。這些是**Update 呼叫次數**，還不能直接當毫秒；需追呼叫頻率及原版播放速度變更點。

原版反編譯直接證據：`Paradise.exe.lst` `.text:004BDD40` 建立 `Player\%s\%s-Death.ACT` 路徑，`.text:004BDD77` 經虛函式讀取，`.text:004BDD84` 設死亡動作 callback；`.text:004B74C2` 也建立武器特效 ACT 路徑。這證明 ACT 用於動作流程，但時間倍率與圖層順序仍要追該函式實作。上述位址來自參考版本，與 HK2 二進位是否完全相同尚未校對。

人物方向鏡像已另外查到原版 `CSGOCharDrawLogic::DirectionMapping`；見 `docs/HK2_DIRECTION_MAPPING.md`。下一步：追 ACT `Update` 呼叫頻率、action ID 與 ACT key 對應、SIF／draw-order DAT、比對原版錄影。直到完成前，Godot 只能準確讀到影像幀，不能聲稱角色動作已還原。
