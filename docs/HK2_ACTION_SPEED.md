# HK2 / Paradise Action Speed 與世界移動速度分離

日期：2026-11-14。狀態：**REFERENCE CALLER MAPPING VERIFIED / HK2 ACTION SETTER VERIFIED / HK2 CALLER MAPPING PARTIAL**。

## 結論

原版存在兩種不同的「速度」，不能混成同一個倍率：

1. **角色世界移動速度**：`CSGOChar::SetSpeed`。
2. **SPR/ACT 動畫速度**：`CELxnObj::SetActionSpeed` → `xnAction::SetSpeed`。

參考 Paradise 的 `CSGOChar::SetSpeed .text:004BF3B5` 只把參數寫入角色欄位 `[this+0x17C]`，函式內沒有呼叫 `SetActionSpeed` 或 `xnAction::SetSpeed`。因此「角色跑得更快」在原版不是自動把 ACT 播放倍率一起提高。

相反，ACT action speed 是明確的另一條 API：`CELxnObj::SetActionSpeed .text:00541BF0` 取得目前 `xnAction` 後呼叫 `xnAction::SetSpeed(int)`。

## VTable 直接證據

參考版兩個實際動畫物件 vtable 都把 offset `+0x2C` 定義為 action speed：

- `CDDxDrawObj vftable 0x005C4360` → `+0x2C = 0x005C438C = CELxnObj::SetActionSpeed`
- `CELxnObj vftable 0x005C5320` → `+0x2C = 0x005C534C = CELxnObj::SetActionSpeed`

同一 layout 的 `+0x24` 是 `InitAction`，因此「先呼叫 +0x24，附近 push immediate，再呼叫 +0x2C」可用作 reference action-speed caller 的保守識別模式。

## 可重跑 reference caller audit

`tools/audit_action_speed_reference.py` 只讀 `Paradise.exe.lst`，驗證：

- `CSGOChar::SetSpeed` 是單純 world-speed field store，沒有 action-speed call。
- `CELxnObj::SetActionSpeed` 確實呼叫 `xnAction::SetSpeed`。
- CDDxDrawObj / CELxnObj 的 SetActionSpeed vtable slot 都是 `+0x2C`。
- 只有在同一短序列內先出現 `InitAction(+0x24)`，才把後面的 `push imm → +0x2C` 列為 action-speed immediate caller。

目前掃到 **31 個 caller sequence / 28 個函式**；所有 immediate speed 值都只有 **50**，沒有其他立即常數。

涉及範圍包含：

- Battle receive / skill / blood / miss effects
- Character effect、race、guard、level-up、item-mix、summon
- Enemy / Pet 初始化與 miss effect
- Robot / mecha skill、summon、unsummon、outlook
- Riding creature / riding machine special action
- `RefreshCharAct` / `UpdateSitMode`

因此「speed=50 只屬於 FX」也太簡化；正確契約是 **只有明確 action-speed caller 才能改 ACT speed**。

私有 audit JSON：`reports/action-speed-reference-audit.json`（gitignore）。

## HK2 action-speed primitive

HK2 binary 的 `xnAction::SetSpeed` 機器碼 signature：

```text
test ecx,ecx
jz ...
mov eax,[esp+4]
mov [ecx+18h],eax
ret 4
```

唯一命中：

- `Client.bin @ file offset 0x1AE530`
- `SGOClient.bin @ file offset 0x1969B0`

這已由 `tools/audit_action_timing_binary.py` 納入 fail-closed timing audit。也就是 HK2 本身直接保留「action speed denominator」這個機制。

目前尚未把 HK2 binary 中每個 **caller** 對回函式名稱；所以 caller 名稱清單仍標記為 Paradise reference lineage，而不是 HK2 同版函式位址。

## 精確整數時間語意

原版 frame counter：

```text
counter = floor(raw_time * 40 / action_speed)
```

每個 20ms fixed update：

```text
counter -= 1
if counter <= 0:
    advance_frame()
```

所以實際「至少要經過幾個 fixed update 才能換幀」為：

```text
effective_ticks = max(1, floor(raw_time * 40 / action_speed))
visible_default_ms = effective_ticks * 20
```

預設 speed=25 時，HK2 raw_time 最小為 1，因此 counter 本身就至少 1。speed=50 時，例如 raw_time=1：

- stored counter = 0
- 下一個 fixed update 才會 advance
- effective visible hold = 1 tick = 20ms

不能把 counter=0 誤實作為「同一瞬間直接跳過」。

## Runtime Contract

正式 Client 必須遵守：

- **World movement speed 不直接改 ACT playback rate。**
- **Render FPS / UI FPS 不直接改 ACT playback rate。**
- ACT timeline 基於 server/action start time + fixed legacy frame durations。
- 明確的 action-speed 值可以改該 action timeline；其運算使用 legacy integer division + minimum one fixed tick。
- 速度切換若發生在 action 已播放中，要保留「SetSpeed 只改 denominator、不立即重算當前 remaining counter」這個原版語意，除非後續 HK2 caller 研究證明特定路徑先重新 SetAction。
- 移動內插速度、走路動畫播放率是獨立變數；若未收到 action-speed 語意，角色/機甲高速移動仍維持該 ACT 的原始時間軸。
- 不能拿 entity speed 去做 `AnimationPlayer.speed_scale` 或 equivalent 的隱式乘法。

## 尚待關閉

- HK2 Client.bin / SGOClient.bin 的 31 類 caller 對應位址與函式級 mapping。
- 是否有非 immediate（從 packet/data 欄位傳入）的 action-speed caller。
- 人物 numeric action ID / paper-doll action 對應。
- ACT event callback 的 hit/audio/FX 精確觸發語意。
