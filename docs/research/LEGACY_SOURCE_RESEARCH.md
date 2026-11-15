# Historical SGTest reference — Legacy Source Research

> REFERENCE ONLY. Archived from the pre-rebuild codebase on 2026-11-15.
> These are exact historical texts, NOT the current client/server implementation or tests.
> Use the current ARCHITECTURE.md and todo.txt for decisions.
> Original individual documents remain recoverable from git commit f755a47.

## Contents

- [HK2_ACTION_PAGE_LOCALITY.md](#source-hk2-action-page-locality-md)
- [HK2_ACTION_SPEED.md](#source-hk2-action-speed-md)
- [HK2_ACTION_TIMING.md](#source-hk2-action-timing-md)
- [HK2_ACT_EVENTS.md](#source-hk2-act-events-md)
- [HK2_ACT_RESEARCH.md](#source-hk2-act-research-md)
- [HK2_ASSET_VIEWER.md](#source-hk2-asset-viewer-md)
- [HK2_ATLAS_CANDIDATES.md](#source-hk2-atlas-candidates-md)
- [HK2_ATOMIC_BALANCED_ATLAS.md](#source-hk2-atomic-balanced-atlas-md)
- [HK2_DATA1_ARCHIVE.md](#source-hk2-data1-archive-md)
- [HK2_DIRECTION_MAPPING.md](#source-hk2-direction-mapping-md)
- [HK2_DRAW_ORDER.md](#source-hk2-draw-order-md)
- [HK2_ENEMY_ANIMATION_MAPPING.md](#source-hk2-enemy-animation-mapping-md)
- [HK2_ENEMY_ATLAS_CATALOG.md](#source-hk2-enemy-atlas-catalog-md)
- [HK2_GROUND_ART_BENCHMARK.md](#source-hk2-ground-art-benchmark-md)
- [HK2_INVENTORY.md](#source-hk2-inventory-md)
- [HK2_ITEM_LIBRARY_MAPPING.md](#source-hk2-item-library-mapping-md)
- [HK2_MAP_ASSET_PALETTE.md](#source-hk2-map-asset-palette-md)
- [HK2_MAP_REFERENCES.md](#source-hk2-map-references-md)
- [HK2_MIXED_ATLAS_BENCHMARK.md](#source-hk2-mixed-atlas-benchmark-md)
- [HK2_PER_ENEMY_ATLAS_SIZES.md](#source-hk2-per-enemy-atlas-sizes-md)
- [HK2_SIF_RESEARCH.md](#source-hk2-sif-research-md)
- [HK2_SPR_POC.md](#source-hk2-spr-poc-md)
- [HK2_UI_REFERENCES.md](#source-hk2-ui-references-md)
- [LEGACY_ASSET_EVIDENCE_20261113.md](#source-legacy-asset-evidence-20261113-md)
- [LEGACY_REFERENCE.md](#source-legacy-reference-md)

---

<a id="source-hk2-action-page-locality-md"></a>

## HK2_ACTION_PAGE_LOCALITY.md

*Historical source: `docs/HK2_ACTION_PAGE_LOCALITY.md` — SHA-256 `4242e5cefc25470576c815eb97639af27f23247700022f3677a57dc57c9f4fb6`*

# HK2 動作／方向 Atlas locality A/B（2026-11-14）

原始／衍生媒體只留在 gitignore 的 local-assets。此文件記錄打包與 GPU 實測，不代表原版視覺／動畫毫秒已完全驗證。

## 問題

舊 dense frame packing 在最初 50 組 loose Enemy 候選的 2,000 個 action×logical-facing 中有 173 個跨頁。單純每個 SPR 一張 texture 可消除大多數跨頁，但 50 組曾達 750 頁，低 unique 場景 draw calls 上升，因此不採用。

## Atomic MaxRects v3

tools/build_atlas_groups.py --source-locality 現在先把每個原始 SPR 的 frames 打成不可拆 atomic block，再用 deterministic no-rotation MaxRects-style placement 把多個完整 block co-pack 到同一頁。方向重用／flip 只存在 catalog metadata，不預先複製鏡像圖。tools/audit_action_page_locality.py --require-single-page 作嚴格 Gate。

對最初 50 組 loose candidate，v3 為 119 pages / 466,182,124 RGBA bytes，2,000/2,000 action-facing 單頁。1440×900、300 monster、RTX 3050／Godot 4.7.2 OpenGL 三回合 A/B：50 種全載 dense 133 pages、load median 1289.209 ms、frame p95 7.955 ms、draw p95 372、Video Mem 595,191,887；atomic v3 119 pages、1342.386 ms、7.983 ms、372、639,997,855。Frame/draw calls 無實質退化，但全世界 50 種常駐多約 45 MB Video Memory，故正式策略必須搭配 map palette，而不是一次載完。8 種／300 monster 亦為 dense draw p95 286、atomic 287，屬同量級；load/cache 差異不作顯著效能宣稱。

## 真實已知地圖內容

Data1 + loose overlay 的 29 種實際地圖怪物：atomic 1024 為 32 pages / 62,287,496 RGBA bytes / 1,160/1,160 action-facing 單頁；dense 1024 為 32 pages / 63,770,056 RGBA bytes / 9/1,160 action-facing 跨頁。Godot headless 驗證 29 groups / 145 actions / 1,160 directions / 8,792 playback steps PASS。

Map13 詳細 GPU A/B 與 runtime palette 見 HK2_MAP_ASSET_PALETTE.md。正式 StreamingManager、多人網路與原版視覺仍 NOT VERIFIED。

---

<a id="source-hk2-action-speed-md"></a>

## HK2_ACTION_SPEED.md

*Historical source: `docs/HK2_ACTION_SPEED.md` — SHA-256 `4477b23ddd78d4e1d9e8b03f3c58cc99a577d7893d5000d7f3ce64368487aeb7`*

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

---

<a id="source-hk2-action-timing-md"></a>

## HK2_ACTION_TIMING.md

*Historical source: `docs/HK2_ACTION_TIMING.md` — SHA-256 `bdbd545a009ebfe0d7a54806319317e5314ff31dba65d04477c70caf0391760d`*

# HK2 ACT 時間基準與固定 Update 證據

日期：2026-11-14。證據等級：**DIRECT + AUDIT**。原始 binary 與反編譯文本只做唯讀研究，不提交 Git。

## 結論

HK2 的 `Client.bin` 與 `SGOClient.bin` 都直接保留與參考 Paradise 同系的 ACT timing primitives：

- GameManager 設定 `SystemSetFrame(50)`。
- `SystemSetFrame` 以整數 `1000 / frameRate` 產生固定步進，因此為 **20ms / 50Hz**。
- `xnAction` 預設 numerator=40、speed=25。
- 設定/切換幀時：`remaining_updates = floor(raw_time * 40 / speed)`。
- `xnAction::Update` 每次固定 Update 先將 remaining count 減一，`<=0` 才切下一幀。
- 因此精確邏輯是 `effective_ticks = max(1, floor(raw_time * 40 / speed))`；預設 speed=25 時 HK2 raw_time>=1，所以等同 `floor(raw_time * 40 / 25)`。
- 預設速度下：`default_duration_ms = effective_ticks * 20`。
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

| binary | SHA-256 | 40/25 | decrement | SetSpeed | duration scale | fixed loop | SystemSetFrame(50) |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Client.bin | `9b45569a0f619b5bf7d99f122c6ed12ae44a1e54576686d88de4fae72dd13147` | 1 | 1 | 1 | 2 | 1 | 1 |
| SGOClient.bin | `6e64368ad43605469246fb5692fd42d8eaa1a7ed0a13aa9a1efac518d46cb3be` | 1 | 1 | 1 | 2 | 1 | 1 |

Client.bin file offsets：action defaults `0x1AE323`、decrement `0x1AE57A`、`xnAction::SetSpeed` `0x1AE530`、duration scale `0x1AE51D/0x1AE5E2`、fixed loop `0x135A10`、SystemSetFrame core `0x135BA7`。

SGOClient.bin file offsets：action defaults `0x1967A3`、decrement `0x1969FA`、`xnAction::SetSpeed` `0x1969B0`、duration scale `0x19699D/0x196A62`、fixed loop `0x124420`、SystemSetFrame core `0x1245B7`。

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
- 只有明確 action-speed 語意才可改動畫 timeline speed；參考 caller/vtable 與 HK2 setter 證據見 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。
- dynamic speed 必須沿用 legacy integer division 與 minimum one fixed tick；例如 raw=1、speed=50 的 stored counter=0，但仍在下一個 20ms Update 才前進。
- 30/60/120 FPS 只改呈現採樣率，不改 NetworkPump、StateReducer、Server tick 或 action 時間。
- Hit/Death/Teleport 等 barrier 仍由 server event/action id 決定，不因動畫未播完阻塞 authoritative state。

## 尚未關閉

- 人物 numeric action ID ↔ ACT key/裝備 source slot 的完整映射。
- HK2 binary 內 action-speed caller 的函式級 mapping／packet/data-driven non-immediate speed 語意；參考版 immediate caller 已在 HK2_ACTION_SPEED.md 稽核。
- ACT event token 的命中／音效／特效 callback 精確語意。
- AS005 同版本原版畫面逐格校驗。

---

<a id="source-hk2-act-events-md"></a>

## HK2_ACT_EVENTS.md

*Historical source: `docs/HK2_ACT_EVENTS.md` — SHA-256 `79ec7084189751c6808d3286f1eb3619d23c1915c1f5a47cc84f5742e0f7fefb`*

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

---

<a id="source-hk2-act-research-md"></a>

## HK2_ACT_RESEARCH.md

*Historical source: `docs/HK2_ACT_RESEARCH.md` — SHA-256 `4a8131e5d8e9785e397a8fe10fe4d47df11d0320c099d368ace9d037d1f65f95`*

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

---

<a id="source-hk2-asset-viewer-md"></a>

## HK2_ASSET_VIEWER.md

*Historical source: `docs/HK2_ASSET_VIEWER.md` — SHA-256 `5176fff6d513182cd6ba6e907eacd1ff5b73b511580e93811dc1a64cd4e4c57c`*

# HK2 素材逐格檢視器

`tools/view_legacy_asset.py` 可直接打開乾淨客戶端的一個 `.spr`，或 `tools/convert_assets.py` 輸出的 SPR `.json`。它會把 atlas 裁切與原始透明邊界還原成完整幀，因此可並排核對原始 SPR 與轉換結果。提供上一幀、下一幀、固定間隔預覽、水平鏡像、raw origin 十字線與 1～4 倍像素放大。

```powershell
py -m pip install -r tools/requirements.txt
py tools/view_legacy_asset.py 'C:\SGO\Stargate Online IE HK2\Enemy\EN023A_NS1.spr'
py tools/view_legacy_asset.py local-assets/hk2-atlas-512/Enemy/EN023A_NS1.json
py -m unittest tools.tests.test_view_legacy_asset
```

`view_legacy_asset.py` 的 100 ms 播放鍵仍只是**單一 SPR 人工預覽間隔**，因它沒有 ACT timeline；不要用它判定動作速度。ACT 的 default 50Hz/20ms 時序現已由 HK2 binary 驗證，請使用下方 `view_enemy_animation.py` 的 verified catalog 播放。鏡像勾選仍只是視覺檢查工具；方向規則見 [方位映射](HK2_DIRECTION_MAPPING.md)。十字線表示 raw origin，實際世界錨點及紙娃娃疊圖仍需與原版畫面比對。

已用自造 atlas 裁切 fixture 驗證原始透明邊界、raw origin 與鏡像像素；也用 HK2 的 `Enemy/EN023A_NS1` 分幀與 512 atlas JSON 各還原 10 幀、首幀 227×202、origin (107,138)。GUI 肉眼比對尚待進行。

## 怪物 ACT 動作／方位預覽

`tools/view_enemy_animation.py` 讀本機私有的 `enemy_catalog.json`，可選怪物、`S/W/D/A/H` 動作、1～8 邏輯方位，逐步顯示來源 SPR 幀索引、原始 origin、鏡像、ACT raw_time、預設 Update 呼叫次數/default ms、事件 raw frame 位置及 `H` 特殊路徑註記。只解碼**目前選中動作／方位**引用的圖集頁；換選項時釋放舊頁引用。視窗上限 1440×900；catalog v2 且 `default_timing_verified=true` 時依每幀 audited default ms 播放，舊版或未驗證 catalog 才 fallback 100ms。

```powershell
py tools/view_enemy_animation.py local-assets/enemy-all-complete/1024/enemy_catalog.json
py -m unittest tools.tests.test_view_enemy_animation
```

本機用乾淨 HK2 `EN023A` 的 15 個來源 SPR 對照 catalog：5 動作、40 邏輯方位、272 播放步的還原**像素與 raw origin 逐步相等**。timing-aware 全量 catalog 又由 Godot 4.7.2 headless 驗證 50 groups／250 actions／2,000 logical directions／12,976 playback steps，`timing_verified=true`。原版實際畫面與 GUI 人工肉眼比對尚未完成；`EN060A`、`EN061A`、`EN115A` 被完整性稽核隔離，Viewer 不會假造其未解動作。人物紙娃娃、多層裝備及正式客戶端動畫仍未驗證。

---

<a id="source-hk2-atlas-candidates-md"></a>

## HK2_ATLAS_CANDIDATES.md

*Historical source: `docs/HK2_ATLAS_CANDIDATES.md` — SHA-256 `d0022bf3118857e5c6d9a255c5ab01f46e92c3f715134b9774fb4325686109fb`*

# HK2 依圖片尺寸分組與 Godot 實測

日期：2026-11-14。來源僅為乾淨 `C:\SGO\Stargate Online IE HK2`；轉換頁面和實測 JSON 留在被 Git 忽略的 `local-assets/atlas-candidates/`。腳本與自造測試資料可推 Git，原版圖片及衍生圖不推送。

`tools/build_atlas_groups.py` 直接讀原始 SPR，保留每個來源 SHA-256、原始幀索引、尺寸、裁切、origin 及 atlas rect。Enemy／Player 暫用「原目錄＋第一個底線前的檔名前綴」聚合；其他類別逐 SPR 分組。此為**檔案局部性規則**，不是已驗證的動作、裝備或地圖同屏關聯。邊緣作 2px 延展，讓線性過濾下相鄰影格不易混色；工具逐幀比對 atlas 內容與解碼來源。頁面尺寸參數是**最大邊長**，匯出 PNG 裁至實際用到的範圍，所以 RGBA 面積按實際輸出計算。

可重跑（替換成自己的乾淨客戶端路徑）：

```powershell
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/atlas-candidates/1024 --prefix Enemy/EN023A --atlas-side 1024
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_group_atlas.gd -- local-assets/atlas-candidates/1024/Enemy/EN023A/group.atlas.json
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_atlas_render.gd -- local-assets/atlas-candidates/1024/Enemy/EN023A/group.atlas.json
py -m unittest discover -s tools/tests -q
```

同法對 `--atlas-side 512/1024/2048/4096` 重跑。以下三組各在本機跑五次 headless PNG 解碼中位，並用 Godot 4.7.2/OpenGL Compatibility、NVIDIA RTX 3050、1280×720、300 個相同種類但不同幀的繪製實例跑 160 幀（前 40 暖機，後 120 統計）。每組目前**只一次實機渲染試跑**，p95 約 8ms，不能宣稱統計顯著或多人場景已達標。headless `texture_create_median_ms` 不代表 GPU upload 時間。

| 來源組 | 最大邊 | 頁數 | 實際 RGBA 面積 | PNG bytes | PNG 解碼中位 ms | Draw calls p50 | Frame p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Enemy/EN023A（15 SPR／111 幀） | 512 | 28 | 19,818,496 | 4,349,779 | 62.134 | 372 | 7.999 |
| 同上 | 1024 | 6 | 20,720,672 | 4,273,636 | 64.485 | 372 | 7.995 |
| 同上 | 2048 | 2 | 21,815,500 | 4,204,432 | 56.925 | 272 | 8.013 |
| 同上 | 4096 | 1 | 20,710,080 | 4,138,764 | 65.011 | 73 | 7.997 |
| Player/A/Cloth/A-Back07（59 SPR／219 幀） | 512 | 2 | 1,396,860 | 264,053 | 3.667 | 174 | 8.000 |
| 同上 | 1024 | 1 | 1,448,400 | 260,199 | 3.929 | 73 | 8.070 |
| 同上 | 2048 | 1 | 1,665,420 | 257,346 | 3.910 | 73 | 8.006 |
| 同上 | 4096 | 1 | 1,680,960 | 255,101 | 4.146 | 73 | 8.017 |
| Map/God_building/GOD_CITY_001（1 SPR／1 幀） | 1024 | 不可用 | 單幀 1151×851 放不下 | — | — | — | — |
| 同上 | 2048 | 1 | 3,950,100 | 602,635 | 10.070 | 73 | 7.994 |
| 同上 | 4096 | 1 | 3,950,100 | 602,635 | 9.674 | 73 | 7.997 |

這三組的暫定選擇：小衣物用 **1024**（一頁，較 512 增約 51.5 KB RGBA，但 draw calls 少）；單張超過 1024 的地圖物件用 **2048**（4096 無收益）；整組 EN023A 若所有動作同時載入，**4096** 在此測試用一頁並使 draw calls 最少，但比 512 多約 0.89 MB RGBA。是否把待命／移動與死亡／稀有攻擊拆成不同生命週期，仍要查 ACT 與同屏使用關係；不能直接把 4096 用在所有怪物。

尚未驗證：Godot 實際 VRAM/上傳尖峰、多人紙娃娃混合、500 掉落、20 玩家／300 怪的多種類場景、4K／21:9／zoom、反覆切圖／登出回收、原版逐格定位及貼圖過濾 Golden。若頁面來源增加，即使單頁數值較好，也可能增加常駐資源；正式決策要看實際 Map Asset Palette 和上述完整場景。

後續已加入 [8 種怪＋20 玩家代理＋500 小圖代理的混合場景初測](HK2_MIXED_ATLAS_BENCHMARK.md)。單外觀 4096 的 draw calls 優勢不能外推到混合場景；混合場景 1024／2048／4096 draw calls 相同，4096 增加 Godot Video Mem 監測值。

---

<a id="source-hk2-atomic-balanced-atlas-md"></a>

## HK2_ATOMIC_BALANCED_ATLAS.md

*Historical source: `docs/HK2_ATOMIC_BALANCED_ATLAS.md` — SHA-256 `d030470bac2d5dc16e7a333e5dfcfc200329f8cc377da39bbac2bc60332cd24e`*

# HK2 Enemy Atomic Balanced Atlas（2026-11-14）

狀態：**BUILD-TIME CANDIDATE SELECTED / RUNTIME STREAMING NOT VERIFIED**。本文件只記錄本機真 HK2 研究與 Godot GPU A/B；原始/衍生媒體仍在 gitignore 的 `local-assets/`，不提交公開 Git。

## 目標

怪物素材同時滿足：

- 原版每個 SPR 內的同一方向/動作影格不得因 atlas packing 任意跨頁；ACT 方位重用與水平鏡像只保留 metadata，不複製像素。
- 不把 50 種怪物全部常駐；runtime 仍由 MapAssetPalette/可見種類決定工作集。
- 在 512/1024/2048/4096 中依實測選尺寸，而不是用怪物外觀大小猜。
- 控制 RAM/VRAM、頁數、load spike、draw calls；UI/render FPS 不影響 network/state。

## Before

原 dense mixed-size（50 組）：
- 133 pages
- 432,604,792 RGBA bytes
- 2,000 action×facing 中 173 組跨頁

第一版 per-SPR locality：
- 2000/2000 action×facing 單頁
- 但每原 SPR 幾乎獨立成頁，頁面/texture bind 過多

atomic MaxRects v3 + 舊尺寸計畫：
- 119 pages
- 466,182,124 RGBA bytes
- 2000/2000 action×facing 單頁

## 新增 measured atomic size planner

`tools/plan_enemy_atomic_sizes.py` 直接從乾淨 HK2 解碼每組 Enemy SPR，在記憶體中對 512/1024/2048/4096 執行 atomic block packing。候選必須讓每個原 SPR 保持單一 atomic block；先找最低 RGBA，再允許預設 5% 記憶體 overhead 換較少頁數。

50 組結果：
- 512：1 組
- 1024：21 組
- 2048：19 組
- 4096：9 組
- 65 pages
- 442,769,264 RGBA bytes
- 69,437,691 PNG bytes
- 2000/2000 action×facing 單頁
- Godot catalog probe：50 groups / 250 actions / 2,000 directions / 12,976 steps PASS

相對 dense，離線全 catalog RGBA 約 +2.35%，但頁數 133→65，並消除全部 action-facing 跨頁。此 442 MB 是**全 catalog 離線總量**，不是 runtime 常駐設計。

## RTX 3050 / Godot 4.7.2 / 1440×900 A/B

每方案交替順序跑 3 回合，300 怪；結果取中位。測試工具：`tools/run_enemy_render_ab.py` + `tools/benchmark_enemy_render.gd`。

### 8 種怪

| 指標 | dense | atomic balanced |
| --- | ---: | ---: |
| pages | 12 | 9 |
| RGBA bytes | 50,782,352 | 51,088,648 |
| load median ms | 231.668 | 245.835 |
| frame p95 ms | 7.982 | 7.991 |
| draw calls p95 | 286 | 275 |
| Video Mem | 86,391,303 | 86,804,943 |

frame 差異不顯著；atomic balanced 的 pages/draw calls 較少，Video Mem 約 +0.4 MB。

### 50 種怪壓力

| 指標 | dense | atomic balanced |
| --- | ---: | ---: |
| pages | 133 | 65 |
| RGBA bytes | 432,604,792 | 442,769,264 |
| load median ms | 1,281.023 | 1,289.890 |
| frame p95 ms | 7.924 | 7.110 |
| draw calls p95 | 372 | 372 |
| Video Mem | 595,191,887 | 608,852,731 |

不能用三回合把 frame p95 的差值宣稱成普遍加速；可確認的是 draw calls 沒退化，頁數減半，Video Mem 約 +13.7 MB。正式 runtime 不會一次載入 50 種，仍以 MapAssetPalette 為準。

## 決策

1. Enemy build pipeline 採 **atomic source blocks + measured multi-size plan** 作完整 catalog 候選。
2. Runtime **不以完整 catalog 常駐**；MapAssetPalette 仍只載入目前地圖/鄰區需要的 visual。
3. `source_spr_atomic_maxrects_v3` 保留為 packing contract；logical facing 鏡像不生成重複圖。
4. 每次素材版本變動重新跑 planner；不硬編碼 EN 編號或尺寸。
5. Player 紙娃娃、ground item、UI 不直接繼承此策略，各自 A/B。

## 可重跑

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
py tools/plan_enemy_atomic_sizes.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-complete-groups.txt local-assets/enemy-atomic-size-plan.json --memory-overhead 0.05
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-atomic-balanced --group-list local-assets/enemy-complete-groups.txt --size-plan local-assets/enemy-atomic-size-plan.json --source-locality
py tools/compile_enemy_atlas_catalog.py local-assets/enemy-animation-audit.json local-assets/enemy-atomic-balanced local-assets/enemy-atomic-balanced/enemy_catalog.json
py tools/audit_action_page_locality.py local-assets/enemy-atomic-balanced/enemy_catalog.json --require-single-page
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- local-assets/enemy-atomic-balanced/enemy_catalog.json
```

GPU raw reports 留在 gitignore 的 `reports/enemy-render-ab-balanced-*.json`。正式 StreamingManager、GPU upload p99、切圖/登出回收、人物紙娃娃與原版逐格視覺仍需後續 Gate。

---

<a id="source-hk2-data1-archive-md"></a>

## HK2_DATA1_ARCHIVE.md

*Historical source: `docs/HK2_DATA1_ARCHIVE.md` — SHA-256 `172b3a258283dc67fcdaca7d15ac60bbbe5c7e4f36a1a291ffa673b1a2e025f0`*

# HK2 Data1.dat 群組檔與 loose overlay 證據

日期：2026-11-14。狀態：DIRECT 格式實證 + 本機真檔驗證。原始／解包媒體只存放於使用者本機 local-assets，不提交公開 Git。

## 原版讀檔順序與 archive 格式

- CELApplication::FileOpenGroup .text:00506677 → CELFileIO::CELFileOpenGroup .text:00506BC0 → xnFileOpenGroup .text:00552980。
- xnFileOpenGroup 從檔案最後 4 bytes 讀 index offset；index 起點依序是解壓後大小、壓縮大小、LZSS 壓縮索引。
- 解壓後索引是 folder_count；每 folder 有 file_count + NUL folder name；每 file 為 12 bytes 數值 + NUL filename。
- FileData::ReadBuf .text:00551810 證實 12 bytes 為 uncompressed_size / compressed_size / data_offset；compressed size=0 直接讀原始 bytes，否則 LZSS 解壓。
- LzssDecompress .text:0055A0E0：4096-byte ring buffer、初始空白、起點 0xFEE；flag bit=1 literal，bit=0 使用 12-bit back-reference，長度 low_nibble+3。
- xnFileRead .text:00552090 先搜尋 _pathList loose 檔，再搜尋 _groupList。因此正式資源解析順序是 loose override > archive base。

tools/legacy_group_archive.py 實作只讀 parser／LZSS／選擇性抽取；tools/materialize_legacy_overlay.py 依原版 precedence 建立私有工作集。

## HK2 真檔

C:\SGO\Stargate Online IE HK2\Data1.dat：
- bytes：585,022,714
- SHA-256：9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42
- index offset：584,491,046
- index raw size：1,251,425
- index compressed size：531,656
- entries：41,687

主要 archive 分布：Player 31,717 entries、Map 4,295、MS 1,668、Enemy 1,376、Effect 775、Interface 758、NPC 536、Audio 210、MA 177、SR 173、Item 2。副檔名以 SPR 37,422、SIF 981、STH 980、BLG 901、ACT 444、ITF 140 為主。

這代表 loose 目錄盤點不是完整 Client 虛擬檔案系統；Data1 是大量基底素材來源。

## 真地圖驗證

Old Server Map13 Enemy.dat 有 78 個出生點，只使用 9 種 visual：EN009A/EN010A/EN024A/EN025A/EN029A/EN030A/EN050A/EN058A/EN064A。這些在 HK2 loose Enemy 都沒有實體檔，但 Data1 內各找到 1 ACT + 15 SPR，共 144 files。抽出後 ACT audit 9/9 完整、0 缺圖、0 frame index 越界。

17 張具有 Enemy.dat 的已知舊 Server 地圖合計只用 29 種怪物 visual。對這 29 種建立工作集時得到 464 files：461 archive base、1 loose override、2 loose-only；實際 override 是 Enemy/EN061A.ACT，loose-only 是 EN061A_ND1.spr、EN061A_ND3.spr。29/29 ACT 群組 audit 完整。

## 全安裝目錄 group archive 掃描

新增 `tools/scan_group_archives.py`，對乾淨 HK2 目錄的**全部 41,741 個實體檔**先做 fail-closed footer 預篩：最後 4 bytes 必須能指向合法 index header，且 `index_offset + 8 + compressed_index_size == file_size - 4` 完全成立；只有命中者才交給 `GroupArchive` 做 LZSS index 完整解析與 entry bounds 驗證。這不抽取媒體內容。

2026-11-14 真檔掃描：41,741 files、footer candidates=1、valid archives=1、rejected=0，唯一命中為 `Data1.dat`。因此對**目前提供的 HK2 安裝目錄**，沒有發現第二個採用相同 xnFile group archive 格式的檔案。這個結論不等於證明原始發行安裝包從未有其他 archive；它只界定目前研究工作集的虛擬檔案來源。

可重跑：
```powershell
py tools/scan_group_archives.py 'C:\SGO\Stargate Online IE HK2' reports/hk2-group-archives.json
```

## 尚未完成

HK2 `Client.bin`／`SGOClient.bin` 已用 PE timestamp 排除與 2004 Paradise binary-identical；但安裝包／更新來源 provenance 仍未建立。原版媒體授權／公開發布權也未確認，因此 parser 與 metadata 可進 Git，原始／衍生媒體不可進公開 repo。

---

<a id="source-hk2-direction-mapping-md"></a>

## HK2_DIRECTION_MAPPING.md

*Historical source: `docs/HK2_DIRECTION_MAPPING.md` — SHA-256 `538495570158497e9157f1ce7b4045251f415f47db11b2e1a304b6710bbf97ad`*

# 原版人物方位與水平鏡像

證據：`C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOCharDrawLogic::DirectionMapping`（`.text:004C263F`～`004C283A`）及 `SetDirection`（`.text:004C24EA`～`004C25B3`）。這是原版參考程式的直接證據；HK2 客戶端與該參考版本的二進位等同性仍待驗證。未把反編譯內容放進 Git。

原版將邏輯方位 5、6、7 分別取來源方位 3、2、1 並水平鏡像。方位 1、2、3 直接使用。方位 4、8 在原版數值 draw action 4～17 時分別取來源方位 3、1 並鏡像；其他 action 則保留 4、8 原圖。原版也將鏡像旗標獨立回傳，供繪圖使用（`CSGOChar::GetDirectionMapping`，`.text:004C0B6A`～`004C0BB0`）。

`tools/legacy_direction.py` 將映射表示為小型純函式，供素材檢視器及未來 client 使用；不猜 action 數值對應的名稱，也不預先鏡像輸出圖片。這可共用原始圖、減少轉換檔案與記憶體。鏡像時還須驗證 SPR origin 與裝備圖層的位置計算，不能只翻轉像素。

驗證：`py -m unittest tools.tests.test_legacy_direction`。待做：確認**人物** action ID 與 ACT key 的對應、其他角色類型的方位路徑、實際遊戲畫面比對。

## 怪物的獨立映射

原版參考 `CSGOEnemDrawLogic::SetDirection`（`.text:004CB708`～`004CB7E8`）與 `InitAction`（`.text:004CBF8C`～`004CC111`）顯示怪物有**另一套**方位規則。`InitAction` 透過 `.data:006051F4` 的 UTF-16LE `SWDAH` 表把數值動作索引 0～4 傳給繪圖物件；但索引 4 在 `InitAction` 的入口直接返回，因此該動作如何完成需再查其他路徑。

| 邏輯方位 | 動作索引 0、2 | 動作索引 1 | 動作索引 3、4（若被 SetDirection 呼叫） |
| ---: | --- | --- | --- |
| 1 | 1 原圖 | 1 原圖 | 1 原圖 |
| 2 | 1 原圖 | 2 原圖 | 2 原圖 |
| 3 | 3 原圖 | 3 原圖 | 3 原圖 |
| 4 | 3 鏡像 | 4 原圖 | 3 鏡像 |
| 5 | 3 鏡像 | 3 鏡像 | 3 鏡像 |
| 6 | 1 鏡像 | 2 鏡像 | 2 鏡像 |
| 7 | 1 鏡像 | 1 鏡像 | 1 鏡像 |
| 8 | 1 鏡像 | 8 原圖 | 1 鏡像 |

`tools/legacy_direction.py` 提供 `map_enemy_direction`；不修改原始像素或提前合併怪物方向，讓 renderer 可共用原圖並按動作指定鏡像。這是參考版本的 DIRECT 反編譯證據，與 HK2 二進位一致性及實際畫面仍待比對。

怪物 ACT key→SPR 後綴與 53 組全量稽核見 [HK2_ENEMY_ANIMATION_MAPPING.md](HK2_ENEMY_ANIMATION_MAPPING.md)。

---

<a id="source-hk2-draw-order-md"></a>

## HK2_DRAW_ORDER.md

*Historical source: `docs/HK2_DRAW_ORDER.md` — SHA-256 `2b9930597600457ea3a2b17388fe6601bc699b7a2baca1d5466fddd926da6302`*

# HK2 人物疊圖順序證據

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 `Player/*DrawOrder.dat` 原始文字、`Paradise.exe.lst` 具體呼叫位址）。

`Player` 下有 9 個 `*DrawOrder.dat`。`CharDrawOrder.dat`、`R_CharDrawOrder.dat` 與兩份 ClubCloth 表各有 51 個不重複鍵；AZ/BZ/GZ/RZ/EM 表各 12 鍵。每行是 `動作方向鍵 : layer>layer>...`，例如 `NS1` 的圖層序列會依 `NS2/NS3/NS4/NS8` 改變，攻擊類鍵也有不同順序。`leg+2` 等後綴暫按原文保留；其語意尚未核對，不等於使用者要求移除的裝備強化 +2 物品 ID。

原版反編譯直接證據：`Paradise.exe.lst` `.text:00408049` 定義 `CSGOCore::LoadDrawOrderDat`；`.text:00408822` 和 `.text:0040882E` 將 `Player\CharDrawOrder.dat` 傳入該函式，並使用 `CSGOLibrary::GetCharAction` 回傳物件；`.text:00408894`／`.text:004088A0` 對 `R_CharDrawOrder.dat` 做同樣操作。這表示疊圖順序參與人物動作資料，不能把所有角色部位預烘成無法調序的一張固定圖。

`tools/convert_assets.py` 現在會把 9 份表逐行保真轉成私有輸出 `.draw_order.json`，記錄 source SHA、raw key 和 raw layer 序列。可重跑：

```powershell
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames --category Player --limit 20
py -m unittest tools.tests.test_convert_assets
```

下一步：追 `LoadDrawOrderDat` 對 `+N` 的解析與實際繪製優先級，核對 1/2/3/4/8 方位與鏡像規則，再用原版畫面驗證衣服、手臂、武器、盾牌在站立／行走／攻擊／受擊／死亡的遮擋順序。

## 2026-11-14 雙槽位展開實證

原版參考 `LoadDrawOrderDat` 在 `.text:004082D2`～`004084BC` 對文字 token 作不分大小寫的完整比較；`L-Arm+2` 依次 Append 槽位 **6、5**，`R-Arm+2` 為 **8、7**，`Body+2` 為 **10、9**，`leg+2` 為 **13、12**。不是先移除 `+2` 再解析。其他 token：Head=0、Hair=1、Bonnet=2、L-Shoulder=3、R-Shoulder=4、Back=11；Sword/L-Gun 共用14，R-Gun/Shield 共用15（`.text:00408183`～`004085C8`）。這是參考版本 DIRECT 槽位建立證據，兩槽位各自對應的身體／衣物來源與實際渲染仍待追。

`tools/audit_character_layers.py` 對乾淨 HK2 的 `CharDrawOrder.dat` 與 `R_CharDrawOrder.dat` 保留 raw token、來源 SHA 並展開成槽位序列；**2 表／102 行全部可解析**，未知 token 會報錯。其他騎乘／ClubCloth 表由不同載入路徑處理，未套用這份規則。可重跑：

```powershell
py tools/audit_character_layers.py 'C:\SGO\Stargate Online IE HK2' local-assets/character-layer-audit.json
py -m unittest tools.tests.test_audit_character_layers
```

此雙槽位語法必須保留；使用者要求去除的裝備強化 `+1/+2/+3` 是物品定義別名整理，不能套用到這裡。HK2 程式版本一致性、每個槽位的貼圖來源、人物 action ID 對應、原版畫面前後遮擋及鏡像位置仍 NOT VERIFIED。

---

<a id="source-hk2-enemy-animation-mapping-md"></a>

## HK2_ENEMY_ANIMATION_MAPPING.md

*Historical source: `docs/HK2_ENEMY_ANIMATION_MAPPING.md` — SHA-256 `bac24f01e2d6b54f32ecc14354d8a6363d776a54c7f4070a9c79c37319ac825b`*

# HK2 怪物 ACT／SPR 來源與方向稽核

日期：2026-11-14。原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOEnemDrawLogic::InitAction`（`.text:004CBF8C`～`004CC111`）、`SetDirection`（`.text:004CB708`～`004CB7E8`）、`SetEnemSprite`（`.text:004CB833`～`004CB8FA`）。`.data:006051F4` 是 UTF-16LE `SWDAH` 動作索引表；`.data:00605200`～`0060520E` 分別是 `NS/NW/ND/FA/NH` SPR 後綴。`SetEnemSprite` 以敵人基本名稱、後綴及映射後的方向建立 `Enemy\%s_%s%d.spr` 路徑。這是參考版本的 DIRECT 反編譯證據，與 HK2 二進位是否完全一致仍待核對。

`tools/legacy_direction.py` 實作怪物專屬方向映射；`tools/audit_enemy_animations.py` 從乾淨 HK2 讀所有 Enemy ACT/SPR，為每個 `S/W/D/A/H` 與 1～8 邏輯方位驗證目標 SPR 存在、ACT 影格索引沒有超過 SPR 幀數，保留原始更新次數、循環、事件、來源 SHA 與鏡像旗標。它只做來源稽核，**不**把 ACT 的 raw duration 誤當毫秒，也不宣稱畫面已視覺校正。`H` 在原版 `InitAction(4)` 走特殊早退路徑，報告標記 `special_init_path`。

```powershell
py tools/audit_enemy_animations.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-animation-audit.json
py -m unittest tools.tests.test_audit_enemy_animations tools.tests.test_legacy_direction
```

HK2 結果：53 個 Enemy ACT 群組與 53 個同名 SPR 群組；51 組有標準 15 個動作／來源方向 SPR，`EN060A`、`EN061A` 僅各有 `ND1/ND3`。**50／53 組在參考版本的映射下，所有 ACT 索引均在實體 SPR 幀數範圍內且來源齊全**。例外共 3 組、72 筆方向級待釐清引用：`EN060A`／`EN061A` 的 `S/W/A/H` 路徑沒有同名實體 SPR（各 32 筆邏輯方位）；它們可能是原版不會播放這些動作的特殊種類，不能單憑數量判定遊戲素材缺失。`EN115A` 的 D 動作引用幀索引 1、2，而 `ND1/ND3` 各只有 1 幀（8 筆邏輯方位）。無法用 ACT 紀錄數與 SPR 幀數相等當條件：例如 `EN050B` 的 ACT S 有 10 個播放紀錄，但 `NS1` 僅 5 幀，因為索引 0～4 各重複兩次。1～8 邏輯方向會依動作重用並鏡像少數來源圖，不表示缺少原版方位素材。

待查：`EN060A/EN061A` 是否有原版特殊無動作路徑、`EN115A` 越界時原版如何顯示、H 特殊初始化、原版 Update 呼叫頻率及實際畫面／裝備遮擋。未通過前，這份報告只能作素材清單與 fail-closed 動畫建立依據，不能當最終怪物渲染定義。

---

<a id="source-hk2-enemy-atlas-catalog-md"></a>

## HK2_ENEMY_ATLAS_CATALOG.md

*Historical source: `docs/HK2_ENEMY_ATLAS_CATALOG.md` — SHA-256 `8716f5c4ea1b64510031dfe059b06cef43bfd3eddb3a709d91003b3b998b4838`*

# HK2 怪物方位動畫圖集索引

日期：2026-11-14。這是 AS002／AS007 的**實驗性視覺資料契約**，由乾淨 HK2 素材、原版參考方向規則與分組 Atlas 建立。來源圖片及衍生圖片只放在 git 忽略的 `local-assets/`，不提交到公開儲存庫。

## 可重跑的產生流程

```powershell
py tools/audit_enemy_animations.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-animation-audit.json --complete-group-list local-assets/enemy-complete-groups.txt
py tools/select_mixed_benchmark_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy-groups.txt
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy/1024 --group-list local-assets/mixed-heavy-groups.txt --atlas-side 1024
py tools/compile_enemy_atlas_catalog.py local-assets/enemy-animation-audit.json local-assets/mixed-heavy/1024 local-assets/mixed-heavy/1024/enemy_catalog.json
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- local-assets/mixed-heavy/1024/enemy_catalog.json
py -m unittest discover -s tools/tests -q
```

全量已稽核怪物可把 `--group-list` 換成 `local-assets/enemy-complete-groups.txt`，輸出目錄換成 `local-assets/enemy-all-complete/1024`，再對新目錄執行 catalog 編譯與 Godot 探針。此清單排除來源或索引異常的群組；所有產物都留在私有目錄。

`compile_enemy_atlas_catalog.py` 只選 Atlas 目錄中的 Enemy 群組。它要求該群組的所有 ACT 方位引用可對上來源 SPR 影格、來源 SHA 與 Atlas manifest 相符、頁面可讀、區域位於頁內、八個邏輯方位齊全。未能對上的群組進入 `quarantined`，命令回傳失敗；不產生貌似完整的動畫。資料保留每個 action 的索引、循環、事件及特殊初始化註記，八方位的來源方位與水平鏡像，以及每一播放步的頁面、裁切範圍、原始 origin 與原始時間欄位。原版重複影格索引的播放順序會保留，不以來源 SPR 幀數推算播放步數。

本機實測使用混合負載中的 8 種怪物：`compiled_groups=8`、`quarantined_groups=0`；Godot 4.7.2 headless 實際解碼頁面並檢查區域，得到 8 群組、40 action、320 邏輯方位、2336 播放步。另有 3 個全量稽核例外群組（`EN060A`、`EN061A`、`EN115A`）仍需查原版特殊路徑，因此**未**把所有 53 種怪物宣稱為可播放。

擴充試跑：50 組完整來源的 1024 atlas 打包 50/50 成功，共 143 頁、實際頁面 RGBA 面積 433,336,404 bytes；catalog 編譯 50/50、隔離 0；Godot headless 解碼驗證 50 群組、250 action、2,000 邏輯方位、12,976 播放步。這是**全量同時打包的離線驗證量**，不是建議 Client 一次載入 433 MB；正式工作集應按地圖及可見種類預載、共享與回收。

## 使用邊界

`time_unit=null`、`animation_timing_verified=false`、`visual_alignment_verified=false` 是刻意保留的未解欄位。原版 Update 呼叫頻率、`H` 特殊初始化、攻擊／命中事件觸發時點、畫面實際位置、圖層遮擋與 HK2 程式版本一致性，均未經原版畫面對照。Godot 探針只證明資源及索引可讀，不代表正式渲染、多人同步或切圖生命週期已通過。正式 Client 仍須等 M0 素材 Gate 完成。

---

<a id="source-hk2-ground-art-benchmark-md"></a>

## HK2_GROUND_ART_BENCHMARK.md

*Historical source: `docs/HK2_GROUND_ART_BENCHMARK.md` — SHA-256 `67d57b962a60103c05b11c9797aba301fd6074e99d9cbf5104dfb23ffe606233`*

# HK2 地上物品圖：逐圖與局部圖集比較

日期：2026-11-14。原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` 的 `CSGOItemDrawLogic::InitItem`，`.text:004CCD17`～`004CCDA9`，在 `.text:004CCD62` 引用 `Interface\ground.spr`，並在 `.text:004CCD75`～`004CCDA3` 將 item 物件的 `+0xCC` 欄位交給繪圖物件。這是參考版本的 DIRECT 反編譯證據；`+0xCC` 與正式物品定義 ID 的對照尚未驗證。乾淨 HK2 的 `Interface/ground.spr` SHA-256 為 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6`，可解出 500 個非空影格（最大 82×54）。前一份混合負載中的 `MS` 小圖是機甲類代理，**不是**經證實的掉落物圖。

## 重跑

```powershell
py tools/build_ground_candidates.py 'C:\SGO\Stargate Online IE HK2' local-assets/ground-candidates --sample-count 25
py tools/build_ground_candidates.py 'C:\SGO\Stargate Online IE HK2' local-assets/ground-all-candidates --sample-count 500
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_ground_render.gd -- local-assets/ground-candidates/1024/catalog.json 1440 900
py -m unittest discover -s tools/tests -q
```

`--sample-count 25` 固定取原始影格 0、20、…、480；每種候選都用**相同 25 張解碼像素**和 500 個實例，逐圖候選保留每張來源尺寸，Atlas 候選用 2px 邊緣延展。`--sample-count 500` 則測所有影格同時可見的高 unique 壓力，不代表單一地圖實際會掉出全部 500 種。轉換腳本逐影格驗證輸出與解碼來源像素一致；原始及衍生 PNG 都在 git 忽略的 `local-assets/`。

Godot 4.7.2 OpenGL Compatibility／RTX 3050，視窗 1440×900，固定 500 個按 Y 排序的可見實例。每次 160 幀，前 40 暖機，後 120 取統計；每候選交錯跑三次。下表載入與 frame p95 是三次中位，Video Mem monitor 含引擎／視窗開銷，不是純貼圖 VRAM。

| 同屏圖種 | 候選 | 頁數 | 實際 RGBA 面積 bytes | PNG bytes | 載入中位 ms | Draw calls p50 | Frame p95 中位 ms | Video Mem monitor bytes |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 25 | 逐圖 | 25 | 104,696 | 42,585 | 7.051 | 572 | 7.989 | 18,848,431 |
| 25 | 1024 | 1 | 179,584 | 37,358 | 1.141 | 73 | 8.012 | 18,949,283 |
| 25 | 2048 | 1 | 179,584 | 37,358 | 1.174 | 73 | 8.003 | 18,949,283 |
| 25 | 4096 | 1 | 179,584 | 37,358 | 1.105 | 73 | 8.009 | 18,949,283 |
| 500 | 逐圖 | 500 | 2,006,004 | 848,054 | 161.763 | 572 | 7.972 | 21,347,039 |
| 500 | 1024 | 1 | 3,605,052 | 738,532 | 36.151 | 73 | 7.993 | 23,513,803 |
| 500 | 2048 | 1 | 3,870,300 | 737,662 | 37.433 | 73 | 8.000 | 23,865,823 |
| 500 | 4096 | 1 | 3,981,312 | 733,564 | 37.638 | 73 | 8.006 | 24,010,623 |

500 種的逐圖第一次載入曾量到 1566ms，後兩次為 160.125／161.763ms；表格使用三次中位，不把首次結果當穩定值。載入受 OS 檔案快取影響，**沒有**量到 GPU commit 尖峰。800×600 另跑逐圖／1024 各一次，實際視窗正確，draw calls 573／74；不是完整多尺寸驗收。

**目前選擇**：已知會同時出現的一批地上物品，優先用 1024 局部圖集，按地圖工作集載入；稀有或不共現的單一圖可維持獨立貼圖，不能因此常駐整張 500 影格圖。此場景的 2048／4096 沒有載入或 draw calls 收益，500 種時還增加記憶體監測值。逐圖的實際 RGBA 面積較小，影格 p95 差異在試跑波動內，因此不能稱逐圖渲染「會 lag」；圖集的明確收益是更少 texture 切換／draw calls 和高 unique 情況較低的載入中位。下一步要建立 item 定義→ground 影格映射、實際地圖掉落共現集合、GPU upload／資源卸載和與怪物／玩家混合的真場景基準。

後續的 [HK2 物品定義稽核](HK2_ITEM_LIBRARY_MAPPING.md) 已查出 `ground1.spr` 另有 500 幀、1,047 筆定義的圖號超過 500。本頁的 500 種負載僅代表第一檔 `ground.spr` 的全部影格，不能當成所有 HK2 物品圖的完整集合。

---

<a id="source-hk2-inventory-md"></a>

## HK2_INVENTORY.md

*Historical source: `docs/HK2_INVENTORY.md` — SHA-256 `39e5930358e5dbca6b4287b36c41ecb89f7b20406f8d4d7a1328d045f097abb2`*

# HK2 原始客戶端實體檔案盤點

日期：2026-11-13（Asia/Taipei）  
來源：`C:\SGO\Stargate Online IE HK2`（本機唯讀；報表在忽略的 `reports/hk2-inventory.json`）  
證據等級：DIRECT，僅限實體目錄、檔案位元組與 SHA-256；尚未證明遊戲內動作語意或授權。

## 可重跑命令

```powershell
py tools/inventory_client.py 'C:\SGO\Stargate Online IE HK2' reports/hk2-inventory.json
py -m unittest tools.tests.test_inventory_client
```

盤點結果：41,741 個實體檔，合計 1,333,254,792 bytes。主要分類：Player 37,410、Map 1,081、MS 1,156、Enemy 823、MA 605、Effect 277、Interface 192、NPC 121、Audio 11。副檔名：SPR 40,224、MAP 249、SIF 186、ACT 178、ITF 20、ITD 4、MP3 11。這是檔案數，不能當作完整動作／方向數。

前 6 bytes 為 `43 2e 44 2e 45 2e` (`C.D.E.`) 的 SPR 有 10,760 個，說明必須依檔頭分流解碼，而非只看 `.spr`。`Data1.dat` 為 585,022,714 bytes，SHA-256 `9ae08a8a7bb78472...`；尚未解析其內容，不能斷定實體目錄已包含所有封包資源。`Client.bin`、`SGOClient.bin` 同時存在，版本與實際啟動路徑待查。

HK2 有 20 個 `.itf` 實體檔，修正先前「本機沒有後期 ITF」的假設；其內容與金黃色 UI 的對應關係尚未驗證。`Player` 目錄含 A/B/R/G/Y 五組大量檔案及 draw-order `.dat`，方向、裝備層次與幀時長仍需從 ACT/SIF/原始客戶端載入邏輯確認。報表不進 Git，亦不含圖像或音訊位元組。

後續 [ITF 引用盤點](HK2_UI_REFERENCES.md) 顯示 122 筆 SPR 引用中 59 筆無實體同名檔，不能用實體目錄數量推論 UI 素材已完整。

下一步：AS002 追查原版讀檔與 ACT/SIF 動作語意；AS003 建立逐分類完整性矩陣；AS004 用已驗證的 SPR 結構做保真影格輸出，再比較 Godot 貼圖組合策略。

---

<a id="source-hk2-item-library-mapping-md"></a>

## HK2_ITEM_LIBRARY_MAPPING.md

*Historical source: `docs/HK2_ITEM_LIBRARY_MAPPING.md` — SHA-256 `bb33b471781f7a1e4f11d4483137ae16798e8813d2b8f2d92eb25b9f1afa0bac`*

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

---

<a id="source-hk2-map-asset-palette-md"></a>

## HK2_MAP_ASSET_PALETTE.md

*Historical source: `docs/HK2_MAP_ASSET_PALETTE.md` — SHA-256 `4aafaf74f46ec4f6182d9c7c224c27efd84467d640b25556097b8c6d6c312e43`*

# 已知地圖怪物 MapAssetPalette 與 1024 Atomic Atlas

日期：2026-11-14。狀態：本機內容與 GPU 實測通過；正式 Client StreamingManager 尚未實作。

## 為什麼按地圖載入

從 Old Server Dat/Map/MapXX/Enemy.dat 與 Enemy/Enemy_Data.csv 直接編譯後，目前 17 張具有 Enemy.dat 的已知地圖合計只用 29 種怪物 visual；單張地圖只有 1～10 種，即使出生點可達 149 個。Entity 數量不能等同 unique texture 數量。

tools/build_enemy_map_palette.py 輸出每 map 的 enemy ID、sprite、spawn count；tools/compile_map_asset_palette.py 再把 visual 與已驗證 atlas catalog 接起來，輸出每張地圖需要預載的 group/page 與頁面 RGBA 面積。

## 29 種 known-map visual 驗證

- 29 groups / 145 actions / 1,160 logical facings / 8,792 playback steps。
- atomic 1024：32 pages / 62,287,496 RGBA bytes / 1,160/1,160 action-facing 單頁。
- dense 1024：32 pages / 63,770,056 RGBA bytes，但有 9/1,160 action-facing 跨頁。
- 需要 2 頁的 visual 為 EN038A/EN051A/EN068A；atomic 下每個實際 action-facing 仍只使用單頁。

Atomic v3 使用 block-level MaxRects：先保證每個原 SPR 區塊不可拆，再把多個完整 SPR co-pack 到頁面，因此保留動作／方向 locality，又不等於一 SPR 一 texture。

## 每張地圖工作集

reports/runtime-enemy-palettes.json 本機結果：Map13 為 78 spawns / 9 visuals / 9 pages / 約 10.620 MiB atlas RGBA。17 張地圖全部 complete=true；已知地圖約 1～10 visual，頁面工作集約 3.305～17.441 MiB。

這些是 atlas page 像素面積，不等於 Windows process working set；Godot 自身、framebuffer、UI、地圖等另計。

## Map13 GPU A/B（RTX 3050 / Godot 4.7.2 / OpenGL / 1440×900）

| 場景 | 方案 | Pages | RGBA | Load median | Frame P95 | Draw P95 | Video Mem monitor |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 78 monsters | dense | 9 | 11,879,204 | 95.016 ms | 8.002 ms | 151 | 34,538,031 |
| 78 monsters | atomic v3 | 9 | 11,135,572 | 94.839 ms | 7.990 ms | 151 | 33,545,907 |
| 300 monsters | dense | 9 | 11,879,204 | 94.895 ms | 7.133 ms | 373 | 34,538,031 |
| 300 monsters | atomic v3 | 9 | 11,135,572 | 79.213 ms | 7.118 ms | 373 | 33,545,907 |

Frame 差異落在抖動範圍，不宣稱 atomic 讓 FPS 顯著變快；可確認 locality 正確、draw calls 沒增加，且此工作集 Video/Texture memory 約少 1 MB。Load 仍受 OS cache/順序影響。

## 決策

怪物 runtime 候選採 map-aware + per-visual atomic 1024 優先：只預載目前 map palette；同 visual 共用 Texture/FrameTable/Material；方向鏡像在 metadata/runtime 做；若單一 SPR 在 1024 無法維持 locality，再依量測提升 2048，不預設 4096。正式 StreamingManager 的 GPU upload p99、切圖、登出100次仍 NOT VERIFIED。

---

<a id="source-hk2-map-references-md"></a>

## HK2_MAP_REFERENCES.md

*Historical source: `docs/HK2_MAP_REFERENCES.md` — SHA-256 `5c762c100ed81c7cfab0fbc90cede0705b0739e903492b04ad92ec91860bf0e5`*

# HK2 地圖清單與實體檔缺口

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（HK2 `Map/Map*.map` 文字引用與本機檔案存在性），不推斷 `Data1.dat` 內部內容或原版尋路規則。

可重跑：`py tools/inspect_map_refs.py 'C:\SGO\Stargate Online IE HK2'`；`py -m unittest tools.tests.test_inspect_map_refs`。

219 份頂層地圖 manifest 的引用結果：Tiles 219/219 有檔、MapBlock Array 115/219、Object List 118/219、Blocking 121/219、Minimap 44/136、Zone 0/115。例：`Map13.map` 指向 `Map/Map13/MapBlock.map`、`ObjectList.dat`、`Blocking.dat`、`safezone.dat`，但 HK2 中沒有該 `Map13` 實體目錄。`Map1.map` 指向 `Minimap.jpg`，同目錄卻只有 `minimap.tga`，顯示版本／路徑差異。許多地圖的 Tiles 都指向共用 `Map/Map01/Map.pic`；目前尚未解析 PIC 格式。

因此 HK2 足以建立 SPR/ACT 原始影格管線，但**不能單靠實體目錄**直接聲稱可還原所有地圖畫面與 Server 權威碰撞。缺件可能在本機其他資料／封包中；需要追原始 Client 的搜尋與封包讀取路徑，並以合法來源的 Blocking資料做座標 fixture。多人 Server 的碰撞不能從畫面像素猜測。

---

<a id="source-hk2-mixed-atlas-benchmark-md"></a>

## HK2_MIXED_ATLAS_BENCHMARK.md

*Historical source: `docs/HK2_MIXED_ATLAS_BENCHMARK.md` — SHA-256 `e82c731a4462b09d73b26d6997825a878380c55a2084a88f9c4857a7e22701cb`*

# HK2 多種類 Atlas 負載初測

日期：2026-11-14。承接 [單外觀候選](HK2_ATLAS_CANDIDATES.md) 與 [多尺寸素材策略](ASSET_STREAMING_AND_RENDERING.md)。本機只使用乾淨 `C:\SGO\Stargate Online IE HK2` 由 `tools/build_atlas_groups.py` 重新解碼與分組；輸出的 PNG／JSON 媒體均在 Git 忽略的 `local-assets/`，不推送。

## 可重跑工作集

選 8 組 Enemy：`EN114A, EN089A, EN121A, EN116A, EN023A, EN084A, EN111A, EN125A`，依轉換 manifest 的裁切矩形總面積排序，避免先前排序前八組多數太小的偏差。每組取所有同前綴 SPR，共用該外觀 atlas；另取 `Player/A/Cloth/A-Back07` 59 SPR 和排序前 25 個 `MS` 小型裝備視覺。`MS` **只是 500 個掉落物視覺的尺寸代理**，尚未證明是原版物品圖示。此測試不含實際角色紙娃娃其他層、物品 ID、網路或戰鬥。

`tools/select_mixed_benchmark_groups.py` 直接從乾淨 HK2 解碼所有 Enemy SPR、按裁切矩形面積排名，產生上述 34 個 group key 的 UTF-8 清單及私有報告。對每個候選上限重跑：

```powershell
py tools/select_mixed_benchmark_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy-groups.txt
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/mixed-heavy/1024 --group-list local-assets/mixed-heavy-groups.txt --atlas-side 1024
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_mixed_render.gd -- local-assets/mixed-heavy/1024 1920 1080
```

同法改成 2048、4096，正式視窗測試可傳 `1440 900` 或 `800 600`。Godot 4.7.2／OpenGL Compatibility、NVIDIA GeForce RTX 3050，Y 座標排序繪製 20 個玩家代理、300 隻 8 種怪、500 個 25 種小圖代理。每回合 160 幀，前 40 幀暖機，後 120 幀取 frame p95 和 draw calls。先前 1080p 每尺寸以交錯順序跑三回合，載入耗時及 frame p95 下表為三回合中位；GPU 記憶體監測值以後續同設定的一次 Godot 監測為準，**含引擎／視窗開銷，不是純 atlas VRAM**。

| 最大頁邊 | 頁數 | 實際 RGBA 面積 bytes | PNG bytes | 1080p 載入中位 ms | Draw calls p50 | Frame p95 中位 ms | Godot Video Mem 監測 bytes |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1024 | 87 | 195,550,316 | 31,693,952 | 715.061 | 892 | 7.987 | 285,515,459 |
| 2048 | 44 | 206,281,432 | 31,440,178 | 658.716 | 892 | 7.986 | 299,891,971 |
| 4096 | 34 | 224,978,452 | 31,243,536 | 702.356 | 892 | 7.911 | 324,815,867 |

正式寬度上限附近的 1440×900 單次測試，1024／2048／4096 frame p95 分別為 8.000／7.969／8.007ms，Godot Video Mem 監測為 279,294,659／293,671,171／318,595,067 bytes；800×600 的 1024 組測得 p95=8.003ms、272,769,803 bytes。基準腳本先前要求 800×600 時曾因視窗設定得到 1080×675，已改用 `root.size` 並重測，實際視窗確認 800×600。先前 4K 的單次數值只保留作超規壓力研究，不列正式驗收。這次各候選的 draw calls 都相同，影格差距落在試跑波動內。4096 比 1024 減少 53 頁、PNG 少約 0.45 MB，但 1080p Godot Video Mem 監測多約 39.3 MB；全域採 4096 沒有這組證據支持。2048 載入中位約少 56 ms，但比 1024 多約 14.4 MB Video Mem。載入測試是一次性全載且受 OS 檔案快取影響，並非有界串流或 GPU 上傳尖峰。

**暫定策略**：小型且一頁能放下的外觀用 1024；超過 1024 的幀或經實景測得 1024 分頁載入尖峰的外觀可選 2048；4096 只在單一外觀確實需要且可見工作集／GPU 記憶體預算容許時使用。這是素材包裝候選，正式 Map Asset Palette 仍需原版地圖刷怪、實際掉落圖、ACT 動作時序、紙娃娃疊層和多解析度可見範圍證據。

## 素材釋放原語試跑

`tools/benchmark_atlas_lifecycle.gd` 將同一 34 組貼圖載入、放掉所有 ImageTexture 引用、各等待 3 個渲染幀，重複 10 次。本機 Godot Video Mem monitor 基線為 22,858,115 bytes；1024／2048／4096 載入時分別為 283,441,859／297,818,371／322,742,267 bytes，**每次釋放後均回到同一基線，10 次後增長 0 bytes**。Texture Mem monitor 也回到 16,317,963 bytes。可重跑：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --script tools/benchmark_atlas_lifecycle.gd -- local-assets/mixed-heavy/1024 10
```

這只證明獨立 ImageTexture 釋放在此測試中沒有累積；未來的 AssetCatalog、地圖切換、玩家外觀引用、Godot ResourceLoader 快取及 AFK 生命週期還未實作，不能宣稱整個 Client 沒有漏資源。

未測：正式資源管理器的地圖切換與 GPU/RAM 回收、相同資產跨地圖共用、混合玩家裝備、實際 500 掉落圖示、網路事件即時同步、原版影格定位及 Recovered 同機對照。不能從這份渲染代理負載推論 Client／Server 遊戲已達標。

後續已從原版 `CSGOItemDrawLogic::InitItem` 找到真正的地上物品圖 `Interface/ground.spr`，其逐圖／1024／2048／4096 A/B 見 [HK2_GROUND_ART_BENCHMARK.md](HK2_GROUND_ART_BENCHMARK.md)。本頁的 `MS` 負載仍保留為舊測試代理，不改稱真掉落物。

---

<a id="source-hk2-per-enemy-atlas-sizes-md"></a>

## HK2_PER_ENEMY_ATLAS_SIZES.md

*Historical source: `docs/HK2_PER_ENEMY_ATLAS_SIZES.md` — SHA-256 `a6fb3863109342e355fbdd4aa51742d02378e1e1ede9119aaac08ec21258b7f6`*

# 逐怪物多尺寸圖集候選

2026-11-14，AS007 本機實驗。乾淨 HK2 中 50 組來源與 ACT 索引完整的怪物，各自打包 1024／2048／4096；每個群組只包含該外觀的 SPR，保留原始影格、origin 與來源 SHA。另三組特殊怪仍隔離。頁面裁至實際範圍，尺寸是上限。

Godot 4.7.2 headless 逐組解碼 PNG 並建立 ImageTexture，每候選三次取中位。以下解碼時間是 **50 個群組各自中位數的加總**，不是整個 Client 啟動時間；受 OS 快取影響，未量 GPU upload、VRAM 或影格時間。

| 方案 | 頁數 | 實際 RGBA bytes | PNG bytes | 解碼中位加總 ms |
| --- | ---: | ---: | ---: | ---: |
| 全 1024 | 143 | 433,336,404 | 69,819,740 | 1088.447 |
| 全 2048 | 62 | 462,400,864 | 69,289,848 | 1133.400 |
| 全 4096 | 50 | 514,834,484 | 68,792,902 | 1211.927 |
| 逐組最低 RGBA | 133 | 432,604,792 | 69,543,543 | 1086.646 |

候選選擇：42 組 1024；`EN083A/EN100A/EN119A/EN125A` 用 2048；`EN023A/EN085A/EN094A/EN096A` 用 4096。大頁不必然佔更多實際面積，例如 EN023A 的 4096 單頁為 20,710,080 bytes，1024 六頁為 20,720,672 bytes。整體混合尺寸比全 1024 僅省約 0.73 MB、10 頁，不能宣稱巨大效能收益。

目前採取可重現的低資源候選規則：先最低實際 RGBA、相同則較少頁、再較小上限。量測中仍保留每組各尺寸的解碼時間，供正式地圖工作集覆核。先前混合場景未見大頁影格優勢，因此此規則不把少頁數直接等同較快。正式 GPU／多人／地圖串流驗收仍未完成；`runtime_validated=false`。

```powershell
# 完整稽核群組清單的產生方式見 HK2_ENEMY_ATLAS_CATALOG.md
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-all-complete/1024 --group-list local-assets/enemy-complete-groups.txt --atlas-side 1024
# 對 2048、4096 各執行一次相同命令並替換目錄及尺寸
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_enemy_size_candidates.gd -- local-assets/enemy-all-complete/size-benchmark.json local-assets/enemy-all-complete/1024 local-assets/enemy-all-complete/2048 local-assets/enemy-all-complete/4096
py tools/plan_enemy_atlas_sizes.py local-assets/enemy-all-complete/size-benchmark.json local-assets/enemy-all-complete/size-plan.json
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-adaptive --size-plan local-assets/enemy-all-complete/size-plan.json
py tools/compile_enemy_atlas_catalog.py local-assets/enemy-animation-audit.json local-assets/enemy-adaptive local-assets/enemy-adaptive/enemy_catalog.json
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- local-assets/enemy-adaptive/enemy_catalog.json
```

尺寸計畫只決定包裝，從乾淨來源重建時仍逐影格核對像素；動畫方位重用／鏡像由原本 ACT catalog 編譯處理。不得把 50 組全量約 433 MB 頁面一次常駐當成設計目標。所有原件及衍生圖片留在 git 忽略的私有目錄。

---

<a id="source-hk2-sif-research-md"></a>

## HK2_SIF_RESEARCH.md

*Historical source: `docs/HK2_SIF_RESEARCH.md` — SHA-256 `c3f2a3b24d8cd0650a206dd5aabd11687161583773fcb4d67621a7ebc8641901`*

# HK2 地圖物件 SIF 側檔

2026-11-13：乾淨 HK2 共 186 個 `.sif`，全部在 `Map/` 下；55 個以現有 CDE 包裝，解開後與其餘 131 個共用同一結構。每份依序是 1-byte ASCII SPR 名稱長度、該名稱、1-byte 原始旗標、4 個 little-endian signed 32-bit 數值。186/186 嚴格長度解析成功；旗標 1 有 175 份、0 有 11 份。186 個名稱中，184 個在同資料夾有實體 SPR；`Map/BUILDING/SPYGLASS_HOUSE_01_IN.sif` 與 `_OUT.sif` 指向的 `SPYGLASS_HOUSE_IN.spr`、`SPYGLASS_HOUSE_OUT.spr` 在乾淨 HK2 全樹都不存在。

原版參考 `C:\SGO\Recovered\Old\ClientData\Paradise.exe.lst` `.text:00548746`～`005488D8` 直接讀取前兩個長度位元組並尋找 `.sif`。四個數值是否是碰撞框、遮擋框、座標或尺寸，尚未由原版程式證實。`tools/convert_assets.py` 現輸出 `.sif.json`，只保留 `raw_values`，`coordinate_semantics` 為 `null`，避免將未知座標意義寫進 Godot 地圖。

驗證：`py -m unittest tools.tests.test_sif`、全 HK2 186/186 `parse_sif` 成功、186/186 `.sif.json` 已匯出至本機 `local-assets/hk2-frames`。待做：追原版如何使用四個數值及旗標，並將這兩個缺圖列入地圖視覺缺件表。

---

<a id="source-hk2-spr-poc-md"></a>

## HK2_SPR_POC.md

*Historical source: `docs/HK2_SPR_POC.md` — SHA-256 `70876ab96360778ffae7f0720b82f054fd09e429ad91c0ced96a9d1d99804d98`*

# HK2 SPR 保真解碼 PoC 與貼圖分組初測

日期：2026-11-13（Asia/Taipei）。原始素材及匯出圖僅存於本機 `local-assets/`，不進公開 Git。使用 `tools/convert_assets.py` 自行讀取乾淨 HK2；不依賴 Recovered 的程式碼或已匯出圖片。

## 使用

```powershell
py -m pip install -r tools/requirements.txt
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames --category Enemy --limit 20
py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-atlas --category Enemy --limit 20 --layout atlas --atlas-side 512
py -m unittest tools.tests.test_convert_assets
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_asset_probe.gd -- local-assets/hk2-frames/Enemy/EN050C_ND1.json
```

不加 `--category`、`--limit` 可掃描所有 `.spr` 與 `.ACT`；單檔失敗會列出前 20 個錯誤並以非零狀態結束。重跑以來源 SHA-256 和輸出檔存在性略過未變檔。輸出是每 SPR 的 PNG 與 JSON，以及每 ACT 的 `.act.json` 原始動作紀錄；SPR JSON 記錄來源相對路徑／SHA-256、幀順序、原始寬高、原始 origin、透明裁切範圍和 atlas 區域。`C.D.E.` 使用 AES-256-CBC 與 raw DEFLATE 解開後再走同一 SPR 解碼器。沒有證據的 animation、facing、duration、draw-order、events 一律輸出 `null`，不可從檔名臆造；ACT 的 `raw_time` 不擅自轉換為毫秒。

完整執行摘要及所有失敗明細寫入輸出根目錄 `conversion_report.json`，終端只顯示前 20 筆錯誤。轉換器也會保真輸出 `Player/*DrawOrder.dat` 的 `.draw_order.json` 及 `Map/**/*.sif` 的 `.sif.json`；圖層 `+N` 後綴與 SIF 四個數值的語意仍待查。

## 目前實測

HK2 `Enemy` 前 20 個 SPR：分幀與 1024／2048 atlas 模式皆 20/20 成功；`Enemy/EN050C_ND1.spr` 是 C.D.E. 樣本，解出 5 幀。人工建構的 2×1 RGB565/RLE fixture 驗證紅色、不透明／透明像素與負原點；來源圖與匯出圖尚未對原始遊戲畫面做視覺校驗。

先行試跑：`Enemy` 全部 769/769 成功；`Player`、`Interface`、`Map`、`Effect` 各取排序前 20 個 SPR，均 20/20 成功。

2026-11-13 全量更新：上述限制已解除。`py tools/convert_assets.py 'C:\SGO\Stargate Online IE HK2' local-assets/hk2-frames` 掃描 **40,224/40,224 SPR 與 178/178 ACT，零失敗**。其中前次已轉的 37,398 個 SPR 及 53 個 ACT 經 SHA-256 重核走快取；其餘 2,826 個 SPR、125 個 ACT 本次新轉。全量輸出是 153,115 張 PNG（558,642,293 bytes）及 40,402 個 JSON（27,765,411 bytes），僅存在被 Git 忽略的本機目錄。這證明 SPR/ACT 的已觀察格式可全量轉出，**不**證明動畫播放語意或 `Data1.dat` 內資源已覆蓋。如此多單張 PNG 也不應直接全部放入正式 Godot 專案匯入；保真輸出是 QA 中間層。

加入 SIF 支援後，同一完整命令重跑結果：SPR 40,224/40,224、ACT 178/178、SIF 186/186、draw-order 9/9 均由 SHA-256 快取核對，**零失敗**。SIF 的詳細缺件與原始欄位見 [SIF 研究](HK2_SIF_RESEARCH.md)。

Godot 4.7.2 headless 的 `tools/godot_asset_probe.gd` 已讀取 C.D.E. 樣本匯出的 5 頁／5 幀及 atlas 樣本的 1 頁／12 幀，所有區域都在貼圖範圍內。這驗證 Godot 可讀格式與 manifest 邊界，尚不等於 GPU 渲染或多人效能驗證。

| 20 SPR / 136 幀 | PNG 數 | PNG 磁碟總量 | 解碼 RGBA 面積估計 |
|---|---:|---:|---:|
| 分幀原尺寸 | 136 | 4,566,864 bytes | 18,499,136 bytes |
| 每 SPR atlas，512 | 40 | 4,465,544 bytes | 19,150,180 bytes |
| 每 SPR atlas，1024 | 20 | 4,414,383 bytes | 22,229,404 bytes |
| 每 SPR atlas，2048 | 20 | 4,355,315 bytes | 27,434,780 bytes |

這是 PNG 尺寸及像素面積估計，**不是** Godot VRAM、draw calls 或 FPS。512 小 atlas 此樣本增加約 3.5% 的 RGBA 面積，並把貼圖數降至 40；1024／2048 增加約 20%／48%。因此目前預設分幀，`atlas` 僅用於 A/B，沒有將全部怪物合成一張大圖。長期候選是按角色／怪物及同時出場的動作分成受控小組，配合可見範圍載入和卸載；需先查 ACT/SIF、方向、裝備疊層，再在同等場景／怪物數下量測。動作資料未確定前，無法宣稱動畫正確或比 Recovered 更快。

Godot 4.7.2 headless 以 `tools/benchmark_asset_layout.gd` 在同一 20 SPR 樣本重複載入 5 次，Image PNG 解碼中位數：分幀 83.584ms（136 頁）、512 atlas 66.662ms（40 頁）；單次試跑且含 OS 檔案快取波動，僅支持此樣本 CPU 讀檔成本較低，**不**代表 GPU VRAM、draw calls、實際 300 怪 FPS。可重跑命令：

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_asset_layout.gd -- local-assets/bench-frames/Enemy
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/benchmark_asset_layout.gd -- local-assets/hk2-atlas-512/Enemy
```

---

<a id="source-hk2-ui-references-md"></a>

## HK2_UI_REFERENCES.md

*Historical source: `docs/HK2_UI_REFERENCES.md` — SHA-256 `4b17eb9c9aa84c657a1a0340228d6b44f3ebfd258ea4c1e583f11049628e6330`*

# HK2 介面檔的 SPR 引用盤點

日期：2026-11-13（Asia/Taipei）。證據等級：DIRECT（本機 HK2 檔頭、解包後字串與實體檔名）；版面語意仍未知。

HK2 `Interface` 有 20 個 ITF，其中 5 個以 `C.D.E.` 封裝、15 個明文。`tools/inspect_itf.py` 對兩種格式只抽取明確出現的 `.spr` 名稱，不猜控件座標與切片規則。可重跑：

```powershell
py tools/inspect_itf.py 'C:\SGO\Stargate Online IE HK2'
py -m unittest tools.tests.test_inspect_itf
```

20 個 ITF 共出現 122 筆 SPR 引用（同名在不同 ITF 重複計算）；按不分大小寫檔名比對，63 筆在 `Interface` 實體檔可找到，另外 59 筆在 HK2 任何實體 SPR 目錄都找不到。這些缺件可能在 `Data1.dat`、外部更新包或別的版本；尚無證據可選定原因。`006_StatusLite_G.itf` 解包後包含 `O:\Star_Gate_Online\Working\617Beta\Client\Interface\...` 內部路徑，屬版本線索，不能視為發行版本或權利證明。

因此金色舊版 UI 只能先做版面研究與原創 Control/Theme；不能宣稱 20 個 ITF 足以完整還原所有畫面。現代 UI 與金色 UI 需共用狀態／命令，並依實際解析度與 viewport 佈局。參考 [SPEC.md](../SPEC.md) 的 F002/F008 與 ADR-013。

---

<a id="source-legacy-asset-evidence-20261113-md"></a>

## LEGACY_ASSET_EVIDENCE_20261113.md

*Historical source: `docs/LEGACY_ASSET_EVIDENCE_20261113.md` — SHA-256 `1b11a5262d604c1d88c728ac87354345438c1873368477043fc32994a2959fa8`*

# 原版素材格式、載入呼叫鏈與來源缺口調查（2026-11-13）

> 證據基準：SGTest/main `bd652f22b0955cd74e63d6061e999c35a3779a4d`；**唯讀** TestGoGo `feature/local-server-net8`，提交 `0f37c5268ceeac1144ed13556f3aeb191b93711e`。反編譯檔 `Old/ClientData/Paradise.exe.lst`：Git blob `16710727227dc5ff8d623ea2acab17cc468fc129`，大小 40,117,423 bytes。直接讀取該 blob 並定位組語、實際抓取四個 SPR 與一個 SIF 位元組資料。未取得使用者本機完整安裝檔，**沒有進行原版執行畫面比對**。

## 1. 結論及證據分級

- **DIRECT（原版組語／二進位直接可見）**：舊版 `Paradise.exe` 實作 `xnFileRead`（先鬆散檔、後群組資料檢索）、`xnSpr::LoadSpr`（尾端影格數與偏移表）、`DecodeFrameLine`（透明跳過／實色／半透明像素串列）、`LzssDecompress`。角色顯示會依角色類型、動作、方向、裝備部件組合檔名與資源。UI 會載入 `.itf`；可看到大量 `.spr`、`.act` 配對引用。
- **DIRECT（Repo 樣本）**：四個 NPC SPR 樣本皆符合「影格資料＋檔尾影格索引」結構；所測掃描列命令皆完整走訪。NPC203.sif 記錄檔名與四個整數，整數與同名 SPR 首影格標頭對應。
- **HYPOTHESIS / NOT VERIFIED**：16-bit 像素對映為 RGB565 可作驗證用近似，尚未使用原版畫面、原始渲染像素或真機色彩配置校驗。人物方向數、影格時長、動作順序、座標原點／pivot、Z 層序、裝備疊圖、完整 `.act` 格式、所有 `.itd/.itf` 欄位、封裝副檔名與後期金色 UI 檔案來源均未證實。
- **BLOCKED**：原版後期正式 Client 安裝包、完整 Player/Effect/Interface/Map 圖像庫、`.act` 與 `.itf` 真實檔案、對照影片及資產發行授權均缺少。不能使用 NPC 單影格解碼當作角色動畫完成的證據。

### 版本警示

`Paradise.exe.lst` 開頭顯示 IDA 反編譯輸入路徑 `...\\SGO Paradise\\program\\Paradise.exe`、PE x86、時間戳 2004-11-24，不能因此假定與「後期《星夢 Online》金黃色 UI」同版本。原版同名副檔名未必跨年代相容。

## 2. 直接實測原始檔案與 SPR 結構

| TestGoGo 原始路徑 | Git Blob SHA | 長度 | 結構性驗證 |
| --- | --- | ---: | --- |
| `Old/ClientData/NPC/NPC203.spr` | `980d15e03764e375ec14f91caa2e461e4a212e48` | 12014 | 53×109；109/109 列走訪、無不明 token |
| `Old/ClientData/NPC/NPC204.spr` | `0169f16d0cfa6b32d594f221e419e5dcfc30435c` | 11572 | 53×99；99/99 列走訪、無不明 token |
| `Old/ClientData/NPC/NPC251.spr` | `838c63e8591797f8b028771b375dfb87cb95fdbc` | 56652 | 178×322；322/322 列走訪、無不明 token |
| `Old/ClientData/NPC/NPC207-R.spr` | `34bb6fca7ad7e9d3d9a05668b0decf912d2f1edd` | 10768 | 52×97；97/97 列走訪、無不明 token |
| `Old/ClientData/NPC/NPC203.sif` | `3247e84e7ae5865fe727c3c7fe58cd5639aa0d26` | 28 | 包含 `NPC203.spr` 路徑與 `21,89,53,109` 四整數 |

**所測 XN SPR 變體結構（小端序）**：

| Offset | 欄位 | 可信度 |
| --- | --- | --- |
| `file_size - 4` | `uint32 frame_count`（四個樣本均為 1） | DIRECT，亦見 0x00552FFF |
| `file_size - 4 - 4*frame_count` | 每影格 `uint32 frame_offset` 陣列，指向檔案內原始影格 | DIRECT，亦見 0x00553006-0x0055307B |
| `frame_start + 0, +4` | `uint32 width, height` | DIRECT，尺寸與 4×列索引驗算相符 |
| `frame_start + 8, +12` | 兩個 `int32` 原始位置相關數值（目前稱 raw_anchor_x/y；語意未驗證） | DIRECT 值、HYPOTHESIS 名稱 |
| `frame_start + 16` | `height` 個 `uint32`，每列相對 frame_start 的 row_offset | DIRECT，亦見 0x00553ED6 |
| `row_offset` | 16-bit 命令與像素資料，必須在下一列開頭前結束 | DIRECT，見下表 |

**掃描列 token 的觀察（保留其原始值）**：

| `command & 0xC000` | 行為 | 載荷 |
| --- | --- | --- |
| `0x8000` | 跳過透明像素 `command & 0x3FFF`；`0x8000` 恰好代表本列結束 | 無 |
| `0x0000` | 直接拷貝 `length` 個 16-bit 色彩像素；不含顯式 Alpha | `length * 2` bytes |
| `0x4000` | 讀 16-bit 局部 Alpha，接著 `length` 個 16-bit 色彩像素 | `2 + length*2` bytes |
| `0xC000` | 未由樣本與程式流程證明，嚴格解析器拒收 | 不推測 |

對應 `DecodeFrameLine` @ `0x00553EB0` 的邏輯：比較 `0x8000`、`0x4000`，將資料複製到像素／Alpha 緩衝區；帶透明度的 run 以輸入 Alpha 與呼叫時的整體不透明度計算。工具只保留檔案的原始局部 Alpha、預設不套整體不透明度；**此步為素材獨立資料解碼，不等於確認原版最終混合公式**。

**`.sif` 已知樣本**：28 bytes：`0x0A` + ASCII `NPC203.spr` + `0x01` + LE32 `21,89,53,109`。前兩個值與 SPR 第三／第四個整數相同，後兩個值與寬高相同。合理推測它是圖像描述／裁切、放置位置索引；**第一個位元組、`0x01` 的實際型別及欄位順序仍需對照 `CELBldg2D` 讀取組語與其他檔案。** 不能把 `.sif` 說成影格播放速度檔。

現有工具 `tools/legacy_spr_decoder.py`：僅支持前述 XN footer row-run 變體，採 fail-closed。只在輸出資料夾為空、且位於來源目錄之外時產生 PNG；不會寫回素材來源。JSON 明示 `animation_verified:false`，未證明的 `direction/action/duration_ms/offset/z_order/events` 一律 `null`，保留兩個原始位置整數。不能將 `PROVISIONAL_RGB565_NOT_VISUALLY_VERIFIED` 宣稱為已完成保真。

## 3. 真正的載入／解包／解碼／動畫呼叫鏈（.lst 直接定位）

| 類別 | 函式／關鍵位址 | 組語提供的觀察 |
| --- | --- | --- |
| 讀檔入口 | `xnFileRead` `0x00552090`；`0x00552128`、`0x0055219A` | 優先使用 `fopen/fread` 讀取鬆散檔案 |
| 讀封裝候補 | `xnFileRead` `0x0055223E..0x005522D3` | `GetFolder`、`xnList::Search`、`FileData::ReadBuf` |
| 開群組 | `CELFileIO::CELFileOpenGroup` `0x00506BC0` → `xnFileOpenGroup` `0x00552980` | 群組路徑存在；不等於 Git repo 有相關封包 |
| 解壓 | `xnFileOpenGroup` `0x00552A5B`、`GroupData::Open` `0x00551C3D`、`FileData::ReadBuf` `0x00551886` → `LzssDecompress` `0x0055A0E0` | 群組索引或檔案資料會呼叫 LZSS 解壓；外部包副檔名及實際資料尚未測定 |
| SPR 資源入口 | `CELXnLib::LoadSprite` `0x00542BD4`，call `xnSpr::LoadSpr` `0x00542BE7` | 可先載完整路徑，必要時嘗試檔名補救 |
| SPR 索引修正 | `xnSpr::LoadSpr` `0x00552FC0` → `xnFileRead` `0x00552FE0`；尾數 `0x00552FFF`；位移指標 `0x00553006-0x005530A9` | 實際資料末尾影格數＋影格 offset，再修正每一列 offset |
| SPR 特殊版本 | `xnSpr::LoadSpr` `0x0055302D-0x00553055`、`CELXnLib::LoadSprite` `0x00542CA0-0x00542CB9` | 檢查 `Ver` 版本標記／version==8 後複製 sprite；尚未收集該版本樣本 |
| 逐列繪製 | `DecodeFrameLine` `0x00553EB0`；`xnSpr::Draw` `0x00553FB0` | 逐列色彩及 Alpha 緩衝區產生後繪製；不能從此推定 GPU cache 生命週期 |
| 顏色轉換 | `CELXnLib::Convert` `0x00542CD0` → `ConvertFrame` `0x00542D55` → `ConvertBuffer` `0x00542E1F` | 能依條件轉換 16-bit 色彩；RGB565 對照仍未完整驗證 |
| 建築／靜態物件描述 | `CELBldg2D` `0x005485BF` 中 `0x0054882A, 0x00548A4E` 將副檔名換成 `.sif`；`CELStaticObj2D` `0x0054A172` 中 `0x0054A2DA` | 原版確實會讀取獨立 SIF，並非只由 SPR 頭推算畫面 |
| 靜態物件動作 | `CELStaticObj2D` `0x0054A4C6` 組合 `.act` → `CELDrawStaticObjLogic2D::InitAction` `0x005514C7` | 動作描述與圖像載入分離；ACT 內部欄位未驗證 |
| UI 格式 | `CConnectingItf::Create` `0x00425ABE` 使用 `011_LOGIN_LOADING.itf`；`CELInterface::Create` `0x0051E972` → `CELFileIO::CELFileRead` `0x0051E9F5` | `.itf` 是原版介面讀入檔，缺真檔不能推稱圖片資源／壓縮格式 |
| ITD 資料 | `0x0040371E` 路徑 `Item\\petcli.ITD`，`0x00404D92` 路徑 `Item\\item.itd` | `.itd` 是物品／寵物相關資料輸入，不等於動作與顯示影格圖 |

**人物方向、動作與紙娃娃路徑（DIRECT 函式及路徑、細部語意部分未驗）**：

`CSGOChar::InitActDisplay` `0x004C1143` → `CSGOCharDrawLogic::InitActDisplay`；`CSGOCharDrawLogic::InitAction` `0x004C4C95` 依情況呼叫 `InitNormalAction` `0x004C4339`、`InitRidingAction` `0x004C4A50`、`InitArmorAction` `0x004C4BB0`。正常動作會呼叫 `DirectionMapping` `0x004C263F`（呼叫點 0x004C4746），`SetMotionDraw` `0x004C2D1D`（呼叫點 0x004C47AA）。`SetDirection` `0x004C24EA` 呼叫 `DirectionMapping`、`SetMotionDraw`、`DrawOrderMapping`；裝備會從 `SmartLoadParts` `0x004C30C8`、`SmartLoadWeapon` `0x004C2E42` 組裝路徑 `Player\\%s\\%s\\%s` 與 `%s%s%d.spr`。武器效果亦參照 `Effect\\Weapon_Effect\\... .spr/.ACT`。游標 `CSGOUim::RunMapCursorEffect` `0x0041A61C` 以 `C_NORMAL.ACT` / `C_NORMAL.SPR` 等配對載入。這能證實檔名由動作及部件條件組合，**尚不能推導出所有實際方向 ID、每方向影格數或時長**。

## 4. 來源可用性／缺漏矩陣（AS003 的現階段盤點）

參考倉目前 `Old/ClientData` 的 Git tree：**199 SPR、12 SIF、4 ITD、10 DAT、3 CSV、1 LST；零 ITF、零 ACT**。它只是原版資料的零散子集，非完整 Client 安裝檔。

| 類別 | 已有目錄／依據 | 缺件／可用結論 | 等級 |
| --- | --- | --- | --- |
| 人物、職業、方向、站立／走／攻／受擊／死亡 | 組語 `Player\\%s\\%s\\%s` 和動作／方向對映呼叫鏈 | Old/ClientData 無完整 `Player` 圖像及 ACT，無法重現角色 | DIRECT 路徑／BLOCKED 實測 |
| 武器、裝備紙娃娃疊圖 | `SmartLoadParts`、`SmartLoadWeapon`、`DrawOrderMapping` | 缺角色裝備 SPR/ACT 及實際疊圖對照 | DIRECT 路徑／BLOCKED |
| NPC | `Old/ClientData/NPC/` 有少量 SPR/SIF；四份 SPR 結構驗證 | 僅部分靜態樣本，不能推為全 NPC 動畫 | DIRECT |
| 怪物 | `Old/ClientData/Enemy/EnemyTable.dat` | 沒有完整 Enemy SPR/ACT，缺原版怪物多方向動畫 | DIRECT 目錄／BLOCKED |
| 機甲／寵物 | `Old/ClientData/MS/Horse*.csv`、`Item/petcli.ITD` | 只有資料索引，缺原件圖像／動作表 | DIRECT 目錄／BLOCKED |
| 技能、武器／受擊特效 | `Old/ClientData/Interface/skill*.dat`；`Effect\\Weapon_Effect\\...` 路徑 | 缺大量 Effect SPR/ACT 與顯示時序 | DIRECT 路徑／BLOCKED |
| 物品 | `Old/ClientData/Item/Item*.itd` | 資料表存在，物品 icon 原圖／ITD schema 尚未證實 | DIRECT 目錄／UNVERIFIED |
| 地圖／建築 | 服務端 `Old/服務端/Dat/Map/Map13` 有 Blocking/NPC/Enemy DAT；Client 建築 SIF 呼叫 | 地圖畫面 tiles/chunks/建築美術缺件，Blocking 不是渲染貼圖 | DIRECT 目錄／BLOCKED |
| 後期金黃色 UI | 原版反編譯可見多個 `.itf` 檔名；`Interface\\...spr` | Git 無 `.itf` 實檔及後期美術；2004 客戶端未證實是目標版本 | DIRECT 檔名字串／BLOCKED |
| 音效／BGM | 使用者尚未提供完整安裝資料 | 對應音檔及第三方權利資訊缺失 | BLOCKED |
| 封裝／更新檔 | 原版 `xnFileOpenGroup`、`LzssDecompress` 的函式存在 | Git 中未找到可驗證的正式 Client archive；封包命名與目錄待查 | DIRECT 程式／BLOCKED 檔案 |

## 5. 程式、測試與可重現操作

在 SGTest 有：

- `tools/legacy_asset_inventory.py`：新增 `header_hex` 與 `--header-bytes`（0～64，預設 16），保留 SHA256 串流摘要，報表僅相對路徑；`schema_version=2`。與原檔分開輸出，略過 symlink。**本機完整安裝目錄未提供，因此無法填入實際全檔案 SHA256 或版本。**
- `tools/legacy_spr_decoder.py`：`parse_spr` 嚴格讀取已確認 XN 變體、保留 raw anchor、輸出逐影格 RGBA PNG 與 `animation.json`。拒收未知 token、損壞 row/offset、溢出、覆寫既有輸出；未知動畫欄位 `null`。
- `tools/legacy_asset_viewer.py`：獨立 Tkinter 預覽 PNG + JSON；方向／動作（有欄位才可選）、上一格、下一格、暫時播放、顯示 raw anchor/offset/z-order/hash。**當原始幀時間未知時使用畫面預覽 100ms，絕不可回寫成原版速率；GUI 未在有桌面的環境逐項人工驗收。**
- `tools/tests/test_legacy_spr_decoder.py`、`test_legacy_asset_viewer.py`、`test_legacy_asset_inventory.py`：僅自製 fixture，**GitHub 不包含任何原版藝術資料**。

```powershell
# 只讀盤點你的合法持有、具備完整檔案的遊戲安裝路徑
python tools/legacy_asset_inventory.py --root "C:\SGO\OriginalClient" --output reports/legacy-assets.json --sha256 --header-bytes 32

# 僅在你的本機使用真實 SPR；輸出必須在原件目錄之外
python tools/legacy_spr_decoder.py --input "C:\SGO\OriginalClient\NPC\NPC203.spr" --output "C:\SGO\reports\npc203"

# 會需要顯示器以及 Python Tkinter；只預覽，不推論動作或原版時長
python tools/legacy_asset_viewer.py "C:\SGO\reports\npc203\animation.json"

python -m unittest discover -s tools/tests -p "test_*.py" -v
```

測試層級嚴格區分：已實際在 Python 本機執行自製 SPR 格式、PNG、負面路徑及 JSON 篩選 **6 tests PASS**；對上述四個真 SPR 做逐列結構解析 **109+99+322+97 列均未碰到未識別 token**（不等於視覺驗證）。**inventory 修改後回歸及 GitHub Actions 已有可追溯成功紀錄**：[整合分支 Actions #43430878232](https://github.com/kevin840307/SGTest/actions/runs/43430878232) 在提交 `a4a3a3a165bada4e4841bd86ce166ee27fb68eef` 執行 `python3 -m unittest discover -s tools/tests -p 'test_*.py' -v`，**10 tests PASS**（僅合成 fixture，含 inventory 4 tests）。GUI 真機、原版角色完整動畫、與原版遊戲截圖逐格對照仍 **NOT VERIFIED**；原件／權利仍 **BLOCKED**。

## 6. 後續 Session 直接接手（依 AS 順序）

1. **AS001**：在使用者合法持有的原版後期完整安裝目錄與更新檔執行上方 inventory；記錄每個版本 manifest SHA256、相對路徑、magic bytes，嚴禁上傳原件。對照此報告的來源 repo tree，找實際 Player/Effect/Interface/Map、ACT、ITF、封包與版本資訊。
2. **AS002**：以位址 `0x00552090`、`0x00551B60`、`0x0055A0E0` 深入分析封包格式及路徑，補群組檔頭樣本；深查 `CSGOCharDrawLogic::InitNormalAction`、`SetDirection`、`DirectionMapping`、`DrawOrderMapping`、`CELDrawStaticObjLogic2D::InitAction`，還原真正動作/方向/時長/疊層。
3. **AS003**：依來源版號補齊各類原件檔與缺件，特別是人物完整方向×五動作、機甲、金黃色 UI。
4. **AS004/AS005**：先驗證 RGB565 及局部 Alpha 與原版畫面完全一致；以實際至少一套完整人物 SPR+ACT+裝備建立多影格、動作、方向、pivot、z-order、時長、事件 PoC；未證實欄位不能亂填。
5. **AS006**：Viewer 加上 anchor 可視化、校正座標、真實時長、圖層；使用錄影逐格比對。再談 batch export、Godot contract / performance（AS007）。
6. **AS008/AS009**：授權未確認維持 BLOCKED；只有來源清單及可重現 tests/証據齊備才決定是否通過 M0 Gate。不要先建立正式 Client／Server／Atlas。

**重要：本文件只是本輪可實證的進度，不代表 AS001～AS009 已完成或 M0 Gate 已通過。**

---

<a id="source-legacy-reference-md"></a>

## LEGACY_REFERENCE.md

*Historical source: `docs/LEGACY_REFERENCE.md` — SHA-256 `705975624d72dd0bd383ad6136e9b5dc9beb0b30c52a7af4b1ae0e9483c33f20`*

# Old 反編譯程式、舊 Client/Server/地圖/資料庫研究指引

## 1. 適用範圍及版本

來源入口：https://github.com/kevin840307/SGO/tree/feature/local-server-net8
目前導向：https://github.com/kevin840307/TestGoGo/tree/feature/local-server-net8
2026-11-12 核對 SHA：aee7ec7fff9e446452655cf6d2be460dfd7a88f1
這份 SHA 是本次文件研究錨點，非永久最新版本；每次接手重新抓最新 HEAD 並檢視變更。

需分清：
- ORIGINAL DIRECT：直接在 *.lst/原始二進位/SQL 中可定位/重現的證據
- REPO AUDIT：來源 repo docs 裡先前人的調查結論，未在本次全部重算
- CURRENT IMPLEMENTATION：來源現有 Godot/.NET 程式，不等於原版正確行為
- DESIGN DECISION：SGTest 新設計，不能假裝 Old 有相同資料模型
- UNVERIFIED：缺原始封包 timestamp/有效測試資產/編碼解析的推論

## 2. 指定必查路徑

Old/ClientData/Paradise.exe.lst：2004 原始 Client 可見的反組譯清單；CSGOMap::MessageMap、CSGOChar::SetMoveloop、CSGOChar::MoveStep、CSGOEnem::movelogic、CSGOMap::OnCharSyncDest/OnEnemyMove/OnCharSyncPosition；需要繼續查動作/方向/紙娃娃/State 更新鏈。
Old/服務端/Core.dll.lst：CDServer::InitThread（FPS_BASE=25、FPS_CURRENT=10、FPS_CLIENT=50 在既有 audit 記錄）、CDServer::run 更新排程。
Old/服務端/plugins/SGOMapService.dll.lst：CDMapService2D::OnPlayerMove/OnNPCMove/Update/SendSyncDestPac/SendSyncPosition，以及視野/傳送事件。
Old/服務端/plugins/SGOPlayer.dll.lst：SGOPlayer::Update、CDPlayerSys::Update/UpdateClient/UpdateOwnClient；戰鬥/方向/角色資料。
Old/服務端/Dat/Map/Map13、Map15：Blocking.dat、Enemy.dat、NPC.dat、safe_area.dat；Map07 等多張地圖可驗證例外。
Old/服務端/Dat/Item：Weapon.itd、Equip.itd、General.itd、Recovery.itd、Booster.itd；老資料不是單純 UTF-8 文本。
Old/ClientData/Item：Item.itd、Item_cht.itd、Item_en.itd，需確定性 decoder。
Old/資料庫/game.sql：equipment/inventory 等 schema；與當前 server 資料與 migration 交叉核對。
來源 client/ 的 player.gd、network_player.gd、scripts/remote_player_motion.gd、scripts/remote_player_state_policy.gd、scripts/network_manager.gd、scripts/network_entity_manager.gd。
來源 server/ 的 SharedMapAiTicker.cs、ServerBroadcastHub.cs、Transport/WebSockets、SqliteGameStore 及對應 Smoke/LoadProbes。
來源 docs/multiplayer-legacy-movement-audit.md、docs/legacy-movement-parity-20261111.md、docs/remote-player-hit-motion-20261111.md、docs/multiplayer-motion-v2-20261111.md、docs/legacy-paradise-lst-vs-godot-density.md。

## 3. 目前已有證據及限制

| 主題 | 既有證據 | 證據層級 | 新設計意義 |
|---|---|---|---|
| 原版 Client 連續步進 | Paradise.exe CSGOChar::MoveStep 0x004BEA62、CSGOEnem::movelogic 0x004C9D69；既有 docs 解讀 | REPO AUDIT | 新角色依 path/速度 + Server timeline，不以每個 Move 包直接瞬移 |
| 原版封包分類 | A106/A107/A108/A10D，相關函式地址於 docs/multiplayer-legacy-movement-audit.md | REPO AUDIT | 開始/移動路段與權威 correction 分離；不直接使用舊 type ID |
| 原版頻率 | Core.dll 預設 Current FPS=10、Client FPS=50 | REPO AUDIT | 這是更新 Tick，**不能據此說每100ms必有一包** |
| 現有 Server | SharedMapAiTicker 用250ms共用 monster AI；部分持鎖/SQLite Damage write 曾造成尖峰 | CURRENT IMPLEMENTATION/LOG AUDIT | 新版由 Map Actor單寫者 + DB非熱路徑，而非只調頻率 |
| 現有 Remote Motion | Godot remote_player_motion.gd 使用 SnapshotSpanSeconds/SpeedTime、有界外推及命中動畫保護 | CURRENT IMPLEMENTATION | 可參考算法與重現案例，但新設計改成 ServerTick 序列 timeline |
| 地圖網格 | 已有 game-editor README 指 floor(world/16)、flag mask、cell*16+8 | REPO AUDIT | 需要 Old Blocking.dat 真實位元/座標/例外 fixture |
| 原版渲染 | Paradise.exe.lst 觀察到 Direct3DCreate8、Sprite VM Cache、index/vertex buffer | REPO AUDIT | 原版使用獨立較低階資源層；**不能因此推論實際 FPS/批次數量** |
| Item +N ID | 舊 Item.itd 為二進位/非 UTF8，需要解析 | UNVERIFIED | 不能用「連號就代表+1」直接把物品合併 |
| 攻擊/面向 | 舊反組譯與素材方向意義尚未逐函式核對 | UNVERIFIED | 請優先執行 A002，明確方向數/面向/Action 結束條件 |

## 4. 分析交付形式

每項研究生成 evidence/motion.csv、evidence/items.csv、evidence/maps.csv（先用原創/脫敏 fixture），包含 source_repo、source_commit、source_path、function/address/record_offset、field、observed、inference、confidence、next_test。
以文件「事實 / 推論 / 設計」三欄撰寫。不能單憑網路文章、先前 ChatGPT 回答或反編譯函式名推導完整事實。
若 Old 串流與實際封包頻率資料不存在：可仍設計更好的新版本、設假設/負載/模擬測試，不能聲稱「原版確定每X毫秒送包」。

## 5. 研究順序

R1 玩家（自己的和其他人的）位置/目的地/停止/方向/跑走/攻擊/受擊/死亡/順移阻擋／相機 AOI。
R2 Monster巡邏/追擊/觸發距離/受擊/死亡/物件更新。
R3 Item +N ID結構與物品/強化/任務/掉落/商店索引。
R4 Map Blocking/Enemy/NPC/safe area 與繪圖資源格式。
R5 Pet/Mecha/Inventory/Shop/Quest/PVP、遷移與參考設計。

## 6. 法律與維護

目前未確定來源倉庫及相關舊遊戲資產授權；GitHub 可見並非可重用授權。以研究觀察、功能需求及自行撰寫乾淨介面/演算法，不在 SGTest 散布大型 .lst、素材/圖片/音樂、舊商標或未授權文本。授權確定後，亦要記來源/授權/範圍/公告義務。

---

