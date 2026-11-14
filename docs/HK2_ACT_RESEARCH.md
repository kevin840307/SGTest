# HK2 ACT 動作資料解析

日期：2026-11-14（Asia/Taipei）。證據等級：DIRECT + AUDIT。完整 timing 證據見 [HK2_ACTION_TIMING.md](HK2_ACTION_TIMING.md)。

`tools/inspect_act.py` 按原版 `xnAction::Load` 與 HK2 位元組解析 ASCII 動作鍵、循環旗標、frame index/raw_time、type 4 event 與結尾 FF。HK2 全部 **178/178 ACT** 完整解析，共 3,303 frame records、514 events；事件索引皆在所屬幀表範圍。

`Enemy/EN023A.ACT`：S=10、W=10、D=2、A=11、H=1；S/W 循環，D/A/H 結束。怪物 `SWDAH`→`NS/NW/ND/FA/NH` 及八方位來源重用/鏡像已由參考程式直接定位並對 HK2 53 組來源稽核，50 組完整、3 組特殊例外；詳 [HK2_ENEMY_ANIMATION_MAPPING.md](HK2_ENEMY_ANIMATION_MAPPING.md)。

## Default timing 已驗證

參考 Paradise 的 GameManager 是固定 50Hz update：`SystemSetFrame(50)`，`SystemSetFrame` 計算 `1000 / 50 = 20ms`；Map ClientObj → Character/Enemy → DrawLogic → DrawObj → `CELxnObj::Update` → `xnAction::Update` 的呼叫鏈可直接定位。

HK2 `Client.bin` 與 `SGOClient.bin` 又各自唯一命中相同的 40/25、duration scale、action decrement、fixed-loop 與 `SystemSetFrame(50)` 機器碼；因此 default timing 不再只是參考版假設。

預設速度：

```text
update_calls = floor(raw_time * 40 / 25)
default_duration_ms = update_calls * 20
```

例如 raw=6 → 9 ticks → 180ms；raw=3 → 4 ticks → 80ms。HK2 3,303 frame records 的 raw_time 範圍 1～70，沒有 0。

`SetSpeed` 可改 action speed，因此 pipeline 仍以 raw_time 為 canonical，並另外輸出 `legacy_default_update_calls` 與 `legacy_reference_default_duration_ms`；不得把 default ms 當成所有技能/特殊動作永遠固定的時間。

## Pipeline 驗證

- `inspect_act.py` / `convert_assets.py`：ACT schema v3，保留 50Hz/20ms/default speed=25 與 runtime override flag。
- `audit_action_timing_binary.py`：fail-closed 驗證 HK2 Client.bin / SGOClient.bin timing primitives。
- `audit_enemy_animations.py --timing-audit ...`：只有 binary audit PASS 才標記 default timing verified。
- `compile_enemy_atlas_catalog.py`：把 default ms 帶到每個播放步。
- Godot 4.7.2 headless：50 groups / 250 actions / 2,000 logical directions / 12,976 playback steps，`timing_verified=true`。
- `view_enemy_animation.py`：verified v2 catalog 用逐幀 default ms 播放；舊/unverified catalog 才 fallback 100ms。

## 尚待研究

人物 numeric action ID→ACT key、紙娃娃 source slot、`SetSpeed` 各 caller/封包語意、event token 的 hit/audio/FX callback，以及 AS005 原版實機逐格視覺比對仍未完成。Render FPS/移動速度不可自行改變 action timeline；正式規則見 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
