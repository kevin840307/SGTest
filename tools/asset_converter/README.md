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
