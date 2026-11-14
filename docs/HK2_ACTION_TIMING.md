# HK2 ACT 時間基準與固定 Update 證據

日期：2026-11-14。證據等級：**DIRECT + AUDIT**。原始 binary 與反編譯文本只做唯讀研究，不提交 Git。

## 結論

HK2 的 `Client.bin` 與 `SGOClient.bin` 都直接保留與參考 Paradise 同系的 ACT timing primitives：

- GameManager 設定 `SystemSetFrame(50)`。
- `SystemSetFrame` 以整數 `1000 / frameRate` 產生固定步進，因此為 **20ms / 50Hz**。
- `xnAction` 預設 numerator=40、speed=25。
- 設定/切換幀時：`remaining_updates = floor(raw_time * 40 / speed)`。
- `xnAction::Update` 每次固定 Update 將 remaining count 減一，到期後切下一幀。
- 因此預設速度下：`default_duration_ms = floor(raw_time * 40 / 25) * 20`。
- HK2 現有 3,303 個 ACT frame record 的 raw_time 最小 1、最大 70，沒有 raw_time=0。

例：raw=6 → 9 fixed updates → **180ms**；raw=3 → 4 updates → **80ms**。

這是 **default timing**。原版 `SetSpeed` / action-speed 路徑可改 denominator，因此 runtime 必須保留 raw_time 與 update-count；不能只存死的毫秒值。

## 參考 Paradise 呼叫鏈

`C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst`：

1. `CSGOCore::AppInit .text:004017F6` 以 50 呼叫 `CELGameManager::SystemSetFrame`。
2. `SystemSetFrame .text:0050751A` 計算 `1000 / frameRate` 存入 fixed-step 欄位。
3. `CELSystemLoop .text:0050739D` 用 `GetTickCount` 累積 elapsed；accumulator >= fixed-step 時呼叫 `CELGameManager::Update` 並扣除 fixed-step。Draw 有獨立路徑。
4. `CELGameManager::Update .text:0050719D` 更新 component。
5. `CSGOMap::Update .text:004CF1B2` → `UpdateCliObjList .text:004D0382`，逐一呼叫 ClientObj vtable +0x0C。
6. `CSGOChar` vtable `.rdata:005C3664` 與 `CSGOEnem` vtable `.rdata:005C3794` 的 +0x0C 均直接是各自 `Update`。
7. `CSGOChar::Update .text:004BEBBF` → `CSGOCharDrawLogic::Update .text:004C4EE9` → `ActionUpdate .text:004C4DA3` → `CxnDraw::GetObjFromIndex` → DrawObj vtable +0x0C Update。
8. `CSGOEnem::Update .text:004C9E57` → `CSGOEnemDrawLogic::Update .text:004CB5A4` → DrawObj vtable +0x0C Update。
9. `CELxnObj::Update .text:005419A7` 直接呼叫 `xnAction::Update`。
10. `xnAction::Load/SetAction/Update/SetSpeed` 位於 `.text:005574A3`～`0055777D`，保存 40/25、duration scale 與 decrement 規則。

因此參考版可直接證實：「每個 game fixed update 消耗一個 action update count」，而不是 render frame 驅動動畫。

## HK2 binary 同版 timing primitive 稽核

工具：`tools/audit_action_timing_binary.py`。它只讀 PE，fail-closed 驗證唯一 byte signature、PE RVA/rel32 call，以及 `SystemSetFrame` call 前的 push 50。

| binary | SHA-256 | 40/25 | decrement | duration scale | fixed loop | SystemSetFrame(50) |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Client.bin | `9b45569a0f619b5bf7d99f122c6ed12ae44a1e54576686d88de4fae72dd13147` | 1 | 1 | 2 | 1 | 1 |
| SGOClient.bin | `6e64368ad43605469246fb5692fd42d8eaa1a7ed0a13aa9a1efac518d46cb3be` | 1 | 1 | 2 | 1 | 1 |

Client.bin file offsets：action defaults `0x1AE323`、decrement `0x1AE57A`、duration scale `0x1AE51D/0x1AE5E2`、fixed loop `0x135A10`、SystemSetFrame core `0x135BA7`。

SGOClient.bin file offsets：action defaults `0x1967A3`、decrement `0x1969FA`、duration scale `0x19699D/0x196A62`、fixed loop `0x124420`、SystemSetFrame core `0x1245B7`。

兩個 binary 的 timing primitive 與 fixed-loop 結構均唯一命中；audit JSON 寫入 gitignore 的 `reports/hk2-action-timing-audit.json`。

## Pipeline 契約

`inspect_act.py` / `convert_assets.py` ACT schema v3 每一 frame 保留：

- `raw_time`
- `legacy_default_update_calls`
- `legacy_reference_default_duration_ms`

並在文件層保留 `legacy_fixed_update_hz=50`、`legacy_fixed_update_ms=20`、`legacy_default_action_speed=25`、`runtime_speed_can_override=true`。

`audit_enemy_animations.py --timing-audit ...` 只有收到通過的 binary audit 才把 `default_timing_verified=true` 傳到 Enemy catalog。Godot probe 對 verified catalog 要求所有播放步具有 default ms。

本機真素材結果：178/178 ACT 解析 PASS；50 組可用怪物 catalog 為 250 actions / 2,000 logical directions / 12,976 playback steps，Godot 4.7.2 headless 回報 `timing_verified=true`。

## Runtime 規則

- 動畫時間軸依 server/action timeline + verified frame duration 取樣，**不依 Render FPS 推進**。
- 玩家/怪物的世界移動速度不得自動乘到動畫播放速度；高速移動不能因此讓腳程、機甲動畫變快。
- 只有未來確認的明確 action-speed 語意才可改動畫 timeline speed。
- 30/60/120 FPS 只改呈現採樣率，不改 NetworkPump、StateReducer、Server tick 或 action 時間。
- Hit/Death/Teleport 等 barrier 仍由 server event/action id 決定，不因動畫未播完阻塞 authoritative state。

## 尚未關閉

- 人物 numeric action ID ↔ ACT key/裝備 source slot 的完整映射。
- `SetSpeed` 在各戰鬥/技能/特殊動作的所有 caller 與封包欄位語意。
- ACT event token 的命中／音效／特效 callback 精確語意。
- AS005 同版本原版畫面逐格校驗。
