# SGTest 獨立實作與驗收任務庫

本目錄為原始盤點的執行版；以每個 MD 為單位進行玩家功能、UI 呈現、原版證據與驗收。不是全部完成報告。正式狀態仍由 TODO / FEATURE_MATRIX 管理。

- 519 個任務：152 個 UI、163 個可執行 Gameplay leaf、71 個 Gameplay 整合、133 個未配對 SPR 逐項證據任務（7 個有 Old/HK 直接字串定位）。直接 ITF 配對 235 個素材；共 368 個素材嘗試讀取幀幾何、368 個成功。
- HK ITF 共 152，結構掃描取得 5134 個候選控件；宣告數量及首偏移吻合 152，部分候選 0，未解析 0；成功以正式解包器解 C.D.E. 5 個。
- COUNT_MATCH 仍非 type/frame/字體/事件綁定的完整語意驗證。未取得證據保留未知，禁止自行猜測。Old 靜態具名呼叫並非完整 opcode、執行順序或公式。
- 原始檔及反編譯全文不提交，只保留 metadata/定位與任務。UI 優先 HK 原圖、Old 行為參考。
- 執行順序遵循 [AGENTS.md](../../../AGENTS.md)：先 P0，再現有可玩鏈最早缺口（尤其 GP-012→013→014），每三個非緊急交付兩玩法一遊戲內 UI。不要每輪重做登入。Gameplay leaf 優先，integration 在其子任務驗收後整合。已符合就驗收現有實作，不重寫。
- Agent 一次取一份 leaf 或 UI MD，先查現有程式與其他 Session，再執行所有適用核取方塊；沒有證據時記錄 blocker 並改做獨立任務。完成後更新唯一正式矩陣/證據及 commit，不能以文件數量當遊戲完成。
- 本次只產出任務規格，未啟動遊戲；個別功能尚未驗證。重建：在 SGTest root 執行 python docs/legacy_task_catalog_20261124/build_execution_specs.py。原始盤點留在上一層。

| Task | Track | 內容 | 優先度 | 父任務 |
|---|---|---|---|---|
| [UI-001_ACCOUNT_LOGIN](ui/UI-001_ACCOUNT_LOGIN.md) | UI | 登入畫面 | P1 | — |
| [UI-002_CONNECTING](ui/UI-002_CONNECTING.md) | UI | 連線等待畫面 | P2 | — |
| [UI-003_SELECT_SERVER](ui/UI-003_SELECT_SERVER.md) | UI | 選擇伺服器 | P1 | — |
| [UI-004_SELECT_CHARACTER](ui/UI-004_SELECT_CHARACTER.md) | UI | 選擇角色 | P1 | — |
| [UI-005_CREATE_CHARACTER](ui/UI-005_CREATE_CHARACTER.md) | UI | 建立角色 | P1 | — |
| [UI-006_StatusLite](ui/UI-006_StatusLite.md) | UI | 角色簡易狀態 HUD（006_StatusLite） | P1 | — |
| [UI-006_StatusLite_G](ui/UI-006_StatusLite_G.md) | UI | 角色簡易狀態 HUD（006_StatusLite_G） | P1 | — |
| [UI-007_HOTKEY_Yl](ui/UI-007_HOTKEY_Yl.md) | UI | 快捷鍵列（007_HOTKEY_Yl） | P1 | — |
| [UI-007_HOTKEY_YS](ui/UI-007_HOTKEY_YS.md) | UI | 快捷鍵列（007_HOTKEY_YS） | P1 | — |
| [UI-008_MINIMAP_L](ui/UI-008_MINIMAP_L.md) | UI | 小地圖（008_MINIMAP_L） | P1 | — |
| [UI-008_MINIMAP_S](ui/UI-008_MINIMAP_S.md) | UI | 小地圖（008_MINIMAP_S） | P1 | — |
| [UI-009_STATUS](ui/UI-009_STATUS.md) | UI | 狀態效果列 | P1 | — |
| [UI-010_MESSAGE01](ui/UI-010_MESSAGE01.md) | UI | 通用訊息對話框（010_MESSAGE01） | P2 | — |
| [UI-010_MESSAGE02](ui/UI-010_MESSAGE02.md) | UI | 通用訊息對話框（010_MESSAGE02） | P2 | — |
| [UI-011_LOGIN_LOADING](ui/UI-011_LOGIN_LOADING.md) | UI | 登入載入進度 | P1 | — |
| [UI-012_DEL_EMAIL](ui/UI-012_DEL_EMAIL.md) | UI | 刪除郵件/驗證輸入畫面 | P3 | — |
| [UI-013_Character_INFO](ui/UI-013_Character_INFO.md) | UI | 人物詳細資料與配點 | P1 | — |
| [UI-014_Character_NICK](ui/UI-014_Character_NICK.md) | UI | 稱號/暱稱清單 | P2 | — |
| [UI-015_Character_JOB](ui/UI-015_Character_JOB.md) | UI | 職業清單與轉職 | P2 | — |
| [UI-016_CHANNEL](ui/UI-016_CHANNEL.md) | UI | 聊天小視窗 | P1 | — |
| [UI-017_CHANNEL_BIG](ui/UI-017_CHANNEL_BIG.md) | UI | 聊天大視窗 | P2 | — |
| [UI-018_EMOTION_BIG](ui/UI-018_EMOTION_BIG.md) | UI | 表情選單 | P2 | — |
| [UI-019_TALKBUBBLE_BIG](ui/UI-019_TALKBUBBLE_BIG.md) | UI | 對話泡泡樣式選單 | P2 | — |
| [UI-020_FRIENDlIST](ui/UI-020_FRIENDlIST.md) | UI | 好友小視窗 | P2 | — |
| [UI-021_COMMAND](ui/UI-021_COMMAND.md) | UI | 主功能工具列 | P1 | — |
| [UI-022_equip](ui/UI-022_equip.md) | UI | 裝備視窗 | P1 | — |
| [UI-023_ITEM](ui/UI-023_ITEM.md) | UI | 背包視窗 | P1 | — |
| [UI-024_Pet](ui/UI-024_Pet.md) | UI | 寵物背包入口（024_Pet） | P2 | — |
| [UI-024_RANK](ui/UI-024_RANK.md) | UI | 排行榜主視窗（024_RANK） | P2 | — |
| [UI-024b_RANK](ui/UI-024b_RANK.md) | UI | 排行榜主視窗（024b_RANK） | P2 | — |
| [UI-025_RANK_02](ui/UI-025_RANK_02.md) | UI | 排行榜詳細/第二頁 | P2 | — |
| [UI-026_TEAM](ui/UI-026_TEAM.md) | UI | 組隊視窗 | P2 | — |
| [UI-027_Skill](ui/UI-027_Skill.md) | UI | 技能視窗 | P1 | — |
| [UI-028_Mix_item](ui/UI-028_Mix_item.md) | UI | 物品合成/強化視窗 | P2 | — |
| [UI-029_SYSTEM](ui/UI-029_SYSTEM.md) | UI | 系統設定視窗 | P2 | — |
| [UI-030_REPAIR](ui/UI-030_REPAIR.md) | UI | 裝備修理視窗 | P2 | — |
| [UI-031_SHOP](ui/UI-031_SHOP.md) | UI | NPC 商店主視窗 | P1 | — |
| [UI-032_SHOP_02](ui/UI-032_SHOP_02.md) | UI | NPC 商店數量確認 | P1 | — |
| [UI-033_MESSAGE_INGAME](ui/UI-033_MESSAGE_INGAME.md) | UI | 遊戲內訊息 | P2 | — |
| [UI-033_MESSAGE_NAME](ui/UI-033_MESSAGE_NAME.md) | UI | 名稱輸入提示 | P2 | — |
| [UI-034_BANK](ui/UI-034_BANK.md) | UI | 銀行與倉庫視窗 | P2 | — |
| [UI-035_TRADE](ui/UI-035_TRADE.md) | UI | 玩家交易視窗（035_TRADE） | P2 | — |
| [UI-035_TRADE_02](ui/UI-035_TRADE_02.md) | UI | 玩家交易視窗（035_TRADE_02） | P2 | — |
| [UI-036_PetItem](ui/UI-036_PetItem.md) | UI | 寵物物品視窗 | P2 | — |
| [UI-037_PetEquip](ui/UI-037_PetEquip.md) | UI | 寵物裝備視窗 | P2 | — |
| [UI-038_WAITINGBOX](ui/UI-038_WAITINGBOX.md) | UI | 等待提示框 | P2 | — |
| [UI-039_PetInfo](ui/UI-039_PetInfo.md) | UI | 寵物詳細資訊 | P2 | — |
| [UI-040_Rightbox](ui/UI-040_Rightbox.md) | UI | 右側快捷面板（040_Rightbox） | P1 | — |
| [UI-040_Rightbox_02](ui/UI-040_Rightbox_02.md) | UI | 右側快捷面板（040_Rightbox_02） | P1 | — |
| [UI-040_Rightbox_Ext](ui/UI-040_Rightbox_Ext.md) | UI | 右側快捷面板（040_Rightbox_Ext） | P1 | — |
| [UI-041_balloon_01](ui/UI-041_balloon_01.md) | UI | 聊天/NPC 對話泡泡（041_balloon_01） | P2 | — |
| [UI-041_balloon_02](ui/UI-041_balloon_02.md) | UI | 聊天/NPC 對話泡泡（041_balloon_02） | P2 | — |
| [UI-041_balloon_03](ui/UI-041_balloon_03.md) | UI | 聊天/NPC 對話泡泡（041_balloon_03） | P2 | — |
| [UI-041_balloon_04](ui/UI-041_balloon_04.md) | UI | 聊天/NPC 對話泡泡（041_balloon_04） | P2 | — |
| [UI-041_balloon_05](ui/UI-041_balloon_05.md) | UI | 聊天/NPC 對話泡泡（041_balloon_05） | P2 | — |
| [UI-041_balloon_06](ui/UI-041_balloon_06.md) | UI | 聊天/NPC 對話泡泡（041_balloon_06） | P2 | — |
| [UI-041_balloon_07](ui/UI-041_balloon_07.md) | UI | 聊天/NPC 對話泡泡（041_balloon_07） | P2 | — |
| [UI-041_balloon_s01](ui/UI-041_balloon_s01.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s01） | P2 | — |
| [UI-041_balloon_s02](ui/UI-041_balloon_s02.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s02） | P2 | — |
| [UI-041_balloon_s03](ui/UI-041_balloon_s03.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s03） | P2 | — |
| [UI-041_balloon_s04](ui/UI-041_balloon_s04.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s04） | P2 | — |
| [UI-041_balloon_s05](ui/UI-041_balloon_s05.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s05） | P2 | — |
| [UI-041_balloon_s06](ui/UI-041_balloon_s06.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s06） | P2 | — |
| [UI-041_balloon_s07](ui/UI-041_balloon_s07.md) | UI | 聊天/NPC 對話泡泡（041_balloon_s07） | P2 | — |
| [UI-042_FRIENDlIST_B](ui/UI-042_FRIENDlIST_B.md) | UI | 好友大型清單 | P2 | — |
| [UI-043_FRIEND_MSG](ui/UI-043_FRIEND_MSG.md) | UI | 好友訊息視窗 | P2 | — |
| [UI-044_QUESTION_BOX](ui/UI-044_QUESTION_BOX.md) | UI | 問答/確認框（044_QUESTION_BOX） | P2 | — |
| [UI-044_QUESTION_BOXB](ui/UI-044_QUESTION_BOXB.md) | UI | 問答/確認框（044_QUESTION_BOXB） | P2 | — |
| [UI-045_Mapname_Display](ui/UI-045_Mapname_Display.md) | UI | 地圖名稱提示 | P1 | — |
| [UI-046_Rename](ui/UI-046_Rename.md) | UI | 改名對話框 | P2 | — |
| [UI-047_NPC_message](ui/UI-047_NPC_message.md) | UI | NPC 對話主框（047_NPC_message） | P1 | — |
| [UI-047_NPC_message_b](ui/UI-047_NPC_message_b.md) | UI | NPC 對話主框（047_NPC_message_b） | P1 | — |
| [UI-048_PetTrade](ui/UI-048_PetTrade.md) | UI | 寵物交易資訊 | P2 | — |
| [UI-049_NPC_message02](ui/UI-049_NPC_message02.md) | UI | NPC 對話第二樣式（049_NPC_message02） | P2 | — |
| [UI-049_NPC_message02B](ui/UI-049_NPC_message02B.md) | UI | NPC 對話第二樣式（049_NPC_message02B） | P2 | — |
| [UI-050_NPC_message03a](ui/UI-050_NPC_message03a.md) | UI | NPC 對話第三樣式 A | P2 | — |
| [UI-051_NPC_message03b](ui/UI-051_NPC_message03b.md) | UI | NPC 對話其他樣式（051_NPC_message03b） | P2 | — |
| [UI-051_NPC_message04](ui/UI-051_NPC_message04.md) | UI | NPC 對話其他樣式（051_NPC_message04） | P2 | — |
| [UI-051_NPC_message05](ui/UI-051_NPC_message05.md) | UI | NPC 對話其他樣式（051_NPC_message05） | P2 | — |
| [UI-051_NPC_message06](ui/UI-051_NPC_message06.md) | UI | NPC 對話其他樣式（051_NPC_message06） | P2 | — |
| [UI-052_INPUT_TYPE](ui/UI-052_INPUT_TYPE.md) | UI | 文字輸入類型面板（052_INPUT_TYPE） | P2 | — |
| [UI-052_INPUT_TYPE_02](ui/UI-052_INPUT_TYPE_02.md) | UI | 文字輸入類型面板（052_INPUT_TYPE_02） | P2 | — |
| [UI-053_SCROLLMESSAGE](ui/UI-053_SCROLLMESSAGE.md) | UI | 跑馬燈公告 | P2 | — |
| [UI-054_PETHP](ui/UI-054_PETHP.md) | UI | 寵物 HP 條 | P2 | — |
| [UI-055_TRADE_MONEY](ui/UI-055_TRADE_MONEY.md) | UI | 交易金錢輸入 | P2 | — |
| [UI-056_Driver_INFO](ui/UI-056_Driver_INFO.md) | UI | 機甲詳細資訊（056_Driver_INFO） | P2 | — |
| [UI-056_Driver_INFO_01](ui/UI-056_Driver_INFO_01.md) | UI | 機甲詳細資訊（056_Driver_INFO_01） | P2 | — |
| [UI-056_Driver_item](ui/UI-056_Driver_item.md) | UI | 機甲物品/裝備列表（056_Driver_item） | P2 | — |
| [UI-057_CLUB_Menu](ui/UI-057_CLUB_Menu.md) | UI | 社團主選單 | P3 | — |
| [UI-058_CLUB_Ext](ui/UI-058_CLUB_Ext.md) | UI | 社團延伸面板 | P3 | — |
| [UI-059_Driver_Repair](ui/UI-059_Driver_Repair.md) | UI | 機甲修理視窗 | P2 | — |
| [UI-060_CLUB_MemList](ui/UI-060_CLUB_MemList.md) | UI | 社團成員清單（060_CLUB_MemList） | P3 | — |
| [UI-060_CLUB_MemList02](ui/UI-060_CLUB_MemList02.md) | UI | 社團成員清單（060_CLUB_MemList02） | P3 | — |
| [UI-061_CLUB_Name](ui/UI-061_CLUB_Name.md) | UI | 社團名稱輸入 | P3 | — |
| [UI-062_CLUB_ALLYList](ui/UI-062_CLUB_ALLYList.md) | UI | 社團盟友清單 | P3 | — |
| [UI-063_CLUB_GoldList](ui/UI-063_CLUB_GoldList.md) | UI | 社團金錢紀錄 | P3 | — |
| [UI-064_CLUB_TechList](ui/UI-064_CLUB_TechList.md) | UI | 社團科技清單 | P3 | — |
| [UI-065_CLUB_BookMark](ui/UI-065_CLUB_BookMark.md) | UI | 社團書籤/標記 | P3 | — |
| [UI-066_CLUB_Intro](ui/UI-066_CLUB_Intro.md) | UI | 社團介紹 | P3 | — |
| [UI-067_CLUB_News](ui/UI-067_CLUB_News.md) | UI | 社團公告 | P3 | — |
| [UI-068_CLUB_OKBox](ui/UI-068_CLUB_OKBox.md) | UI | 社團確認框（068_CLUB_OKBox） | P3 | — |
| [UI-068_CLUB_YesNoBox](ui/UI-068_CLUB_YesNoBox.md) | UI | 社團確認框（068_CLUB_YesNoBox） | P3 | — |
| [UI-069_CLUB_INTRO_EDIT](ui/UI-069_CLUB_INTRO_EDIT.md) | UI | 社團介紹編輯 | P3 | — |
| [UI-070_CLUB_News_EDIT](ui/UI-070_CLUB_News_EDIT.md) | UI | 社團公告編輯 | P3 | — |
| [UI-071_CLUB_ALLY](ui/UI-071_CLUB_ALLY.md) | UI | 社團結盟 | P3 | — |
| [UI-072_CLUB_DonateInput](ui/UI-072_CLUB_DonateInput.md) | UI | 社團捐獻金額 | P3 | — |
| [UI-073_CLUB_ResourceInput](ui/UI-073_CLUB_ResourceInput.md) | UI | 社團資源輸入 | P3 | — |
| [UI-074_CLUB_TechDevelop](ui/UI-074_CLUB_TechDevelop.md) | UI | 社團科技開發 | P3 | — |
| [UI-075_CLUB_TechDevelop02](ui/UI-075_CLUB_TechDevelop02.md) | UI | 社團科技開發明細 | P3 | — |
| [UI-076_CLUB_TechUPGRADE](ui/UI-076_CLUB_TechUPGRADE.md) | UI | 社團科技升級 | P3 | — |
| [UI-077_CLUB_Takemoney](ui/UI-077_CLUB_Takemoney.md) | UI | 社團金庫提款 | P3 | — |
| [UI-078_CLUB_DisAlly](ui/UI-078_CLUB_DisAlly.md) | UI | 解除社團同盟 | P3 | — |
| [UI-079_MainQuest](ui/UI-079_MainQuest.md) | UI | 主線任務敘事畫面 | P2 | — |
| [UI-080_CLUB_RANK](ui/UI-080_CLUB_RANK.md) | UI | 社團排行榜 | P3 | — |
| [UI-081_LOGIN_CHANGE_PASSWORD](ui/UI-081_LOGIN_CHANGE_PASSWORD.md) | UI | 變更角色第二密碼 | P1 | — |
| [UI-082_LOGIN_PASSWORD](ui/UI-082_LOGIN_PASSWORD.md) | UI | 角色第二密碼驗證 | P1 | — |
| [UI-083_PET_CARD](ui/UI-083_PET_CARD.md) | UI | 機甲/寵物卡片預覽 | P2 | — |
| [UI-084_ENEMY_HP](ui/UI-084_ENEMY_HP.md) | UI | 怪物目標 HP 視窗 | P1 | — |
| [UI-085_COMPLEX_INFO](ui/UI-085_COMPLEX_INFO.md) | UI | 合體機甲資訊 | P2 | — |
| [UI-086_COMPLEX_INFO_01](ui/UI-086_COMPLEX_INFO_01.md) | UI | 合體機甲資訊變體 | P3 | — |
| [UI-087_DRIVE_COMPLEX_BAR](ui/UI-087_DRIVE_COMPLEX_BAR.md) | UI | 駕駛/合體能源條 | P2 | — |
| [UI-088_COMPLEX_HP](ui/UI-088_COMPLEX_HP.md) | UI | 合體 HP/MP/EXP 條 | P2 | — |
| [UI-089_PCMEMO_TRADE](ui/UI-089_PCMEMO_TRADE.md) | UI | 玩家備忘錄交易文字（089_PCMEMO_TRADE） | P3 | — |
| [UI-089_PCMEMO_TRADE_READ](ui/UI-089_PCMEMO_TRADE_READ.md) | UI | 玩家備忘錄交易文字（089_PCMEMO_TRADE_READ） | P3 | — |
| [UI-090_PCMEMO](ui/UI-090_PCMEMO.md) | UI | 玩家備忘錄/個人狀態 | P3 | — |
| [UI-091_PVP_MAIN](ui/UI-091_PVP_MAIN.md) | UI | PvP 主戰鬥 HUD | P2 | — |
| [UI-092_PShop](ui/UI-092_PShop.md) | UI | 玩家商店主畫面 | P2 | — |
| [UI-092_PShop_Buy](ui/UI-092_PShop_Buy.md) | UI | 玩家商店買家清單 | P2 | — |
| [UI-092_PShop_Buy_Item_01](ui/UI-092_PShop_Buy_Item_01.md) | UI | 玩家商店購買商品確認 01 | P2 | — |
| [UI-092_PShop_Buy_Item_02](ui/UI-092_PShop_Buy_Item_02.md) | UI | 玩家商店購買商品確認 02 | P2 | — |
| [UI-092_PShop_head](ui/UI-092_PShop_head.md) | UI | 擺攤招牌 | P2 | — |
| [UI-092_PShop_Name](ui/UI-092_PShop_Name.md) | UI | 擺攤命名 | P2 | — |
| [UI-092_PShop_Sell](ui/UI-092_PShop_Sell.md) | UI | 玩家商店賣家清單 | P2 | — |
| [UI-092_PShop_Sell_Item_01](ui/UI-092_PShop_Sell_Item_01.md) | UI | 擺攤商品設定 01 | P2 | — |
| [UI-092_PShop_Sell_Item_02](ui/UI-092_PShop_Sell_Item_02.md) | UI | 擺攤商品設定 02 | P2 | — |
| [UI-093_Pvp_List_01](ui/UI-093_Pvp_List_01.md) | UI | PvP 列表 093_Pvp_List_01 | P3 | — |
| [UI-093_Pvp_List_02](ui/UI-093_Pvp_List_02.md) | UI | PvP 列表 093_Pvp_List_02 | P3 | — |
| [UI-093_Pvp_List_Item](ui/UI-093_Pvp_List_Item.md) | UI | PvP 列表 093_Pvp_List_Item | P3 | — |
| [UI-093_Pvp_Rank_01](ui/UI-093_Pvp_Rank_01.md) | UI | PvP 排行 093_Pvp_Rank_01 | P3 | — |
| [UI-093_Pvp_Rank_02](ui/UI-093_Pvp_Rank_02.md) | UI | PvP 排行 093_Pvp_Rank_02 | P3 | — |
| [UI-094_CLUB](ui/UI-094_CLUB.md) | UI | 社團總覽 | P2 | — |
| [UI-094_Hair_Shop](ui/UI-094_Hair_Shop.md) | UI | 髮型商店 | P2 | — |
| [UI-095_Collection](ui/UI-095_Collection.md) | UI | 收藏圖鑑 | P3 | — |
| [UI-095_Collection_01](ui/UI-095_Collection_01.md) | UI | 收藏圖鑑子頁 01 | P3 | — |
| [UI-095_Collection_02](ui/UI-095_Collection_02.md) | UI | 收藏圖鑑子頁 02 | P3 | — |
| [UI-095_Collection_03](ui/UI-095_Collection_03.md) | UI | 收藏圖鑑子頁 03 | P3 | — |
| [UI-095_Collection_04](ui/UI-095_Collection_04.md) | UI | 收藏圖鑑子頁 04 | P3 | — |
| [UI-095_Collection_05](ui/UI-095_Collection_05.md) | UI | 收藏圖鑑子頁 05 | P3 | — |
| [UI-095_Collection_06](ui/UI-095_Collection_06.md) | UI | 收藏圖鑑子頁 06 | P3 | — |
| [UI-096_Dissolve](ui/UI-096_Dissolve.md) | UI | 物品分解 | P2 | — |
| [UI-097_Quest_List](ui/UI-097_Quest_List.md) | UI | 任務清單 | P1 | — |
| [UI-098_Refund](ui/UI-098_Refund.md) | UI | 回收/退款視窗 | P3 | — |
| [GP-001-01](gameplay/GP-001-01.md) | Gameplay leaf | 登入成功 | P1 | GP-001 |
| [GP-001-02](gameplay/GP-001-02.md) | Gameplay leaf | 登入拒絕 | P1 | GP-001 |
| [GP-001](gameplay/GP-001.md) | Gameplay integration | 登入與帳號 session | P1 | — |
| [GP-002-01](gameplay/GP-002-01.md) | Gameplay leaf | 角色列表 | P1 | GP-002 |
| [GP-002-02](gameplay/GP-002-02.md) | Gameplay leaf | 進入世界 | P1 | GP-002 |
| [GP-002](gameplay/GP-002.md) | Gameplay integration | 角色列表與進入世界 | P1 | — |
| [GP-003-01](gameplay/GP-003-01.md) | Gameplay leaf | 角色外觀草稿 | P1 | GP-003 |
| [GP-003-02](gameplay/GP-003-02.md) | Gameplay leaf | 建立提交 | P1 | GP-003 |
| [GP-003](gameplay/GP-003.md) | Gameplay integration | 角色建立 | P1 | — |
| [GP-004-01](gameplay/GP-004-01.md) | Gameplay leaf | 刪除角色 | P2 | GP-004 |
| [GP-004-02](gameplay/GP-004-02.md) | Gameplay leaf | 第二密碼驗證 | P2 | GP-004 |
| [GP-004-03](gameplay/GP-004-03.md) | Gameplay leaf | 變更第二密碼 | P2 | GP-004 |
| [GP-004](gameplay/GP-004.md) | Gameplay integration | 角色刪除與第二密碼 | P2 | — |
| [GP-005-01](gameplay/GP-005-01.md) | Gameplay leaf | 初始外觀 | P1 | GP-005 |
| [GP-005-02](gameplay/GP-005-02.md) | Gameplay leaf | 裝備外觀 | P1 | GP-005 |
| [GP-005](gameplay/GP-005.md) | Gameplay integration | 人物初始外觀與 paper doll | P1 | — |
| [GP-006-01](gameplay/GP-006-01.md) | Gameplay leaf | 移動 | P1 | GP-006 |
| [GP-006-02](gameplay/GP-006-02.md) | Gameplay leaf | 停止改點 | P1 | GP-006 |
| [GP-006](gameplay/GP-006.md) | Gameplay integration | 點地面移動與停止 | P1 | — |
| [GP-007-01](gameplay/GP-007-01.md) | Gameplay leaf | 合法尋路 | P1 | GP-007 |
| [GP-007-02](gameplay/GP-007-02.md) | Gameplay leaf | 不可走目標 | P1 | GP-007 |
| [GP-007](gameplay/GP-007.md) | Gameplay integration | 地圖碰撞與尋路 | P1 | — |
| [GP-008-01](gameplay/GP-008-01.md) | Gameplay leaf | 跨圖 | P1 | GP-008 |
| [GP-008-02](gameplay/GP-008-02.md) | Gameplay leaf | 返程 | P1 | GP-008 |
| [GP-008](gameplay/GP-008.md) | Gameplay integration | 傳點與跨圖 lifecycle | P1 | — |
| [GP-009-01](gameplay/GP-009-01.md) | Gameplay leaf | AOI 進入 | P1 | GP-009 |
| [GP-009-02](gameplay/GP-009-02.md) | Gameplay leaf | AOI 離開重入 | P1 | GP-009 |
| [GP-009](gameplay/GP-009.md) | Gameplay integration | 玩家 AOI 進出與同步 | P1 | — |
| [GP-010-01](gameplay/GP-010-01.md) | Gameplay leaf | 怪物生成 | P1 | GP-010 |
| [GP-010-02](gameplay/GP-010-02.md) | Gameplay leaf | 怪物重生 | P1 | GP-010 |
| [GP-010](gameplay/GP-010.md) | Gameplay integration | 怪物生成與重生 | P1 | — |
| [GP-011-01](gameplay/GP-011-01.md) | Gameplay leaf | 巡邏 | P1 | GP-011 |
| [GP-011-02](gameplay/GP-011-02.md) | Gameplay leaf | 追擊與脫戰 | P1 | GP-011 |
| [GP-011](gameplay/GP-011.md) | Gameplay integration | 怪物巡邏與追擊/回位 | P1 | — |
| [GP-012-01](gameplay/GP-012-01.md) | Gameplay leaf | 正式新角戰鬥 | P1 | GP-012 |
| [GP-012-02](gameplay/GP-012-02.md) | Gameplay leaf | 未知 profile | P1 | GP-012 |
| [GP-012](gameplay/GP-012.md) | Gameplay integration | 正式角色戰鬥屬性解鎖 | P1 | — |
| [GP-013-01](gameplay/GP-013-01.md) | Gameplay leaf | 選怪與取消 | P1 | GP-013 |
| [GP-013-02](gameplay/GP-013-02.md) | Gameplay leaf | 近遠攻距 | P1 | GP-013 |
| [GP-013](gameplay/GP-013.md) | Gameplay integration | 選怪與接近攻擊距離 | P1 | — |
| [GP-014-01](gameplay/GP-014-01.md) | Gameplay leaf | 普通攻擊 | P1 | GP-014 |
| [GP-014-02](gameplay/GP-014-02.md) | Gameplay leaf | MISS | P1 | GP-014 |
| [GP-014-03](gameplay/GP-014-03.md) | Gameplay leaf | 非法攻擊 | P1 | GP-014 |
| [GP-014](gameplay/GP-014.md) | Gameplay integration | 普通攻擊與命中/MISS | P1 | — |
| [GP-015-01](gameplay/GP-015-01.md) | Gameplay leaf | 傷害結算 | P1 | GP-015 |
| [GP-015-02](gameplay/GP-015-02.md) | Gameplay leaf | 受擊動畫 | P1 | GP-015 |
| [GP-015](gameplay/GP-015.md) | Gameplay integration | 傷害結算、受擊與動畫 | P1 | — |
| [GP-016-01](gameplay/GP-016-01.md) | Gameplay leaf | 擊殺 EXP | P1 | GP-016 |
| [GP-016-02](gameplay/GP-016-02.md) | Gameplay leaf | 升級 | P1 | GP-016 |
| [GP-016-03](gameplay/GP-016-03.md) | Gameplay leaf | 金錢 | P1 | GP-016 |
| [GP-016](gameplay/GP-016.md) | Gameplay integration | 擊殺、EXP、金錢與升級 | P1 | — |
| [GP-017-01](gameplay/GP-017-01.md) | Gameplay leaf | 掉落生成 | P1 | GP-017 |
| [GP-017-02](gameplay/GP-017-02.md) | Gameplay leaf | 掉落生命周期 | P1 | GP-017 |
| [GP-017](gameplay/GP-017.md) | Gameplay integration | 怪物死亡與掉落生成 | P1 | — |
| [GP-018-01](gameplay/GP-018-01.md) | Gameplay leaf | 撿取成功 | P1 | GP-018 |
| [GP-018-02](gameplay/GP-018-02.md) | Gameplay leaf | 競拾/滿包 | P1 | GP-018 |
| [GP-018](gameplay/GP-018.md) | Gameplay integration | 地上物拾取 | P1 | — |
| [GP-019-01](gameplay/GP-019-01.md) | Gameplay leaf | 死亡 | P1 | GP-019 |
| [GP-019-02](gameplay/GP-019-02.md) | Gameplay leaf | 復活 | P1 | GP-019 |
| [GP-019](gameplay/GP-019.md) | Gameplay integration | 玩家死亡與復活 | P1 | — |
| [GP-020-01](gameplay/GP-020-01.md) | Gameplay leaf | 技能學習 | P1 | GP-020 |
| [GP-020-02](gameplay/GP-020-02.md) | Gameplay leaf | 技能升級 | P1 | GP-020 |
| [GP-020](gameplay/GP-020.md) | Gameplay integration | 技能學習與職業限制 | P1 | — |
| [GP-021-01](gameplay/GP-021-01.md) | Gameplay leaf | 單體施法 | P1 | GP-021 |
| [GP-021-02](gameplay/GP-021-02.md) | Gameplay leaf | 施法拒絕 | P1 | GP-021 |
| [GP-021](gameplay/GP-021.md) | Gameplay integration | 單體技能與 MP/冷卻 | P1 | — |
| [GP-022-01](gameplay/GP-022-01.md) | Gameplay leaf | 範圍選取 | P1 | GP-022 |
| [GP-022-02](gameplay/GP-022-02.md) | Gameplay leaf | 範圍結算 | P1 | GP-022 |
| [GP-022](gameplay/GP-022.md) | Gameplay integration | 範圍技能與多目標 | P1 | — |
| [GP-023-01](gameplay/GP-023-01.md) | Gameplay leaf | Buff | P1 | GP-023 |
| [GP-023-02](gameplay/GP-023-02.md) | Gameplay leaf | Debuff | P1 | GP-023 |
| [GP-023-03](gameplay/GP-023-03.md) | Gameplay leaf | 治療 | P1 | GP-023 |
| [GP-023](gameplay/GP-023.md) | Gameplay integration | Buff/Debuff 與治療 | P1 | — |
| [GP-024-01](gameplay/GP-024-01.md) | Gameplay leaf | 背包格位 | P1 | GP-024 |
| [GP-024-02](gameplay/GP-024-02.md) | Gameplay leaf | 堆疊與分堆 | P1 | GP-024 |
| [GP-024](gameplay/GP-024.md) | Gameplay integration | 背包格位與堆疊 | P1 | — |
| [GP-025-01](gameplay/GP-025-01.md) | Gameplay leaf | 使用藥水 | P1 | GP-025 |
| [GP-025-02](gameplay/GP-025-02.md) | Gameplay leaf | 消耗品拒絕 | P1 | GP-025 |
| [GP-025](gameplay/GP-025.md) | Gameplay integration | 消耗品使用與恢復 | P1 | — |
| [GP-026-01](gameplay/GP-026-01.md) | Gameplay leaf | 裝備穿戴 | P1 | GP-026 |
| [GP-026-02](gameplay/GP-026-02.md) | Gameplay leaf | 卸下與替換 | P1 | GP-026 |
| [GP-026-03](gameplay/GP-026-03.md) | Gameplay leaf | 裝備限制 | P1 | GP-026 |
| [GP-026](gameplay/GP-026.md) | Gameplay integration | 裝備裝卸與屬性 | P1 | — |
| [GP-027-01](gameplay/GP-027-01.md) | Gameplay leaf | 屬性配點 | P1 | GP-027 |
| [GP-027-02](gameplay/GP-027-02.md) | Gameplay leaf | 非法配點 | P1 | GP-027 |
| [GP-027](gameplay/GP-027.md) | Gameplay integration | 屬性配點與重算 | P1 | — |
| [GP-028-01](gameplay/GP-028-01.md) | Gameplay leaf | 快捷鍵配置 | P1 | GP-028 |
| [GP-028-02](gameplay/GP-028-02.md) | Gameplay leaf | 快捷鍵使用 | P1 | GP-028 |
| [GP-028](gameplay/GP-028.md) | Gameplay integration | 快捷鍵綁定及執行 | P1 | — |
| [GP-029-01](gameplay/GP-029-01.md) | Gameplay leaf | NPC 開啟 | P1 | GP-029 |
| [GP-029-02](gameplay/GP-029-02.md) | Gameplay leaf | NPC 分支 | P1 | GP-029 |
| [GP-029](gameplay/GP-029.md) | Gameplay integration | NPC 互動與腳本分支 | P1 | — |
| [GP-030-01](gameplay/GP-030-01.md) | Gameplay leaf | 購買 | P1 | GP-030 |
| [GP-030-02](gameplay/GP-030-02.md) | Gameplay leaf | 購買拒絕 | P1 | GP-030 |
| [GP-030](gameplay/GP-030.md) | Gameplay integration | NPC 商店購買 | P1 | — |
| [GP-031-01](gameplay/GP-031-01.md) | Gameplay leaf | 出售 | P1 | GP-031 |
| [GP-031-02](gameplay/GP-031-02.md) | Gameplay leaf | 出售拒絕 | P1 | GP-031 |
| [GP-031](gameplay/GP-031.md) | Gameplay integration | NPC 商店出售 | P1 | — |
| [GP-032-01](gameplay/GP-032-01.md) | Gameplay leaf | 存款 | P2 | GP-032 |
| [GP-032-02](gameplay/GP-032-02.md) | Gameplay leaf | 提款 | P2 | GP-032 |
| [GP-032](gameplay/GP-032.md) | Gameplay integration | 銀行存取金錢 | P2 | — |
| [GP-033-01](gameplay/GP-033-01.md) | Gameplay leaf | 倉庫存物 | P2 | GP-033 |
| [GP-033-02](gameplay/GP-033-02.md) | Gameplay leaf | 倉庫取物 | P2 | GP-033 |
| [GP-033](gameplay/GP-033.md) | Gameplay integration | 倉庫存取物品 | P2 | — |
| [GP-034-01](gameplay/GP-034-01.md) | Gameplay leaf | 耐久消耗 | P2 | GP-034 |
| [GP-034-02](gameplay/GP-034-02.md) | Gameplay leaf | 修理 | P2 | GP-034 |
| [GP-034](gameplay/GP-034.md) | Gameplay integration | 裝備耐久與修理 | P2 | — |
| [GP-035-01](gameplay/GP-035-01.md) | Gameplay leaf | 強化成功 | P2 | GP-035 |
| [GP-035-02](gameplay/GP-035-02.md) | Gameplay leaf | 強化失敗 | P2 | GP-035 |
| [GP-035](gameplay/GP-035.md) | Gameplay integration | 裝備強化 | P2 | — |
| [GP-036-01](gameplay/GP-036-01.md) | Gameplay leaf | 物品合成 | P2 | GP-036 |
| [GP-036-02](gameplay/GP-036-02.md) | Gameplay leaf | 合成拒絕 | P2 | GP-036 |
| [GP-036](gameplay/GP-036.md) | Gameplay integration | 物品合成 | P2 | — |
| [GP-037-01](gameplay/GP-037-01.md) | Gameplay leaf | 物品分解 | P2 | GP-037 |
| [GP-037-02](gameplay/GP-037-02.md) | Gameplay leaf | 分解拒絕 | P2 | GP-037 |
| [GP-037](gameplay/GP-037.md) | Gameplay integration | 物品分解 | P2 | — |
| [GP-038-01](gameplay/GP-038-01.md) | Gameplay leaf | 交易邀請 | P2 | GP-038 |
| [GP-038-02](gameplay/GP-038-02.md) | Gameplay leaf | 交易內容 | P2 | GP-038 |
| [GP-038-03](gameplay/GP-038-03.md) | Gameplay leaf | 交易確認 | P2 | GP-038 |
| [GP-038](gameplay/GP-038.md) | Gameplay integration | 玩家雙方交易 | P2 | — |
| [GP-039-01](gameplay/GP-039-01.md) | Gameplay leaf | 開攤命名 | P2 | GP-039 |
| [GP-039-02](gameplay/GP-039-02.md) | Gameplay leaf | 商品上架 | P2 | GP-039 |
| [GP-039-03](gameplay/GP-039-03.md) | Gameplay leaf | 關攤 | P2 | GP-039 |
| [GP-039](gameplay/GP-039.md) | Gameplay integration | 玩家商店開攤與管理 | P2 | — |
| [GP-040-01](gameplay/GP-040-01.md) | Gameplay leaf | 擺攤購買 | P2 | GP-040 |
| [GP-040-02](gameplay/GP-040-02.md) | Gameplay leaf | 多人競購 | P2 | GP-040 |
| [GP-040](gameplay/GP-040.md) | Gameplay integration | 玩家商店購買與競購 | P2 | — |
| [GP-041-01](gameplay/GP-041-01.md) | Gameplay leaf | 隊伍邀請 | P2 | GP-041 |
| [GP-041-02](gameplay/GP-041-02.md) | Gameplay leaf | 隊伍管理 | P2 | GP-041 |
| [GP-041](gameplay/GP-041.md) | Gameplay integration | 組隊邀請與隊長 | P2 | — |
| [GP-042-01](gameplay/GP-042-01.md) | Gameplay leaf | 隊友狀態 | P2 | GP-042 |
| [GP-042-02](gameplay/GP-042-02.md) | Gameplay leaf | 共享結算 | P2 | GP-042 |
| [GP-042](gameplay/GP-042.md) | Gameplay integration | 組隊戰鬥共享與狀態 | P2 | — |
| [GP-043-01](gameplay/GP-043-01.md) | Gameplay leaf | 公開聊天 | P2 | GP-043 |
| [GP-043-02](gameplay/GP-043-02.md) | Gameplay leaf | 私訊/隊伍聊天 | P2 | GP-043 |
| [GP-043](gameplay/GP-043.md) | Gameplay integration | 聊天頻道與私訊 | P2 | — |
| [GP-044-01](gameplay/GP-044-01.md) | Gameplay leaf | 好友 | P2 | GP-044 |
| [GP-044-02](gameplay/GP-044-02.md) | Gameplay leaf | 黑名單 | P2 | GP-044 |
| [GP-044](gameplay/GP-044.md) | Gameplay integration | 好友與黑名單 | P2 | — |
| [GP-045-01](gameplay/GP-045-01.md) | Gameplay leaf | 留言寄送 | P2 | GP-045 |
| [GP-045-02](gameplay/GP-045-02.md) | Gameplay leaf | 留言讀取 | P2 | GP-045 |
| [GP-045](gameplay/GP-045.md) | Gameplay integration | 郵件與留言 | P2 | — |
| [GP-046-01](gameplay/GP-046-01.md) | Gameplay leaf | 表情 | P2 | GP-046 |
| [GP-046-02](gameplay/GP-046-02.md) | Gameplay leaf | 泡泡樣式 | P2 | GP-046 |
| [GP-046](gameplay/GP-046.md) | Gameplay integration | 表情與泡泡樣式 | P2 | — |
| [GP-047](gameplay/GP-047.md) | Gameplay leaf | 稱號變更 | P2 | — |
| [GP-048-01](gameplay/GP-048-01.md) | Gameplay leaf | 轉職 | P2 | GP-048 |
| [GP-048-02](gameplay/GP-048-02.md) | Gameplay leaf | 不符前置 | P2 | GP-048 |
| [GP-048](gameplay/GP-048.md) | Gameplay integration | 轉職與職業條件 | P2 | — |
| [GP-049-01](gameplay/GP-049-01.md) | Gameplay leaf | 任務接取 | P2 | GP-049 |
| [GP-049-02](gameplay/GP-049-02.md) | Gameplay leaf | 進度 | P2 | GP-049 |
| [GP-049](gameplay/GP-049.md) | Gameplay integration | 任務接取與進度 | P2 | — |
| [GP-050-01](gameplay/GP-050-01.md) | Gameplay leaf | 任務完成 | P2 | GP-050 |
| [GP-050-02](gameplay/GP-050-02.md) | Gameplay leaf | 獎勵拒絕 | P2 | GP-050 |
| [GP-050](gameplay/GP-050.md) | Gameplay integration | 任務完成與獎勵 | P2 | — |
| [GP-051-01](gameplay/GP-051-01.md) | Gameplay leaf | 寵物召喚 | P2 | GP-051 |
| [GP-051-02](gameplay/GP-051-02.md) | Gameplay leaf | 寵物收回 | P2 | GP-051 |
| [GP-051](gameplay/GP-051.md) | Gameplay integration | 寵物召喚與收回 | P2 | — |
| [GP-052-01](gameplay/GP-052-01.md) | Gameplay leaf | 寵物跟隨 | P2 | GP-052 |
| [GP-052-02](gameplay/GP-052-02.md) | Gameplay leaf | 寵物攻擊 | P2 | GP-052 |
| [GP-052](gameplay/GP-052.md) | Gameplay integration | 寵物 AI 與戰鬥 | P2 | — |
| [GP-053-01](gameplay/GP-053-01.md) | Gameplay leaf | 餵食 | P2 | GP-053 |
| [GP-053-02](gameplay/GP-053-02.md) | Gameplay leaf | 寵物配點 | P2 | GP-053 |
| [GP-053-03](gameplay/GP-053-03.md) | Gameplay leaf | 寵物裝卸 | P2 | GP-053 |
| [GP-053](gameplay/GP-053.md) | Gameplay integration | 寵物餵食、配點與裝備 | P2 | — |
| [GP-054](gameplay/GP-054.md) | Gameplay leaf | 寵物交易 | P2 | — |
| [GP-055-01](gameplay/GP-055-01.md) | Gameplay leaf | 機甲搭乘 | P2 | GP-055 |
| [GP-055-02](gameplay/GP-055-02.md) | Gameplay leaf | 機甲解除 | P2 | GP-055 |
| [GP-055](gameplay/GP-055.md) | Gameplay integration | 機甲搭乘與解除 | P2 | — |
| [GP-056-01](gameplay/GP-056-01.md) | Gameplay leaf | 機甲裝卸 | P2 | GP-056 |
| [GP-056-02](gameplay/GP-056-02.md) | Gameplay leaf | 能源消耗 | P2 | GP-056 |
| [GP-056-03](gameplay/GP-056-03.md) | Gameplay leaf | 能源補充 | P2 | GP-056 |
| [GP-056](gameplay/GP-056.md) | Gameplay integration | 機甲裝備與能源 | P2 | — |
| [GP-057-01](gameplay/GP-057-01.md) | Gameplay leaf | 機甲修理 | P2 | GP-057 |
| [GP-057-02](gameplay/GP-057-02.md) | Gameplay leaf | 機甲升級 | P2 | GP-057 |
| [GP-057](gameplay/GP-057.md) | Gameplay integration | 機甲修理與升級 | P2 | — |
| [GP-058-01](gameplay/GP-058-01.md) | Gameplay leaf | 機甲卡片 | P2 | GP-058 |
| [GP-058-02](gameplay/GP-058-02.md) | Gameplay leaf | 合體 | P2 | GP-058 |
| [GP-058-03](gameplay/GP-058-03.md) | Gameplay leaf | 解除合體 | P2 | GP-058 |
| [GP-058](gameplay/GP-058.md) | Gameplay integration | 機甲卡片與合體 | P2 | — |
| [GP-059-01](gameplay/GP-059-01.md) | Gameplay leaf | PvP 地圖 | P2 | GP-059 |
| [GP-059-02](gameplay/GP-059-02.md) | Gameplay leaf | PK 名稱 | P2 | GP-059 |
| [GP-059](gameplay/GP-059.md) | Gameplay integration | PvP 地圖與名稱狀態 | P2 | — |
| [GP-060-01](gameplay/GP-060-01.md) | Gameplay leaf | PvP 對戰入口 | P3 | GP-060 |
| [GP-060-02](gameplay/GP-060-02.md) | Gameplay leaf | PvP 結算排行 | P3 | GP-060 |
| [GP-060](gameplay/GP-060.md) | Gameplay integration | PvP 對戰與排行 | P3 | — |
| [GP-061](gameplay/GP-061.md) | Gameplay leaf | 排行榜 | P3 | — |
| [GP-062-01](gameplay/GP-062-01.md) | Gameplay leaf | 社團建立 | P3 | GP-062 |
| [GP-062-02](gameplay/GP-062-02.md) | Gameplay leaf | 成員管理 | P3 | GP-062 |
| [GP-062](gameplay/GP-062.md) | Gameplay integration | 社團建立與成員 | P3 | — |
| [GP-063-01](gameplay/GP-063-01.md) | Gameplay leaf | 結盟 | P3 | GP-063 |
| [GP-063-02](gameplay/GP-063-02.md) | Gameplay leaf | 社團公告 | P3 | GP-063 |
| [GP-063](gameplay/GP-063.md) | Gameplay integration | 社團同盟與公告 | P3 | — |
| [GP-064-01](gameplay/GP-064-01.md) | Gameplay leaf | 社團金庫捐獻 | P3 | GP-064 |
| [GP-064-02](gameplay/GP-064-02.md) | Gameplay leaf | 社團金庫提款 | P3 | GP-064 |
| [GP-064-03](gameplay/GP-064-03.md) | Gameplay leaf | 社團資源 | P3 | GP-064 |
| [GP-064-04](gameplay/GP-064-04.md) | Gameplay leaf | 社團科技開發 | P3 | GP-064 |
| [GP-064-05](gameplay/GP-064-05.md) | Gameplay leaf | 社團科技升級 | P3 | GP-064 |
| [GP-064](gameplay/GP-064.md) | Gameplay integration | 社團資源、金庫與科技 | P3 | — |
| [GP-065-01](gameplay/GP-065-01.md) | Gameplay leaf | 髮型預覽 | P3 | GP-065 |
| [GP-065-02](gameplay/GP-065-02.md) | Gameplay leaf | 髮型購買 | P3 | GP-065 |
| [GP-065](gameplay/GP-065.md) | Gameplay integration | 髮型商店 | P3 | — |
| [GP-066-01](gameplay/GP-066-01.md) | Gameplay leaf | 收藏 | P3 | GP-066 |
| [GP-066-02](gameplay/GP-066-02.md) | Gameplay leaf | 收藏獎勵 | P3 | GP-066 |
| [GP-066](gameplay/GP-066.md) | Gameplay integration | 收藏與獎勵 | P3 | — |
| [GP-067-01](gameplay/GP-067-01.md) | Gameplay leaf | Refund 原版語意 | P3 | GP-067 |
| [GP-067-02](gameplay/GP-067-02.md) | Gameplay leaf | Refund 交易 | P3 | GP-067 |
| [GP-067](gameplay/GP-067.md) | Gameplay integration | 退款/回購功能證據與實作 | P3 | — |
| [GP-068-01](gameplay/GP-068-01.md) | Gameplay leaf | 系統設定 | P3 | GP-068 |
| [GP-068-02](gameplay/GP-068-02.md) | Gameplay leaf | 登出 | P3 | GP-068 |
| [GP-068](gameplay/GP-068.md) | Gameplay integration | 系統設定與登出 | P3 | — |
| [GP-069](gameplay/GP-069.md) | Gameplay leaf | 全域公告與跑馬燈 | P3 | — |
| [GP-070](gameplay/GP-070.md) | Gameplay leaf | 禮物/邀請獎勵 | P3 | — |
| [GP-071](gameplay/GP-071.md) | Gameplay leaf | GM 管理功能 | P3 | — |
| [GP-072](gameplay/GP-072.md) | Gameplay leaf | 原版游標與地面選取 | P1 | — |
| [GP-073-01](gameplay/GP-073-01.md) | Gameplay leaf | 傷害數字 | P1 | GP-073 |
| [GP-073-02](gameplay/GP-073-02.md) | Gameplay leaf | MISS/Guard | P1 | GP-073 |
| [GP-073](gameplay/GP-073.md) | Gameplay integration | 戰鬥數字與 MISS/防禦特效 | P1 | — |
| [GP-074-01](gameplay/GP-074-01.md) | Gameplay leaf | 武器/命中特效 | P1 | GP-074 |
| [GP-074-02](gameplay/GP-074-02.md) | Gameplay leaf | 技能特效 | P1 | GP-074 |
| [GP-074](gameplay/GP-074.md) | Gameplay integration | 技能與武器特效 | P1 | — |
| [GP-075-01](gameplay/GP-075-01.md) | Gameplay leaf | 地圖介面 | P2 | GP-075 |
| [GP-075-02](gameplay/GP-075-02.md) | Gameplay leaf | 傳送地圖 | P2 | GP-075 |
| [GP-075](gameplay/GP-075.md) | Gameplay integration | 世界地圖與傳送地圖介面 | P2 | — |
| [GP-076-01](gameplay/GP-076-01.md) | Gameplay leaf | 共用圖像 | P1 | GP-076 |
| [GP-076-02](gameplay/GP-076-02.md) | Gameplay leaf | 共用文字 | P1 | GP-076 |
| [GP-076](gameplay/GP-076.md) | Gameplay integration | 原版 UI 素材共用載入與字型 | P1 | — |
| [GP-077](gameplay/GP-077.md) | Gameplay leaf | 五個 C.D.E. ITF 解密與控件驗證 | P3 | — |
| [GP-078-01](gameplay/GP-078-01.md) | Gameplay leaf | 未直接引用 ITF | P3 | GP-078 |
| [GP-078-02](gameplay/GP-078-02.md) | Gameplay leaf | 未配對 SPR | P3 | GP-078 |
| [GP-078](gameplay/GP-078.md) | Gameplay integration | 未直接引用 ITF 與剩餘 SPR 可達性調查 | P3 | — |
| [GP-079-01](gameplay/GP-079-01.md) | Gameplay leaf | 換圖與登出資源 | P2 | GP-079 |
| [GP-079-02](gameplay/GP-079-02.md) | Gameplay leaf | AFK/Unfocus | P2 | GP-079 |
| [GP-079](gameplay/GP-079.md) | Gameplay integration | 遊戲內資源生命週期與效能 | P2 | — |
| [ASSET-ADV_00](assets/ASSET-ADV_00.md) | Asset evidence | ADV_00.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_BASIC_MAIL](assets/ASSET-B_BASIC_MAIL.md) | Asset evidence | B_BASIC_MAIL.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_BASIC_PCMEMO](assets/ASSET-B_BASIC_PCMEMO.md) | Asset evidence | B_BASIC_PCMEMO.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_BASIC_STATUS](assets/ASSET-B_BASIC_STATUS.md) | Asset evidence | B_BASIC_STATUS.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_Command_ALLY](assets/ASSET-B_Command_ALLY.md) | Asset evidence | B_Command_ALLY.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_EMOTION](assets/ASSET-B_EMOTION.md) | Asset evidence | B_EMOTION.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_LIST_IGNORE](assets/ASSET-B_LIST_IGNORE.md) | Asset evidence | B_LIST_IGNORE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_LIST_INGORE](assets/ASSET-B_LIST_INGORE.md) | Asset evidence | B_LIST_INGORE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_LIST_INGORELIST](assets/ASSET-B_LIST_INGORELIST.md) | Asset evidence | B_LIST_INGORELIST.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_LIST_MAIL](assets/ASSET-B_LIST_MAIL.md) | Asset evidence | B_LIST_MAIL.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_FRIEND_F](assets/ASSET-B_PCMEMO_FRIEND_F.md) | Asset evidence | B_PCMEMO_FRIEND_F.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_FRIEND_FF](assets/ASSET-B_PCMEMO_FRIEND_FF.md) | Asset evidence | B_PCMEMO_FRIEND_FF.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_FRIEND_FM](assets/ASSET-B_PCMEMO_FRIEND_FM.md) | Asset evidence | B_PCMEMO_FRIEND_FM.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_FRIEND_FRI](assets/ASSET-B_PCMEMO_FRIEND_FRI.md) | Asset evidence | B_PCMEMO_FRIEND_FRI.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_FRIEND_M](assets/ASSET-B_PCMEMO_FRIEND_M.md) | Asset evidence | B_PCMEMO_FRIEND_M.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_AWAY](assets/ASSET-B_PCMEMO_PC_AWAY.md) | Asset evidence | B_PCMEMO_PC_AWAY.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK1](assets/ASSET-B_PCMEMO_PC_LV_RANK1.md) | Asset evidence | B_PCMEMO_PC_LV_RANK1.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK2](assets/ASSET-B_PCMEMO_PC_LV_RANK2.md) | Asset evidence | B_PCMEMO_PC_LV_RANK2.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK3](assets/ASSET-B_PCMEMO_PC_LV_RANK3.md) | Asset evidence | B_PCMEMO_PC_LV_RANK3.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK4](assets/ASSET-B_PCMEMO_PC_LV_RANK4.md) | Asset evidence | B_PCMEMO_PC_LV_RANK4.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK5](assets/ASSET-B_PCMEMO_PC_LV_RANK5.md) | Asset evidence | B_PCMEMO_PC_LV_RANK5.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK6](assets/ASSET-B_PCMEMO_PC_LV_RANK6.md) | Asset evidence | B_PCMEMO_PC_LV_RANK6.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_LV_RANK7](assets/ASSET-B_PCMEMO_PC_LV_RANK7.md) | Asset evidence | B_PCMEMO_PC_LV_RANK7.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_PC_NA](assets/ASSET-B_PCMEMO_PC_NA.md) | Asset evidence | B_PCMEMO_PC_NA.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_SELL_B](assets/ASSET-B_PCMEMO_SELL_B.md) | Asset evidence | B_PCMEMO_SELL_B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_SELL_S](assets/ASSET-B_PCMEMO_SELL_S.md) | Asset evidence | B_PCMEMO_SELL_S.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_SELL_X](assets/ASSET-B_PCMEMO_SELL_X.md) | Asset evidence | B_PCMEMO_SELL_X.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_TEAM_ADD](assets/ASSET-B_PCMEMO_TEAM_ADD.md) | Asset evidence | B_PCMEMO_TEAM_ADD.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_TEAM_L](assets/ASSET-B_PCMEMO_TEAM_L.md) | Asset evidence | B_PCMEMO_TEAM_L.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PCMEMO_TEAM_X](assets/ASSET-B_PCMEMO_TEAM_X.md) | Asset evidence | B_PCMEMO_TEAM_X.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PSHOP_SELL](assets/ASSET-B_PSHOP_SELL.md) | Asset evidence | B_PSHOP_SELL.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_PVP_KING](assets/ASSET-B_PVP_KING.md) | Asset evidence | B_PVP_KING.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_RADIO_BS](assets/ASSET-B_RADIO_BS.md) | Asset evidence | B_RADIO_BS.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_FRIEND_F](assets/ASSET-B_STATUS_FRIEND_F.md) | Asset evidence | B_STATUS_FRIEND_F.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_FRIEND_FF](assets/ASSET-B_STATUS_FRIEND_FF.md) | Asset evidence | B_STATUS_FRIEND_FF.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_FRIEND_FM](assets/ASSET-B_STATUS_FRIEND_FM.md) | Asset evidence | B_STATUS_FRIEND_FM.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_FRIEND_FRI](assets/ASSET-B_STATUS_FRIEND_FRI.md) | Asset evidence | B_STATUS_FRIEND_FRI.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_FRIEND_M](assets/ASSET-B_STATUS_FRIEND_M.md) | Asset evidence | B_STATUS_FRIEND_M.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_AWAY](assets/ASSET-B_STATUS_PC_AWAY.md) | Asset evidence | B_STATUS_PC_AWAY.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK1](assets/ASSET-B_STATUS_PC_LV_RANK1.md) | Asset evidence | B_STATUS_PC_LV_RANK1.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK2](assets/ASSET-B_STATUS_PC_LV_RANK2.md) | Asset evidence | B_STATUS_PC_LV_RANK2.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK3](assets/ASSET-B_STATUS_PC_LV_RANK3.md) | Asset evidence | B_STATUS_PC_LV_RANK3.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK4](assets/ASSET-B_STATUS_PC_LV_RANK4.md) | Asset evidence | B_STATUS_PC_LV_RANK4.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK5](assets/ASSET-B_STATUS_PC_LV_RANK5.md) | Asset evidence | B_STATUS_PC_LV_RANK5.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK6](assets/ASSET-B_STATUS_PC_LV_RANK6.md) | Asset evidence | B_STATUS_PC_LV_RANK6.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_LV_RANK7](assets/ASSET-B_STATUS_PC_LV_RANK7.md) | Asset evidence | B_STATUS_PC_LV_RANK7.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_PC_NA](assets/ASSET-B_STATUS_PC_NA.md) | Asset evidence | B_STATUS_PC_NA.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_SELL_B](assets/ASSET-B_STATUS_SELL_B.md) | Asset evidence | B_STATUS_SELL_B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_SELL_S](assets/ASSET-B_STATUS_SELL_S.md) | Asset evidence | B_STATUS_SELL_S.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_SELL_X](assets/ASSET-B_STATUS_SELL_X.md) | Asset evidence | B_STATUS_SELL_X.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_SHOW](assets/ASSET-B_STATUS_SHOW.md) | Asset evidence | B_STATUS_SHOW.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_TEAM_ADD](assets/ASSET-B_STATUS_TEAM_ADD.md) | Asset evidence | B_STATUS_TEAM_ADD.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_TEAM_L](assets/ASSET-B_STATUS_TEAM_L.md) | Asset evidence | B_STATUS_TEAM_L.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_STATUS_TEAM_X](assets/ASSET-B_STATUS_TEAM_X.md) | Asset evidence | B_STATUS_TEAM_X.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TALKBUBBLE](assets/ASSET-B_TALKBUBBLE.md) | Asset evidence | B_TALKBUBBLE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_GUILD](assets/ASSET-B_TEAM_GUILD.md) | Asset evidence | B_TEAM_GUILD.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_A01](assets/ASSET-B_TEAM_LOC_A01.md) | Asset evidence | B_TEAM_LOC_A01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_B01](assets/ASSET-B_TEAM_LOC_B01.md) | Asset evidence | B_TEAM_LOC_B01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_C01](assets/ASSET-B_TEAM_LOC_C01.md) | Asset evidence | B_TEAM_LOC_C01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_D01](assets/ASSET-B_TEAM_LOC_D01.md) | Asset evidence | B_TEAM_LOC_D01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_E01](assets/ASSET-B_TEAM_LOC_E01.md) | Asset evidence | B_TEAM_LOC_E01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_LOC_F01](assets/ASSET-B_TEAM_LOC_F01.md) | Asset evidence | B_TEAM_LOC_F01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TEAM_OUT](assets/ASSET-B_TEAM_OUT.md) | Asset evidence | B_TEAM_OUT.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TRADE_GOLD](assets/ASSET-B_TRADE_GOLD.md) | Asset evidence | B_TRADE_GOLD.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-B_TRADE_REDAY](assets/ASSET-B_TRADE_REDAY.md) | Asset evidence | B_TRADE_REDAY.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_CLUB_BENEFICENCE](assets/ASSET-BG_CLUB_BENEFICENCE.md) | Asset evidence | BG_CLUB_BENEFICENCE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_CLUB_INFO](assets/ASSET-BG_CLUB_INFO.md) | Asset evidence | BG_CLUB_INFO.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_CLUB_MEMBER](assets/ASSET-BG_CLUB_MEMBER.md) | Asset evidence | BG_CLUB_MEMBER.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_CLUB_MESS_EDIT](assets/ASSET-BG_CLUB_MESS_EDIT.md) | Asset evidence | BG_CLUB_MESS_EDIT.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_COMPLXE_MAP](assets/ASSET-BG_COMPLXE_MAP.md) | Asset evidence | BG_COMPLXE_MAP.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_EMOETION](assets/ASSET-BG_EMOETION.md) | Asset evidence | BG_EMOETION.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_EMOTETION](assets/ASSET-BG_EMOTETION.md) | Asset evidence | BG_EMOTETION.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_FRIEENDLIST_BIG](assets/ASSET-BG_FRIEENDLIST_BIG.md) | Asset evidence | BG_FRIEENDLIST_BIG.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_ICQ_BOX](assets/ASSET-BG_ICQ_BOX.md) | Asset evidence | BG_ICQ_BOX.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_01](assets/ASSET-BG_MAP_BIG_01.md) | Asset evidence | BG_MAP_BIG_01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_02](assets/ASSET-BG_MAP_BIG_02.md) | Asset evidence | BG_MAP_BIG_02.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_03](assets/ASSET-BG_MAP_BIG_03.md) | Asset evidence | BG_MAP_BIG_03.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_04](assets/ASSET-BG_MAP_BIG_04.md) | Asset evidence | BG_MAP_BIG_04.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_05](assets/ASSET-BG_MAP_BIG_05.md) | Asset evidence | BG_MAP_BIG_05.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_06](assets/ASSET-BG_MAP_BIG_06.md) | Asset evidence | BG_MAP_BIG_06.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_MAP_BIG_07](assets/ASSET-BG_MAP_BIG_07.md) | Asset evidence | BG_MAP_BIG_07.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_PVP_321_FIGHT](assets/ASSET-BG_PVP_321_FIGHT.md) | Asset evidence | BG_PVP_321_FIGHT.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_PVP_WIN_LOST](assets/ASSET-BG_PVP_WIN_LOST.md) | Asset evidence | BG_PVP_WIN_LOST.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_RANK](assets/ASSET-BG_RANK.md) | Asset evidence | BG_RANK.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_SKILL](assets/ASSET-BG_SKILL.md) | Asset evidence | BG_SKILL.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_01](assets/ASSET-BG_STATION_01.md) | Asset evidence | BG_STATION_01.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_02](assets/ASSET-BG_STATION_02.md) | Asset evidence | BG_STATION_02.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_03](assets/ASSET-BG_STATION_03.md) | Asset evidence | BG_STATION_03.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_04](assets/ASSET-BG_STATION_04.md) | Asset evidence | BG_STATION_04.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_05_KING](assets/ASSET-BG_STATION_05_KING.md) | Asset evidence | BG_STATION_05_KING.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_06_MAGIC](assets/ASSET-BG_STATION_06_MAGIC.md) | Asset evidence | BG_STATION_06_MAGIC.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_07_KNIGHT](assets/ASSET-BG_STATION_07_KNIGHT.md) | Asset evidence | BG_STATION_07_KNIGHT.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_STATION_08_ROBOT](assets/ASSET-BG_STATION_08_ROBOT.md) | Asset evidence | BG_STATION_08_ROBOT.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-BG_TRADE_TEMP](assets/ASSET-BG_TRADE_TEMP.md) | Asset evidence | BG_TRADE_TEMP.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ground](assets/ASSET-ground.md) | Asset evidence | ground.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ground1](assets/ASSET-ground1.md) | Asset evidence | ground1.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-HOTKEY_LX](assets/ASSET-HOTKEY_LX.md) | Asset evidence | HOTKEY_LX.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-HOTKEY_SX](assets/ASSET-HOTKEY_SX.md) | Asset evidence | HOTKEY_SX.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-HOTKEY_XL_B](assets/ASSET-HOTKEY_XL_B.md) | Asset evidence | HOTKEY_XL_B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-HOTKEY_XS_B](assets/ASSET-HOTKEY_XS_B.md) | Asset evidence | HOTKEY_XS_B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-Icon1](assets/ASSET-Icon1.md) | Asset evidence | Icon1.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ICON_STATUS](assets/ASSET-ICON_STATUS.md) | Asset evidence | ICON_STATUS.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ICON_STATUS_TEMP](assets/ASSET-ICON_STATUS_TEMP.md) | Asset evidence | ICON_STATUS_TEMP.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ICON_TALKBUBBLE](assets/ASSET-ICON_TALKBUBBLE.md) | Asset evidence | ICON_TALKBUBBLE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-ICON_TEMP](assets/ASSET-ICON_TEMP.md) | Asset evidence | ICON_TEMP.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-IE_SGO_LOGO](assets/ASSET-IE_SGO_LOGO.md) | Asset evidence | IE_SGO_LOGO.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-itemIcon](assets/ASSET-itemIcon.md) | Asset evidence | itemIcon.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-MAIL](assets/ASSET-MAIL.md) | Asset evidence | MAIL.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-NIGHTMASK](assets/ASSET-NIGHTMASK.md) | Asset evidence | NIGHTMASK.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-NPC_TALK_018](assets/ASSET-NPC_TALK_018.md) | Asset evidence | NPC_TALK_018.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-NPC_TALK_018_EYE](assets/ASSET-NPC_TALK_018_EYE.md) | Asset evidence | NPC_TALK_018_EYE.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-PET_CARD](assets/ASSET-PET_CARD.md) | Asset evidence | PET_CARD.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-PET_ICON](assets/ASSET-PET_ICON.md) | Asset evidence | PET_ICON.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-Photo](assets/ASSET-Photo.md) | Asset evidence | Photo.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-Photo1](assets/ASSET-Photo1.md) | Asset evidence | Photo1.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-Robot_Icon](assets/ASSET-Robot_Icon.md) | Asset evidence | Robot_Icon.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-Robot_Photo](assets/ASSET-Robot_Photo.md) | Asset evidence | Robot_Photo.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-SELECT_SEVER GOURP](assets/ASSET-SELECT_SEVER GOURP.md) | Asset evidence | SELECT_SEVER GOURP.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-SGO_LOGO_M_00](assets/ASSET-SGO_LOGO_M_00.md) | Asset evidence | SGO_LOGO_M_00.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-SKILL_ICON](assets/ASSET-SKILL_ICON.md) | Asset evidence | SKILL_ICON.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_01A](assets/ASSET-STORY_NAME_01A.md) | Asset evidence | STORY_NAME_01A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_01B](assets/ASSET-STORY_NAME_01B.md) | Asset evidence | STORY_NAME_01B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_02A](assets/ASSET-STORY_NAME_02A.md) | Asset evidence | STORY_NAME_02A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_02B](assets/ASSET-STORY_NAME_02B.md) | Asset evidence | STORY_NAME_02B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_03A](assets/ASSET-STORY_NAME_03A.md) | Asset evidence | STORY_NAME_03A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_03B](assets/ASSET-STORY_NAME_03B.md) | Asset evidence | STORY_NAME_03B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_04A](assets/ASSET-STORY_NAME_04A.md) | Asset evidence | STORY_NAME_04A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_04B](assets/ASSET-STORY_NAME_04B.md) | Asset evidence | STORY_NAME_04B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_05A](assets/ASSET-STORY_NAME_05A.md) | Asset evidence | STORY_NAME_05A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_05B](assets/ASSET-STORY_NAME_05B.md) | Asset evidence | STORY_NAME_05B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_06A](assets/ASSET-STORY_NAME_06A.md) | Asset evidence | STORY_NAME_06A.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-STORY_NAME_06B](assets/ASSET-STORY_NAME_06B.md) | Asset evidence | STORY_NAME_06B.spr 素材可達性 | P3 | GP-078-02 |
| [ASSET-TW_SGO_LOGO](assets/ASSET-TW_SGO_LOGO.md) | Asset evidence | TW_SGO_LOGO.spr 素材可達性 | P3 | GP-078-02 |
