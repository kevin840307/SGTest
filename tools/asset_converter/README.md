# Asset Converter — SGTest Clean-slate

**只有這個目錄的 cli.py 為正式轉換器入口**。Client/Server 已從重建分支移除；素材可以產出到它們將來的資料目錄，但這不是可執行的遊戲。

## 建置

~~~powershell
python -m pip install -r requirements.txt
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
# 或
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
~~~

## 目前已提交的轉換器

- source/ — Data1＋loose override／SPR/ACT/SIF／方向、Origin、DrawOrder
- atlas/ — 一個原素材一個獨立 Atlas／像素驗證；本分支原始 HEAD 使用 Adaptive Atomic
- content/ — catalog、Map/Blocking、NPC／Population、Runtime 資產發布
- benchmarks/ — Packing/Recovered 原生 Renderer、壓縮與畫面對照測試，**不是 production runtime**
- operation_lock.py — build 互斥鎖，避免產物被多個 Session 同時覆蓋

**目前正式政策已合併：Per-Asset Height-Dense P0 + `runtime_pack.py` 外部壓縮 PCK。** 舊 Adaptive Atomic 只保留研究／比較用途；Client／Server 原型已刪除，正式建置不應依賴舊 `client/project.godot`。

## 轉換輸出

- local-assets/asset_converter/complete — 私有完整 archive，原始／衍生媒體不進 Git
- client/assets — Client 未來使用的獨立 Atlas／Manifest、Enemy/<id> 不與 Map 合圖
- server/content — Map/Population/Blocking.dat，無貼圖
- .cache/、reports/ — 本機驗證／快取，不推 Git

## 純 Converter 驗證

~~~powershell
python -m unittest discover -s tools/tests -p "test_*.py" -q
python -m unittest discover -s tools -p "test_*.py" -q
# 需要原始私有 HK2 來源才進行完整 Coverage
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1
# 要連同原始客戶端一起做少量原圖解碼試跑時（此步驟較慢）
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1 -Client "C:\SGO\Stargate Online IE HK2" -PreviewGroups 2
~~~

不使用已移除的 client/project.godot、server/*.csproj 做必需驗證。大量影像與 EXE 發行必須在新專案建置後另測。

技術規格：[docs/ASSET_CONVERTER.md](../../docs/ASSET_CONVERTER.md)；歷史研究：[docs/research/INDEX.md](../../docs/research/INDEX.md)。

## Production Height-Dense / external PCK

- `atlas/dense_atlas.py`：唯一正式 per-asset Height-Dense P0，按照 Frame 高／寬降序排序，單頁最大 4096、超量同素材分頁；逐幀檢查裁切/像素/Origin/hash。
- `runtime_pack.py` + `runtime_pack_packer.gd`：匯入 Godot `Texture2D`、世界 Atlas 採 VRAM 壓縮、UI/icons 保持 lossless，將各**獨立資產**輸出到外部 `assets.pck`。
- `smoke_runtime_pack.py <godot.exe>`：獨立建立測試 Atlas、壓縮 PCK，再在空白 Godot 專案掛載，檢查 `ResourceLoader`。此 Smoke 不要求原始遊戲 Client/Server。
- `benchmarks/packing/`：保留 Recovered Model1、Sprite、Map13、MaxRects/POT 對照程序與候選，不是第二套正式 Builder。
- 先跑 `verify_converter.ps1`；完整原版來源仍須重新驗證全量 SPR/ACT，發行前 Windows/macOS 要另測。
