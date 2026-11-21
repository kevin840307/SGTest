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

1. 原始 Hair／起始裝備 ID 與 ItemDefinition、種族、SPR／顏色的關係；確認每種族可選集合。Robot 的起始部件與 Y 的衍生版本規則需分別追溯。
2. Server 驗證的 creation options、外觀持久化、舊角色遷移；創角非法值、重名、滿欄、並發與重送測試。外觀 race 與既有 CombatProfile race 不能默默不一致。
3. 同一正式 Wire 的創角輸入、角色列表／創角結果及 AOI 外觀資料，跨端 golden、畸形封包與 generation 測試。
4. 由同一 Converter 發布已支援 profile 的確切素材集；PlayerPresentation 依 Server 外觀選部件，預覽與世界共用 profile，不各維護一套 race/hair 對照。
5. 創角選項放回來源頁面的位置；換種族／髮型／髮色即時預覽、創角後重登不變、至少兩個真 Client 的外觀一致、GPU／多開／資源 Gate。

以上未完成前，UI 保留暫定外觀提示，不加入會誤導玩家的假選項。
