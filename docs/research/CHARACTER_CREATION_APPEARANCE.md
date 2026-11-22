# 創角外觀來源與接入進度

日期：2026-11-22。原版／Recovered／Recovered2 為唯讀來源，圖片、原始表格及反編譯全文不提交。以下是可追溯的實作決策，不代表完整創角已完成。

## 已確認來源

| 來源 | 證據 | 實作含義 |
|---|---|---|
| `Recovered/Old/ClientData/Paradise.exe.lst`，`004C7B98`、`004C7BB2`～`004C7D02` | Controller 選擇有 race 0～3 四種分支，分別指定 B/Boy、G/Girl、A/Alien、R/Robot；找不到帶前綴的 ACT 時回對應種族原始 ACT | 不能讓 Girl、Alien、Robot 一律使用 Boy 的動畫時間 |
| 同上 `00408894` 與原始 `Player/R_CharDrawOrder.dat` | Robot 另有繪製排序表 | Converter 對 R 使用獨立排序表；其他已確認人形使用 `CharDrawOrder.dat` |
| 原始 `Player/CharActionName.dat` 與各種族 `*.act` | 主動作名稱、原始 frame index／duration／loop | 沿用同一 `compile_definition`／`logical_views`，保留 ACT 50Hz metadata；移動速度不直接縮放走路動畫 |
| `NewVersion/Recovered2/scripts/game_constants.gd`、`character_select.gd` | UI 種族 0～4 為人族男、人族女、米古族、機動靈、亞斯人；八種髮色；Robot 髮型與髮色選項停用 | 這是新版衍生版本的 UI 規則，不可宣稱原版四分支已證明亞斯人規則 |
| `NewVersion/Recovered2/scripts/paper_doll_engine.gd` | 亞斯人使用 Y 素材，但 Controller 借用 B；Robot 的部件組成與一般人形不同 | Y 借用 B 尚未納入原版 Converter 的 verified 設定；Robot 不可硬塞一般六部件 profile |
| `Recovered/Old/服務端/CreateCharPara.dat` | 有明確 Hair、Bonnet、Left_hand、Right_hand、Upper_armor、Lower_armor 等允許項目 | Hair 是原始數字 ID 清單，不能直接視為 UI 的髮型索引。需連結 ItemDefinition／race 規則後才開放 Server 創角選項 |
| `Recovered/Old/服務端/plugins/SGOLogin.dll.lst`，`GetCreationCheck` 於 `10014AA4`、讀取檔名於 `10014EF2`、`CheckCreationPacket` 於 `1001547C` | Server 確實讀取 CreateCharPara，另有創角封包驗證入口 | 下一步沿此函式追溯允許集合與外觀欄位，不以 UI 上顯示的數目代替 Server 規則 |

## 已實作與驗證

唯一 `tools/asset_converter/content/export_assets.py` 目前能對 B／G／A／R 的 Standard 素材產生各自 ACT 的 normalized animation/v2、八方向 logical view、原始部件排序。每種族 death 使用自身獨立的 death ACT，不與其他種族混用。缺 ACT 或無法確認的 Y 保留 source clips，**不標記 timing verified**。沒有新增第二套 Converter、Atlas 或圖片快取。

`tools/tests/test_scoped_export.py` 使用公開合成資料驗證不同種族實際讀取不同 ACT 時間、R 獨立排序、八方向鏡射、各種族非循環死亡動作、缺 Controller 不偽造 verified、Y 不借用 B。完整 Python tests：123 PASS。

實作提交 `3e6f175`，[Converter CI 44923254495](https://github.com/kevin840307/SGTest/actions/runs/44923254495) SUCCESS。本機完整 Archive 稽核及 22 素材定向發布亦已 **PASS**，22 個獨立 Atlas 皆為 `normalized_player_part`；私有輸出 `.cache/character_creation_assets/`、報告 `reports/character_creation_asset_publication.json`。這是正式 Publisher 的素材與 provenance 驗收，尚未打包進正式雙圖 PCK，也不是多種族 GPU 或 Server 創角驗收。

本機原始來源另實際編譯 22 個素材（B／G／A 各六個基本部件與 B／G／A／R 四個 death），4,290 source frames、1,904 logical views 全部通過 metadata 驗證；私人證據為 `reports/character_animation_source_audit.json`。此結果**不是多種族 GPU／創角 Server／持久化測試**。

前置 UI 切片 `b1d8704` 已有共用 AssetStore 的選角／創角站立預覽，仍使用與世界相同的暫定 B profile；[Client CI 44922373464](https://github.com/kevin840307/SGTest/actions/runs/44922373464) SUCCESS。

## 接下來必須完成

Host／UI 已接入：`serve --character-appearance-rules <PRIVATE_JSON>` 必須與完整的合法 spawn 設定同時提供；`CharacterAppearanceRules.LoadVerified` 限制檔案大小、深度、明確驗證標記、必要整數欄位、重複欄位／選項與數量。設定有誤時不啟動 listener。範例格式（以下**只有合成示例，不是原版合法集合**）：

```json
{"kind":"verified_character_appearance_rules","version":1,"rules_verified":true,"source_reference":"synthetic/example-only","choices":[{"race":0,"hair_index":0,"hair_color":0}]}
```

`client/player/character_creation_options.gd` 是 SessionPanel 唯一的 UI 選擇模型，按 Server tuple 推導相依選單，不以素材目錄數量猜選項。一般皮膚的 `character_appearance_picker.gd` 下拉選單與 Recovered2 左右箭頭共用該模型；切皮膚不重設選擇，待送／登出停止編輯。入列成功即鎖定，以本機 WorldState 的單調 create-result revision 解鎖，避免快回覆被 UI 輪詢略過或等待入列時仍可重按。Recovered2 三列箭頭／數值位置分別依來源 `(351/386/464,424/460/496)`，原先擠在種族列的 slot 編輯器已隱藏，由選角欄位決定。外觀 tuple 會經原 ClientSession 送出並持久化。

現有預覽 Renderer 已共用 B/G/A profile（hair00／色0 實機驗證；legacy 預設示意維持 B）：選到未支援的控制器／髮色時立即清空預覽、停止更新並釋放 scope，顯示暫無預覽，不畫錯誤的 B 人物；角色列表已保存的其他外觀亦如此。這是正確的未完成狀態，**不是已完成所有人物外觀**。未設定外觀的 legacy 角色仍明示為基本示意。

Windows Godot RTX3050 1152×620 六張登入／載入／選角／創角畫面及外觀選擇箭頭已實際檢視；source-coordinate、相依選項、跨 skin 選擇、UI→ClientSession→TLS→SQLite→重登、資源釋放 PASS。Server 279、Python 123 tests PASS。GM capability 測試原先關線前未讀完空背包登入基線，已補齊收包，沒有放寬 CleanEof／權限斷言。正式玩家 UI 各皮膚的 Map13／Map15 檢視按鈕、檢視標題與提示已隱藏，離線 QA 模式保留。

Server 持久化前置已實作（schema v24）：`CharacterAppearance(Race,HairIndex,HairColor)` 及來源字串由 trusted `CharacterAppearanceRules` 的**完整 tuple 允許集合**核准，角色、外觀與錢包在同一 SQLite transaction 建立。Rules 會複製輸入集合並限制大小；通用數值範圍只是儲存邊界，不是各種族合法髮型規則，也不代表來源已由此 class 自動驗證。新 API 需明確提供 Server rules，舊 API 與舊角色保持 `Appearance=null`，不虛構 race/hair，也不從現有 CombatProfile 自動補值。已設定外觀的角色若 provision 不同 race 的 CombatProfile，會在同一交易中拒絕。CharacterRecord 查詢／重新認證會讀回原 tuple 和來源。

持久化切片 `f2a43e1` 的 [Server CI 44924564032](https://github.com/kevin840307/SGTest/actions/runs/44924564032) 與 [Client CI 44924564078](https://github.com/kevin840307/SGTest/actions/runs/44924564078) SUCCESS。後續已接上同一 Wire V1 的 CharacterList／CharacterCreate／CharacterCreateResult 外觀擴充：Server 發布 bounded exact choices，拒絕省略／未列／未啟用的外觀；成功與列表均帶保存值。ClientSession 透過原 intent FIFO 送出複本，WorldState 驗證選項與結果／列表一致，進世界後釋放 creation menu。真 Godot TLS 已驗證選定 tuple 創角→斷線→重登不變→選角→WorldReady，並非只測 C# codec。這仍是**合成規則的協定驗收**，Host 載入與 UI 操作已由後續切片接上；全量原版 allowlist、多種族 GPU／AOI 外觀尚未完成。格式與重跑命令見 [Runtime Protocol](../RUNTIME_PROTOCOL.md#character-appearance-extension-2026-11-22)。

1. 原始 Hair／起始裝備 ID 與 ItemDefinition、種族、SPR／顏色的關係；確認每種族可選集合。Robot 的起始部件與 Y 的衍生版本規則需分別追溯。
2. Host 與 UI 入口已接通；接續以真正來源合法集合驗收，而非只用合成 allowlist。創角初始裝備／能力／完整滿欄流程仍須驗證。
3. 已完成創角／列表 Wire 與 ClientSession；仍需 AOI 外觀資料、Spawn 前後順序及 generation／重同步測試。
4. 由同一 Converter 發布已支援 profile 的確切素材集；PlayerPresentation 依 Server 外觀選部件，預覽與世界共用 profile，不各維護一套 race/hair 對照。
5. 三組選項已放回 Recovered2 來源位置並通過真 UI→TLS 重登；仍需各選項的真實動態人物預覽、至少兩個真 Client 的外觀一致、GPU／多開／資源 Gate。

以上未完成前，UI 保留暫定外觀提示，不加入會誤導玩家的假選項。


2026-11-22 GPU補充：`reports/character_profiles_gpu.json` 已完成 B/G/A hair00/color0、八方向NS/NR在16 updates的16張／48姿態來源比對，原SPR180 frame，超容差0、資源全0、engine error0。Python驗證器不使用Runtime commands推導預期部件，來源manifest獨立重建並核對SPR。frame／flip_h／part_slot故意變異均拒絕。既有雙圖12張和B人物20張回歸PASS。這仍不是所有動畫幀、其他髮型／色、R/Y、AOI或完整創角規則證據。


### 原版創角封包檢查補證（2026-11-22）

以下是唯讀 `SGOLogin.dll.lst` 的直接行為，尚未完成所有欄位語義及素材映射，不能直接匯出正式允許 tuple：

- `OnCreateChar 1000FF22..1000FF30` 以 display-info 指標和起始 slot 參數呼叫 `CheckCreationPacket`。
- `10015873..100158A9` 從該起始 slot 連續檢查8個 ushort，位址為 `display_info + 0x0e + slot*2`，交給 `SubCheckCreation(value,slot)`。
- `SubCheckCreation 10017533..1001757E` 將 value 遮罩為 ushort，再與該 slot 的 `CELArray<ushort>.GetSize()+1` 比較，值不大於此上限才接受。這是**索引範圍檢查**，並未在此函式做 Hair 來源數字 ID 的 membership test；不能直接把 INI Hair 值當作 `HairIndex`。原程式可接受的邊界也不能直接當安全 Runtime 索引，仍須追查其後解析／fallback。
- `10015CC1..10015CCC` 檢查 display-info offset `0x0b` 的 byte 小於4；其完整欄位語義仍需對照 caller/client packet，不能由這一條 alone 推論衍生版本 Y 種族合法。
- `100160D3..10016110`、`10016518..1001652D` 檢查 ushort slots9..12每項至少1，合計最多8。這是後續能力點 UI／Server 規則研究的具體入口，四項名稱與 display-info layout 尚需交叉確認。

下一個來源追查點是 `OnCreateChar` 通過檢查後如何取 `CELArray` 值、配置起始裝備與外觀，以及原 Client Hair/HairColor 封包欄位；不能以 R2 按目錄計算髮型數量的做法替代此映射。


`OnCreateChar` 的後續映射亦已找到：`1000FF07..1000FF1B` 依 offset0x0b 是否等於3，選擇起始 slot0 或1；`10010D1C..10010D4E` 將傳入 ushort 當作各 slot 的 `CELArray<ushort>::operator[]` 索引，取出真正數值寫入裝備結構；`10010D7F..10010DB2` 再以起始 slot+迴圈索引取來源數值寫入建立角色的 CDStream。這確認了「傳入選項索引→配置清單值」是兩層映射，下一步要連結該值的 Item／外觀來源；仍不能將配置清單內容直接當作 SPR 髮型號碼。沒有把原程式寬鬆的 `size+1` 邊界搬入正式 Server。


2026-11-22 Client來源補證：唯一解包輸出 `interface/createcharpara.dat` 與 Old Server 設定的清單目前相同。原 Client `InitCreateCharLib 004174DF..00417604` 讀取此檔並按欄位順序組裝11個選項集合。`ReadOptionConfig 00426C50..00426CA3` 讀取第9/10集合首值到0x18e/0x190；`RefreshHead 00426660..004266AB` 使用 `option1*option2_count+option2` 作第8集合的索引，再將取出的數值套用至人物資料。這補足原版兩層選項映射的算式，但尚未連結 Item ID 到 SPR／palette，未據此啟用全量 Server rules。R2 自有髮型索引與此原版數值不能直接混用。
