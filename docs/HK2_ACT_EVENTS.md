# HK2 ACT Event Callback 語意與時間軸

日期：2026-11-14。狀態：**REFERENCE CALLBACK SEMANTICS VERIFIED / HK2 EFFECTIVE EVENT CENSUS VERIFIED / SERVER AUTHORITY CONTRACT ACCEPTED**。

本文件只記錄事件 metadata、函式位址與行為結論；Data1、ACT、音訊與反編譯原文不提交公開 Git。

## 1. 有效 HK2 ACT 視圖

工具 `tools/audit_act_events.py` 以 `Data1.dat` 為 base，再套用 loose file override，與原版「封包檔 + 實體檔覆寫」使用方式一致。

| 指標 | 數量 |
| --- | ---: |
| Data1 ACT | 444 |
| loose ACT | 178 |
| loose override | 9 |
| 最終有效 ACT | 613 |
| actions | 1,432 |
| events | 1,683 |
| exact token 種類 | 163 |
| audio-candidate events | 967 |
| control events | 716 |

控制 token：

| token | 次數 | 參考 callback 語意 |
| --- | ---: | --- |
| `+` | 568 | attack/receive/hurt visual cue；依 callback 類型套接收效果、Damage/Block 視覺資料 |
| `-` | 144 | Character attack callback 的 projectile / `CBulletObj` 建立 cue |
| `*` | 2 | Battle SFX callback 的 hurt-effect cue；不走一般音效 |
| `$` | 1 | Character callback 的 riding-mode/control cue |
| `#` | 1 | Character callback 明確 consume，沒有一般音效 |

因此 `$ / # / *` 在有效 HK2 資料中確實存在，不只是舊程式遺留分支。

## 2. Event 是「進入 frame」callback

參考 `xnAction` 直接證據：

- `xnAction::SetEventCallback .text:005575F0` 保存 callback/context。
- `xnAction::SetAction .text:00557610` 選到 action 後，**在 frame 0 圖與 duration 載入前**先檢查 frame 0 event 並 callback。
- `xnAction::Update .text:005576E0` 的 counter 到期後先增加 frame index，再取該 frame event callback，之後載入該 frame 圖與 duration。

所以 `at_frame_raw=N` 的語意是：

```text
進入 action frame N 時觸發 event
```

frame 0 event 的 default offset = 0ms。frame N 的 default offset = 前 N 個 frame 的 audited default duration 總和。

ACT schema v4 每個 event 保留：

- `at_frame_raw`
- `fires_on_enter_frame=true`
- `legacy_reference_default_offset_ms`
- 原始 `token`
- raw payload hex

dynamic `SetActionSpeed` 時仍以 frame index 為 canonical；default offset 只是 speed=25 的 reference offset。

## 3. Reference callback 語意

工具 `tools/audit_act_event_reference.py` fail-closed 驗證以下函式。

### Battle callback

`CBattleSys::BattleEffSFXCallback .text:004BA372`

- `*` / `+`：依 target runtime type 觸發 Hurt Effect 類路徑。
- 其他字串：交給 `CSGOMap::RandomPlaySound`。

### Character attack callback

`CSGOCharDrawLogic::AttEffAxtionCallback .text:004C5B63`

- `+`：走 target list；建立/套用 receive effect，消費已準備的 Damage / Block 視覺資料。
- `-`：建立 `CBulletObj` projectile 視覺物件並加入地圖。
- `$`：riding-mode/control；可能同步 riding 狀態、重設 action、更新玩家攻擊範圍。
- `#`：直接 consume，不走一般音效。
- 其他：`RandomPlaySound(token)`。

### Enemy / Pet callbacks

- `CSGOEnemDrawLogic::EnemEffAxtionCallback .text:004CB96D`
- `CSGOPetDrawLogic::PetEffAxtionCallback .text:004DFBE2`

`+` 會在 action timing 點套 receive effect / Damage / Block 視覺資料；其他 token 走 RandomPlaySound。

**同一 token 的語意取決於掛載哪個 callback。** 不能只靠 token 字串建立全域 switch。

## 4. RandomPlaySound 規則

`CSGOMap::RandomPlaySound .text:004D09C3`：

1. 用 delimiter bytes `2C 0A 00`，即 comma + newline，對 token 做 `strtok`。
2. 一個以上候選時使用 `rand() % count` 選 **一個**。
3. 組成 `Audio\\SE\\%s.WAV`。
4. 交給 `CELSound::PlayGlobalSource`。

所以：

```text
SE0002,SE0003,SE0004
```

是「隨機播放其中一個」，不是同時播放三個。

`EN0049` 若落到 default audio branch，也只是 `Audio\SE\EN0049.WAV` 的 stem，不是 Enemy ID command。

## 5. 音效素材 anomaly

有效 ACT event 全量檢查對照 Data1 + loose `Audio/SE/*.WAV`，只有四個 audio stem 找不到實體 WAV：

- `SE0003.SE0004`：6 次，來自 Data1 Dark weapon effect token `SE0003.SE0004,SE0005`。
- `SE0008.SE0006`：6 次，來自 Data1 Fire weapon effect token `SE0008.SE0006,SE0007`。
- `SE002`：1 次，`Enemy/EN072A.ACT` 的 `SE002,SE0003,SE0004`。
- `SE0163`：6 次，Normal_Effect17～22 類 loose ACT。

原版 RandomPlaySound 不把 `.` 當 delimiter，因此前兩個會真的嘗試不存在的 `SE0003.SE0004.WAV` / `SE0008.SE0006.WAV`。這些保留為素材 anomaly，不自行修正成猜測檔名。

Runtime 遇到缺少的非必要聲音：
- 記一次 bounded diagnostic。
- 靜默略過音效。
- 不影響 action state、combat、movement 或 event queue。

## 6. 新 Client / Server Runtime Contract

ACT event 是 **presentation timeline cue**，不是 Server authority。

### 必須如此

```text
Server authoritative combat/action result
  -> Client OrderedEventReducer
  -> ActionTimeline / VisualSnapshot
  -> ACT frame timeline
  -> enter-frame event cue
       +  -> consume already-authoritative combat visual payload
       -  -> spawn projectile visual
       audio -> choose/play local SFX
       control -> presentation/control adapter with explicit allowlist
```

- Damage、Miss、Block、Death、HP、擊殺、掉落全部由 Server authoritative event/state 決定。
- ACT `+` **不能**呼叫本地 damage formula、不能扣 HP、不能自行判定 hit。
- 若 Server combat result 已先到，但 visual 尚未走到 `+`，可保存 bounded visual payload，等 cue 顯示；state 立即套用，不能被動畫阻塞。
- 若 cue 先到但 authoritative visual payload 尚未到，Client 不猜 damage；可等待短 bounded visual window 或略過效果，之後用最新 state 收斂。
- Death / Teleport / Despawn 是 barrier，可中止尚未播放的 presentation cues。
- event queue 不由 Render FPS 驅動；VisualClock 只在渲染時對同一 ActionTimeline 取樣。
- 30/60/120 FPS、失焦、AFK 不能改變可靠 combat event 的處理順序。
- 音效隨機選擇只影響 presentation；不進 state hash，不影響同步。

## 7. 真素材驗證

50 組可編譯 Enemy catalog 重新以 ACT v4 建立：

- 50 groups
- 250 actions
- 2,000 logical facings
- 12,976 playback steps
- 176 events
- Godot 4.7.2 headless：`timing_verified=true`
- Godot 4.7.2 headless：`event_timing_verified=true`

三個既有特殊 Enemy group 仍因素材來源/索引問題隔離；event schema 沒有新增新的 quarantine。

## 8. 尚待研究

- HK2 binary callback caller 的函式級映射（目前 callback 語意以 Paradise lineage 直接證據為主）。
- Character numeric action ID / paper-doll 動作 mapping。
- `$` control 在 HK2 同版本的完整 packet/state contract。
- projectile 的 Server action/result 與 local visual projectile 對應 key。
- AS005 原版同版本畫面/錄影逐格校驗。
