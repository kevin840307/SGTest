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
