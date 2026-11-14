# Adaptive Atomic Atlas — 可獨立維護的素材轉換工具

**正式入口只有一個**，所有轉換程式與專案內部依賴都收在本資料夾。
不因 Enemy、Player、Weapon、NPC、Map、Effect 等素材類型再建立獨立 Loader／Cache。

## 一鍵執行

從 SGTest 根目錄：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

或在本工具目錄：

```bat
build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

需 Python 3.10+，依賴請安裝：

```powershell
python -m pip install -r tools/asset_converter/requirements.txt
```

## 架構

```text
tools/asset_converter/
├── cli.py                      # 唯一 CLI：建置、驗證、報告、發布
├── build_assets.bat            # Windows 一鍵啟動
├── requirements.txt
├── README.md
├── source/                     # 原始格式，不含 Godot Runtime
│   ├── extract_client.py       # Data1.dat + loose overlay
│   ├── runtime_spr.py          # 原始 SPR 解碼與 CDE
│   ├── convert_assets.py       # SPR/ACT/SIF/DrawOrder 轉換
│   ├── inspect_act.py
│   ├── legacy_direction.py
│   └── build_assets.py         # 地圖原始格式支援
├── atlas/                      # 唯一共用 Atlas 打包核心
│   ├── adaptive_atlas.py       # 自適應選尺寸/快取/逐幀驗證
│   ├── build_atlas_groups.py   # 來源 SPR Atomic MaxRects
│   └── asset_bundle_builder.py # 正式 Bundle 素材打包
└── content/                    # 產生遊戲能直接使用的內容
    ├── animation_definition.py
    ├── runtime_animation_semantics.py
    ├── audit_character_layers.py
    ├── build_maps.py
    ├── build_enemy_map_palette.py
    ├── build_population.py
    └── asset_pipeline.py       # Map/session Runtime Bundle
```

`source` 管格式及還原、`atlas` 管影格物理位置、`content` 管動畫語義和現有消費端；不同素材種類不是額外的程式架構層。原始包裝器和舊工具仍在 `tools/` 作相容/回歸，不是新 CLI 的依賴。**正式變更一律修改本工具包內部實作。**

## 輸出與驗證

- **完整離線來源圖集**：`local-assets/asset_converter/complete/sprites/`、`sidecars/`。避免未使用的所有圖直接導入 Godot。
- **實際 Runtime 使用資產**：`client/assets/bundles/map/13/`、`bundles/session/`、`maps/13/`，不另建立 `generated/` 目錄。
- **可觀測報告**：`local-assets/asset_converter/complete/index.json`、`local-assets/asset_converter/build_report.json`。
- **安全**：原版唯讀、逐幀像素驗證、來源＋圖片 SHA256、增量建置、staging 後才發布、保留未修改的 live 資產。
- **測試**：`python -m unittest discover -s tools/tests -q`；也可執行 `--archive-only --prefix player/a/weapon/a-sword014` 只驗證一種原版武器。

## 完整度邊界

轉換「全部來源素材」代表所有可解碼 SPR 的私有圖集與支援的 ACT/SIF/DrawOrder 中介資料；**不是**完整遊戲所有地圖、武器/裝備紙娃娃、UI、音效都已串接到 Godot Runtime。目前正式 Runtime consumer 仍以 Map13 和基礎玩家為主。若遇原版檔格式不合法會在索引中標示錯誤，不會用空白圖冒充成功。未確認素材授權前不公開分發原圖或生成圖片。

詳細範圍見 `docs/ASSET_CONVERTER.md`。

## 2026-11-15 解碼與完整度修正

離線Atlas與Runtime bundle統一使用 `source/runtime_spr.py`，同時支援RGB565與`Ver\x08` indexed SPR；不要另加只認第一影格offset=0的簡化解碼器。Indexed格式的像素可位於header之前，不能據此判為少圖或壞檔。

原版Data1路徑搭配完整建置時會先核對SPR來源檔案集合；指定不完整的 `--extracted-source` 會報錯，避免把殘缺cache輸出當完整目錄。filtered/preview只代表指定範圍，並不宣稱來源完整。此核對是檔案存在性，逐格解碼／像素驗證另外執行。

增量manifest保留decoder SHA與metadata SHA；舊cache先逐像素／size／crop／pivot驗證才升級stamp，已正確的PNG不重新packing。metadata或PNG損毀會重建。每50組寫 `checkpoint.json`，保留成功與失敗來源，最終看 `index.json.status`／errors，進度數字不代表完成。

完整SPR來源數以當次原版 archive+loose union為準，不把舊7893組/75591 SPR硬寫成門檻。2026-11-15隔離抽取的實際集合為7890組/75588 SPR，presence audit無缺檔／額外檔；完整Atlas與sidecar結果仍須驗收，不等同全部遊戲功能已接線。

## 候選幾何評估回歸與A/B

從專案根目錄重跑離線評估：`python tools/benchmark_converter_planning.py --source .cache/asset_converter/source`。三組真素材各跑3次；快速幾何路徑每次batch 100次取單次CPU時間，避免Windows process CPU timer把短測量量成0。報告含來源SHA與打包模組SHA，只比較候選評估，沒有包含最終PNG產出／磁碟／遊戲FPS。

2026-11-15三次中位數：EN010A 765.625→1.719ms；Boy body 3093.750→30.625ms；Boy hair00 3062.500→36.250ms。四候選尺寸的頁數／RGBA面積全相等；160組隨機候選以及透明／atomic失敗／無畫布配置測試PASS。最終真打包的頁數與RGBA成本另須吻合選定候選，否則拒絕發布。`reports/converter_planning_geometry.json`保留原始樣本與指紋，不把此結果推論成遊戲FPS或整體建置相同比例加速。
