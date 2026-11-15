# SGTest 素材轉換器（重建版本）

**目前狀態：正式唯一 Converter 包含 Height-Dense Builder 及 Godot external assets.pck packer，已完成純 Python unit tests 與空白 Godot 專案掛載 PCK 的 smoke；完整原版素材重新轉換、Windows Release 與 macOS 實機尚未完成。**

## 正式入口

~~~powershell
python -m pip install -r requirements.txt
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
# 等效入口
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
python -m unittest discover -s tools/tests -p "test_*.py" -q
~~~

Converter **不應依賴**可執行的 client/project.godot 或 server/*.csproj。保留的 client/assets/ 和 server/content/ 僅供輸出。

## 主要責任

| 區域 | 職責 |
|---|---|
| source/ | Data1.dat / 原版 client extraction、SPR、ACT、SIF、方向／Item/NPC 原始格式 |
| atlas/ | 依 asset ID 抽幀、裁切、排列、像素驗證／候選演算法 |
| content/ | AssetCatalog、Map terrain/blocking/scenery、Server Population、NPC、DrawOrder、可索引資產發布 |
| benchmarks/ | 回歸／素材編排／Model1 比對；**不是正式 Runtime** |
| `runtime_pack.py` + `runtime_pack_packer.gd` | **已整合**：Godot imported textures，BPTC/ASTC remap、UI lossless、external assets.pck；不依賴可執行 Client，macOS 實機仍待驗證 |

不新增第二套「依怪物／裝備／地圖類別」的 Converter，也不回復舊 map/session bundles。

## 輸出

- local-assets/asset_converter/complete：完整私有來源／sidecars／index。
- client/assets：runtime 資產，含 Enemy/<id>/atlas.json 等獨立頁／Manifest。
- server/content：只輸出權威 Server 要用的地圖、Blocking.dat、Population，不包含圖片。
- .cache 與 reports：本機測試／分析產物；Git 忽略。

有清除／重新產生需要時**只處理有明確 ownership 的輸出目錄**，不得直接清空 source game、Recovered、其他 Session 的 .cache。

## 重要驗收

- 來源覆蓋／hash：先前原型曾完成 75,588 SPR、282,968 frame／7,890 asset groups；此為**歷史基準，clean-slate 重新跑才算當次通過**。
- 每個 Asset 必須保持獨立 logical/physical identity，Map13 Monster 外觀以 Enemy logical ID 查找。
- 來源 SPR/ACT、trim、Origin、8 方向、事件時間與紙娃娃 DrawOrder 可完整索引和驗證。
- 同一素材 P0/P1 与 VRAM compression 的畫質與 Texture/Load/Frame 指標需做實測。
- 外部 PCK 的 Windows Release 與 macOS import/remap/載入有獨立 Gate。

歷史文件全文見 [research/INDEX](research/INDEX.md)，最新重建任務見根目錄 todo.txt。
## 保持轉換器獨立與可驗證

```powershell
# 純 Python 契約：不會啟動或依賴任何 Client/Server
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1

# 額外從原始 Data1.dat 抽樣重建（需重新解包，清掉快取後會較慢）
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1 -Client "C:\SGO\Stargate Online IE HK2" -PreviewGroups 2

# 真正全量轉換，Server population 的原版 Dat 可用 --server-dat 提供
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2" --server-dat "C:\SGO\original-server-Dat-path"
```

`--server-dat` 所示為**示例佔位路徑**，必須換成實際原始 Server Dat；無該來源時 Converter 可做 SPR/Atlas，但不能宣稱 Server population 已重建。目錄 `.cache` 和 `local-assets` 會按命令重新建立，舊圖片不在 Git。避免讓正式轉換器依賴已刪除的 `client/project.godot` 或舊 `server/*.csproj`。

## 已合併的 Height-Dense 與 external PCK 驗證

```powershell
# 不依賴舊 Client/Server 的 Converter 合約測試
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1

# 真正呼叫 Godot 4 Editor Import／PCKPacker，
# 建立 1 份壓縮 Enemy 圖與 1 份無損 UI 圖，
# 再用完全空白 Godot 專案載入外部 assets.pck；所有暫存自動清除
python tools/asset_converter/smoke_runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe"

# 有完整已轉換素材時再輸出正式外部 PCK
python tools/asset_converter/runtime_pack.py "C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe" --assets client/assets --output .cache/client-release/assets/assets.pck
```

以上 Smoke 已在 Windows/Godot 4.7.2 通過，驗證 PCK 實際可由**另一個空白 Godot 專案**載入。這**不是**正式遊戲的 Windows Release、多機多開或 macOS GPU 格式相容性驗證。壓縮 UI icon 及字體必須繼續使用 lossless 模式。
