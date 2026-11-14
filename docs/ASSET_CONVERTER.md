# Adaptive Atomic Atlas — SGTest 一鍵素材轉換

## 命名與決策

正式名稱為 **Adaptive Atomic Atlas（自適應原子圖集）**，不再以實驗排名「Mode 4」稱呼。採用已經驗證的來源 SPR 動畫區塊完整保留、2px邊緣延展、MaxRects、透明裁切、RGBA 成本/頁數平衡選擇和必要時多頁。維護上的邏輯 action/facing/frame 與 GPU 圖片排法獨立。沒有引入新的 Runtime Loader、Cache 或 Bundle 類別。

## 直接執行

專案根目錄：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

或使用獨立工具目錄的入口：

```powershell
python tools/asset_converter/cli.py build 'C:\SGO\Stargate Online IE HK2'
```

單一來源路徑需有原始 `Data1.dat`；會抽取至工具專用 `.cache/asset_converter/source`、套用原始遊戲 loose override，增量保存並建置來源 Atlas。若已存在解壓來源可加入 `--extracted-source .cache/asset_converter/source` 節省重複抽取檢查。需要 Python 3.10+ 和 requirements.txt 所列 Pillow。

## 研究/局部驗證

```powershell
python tools/asset_converter/cli.py build 'C:\SGO\Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --archive-only --category Enemy
python tools/asset_converter/cli.py build 'C:\SGO\Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --archive-only --prefix player/a/weapon/a-sword014
python tools/asset_converter/cli.py build 'C:\SGO\Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --runtime-only
python -m unittest discover -s tools/tests -q
```

`--preview-groups N` 僅做快速抽樣，不代表全量驗證；`--force` 可重建；`--archive-output` 可以指定純離線素材輸出路徑；`--runtime-output` 可以指定測試用 Runtime 根目錄。

## 產物位置與生命週期

| 目的 | 路徑 | Godot Runtime 載入？ |
|---|---|---|
| 完整原版 SPR 離線目錄 | `local-assets/asset_converter/complete/sprites/<group>/atlas.json`、`page_N.png` | **否**，避免把全部素材放入 VRAM |
| ACT/SIF/DrawOrder 素材資料 | `local-assets/asset_converter/complete/sidecars/` | 供後續語義檢視與生成器 |
| 當前可用地圖 Bundle | `client/assets/bundles/map/13/` | **是**，map owner |
| Player Base Bundle | `client/assets/bundles/session/` | **是**，session owner |
| Terrain、物件與 Spawn | `client/assets/maps/13/` | **是** |
| 完成/錯誤報告 | `local-assets/asset_converter/build_report.json`，離線 index.json | 測試稽核 |

不使用 `client/assets/generated` 或 `assets/generation` 多一層。權限未確認之前，原始與生成圖片不推 Git。

## 自動化保護

- 每群組逐一解碼，來源 SHA-256 做增量快取；素材與打包策略更動才重建。
- 驗證每格與原始 SPR 裁切後像素完全一致，保留原圖尺寸、原點、來源影格序號；輸出邊界與貼圖最大尺寸合法。
- 生成 Atlas 和 manifest 使用暫存檔取代；已生成的每頁 PNG 使用 SHA-256 比對，檔案壞掉會重新產生。
- Runtime 先放在隔離 staging 目錄，驗證過圖集 manifest 和影格範圍後才發布到 `client/assets`。不會先刪除整個 assets，也不碰其它 Session 的無關輸出。
- 自動選 512、1024、2048、4096；單幀來源跳過四次重複打包量測，但輸出仍通過像素與座標檢查。無法在 4096 限制容納者明確報錯，不能默默刪影格。

## 範圍與缺口

- 完整 SPR 類別涵蓋 Enemy、Player（含 weapon/cloth）、NPC、MS、Effect、Interface、Map 等來源；不因每個素材類型而新增一套 runtime loader。
- 「全部資產」分成 **離線素材索引** 與 **目前有消費端的 Runtime Bundle**。目前只有 Map13、Boy base、地形等正式串接，其他地圖/裝備/介面/特效的 Runtime consumer 仍依功能發展實作。
- 未取得完整原版視覺 Golden/授權證據（AS005/AS008），生成圖仍只能本機研究使用；專有音訊/資料格式不是單純 SPR Atlas 即可完整還原。
- 產物總量及通過率以 `index.json`、`build_report.json` 的成功項目為準，不把局部執行當完整完成。

## 已執行驗證（同機）

- Windows RTX3050 / Godot 4.7.2；Map13 9 visuals/78 spawns、573 atlas frames/2 pages；Player base 881 frames/1 page，terrain pixel-roundtrip PASS。
- Godot tests：directions、monster_visuals、map_objects_async、paper_doll_async、runtime_asset_store 全 PASS。
- Python tools/tests：103/103 PASS；新增 incremental cache、source change、page corruption rebuild、preview isolation fixture。
- Enemy 137/137 groups Adaptive Atlas PASS；武器 A-Sword014 單群組 PASS。
- 獨立來源快取驗出 7,890 個群組（75,588 SPR）；全量狀態必須檢查進度與 index，不能在完成前宣稱 ALL PASS。

## 相關

- [資產生命週期](ASSET_BUNDLE_ARCHITECTURE.md)
- [正規化動畫定義](NORMALIZED_ANIMATION_DEFINITION.md)
- [來源/研究報告](HK2_ATOMIC_BALANCED_ATLAS.md)
- 舊入口 `tools/rebuild_assets.ps1` 保留相容用途，但會清理正式 `client/assets`；新一鍵工具不會先刪除整個目錄。
## Adaptive Atlas 候選評估最佳化（2026-11-15）

為避免每次測量 512、1024、2048、4096 都實際建立大型 PIL 畫布，新增 `atlas/build_atlas_groups.py::measure_source_blocks`，純幾何模擬原版 SPR 的 2px padded shelf 與 Atomic MaxRects。每一尺寸的頁數、邊界與 RGBA 理論容量跟 `pack_source_blocks` 保持一致；**只有最終選中的候選會真正繪製 PNG**。這只影響 Builder 速度，不增加 Runtime 分組或 Loader。

以 Player/A/Standard/A-Hair00 原始 SPR（真素材）單次量測：四尺寸原本完整打包累積 3396.8 ms，改為幾何評估累積 25.0 ms；此為候選評估階段單次 A/B，實際完整轉換另含最終打包、像素驗證、PNG 壓縮與磁碟寫入，不能說整體快 136 倍。四尺寸各自的頁數與 RGBA8 理論容量完全相同。原始實測：`local-assets/asset_converter/candidate_geometry_benchmark.json`。

驗收：`tools/tests/test_adaptive_measure.py` 4 組合成案例（含透明影格、多頁拒絕、邊界尺寸），以及實際原版玩家髮型素材一組均 PASS；`tools/tests` 總計 103 PASS。最新全量 source archive 的結果以 `local-assets/asset_converter/complete/index.json` 為準；之前的 preview index 不可當作全量完成證據。
