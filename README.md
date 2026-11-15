# SGTest — 文件與素材轉換基線

> **2026-11-15 重新開發決策：現有 Godot Client 與 C# Server 原型已自本基線移除。**
> 此儲存庫目前**不是可執行的遊戲專案**。舊程式仍保存在 Git 歷史，請勿引用舊測試結果宣稱新版本已完成。

## 這個儲存庫目前保留什麼

- `tools/asset_converter/`：原始素材解包、SPR/ACT/SIF、方向／Origin／DrawOrder、Atlas、地圖、人口、NPC、Runtime 資產輸出；轉換器與 Runtime 實作互不依賴。
- `tools/`：轉換器需要的來源研究、驗收、相容性工具（**不是 Client/Server 程式**）。
- `docs/`、`SPEC.md`、`todo.txt`：正式設計與原版研究證據；歷史完成紀錄見 `docs/research/FEATURE_DECISION_HISTORY.md#source-todo-pre-rebuild-20261115-txt`。
- `client/assets/.gdignore`：**僅保留轉換輸出的定位標記**，不是 Godot Runtime。
- `client/README.md`、`server/README.md`：未來實作位置，**沒有可執行程式**。

## 從哪份文件開始重建

1. [重新開發架構 Review / 取捨](docs/REBUILD_ARCHITECTURE_REVIEW.md)
2. [正式架構總覽](docs/ARCHITECTURE.md)
3. [Client 新設計](docs/CLIENT_ARCHITECTURE.md) 與 [Server 新設計](docs/SERVER_ARCHITECTURE.md)
4. [Asset 結構](docs/ASSET_ARCHITECTURE.md)、[素材轉換](docs/ASSET_CONVERTER.md)、[效能對照](docs/research/ASSET_RENDERER_EVIDENCE.md#source-renderer-parity-v2-2026-11-15-md)
5. [重建任務](todo.txt) 與 [開發規則](AGENTS.md)

歷史文獻、原型效能研究和舊 SPEC 大部分內容保留供對照，但**不能以其中「已實作／已通過」的說法代替新版本驗證**。

## 素材轉換

環境：Python 3.10+，必要時 Godot 4（獨立實驗／匯入 Texture），未安裝或未重建 Client / Server 仍可執行純素材轉換。

```powershell
python -m pip install -r requirements.txt
run_build_assets.bat "C:\SGO\Stargate Online IE HK2"
# 或
python tools/asset_converter/cli.py build "C:\SGO\Stargate Online IE HK2"
# 轉換器合約測試（不需要 Godot Client / Server）
python -m unittest discover -s tools/tests -p "test_*.py" -q
# 不依賴舊 Godot Client／Server 的獨立驗證
powershell -ExecutionPolicy Bypass -File tools/asset_converter/verify_converter.ps1
```

輸出位置通常為 `local-assets/`（完整私有索引）、`client/assets/`（未來 Client 內容）、`server/content/`（未來 Server 內容）。後兩者**是資料輸出目錄，不代表可執行程式存在**。原版及衍生圖片不得推送至公開 GitHub。

目前已將經實測的 **Per-Asset Height-Dense P0 Builder、VRAM imported Texture、外部 assets.pck Converter 與 Recovered/Map13 benchmark 腳本**整合回正式重建基線，沒有保留另一個可執行 Client/Server 原型。所有功能後續只在 `main` 和原本 `C:\SGO\SRC\SGTest` 開發。

## 新版核心約束

**低資源、高效能、好維護，玩家端同步與畫面正確性優先。**

- 每個 Enemy / NPC / scenery / player part / Equipment 分別保留自己的 Asset ID 和物理 Atlas，僅同一素材的 Frame／方向／動作允許合圖；不跨素材合併，也不增加 `assets/generation/`。
- AssetPack 是分發容器，AssetStore 依需求載入、共用、釋放。不要為每個類型重複建立 Loader／Cache。
- Client 收包、World State、輸入與 Server 權威更新和 Render/UI FPS **完全解耦**。
- 怪物 Animator 可採共享 `SpriteFrames + AnimatedSprite2D` 作第一候選；**跨 Player/Enemy/Scenery World Y-sort 必須正確**，不能只依隔離 Benchmark 宣稱全面勝出。
- Server 對位置、碰撞、戰鬥、經濟、交易保持權威；DB 不阻塞 World Tick。

**目前不提供任何 `dotnet run` 或 Godot Client 啟動命令，直到重新開發實際完成並通過測試。**

## 重建狀態（2026-11-15）

- **唯一正式維護目錄：`C:\SGO\SRC\SGTest`，分支：`main`。** 不需管理第二套專案路徑。
- 舊版 Godot Client 與 C# Server 皆已從正式重建版移除；未來重建 **Godot 4 Client + .NET 8 C# Server**。
- `tools/asset_converter` 已整合 Height-Dense P0、外部 VRAM-compressed `assets.pck` 與對照 benchmark；請用該唯一 Converter CLI。
- 只靠現有原版素材轉換工具與文件即可開始重建；新 Client/Server 功能標示為未完成，請依 `SPEC.md` 及 `todo.txt`。
