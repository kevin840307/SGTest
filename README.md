# SGTest

目前直接在 `C:\SGO\SRC\SGTest` 的 **main** 開發。已合併 master 的可執行原型，並保留 main 原有素材研究工具及文件。

- **client/**：獨立 Godot 4 客戶端，入口 `client/project.godot`。
- **server/**：獨立 **C#／.NET 8** 伺服器，入口 `server/SGTest.Server.csproj`；無 Godot 或圖像依賴。
- **tools/**：原版提取、轉換、研究及驗收工具。
- **docs/**、**SPEC.md**、**todo.txt**：規格、研究證據與進度；舊 M0／.NET 10 敘述是歷史，最新決策見 ADR-024。

## 架構入口

目前正式目標架構採 **功能導向的模組化單體**，避免過度分層與模糊的 Manager / Session / Core 命名。後續 Client／Server 開發先讀：

- [總架構](docs/ARCHITECTURE.md)
- [Client Architecture](docs/CLIENT_ARCHITECTURE.md)
- [Server Architecture](docs/SERVER_ARCHITECTURE.md)

其中 Client 的 Network / World State / Rendering / UI / Assets 明確分離；Server 維持單一 .NET 8 project，按 Networking / Accounts / World / Combat / Items 等實際功能分類。文件是**目標架構**，目前程式碼仍有部分舊目錄，搬移必須與行為修改分開。

## 最新資源架構重評

資源 Runtime 維持**單一 AssetStore + logical catalog**，詳 [Client Architecture](docs/CLIENT_ARCHITECTURE.md) 與 [Runtime Asset Bundle Architecture](docs/ASSET_BUNDLE_ARCHITECTURE.md)。資料夾描述「內容是什麼」：`characters / items / effects / maps / ui / audio / data`；載入生命週期則用 metadata 表達為 **app / character / map / on_demand**，不再把 `session` 當成目標架構名稱。現有 `bundles/session/` 與相關欄位屬舊實作名稱，後續會以獨立搬移 commit 調整，不在文件階段假裝已完成。怪物種類、方向、動作、人物部位、Atlas 尺寸都不是 Runtime Loader 類型。RUNTIME206 已驗證 Render/Logic/Network 隔離、多 FPS、低功耗、背景與多開；AFK 可以停止世界渲染和釋放 GPU world assets，但 Network / Reducer / authoritative state 仍維持正常節奏。

## Adaptive Atomic Atlas 一鍵資產轉換

正式名稱為 **Adaptive Atomic Atlas（自適應原子圖集）**。資產轉換程式集中在 `tools/asset_converter/`，使用原始遊戲路徑即可轉換並驗證。詳細說明見 [ASSET_CONVERTER](docs/ASSET_CONVERTER.md)。

```bat
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
```

完整原始 SPR Atlas、ACT、SIF 與 DrawOrder 索引保留在 **本機私有的** `local-assets/asset_converter/complete/`，避免所有素材被 Godot 常駐；目前有消費端的 Map13／Player Base 直接輸出到 `client/assets/bundles/` 與 `client/assets/maps/`。不需要額外的 `generated/` 目錄。原版與衍生圖片均不推 Git。

正式 Client 發行時，`client/assets/**` 不再由自製 Godot addon 塞進主 PCK；`client/addons/raw_assets_export` 已移除。改由 `client/tools/pack_assets.gd` 產生平台共用的外部 `assets/assets.pck`，Windows 與 macOS 使用同一份 pack / catalog / `res://assets/**` 路徑。Client 啟動時由 `scripts/asset_pack.gd` 掛載，之後仍由單一 AssetStore 按需載入；AFK 釋放 GPU world resources，不需要卸載 PCK。Windows Release 已實測通過，macOS 實際 .app export 尚未在本機驗證。

## 啟動

從儲存庫根目錄執行，需 .NET 8 SDK、Godot 4（本機 4.7.2）及 Python 3.10+。

```powershell
# 首次建立本機素材（原版只讀；生成圖像不進 Git）
python -m pip install -r requirements.txt
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"

# 終端一：C# 伺服器
 dotnet run --project server/SGTest.Server.csproj -c Release -- --map=13

# 終端二：Godot 客戶端
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path client
```

也可直接開 Godot 的 `client/project.godot`。客戶端以 `-- --host=127.0.0.1 --port=27888` 指定伺服器；伺服器預設只綁本機，`--bind=0.0.0.0` 可開放區網。`--map=0` 為不載入原版地圖的模擬場地；`--combat-arena` 為戰鬥驗收場地。

本機未安裝 .NET 8 時，可用此工作目錄內已有的 `.cache/dotnet/dotnet.exe` 代替 `dotnet`；該 SDK 快取不進 Git。`global.json` 選擇 .NET 8 SDK。

## 素材

原版來源是 `C:\SGO\Stargate Online IE HK2`；`Recovered` 及其 `Old` 僅作唯讀參考。Data1.dat 解包後套用 loose override。目前原版 archive＋loose 集合為 75,588 SPR／282,968 影格；完整離線索引、PNG幾何／來源指紋與sidecar重解析驗收見 `reports/converter_archive_verification.json`，一鍵建置結果見 `reports/converter_full_build.json`；這不等於原版遊戲畫面逐格／授權 Gate 已通過。

正式 sprite pipeline 不再要求開發者選 entity/direction/readable/hot-warm-cold。從原始檔重建只需：

```powershell
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
# 已存在專用抽取快取時，只重建 Runtime：
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" --extracted-source .cache/asset_converter/source --runtime-only
```

Builder 內部仍保留原 SPR clip atomic、8 logical facing、alias/mirror、pivot、ACT timeline、paper-doll z-order；每個 normalized entity 另有 `inspect[action][facing]`，所以怪物的動作／方向可以獨立查閱，與最後 physical packing 無關。Atlas side 由相同素材 A/B 決定。舊 `build_assets.py`、`build_atlas_groups.py`、`build_normalized_atlas_candidates.py` 等保留為研究／回歸工具，不是 production API。Client Runtime 素材直接放 `client/assets/`，不再多一層 `generated/`；正式 rebuild 只輸出目前真的被 Runtime 使用的 `bundles/` 與 `maps/`，原始／未使用素材留 `.cache/client`。Map13 static objects 與 monsters 共用 map lifecycle bundle，viewport 只做 draw cull；drops 目前是單一 MultiMesh，沒有另立 texture-atlas 分類。完整稽核見 `docs/ASSET_NECESSITY_AUDIT.md`。

## 已實作與驗證

C# Server 權威決定移動、碰撞、八方向、攻擊、扣血、死亡復活、掉落與原子拾取。Map13 預設按原版78個出生點配置；`--monsters=1000` 明確進入多種類壓測分布。30Hz 固定模擬、10Hz 快照；每隻怪 AI 分組 5Hz，位置持續 30Hz。Server 不依 Client FPS 推進。

Client 具有本地輸入預測／確認重播、各實體獨立位置歷史、單調 RTT 時鐘及分開的動畫相位。WebSocket I/O 使用獨立 worker 與有界佇列，圖像載入／繪圖不直接驅動 socket。狀態套用由 Client 固定步消費；30/45/60/75/90FPS、Visual5/10/15、no-world、真 Windows unfocus/minimize/restore 與 1/2/4/8 開的 state-hash / queue-age 矩陣已完成，見 `docs/RUNTIME_ISOLATION_VERIFICATION.md`。

基本男性紙娃娃、Map13 的9種怪物、78個原版出生點、地磚／物件／碰撞已接入。視窗預設 1280×720，最大寬 1440，可拖曳調整或選擇解析度；F2 切換金色／現代樣式。

目前為開發原型，已有帳號／角色與SQLite身分資料流程；完整裝備／物品外觀、其他地圖轉場、怪物繞路尋徑與其餘Recovered功能仍未完成。

NPC／傳點的離線來源轉換已有 `asset_converter/cli.py world-content`：24份NPC.dat／275筆、49個已審確認式傳點模板，226筆仍需腳本引擎。完整JSON只留本機，尚未接成可遊玩的跨圖功能；實作順序與證據見 [F002_WORLD_TRANSITIONS](docs/F002_WORLD_TRANSITIONS.md)。

## 驗證指令

```powershell
python -m unittest discover -s tools/tests -q
python -m unittest discover -s tools -p test_assets.py
python -m unittest discover -s tools -p test_maps.py
dotnet run --project server/tests/SGTest.Server.Tests.csproj -c Release
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script tests/test_wire.gd
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 8 --seconds 30
python tools/smoke_network.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --clients 2 --seconds 15 --combat-arena
python tools/visual_probe.py C:/SGO/Godot/Godot_v4.7.2-stable_win64_console.exe --stress
```

最新 C#／Godot 實測與限制見 [reports/README.md](reports/README.md)。先前 ENet／UDP impairment 是舊 Godot Server 的歷史數據，不能代表目前 WebSocket Server 的退化網路表現。原版及衍生媒體只保留本機，不上傳 Git。
