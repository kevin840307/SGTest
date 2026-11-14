# Runtime 方向與移動呈現契約

日期：2026-11-14。狀態：**DIRECTION RUNTIME FIX IMPLEMENTED / LIVE VISUAL HUMAN CHECK STILL REQUIRED**。

## 問題

使用者實機觀察：人物與怪物方向錯誤，移動會卡頓、順移，尤其畫面外圍更容易看到。

這不是單一 Server Heading 數字錯誤。Server 的八方位數值本來就是：

`+X=6, +X+Y=7, +Y=8, -X+Y=1, -X=2, -X-Y=3, -Y=4, +X-Y=5`。

Recovered 的 `GameConstants.DIR_STRINGS = [6,7,8,1,2,3,4,5]` 與 Server 一致。真正缺口有三個：

1. 舊 `EntityAtlas` 只在 requested clip 不存在時才查 alias；若 physical clip 剛好存在，就會蓋掉原版 action-specific DirectionMapping。
2. remote snapshot 每次都直接覆寫 facing，走路邊界 jitter 與 attack/hit snapshot 交錯會讓動畫反覆翻面。
3. Server AOI 固定 900px，小於 1440x1080 視窗加 Client cull margin 的最遠可視角落（約 1078px）；Actor 還在畫面邊緣時就可能 Despawn，之後重新 Spawn 到新座標，看起來像瞬移。

## 原版 / Recovered 證據

- 人物：`Paradise.exe.lst` `CSGOCharDrawLogic::DirectionMapping` `.text:004C263F～004C283A`；方位 5/6/7 重用 3/2/1 並鏡像，4/8 另依 action 分支。
- 怪物：`CSGOEnemDrawLogic::SetDirection` `.text:004CB708～004CB7E8`；不同 action index 對 2/4/6/8 的來源方位與 mirror 規則不同。
- 既有可重跑純函式：`tools/legacy_direction.py`。
- Recovered `client/scripts/game_constants.gd`：`DIR_STRINGS = ["6","7","8","1","2","3","4","5"]`。
- Recovered `monster_entity.gd::_direction_from_vector()`：45° sector，presentation 可用約 28° hysteresis 避免邊界翻向。
- Recovered `remote_player_state_policy.gd`：movement direction 不應在 attack/hit one-shot lock 期間覆蓋 action facing。
- Recovered `remote_player_motion.gd`：Server position 是 authority，但 visible position 用 bounded dead reckoning / catch-up，不重播每個封包已經過去的歷史路段。

## Runtime After

### 1. logical view 永遠優先於 physical clip

`tools/runtime_animation_semantics.py` 由 `sgtest.animation/v2` 編譯每個 requested logical clip：

- canonical physical clip
- `flip_h`
- verified ACT `timeline_ticks`
- loop
- full ACT key
- character draw-order metadata

`client/scripts/entity_atlas.gd` 先查 `logical_views[requested]`，只有沒有 normalized contract 的舊資產才 fallback 到舊 direct/alias 流程。

因此像：

- `EN010A_NS2` physical SPR 即使存在，logical facing 2 仍可依怪物原版規則重用 `EN010A_NS1`。
- `B-BODY_AS4` physical SPR 即使存在，人物 action AS 的 logical facing 4 仍依原版 `DirectionMapping` 使用 `AS3 + flip`。

`tools/rebuild_assets.ps1` 在 build population 後執行 `apply_normalized_runtime_semantics.py`，重建不會再把這個修正丟掉。

### 2. authoritative facing 與 presentation facing 分離

`client_replica.gd` 同時保留：

- `facing`：Server authoritative protocol state。
- `visual_facing`：畫面使用的方向。

走路時 `visual_facing` 由實際 authoritative position displacement 推導，並用 28° hysteresis；idle 保留最後方向。可靠 attack/hit/death event 取得 presentation facing 所有權，movement snapshot 不會在 one-shot 中途翻面。

`legacy_facing.gd` 集中維護八方位 convention，local prediction、remote replica 與 attack debug vector 不再各寫一份方向公式。

### 3. remote position 不因 attack/hit 直接 snap

舊 `position_at()` 在 attack/hit active 時直接回傳最新 `state.p`，會在進入戰鬥瞬間把可見角色跳到 authoritative endpoint。

現在 attack/hit 保持同一個位置 timeline；只有 spawn / death / respawn 是 teleport barrier。Remote walk 使用 120ms interpolation delay + 最多 250ms bounded extrapolation，presentation speed capped 650px/s。這不修改 Server position、碰撞、攻擊距離或拾取判定。

z-sort 改用 `rendered_players/rendered_monsters` 的 y，而不是 authoritative endpoint y，避免 body 平滑但 draw-order 仍在封包節奏跳動。

### 4. AOI 覆蓋最大正式視窗

Client 最大視窗 1440x1080；現行 draw cull margin 為 x=100、y=160，所以最遠可見角落約：

`sqrt((720+100)^2 + (540+160)^2) ≈ 1078px`。

Server 改為：

- enter radius = 1100px
- already-seen exit radius = 1250px

Player、monster、drop 與 reliable action event 使用同一 bounded interest policy。這不是全圖廣播；exit hysteresis 只避免邊界來回 Spawn/Despawn。

### 5. 預設 Render FPS = 60

`client/project.godot` 設 `application/run/max_fps=60`。Physics/Client input/Server simulation 仍是 30Hz；Render FPS 不改網路、戰鬥與 authority 節奏。

## 回歸

目前 deterministic 回歸：

- Python `tools/tests`: 89 PASS。
- Python normalized/runtime semantic/report tests: 18 PASS。
- C# Release build: 0 warning / 0 error。
- C# server tests: 350 PASS（含八方位與 AOI enter/exit boundary）。
- Godot 8 組 headless tests 全 PASS：animation definition、directions、monster visuals、navigation、prediction、replica、runtime asset store、wire。
- `test_directions.gd` 實際檢查 Map13 `EN010A_NS2 -> NS1`，及人物 `B-BODY_AS4 -> AS3 + flip`。
- `test_replica.gd` 檢查 walking vector direction、28° hysteresis、attack event facing ownership、attack 不 snap、death 仍 snap。

### 20-client 真多人 smoke

目前工作樹以預設生成資產、RTX 3050、1440×900、60FPS cap，執行 20 clients／300 monsters／1000 drops／40 秒：

- execution PASS，`source_files_unchanged_during_run=true`。
- 平均 60.00 FPS；frame P95 23.53ms、P99 25.58ms、max 34.82ms。
- draw-call P95 414。
- 20/20 client `snapshot_received`、20/20 `prediction_ready`，0 `ERROR`／`SCRIPT ERROR`。
- prediction `max_correction_px=0`，observed `max_pending=7`。
- Server 啟動大量連線/世界暖機時 tick=300 曾出現 P95/P99 30.15/83.69ms；穩定後 tick=600/900/1200 的 P99 為 5.90/7.32/7.40ms。啟動尖峰保留為後續維運/載入優化項目，不把它算成穩態通過而忽略。

此 smoke 能證明新 AOI／presentation smoothing 沒讓一般多人 frame/tick 穩態爆掉，但**不能用自動數值取代使用者實機肉眼確認方向與順移是否完全消失**。

## 還沒有宣稱完成的部分

- AS005 原版同版本實機逐格畫面仍 NOT VERIFIED；這次是反編譯 contract + deterministic runtime regression，不代替人工視覺比對。
- 完整 30/45/60/75/90 FPS、5/10/15 掛機、失焦/最小化/恢復、1/2/4/8 開 trace matrix 仍屬 RUNTIME206。
- ASSET204 async Runtime store 正由另一個 Session 在同一工作樹實作；方向/同步修正不得降低 network/state 消化頻率。

## 重跑

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
cd tools
py -m unittest test_animation_definition test_normalized_atlas_candidates test_runtime_animation_semantics -q
cd ..
.\.cache\dotnet\dotnet.exe build server\SGTest.Server.csproj -c Release
.\.cache\dotnet\dotnet.exe run --project server\tests\SGTest.Server.Tests.csproj -c Release
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_directions.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_replica.gd
```
