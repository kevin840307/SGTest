# Historical SGTest reference — Asset Renderer Evidence

> REFERENCE ONLY. Archived from the pre-rebuild codebase on 2026-11-15.
> These are exact historical texts, NOT the current client/server implementation or tests.
> Use the current ARCHITECTURE.md and todo.txt for decisions.
> Original individual documents remain recoverable from git commit f755a47.

## Contents

- [ASSET_BUNDLE_ARCHITECTURE.md](#source-asset-bundle-architecture-md)
- [ASSET_EVIDENCE_MATRIX.md](#source-asset-evidence-matrix-md)
- [ASSET_NECESSITY_AUDIT.md](#source-asset-necessity-audit-md)
- [ASSET_PACKING_BENCHMARK.md](#source-asset-packing-benchmark-md)
- [ASSET_RUNTIME_OPTIMIZATION_COMPARISON_2026-11-15.md](#source-asset-runtime-optimization-comparison-2026-11-15-md)
- [ASSET_RUNTIME_STRATEGY.md](#source-asset-runtime-strategy-md)
- [ASSET_STREAMING_AND_RENDERING.md](#source-asset-streaming-and-rendering-md)
- [MAP13_MIXED_RENDERER_VALIDATION_2026-11-15.md](#source-map13-mixed-renderer-validation-2026-11-15-md)
- [RENDERER_PARITY_V2_2026-11-15.md](#source-renderer-parity-v2-2026-11-15-md)
- [RESOURCE_BASELINE.md](#source-resource-baseline-md)
- [RUNTIME_ASSET_STORE.md](#source-runtime-asset-store-md)

---

<a id="source-asset-bundle-architecture-md"></a>

## ASSET_BUNDLE_ARCHITECTURE.md

*Historical source: `docs/ASSET_BUNDLE_ARCHITECTURE.md` — SHA-256 `73c6eee283495c39e7a864d6e523bfc4d290b8c305aa3c193de247151084bd0f`*

# Runtime Asset Bundle Architecture — SUPERSEDED

日期：2026-11-15
狀態：**SUPERSEDED / DO NOT IMPLEMENT**

這份文件原本主張把 Map13 的 Monster + scenery 合成 map lifecycle bundle，並把 Player base parts 合成 session bundle。

2026-11-15 重新檢查原版素材 identity、Runtime 維護需求與四種 packing 公平 benchmark 後，此政策已被撤銷。

新的正式 source of truth：

- [Asset Architecture](ASSET_ARCHITECTURE.md)
- [Asset Packing Benchmark](ASSET_PACKING_BENCHMARK.md)
- [Client Architecture](CLIENT_ARCHITECTURE.md)
- ADR-040（DECISIONS.md）

## 不再允許的 production 結構

```text
assets/bundles/map/<id>/sprites.json
assets/bundles/session/player-*.json
Map + Monster shared atlas
different Monsters shared atlas
different scenery source assets shared atlas
Player body/head/leg/arms/death shared physical atlas
```

## 現行規則

- 每個素材保持獨立 logical/physical asset identity。
- 同一 asset 內 action/facing/frame 可重新 packing。
- 同 visual 的很多 instance 共用同一 AssetStore entry。
- Map 只引用 scenery；Monster 由 Server population definition 指向 `Enemy/<id>/atlas.json`。
- Player parts 可共用 owner lifetime，但不 physical merge。
- 磁碟檔案多可以接受；Runtime working set 才是主要最佳化目標。
- external `assets.pck` 是檔案容器，不是 lifecycle bundle。

舊文件中的 benchmark 數字只保留在 Git history 作歷史研究，不得再當作 production architecture 證據。

---

<a id="source-asset-evidence-matrix-md"></a>

## ASSET_EVIDENCE_MATRIX.md

*Historical source: `docs/ASSET_EVIDENCE_MATRIX.md` — SHA-256 `a5664f460f848e52a18f660fdd9210e8d16edd66d5d976531e0de695c5e18667`*

# AS009 — 原版素材證據矩陣與 M0 Gate 判定

日期：2026-11-14。此文件是 AS009 的單一證據索引；原始／衍生媒體仍只留在本機 `local-assets/` 或原始安裝目錄，不提交公開 Git。證據等級沿用 AGENTS.md：**DIRECT**＝原始位元組／反編譯可定位；**AUDIT**＝可重跑工具對真檔驗證；**HYPOTHESIS**＝待原版畫面或同版本程式驗證。

## 版本基準與限制

參考反編譯為 TestGoGo `feature/local-server-net8` commit `0f37c5268ceeac1144ed13556f3aeb191b93711e` 的 `Old/ClientData/Paradise.exe.lst`，Git blob `16710727227dc5ff8d623ea2acab17cc468fc129`，40,117,423 bytes；其 PE 時間戳記錄為 2004-11-24。

HK2 本機可執行檔與參考版本**不是 binary-identical**：

| 檔案 | bytes | SHA-256 | PE timestamp UTC | 結論 |
| --- | ---: | --- | --- | --- |
| `Client.bin` | 2,457,600 | `9b45569a0f619b5bf7d99f122c6ed12ae44a1e54576686d88de4fae72dd13147` | 2009-02-18T10:58:05Z | 與 2004 Paradise 不同版本 |
| `SGOClient.bin` | 2,330,624 | `6e64368ad43605469246fb5692fd42d8eaa1a7ed0a13aa9a1efac518d46cb3be` | 2008-02-28T12:32:34Z | 與 2004 Paradise 不同版本 |

兩個 HK2 binary 均能直接搜尋到 `Data1.dat`、`011_LOGIN_LOADING.itf`、`ground.spr`、`C_NORMAL.ACT` 字串；因此舊版反編譯可作**行為與格式 lineage 參考**，但其函式位址不是 HK2 binary 位址。所有正式資料契約仍以 HK2 真檔解析、hash、index bounds 與 Godot probe 二次驗證。

## 證據矩陣

| 範圍 | 真實來源／Hash | 參考程式直接證據 | 可重跑驗證 | 現況 |
| --- | --- | --- | --- | --- |
| 虛擬檔案系統／Data1 | `Data1.dat` 585,022,714 bytes；SHA-256 `9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42`；41,687 entries | `xnFileOpenGroup 0x00552980`、`FileData::ReadBuf 0x00551810/0x00551886`、`xnFileRead 0x00552090`、`LzssDecompress 0x0055A0E0` | `legacy_group_archive.py` + `materialize_legacy_overlay.py`；17 張地圖工作集 461 archive base / 1 override / 2 loose-only；`scan_group_archives.py` 掃 41,741 實體檔只找到 Data1（1/1 valid, 0 rejected） | **DIRECT + AUDIT / PARTIAL**；目前 HK2 tree 的同格式 archive 集合已界定，仍缺原安裝包／更新來源 provenance |
| XN SPR frame/alpha | Paradise NPC 真樣本 4 份；HK2 loose 全量 40,224 SPR 已轉換；`ground.spr` SHA-256 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6` | `xnSpr::LoadSpr 0x00552FC0`、`DecodeFrameLine 0x00553EB0`、`xnSpr::Draw 0x00553FB0` | fail-closed decoder；frame offset/row RLE/bounds；atlas 還原逐像素比對 | **DIRECT + AUDIT / PARTIAL**；最終 RGB565/alpha 與原版畫面尚未逐像素校驗 |
| ACT 動作／時間／速度 | `Enemy/EN023A.ACT` 438 bytes；SHA-256 `7185027b043640f87daeb901b4bd7319d869b8940abf0209bd73713a13118609`；HK2 loose 178/178 ACT 完整解析 | `xnAction::Load/Update/SetSpeed .text:005574A3–0055777D`；`CSGOChar::SetSpeed .text:004BF3B5`；`CELxnObj::SetActionSpeed .text:00541BF0`；CDDxDrawObj/CELxnObj vtable `+0x2C=SetActionSpeed`；`SystemSetFrame .text:0050751A`；fixed-loop 與 Map→Char/Enemy→xnAction Update 鏈 | `audit_action_timing_binary.py` 對 Client.bin/SGOClient.bin 各唯一驗證 40/25、decrement、`xnAction::SetSpeed`、duration scale、fixed loop、SystemSetFrame(50)；`audit_action_speed_reference.py` 驗證 world-speed/action-speed 分離，31 個 InitAction→immediate→SetActionSpeed sequence 的 immediate 全為 50；3,303 frames、default 20ms tick；50-group Godot 12,976 steps `timing_verified=true` | **DIRECT + AUDIT / PASS for default timing + reference speed separation**；HK2 caller/packet/data-driven action-speed mapping 仍 PARTIAL |
| 怪物方向／鏡像 | Data1+loose 已知地圖 29 visual；loose audit 50/53 完整 | `CSGOEnemDrawLogic::SetDirection .text:004CB708–004CB7E8`、`InitAction .text:004CBF8C–004CC111` | 29 known-map groups 1,160/1,160 logical facing 單頁；50 loose groups 2,000/2,000 atomic locality | **DIRECT + AUDIT / PASS for catalog mapping**；EN060A/EN061A/EN115A 舊 loose 特例及原版畫面仍需獨立確認 |
| 人物方向 | HK2 Player 真 SPR/DrawOrder 存在；來源對應仍在研究 | `CSGOCharDrawLogic::DirectionMapping .text:004C263F–004C283A`、`SetDirection .text:004C24EA–004C25B3` | `legacy_direction.py` 純函式測試 | **DIRECT / PARTIAL**；action ID、紙娃娃圖層來源、鏡像後 pivot 視覺未完成 |
| 人物 draw-order | `Player/CharDrawOrder.dat` 4,442 bytes；SHA-256 `530bb046a8041e3af33418252784ee46d5295a20f37d639a1c617ba812865284` | `LoadDrawOrderDat .text:00408049`；雙槽位解析 `.text:004082D2–004084BC` | 兩份主要表 102/102 行展開；未知 token fail | **DIRECT + AUDIT / PARTIAL**；實際裝備遮擋畫面尚未逐動作比對 |
| 物品定義→地上圖 | `Item/item.itd` 239,330 encrypted bytes；SHA-256 `585aeec6943830b2cde7eee42294014f0bdc80a1e852e7ca723a48da35547fb2`；解封 8,236,329 bytes / 9,096 records | HK2 `Client.bin 0x004F4ECC–0x004F4F54`：index<500→ground.spr，否則 ÷500→`ground%d.spr` + remainder；參考 `GetItemByID .text:00403AC1–00403B75` / `BinarySearch .text:00409AF0–00409B23` | 8,049→ground.spr + 1,047→ground1.spr，0 越界；13 duplicate IDs 中 12 個 visual conflict、1 equivalent | **DIRECT + AUDIT / PARTIAL**；ground page runtime 已驗證，12 個 duplicate visual conflict 因 HK2 同版 lookup precedence 未證實而 quarantine |
| 地上物 Atlas | `ground.spr` / `ground1.spr` 各 500 frames，item 定義可完整映射 | HK2 ground-page formula 如上；舊 `CSGOItemDrawLogic::InitItem .text:004CCD17–004CCDA9` 為早期單頁 lineage | 1440×900 / 500 instances 的 ground.spr 逐圖 vs 1024 atlas 已量 draw calls/load/Video Mem | **AUDIT / PROVISIONAL**；ground1 映射已驗證，但 ground1 同場 A/B、真地圖 drop co-occurrence 與 StreamingManager 尚未驗 |
| Data1 + 真 Map13 | `Old/服務端/Dat/Map/Map13/Enemy.dat` 17,255 bytes；SHA-256 `04b4b2b223019d229142f6868d10ecab83c7613a6ef163b1f8bff2d7f995324e` | Server DAT 為內容來源；Client archive precedence 由上述 file I/O 證據支持 | Map13 78 spawns / 9 visuals；9/9 ACT 完整；atomic palette 約 10.620 MiB atlas RGBA | **AUDIT / PASS for known-map palette input** |
| Enemy atomic Atlas | Data1+loose 29 known-map visuals；50 loose stress set | 無假設「大 atlas 一定快」；以原 SPR 為 atomic action/facing block | 29 groups：32 pages / 62,287,496 RGBA / 1,160/1,160 locality；Map13 78/300 monster RTX3050 draw calls 不增加 | **AUDIT / PROVISIONAL SELECTED**；正式 async GPU upload、eviction、logout 仍未實作 |
| 50-kind 極端 stress | 50 visual 全載，非正式 runtime 工作集 | — | dense：133 pages / 432,604,792 RGBA / load median 1281.023ms / frame p95 7.924ms / draw p95 372 / Video Mem 595,191,887；measured atomic balanced：65 pages / 442,769,264 RGBA / 1289.890ms / 7.110ms / 372 / 608,852,731 | **AUDIT / STRESS ONLY**；頁數減半且 default action-facing 2,000/2,000 單頁，但不能全世界常駐，仍需 map palette |
| 原版畫面逐格比對 | 尚無正式錄影／同版本 runtime baseline | Viewer 僅能展示 decoded frame + raw origin | EN023A atlas 還原像素與來源 SPR 一致；Tk viewer 可啟動 | **NOT VERIFIED**；AS005 仍未完成 |
| 公開發布／轉換權 | 使用者本機持有檔案不等於公開發行授權 | — | repo 僅提交 parser/metadata/fixture，不提交原圖 | **BLOCKED**；AS008 維持阻塞 |

## 目前 M0 Gate 判定

**AS009 本身：PASS。M0 素材 Gate：NOT PASSED。**

原因不是解碼器或怪物 atlas 失敗，而是以下硬缺口仍存在：

1. **AS005 原版視覺實證未完成**：人物站立／行走／攻擊／受擊／死亡、裝備遮擋、pivot、鏡像後位置、alpha/color 仍缺同版本原版畫面逐格對照。
2. **AS008 權利 Gate 阻塞**：尚未確認舊圖、UI、音訊的公開轉換／發布權；因此只能在本機私有素材工作集測試。
3. ACT **default 毫秒時序與 HK2 `xnAction::SetSpeed` primitive 已由 binary 驗證**；Paradise reference 也已證實 world movement speed 與 action speed 分離、31 個 immediate action-speed caller 全為 50。仍缺人物 numeric action ID→SPR/ACT、紙娃娃 source slot、HK2 caller/packet/data-driven action-speed mapping。物品 ground page 已由 HK2 binary 驗證，但 12 個 duplicate visual conflict 的 HK2 同版 lookup precedence 仍未證實。
4. 正式 Client 的 StreamingManager、GPU upload budget、切圖／登出回收與 network/render isolation 尚未進入實作 Gate。

因此目前允許：繼續本機 decoder/viewer/atlas/benchmark/來源研究；**不得把 AS009 PASS 誤寫成整個素材 Gate PASS，也不得宣稱正式 Client/Server 已完成。**

## 可重跑核心檢查

```powershell
py -m unittest discover -s tools/tests -q
py tools/audit_action_timing_binary.py 'C:\\SGO\\Stargate Online IE HK2\\Client.bin' 'C:\\SGO\\Stargate Online IE HK2\\SGOClient.bin' --output reports/hk2-action-timing-audit.json
py tools/audit_action_speed_reference.py 'C:\\SGO\\Recovered\\Old\\ClientData\\Paradise.exe.lst' --output reports/action-speed-reference-audit.json
py tools/legacy_group_archive.py 'C:\SGO\Stargate Online IE HK2\Data1.dat' --list
py tools/audit_enemy_animations.py <private-overlay> reports/enemy-animation-audit.json
py tools/audit_action_page_locality.py <private-atlas>\enemy_catalog.json --require-single-page
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- <private-atlas>\enemy_catalog.json
```

真素材輸出及 benchmark JSON 均維持在 gitignore 目錄；公開 Git 僅保留工具、測試、數值摘要與可定位證據。

---

<a id="source-asset-necessity-audit-md"></a>

## ASSET_NECESSITY_AUDIT.md

*Historical source: `docs/ASSET_NECESSITY_AUDIT.md` — SHA-256 `cd8f4fb2e9f8284629993780d26238e0663bba5547a3d9ec9968e1805c9679f1`*

# 全 Godot 素材分類必要性稽核

> **HISTORICAL / SUPERSEDED 2026-11-15:** 本文件記錄舊 lifecycle-bundle A/B，不能再當 Production 邊界。現行規則是每素材獨立 identity，見 [ASSET_ARCHITECTURE](ASSET_ARCHITECTURE.md) 與 [ASSET_PACKING_BENCHMARK](ASSET_PACKING_BENCHMARK.md)。

日期：2026-11-14
範圍：Enemy、Player、NPC、Equipment、Item、Pet、Mecha、Map、Effect、UI、Ground Drop，以及所有 Builder / Bundle / Loader / Cache。
狀態：**Production policy / Before-After evidence recorded**。AS005 原版逐格人工視覺與 AS008 發布權仍是獨立 Gate。

## 結論

SGTest 不以素材種類、檔名、方向、動作、圖片尺寸或原始來源檔建立 Runtime 類型。

正式模型只保留：

1. **Normalized definition**：保存「這是什麼」；每個 entity 的 action / facing / mirror / pivot / timeline / layer 可獨立查閱。
2. **Lifecycle bundle**：保存「哪些素材實際一起載入、一起卸載」。
3. **RuntimeAssetStore**：保存「如何共用、引用計數、async decode / GPU commit、bounded cache、確實卸載」。
4. **EntityAtlas**：單一 sprite manifest reader；不知道 Enemy / NPC / Item 等業務分類。

Builder 可以依同一 workload 的量測選 512 / 1024 / 2048 / 4096 或多頁；**Atlas side 是 packing 參數，不是 Runtime asset type**。每個怪物仍能逐動作、逐方向查閱，不要求一怪一圖。

## 分類必要性矩陣

| 現有／可能概念 | 判定 | Production 決策 |
|---|---|---|
| normalized animation definition | **必需** | 保留每 entity 的 action/facing/mirror/pivot/timeline/layer |
| EntityAtlas | **必需** | 唯一 sprite manifest reader |
| RuntimeAssetStore | **必需** | 唯一 async decode/GPU commit/owner/cache store |
| Map lifecycle sprite bundle | **有實測收益** | Map13 monster + scenery 同 bundle；切圖一起 release |
| Session player-base bundle | **有實測收益** | body/leg/head/hair00/arms/death 同 session lifetime |
| Terrain texture + viewport MultiMesh | **必需** | 地圖地磚有不同表示法，不另建 Atlas Loader |
| Ground Drop MultiMesh | **必需（目前 renderer）** | 現階段沒有原版 Item texture consumer，不預建 Item/Drop Atlas 類型 |
| per-enemy Atlas / Loader | **冗餘** | 同 map 共現；已移除 production 分組 |
| per-player-part Atlas / Loader | **冗餘** | base parts 同 session lifetime；已移除 |
| per-map-object Atlas / viewport owner | **冗餘（現 Map13）** | 45 種 scenery 已併入 map lifecycle bundle |
| Direction / Action bundle | **僅供研究** | 是動畫語意，不是 unload boundary |
| Hot / Warm / Cold / Boss / Rare | **僅供研究 heuristic** | 沒有真實 lifetime + A/B 不得變 Runtime type |
| readable / grid / dense / source-locality | **僅供研究 packing** | Builder 內部候選，不暴露 Runtime |
| 512 / 1024 / 2048 / 4096 類型 | **冗餘** | 只作 Builder 候選參數 |
| copy_support_assets 全量鏡像 | **僅供研究；production 冗餘** | 不再由正式 rebuild 產生 |
| NPC / Equipment / Item / Pet / Mecha / Effect 專屬 Loader | **尚未需要；預建即冗餘** | 功能實作時先沿用同一 definition/bundle/store |
| Legacy Interface 全量 Runtime 分類 | **尚未需要；預建即冗餘** | 目前 UI 是 Godot Control；原版 UI 素材仍可在研究工具查閱 |
| Audio 全量 Runtime 鏡像 | **尚未需要；預建即冗餘** | 原始資料留 .cache；有 consumer 後按實際 lifetime 導入 |

## 「可獨立查閱」不等於「獨立圖片」

Production bundle manifest 的每個 normalized entity 都保留：

- `entity`
- `definition_sha256`
- `inspect[action][facing] -> logical requested clip`
- `physical_clips`

例如 Enemy 可直接查 `FA / ND / NH / NS / NW` 的 facing 1～8；requested logical view 再指向 normalized physical clip + `flip_h` + timeline。Viewer／測試不需要知道它最後在第幾張 PNG、第幾頁。

因此：

**Debug / Viewer 的查閱粒度 = entity / action / facing。**
**Runtime 的卸載粒度 = 實測 lifecycle bundle。**
兩者不綁定。

## Runtime asset root 清理 Before / After

完整 production rebuild 以前，`client/assets` 實際包含大量尚未被 Runtime 讀取的全量轉檔輸出：

- Before：**17,274 files / 1,179.31 MiB**
- 類別包含 Player、Enemy、data、Map、MA、MS、SR、Effect、Interface、NPC、audio/images 等。

Client Runtime 引用盤點對 `audio/ images/ interface/ effect/ npc/ ma/ ms/ sr/ data/` 的實際路徑 consumer 為 **0**；`ms/` 唯一文字命中只是註解，不是路徑。

正式 rebuild 改成先清空可重建的 `client/assets`（保留 `.gdignore`），只輸出目前 Runtime 真的讀取的 `bundles/` + `maps/`：

- After：**12 files / 9.036 MiB**
- 檔案數減少約 **99.93%**
- 磁碟量減少約 **99.23%**
- `tools/summarize_assets.py` 驗證 `unexpected_top_level=[]`、`production_clean=true`

原始素材沒有刪除；仍在 `.cache/client`，研究／Viewer 可讀。這是停止「把整套原版資料複製進 Godot Runtime root」，不是丟失素材能力。

## Map13 scenery 分組 A/B

### 舊 viewport 細拆

Map13：

- 367 placed objects
- 45 unique object atlases
- 全部 object decoded RGBA：約 **7.222 MiB**
- 1440 最大視窗 + 180px preload margin 的採樣：
  - P50：21 atlases / **4.394 MiB**
  - P95：30 atlases / **5.414 MiB**
  - Max：32 atlases / **5.813 MiB**

也就是常態為省約 1.8～2.8MiB object resident，需要管理 21～30 個 manifest/acquire/release/owner。

### 合併 monster + scenery 候選

Map13 9 monster + 45 scenery raw clips：

| Side | Pages | RGBA bytes |
|---:|---:|---:|
| 512 | invalid | 885×711 frame 超過 side |
| 1024 | 5 | 18,572,476 |
| **2048** | **2** | **18,569,428** |
| 4096 | 1 | 18,478,500 |

2048 與 4096 lifecycle 20-cycle：

| 指標 | 2048×2 | 4096×1 |
|---|---:|---:|
| Load P95 | 100.107ms | 100.031ms |
| Frame P95 | 16.749ms | 16.729ms |
| Frame P99 | 16.790ms | 16.764ms |
| Upload page P95 | 2.448ms | 2.258ms |
| Static/decode peak | 36,911,847 | **53,543,643** |
| Texture peak | 35,146,819 | 35,023,371 |
| Video peak | 41,557,451 | 41,434,003 |

Synthetic 300 draw 三輪：

- 2048×2：draw P95 **206**；frame P95 約 **7.02～7.05ms**
- 4096×1：draw P95 **73**；frame P95 約 **7.76～8.01ms**

4096 少一頁，但沒有 load/frame 勝利，反而 transient decode/static workspace 多約 16MiB。因此 auto mode 優先 `<=2048`；4096 仍會被量測，也可研究強制，但只有 2048 無法容納 atomic source block 時才自動 fallback。

### 正式 20 Client / 300 Monster / 1000 Drop / 40s

Before（monster bundle + per-object viewport atlases）對 After（monster + scenery shared map bundle）：

| 指標 | Before | After |
|---|---:|---:|
| Mean FPS | 59.996 | **59.999** |
| Draw P95 | 236 | **228** |
| Frame P95 | 19.236ms | **18.947ms** |
| Frame P99 | 20.422ms | **20.322ms** |
| Static memory monitor | 69,419,012 | **68,961,657** |
| Texture memory monitor | **39,057,403** | 46,035,499 |
| Video memory monitor | **45,557,371** | 52,534,347 |
| 20-client process RSS peak | **2,893.73 MiB** | 3,023.27 MiB |
| 約每 Client RSS peak | **144.69 MiB** | 151.16 MiB |

所以這不是「RAM/VRAM 最佳化」；它是約 **+6～7MiB/client resident** 的明確 trade-off，換來：

- 45 個 per-object manifests/pages 不再是 production dependency
- viewport 不再每次管理 21～30 個 texture owners
- clean rebuild 不再依賴 stale `map/*.json`
- draw/frame 小幅改善
- map lifetime 更單純、好維護

30-cycle async lifecycle：

- `all_texture_handles_released=true`
- `engine_texture_recovery_passed=true`
- `store_resident_recovery_passed=true`
- frame P95/P99 **16.824 / 16.906ms**

增加的 resident 在離開 map 後可完整釋放，沒有量到 leak。基於「沒有足夠收益不要增加分組」，Map13 保留 shared map bundle。

## 全素材新增規則

新增 Enemy、NPC、Equipment、Item、Pet、Mecha、Map object、Effect、UI 或 Drop 素材時，Builder 依序回答：

1. 是否通常同時使用？
2. 是否相同載入／卸載時機？
3. 拆開後是否真的能釋放可觀的 RAM / texture / video memory？
4. 合併後是否造成 load P95/P99、GPU upload、frame P95/P99 的可觀退化？
5. 拆分是否在相同 workload 有可量測收益？
6. 新增／修改素材是否仍能只改資料，不改 Runtime core？

沒有證據時，沿用現有 bundle boundary。

任何新 boundary 必須附：

- Before / After 同 workload
- CPU decode
- GPU upload
- draw calls
- RAM / process RSS
- Godot texture/video monitor；若有 driver VRAM 則另列
- 首次載入、切圖、卸載
- cache hit / resident recovery
- frame P95/P99
- 長時間 lifecycle / soak

沒有同 workload Before／After，只能寫「候選／研究」，不能宣稱最佳化完成。

## Builder 離線成本

`build_maps.py` 改成在單次 map build 內只 decode 每個 unique scenery SPR 一次；這只是函式內 local cache，不是新的 Runtime Cache／Loader 類型。相同 Map13 原始來源與相同 fingerprint `3827af...03d9`：

- Before（367 placements 重複 decode）：約 **3.65～3.78s**
- After（45 unique SPR decode）：**0.715～0.717s**

約 80% build-time 改善，輸出 fingerprint 與 pixel round-trip 不變。

## 工具邊界

`tools/README.md` 定義：

- Production build：`run_build_assets.bat` → `tools/asset_converter/cli.py`
- Runtime verify：`tools/asset_converter/verify_runtime.ps1`
- Server contract export：`tools/export_server_content.py`
- Packing benchmark：`tools/asset_converter/benchmarks/packing/`
- Research / Viewer：原版格式 audit/inspect/viewer；不得成為 Runtime dependency
- 舊 lifecycle bundle / normalized candidate builder 與對應 tests 已移除

不為每個素材類別建立資料夾層級、Builder、CLI 或 Loader。

## Gate

這次架構簡化不代表：

- AS005 原版同版本逐格人工視覺已完成
- AS008 原版素材公開／發行授權已完成
- NPC / Equipment / Item / Pet / Mecha / Effect / Legacy UI 已實作

這些仍是各自功能／證據 Gate。現在的規則只是確保未來導入時，**資料增加不等於 Runtime 分類增加**。

---

<a id="source-asset-packing-benchmark-md"></a>

## ASSET_PACKING_BENCHMARK.md

*Historical source: `docs/ASSET_PACKING_BENCHMARK.md` — SHA-256 `87fbc4128d48f78ba36958286d64f487c2696f8f2d3f0010c2f300eba6f6ca74`*

# Asset Packing Benchmark — Per-Asset Candidates

日期：2026-11-15
狀態：**CURRENT FAIR COMPARISON / PRODUCTION SWITCH NOT YET APPLIED**

## 1. 不變的硬規則

本 benchmark 只比較**同一 logical asset 內部**如何排列 frames。

永遠禁止：

- Map + Monster 合圖。
- 不同 Monster 合圖。
- 不同 NPC 合圖。
- 不同 scenery 合圖。
- Player body/head/arms/death 跨 part 合圖。
- Equipment 與 Player base 合成共同 lifecycle atlas。

因此 packing policy 可以換，asset identity 不會換。

## 2. 這次比較的四個候選

### A. Per-Asset Dense Single

舊研究 Mode1 的直接語意名稱。

- 同一 asset 的所有可見 frames 自由 dense shelf。
- 目標單一 page。
- 寬度可變，輸出只保留實際 used bounds。
- 不要求 power-of-two。
- 不跨 asset。

### B. Adaptive Dense Shelf

- 同一 asset 內 frames 可自由排列。
- 候選 side：1024 / 2048 / 4096。
- 允許多 page。
- 在 5% decoded area 容忍範圍內偏好較少 pages。

### C. Adaptive Dense MaxRects

- 同一 asset 內 frames 可自由排列。
- 候選 side：1024 / 2048 / 4096。
- frame-level MaxRects。
- 允許多 page。

### D. Adaptive Atomic

目前 production builder 的 physical policy。

- 原始 SPR 保持 atomic placement block。
- 512 / 1024 / 2048 / 4096 候選。
- 可多 page。

## 3. 公平測試條件

固定 Monster IDs：

```text
EN114A EN089A EN121A EN116A EN023A EN084A
EN111A EN125A EN050C EN080A EN093A EN079A
```

固定：

- Map13 terrain。
- 16 個可視 independent scenery assets。
- 48 個可視 scenery objects。
- 1440×900。
- 1000 monster instances。
- 180 frames / 60 warmup。
- 每候選 3 個獨立 Godot process。
- repeat 2 反轉執行順序。
- deterministic action/facing trajectory。
- Map root 與 Monster root 分離。
- 每次要求 `asset_boundary_verified=true`。

工具：

```text
tools/asset_converter/benchmarks/packing/
├── build_candidates.py
├── benchmark.gd
└── run.py
```

產物只放：

```text
.cache/asset_packing_benchmark/
```

不再把 benchmark PNG/JSON 堆在 `local-assets/`。

## 4. 2 kinds / 1000 instances

| Candidate | Pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| **Per-Asset Dense Single** | **2** | **64.73** | **222.5** | **107.52** | **926** | 8.008 | 8.016 |
| Adaptive Dense Shelf | 11 | 69.35 | 225.8 | 113.66 | 1009 | 8.006 | 8.012 |
| Adaptive Dense MaxRects | 5 | 67.80 | 224.5 | 111.60 | 1041 | 8.001 | **8.010** |
| Adaptive Atomic | 7 | 79.77 | 263.0 | 127.55 | 1064 | **8.002** | 8.016 |

Frame-time 差異很小且三輪有重疊；不能把 0.00x ms 差距解讀成某個候選明顯更快。確定差距是 pages / decoded texture area / texture monitor / load。

## 5. 12 kinds / 1000 instances

| Candidate | Pages | Monster RGBA MiB | Monster load ms | Texture monitor MiB | Draw P95 | Frame P95 ms | Frame P99 ms |
|---|---:|---:|---:|---:|---:|---:|---:|
| **Per-Asset Dense Single** | **12** | **179.06** | 638.5 | **259.93** | 1106 | 7.993 | 8.266 |
| Adaptive Dense Shelf | 42 | 192.33 | **617.5** | 277.56 | 1106 | 7.308 | 7.983 |
| Adaptive Dense MaxRects | 23 | 189.26 | 663.9 | 273.48 | 1106 | 7.272 | **7.787** |
| Adaptive Atomic | 30 | 211.98 | 659.2 | 303.76 | 1106 | **7.215** | 7.990 |

這組 load/frame 有 OS/driver cache 波動；三次中位數不能單獨決定 packing。Dense Single 的優勢是 deterministic：最低 pages 與最低 decoded texture footprint。

## 6. 全素材幾何覆蓋

另外使用正式 archive 的 frame metadata 掃描目前完整 asset set：

- 正式 index：7,890 logical sprite groups。
- 7,890 / 7,890 都能以單一 Dense page、最大邊 <= 4096 放下。
- Monster：137 / 137。
- 目前沒有一個正式 asset 需要先拆成多 page。

Monster 中接近上限的例子：

- EN023A：4051×1174。
- EN078A：4017×2908。
- EN119A：3837×625。

全素材接近上限例子包括 Player back equipment 與 Effect，但仍 <=4096。

這個掃描只使用已驗證的 frame crop/rect metadata 做幾何規劃；production 切換前仍必須由 Builder 重新輸出並做 pixel/origin/hash verification。

## 7. 建議：Per-Asset Dense Atlas

目前最簡單、可維護且有數據支持的目標 policy：

```text
one logical asset
      ↓
trim frames
      ↓
dense single-page pack
      ↓
atlas.json + page_0.png
```

Hard rules：

1. asset boundary 永遠不跨越。
2. 優先單 page。
3. 最大邊 4096。
4. 保留原 frame source / crop / pivot / logical action/facing metadata。
5. 若未來真的出現 >4096 的新素材，才使用 **Paged Dense MaxRects** fallback。
6. fallback 是單一 asset 內部分頁，不是跨 asset bundle。

正式名稱建議：

- primary：**Per-Asset Dense Atlas**
- fallback：**Paged Dense Atlas**

不使用 Mode1 / Mode4 當 production 名稱。

## 8. 為什麼不選其它方案

### Adaptive Atomic

優點是原 SPR placement locality 很直觀；缺點是大型動畫容易多 page。公平測試下記憶體/page/load 沒有勝過 Dense。

### Dense MaxRects

幾何比 shelf 緊，但在限制候選 side 後可能拆頁；對 2 kinds 的 draw calls 反而高於單頁 Dense。若未來有超過4096的 asset，它適合當 fallback。

### Per-Source

已淘汰。每 SPR 一 texture 造成 page/texture switch 過多。

### Strict Row

已淘汰。透明區域浪費大。

### Texture2DArray

目前只列 research candidate，不作 production。它會引入 custom shader / layer-dimension 約束；目前所有正式 asset 已可單 page，沒有足夠利益支持增加這層複雜度。

## 9. Production 狀態

**這份文件尚未把 production builder 從 Adaptive Atomic 切成 Dense。**

使用者目前要求先討論與比較，因此本輪先完成：

- 可重跑候選。
- 公平 benchmark。
- 全 asset 幾何覆蓋。
- 架構與舊工具清理。

若決定採用 Per-Asset Dense Atlas，下一個 implementation commit 才修改正式 Builder，並重新跑完整 7,890 asset pixel/origin/hash verification + Godot runtime regression。

## 10. Model1 / Recovered 深入比較（2026-11-15）

本節回答「即使使用 Model1，frames 怎麼排序、要不要分桶、Recovered 實際作法是否更好」。

### 10.1 Recovered Model1 實際作法

Recovered 不是模擬資料，而是直接檢查：

- `C:\SGO\Recovered\client\Assets\Monsters/*.png + *.json`
- `MonsterLoader.get_monster_frames()`
- `PaperDollLoader.get_sprite_frames()`
- `MonsterEntity / PaperDollEngine`

Recovered Enemy renderer：

```text
one Enemy
  -> one large PNG + JSON
  -> ResourceLoader
  -> one shared SpriteFrames
  -> canonical AtlasTexture per JSON frame
  -> AnimatedSprite2D per entity
```

Recovered Player/weapon/equipment：

```text
one part
  -> one PNG + JSON
  -> PaperDollLoader
  -> SpriteFrames / AtlasTexture
  -> independent AnimatedSprite2D layer
  -> DrawOrder controls layer order
```

Recovered Model1 重要特性：

- 同一 asset 單張大 atlas。
- frame edge-to-edge，沒有 extrude padding。
- `AtlasTexture.filter_clip=true` 防止線性採樣污染鄰格。
- 沒有刪 frame：實測 Enemy 的 Recovered JSON frame count 與 source archive frame count一致。
- 常見寬度固定在 2048，但大型 Enemy 高度可超過4096，例如 EN078A 為 2048x5872。

### 10.2 Model1 排序 / 分桶全類型幾何比較

使用正確四邊 padding 定義，聚焦 2,761 個會直接影響本專案 Runtime 的資產：

- Enemy
- Player Weapon
- Player Equipment
- Map scenery

| Strategy | Estimated RGBA MiB | Median Waste | P90 Waste |
|---|---:|---:|---:|
| **Height global** | **3136.55** | **18.59%** | **52.10%** |
| Size bucket soft | 3155.88 | 19.36% | 52.74% |
| Size bucket hard | 3183.81 | 21.33% | 52.60% |
| Aspect bucket soft | 3300.62 | 24.55% | 64.06% |
| Max-side global | 3307.57 | 23.55% | 73.50% |
| Height band 32 | 3315.59 | 24.92% | 68.72% |
| Height band 64 | 3357.04 | 25.89% | 72.50% |
| Action bucket soft | 3384.79 | 27.81% | 69.52% |
| Width global | 3423.28 | 28.53% | 72.56% |
| Area global | 3485.21 | 29.15% | 81.51% |
| Action bucket hard | 3703.47 | 39.95% | 87.95% |

結論：**不要先按 action/source/size 硬分桶。Height-desc global shelf 是目前最穩定基準。**

各類別：

| Type | Height global | Next best | 結論 |
|---|---:|---:|---|
| Enemy | **703.68 MiB** | Size soft 707.53 | Height 最佳 |
| Weapon | **140.60 MiB** | Size soft 144.60 | Height 優勢更明顯 |
| Equipment | **1926.69 MiB** | Size soft 1938.17 | Height 最佳 |
| Scenery | 約 **365.58 MiB** | 幾乎相同 | 多數單/少 frame，不值得複雜分桶 |

### 10.3 Recovered atlas vs Model1 Height-P0

這裡只比較 atlas 幾何，不混 Renderer。

#### Enemy

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| small EN032A | 2048x178 / 1.391 MiB | 447x521 / 0.888 MiB | **-36.1%** |
| medium EN016A | 2048x468 / 3.656 MiB | 494x1507 / 2.840 MiB | **-22.3%** |
| large EN081A | 2048x1605 / 12.539 MiB | 1015x2940 / 11.383 MiB | **-9.2%** |
| huge EN078A | 2048x5872 / 45.875 MiB | 2778x4094 / 43.385 MiB | **-5.4%** |

#### Weapon

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| B-Dagger006 | 0.135 MiB | 0.085 MiB | **-36.7%** |
| B-Sword024 | 0.381 MiB | 0.340 MiB | **-10.9%** |
| B-Stick003 | 1.203 MiB | 0.797 MiB | **-33.8%** |
| B-Stick011 | 1.875 MiB | 1.327 MiB | **-29.2%** |

#### Equipment

| Sample | Recovered | Height-P0 | RGBA change |
|---|---:|---:|---:|
| G-Bonnet092 | 0.204 MiB | 0.193 MiB | **-5.3%** |
| A-Back023 | 1.031 MiB | 0.866 MiB | **-16.0%** |
| R-Back029 | 3.695 MiB | 3.475 MiB | **-6.0%** |
| R-Back026 | 16.266 MiB | 15.654 MiB | **-3.8%** |

因此在**純 packing**上，可變尺寸 Height-P0 普遍優於 Recovered 固定2048寬。

### 10.4 Padding 比較

候選：

- Height-P0：0px padding。
- Height-P1：1px extrude。
- Height-P2：2px extrude。
- MaxRects-P1：single-page MaxRects + 1px extrude。

代表結果：

- Enemy small：P0 0.888 MiB / P1 0.943 / P2 1.006 / MaxRects-P1 0.970。
- Enemy huge：P0 43.385 / P1 44.235 / P2 44.891 / MaxRects-P1 46.462。
- Weapon large：P0 0.797 / P1 0.857 / P2 0.926 / MaxRects-P1 0.817。
- Equipment huge：P0 15.654 / P1 16.288 / P2 16.571 / MaxRects-P1 17.227。

幾何上 P0 最省。是否能 production 使用 P0，必須與 texture filtering / bleeding 策略一起決定。Recovered 用 edge-to-edge + `filter_clip=true`；SGTest 如果保留直接 Canvas draw，則必須另外驗證 nearest/filter clip/scale 情境，不可只因面積較小就直接移除 padding。

## 11. Native Renderer 比較

這一輪不是把兩種格式塞進同一 Renderer，而是使用各自真正的 Runtime path。

### Recovered

- ResourceLoader。
- imported texture。
- shared SpriteFrames。
- AtlasTexture。
- AnimatedSprite2D per entity/layer。

### SGTest

- Image.load_from_file。
- ImageTexture.create_from_image。
- EntityAtlas。
- GDScript sample()。
- CanvasItem.draw_texture_rect_region()。

條件：

- 1280x720。
- 100 frames / 30 warmup。
- VSync off / uncapped。
- Recovered benchmark 強制 render loop 開啟，避免失焦省電模式污染結果。
- 單素材情境：Enemy 1000 instances；Weapon/Equipment 300 layers。
- 混合情境：2 repeats 取 median。

### 11.1 Enemy mixed

| Scenario | Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---|---:|---:|---:|---:|
| 4 kinds / 1000 | **Recovered Model1** | **35.67** | **2.188** | 1001 | **21.18** |
| | Height-P0 | 189.69 | 4.793 | 1000 | 77.98 |
| | Height-P1 | 181.68 | 4.726 | 1000 | 79.77 |
| | Height-P2 | 181.04 | 5.014 | 1000 | 81.30 |
| | MaxRects-P1 | 208.03 | 4.872 | 1000 | 83.80 |
| 12 kinds / 1000 | **Recovered Model1** | **67.96** | **2.414** | 1001 | **62.26** |
| | Height-P0 | 592.43 | 4.750 | 1000 | 231.17 |
| | Height-P1 | 598.23 | 5.399 | 1000 | 234.59 |
| | Height-P2 | 632.72 | 4.879 | 1000 | 239.57 |
| | MaxRects-P1 | 656.76 | 4.834 | 1000 | 251.78 |

**重要：這不代表 Recovered packing 較小。** 前一節已證明 Height-P0 幾何更小。這裡巨大差距主要來自 loader / texture representation / renderer path。

### 11.2 Weapon mixed 4 / 300 layers

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| Recovered Model1 | 27.19 | **0.810** | 301 | 4.79 |
| **Height-P0** | 9.82 | 1.730 | 300 | **3.40** |
| Height-P1 | **9.47** | 1.794 | 300 | 3.63 |
| Height-P2 | 10.94 | 1.619 | 300 | 3.87 |
| MaxRects-P1 | 10.23 | 1.560 | 300 | 3.58 |

Weapon 的 SGTest atlas 在 load / texture footprint 已有優勢，但 direct Canvas GDScript renderer 仍較慢。

### 11.3 Equipment mixed 4 / 300 layers

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| Recovered Model1 | 65.26 | **1.101** | 301 | 28.26 |
| **Height-P0** | **62.89** | 1.818 | 300 | **26.91** |
| Height-P1 | 81.55 | 1.565 | 300 | 28.30 |
| Height-P2 | 86.26 | 1.718 | 300 | 29.13 |
| MaxRects-P1 | 100.38 | 1.672 | 300 | 29.92 |

### 11.4 Scenery mixed 4 / 600

Recovered 沒有相同的 isolated loose-scenery loader；它的 Map 使用不同 PackedScene/Map lifecycle，因此不假裝做 apples-to-apples。

SGTest：

| Mode | Load ms | Frame P95 ms | Draw P95 | Texture delta MiB |
|---|---:|---:|---:|---:|
| **Height-P0** | 44.84 | **2.859** | 600 | **16.18** |
| Height-P1 | 45.32 | 3.064 | 600 | 16.33 |
| Height-P2 | 49.12 | 3.116 | 600 | 16.49 |
| MaxRects-P1 | **43.95** | 2.992 | 600 | 17.40 |

差距很小；scenery 不值得建立獨立複雜 packing policy。

## 12. 這輪真正的結論

### Packing

目前第一名：

**Per-Asset Height-Descending Dense Atlas**

也就是：

```text
one independent asset
 -> trim frames
 -> sort by height descending, width descending
 -> search useful atlas width
 -> single page <=4096
```

目前沒有證據支持：

- 先按 action 分桶。
- 先按 source 分桶。
- size hard bucket。
- aspect bucket。
- Model1 主路徑使用 MaxRects。

MaxRects 留給未來超過4096的單 asset fallback 比較合理。

### Runtime

目前最大瓶頸不是 packing。

SGTest Enemy 的問題是：

```text
PNG
 -> Image.load_from_file
 -> ImageTexture.create_from_image
 -> Dictionary-heavy EntityAtlas.sample
 -> per-instance GDScript draw submission
```

Recovered 的優勢路徑是：

```text
Godot imported texture
 -> ResourceLoader
 -> shared SpriteFrames / AtlasTexture
 -> engine-side AnimatedSprite2D update/render
```

因此下一輪最值得測的不是再發明新的 bucket，而是：

1. 保留 **per-asset Height Dense**。
2. A/B `0px + filter_clip/nearest` vs `1px extrude`，確認縮放/旋轉/不同解析度無 bleed。
3. A/B **external PCK imported/compressed texture + ResourceLoader** vs目前 raw PNG + ImageTexture。
4. 比較 SGTest 目前 GDScript direct Canvas 與「共享 frame descriptor / engine-side sprite or lower-allocation draw path」。
5. 多 Enemy 交錯時，兩邊都約一 instance 一 draw call；另開 texture-array / safe Y-band batching research，但**不得跨 asset identity 合圖**。

## 13. Runtime Asset Folder Naming

Monster 的 domain 名稱仍為 monster；實體資產 folder 依原版名稱固定：

```text
client/assets/Enemy/<id>/atlas.json
```

Server population definition 也引用：

```text
Enemy/<id>/atlas.json
```

不要再建立舊怪物素材資料夾；Runtime 實體名稱固定為 `client/assets/Enemy/`。

## 15. Runtime-only comparison（3 repeats, Windows process-tree RSS）

本節依使用者要求，**不把磁碟大小、PNG大小、atlas空白率當決策指標**。只比較實際執行：

- Windows Godot process-tree Peak RSS（包含 console + real renderer process）。
- Godot Texture/Video memory delta。
- load time。
- Frame P95 / P99。
- draw calls。
- CPU seconds / Node count 作輔助。

條件：1280x720、VSync off、120 frames、30 warmup、每情境3個獨立 process。Recovered 使用它自己的 ResourceLoader/SpriteFrames/AnimatedSprite2D renderer；SGTest 使用自己的 EntityAtlas/direct Canvas renderer。

> Peak RSS 是完整實際 process 成本，因此包含 Recovered 較多 autoload/node 的基線；這正是實際執行資源，但不能誤解成純 texture 差異。

### 15.1 Enemy：不同尺寸 × 1000

| Size | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | Draw |
|---|---|---:|---:|---:|---:|---:|
| Small | Recovered Model1 | 209.5 | **0.5** | **1.4** | **1.015** | 2 |
| | SG Height-P0 | **147.4** | 1.2 | 3.9 | 3.334 | **1** |
| Medium | Recovered Model1 | 209.4 | **1.2** | 18.5 | **1.059** | 2 |
| | SG Height-P0 | **151.0** | 3.8 | **11.8** | 3.219 | **1** |
| Large | Recovered Model1 | 209.9 | **4.2** | **35.8** | **1.579** | 2 |
| | SG Height-P0 | **156.7** | 15.2 | 39.1 | 3.172 | **1** |
| Huge | Recovered Model1 | 220.0 | **15.3** | **50.7** | **2.042** | 2 |
| | SG Height-P0 | **179.0** | 57.8 | 129.4 | 3.250 | **1** |

Enemy 結論：

- Recovered 的 texture representation + engine-side animation 明顯優於目前 SGTest Enemy renderer。
- SGTest process RSS 低約 29~62 MiB，且只需1個 renderer node。
- SGTest 目前最大問題不是 Height-P0 packing，而是 raw PNG → ImageTexture 與每 instance GDScript sample/draw。

### 15.2 Enemy 混合

| Scenario | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | Draw |
|---|---|---:|---:|---:|---:|---:|
| 4 kinds / 1000 | **Recovered Model1** | 212.9 | **21.2** | **32.5** | **2.461** | 1001 |
| | Height-P0 | **201.7** | 78.0 | 174.9 | 5.072 | 1000 |
| | Height-P1 | 202.7 | 79.8 | 194.8 | 5.130 | 1000 |
| | Height-P2 | 203.2 | 81.3 | 185.4 | 5.123 | 1000 |
| | MaxRects-P1 | 207.7 | 83.8 | 194.3 | 5.202 | 1000 |
| 12 kinds / 1000 | **Recovered Model1** | 217.9 | **62.3** | **77.7** | **2.681** | 1001 |
| | Height-P0 | 190.4 | **231.2** | 609.8 | 5.358 | 1000 |
| | Height-P1 | **189.5** | 234.6 | **600.9** | 5.303 | 1000 |
| | Height-P2 | 194.2 | 239.6 | 602.3 | 5.149 | 1000 |
| | MaxRects-P1 | 194.4 | 251.8 | 650.4 | **5.051** | 1000 |

12-kind Recovered vs Height-P0：

- SGTest Peak RSS 約 **12.6%低**。
- Recovered Texture memory 約 **73%低**。
- Recovered load 約 **87%快**。
- Recovered P95 約 **50%快**。
- draw calls 幾乎相同。

因此 Enemy 不能只調 atlas排序；要優先換 texture/renderer path。

### 15.3 Weapon：不同尺寸 × 300 layers

| Size | Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms |
|---|---|---:|---:|---:|---:|
| Small | Recovered | 209.0 | 0.2 | 1.8 | **0.518** |
| | Height-P0 | **146.3** | **0.1** | **0.8** | 1.138 |
| Medium | Recovered | 209.3 | 0.5 | 2.4 | **0.549** |
| | Height-P0 | **146.6** | 0.5 | **2.0** | 1.155 |
| Large | Recovered | 209.4 | 1.6 | 20.3 | **0.542** |
| | Height-P0 | **147.0** | **1.1** | **2.6** | 1.116 |
| Huge | Recovered | 209.4 | 2.5 | 20.9 | **0.507** |
| | Height-P0 | **150.2** | **1.8** | **4.5** | 1.245 |

Weapon 結論：SGTest Height-P0 的實際資源/load 明顯更好；Recovered 只是 renderer frame time 更快。不要退回 Recovered 固定2048 atlas。

### 15.4 Weapon mixed 4 / 300

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | CPU s |
|---|---:|---:|---:|---:|---:|
| Recovered | 209.7 | 4.8 | 27.7 | **1.029** | 1.48 |
| **Height-P0** | **151.3** | **3.4** | 9.6 | 1.618 | **0.69** |
| Height-P1 | 151.0 | 3.6 | **9.5** | 1.707 | 0.72 |
| Height-P2 | 151.6 | 3.9 | 10.4 | **1.549** | 0.72 |
| MaxRects-P1 | 151.3 | 3.6 | 10.3 | 1.749 | 0.73 |

Height-P0 是最好的資源基準。P2 的 P95 僅快約0.07ms，但多用 texture memory，沒有足夠理由成為主策略。

### 15.5 Equipment mixed 4 / 300

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms | CPU s |
|---|---:|---:|---:|---:|---:|
| Recovered | 214.6 | 28.3 | 65.1 | **1.391** | 1.47 |
| **Height-P0** | 167.1 | **26.9** | **63.4** | 1.750 | **0.77** |
| Height-P1 | **167.0** | 28.3 | 69.2 | 1.844 | 0.78 |
| Height-P2 | 167.3 | 29.1 | 79.3 | 1.782 | **0.75** |
| MaxRects-P1 | 167.6 | 29.9 | 74.3 | **1.672** | 0.81 |

MaxRects-P1 只在 P95 贏 Height-P0 約0.08ms，但 texture/load 都更差；不值得增加 production 分支。

### 15.6 Scenery mixed 4 / 600

| Runtime | Peak RSS MiB | Texture Δ MiB | Load ms | P95 ms |
|---|---:|---:|---:|---:|
| **Height-P0** | **160.1** | **16.2** | 48.0 | 3.200 |
| Height-P1 | 163.5 | 16.3 | 47.3 | 3.375 |
| Height-P2 | 163.0 | 16.5 | 47.0 | 3.234 |
| MaxRects-P1 | 163.3 | 17.4 | **46.6** | **3.095** |

MaxRects 的約0.1ms P95 / 1.4ms load 優勢很小，但增加約3.2MiB Peak RSS與1.2MiB texture；目前不值得為 scenery 做另一套 packing。

## 16. Runtime decision list

### SGTest packing 候選排名

1. **Height-P0** — 整體資源效率最佳；Weapon/Equipment/Scenery/Enemy 都是目前 baseline。
2. **Height-P1** — 若 visual bleeding 驗證需要 padding，作安全 fallback；效能沒有實質優勢。
3. **Height-P2** — 更保守但資源持續增加；沒有 runtime 證據支持預設使用。
4. **MaxRects-P1** — 少數 P95 略好，但 memory/load 綜合較差；只保留研究/fallback。

### Renderer / loader 排名

Enemy：
1. **Recovered imported texture + ResourceLoader + shared SpriteFrames/AnimatedSprite2D**：實際 frame/load/texture 明顯最佳。
2. SGTest direct Canvas：Peak RSS低、node少，但目前 texture/load/frame慢。

Weapon / Equipment：
1. **SGTest Height-P0 asset + lean process**：實際 RSS、load、texture/CPU綜合最佳。
2. Recovered：frame P95較快，但 process/node成本高。

### Recommended hybrid

不要直接複製 Recovered 整套，也不要只改 atlas：

```text
Per-asset Height-P0/Height-P1
        +
Godot imported/compressed texture / ResourceLoader
        +
shared immutable animation/frame metadata
        +
移除每 instance Dictionary-heavy EntityAtlas.sample()
        +
engine-side 或低 allocation renderer
```

Enemy 的多 texture Y-order 交錯使 Recovered 與 SGTest 都接近一 instance 一 draw call；這是下一個 renderer batching 問題，不應靠跨 Enemy 合圖解決。

---

<a id="source-asset-runtime-optimization-comparison-2026-11-15-md"></a>

## ASSET_RUNTIME_OPTIMIZATION_COMPARISON_2026-11-15.md

*Historical source: `docs/ASSET_RUNTIME_OPTIMIZATION_COMPARISON_2026-11-15.md` — SHA-256 `b9fa398635f258540e79561e89ba6392a8c516a68e0ebb635af72db2b38e176e`*

# SGTest Runtime Asset Optimization Comparison — 2026-11-15

Status: **measured isolated benchmark; renderer fidelity and macOS release still require verification**.

## Scope and fairness

- Windows, Godot 4.7.2, GL Compatibility, 1280×720, VSync disabled, 120 frames, 30 warmup, 3 isolated repeats per case (median).
- Windows process-tree Peak RSS includes the console launcher and actual Godot process.
- Texture memory is Godot's reported delta (not measured physical dedicated VRAM).
- Recovered Model1 uses its own imported textures, shared SpriteFrames, AtlasTexture and AnimatedSprite2D.
- Raw SGTest uses PNG → ImageTexture → EntityAtlas/direct Canvas.
- Imported SGTest uses Godot VRAM-compressed Texture2D/ResourceLoader and an optimized frame table/draw path.
- Logical assets remain independent. No Map+Enemy, Enemy+Enemy, equipment+player cross-asset atlas.
- File size and blank space are **not ranking criteria**.
- All timing rows below are isolated rendering benchmarks, **not full gameplay or multiplayer simulation**.

## A. Main controlled comparison: 12 kinds Enemy × 1000

Source: `.cache/asset_packing_benchmark/imported_compare/summary.json`.

| Candidate | Peak RSS MiB | Texture delta MiB | Load ms | P95 ms | P99 ms | Draw P95 |
|---|---:|---:|---:|---:|---:|---:|
| Recovered Model1 | 213.6 | 62.3 | 63.3 | 2.345 | 2.868 | 1001 |
| Raw Height-P0 + original direct | 190.2 | 231.2 | 594.2 | 5.136 | 5.294 | 1000 |
| Raw POT rectangle + direct | 204.4 | 372.0 | 705.7 | 4.998 | 5.337 | 1000 |
| Compressed P0 + original direct | 172.6 | 58.0 | **40.2** | 4.939 | 5.175 | 1000 |
| Compressed POT + original direct | 174.7 | 93.0 | 71.6 | 5.157 | 5.409 | 1000 |
| **Compressed P0 + fast direct** | **167.8** | **58.0** | 47.5 | 3.735 | 3.967 | 1000 |
| Compressed POT + fast direct | 171.9 | 93.0 | 73.3 | 3.525 | 3.650 | 1000 |
| Compressed P0 + sprite benchmark | 171.4 | 58.0 | 43.7 | **2.091** | **2.244** | 1000 |
| Compressed POT + sprite benchmark | 173.2 | 93.0 | 64.9 | 2.187 | 2.432 | 1000 |

**Controlled effects**:
- Changing only raw PNG/ImageTexture to compressed imported texture with the same direct renderer: texture 231.2 → 58.0 MiB, load 594.2 → 40.2 ms, P95 5.136 → 4.939 ms.
- Keeping compressed assets but replacing dictionary-heavy direct draw with precompiled fast draw: P95 4.939 → 3.735 ms.
- Sprite benchmark is the fastest measured but is **not yet functionally equivalent**: its simplified SpriteFrames reconstruction omits the source frame's trim/origin in the displayed sprite offset and does not implement the full ACT/logical-view/mirror semantics. Its world Y-sort/overlap integration has not been proven.

## B. Representative type/size workloads

Source: `.cache/asset_packing_benchmark/optimized_matrix/summary.json`, **a separate three-repeat run**. Avoid mixing timings across different runs as if paired.

| Scenario | Candidate | Peak RSS MiB | Texture delta MiB | Load ms | P95 ms |
|---|---|---:|---:|---:|---:|
| Enemy small ×1000 | Recovered | 209.1 | 0.5 | 1.5 | 1.111 |
|  | Raw Height-P0 | 147.8 | 1.2 | 4.2 | 2.426 |
|  | Compressed P0 fast | **146.8** | **0.3** | 5.9 | **0.760** |
| Enemy medium ×1000 | Recovered | 209.1 | 1.2 | 18.1 | 1.037 |
|  | Raw Height-P0 | 151.5 | 3.8 | 12.6 | 2.525 |
|  | Compressed P0 fast | **147.2** | **1.0** | **6.5** | **0.836** |
| Enemy large ×1000 | Recovered | 209.4 | 4.2 | 34.6 | **1.564** |
|  | Raw Height-P0 | 157.4 | 15.2 | 40.8 | 2.506 |
|  | Compressed P0 fast | **151.0** | **3.8** | **16.5** | 1.938 |
| Enemy huge ×1000 | Recovered | 220.9 | 15.3 | 39.4 | **2.047** |
|  | Raw Height-P0 | 183.5 | 57.8 | 127.0 | 2.489 |
|  | Compressed P0 fast | **157.5** | **14.5** | **35.8** | 2.730 |
| Enemy 4 kinds ×1000 | Recovered | 213.0 | 21.2 | **32.0** | 2.672 |
|  | Raw Height-P0 | 198.6 | 78.0 | 193.6 | 4.401 |
|  | Compressed P0 fast | **164.0** | **19.5** | 45.1 | **2.496** |
| Weapon 4 kinds ×300 | Recovered | 209.4 | 4.8 | 41.8 | 1.052 |
|  | Raw Height-P0 | 151.8 | 3.4 | **9.7** | 1.412 |
|  | Compressed P0 fast | **146.9** | **0.9** | 23.6 | **0.922** |
| Equipment 4 kinds ×300 | Recovered | 213.6 | 28.3 | 81.6 | 1.170 |
|  | Raw Height-P0 | 168.0 | 26.9 | 69.5 | 1.474 |
|  | Compressed P0 fast | **156.4** | **6.8** | **53.9** | **0.911** |
| Scenery 4 kinds ×600 | Raw Height-P0 | 159.6 | 16.2 | 47.4 | 2.669 |
|  | Compressed P0 fast | **152.3** | **4.1** | **31.9** | **2.366** |

Recovered loose-scenery renderer is not equivalent to SGTest's isolated scenery canvas and is omitted.

## C. POT rectangular buckets, e.g. 1024×256 or 2048×512

Source: `.cache/asset_packing_benchmark/optimized_matrix/summary.json`.

| Workload | Compressed P0 Texture MiB | Compressed POT Texture MiB | Compressed P0 load ms | Compressed POT load ms | P0 P95 ms | POT P95 ms |
|---|---:|---:|---:|---:|---:|---:|
| Enemy huge ×1000 | 14.5 | 21.3 | 35.8 | 43.4 | 2.730 | 2.715 |
| Enemy 4 kinds ×1000 | 19.5 | 28.3 | 45.1 | 76.4 | 2.496 | 2.727 |
| Enemy 12 kinds ×1000 | 58.0 | 93.0 | 109.2 | 123.1 | 2.765 | 2.592 |
| Weapon 4 kinds ×300 | 0.9 | 1.2 | 23.6 | 24.0 | 0.922 | 0.921 |
| Equipment 4 kinds ×300 | 6.8 | 12.4 | 53.9 | 55.6 | 0.911 | 0.913 |
| Scenery 4 kinds ×600 | 4.1 | 5.8 | 31.9 | 49.0 | 2.366 | 2.378 |

**Decision**: POT rectangles are a valid candidate but **not the default**. Their small P95 wins in some scenarios are inconsistent between independent runs, while texture usage and loading are consistently more expensive. Keep POT as a constrained/hardware-specific fallback when an actual measured advantage exists, not a required bucket for every asset.

## D. Compression format test

Source: `.cache/asset_packing_benchmark/compression_matrix/summary.json`.

| Enemy 12 kinds, compressed fast | Texture MiB | Load ms | P95 ms |
|---|---:|---:|---:|
| Candidate labeled BPTC | 58.0 | 104.8 | 2.495 |
| Candidate labeled S3TC | 58.0 | 106.5 | 2.507 |
| Basis Universal | 57.9 | 494.5 | 2.508 |

**Important technical correction:** The measurements use the GL Compatibility renderer. In this renderer, Godot ignores the high-quality BPTC option and normally selects S3TC for desktop textures. The demonstrated benefit is **VRAM-compressed imported textures**, not a verified BC7/BPTC-versus-S3TC win. See the Godot Importing Images documentation.

## E. Recommended production approach

1. Keep `Enemy/<id>/` and every other logical asset independent.
2. Use Height-descending, variable-sized, single-page atlas (`<=4096`) as primary packing. Do not force power-of-two rectangles or cross-asset bucket merging.
3. Use imported VRAM-compressed Texture2D via ResourceLoader in an **external PCK** rather than bundling all art inside the executable. Pixel-sensitive UI/icons remain lossless.
4. Keep Client/Server and renderer FPS independent.
5. Apply the precompiled frame table/low-allocation direct draw route **only when original ACT timeline, facing/mirror, trim/pivot, action speed and world Y-order remain correct**.
6. Do not switch the entire game to AnimatedSprite2D on the basis of the isolated 2.091 ms result alone: its benchmark version has incomplete source/paper-doll semantics and does not prove gameplay Y-order parity.
7. No real macOS export/runtime results yet. Windows/macOS can share the same logical asset contract, but the compressed texture variants and exported pack must be validated on actual target hardware.
8. P0 with zero padding requires rendering/compression edge-artifact checks at different scaling/filter modes; UV clipping alone does not prove block-compressed sprite edges visually match the original.

## F. Validation and implementation state

- Per-asset Height-Dense production builder and external compressed PCK pipeline have **working-tree modifications**; do not claim these are committed/pushed or fully released until separately verified.
- Python converter/tests: **128 PASS**.
- Python root contracts: **37 PASS, 1 skipped**.
- Godot Client scripts: **15/15 PASS**.
- C# Server tests: **376 PASS**.
- Windows compressed external PCK smoke artifact exists; final Windows EXE export and macOS runtime validation remain distinct gates.
- Full source archive conversion, deterministic 7,890-asset pixel/pivot/hash validation and production multiplayer/AFK long-run tests have not been re-certified for the new pipeline.

This document records an isolated performance decision, not an assertion that all game and export tasks are complete.

---

<a id="source-asset-runtime-strategy-md"></a>

## ASSET_RUNTIME_STRATEGY.md

*Historical source: `docs/ASSET_RUNTIME_STRATEGY.md` — SHA-256 `fefac2be3544c5e1799909f8bef5f3b638801bfd828d316ee1a50e930ef9781b`*

# Runtime Asset Strategy

日期：2026-11-15
狀態：**ACCEPTED STRUCTURE / PACKING MODE PENDING FINAL USER CHOICE**

本文件是 Runtime 資產邊界的正式規則。重點不是減少磁碟檔案數，而是降低執行期 RAM / VRAM、載入延遲、draw call、CPU/GPU 成本，並讓 AFK 可以可靠釋放世界渲染資源。

## 1. 每個素材必須保持獨立 identity

正式 Runtime 不允許為了方便或減少檔案數，將不同素材綁成一個生命週期資產。

正確：

```text
Enemy/en023a/atlas.json
Enemy/en089a/atlas.json
npcs/npc001/atlas.json
maps/visuals/tree001/atlas.json
maps/visuals/house014/atlas.json
players/b/base/body/atlas.json
players/b/base/head/atlas.json
equipment/b/weapon/sword014/atlas.json
```

不允許：

```text
bundles/map/13/sprites.json       # map + monster + scenery
bundles/session/player-b-base     # body/head/arm/death 合成一份 physical asset
all_monsters.pck                  # 若它被當成共同載入生命週期
```

同一素材內可以有多 page，也可以將它自己的多動作／多方向影格重新 packing；**不能跨 monster / NPC / scenery / player part 合圖**。

## 2. PCK 只是磁碟容器，不是 Runtime Bundle

Windows / macOS 共用：

```text
assets/assets.pck
```

PCK 可以包含很多獨立檔案，這不表示 Runtime 會一起載入。

```text
external PCK
    ↓ mount
res://assets/...
    ↓
AssetCatalog
    ↓
AssetStore
    ↓
只 decode / upload 目前需要的 individual assets
```

磁碟檔多可以接受；不能為了減少檔案數把 Runtime ownership 綁在一起。

## 3. Map 與 Monster 完全分離

`assets/maps/<id>/` 只包含地圖本身：

```text
maps/13/
├── world.json
├── terrain.png
├── tiles.bin
└── blocking.dat
```

`world.json` 的 scenery object 只參考：

```text
maps/visuals/<asset>/atlas.json
```

Map 不包含 Monster image。

Monster 出生與 appearance 由 Server population / entity definition 決定：

```text
definition.sprite = EN023A
definition.atlas  = Enemy/en023a/atlas.json
```

同一 monster 可以跨多張地圖共用，不因 map 重包。

## 4. Collision 使用原版 Blocking.dat

碰撞不建立大量 Godot `CollisionShape2D`。

原版：

```text
Blocking.dat
uint32 width
uint32 height
byte[width*height]
```

Runtime 保留原 byte grid 為：

```text
maps/<id>/blocking.dat
```

Client / Server 都用 cell index O(1) 查詢；目前 walkability 規則維持原驗證邏輯 `(cell & 7) == 0`。

優點：

- 不建立數十萬 Node / Shape。
- Server / Client 可使用相同 source hash。
- 原始碰撞不因 Godot scene 轉換產生誤差。
- 路徑、預測與權威碰撞容易對帳。

## 5. Runtime Loading

仍然只有一套 `AssetStore`：

- bounded async decode
- bounded GPU commit
- refcount / owner epoch
- LRU eviction
- cache byte limit
- cancel stale load
- AFK / map transition resource release

不因素材獨立而建立：

- MonsterLoader
- NPCLoader
- MapObjectLoader
- EquipmentLoader

Renderer 可以不同，但實體資源載入機制共用。

## 6. AFK

AFK：

```text
Network               ON
Reducer / WorldState  ON
Reliable events       ON
World rendering       OFF
World textures        可釋放
External PCK          保持掛載
```

PCK 掛載本身不代表 Texture 常駐；真正 RAM/VRAM 成本由 AssetStore loaded entries 決定。

## 7. Packing Mode 公平測試規則

任何 Atlas Mode 比較必須：

1. 使用**完全相同的 monster IDs / source SPR**。
2. Map asset 固定且與 monster asset 分開。
3. 每一個 Mode 使用獨立 Godot process。
4. 不把 Map + Monster / Monster + NPC 合成後再宣稱是 monster packing 結果。
5. 同一實例數、相同 action/facing deterministic workload。
6. 至少重複三次，以中位數比較。
7. 報告分開列 map cost 與 monster cost。

2026-11-15 發現舊 Mode4 12-kind benchmark 不符合第1點：Mode1/2/3 root 只有固定12隻，但舊 Mode4 root 有更多怪，舊腳本排序後取前12隻，因此舊的 Mode4 12-kind 優勢數字 **INVALID FOR CROSS-MODE COMPARISON**。

新版 benchmark：

```text
Map13 + 2 kinds  x 1000 monsters
Map13 + 12 kinds x 1000 monsters
4 modes
3 independent repeats
repeat 2 反轉 mode 順序
```

Map13 terrain/blocking/scenery 固定；monster root 才依 Mode 改變。每個 trial 都驗證 Map/Monster asset root 分離。

完整私有結果：

```text
local-assets/four-mode-benchmark/map_separated_trials/summary.json
```

目前結果顯示 Mode1 Dense 在這組固定怪物上具有最低或接近最低的 Runtime texture footprint、低 page count 與較低 load cost；最新版 Adaptive Atomic 並未在這組大型怪物上勝過 Dense。因此正式 packing mode **不得再以舊 benchmark 直接指定 Mode4**，需依這次公平結果重新決策。

## 8. 目前實際驗證

全量來源：

- 75,588 SPR
- 7,890 independent sprite groups
- 7,890 unique logical asset IDs
- 0 logical path collision
- 0 export error
- 8,094 atlas pages
- 282,968 frames

Map13 runtime contract：

- 367 placed scenery objects
- 45 independent scenery visual assets
- 9 monster definitions
- 9 independent monster assets
- 7 independent Player-B base parts
- cross-asset bundle = false
- collision = original Blocking byte grid

## 9. 後續規則

- 新增怪物：只新增／更新該 monster asset。
- 地圖換怪：只改 Server population / definition，不重包 map。
- scenery 修圖：只更新該 scenery asset。
- Player 換裝：body / hair / equipment layer 分離。
- 磁碟檔案數不是 optimization target。
- RAM / VRAM / load latency / draw calls / frame P95/P99 / AFK release 才是 Runtime optimization target。

---

<a id="source-asset-streaming-and-rendering-md"></a>

## ASSET_STREAMING_AND_RENDERING.md

*Historical source: `docs/ASSET_STREAMING_AND_RENDERING.md` — SHA-256 `d34eab9085338237115b919d09483d6bfa091de88744e92d1f1b419fe3c15e95`*

# SGTest 高效能素材存取、Atlas 與渲染架構

版本：設計 v1／2026-11-13，2026-11-14 加入 HK2 實測。正式 runtime **NOT VERIFIED**。乾淨 HK2 的 SPR/ACT/SIF 已可本機轉換，並有三組實驗 Atlas；不代表原版視覺、動畫、授權或 Client 已完成。必須遵守 AS001～AS009 及 AS008 授權 Gate。詳 [HK2 Atlas 候選實測](HK2_ATLAS_CANDIDATES.md)。

## 最新執行狀態（2026-11-14）

已有可執行Godot原型與C#／.NET 8 Server；目前先依 [SPEC §0](../SPEC.md) 交付PERF001可重現基準，再做標準動畫定義、自動混合Builder與統一Runtime store。舊候選實測只適用其場景，不代表最新架構已選定或所有Gate通過。一般遊玩Render FPS支援30～90，Client／Server傳輸與固定邏輯頻率不變；低功耗掛機另驗收，見 [隔離契約](CLIENT_RUNTIME_ISOLATION.md)。

## 1. 核心目標

- 降低 Client RAM、VRAM、CPU、GPU Draw Calls、載入 Spike，維持人物、怪物、掉落物準確且流暢；正式視窗寬度約 800～1440 像素，支援多種長寬比與鏡頭縮放。
- **EntityCount ≠ UniqueAssetCount ≠ AtlasPageCount ≠ Godot NodeCount**：一張圖有 500 隻怪、2,000 件掉落物，實際可能只有 8 種怪、25 種物品圖。每個種類只共享一份 Texture、FrameTable、材質和動畫定義；實例只保留自己的 EntityId／座標／方向／ActionTimeline。
- State/Network/Server 權威絕不等待 Texture 讀取、動畫或視覺更新；FPS 不得影響收送包和戰鬥。見 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
- 原版方向×動作×影格及 duration、pivot、offset、palette、alpha、z-order、attachment、事件一律保真。方向數由真實 decoder 證據確立，不預設八向。

## 2. 離線素材管線（不在遊戲執行中解碼/合圖）

```text
原件唯讀 inventory → 格式/版本證據 → Decoder → RGBA + Canonical JSON
→ 透明裁邊（可還原）／重複像素影格去重 → Usage-aware grouping
→ deterministic MaxRects atlas packer → Pages + Frame Region Manifest
→ Godot compiled resource catalog → 視覺 Golden/回歸測試
```

Canonical Metadata 至少包含：SourceHash、DecoderVersion、VisualDefinitionId、ActionId、FacingId、FrameIndex、DurationMs、原始尺寸、TrimRect、Pivot、Offset、ZOrder、AttachmentPoint、Hit/Action Events、Alpha/Palette 轉換資訊。Unknown 欄位明列，不補假資料。裁邊後使用原始坐標還原，不得改變腳底錨點/武器疊層。

Packing：stable sort + deterministic seed + hash，輸出 `AtlasPageId, RegionRect, UV, TextureVariant, ContentHash`。依2026-11-14最新要求，同一entity的同一behavior×logical facing所需影格視為不可拆clip單位；方向alias指向共用來源，不複製像素。不同方向／行為可分頁，但單clip不跨頁；超大clip超過4096時明列不支援／待決策，不默默拆分。絕不改變動畫時間軸。壓縮/縮圖必須經視覺測試；PNG/RGBA 為保真基準，VRAM 壓縮未驗證不得預設啟用。

## 3. Adaptive Multi-Size Atlas 與地圖感知分群

| 頁面（候選） | 單頁 RGBA8 理論 VRAM，無 mipmap | 使用情境 |
| --- | ---: | --- |
| 512×512 | 1 MiB | 少量小素材或分散圖示 |
| 1024×1024 | 4 MiB | 小怪、掉落物、常見圖示 |
| 2048×2048 | 16 MiB | 一般怪/較多動作、角色部件 |
| 4096×4096 | 64 MiB | 大型 Boss 或分組多影格 |

**尺寸不是依怪物顯示大小直接決定**，也不是越大/越少頁就越快。分類依：
1. 場景/地圖/region 同時出現（co-occurrence），例如 Map13 常見怪、掉落物。
2. 種類出現頻率（spawn frequency），Hot/暖/Cold 分層。
3. 材質、透明度、filter、mipmap 設定（兼顧 batching）。
4. 動畫動作、特效使用時機、紙娃娃部件與快取生命週期。
5. frame 總矩形、packing efficiency、GPU 最大尺寸與 runtime VRAM budget。

先按使用關聯分組，群內選 512/1024/2048/4096 page，測試 70–85% 填充率作初始 heuristic，但**填充率不是單一成功指標**。Padding/extrusion 先試 2–4px，依 nearest/linear/mipmap 真實驗證，防止貼圖 bleeding。不能為合批破壞 Y-sort、背後/前方武器層序。

### Map Asset Palette

離線由 map spawn table、NPC、可能掉落物和鄰區集合編譯：

```text
Global Shared Assets: common monsters, common ground items, player parts, common FX
Map13 Palette:
  hot: 6 common monster visuals / 15 common item visuals
  warm: rare monster types / near-area items
  cold: boss / rare special action pages
Map15 Palette:
  own NPC/map pages + references shared assets
```

上述數字與名稱是**設計示例，非真實遊戲盤點**。跨地圖共用頁只留一份；若某類素材只在一張地圖常用，不要和全世界低頻素材合成常駐巨頁。Boss 的 Idle/Walk 與稀有技能動畫可分頁，避免只顯示待命就載入所有特效。Map/Region 的 resource manifest 可以做確定性預取，減少每次 Spawn 的磁碟 I/O。

## 4. 不同實體採不同 Rendering Strategy

| 實體 | 資產共用 | Runtime 呈現 |
| --- | --- | --- |
| 自己角色 | 身體、髮型、裝備、武器各自 TextureAtlas | 精確 Paper Doll、多方向 layer z-order、玩家優先 |
| 其他玩家 | 相同部件 Atlas、可選有上限 AppearanceKey 合成快取 | 可見 Visual Pool；不同玩家不同 Pose/ActionTime |
| 普通怪物/NPC | 同種共享 Texture/AnimationDefinition/Material | 資料導向可視 renderer，少量 Node/CanvasItem；必要時 RenderingServer |
| 大 Boss/機甲/寵物 | 可分動作頁、特殊部件 | 獨立或分層渲染，不為 batch 犧牲動作正確性 |
| 掉落物 | 少數 item visual atlas；同類共用 | 位置與物品 ID 緊湊表、區域批次；避免逐個 Timer/Area2D/Scene |
| 地圖 | 區塊 TileAtlas | Camera WorldRect + Chunk Streaming |
| UI/FX | Theme/NinePatch/FX page | 與 World/Network 的資源 owner 分開 |

MonsterRenderer 可以比較 Sprite2D / CanvasItem / RenderingServer 與 MultiMeshInstance2D；**MultiMesh 使用不同 Atlas UV、不同動畫及 Y-sort 有額外 shader/排序限制，未 benchmark 不選定**。掉落物不動時不重建每個 RenderCommand，點擊用獨立空間查詢，Server 撿取權威不受渲染精簡影響。玩家換裝只替換相關 layer，不能預合成所有外觀的笛卡兒積；相同外觀合成 cache 需 bounded LRU 和失效規則。

## 5. Runtime Asset Streaming 與記憶體

```text
Catalog / Map Palette → Prefetch + Prioritized Request Queue
→ Disk Read / Decode / Prepare（非同步、有界）
→ Main/Render-safe GPU Commit（時間和 bytes 預算）
→ Hot GPU Handles + Optional Warm Prepared RAM + Disk Compiled Pack
→ RefCount/TTL/LRU/Hysteresis → Safe Release
```

- 不強制 RAM、VRAM 各保留一份完整解碼貼圖；依 Godot ResourceLoader 實際行為量測 resident memory。
- P0：自己角色、畫面可見玩家必備動作；P1：戰鬥中的敵人/受擊動作；P2：近場怪與地圖；P3：掉落圖示、裝飾；P4：遠區及不常用技能。**僅排序視覺準備，不排序/丟棄 Server 可靠事件**。
- Background loading 中避免 blocking get；資源完成後的 GPU upload/scene bind 也要限制每幀 commit budget，測 FirstVisibleDelay/UploadSpike。
- 設 preload margin、較大的 unload margin、最短保留時間、防止界線進出反覆 cache churn；切圖、登出解除所有 world owners，shared/global 明確計入基線。
- Cache key 包含 VisualDefinitionId、version/hash、material、quality、appearance，cache 容量有上限，裝備改變需 invalidate。
- 缺圖只影響視覺，不得令 Entity/HP/Position/Action 消失。資源完成時以**最新** WorldState 直接顯示現在 pose，不回放舊 Move。

## 6. 多視窗尺寸與鏡頭縮放

相同 Atlas/Animation Metadata 服務各解析度。區分「相同世界視野但像素變多」及「WorldRect/Zoom 擴大、同屏更多實體」；前者偏 GPU 像素成本，後者會擴張 Chunk 和 VisibleInstances/AOI/資產工作集。必須從 Camera Transform 計算實際世界可見邊界及預載 margin，不可沿用原版 800×600 的固定距離。

可選 QualityVariant 應離線生成、用同一 AssetId 對應；像素美術保真優先。Server 對巨大視野的 AOI 需明確上限與協商，不可讓 Client 已顯示的關鍵角色不更新。

## 7. AS007 實測矩陣與驗收 Gate

比較 A 個別 PNG/SpriteFrames、B 固定 1024、C 固定 2048、D 混合 512/1024/2048/4096、E D+Map co-occurrence hot/warm/cold、F E+不同 rendering backend。保持**相同可視實體、動畫、貼圖解析度、材質品質與硬體**；測試同圖「500怪/8種、2000掉落/25種」的高重用示例與高 unique 種類壓力例；另測不同裝備玩家、Boss、快速切圖、100次登出、800×600/1280×720/1440×810/1440×900/Zoom-out。先前 1080p/4K 測量僅為超規壓力參考，不是正式視窗需求。

測量：UniqueAssetCount、VisibleInstances、AtlasPagesTouched、PageFillRate、DrawCalls、TextureChanges、Overdraw、RAM WorkingSet/PrivateBytes、VRAM Dedicated/Shared、Frame P95/P99、Load P95、CacheHit/Miss、UploadSpikeP99、EvictionChurn、LogoutGrowth，以及獨立的 network/reducer telemetry。

三組 HK2 真素材已作一次 Godot/OpenGL 300 同種類實例 A/B，見 [實測記錄](HK2_ATLAS_CANDIDATES.md)；它不涵蓋此矩陣的多種類、掉落、快取和解析度案例。其他未實測項目仍為 **NOT VERIFIED**。原版逐格 Viewer、授權與完整 AS001～AS009 Gate 尚未完成。

---

<a id="source-map13-mixed-renderer-validation-2026-11-15-md"></a>

## MAP13_MIXED_RENDERER_VALIDATION_2026-11-15.md

*Historical source: `docs/MAP13_MIXED_RENDERER_VALIDATION_2026-11-15.md` — SHA-256 `20ffef7e01c070f18dec9d28d12c55c7ea177389c5c2d59b98eb232472a87ce4`*

# Map13 Mixed Renderer Validation — 2026-11-15

## Scope

This is an **isolated visual-runtime benchmark**, not a full online/MMORPG gameplay certification. It compares Recovered's real MonsterLoader/SpriteFrames/AnimatedSprite2D with SGTest's independent Height-Dense per-Enemy atlas, imported VRAM-compressed textures, and a corrected shared SpriteFrames/AnimatedSprite2D render path.

The benchmark uses **real Map13 world.json placement and 41 independent scenery objects (16 unique asset identities)** around world coordinates (6616,4728). It adds **20 procedurally rendered player placeholders**, **500 MultiMesh drop markers**, **12 benchmark Enemy identities**, per-frame movement, and stable-count **5% AOI despawn/respawn every 20 frames**. Player paper-doll and ground drops are only workload approximations; network packets, map terrain tile drawing and actual server AI are NOT included.

Each asset remains independent. Never merge Map+Enemy, Enemy+Enemy, or Player/Equipment assets to improve these numbers.

## Benchmark conditions

- Godot 4.7.2 / OpenGL Compatibility, Windows RTX 3050
- 1280×720, VSync off, uncapped rendering
- 150 measured/warmup frames, first 35 warmup, three separate process launches per candidate and scenario
- Recovered animation uses its actual MonsterLoader; SGTest loads compressed imported atlases in a separate test Godot project
- Same logical 14 action/direction view set per Enemy type (including mirrored idle/walk/attack)
- Continuous independent movement, frame changes, **5% AOI churn every 20 frames**, shared world Y-sort
- Process tree RSS monitored (launcher + renderer); GPU texture metric is Godot Texture delta, **not physical VRAM**
- Figures are median of independent trials; reported frame p95/p99 include AOI churn and action transitions; order reversed in second repeat

## Comparison

Lower is better. RAM/Texture in MiB; times in ms.

| Scenario | Mode | Peak RSS | Texture Δ | Enemy palette load | Frame p95 | Frame p99 | Draw p95 | >16.7ms frame count |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 12 Enemy types ×300 | Recovered | 214.1 | 66.7 | 59.4 | 1.498 | 1.663 | 342 | 0 |
| | **SGTest Sprite** | **164.3** | **62.4** | 62.3 | **1.402** | **1.535** | 341 | 0 |
| 12 Enemy types ×1000 | Recovered | 217.6 | 66.7 | **45.6** | 3.640 | 3.901 | 947 | 0 |
| | **SGTest Sprite** | **170.7** | **62.4** | 57.5 | **3.421** | **3.865** | 946 | 0 |
| 12 Enemy types ×2000 | Recovered | 219.8 | 66.7 | **50.2** | 7.470 | 10.361 | 1843 | 1 |
| | **SGTest Sprite** | **176.6** | **62.4** | 55.1 | **6.900** | **7.728** | 1843 | 0 |

The 2000-Enemy SGTest Sprite variant improves frame p95 by approximately 7.6%, p99 by 25.4%, and Peak RSS by 19.7% compared with the corresponding Recovered test. At 300/1000 the timing advantage is smaller and should be treated as workload-sensitive. Loading speed does **not** consistently beat Recovered.

AOI churn and action switch counts (same in both modes):

| Visible Enemy target | AOI removed | AOI created | Action switches |
|---:|---:|---:|---:|
| 300 | 105 | 105 | 215 |
| 1000 | 350 | 350 | 715 |
| 2000 | 700 | 700 | 1430 |

## Correctness and script improvements

- The old prototype applied Recovered `flip_h` using an incorrect numerical direction shortcut. It has been replaced with **the actual Recovered MonsterEntity direction mapping** (idle facing4 -> IDLE3 mirrored; walking facing6 -> WALK2 mirrored; attack facing6 -> ATTACK1 mirrored) and matching SGTest logical-view mirror metadata.
- The runtime now updates `flip_h` when the action/direction changes, rather than only when an entity is created.
- Monster-specific action table references remain aligned to the spawned sprite's actual Enemy ID after AOI churn.
- Godot test runner **fails closed** if the required action/view list is incomplete, an imported file is missing, AOI counts mismatch, scenery is absent, Godot logs an error, or expected output is not generated. It deletes a previous run's output before executing so stale JSON cannot be mistaken for a success.
- Python runner monitors the **complete process tree**, reports Windows RSS, CPU and frame metrics, accepts configurable counts/repeats/frames/output path and environment overrides for Godot/Recovered/SGTest paths.
- `ysort_layer_parity.gd`: **5/5 pixel-observed occlusion cases pass** for scenery, player, Enemy and removal/reveal order. It verifies that all categories must share the same Y-sort world.
- Earlier `visual_pixel_parity.gd`: **96/96** compressed-atlas visual comparisons, **36 flipped** cases, **0 differing pixels** on the chosen frames. Earlier `renderer_lifecycle.gd`: 5/5 unload cycles return texture and node counts to the isolated baseline.
- Regression tests in this session: **Python 128/128**, root tools **37 PASS / 1 skipped**, **Godot 15/15**, **C# Server 376/376**.

## How to rerun

Run from the source checkout in Windows PowerShell (Godot 4.7.2 CLI and psutil are required):

```powershell
python tools/asset_converter/benchmarks/packing/run_map13_mix.py
```

Target a quick smoke without overwriting the full report:

```powershell
python tools/asset_converter/benchmarks/packing/run_map13_mix.py --counts 300 --repeats 1 --frames 90 --output-dir .cache/map13_smoke
```

Default inputs are: `C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe`, `C:\SGO\Recovered\client`, `.cache/bptc_matrix` and `client/assets`. Override through environment variables `SGTEST_GODOT`, `SGTEST_RECOVERED_CLIENT`, `SGTEST_COMPRESSED_PROJECT`, `SGTEST_ASSETS_ROOT` if paths differ.

Pixel Y-sort verification:

```powershell
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --rendering-method gl_compatibility --path .cache/bptc_matrix --script tools/asset_converter/benchmarks/packing/ysort_layer_parity.gd -- .cache/ysort_parity.json
```

Raw results: `.cache/asset_packing_benchmark/map13_mix_v1/summary.json`. This stays ignored/local and is not added to Git.

## Production decision and remaining work

**Keep SGTest per-asset Height-Dense + imported compressed Texture2D. Trial the shared SpriteFrames/AnimatedSprite2D path for Enemy instances in the real world renderer**. The mixed benchmark now includes scenery and AOI churn and favors this Sprite variant, while its RAM footprint remains below the Recovered project in this environment.

The current production client **still draws world scenery, player and monsters through a single ordered CanvasItem**. Directly inserting Sprite children would break object interleaving unless the world renderer is restructured to put all visible sorted categories under a common Y-sort root or equivalent correct ordering. This is the **next integration blocker** and should be resolved before switching the client.

Not yet proven: actual server AOI and packets, source-perfect terrain rendering, player/weapon/equipment paper-doll, original animation interruption/attack-hit-death timing in live gameplay, action audio, camera zoom or high-DPI interpolation, Unfocus/AFK and 30 complete map/relogin cycles in the finished client, Windows Release EXE/PCK and real macOS GPU tests. Do not label these completed because this isolated benchmark passes.

---

<a id="source-renderer-parity-v2-2026-11-15-md"></a>

## RENDERER_PARITY_V2_2026-11-15.md

*Historical source: `docs/RENDERER_PARITY_V2_2026-11-15.md` — SHA-256 `5b1ab84d5735e14d83afb58a9026950e8a5c497d2a1e9cf6fe8d36f2dda63587`*

# SGTest Renderer parity vs Recovered Model1 — 2026-11-15

Status: **isolated rendering and lifecycle validation complete; not yet a production Client renderer switch**.

## Goal

Choose the renderer users will feel is most responsive, while staying within acceptable runtime RAM/Texture memory. Retain logical asset boundaries: one Enemy per asset; no Map+Enemy or Enemy+Enemy merged atlas. Windows and macOS share the logical asset contract but macOS has not been measured.

### Runtime candidates

1. Recovered-native: actual Recovered `MonsterLoader.get_monster_frames(id,true)`, imported compressed Texture2D, shared `SpriteFrames`, `AnimatedSprite2D`, MonsterEntity-style `frame_changed` origin correction, world Y-sort.
2. SGTest Sprite: same **SGTest Height-P0 VRAM-compressed imported atlas** as SGTest Fast; shared `SpriteFrames` per Enemy type, independent `AnimatedSprite2D` per visible actor, `frame_changed` trim/origin correction, logical-view tick durations/looping, world Y-sort.
3. SGTest Fast Canvas: exactly the same SGTest imported atlas and logical-view timing, precompiled regions/offsets, one renderer node, explicit Y-sort and per-frame draw submission.

Scripts: `tools/asset_converter/benchmarks/packing/{parity_renderer.gd,recovered_parity_native.gd,visual_pixel_parity.gd,renderer_lifecycle.gd,run_renderer_parity.py}`.

Raw outputs: `.cache/asset_packing_benchmark/renderer_parity_v2/summary.json` and `.cache/pixel_parity_nonblank.json`, `.cache/renderer_lifecycle_result.json`. Results do not contain redistributed source images.

## Test conditions

- Windows; Godot 4.7.2; NVIDIA RTX 3050; OpenGL Compatibility.
- 1280×720; VSync off; 150 measured/warmup frames per case, first 35 as warmup.
- Three independent processes/case, median for each metric; second repetition reverses candidate order.
- Common action set for each Enemy includes IDLE, WALK, ATTACK, HIT, DEATH and multiple directional variants.
- Entity positions change every frame; all candidates perform world Y-sort and periodically change animation.
- Recovered uses its own ACT-aware loader; SGTest uses canonical logical views and timelines. Exact sampling of animation time between the two original clients is not guaranteed identical.
- Windows Peak RSS includes complete Godot launcher/renderer process tree. Engine Texture delta is a Godot monitor, **not a physical GPU VRAM probe**.
- Frame-time P95/P99 measure isolated Godot rendered frames, not end-to-end multiplayer input latency.
- No source and no production map/Server process loaded in the isolated test.

## Results

All RAM/Texture values are MiB; Load and P95/P99 are milliseconds. Lower is better.

| Scenario | Mode | Peak RSS | Texture delta | Load | P95 | P99 | Draw P95 |
|---|---|---:|---:|---:|---:|---:|---:|
| small Enemy ×1000 | Recovered | 209.1 | 0.5 | 1.6 | 1.474 | 1.594 | 2 |
| | **SGTest Sprite** | **150.9** | **0.3** | 2.2 | **0.935** | **1.326** | **1** |
| | SGTest Fast | 148.0 | 0.3 | 2.2 | 2.745 | 2.767 | 1 |
| huge Enemy ×1000 | **Recovered** | 222.5 | 15.3 | 41.7 | **1.988** | **2.114** | 2 |
| | SGTest Sprite | 161.2 | **14.5** | 34.7 | 2.732 | 2.858 | **1** |
| | SGTest Fast | **158.1** | 14.5 | **33.9** | 2.713 | 2.837 | **1** |
| 4 Enemy kinds ×1000 | Recovered | 214.9 | 21.2 | 48.1 | 2.777 | 2.990 | 840 |
| | **SGTest Sprite** | 167.9 | **19.5** | **28.5** | **2.582** | **2.715** | 839 |
| | SGTest Fast | **164.1** | 19.5 | 36.4 | 4.118 | 4.219 | 839 |
| 12 Enemy kinds ×200 | Recovered | 215.1 | 62.3 | **78.7** | 0.829 | **0.913** | 199 |
| | **SGTest Sprite** | 166.5 | **58.0** | 80.8 | **0.796** | 1.031 | 198 |
| | SGTest Fast | **165.9** | 58.0 | 79.1 | 1.017 | 1.151 | 198 |
| 12 Enemy kinds ×1000 | Recovered | 215.6 | 62.3 | **78.0** | 3.047 | 3.316 | 967 |
| | **SGTest Sprite** | 172.6 | **58.0** | 79.5 | **3.014** | **3.298** | **966** |
| | SGTest Fast | **168.2** | 58.0 | **77.8** | 4.399 | 4.548 | 967 |
| 12 Enemy kinds ×2000 | Recovered | 219.8 | 62.3 | **78.7** | 6.576 | 7.396 | **1901** |
| | **SGTest Sprite** | 178.7 | **58.0** | 84.5 | **5.602** | **6.142** | **1901** |
| | SGTest Fast | **168.7** | 58.0 | 79.0 | 8.997 | 9.265 | 1902 |

Key relative outcomes:
- 12 kinds ×2000: SGTest Sprite P95 **14.8% lower** than Recovered, peak RSS **18.7% lower**, Texture delta **6.9% lower**.
- 12 kinds ×1000: P95 difference ~1%, practically a tie at this repeat count; SGTest Sprite RSS ~20% lower.
- 4 kinds ×1000: SGTest Sprite P95 ~7% lower, RSS ~22% lower.
- Huge *single* kind ×1000: Recovered **faster** on P95 (1.988 vs 2.732). Do **not** claim universal superiority.
- SGTest Sprite typically uses ~1001 Nodes where Fast Canvas uses two nodes. However, it retains a modest RSS advantage versus the actual Recovered client in this isolated setup.
- 12-kind interleaved Y-order has ~967 draws per 1000 entities in both; no texture-binding/draw-call miracle was observed.

## Visual and timeline correctness gates

### Draw parity

`visual_pixel_parity.gd` compared the *same imported compressed atlas* via the direct region renderer and `AtlasTexture + Sprite2D`. It tested small/medium/huge Enemy and EN114A; idle/walk/attack/hit/death frames and mirrored directions. Both target images were verified nonblank before comparison.

Result:

- **96 / 96 test cases**.
- **36 mirrored cases**.
- **0 differing RGBA pixels** across all tested viewport captures.

This proves sampled sprite region placement and flip consistency **between the two renderer implementations on the compressed atlas**, not that VRAM compression is pixel-perfect compared with the original uncompressed SPR.

### Frame metadata/timeline

`parity_renderer.gd` statically verifies every generated `SpriteFrames` frame maps to the exact same imported page/region, expected trim-origin offset, flip offset, loop value and frame duration as the logical-view data.

On a 12-Enemy sample, **1,179 frame assertions passed**. This proves the chosen frames' metadata representation, not a full online action/audio/packet simulation.

### Map/AFK lifecycle

`renderer_lifecycle.gd`: 12 Enemy types / 1000 nodes, **five load/unload cycles**.

- Loaded Texture delta: **60,768,112 bytes** each cycle (about 58.0 MiB).
- After each unload: **0 bytes Texture delta** relative to test baseline.
- After each unload: **0 Node delta** relative to test baseline.
- No increasing retained GPU Texture resource in this isolated repeated-creation test.

This does not certify full Game UI/AFK, in-flight worker cancellations, or macOS process RSS.

## Decision for SGTest

**Recommended next implementation candidate**:

```text
Individual Enemy/<id> Height-Dense P0 atlas
  -> Godot imported VRAM-compressed Texture2D in external PCK
  -> one shared SpriteFrames per logical Enemy type
  -> one AnimatedSprite2D per visible moving Enemy
  -> frame_changed offset correction + canonical logical-view timing
  -> correct World Y-sort
  -> shared AssetStore ownership / AFK / map unload
```

Why: This meets the user's preference to prioritize **smoother visible motion** when RAM is acceptable, and is faster *and* lower RAM in the tested 4-kind and 12-kind high-load scenarios. Node count alone should not override the observed frame-time results.

**Do not** switch Player/Equipment/Pet/Mecha automatically: each has distinct draw-order, layering and actions and needs its own rendering fidelity/profiling. Keep low-node Canvas for static scenery/drops as an independent measured decision.

**Remaining acceptance gates before production switch:**

1. Integrate with actual `RuntimeAssetStore`, original server packet updates and actual Map13 Y-sort.
2. Confirm walking/attack/HIT/DEATH, directional mirror, action interrupt/resume and audio event timing under real network jitter.
3. Full player paper-doll, weapon layering, pet/mecha and drops simultaneously: no wrong occlusion / missing animation.
4. Test viewport sizes and P0 compressed edge quality with nearest/linear filtering. A zero-difference comparison between two compressed render paths is **not** equivalent to comparing with original uncompressed pixels.
5. Test AFK/unfocus, multi-map switching, re-login, texture release while workers may still be loading, and memory stability.
6. Measure on Windows Release EXE/PCK **and actual macOS hardware**; separate input-to-render latency from frame P95.
7. Investigate the single huge-Enemy regression and multi-texture draw-call bottleneck only if representative gameplay workloads justify complexity.

Nothing in these test-only scripts modifies production client logic or the original Recovered project.

---

<a id="source-resource-baseline-md"></a>

## RESOURCE_BASELINE.md

*Historical source: `docs/RESOURCE_BASELINE.md` — SHA-256 `cbed3551b7dd4a4ba58a7edaad7061add695360536da3c7eda1db4c09ed535c6`*

# 資源 Runtime 可重現基準（PERF001）

本報告由 `tools/report_runtime_baseline.py` 從實際 JSON 產生。執行成功與達到效能門檻分開；未測項目不標完成。

> **Production update（2026-11-14）**：下方 `entity/direction/readable/current` 表格現在是歷史 A/B 基準，不是正式資產分類。Production 已收斂為 ownership/lifetime bundle，見 [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md)：Map13 九種怪共一個 2048 map bundle、Boy base 共一個 2048 session bundle；方向/動作/尺寸只留在 builder/normalized metadata。簡化後 30-cycle async P95/P99 16.752/16.809ms；20client/300monster/1000drop P95/P99 19.236/20.422ms，execution PASS。

## 測試界線

- 本機 Windows／RTX3050、Godot Compatibility、1440×900、60FPS cap；版本／檔案 SHA／素材頁 SHA 見摘要 JSON 的 provenance。
- Lifecycle 是同步 PNG decode＋ImageTexture 建立＋基準專用 force_sync，2 次暖機後 30 次載入／卸載；不是正式 Runtime store。GPU submit 是 CPU 呼叫耗時；sync wait 不是獨立 GPU timestamp。
- 多人為 20 個實際 Client（1 原生視窗＋19 headless）／300 怪／1000 掉落，40 秒執行、暖機 2 秒後取樣 35 秒；不等於 20 個 GPU 視窗，也不是掛機多開驗收。
- 候選保留同一批 Map13 九種來源。512／1024／2048／4096 是偏好尺寸／必要時 fallback，PNG 會裁至實際矩形；不是每張都配置完整正方形。direction 是整個來源方向分組的舊候選，不是最終 action×direction 混合策略。
- 快取為熱 OS；冷磁碟、實體 driver VRAM、真正 map/logout owner/LRU、時鐘／背景隔離與戰鬥完整負載仍 NOT VERIFIED。

## 載入／卸載（毫秒，RAM 為程序樹 RSS）

| 候選 | 頁 | RGBA MiB | load P95 | decode P95 | submit P95 | sync P95 | frame P99 / max | RSS MiB | 畫面門檻 | 卸載 RSS / texture |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| current | 13 | 10.87 | 56.36 | 5.60 | 0.18 | 0.00 | 60.24 / 61.33 | 161.22 | FAIL | PASS / PASS |
| direction_1024 | 45 | 9.33 | 39.21 | 1.83 | 0.07 | 0.00 | 43.23 / 51.99 | 160.43 | FAIL | PASS / PASS |
| direction_2048 | 45 | 9.21 | 38.88 | 1.74 | 0.06 | 0.00 | 42.19 / 42.35 | 160.60 | FAIL | PASS / PASS |
| direction_4096 | 45 | 9.21 | 42.09 | 1.75 | 0.07 | 0.00 | 47.43 / 57.12 | 159.45 | FAIL | PASS / PASS |
| direction_512 | 45 | 9.77 | 41.05 | 1.77 | 0.07 | 0.00 | 44.29 / 45.11 | 160.11 | FAIL | PASS / PASS |
| entity | 9 | 11.42 | 55.31 | 6.62 | 0.27 | 0.00 | 58.72 / 61.30 | 161.67 | FAIL | PASS / PASS |
| readable_auto | 13 | 14.78 | 58.66 | 6.51 | 0.26 | 0.00 | 60.55 / 78.24 | 166.26 | FAIL | PASS / PASS |

## 多人場景

| 候選 | FPS | frame P95 / P99 | Draw calls P95 | Client / Server RSS MiB | CPU Client / Server（一核心%） | RAM門檻 |
|---|---:|---:|---:|---:|---:|---|
| current | 60.00 | 22.32 / 24.93 | 435.00 | 207.73 / 79.78 | 43.80 / 4.65 | FAIL |
| direction_1024 | 59.99 | 22.40 / 24.18 | 446.00 | 202.65 / 79.32 | 42.65 / 6.20 | FAIL |
| entity | 59.99 | 21.92 / 23.23 | 427.00 | 202.71 / 79.22 | 43.10 / 9.20 | FAIL |
| readable_auto | 60.00 | 21.63 / 22.76 | 427.00 | 203.00 / 79.62 | 41.50 / 5.45 | FAIL |

## 判讀與後續

這是當前同步載入與舊分組的基準，尚未選定最終 Atlas 尺寸或排法。檢查各列 FAIL；不能只因 load 小於 1 秒便宣稱玩家無感，亦不能以平均 60FPS 排除角色位移頓挫。原始記錄、輸入 SHA 與各 gate 詳見 [摘要](../reports/runtime_baseline_summary.json)。

一般遊玩支援 **30～90FPS**；這只改呈現，不改 Client／Server 傳輸或固定邏輯頻率。5／10／15FPS 與停止世界繪製是另列的低功耗掛機模式。RUNTIME206 已以同 Server deterministic state hash、真 Windows unfocus/minimize/restore 與 1／2／4／8 開矩陣完成驗證；完整證據見 [RUNTIME_ISOLATION_VERIFICATION.md](RUNTIME_ISOLATION_VERIFICATION.md)。

ASSET201～205 已完成：標準動畫定義、Atlas 選型、bounded async RuntimeAssetStore 與資料驅動 Monster Content Workflow 分別見 [NORMALIZED_ANIMATION_DEFINITION.md](NORMALIZED_ANIMATION_DEFINITION.md)、[NORMALIZED_ATLAS_CANDIDATES.md](NORMALIZED_ATLAS_CANDIDATES.md)、[RUNTIME_ASSET_STORE.md](RUNTIME_ASSET_STORE.md)、[MONSTER_CONTENT_WORKFLOW.md](MONSTER_CONTENT_WORKFLOW.md)。`normalized_memory` 30-cycle 同素材同步 loader frame P99 53.52ms FAIL → bounded async 17.028ms PASS；20client/300monster/1000drop P95/P99 22.109/23.949ms。RUNTIME206 亦已完成：30/45/60/75/90FPS、5/10/15低功耗、失焦／最小化／恢復、1/2/4/8開皆驗證 network/state 固定節奏與 authoritative state hash 不因 Render 改變。

## 重跑

在儲存庫根目錄、原版只讀且 private generated assets 已建立時執行：

```powershell
.cache/dotnet/dotnet.exe build server/SGTest.Server.csproj -c Release
python tools/build_baseline_candidates.py --source .cache/client
python tools/benchmark_runtime_baseline.py 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --case all
# 各候選依序執行，禁止同時跑 GPU A/B：
python tools/benchmark_runtime_baseline.py 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --case all --monster-assets reports/atlas_candidates/direction_1024 --output reports/runtime_baseline/direction_1024
python tools/report_runtime_baseline.py
```

其他候選目錄為 entity、direction_512、direction_2048、direction_4096、readable_auto。執行工具回傳成功只代表程序／資料收集成功；門檻判定由摘要 gate 呈現。提交的報告只含量測與指紋，原始／生成圖像不公開。

---

<a id="source-runtime-asset-store-md"></a>

## RUNTIME_ASSET_STORE.md

*Historical source: `docs/RUNTIME_ASSET_STORE.md` — SHA-256 `3113fdf03f41642540fb9eb3a052c8170a823ac14167bd20e5df36afde136d16`*

# RuntimeAssetStore — ASSET204

> **2026-11-15 CURRENT POLICY:** RuntimeAssetStore 現在管理 per-asset manifests；Map/Monster/scenery/Player parts 不再 physical merge。下方所有舊 `map/session bundle` 數據只保留作歷史效能證據，現行邊界以 [ASSET_ARCHITECTURE](ASSET_ARCHITECTURE.md) 為準。

日期：2026-11-14，2026-11-15補PERF002/003/ASSET211。ASSET204原Gate已通過；RUNTIME206完整隔離矩陣的獨立證據見 [RUNTIME_ISOLATION_VERIFICATION](RUNTIME_ISOLATION_VERIFICATION.md)，本次未重跑該矩陣。

## 目的

ASSET203 已證明 `normalized_memory` 的 Atlas packing 本身無法消除同步載入尖峰：Map13 9 visuals 的同步 loader frame P99 為 53.52ms。ASSET204 不再改動畫語意或降低同步頻率，而是把資源生命週期改成：背景 CPU decode + 有界 main-thread GPU commit + owner/epoch/cache 管理。

## Runtime 架構

`client/scripts/runtime_asset_store.gd` 是 Client 共用的 texture/atlas store：

- 一個 worker thread 負責 manifest/PNG CPU decode。
- Godot `ImageTexture.create_from_image()` 只在 main thread 執行。
- production `main.gd` 每 frame 呼叫 `poll_gpu(1, 1200)`：最多 1 page，目標 GPU submit budget 1.2ms。
- decoded ready queue 有 byte budget及128個item上限（metadata／error也計數）；單張較大page可在queue空時通過，避免deadlock。另有一個worker中的in-flight page，byte budget不是整個程序RAM上限。
- manifest page 單張上限 64MiB；job 上限 128。
- 每個request有token；owner有epoch。最後owner取消loading時，立即移除queued job／ready events／active token；正在進行的PNG decode只能在page邊界停止，但結果不能重新入queue或提交GPU。部分GPU頁及failed atlas也扣回resident bytes並清除引用。重現與回歸見 [PERF003_CANCELLATION](PERF003_CANCELLATION.md)。
- ready atlas 可被多 owner 共用，同一 path 不重複 decode/upload。
- unowned ready atlas 是 LRU cache；超過 cache budget 自動 eviction。
- entry數也受128上限約束；新增path遇到滿額時，以同一LRU規則先騰一格，即使bytes未超標。只淘汰無owner的ready atlas；全部仍被引用時回傳ERR_BUSY，釋放後可恢復。PERF004自製fixture在512KiB／128項已重現並驗證修正。
- failed 且已無 owner 的 entry 立即移除，下一 generation 可 retry，不會永久吃掉 MAX_JOBS。
- `close()` 釋放所有 owner、force purge、清空 decoded queue 並 join worker。

正式 `main.gd` 設定：

- production unowned cache budget：24MiB；ready/decode queue budget 16MiB。active owners 不受 unowned LRU budget 強制驅逐。
- 每個 Monster visual 使用自己的 `Enemy/<sprite>/atlas.json`；同 sprite 的多個 definition/instance 共用相同 path entry。
- Player B base/death 是 7 個獨立 manifests，但可共用一個 character owner；headless client 不載純視覺 paper doll。
- Map scenery 每個來源 visual 獨立，`map_objects` 只對 viewport 需要的 paths acquire；超出視野一段時間後 release，仍由同一 Store/LRU 控制。
- PCK 只是磁碟容器，不改變 per-asset cache entry。

## Owner / lifecycle

### Monster palette

`monster_visuals.begin_async()` 每次 map generation 先 release 舊 owner、清除 definition→path refs，再取得新 epoch。每個不同 sprite path 是獨立 Store entry；同一 sprite 的多個 definition/instance 只共用同一 entry。切圖途中尚未完成的舊 job 仍由 token/epoch cancel。

### Map objects

`map_objects.configure(..., store, owner)` 讀 `world.objects[].atlas` 的獨立 scenery paths。`prepare(view)` 只 acquire 可視／預取範圍需要的 assets；離開視野超過 release timeout 後釋放該 path。不同 scenery 不共用 physical atlas，但仍共用同一 Store worker/cache。

### Player paper doll

`paper_doll.begin_async()` acquire body/leg/head/hair/left-arm/right-arm/death 七個獨立 manifests；它們共用 character owner epoch。z-order 仍由 normalized `draw_layers/draw_slots` 取得。登入/Client 結束時 release owner；Store close 會回收。

## correctness tests

Godot synthetic/real-private-asset tests：

- `test_runtime_asset_store.gd`：owner sharing、cache hit、force purge、loading cancellation、failed-path retry、automatic cache-cap eviction。
- `test_runtime_asset_cancellation.gd`：1000 generations、MAX_JOBS、過期publish、priority、zero-byte事件數上限、byte backpressure取消、close／restart。`probe_asset_cancel_queue.gd -- --assert-bounded`另驗證部分提交／後頁失敗／相同path重試及native Texture弱引用歸零。
- `test_runtime_asset_cache_pressure.gd`：128個真小Texture低於byte budget時，第129個path可按LRU騰一格；保護pinned／最近使用項目、所有項目有owner時維持ERR_BUSY、釋放一項後恢復；headless及native均PASS。Before/After在`reports/perf004_*.json`。
- `test_monster_visuals.gd`：Map13 9 definitions 指向獨立 monster assets；同 sprite reuse、360 logical views PASS。
- `test_map_objects_async.gd`：獨立 scenery assets、viewport demand、shared Store、reset/purge PASS。
- `test_paper_doll_async.gd`：7 independent base/death manifests、normalized draw-order、shared owner、purge PASS。
- `test_runtime_asset_cache_pressure.gd`：128 independent entries、LRU/pinned/recent/backpressure/recovery，final resident=0。

## 歷史：Lifecycle-bundle 實驗（2026-11-14，**SUPERSEDED**）

以下資料只保留歷史 Before/After；2026-11-15 已由 per-asset architecture 取代。當時曾由多個 per-entity/per-part/per-map-object manifests 收斂成 map/session lifecycle bundles；完整理由見 [ASSET_BUNDLE_ARCHITECTURE.md](ASSET_BUNDLE_ARCHITECTURE.md) 與 [ASSET_NECESSITY_AUDIT.md](ASSET_NECESSITY_AUDIT.md)。早期2048×2 map bundle數據保留在該文件。2026-11-15 PERF002改為Runtime優先512／1024，Map13為1024候選5頁、session2頁，ownership仍不變。修正worker idle持有Image／metadata，telemetry各有2048筆上限、idle不追加，`statistics(false)`不複製樣本。正式release峰值187.09MiB，30次map載卸frame P95/P99 16.822/16.971ms、回收PASS；見 [PERF002_MEMORY](PERF002_MEMORY.md)。取消／部分載入錯誤邊界另由PERF003自製fixture與native probe實證修正，不能單以串行30次載卸證明。

以下 ASSET204 數字保留為「簡化前 normalized_memory 14-page」歷史基準，用來證明 bounded async store 本身的改善。

## 30-cycle lifecycle A/B

候選：ASSET203 selected `normalized_memory`，14 pages / 10,741,992 RGBA bytes。RTX 3050、1440×900、60FPS cap、warm OS cache。

| 指標 | 同步 loader（ASSET203） | RuntimeAssetStore async（final） |
|---|---:|---:|
| Frame P95 | — | **16.885ms** |
| Frame P99 | **53.52ms FAIL** | **17.028ms PASS** |
| Frame max | — | 17.309ms |
| Load wall P50 | — | 249.003ms |
| Load wall P95 | — | 249.914ms |
| Load frames P95 | — | 15 frames |
| GPU commit P95 | main-thread spike path | **1.205ms** |
| Upload submit P95 | — | 0.904ms |
| Worker decode P95 | main-thread decode | 6.409ms |
| Peak Store resident | — | 10.24MiB |
| Peak decoded ready queue | — | 9.02MiB |
| Process RSS peak | — | 164.55MiB |

30 measured cycles：

- `all_texture_handles_released=true`。
- `engine_texture_recovery_passed=true`。
- `store_resident_recovery_passed=true`；final resident=0。
- RAM recovery PASS；last10-first10 median growth約 1.74MiB。
- 448 decoded pages / 448 uploaded pages。
- `source_files_unchanged_during_run=true`。

這個結果不是「載入變成 0ms」；總 wall 約 249ms，刻意攤到約 15 個 frame，避免玩家吃 50ms 級單幀停頓。

## 20-client / 300-monster / 1000-drop

selected `normalized_memory` + production async store，40秒，profile 35秒：

- execution PASS，provenance unchanged。
- mean FPS 59.99。
- frame P95/P99 **22.109 / 23.949ms**，max 28.554ms。
- draw-call P95 422。
- texture memory約 39.44MiB；video memory約 45.64MiB。
- 20/20 `snapshot_received`、20/20 `prediction_ready`、0 ERROR / SCRIPT ERROR。
- prediction `max_correction_px=0`，observed max pending=4。
- monster palette 9 shared atlases / 10.24MiB；與 player core 同 Store 時 native client resident約 **18.57MiB**（19,473,444 bytes）。
- palette async load wall約 378ms；load 期間 network/state 仍由 physics/network 路徑消化。
- Server 大量 client 進場 warm-up tick=300 P95/P99 33.02/69.08ms；穩態 tick=600/900/1200 P99 6.43/5.56/6.31ms。

## 邊界 / 尚未宣稱

- 實體 driver VRAM 仍 NOT VERIFIED；目前使用 Godot texture/video monitor。
- cold-disk cache 尚未做完整重複矩陣；上述 lifecycle 是 warm OS cache。
- 正式 login/logout UI 尚未存在；Store `release_owner/purge/close` 與 map-generation lifecycle 已測。未來 logout flow 必須呼叫同一契約，不得自行保留 atlas refs。
- 30/45/60/75/90 FPS、5/10/15 low-power、unfocus/minimize/restore、1/2/4/8 開的 network/state/render trace 屬 RUNTIME206，ASSET204 不因此宣稱完成。
- AS005 原版同版本人工逐格視覺與 AS008 發布權仍未完成。

## 重跑

```powershell
cd C:\SGO\SRC\SGTest

# Store/component tests
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_runtime_asset_store.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_monster_visuals.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_map_objects_async.gd
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --path client --script res://tests/test_paper_doll_async.gd

# Current per-asset packing A/B (outputs only to .cache)
py tools/asset_converter/benchmarks/packing/build_candidates.py
py tools/asset_converter/benchmarks/packing/run.py

# General runtime isolation/lifecycle regression remains separate
py tools/benchmark_runtime_baseline.py C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe --case lifecycle --loader async --cycles 30 --output reports/runtime_asset_lifecycle
```

---

