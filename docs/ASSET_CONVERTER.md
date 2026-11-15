# SGTest 素材轉換器（重建版本）

**狀態：tools/asset_converter 是唯一維護的正式 Converter。** clean-slate 分支保留已提交版本；其他 Session 尚未提交的 Height-Dense / runtime_pack 不能當作本分支已具備的功能。

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
| runtime_pack.py（待合併） | Godot imported textures、壓縮 remap、產生外部 assets.pck；不得依賴舊可執行 Client |

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