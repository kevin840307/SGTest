# HK2 原始 SPR 局部性 A/B 實驗（2026-11-14）

本文件只記錄可重現的本機研究；原始圖及轉出 PNG 存放在 gitignore 的 `local-assets/`，不可提交 GitHub。正式 Client、Server、多人同步、授權及原版視覺比對均 **NOT VERIFIED**。

## 目的與修改前行為

使用者要求同一怪物／物品可有一張或多張圖集，但同一動作、同一方向應盡可能在同一頁，且必須保留原版 SPR 與 ACT 的方向重用、水平鏡像、來源影格和 origin。

舊 `build_atlas_groups.py` 使用跨 SPR 的 dense frame packing，已轉好的 `local-assets/enemy-adaptive/enemy_catalog.json` 共有 50 組怪、2,000 個動作×方向，其中 173 組跨多頁：1,827 單頁、170 雙頁、3 三頁。這不代表渲染有錯，但會放大特殊動作切頁風險。

## 新增工具

- `tools/audit_action_page_locality.py`：從 ACT catalog 驗證所有 page index，輸出動作×方向所跨頁數；可選 `--require-single-page` 作嚴格 Gate。
- `tools/build_atlas_groups.py --source-locality`：**實驗候選**，對同一怪物每個原版 SPR 個別打包，保留原頁、像素、origin、來源 SHA、重複方向與 ACT 播放順序；若單一 SPR 大於頁容量仍可能拆頁，**不能視為所有素材均保證單頁**。
- 預設 dense 模式維持不變，避免在缺乏 GPU A/B 前影響既有成果。

## 真素材試跑

本機來源：`C:\SGO\Stargate Online IE HK2`；怪物 `EN023A`，4096 上限，15 SPR、111 來源幀。新模式 15 頁、約 18,816,668 bytes 頁面 RGBA，PNG 約 4,141,944 bytes。參照先前的 dense 4096 候選約 20,710,080 RGBA bytes（不同 packer 結果，非同場 runtime 測試，僅離線面積比較）。

新 catalog：編譯 1/1、隔離 0；動作×方向 40/40 同頁；Godot 4.7.2 headless 解碼／區域檢查：1 怪、5 動作、40 方向、272 播放步 PASS。全部 Python unit tests：46 PASS。

```powershell
cd C:\SGO\SRC\SGTest
py -m unittest discover -s tools/tests -q
py tools/audit_action_page_locality.py local-assets/enemy-adaptive/enemy_catalog.json --output reports/enemy-action-locality.json
py tools/build_atlas_groups.py 'C:\SGO\Stargate Online IE HK2' local-assets/enemy-source-locality --prefix Enemy/EN023A --atlas-side 4096 --source-locality
py tools/compile_enemy_atlas_catalog.py local-assets/enemy-animation-audit.json local-assets/enemy-source-locality local-assets/enemy-source-locality/enemy_catalog.json
py tools/audit_action_page_locality.py local-assets/enemy-source-locality/enemy_catalog.json --require-single-page
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- local-assets/enemy-source-locality/enemy_catalog.json
```

## 下階段 Gate

1. 全 50 組怪對 source-locality / dense / 單 SPR 無圖集合併三方案做同場 A/B；衡量 unique textures、pages、RAM/VRAM、upload p95/p99、load p95/p99、frame p95/p99、draw calls。
2. 針對同 SPR 多頁者，研究 action-aware 避免跨頁策略，並檢查原版方向鏡像、透明/錨點/原始畫面逐格對照。
3. 研究地上物 `ground.spr`／`ground1.spr` 及人物雙槽位疊圖，不能把怪物策略無條件套用在物品或角色。
4. 素材 Gate AS005/AS008/AS009 未過前，不宣稱正式遊戲已完成。視窗目標上限寬 1440，與非 UI FPS 網路處理隔離的設計要求不變。
