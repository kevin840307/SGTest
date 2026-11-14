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
| 虛擬檔案系統／Data1 | `Data1.dat` 585,022,714 bytes；SHA-256 `9ae08a8a7bb784720c83bfdd608688c045d717dee3868c13d7b8f444562bfc42`；41,687 entries | `xnFileOpenGroup 0x00552980`、`FileData::ReadBuf 0x00551810/0x00551886`、`xnFileRead 0x00552090`、`LzssDecompress 0x0055A0E0` | `legacy_group_archive.py` + `materialize_legacy_overlay.py`；17 張地圖工作集 461 archive base / 1 override / 2 loose-only | **DIRECT + AUDIT / PARTIAL**；loose override > archive base 已實證，其他可能 group archive 尚待掃描 |
| XN SPR frame/alpha | Paradise NPC 真樣本 4 份；HK2 loose 全量 40,224 SPR 已轉換；`ground.spr` SHA-256 `6f69a5e9d7192090618f69c5f0d8b31b617ee5fb3e6fb3f5334f6da022341cb6` | `xnSpr::LoadSpr 0x00552FC0`、`DecodeFrameLine 0x00553EB0`、`xnSpr::Draw 0x00553FB0` | fail-closed decoder；frame offset/row RLE/bounds；atlas 還原逐像素比對 | **DIRECT + AUDIT / PARTIAL**；最終 RGB565/alpha 與原版畫面尚未逐像素校驗 |
| ACT 動作 | `Enemy/EN023A.ACT` 438 bytes；SHA-256 `7185027b043640f87daeb901b4bd7319d869b8940abf0209bd73713a13118609`；HK2 loose 178/178 ACT 完整解析 | `xnAction::Load .text:005574CB–0055757B`、`Update .text:005576FA–0055777D`、`SetSpeed .text:005576B0–005576BB` | 3,303 frame records、514 events；index bounds；enemy catalog playback probe | **DIRECT + AUDIT / PARTIAL**；raw duration 已知但 Update 呼叫頻率→毫秒仍未證實 |
| 怪物方向／鏡像 | Data1+loose 已知地圖 29 visual；loose audit 50/53 完整 | `CSGOEnemDrawLogic::SetDirection .text:004CB708–004CB7E8`、`InitAction .text:004CBF8C–004CC111` | 29 known-map groups 1,160/1,160 logical facing 單頁；50 loose groups 2,000/2,000 atomic locality | **DIRECT + AUDIT / PASS for catalog mapping**；EN060A/EN061A/EN115A 舊 loose 特例及原版畫面仍需獨立確認 |
| 人物方向 | HK2 Player 真 SPR/DrawOrder 存在；來源對應仍在研究 | `CSGOCharDrawLogic::DirectionMapping .text:004C263F–004C283A`、`SetDirection .text:004C24EA–004C25B3` | `legacy_direction.py` 純函式測試 | **DIRECT / PARTIAL**；action ID、紙娃娃圖層來源、鏡像後 pivot 視覺未完成 |
| 人物 draw-order | `Player/CharDrawOrder.dat` 4,442 bytes；SHA-256 `530bb046a8041e3af33418252784ee46d5295a20f37d639a1c617ba812865284` | `LoadDrawOrderDat .text:00408049`；雙槽位解析 `.text:004082D2–004084BC` | 兩份主要表 102/102 行展開；未知 token fail | **DIRECT + AUDIT / PARTIAL**；實際裝備遮擋畫面尚未逐動作比對 |
| 物品定義→地上圖 | `Item/item.itd` 239,330 encrypted bytes；SHA-256 `585aeec6943830b2cde7eee42294014f0bdc80a1e852e7ca723a48da35547fb2`；解封 8,236,329 bytes / 9,096 records | HK2 `Client.bin 0x004F4ECC–0x004F4F54`：index<500→ground.spr，否則 ÷500→`ground%d.spr` + remainder；參考 `GetItemByID .text:00403AC1–00403B75` / `BinarySearch .text:00409AF0–00409B23` | 8,049→ground.spr + 1,047→ground1.spr，0 越界；13 duplicate IDs 中 12 個 visual conflict、1 equivalent | **DIRECT + AUDIT / PARTIAL**；ground page runtime 已驗證，12 個 duplicate visual conflict 因 HK2 同版 lookup precedence 未證實而 quarantine |
| 地上物 Atlas | `ground.spr` / `ground1.spr` 各 500 frames，item 定義可完整映射 | HK2 ground-page formula 如上；舊 `CSGOItemDrawLogic::InitItem .text:004CCD17–004CCDA9` 為早期單頁 lineage | 1440×900 / 500 instances 的 ground.spr 逐圖 vs 1024 atlas 已量 draw calls/load/Video Mem | **AUDIT / PROVISIONAL**；ground1 映射已驗證，但 ground1 同場 A/B、真地圖 drop co-occurrence 與 StreamingManager 尚未驗 |
| Data1 + 真 Map13 | `Old/服務端/Dat/Map/Map13/Enemy.dat` 17,255 bytes；SHA-256 `04b4b2b223019d229142f6868d10ecab83c7613a6ef163b1f8bff2d7f995324e` | Server DAT 為內容來源；Client archive precedence 由上述 file I/O 證據支持 | Map13 78 spawns / 9 visuals；9/9 ACT 完整；atomic palette 約 10.620 MiB atlas RGBA | **AUDIT / PASS for known-map palette input** |
| Enemy atomic Atlas | Data1+loose 29 known-map visuals；50 loose stress set | 無假設「大 atlas 一定快」；以原 SPR 為 atomic action/facing block | 29 groups：32 pages / 62,287,496 RGBA / 1,160/1,160 locality；Map13 78/300 monster RTX3050 draw calls 不增加 | **AUDIT / PROVISIONAL SELECTED**；正式 async GPU upload、eviction、logout 仍未實作 |
| 50-kind 極端 stress | 50 visual 全載，非正式 runtime 工作集 | — | dense：133 pages / 432,604,792 RGBA / load median 1289.209ms / frame p95 7.955ms / draw p95 372 / Video Mem 595,191,887；atomic：119 pages / 466,182,124 RGBA / 1342.386ms / 7.983ms / 372 / 639,997,855 | **AUDIT / STRESS ONLY**；證明不能全世界常駐，需 map palette |
| 原版畫面逐格比對 | 尚無正式錄影／同版本 runtime baseline | Viewer 僅能展示 decoded frame + raw origin | EN023A atlas 還原像素與來源 SPR 一致；Tk viewer 可啟動 | **NOT VERIFIED**；AS005 仍未完成 |
| 公開發布／轉換權 | 使用者本機持有檔案不等於公開發行授權 | — | repo 僅提交 parser/metadata/fixture，不提交原圖 | **BLOCKED**；AS008 維持阻塞 |

## 目前 M0 Gate 判定

**AS009 本身：PASS。M0 素材 Gate：NOT PASSED。**

原因不是解碼器或怪物 atlas 失敗，而是以下硬缺口仍存在：

1. **AS005 原版視覺實證未完成**：人物站立／行走／攻擊／受擊／死亡、裝備遮擋、pivot、鏡像後位置、alpha/color 仍缺同版本原版畫面逐格對照。
2. **AS008 權利 Gate 阻塞**：尚未確認舊圖、UI、音訊的公開轉換／發布權；因此只能在本機私有素材工作集測試。
3. 人物 action ID→SPR/ACT、紙娃娃 source slot、真實毫秒時序仍未完成；物品 ground page 已由 HK2 binary 驗證，但 12 個 duplicate visual conflict 的 HK2 同版 lookup precedence 仍未證實。
4. 正式 Client 的 StreamingManager、GPU upload budget、切圖／登出回收與 network/render isolation 尚未進入實作 Gate。

因此目前允許：繼續本機 decoder/viewer/atlas/benchmark/來源研究；**不得把 AS009 PASS 誤寫成整個素材 Gate PASS，也不得宣稱正式 Client/Server 已完成。**

## 可重跑核心檢查

```powershell
py -m unittest discover -s tools/tests -q
py tools/legacy_group_archive.py 'C:\SGO\Stargate Online IE HK2\Data1.dat' --list
py tools/audit_enemy_animations.py <private-overlay> reports/enemy-animation-audit.json
py tools/audit_action_page_locality.py <private-atlas>\enemy_catalog.json --require-single-page
& 'C:\SGO\Godot\Godot_v4.7.2-stable_win64_console.exe' --headless --script tools/godot_enemy_catalog_probe.gd -- <private-atlas>\enemy_catalog.json
```

真素材輸出及 benchmark JSON 均維持在 gitignore 目錄；公開 Git 僅保留工具、測試、數值摘要與可定位證據。
