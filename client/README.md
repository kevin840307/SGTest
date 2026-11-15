# Godot 4 Client

入口：`client/project.godot`。

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path client
```

正式 script 結構：

```text
scripts/
├── app/
├── networking/
├── world/
├── gameplay/
├── rendering/
├── assets/
├── ui/
└── diagnostics/
```

完整責任與依賴規則見 `../docs/CLIENT_ARCHITECTURE.md`。

素材以 per-asset 形式放在本機 `client/assets/`，不同 Monster/NPC/scenery/Player part 不跨 asset 合圖。建立素材：

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

Runtime asset 驗證：

```powershell
tools\asset_converter\verify_runtime.ps1 -Client "C:\SGO\Stargate Online IE HK2"
```

Client 的 Render/UI FPS 與 Network/World state 分離；AFK 可以停止 world rendering 與釋放 GPU world assets，但不能降低 reliable network/state correctness。
