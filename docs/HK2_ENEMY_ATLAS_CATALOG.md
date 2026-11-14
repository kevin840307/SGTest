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
