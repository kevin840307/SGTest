# SGTest — 文件與素材轉換基線

> **2026-11-15 重新開發決策：現有 Godot Client 與 C# Server 原型已自本基線移除。**
> 此儲存庫目前**不是可執行的遊戲專案**。舊程式仍保存在 Git 歷史，請勿引用舊測試結果宣稱新版本已完成。

## 這個儲存庫目前保留什麼

- `tools/asset_converter/`：原始素材解包、SPR/ACT/SIF、方向／Origin／DrawOrder、Height-Dense Atlas、地圖、人口、NPC、外部 PCK；轉換器與 Runtime 實作互不依賴。
- `tools/tests/`：正式 Converter、原始格式及佈署包契約測試（**不是 Client/Server 程式**）；舊獨立轉換器與 Viewer 已從工作樹移除，可從 Git 歷史查閱。
- `docs/`、`SPEC.md`、`todo.txt`：正式設計與原版研究證據；歷史完成紀錄見 `docs/research/FEATURE_DECISION_HISTORY.md#source-todo-pre-rebuild-20261115-txt`。
- `client/assets/.gdignore`：**僅保留轉換輸出的定位標記**，不是 Godot Runtime。
- `client/README.md`、`server/README.md`：未來實作位置，**沒有可執行程式**。

## 現階段 MVP：Map15 主城 + Map13 尼西亞台地

**兩張原版真地圖 + 大部分完整玩法**，不是單圖 Demo：先確認兩圖資產／Blocking／NPC／傳點；Map15 用於登入、角色、NPC 商店／銀行／倉庫、裝備、交易／玩家商店，Map13 用於 A*／長按、怪物 AI、技能戰鬥、大量掉落和多人 AOI；中間要完成**Map15 ↔ Map13 真實雙向傳點**、MapEpoch/WorldReady、資源釋放及多人重連。

目前 Map13／Map15 原始地形、Blocking、NPC 與兩圖共 82 筆 Enemy Spawn／10 種 SPR-ACT 已完成來源轉換驗證（詳 `todo.txt` 的 MVP001 子項）；完整 7,890 素材 Archive audit 與兩圖 242 個獨立 Atlas／Catalog 定向發布已 PASS；雙圖 Runtime content 與外部 PCK 的空白 Godot ResourceLoader 已 PASS（244 Texture）；可執行 Client/Server 仍未完成。先小型切片逐步做到兩真 Godot Client、Map13 同圖 20 連線／300 Enemy／500 Drop、Map15 多人城市互動及兩圖同時活躍、AFK／多開 Gate。按 todo.txt 的 MVP001～MVP010 進行，**不是只做每種功能一條 happy path**；所有適用原版規則、異常／多人競態及 Server authority 要驗證，好友暫緩。選型多方案先實測 ALT001；MVP 通過後才擴所有地圖／品項及更大負載。

## 從哪份文件開始重建

1. [正式架構總覽與重建決策](docs/ARCHITECTURE.md)
2. [Client 新設計](docs/CLIENT_ARCHITECTURE.md) 與 [Server 新設計](docs/SERVER_ARCHITECTURE.md)
3. [Asset 結構](docs/ASSET_ARCHITECTURE.md)、[素材轉換](docs/ASSET_CONVERTER.md)、[歷史效能證據](docs/research/ASSET_RENDERER_EVIDENCE.md)
4. [測試與效能 Gate](docs/TEST_PLAN.md)、[重建任務](todo.txt)、[開發規則](AGENTS.md)

歷史文獻、原型效能研究和舊 SPEC 大部分內容保留供對照，但**不能以其中「已實作／已通過」的說法代替新版本驗證**。

**開發流程：有多種合理方案時先對等測試，選整體最佳方案再繼續。** 詳見 AGENTS.md 的「多方案先比較」及 docs/TEST_PLAN.md 的 ALT001；判斷同時考慮即時正確性、低資源、P95/P99、網路 AOI 與可維護性，不新增長期平行實作。

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

目前已將經實測的 **Per-Asset Height-Dense P0 Builder、VRAM imported Texture、外部 assets.pck Converter**整合回正式重建基線；過期的 Recovered/Map13 Benchmark 腳本保留於 Git 歷史，沒有保留另一個可執行 Client/Server 原型。所有功能後續只在 `main` 和原本 `C:\SGO\SRC\SGTest` 開發。

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
- `tools/asset_converter` 已整合 Height-Dense P0、外部 VRAM-compressed `assets.pck`；已淘汰的 benchmark 腳本只保留於 Git 歷史，未來效能測試以新 Client/Server 實際負載為準。
- 只靠現有原版素材轉換工具與文件即可開始重建；新 Client/Server 功能標示為未完成，請依 `SPEC.md` 及 `todo.txt`。
