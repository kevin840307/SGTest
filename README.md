# SGTest

目前直接在 `C:\SGO\SRC\SGTest` 的 **main** 開發。已合併 master 的可執行原型，並保留 main 原有素材研究工具及文件。

- **client/**：獨立 Godot 4 客戶端，入口 `client/project.godot`。
- **server/**：獨立 **C#／.NET 8** 伺服器，入口 `server/SGTest.Server.csproj`；無 Godot 或圖像依賴。
- **tools/**：原版提取、轉換、研究及驗收工具。
- **docs/**、**SPEC.md**、**todo.txt**：規格、研究證據與進度；舊 M0／.NET 10 敘述是歷史，最新決策見 ADR-024。

## 最新資源架構重評

先依 [SPEC 第0節](SPEC.md) 交付可重跑基準，再決定標準動畫定義、512／1024／2048／4096混合Atlas及統一Runtime store。報告與執行命令見 [RESOURCE_BASELINE.md](docs/RESOURCE_BASELINE.md)。目前實驗排法／loader均未選定，不能將原型可啟動或單次解碼速度當作全部資源Gate通過。

## 啟動

從儲存庫根目錄執行，需 .NET 8 SDK、Godot 4（本機 4.7.2）及 Python 3.10+。

```powershell
# 首次建立本機素材（原版只讀；生成圖像不進 Git）
python -m pip install -r requirements.txt
.\tools\rebuild_assets.ps1 -Client 'C:\SGO\Stargate Online IE HK2'

# 終端一：C# 伺服器
 dotnet run --project server/SGTest.Server.csproj -c Release -- --map=13

# 終端二：Godot 客戶端
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --path client
```

也可直接開 Godot 的 `client/project.godot`。客戶端以 `-- --host=127.0.0.1 --port=27888` 指定伺服器；伺服器預設只綁本機，`--bind=0.0.0.0` 可開放區網。`--map=0` 為不載入原版地圖的模擬場地；`--combat-arena` 為戰鬥驗收場地。

本機未安裝 .NET 8 時，可用此工作目錄內已有的 `.cache/dotnet/dotnet.exe` 代替 `dotnet`；該 SDK 快取不進 Git。`global.json` 選擇 .NET 8 SDK。

## 素材

原版來源是 `C:\SGO\Stargate Online IE HK2`；`Recovered` 及其 `Old` 僅作唯讀參考。Data1.dat 解包後套用 loose override。已生成 75,591 SPR／282,990 影格，像素重建驗證見 `reports/asset_validation.json`；這不等於原版遊戲畫面逐格／授權 Gate 已通過。

同一方向的所有動作集中管理，保留 alias、鏡射、原點與偏移。小素材合成單張，大素材依方向分張；可用 `build_assets.py --layout entity|direction|auto` 覆寫。策略與載入 A/B 見 `reports/README.md`。

```powershell
python tools/extract_client.py 'C:/SGO/Stargate Online IE HK2' .cache/client
python tools/build_assets.py .cache/client client/assets/generated --layout auto
python tools/copy_support_assets.py .cache/client client/assets/generated
python tools/build_maps.py .cache/client client/assets/generated --map 13
python tools/build_population.py 'C:/SGO/Recovered/Old/服務端/Dat' --map 13
python tools/export_server_content.py
python tools/validate_assets.py .cache/client client/assets/generated --all-pixels
```

`tools/convert_assets.py` 保留 main 的研究版與新 ACT 結果；runtime 素材管線使用獨立 `tools/runtime_spr.py`，不覆蓋研究版本。Client 圖片只放 `client/assets/generated/`；Server 只接收 `server/content/` 的碰撞／出生／種類ID與內容指紋。怪物圖集在 Client 按當前地圖palette載入，同種共用、切圖清空。原版出生／visual 的對照來自使用者指定的 Recovered/Old 原始服務端 Dat；戰鬥數值仍是明確的原型預設，未宣稱還原全部怪物AI／數值。

## 已實作與驗證

C# Server 權威決定移動、碰撞、八方向、攻擊、扣血、死亡復活、掉落與原子拾取。Map13 預設按原版78個出生點配置；`--monsters=1000` 明確進入多種類壓測分布。30Hz 固定模擬、10Hz 快照；每隻怪 AI 分組 5Hz，位置持續 30Hz。Server 不依 Client FPS 推進。

Client 具有本地輸入預測／確認重播、各實體獨立位置歷史、單調 RTT 時鐘及分開的動畫相位。WebSocket I/O 使用獨立 worker 與有界佇列，圖像載入／繪圖不直接驅動 socket。狀態套用仍由 Client 固定步消費；完整失焦／最小化／各 FPS 的 trace 一致性尚待驗證，不能只因有 worker 就宣稱全數通過。

基本男性紙娃娃、Map13 的9種怪物、78個原版出生點、地磚／物件／碰撞已接入。視窗預設 1280×720，最大寬 1440，可拖曳調整或選擇解析度；F2 切換金色／現代樣式。

目前為開發原型，尚無正式登入、持久化、完整裝備／物品外觀、其他地圖轉場及怪物繞路尋徑。

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
