# HK2 怪物 ACT／SPR 來源與方向稽核

日期：2026-11-14。原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOEnemDrawLogic::InitAction`（`.text:004CBF8C`～`004CC111`）、`SetDirection`（`.text:004CB708`～`004CB7E8`）、`SetEnemSprite`（`.text:004CB833`～`004CB8FA`）。`.data:006051F4` 是 UTF-16LE `SWDAH` 動作索引表；`.data:00605200`～`0060520E` 分別是 `NS/NW/ND/FA/NH` SPR 後綴。`SetEnemSprite` 以敵人基本名稱、後綴及映射後的方向建立 `Enemy\%s_%s%d.spr` 路徑。這是參考版本的 DIRECT 反編譯證據，與 HK2 二進位是否完全一致仍待核對。

`tools/legacy_direction.py` 實作怪物專屬方向映射；`tools/audit_enemy_animations.py` 從乾淨 HK2 讀所有 Enemy ACT/SPR，為每個 `S/W/D/A/H` 與 1～8 邏輯方位驗證目標 SPR 存在、ACT 影格索引沒有超過 SPR 幀數，保留原始更新次數、循環、事件、來源 SHA 與鏡像旗標。它只做來源稽核，**不**把 ACT 的 raw duration 誤當毫秒，也不宣稱畫面已視覺校正。`H` 在原版 `InitAction(4)` 走特殊早退路徑，報告標記 `special_init_path`。

```powershell
py tools/audit_enemy_animations.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-animation-audit.json
py -m unittest tools.tests.test_audit_enemy_animations tools.tests.test_legacy_direction
```

HK2 結果：53 個 Enemy ACT 群組與 53 個同名 SPR 群組；51 組有標準 15 個動作／來源方向 SPR，`EN060A`、`EN061A` 僅各有 `ND1/ND3`。**50／53 組在參考版本的映射下，所有 ACT 索引均在實體 SPR 幀數範圍內且來源齊全**。例外共 3 組、72 筆方向級待釐清引用：`EN060A`／`EN061A` 的 `S/W/A/H` 路徑沒有同名實體 SPR（各 32 筆邏輯方位）；它們可能是原版不會播放這些動作的特殊種類，不能單憑數量判定遊戲素材缺失。`EN115A` 的 D 動作引用幀索引 1、2，而 `ND1/ND3` 各只有 1 幀（8 筆邏輯方位）。無法用 ACT 紀錄數與 SPR 幀數相等當條件：例如 `EN050B` 的 ACT S 有 10 個播放紀錄，但 `NS1` 僅 5 幀，因為索引 0～4 各重複兩次。1～8 邏輯方向會依動作重用並鏡像少數來源圖，不表示缺少原版方位素材。

待查：`EN060A/EN061A` 是否有原版特殊無動作路徑、`EN115A` 越界時原版如何顯示、H 特殊初始化、原版 Update 呼叫頻率及實際畫面／裝備遮擋。未通過前，這份報告只能作素材清單與 fail-closed 動畫建立依據，不能當最終怪物渲染定義。
