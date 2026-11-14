# Monster Content Workflow — ASSET205

日期：2026-11-14。狀態：**ASSET205 PASS / RUNTIME206 FULL MATRIX STILL NOT VERIFIED**。

## 目標

新增一種怪物時，不要求修改 C#、GDScript 或手工改生成 PNG/JSON。正式 source of truth 是 `content/monster_recipes/*.monster.json`；原始 SPR/ACT 維持 private，Runtime 資產由工具確定性重建。

一份 recipe 同時定義：

- map id
- monster definition id
- sprite identity
- runtime stats
- spawn positions / source flags
- 是否明確 replace 已有 definition

工具 `tools/monster_workflow.py` 會 fail-closed 驗證 source、ACT、8方位、stats、collision，再產 normalized definition、private preview、population v2，並**重建該 map 的共享 lifecycle sprite bundle** 與 Server copy。新增怪物不再建立新的 per-monster production atlas 分類。

## Recipe v1

```json
{
  "version": 1,
  "map_id": 13,
  "replace": false,
  "definition": {
    "id": 81,
    "sprite": "EN010A",
    "stats": {
      "hp": 120,
      "damage": 12,
      "move_speed": 72,
      "aggro_range": 520,
      "attack_range": 75,
      "chase_stop_range": 45,
      "attack_interval_seconds": 0.9,
      "respawn_seconds": 3
    }
  },
  "spawns": [{"p": [6428, 4881], "source_flags": 0}]
}
```

格式範例：`content/monster_recipes/example.monster.json.example`。只有 `*.monster.json` 會被 full rebuild 自動套用。

## 一次命令流程

先驗證，不修改 population：

```powershell
python tools/monster_workflow.py content/monster_recipes/my_monster.monster.json --validate-only
```

正式套用：

```powershell
python tools/monster_workflow.py content/monster_recipes/my_monster.monster.json
python tools/export_server_content.py
```

一般不需第二行：workflow 本身會同步該 map 的 population 到 `server/content`；完整 `rebuild_assets.ps1` 仍會在最後統一 export collision/map contract。

## Source / animation Gate

sprite `ENxxxx` 會對應 private extracted source：

- `enemy/<sprite>_*.spr`
- `enemy/<sprite>.act`

工具固定要求 monster 5 個 behavior：`NS / NW / FA / NH / ND`，以 `sgtest.animation/v2` 與 `legacy_direction.py` 驗證 8 logical facing，共 **40 logical views**。缺 ACT、缺必要 SPR、timeline frame 越界、pivot/hash 不一致都直接失敗。

Animation source of truth 仍是 `sgtest.animation/v2`。Production packing 改由 [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md) 的 lifecycle policy：同 map definitions 一起重建 `bundles/map/<map>/sprites.json`，目前經 1024/2048 A/B 選 2048。512/1024/2048/4096 是 builder 內部候選，不是對外 Runtime 類型；不能手改 atlas。

## Private preview

每次 validate/apply 都會在 `reports/monster_workflow/<sprite>/` 產：

- `definition.json`
- `preview.png`
- `report.json`

preview 是 5 actions × 8 facings 的 40-view contact sheet，依 source frame pivot 與 mirror 繪製，中心十字標示 origin。`reports/` 已 gitignore，因此不會把原版衍生圖公開提交。

真 HK2 EN010A smoke：16 source files、5 actions、40 logical views，preview **768×495**，spawn `[6428,4881]` 原位 walkable。

## Spawn collision Gate

`blocking.bin` 使用與 Server `CollisionGrid` 相同規則：16px cell、`cell & 7 == 0` 才可走。

- 預設：blocked/out-of-map spawn 直接拒絕，並回報最近可走建議。
- 只有明確 `--snap-spawns` 才自動移到 64-cell 搜尋範圍內最近 open cell。
- report 永遠保存 `requested_p / actual p / snapped`，不做靜默修正。

## Population v2：每 definition 的 Runtime Stats

ASSET205 把 population contract 從 v1 升成 v2。v1 Server 仍可讀，並套 legacy-equivalent defaults；v2 每個 definition 必須有明確 stats：

| 欄位 | 原 Map13 預設 |
|---|---:|
| hp | 30 |
| damage | 10 |
| move_speed | 65 |
| aggro_range | 500 |
| attack_range | 70 |
| chase_stop_range | 45 |
| attack_interval_seconds | 1.0 |
| respawn_seconds | 2.0 |

`World.Populate` 會把 stats 套進每個 Monster instance；AI 移速、追擊/攻擊範圍、攻擊間隔、傷害、respawn、MaxHp 都由 definition data 決定。Stress mode 循環 definition 時 stats 一起循環；死亡復活恢復該 definition 的 MaxHp。

數值有 fail-closed bounds；例如 `attack_range > aggro_range`、HP<=0、非有限浮點數都拒絕。

既有 Map13 已由原始 Dat 重建成 population v2：**9 definitions / 78 spawns**，所有 stats 與舊硬編碼值完全相同，因此 schema 升版不應改變既有手感。

## Full rebuild 不會丟 custom monster

`tools/rebuild_assets.ps1` 流程：

1. 從 private original client/server material 解出 source、support assets 與 map。
2. `asset_pipeline.py` 直接以 normalized v2 建立 map/session lifecycle bundles 與 base population。
3. 依檔名排序重套 `content/monster_recipes/*.monster.json`；每次修改 definition/spawn 後重建該 map 的共享 bundle。
4. export Server collision/population content。

所以正式 custom monster 必須提交 recipe，而不是提交 private PNG/SPR/ACT。

## Replace / 重複 ID

- 新 recipe 若 definition id 已存在，預設 FAIL。
- 只有 recipe `"replace": true` 或 CLI `--replace` 才會刪除該 definition 原 spawns，改用 recipe 新 definition/spawns。
- 同 sprite 或不同 sprite 的 definitions，只要屬於同一 map lifetime，都共用該 map bundle。RuntimeAssetStore 以 bundle path 建一個 texture owner；definition id 只決定 stats/spawn/prefix。

## 實測

### Offline / E2E

- Python `test_population + test_monster_workflow`: 5 tests PASS（之後完整回歸另列）。
- 真 EN010A validate-only：16 sources / 5 behaviors / 40 views / collision PASS / preview 768×495。
- temp assets 正常模式：v1 base → v2 10 definitions / 79 spawns，definition 81 stats 完整，Server population copy byte-equivalent。
- 無既有 atlas 案例：工具自己建 runtime v9 / schema v2 atlas，EN010A 1 page `252×684`、689,472 RGBA bytes、40 logical views。

### 真多人 temporary live add

為驗證新增流程真的能上線，曾在本機 generated content 暫時加入：

- definition 81
- sprite EN010A（與 definition 1 共用 visual）
- HP120 / Damage12 / MoveSpeed72 / Aggro520 / Attack75 / interval0.9s / respawn3s
- 1 spawn

Server log：`population_loaded monsters=79 definitions=10`。

20 clients / 原 population 79 monsters / 0 drops / 40s：

- mean FPS **59.998**
- frame P95/P99 **17.71 / 17.907ms**，max 28.901ms
- draw-call P95 149
- 20/20 `snapshot_received`，20/20 `prediction_ready`
- 0 ERROR / SCRIPT ERROR
- `source_files_unchanged_during_run=true`
- Client `monster_palette`: **10 definitions / 9 shared atlases**，證明同 sprite definition 共用 texture。
- Server tick P99：tick300 3.329ms；600/900/1200 約 0.285/0.261/0.259ms。

測完已從原始 Server Dat 重建恢復正式內容：population v2 **9 definitions / 78 spawns**，Client/Server population 一致。

## 尚未包含

- Recipe 目前針對既有 `sgtest.animation/v2` monster 五動作契約；特殊 Boss/多段技能/不同 action schema 需另立版本，不會猜。
- 掉落表、技能表、任務/AI behavior tree 尚未放進 monster recipe；ASSET205 只把目前 prototype combat stats 與 spawn/visual 資料化。
- AS005 原版逐格人工視覺與 AS008 發布權仍未完成。
- 30/45/60/75/90FPS、5/10/15 low-power、unfocus/minimize、1/2/4/8 開完整 trace 是 RUNTIME206，不因本次 60FPS live Gate 而完成。

## 驗證命令

```powershell
cd C:\SGO\SRC\SGTest
cd tools
py -m unittest test_population test_monster_workflow -q
cd ..
.\.cache\dotnet\dotnet.exe run --project server\tests\SGTest.Server.Tests.csproj -c Release
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_monster_visuals_async.gd
```
