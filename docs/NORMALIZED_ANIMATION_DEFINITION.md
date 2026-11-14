# Normalized Animation Definition v2（ASSET201）

日期：2026-11-14。狀態：**ASSET201 PASS / AS005 視覺逐格驗證仍 NOT VERIFIED**。

## 目的

把動畫語意從 Atlas 排版中抽離。Runtime 不應從 page/UV、圖片檔名或 Render FPS 推導行為；標準定義只描述：

- entity / kind
- behavior
- 8 logical facings
- source clip alias / mirror
- source frame / source index
- pivot / trim / pixel hash
- ACT timeline / loop / raw event
- character draw-order raw layers / expanded z-order slots

Atlas、page、region、streaming owner、GPU handle 屬於後續 ASSET202～204，不進此契約。

## Before → After

Before：
- 舊 runtime asset JSON 主要服務 Atlas，方向 alias、ACT timing 與人物 z-order 分散。
- 9 個 Map13 monster 舊 runtime 與 reference-normalized facing mapping 合計有 182 個差異；不能把舊 alias 當 source of truth。
- 人物 definition 只有 `draw_order_key`，Runtime 若要排序仍需重新解讀 legacy `CharDrawOrder.dat`。

After：
- `tools/animation_definition.py` 輸出 `sgtest.animation` **schema v2**。
- 每個 character facing 同時保存 `draw_order_key`、原始 `draw_layers` 與驗證後 `draw_slots`。
- `Body+2 / L-Arm+2 / R-Arm+2 / Leg+2` 等雙槽位依已稽核規則確定性展開；未知 token fail closed。
- v1 definition 明確拒絕，避免缺 z-order 的資料靜默進入新 Runtime。
- `client/scripts/animation_definition.gd` 只讀 immutable definition；相同 clip + flip + draw metadata 才共享 descriptor。
- 動畫取樣使用 verified ACT fixed timeline，不依 Render FPS 或 world movement speed 累積。

## v2 核心資料

`frames[]`：
- `id`
- `source`
- `source_index`
- `size`
- `pivot`
- `trim`
- `pixel_sha256`

`behaviors[code]`：
- `legacy_action_id`
- `act_key`
- `raw_loop_flag`
- `loop`
- `timeline[]`：`frame_index / raw_time / default_ticks`
- `events[]`
- `facings[1..8]`

每個 facing：
- `clip`
- `source_facing`
- `flip_h`
- `draw_order_key`
- `draw_layers`
- `draw_slots`

Monster / single-view 沒有人物 z-order 時，draw 欄位必須為 null；不能混入半套資料。

## 真素材驗證

`tools/audit_normalized_definitions.py` 以 `SGTest/.cache/client` 的私有原始素材重建：

- 9 個 Map13 monster
- Boy 6 個 core paper-doll parts
- Boy explicit single-view death
- 共 16 definitions
- 992 logical facings
- 624 character draw-order views
- `normalized_z_order_verified=true`
- 0 schema failure
- 舊 runtime facing mapping 與 normalized reference 有 182 個差異，完整保留在本機 audit report，不自動接受舊值

另以 `tools/audit_character_standard.py` 對四種族 core 資料重跑：

| Race | Sources | Logical states | Issues | Missing draw-order |
|---|---:|---:|---:|---:|
| A | 282 | 104 | 0 | 0 |
| B | 282 | 104 | 0 | 0 |
| G | 282 | 104 | 0 | 0 |
| R | 235 | 104 | 0 | 0 |

這證明 core source/action/facing/draw-order 契約可確定性重建；**不等於原版畫面逐格視覺已驗證**。

## 回歸

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
cd tools
py -m unittest test_animation_definition -q
cd ..
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_animation_definition.gd
py tools/audit_character_standard.py .cache/client local-assets/player-core-audit.json
py tools/audit_normalized_definitions.py --source .cache/client --runtime-assets client/assets --output reports/normalized --report reports/normalized_definition_audit.json
```

本次結果：
- `tools/tests`: 89 PASS
- `test_animation_definition`: 10 PASS
- Godot normalized definition consumer: PASS
- normalized true-asset audit: 16/16 PASS
- four-race core audit: complete=true

## 邊界

- AS005：原版同版本畫面、pivot、mirror、alpha、紙娃娃實際遮擋逐格比對仍未完成。
- AS008：原版媒體公開發布權仍阻塞；原始/衍生圖片不提交 Git。
- v2 目前是資料契約；舊 Runtime atlas 不直接被「就地升級」。ASSET202/203 會用同一 v2 definition 比較 Atlas 候選，ASSET204 才建立正式 Runtime store。
- 182 個 old-runtime direction differences 不是自動修圖結果；它們代表新 Runtime migration 必須採 reference-normalized mapping 並保留視覺驗收 Gate。
