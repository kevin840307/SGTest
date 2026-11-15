# SGTest 工具目錄

目前只有兩種責任，不再提供第二套轉換流程：

```text
tools/
  asset_converter/      # 唯一正式素材轉換器
    cli.py              # 唯一 Conversion CLI
    source/             # 原版 Data1、SPR、ACT、SIF 解碼
    atlas/              # per-asset Height-Dense、layout fallback、完整性稽核
    content/            # Asset catalog、Map/Blocking/NPC/Enemy/Player 資料
    runtime_pack.py     # 外部 Godot compressed assets.pck
  tests/                # Converter、原版格式與 pack policy 測試
    wire_aoi_probe/      # 新版 Wire/AOI 選型的合成實測（不是正式 Client/Server）
```

`tools/` 根目錄沒有可執行的舊版 `build_*.py`、`convert_*.py`、
Viewer 或其他平行 Builder。因為已存在正式相同功能／不再被使用，
這些研究原型與過期 benchmark 已從 HEAD 移除，仍可從 Git 歷史取得。

## 使用

```powershell
python -m pip install -r requirements.txt
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1
```

轉換器不依賴可執行 Client/Server；`.cache` 是可重建產物而非來源。
Wire/AOI 第一輪實測：dotnet run -c Release --project tools/tests/wire_aoi_probe/WireAoiProbe.csproj；Godot Golden Fixture 指令與侷限見 [測試計畫](../docs/TEST_PLAN.md)。

完整實作與命令請看 [Converter README](asset_converter/README.md) 和
[素材轉換架構](../docs/ASSET_CONVERTER.md)。
