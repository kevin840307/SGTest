# Monster Recipes

把正式新增怪物的資料定義放在此目錄，檔名使用 `*.monster.json`。

- 原始 SPR/ACT 不放進 Git；recipe 只記錄 definition id、sprite identity、runtime stats 與 spawn。
- `tools/rebuild_assets.ps1` 先從原版資料重建，再依檔名排序自動套用所有 `*.monster.json`。
- 新增前可先跑 `python tools/monster_workflow.py <recipe> --validate-only`，只驗證並產 private preview，不修改 population。
- 同 definition id 預設拒絕覆蓋；recipe 可明確設 `"replace": true`，或 CLI 使用 `--replace`。
- blocked spawn 預設拒絕；只有明確使用 `--snap-spawns` 才會移到最近可走 collision cell，report 會保存 requested/actual 座標。

`example.monster.json.example` 是格式範例，不會被 rebuild 自動套用。
