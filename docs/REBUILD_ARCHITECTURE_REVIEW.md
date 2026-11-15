# SGTest Client / Server 重新開發架構 Review

**決策日期：2026-11-15。狀態：已選定設計方向；新 Client／Server 尚未實作。**

這次是有意識地**捨棄可執行原型、保留轉換器與研究證據**。移除的程式在 Git 歷史可取回，**不要將它當成新專案程式或舊 PASS 可直接沿用**。

## 1. 清理與保留邊界

| 類別 | 處理 | 原因 |
|---|---|---|
| `client/project.godot`、main/Networking/World/UI/Render/AssetStore、場景、舊 Client tests | **移除** | 原型耦合、重建需契約先行 |
| `server/*.csproj`、Accounts/Networking/Persistence/World、舊 Server tests | **移除** | 權威世界與協定需重新定義 Gate |
| Runtime smoke／舊身分測試腳本、Windows Release 整合腳本與 Server CI | **移除** | 會對不存在的遊戲原型產生失敗／誤導 |
| `tools/asset_converter/`、各種原版解析／地圖／人口／NPC 轉換、驗證測試 | **保留** | 真正的可重用來源、資料契約 |
| `client/assets/.gdignore`、client/server README | **保留最小輸出位置** | Converter 仍可產出未來 Client/Server 消費的資料 |
| `docs/`、`SPEC.md`、歷史 TODO | **全部保留** | 功能需求／原版來源／實測證據 |
| `.cache/`、`local-assets/`、`reports/`、私有 media | **快取可重建，私有來源另行確認** | 根據各目錄 ownership 清除；Git worktree 與他人 WIP 不能直接 `Remove-Item` |
| 其他 Session 未提交的 Height-Dense／PCK Builder | **原工作樹不動** | 不能清理時破壞其他人的程式與驗證 |

> 注意：獨立清理分支從已提交的 `main` 建立，因此尚未提交的 Height-Dense/PCK 變更**不在本分支內**。先在原工作階段整理完成、測試並提交，再安全合併；不可聲稱已納入清理基線。

## 2. 舊架構實際問題（以刪除前程式盤點）

### Client

- `client/scripts/app/main.gd` 約 **900 行**：同一個 Node2D 負責 socket 事件、WorldState、預測、動作、畫面、UI、AFK、AssetStore 和生命週期。分類資料夾本身沒有解決依賴耦合。
- 同一 Canvas 交錯繪製 Player／Enemy／Scenery 的 Y-order；直接加入獨立怪物 AnimatedSprite2D 會失去跨類型排序。新版本應一開始就建立**一致的 World-space Y-sort**。
- 地圖、角色、怪物對 ResourceLoader 與 async owner 的生命週期關係較複雜；`RuntimeAssetStore` 約 450 行，須以**明確 acquire/release/cancel/epoch** 契約重寫，不能複製舊快取實作。
- UI 與 Network 事件在 app composition root 混用。新設計中 UI 只發意圖、只讀 ViewModel，不能直接改權威狀態。
- 渲染選項必須不影響 Net poll、Snapshot reducer、Player command 時鐘；舊固定 30Hz 與 10Hz snapshot 是歷史基準，不是新版本自動 PASS。

### Server

- `WorldHost.cs` 與 `World.cs` 負責排程、AOI、AI、戰鬥、發布等多項工作；現階段規模不是不能維護，但重建時應把**Single-writer World 更新**和**網路／持久化非同步 I/O**契約先寫清楚。
- Accounts、Persistence、Networking、World 的四大模組方向正確，**不用改成多個微服務／多個空 Domain/Infrastructure 層**；只在 Combat/Items/Social 確有複雜規則時拆出。
- `ConnectionEpoch`、`MapEpoch`、`EntityRevision`、`ActionSequence` 不可混用為單一 session/generation；AOI 建立／刪除與 Player action 必須處理亂序、重傳、換圖和新登入。

### Documents & testing

- 歷史文檔曾同時描述「尚未實作」與「原型已可玩」，不能當現況；新 `README`／`AGENTS`／`todo` 以**重建尚未開始**為唯一狀態。
- Recovered benchmark 有價值，但純 Renderer 測試缺少 Server jitter、人物紙娃娃、完整 map、AFK lifecycle，所以**不能把它當完整新 Client 的 FPS 成績**。

## 3. 重新開發的唯一資料流

```text
 Client input (intent) ──> outbound command queue ──> Network worker ──> Server gateway
          ^                                                            │
          │                                                            v
    UI ViewModel                                                bounded commands
          ^                                                            │
          │                                      single-writer Map World / 30Hz baseline
          │                                                            │
   WorldState/Reducer <── packet decode / ordered inbox <── snapshots & reliable events
          │
          ├── Client prediction / reconciliation / remote interpolation
          │
          └── Render model (read-only)
                    │
              World Y-sort root
                ├── Player paper-doll
                ├── Enemy / NPC / Pet (shared per-type SpriteFrames)
                ├── map scenery
                └── ground items / effects
                    ^
          AssetCatalog + Single AssetStore + ResourceLoader + external PCK
```

核心：**網路接收與渲染無關；Server 決定遊戲結果；Texture 不 Ready 也不會丟失 Spawn、Move、Attack、Death、Pickup 等可靠狀態。**

## 4. 具體架構決策與反過度設計

1. **Client Godot 4 + Server C# .NET 8**；兩個模組化單體，不提前拆微服務／插件架構；功能需要時才新增程式檔。
2. Client 一個 `RuntimeAssetStore` 和可驗證的 `AssetCatalog`，資料切割只依**來源 Asset ID**，不是每個 Map/Enemy/方向/尺寸建立 Loader。
3. Converter 與 Client/Server 完全拆開；保留 **Per-Asset Height-Dense** 作優先 Builder 候選，最大單頁 4096；不強制 POT，必要時 P1 防壓縮邊緣污染；Asset 名稱 `Enemy/`；不多一層 `generation/`。
4. GPU 壓縮採 Godot imported Texture2D + ResourceLoader；**Windows/Mac 必須有平台 remap 實證**，外部 PCK 是封裝不是跨 Asset 合圖。
5. 高密度可見 Enemy 優先採**共享 SpriteFrames、每個可見實例 AnimatedSprite2D**；原版 Origin/ACT/timeline/flip 要對齊，Player/Enemy/Scenery 全部參與 World Y-sort，不能只是把 Monster Node 掛到任意 Canvas。
6. 玩家紙娃娃仍按各部位 Asset 獨立；多層繪製與 DrawOrder 不等於與 Enemy 相同一個 SpriteFrames；專屬驗收，不過度抽象。
7. Server World 對地圖位置、Blocking.dat、AOI、AI、damage、loot、inventory/UID、money/trade 持有權威；外部 DB 和 socket Send 不在 Map tick 內等待。
8. **AFK/Unfocus/低 FPS 限制只作用於呈現**。Node/Texture 可卸載、replay render state；網路處理、可靠事件、Server 跳動不能降低。
9. Client/Server DTO 共享 wire spec/schema 及測試向量，**不是互相引用對方程式碼**；嚴格版本、長度上限、身份／重放檢查。
10. 程式責任按實際運作劃分；不要為了 Monster/Player/NPC 重複建立三種 Cache/Worker/Transport，也不建立未用的 Manager/Utils/Core。

## 5. 重新開發先後與驗收 Gate

| 階段 | 最小交付 | 必須通過 |
|---|---|---|
| 0. Converter | 本次保留的來源／測試可重跑 | 素材 ID、Frame/Origin、ACT、Map/Blocking、Sprite coverage；外部 PCK 待合併 |
| 1. Protocol | 定義 wire，Client/Server 不依賴 UI 的 transport | 相同 packet trace 產生相同 State、錯誤封包 reject、epoch 隔離 |
| 2. World | 固定 tick Server + Client Reducer + MapEpoch/AOI | 玩家連線/登入/切圖/離線、舊事件不污染新地圖 |
| 3. Render | World Y-sort、Player / Enemy 動作視圖與共用素材 | 逐幀 Origin/flip、攻擊/受擊/死亡不中斷、AOI 出入一致 |
| 4. Game | Combat、Drops、Item UID、Persistence、UI | Server 權威、可重播／斷線重試無重複扣款或掉落 |
| 5. Perf | 同批多玩家、怪、掉落＋真實 Server | P95/P99 + input-to-render + RSS/Texture + queue age，不靠降 FPS 掩蓋 |
| 6. Release | Windows/macOS + 外部壓縮 PCK | 30 次換圖/登出/AFK、不同解析度、外觀、可執行檔實測 |

## 6. 我認為應避免的誤區

- **不要為了「砍乾淨」刪掉 Converter 的原版格式研究、驗證 fixture、Renderer A/B benchmark 與它們記錄的根據**。後續新 Client 必須靠這些確定原始語意。
- **不要因為某個隔離場景 P95 比 Recovered 低，就直接把所有 Player、Pet、Mecha 改成相同節點架構**；人物多部位繪製與伺服器同步優先。
- **不要保留舊可執行原型作「新專案底座」**；只移植經獨立測試的資料格式/演算法與原則。
- **不要混淆資料輸出和 Runtime**：`client/assets`、`server/content` 是 Converter 輸出路徑，即使沒有 `project.godot` / `.csproj` 也合法。
- **不要直接清空共享工作樹**：主工作樹仍有其他 Session 未提交的 Height-Dense／PCK 開發，Git cleanup 分支只是保護性隔離的清理成果。

## 7. Review 結論

**建議全面重寫 Client／Server，保留 Converter／資料契約／實測證據。** 目前的功能導向 modular monolith 概念可保留，但**必須由 Packet/WorldState/Renderer/AssetStore 的真實依賴和測試劃分責任**，而不是把舊 `main.gd` 拆到不同檔案而已。

最後正式選型以**玩家能感受到的動作流暢、網路因果正確性和可接受的 RAM/Texture/CPU**為準；不追求每一項孤立數字第一。

## 8. 2026-11-15 實體快取清理與保護紀錄

- 清理前檢查原始 `C:\SGO\Stargate Online IE HK2\Data1.dat` 存在，且 `Recovered`／`Old` 不在任何刪除清單。
- `main` 尚有另一工作階段未提交的 Height-Dense／PCK Converter 內容；先驗證 WIP 檔案快照與 ZIP CRC，**沒有 stash、reset、清掉或修改它們**。
- 原 clean-slate Git worktree 由 `.cache/clean_slate_worktree` 透過 `git worktree move` 移到獨立的 `SGTest-rebuild`，而非直接從檔案系統刪除。
- 在確認無程序占用與 worktree 依賴後，刪除主專案可重建的 `.cache` 約 11.24 GiB、`local-assets` 約 1.11 GiB、`reports` 約 0.09 GiB、舊 `client/assets` 約 1.28 GiB、`server/content` 產物；保留 `client/assets/.gdignore`。4 支本機一次性整理腳本已封存，不作正式 Converter。
- 此工作目前安全存在於 `rebuild/clean-slate-20261115` 分支；**不要把主 `main` 仍保留的舊 Runtime 誤報為已刪除**。Main 快速前進／合併前，先由原工作階段決定尚未提交的轉換器改動如何獨立提交或移交。
- 刪掉 `.cache` 包含本地 .NET SDK／Godot template 快取；後續重建 Client/Server 和打包需要重新安裝/下載，不是 Converter 遺失。原圖、Sidecar、Benchmark 皆需依正式 CLI 重新產生。
