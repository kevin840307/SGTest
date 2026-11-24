from pathlib import Path
import csv,json,re,collections
R=Path(__file__).resolve().parent
D=json.loads((R/'source_inventory.json').read_text(encoding='utf-8-sig'))
S={}
for f in R.glob('server_symbols_*.json'): S.update(json.loads(f.read_text(encoding='utf-8-sig')))
UI=R/'ui'; GAME=R/'gameplay'
UI.mkdir(exist_ok=True); GAME.mkdir(exist_ok=True)
def write(p,s): p.write_text(s.strip()+'\n',encoding='utf-8')
def path(u): return 'UI-'+Path(u['itf']).stem+'.md'
def info(t,goal,checks,plugins='',data='',phase='P2'):
    return (t,goal,checks,plugins,data,phase)
# UI metadata: one ITF per task; variants remain individually executable.
M={
'001':info('登入畫面','帳號與密碼輸入、確認、取消、錯誤回應、焦點切換','成功登入；空值與錯誤帳密拒絕；重試不重送；遮罩與 Tab/Enter 對照','SGOLogin','login.cfg；資料庫/account.sql','P1'),
'002':info('連線等待畫面','連線中的提示、等待、取消／超時與回到登入','正常連線切換；失敗回復；禁止重複發起連線','SGOLogin','login.cfg','P2'),
'003':info('選擇伺服器','伺服器列表、狀態燈、捲動、選擇、返回','伺服器滿載或離線不可誤進；列表更新和捲動；選擇後正確切換','SGOLogin','login.cfg；HK Interface/ServerName.dat','P1'),
'004':info('選擇角色','角色槽、角色預覽、進入遊戲、空槽創角、刪除及密碼入口','空槽與已有角色均可操作；選中的角色 UID 正確；返回／創角／刪除分支不串錯','SGOLogin','CreateCharPara.dat；資料庫/game.sql','P1'),
'005':info('建立角色','姓名、種族/性別及外觀選項、預覽、確認與返回；欄位以原版核對','合法角色建立後可選；重名／非法名稱拒絕；切換外觀與預覽一致','SGOLogin,SGOCareerSys','CreateCharPara.dat；Dat/Player/；Dat/Quest/Build_Character.txt','P1'),
'006':info('角色簡易狀態 HUD','HP/MP/EXP/WP 即時顯示；數值與進度裁切不錯位','受擊、恢復、升級、換圖、重登後正確；0/MAX 邊界','SGOPlayer,SGOBattleSys','Player.cfg；Dat/Player/','P1'),
'007':info('快捷鍵列','物品與技能欄位、拖曳、翻頁/展開縮合、快捷使用','拖入/移除後狀態同步；按鍵消耗正確；冷卻及重登保存','CDHotKey,SGOSkillSys,SGOGeneralItem','ClientData/Interface/skill.dat','P1'),
'008':info('小地圖','世界座標轉換、玩家/隊友標記、大小切換','跨圖重設、邊界與縮放正確；標記跟隨世界座標','SGOMapService,CDTeam','map.cfg；Dat/Map/','P1'),
'009':info('狀態效果列','Buff/Debuff icon、剩餘時間、移除、提示','增加、更新、到期、死亡清除／重登規則一致','SGOStatus,SGOSkillSys','status.cfg；skill.cfg','P1'),
'010':info('通用訊息對話框','訊息與確認/取消按鈕的模態流程','測試不同文字長度、Enter/Esc、焦點恢復、確認與取消不混淆'),
'011':info('登入載入進度','載入進度與進世界條件；錯誤可返回','WorldReady 前不誤進；正常載入結束；斷線不永久卡住','SGOLogin,SGOMapService','login.cfg；map.cfg','P1'),
'012':info('刪除郵件/驗證輸入畫面','確認原版實際用途與入口；禁止只依 DEL_EMAIL 名稱猜測刪角驗證或郵件流程','取得原版可重現入口；驗證/取消與錯誤回應一致','SGOLogin,CDChat','資料庫/icq.sql；account.sql','P3'),
'013':info('人物詳細資料與配點','角色屬性、職業、等級與可配點數；配點送 Server 確認','未確認不永久改值；合法配點成功；超量、重送與重登一致','SGOPlayer,SGOCareerSys','Dat/Player/；Player.cfg','P1'),
'014':info('稱號/暱稱清單','已取得稱號、選擇、變更與原版名稱顯示','未解鎖不可選；更換後自己與他人可見；重登保存','SGOTitleSys','ClientData/Interface/TitleName*.dat','P2'),
'015':info('職業清單與轉職','職業列表、已解鎖條件、轉職入口與回應','符合/不符條件；轉職後技能與屬性更新','SGOCareerSys','career.cfg；Dat/SGO.job','P2'),
'016':info('聊天小視窗','頻道選擇、輸入、發送、捲動、訊息歷史','不同頻道只收應收內容；中文輸入與長字串；大小切換','CDChat','chat.cfg；HK Interface/Chat.dat','P1'),
'017':info('聊天大視窗','展開聊天、歷史、頻道、捲動及返回小視窗','切換不遺失歷史/未送出文字；兩種模式顯示一致','CDChat','chat.cfg','P2'),
'018':info('表情選單','表情分頁、選擇與顯示事件','合法表情發送、其他玩家可見、重複與冷卻規則核對','CDEmotion','chat.cfg','P2'),
'019':info('對話泡泡樣式選單','樣式選擇及預覽，套用至聊天泡泡','每種可用樣式可選；聊天泡泡正確顯示；取消不改設定','CDChat,CDEmotion','HK Interface/BalloonStyle.dat','P2'),
'020':info('好友小視窗','好友、黑名單、新增/刪除、聯絡入口','好友線上狀態、雙方互動、黑名單過濾、重登','CDChat','chat.cfg；資料庫/icq.sql','P2'),
'021':info('主功能工具列','主選單各按鈕實際入口、顯示/隱藏與相鄰面板互斥','每個已確認入口能開關對應面板；世界操作與快捷鍵不衝突','','','P1'),
'022':info('裝備視窗','裝備槽、欄位分類、裝卸、裝備預覽與屬性','合法裝卸成功；職業等級不符拒絕；UID、能力值、重登一致','SGOEquip,CDItem','equip.cfg；Dat/Item/Equip.itd；Weapon.itd','P1'),
'023':info('背包視窗','物品分類、格位、圖示/數量、拖曳、提示與物品使用入口','拾取進包、堆疊、滿包、移動、使用、重登；失敗不複製物品','CDItem,SGOGeneralItem','Dat/Item/；ClientData/Item/Item.itd','P1'),
'024':info('寵物背包入口','寵物欄與選取、切換、寵物面板入口','寵物與一般物品不混淆；選擇後顯示正確 pet UID','SGOPetSys,CDItem','pet.cfg；ClientData/Item/petcli.ITD','P2'),
'025':info('排行榜詳細/第二頁','依 ITF 與 handler 確認排行明細、切頁與選取','空排行、多人排行、翻頁及正確資料對應','CDRankDLL','rank.cfg；ClientData/Interface/rank.dat','P2'),
'026':info('組隊視窗','建立/邀請/退出、成員列表、HP/MP 與隊長狀態','雙 Client 邀請、接受、拒絕、退出、換圖及斷線','CDTeam','Dat/Quest/CommTeam.txt','P2'),
'027':info('技能視窗','技能清單、圖示、等級、技能熟練度/學習與快捷列入口','未學會不可施放；學習後顯示、拖曳及消耗一致','SGOSkillSys,SGOCareerSys','skill.cfg；Dat/Skill/；ClientData/Interface/skill.dat','P1'),
'028':info('物品合成/強化視窗','配方、材料、數量、預覽、執行、結果；強化與合成規則分開核對','材料不足拒絕；成功/失敗依證據結算；交易原子性','SGOMix,SGOEquip,CDItem','equip.cfg；Dat/Skill/MixCommon.txt；Dat/Item/','P2'),
'029':info('系統設定視窗','音效/畫面/操作與退出等原版設定項目；不擅自新增原版未有控件','修改/取消/恢復設定；重新開啟與重登一致；不影響網路 tick','','HK Setting.bin','P2'),
'030':info('裝備修理視窗','可修理裝備列表、耐久/費用、選擇和確認','資金不足拒絕；修理後耐久與金錢原子更新；重登一致','SGORepair,SGOEquip','equip.cfg；Dat/Item/','P2'),
'031':info('NPC 商店主視窗','商品與售價、買賣切換、分類、捲動及選取','購買/出售合法物品；沒錢/滿包拒絕；退出清理交易上下文','SGOShop,CDItem','Dat/Shop/Shop.csv；Dat/Quest/','P1'),
'032':info('NPC 商店數量確認','物品、單價、數量、合計、確認/取消；核對各呼叫者用途','0/超量、金額溢位、取消；正確商品及 UID、原子結算','SGOShop,CDItem','Dat/Shop/Shop.csv','P1'),
'033':info('遊戲內訊息/名稱提示','依名稱變體顯示訊息或輸入名稱，回傳正確發起者','確認取消、中文字、長度與模態焦點'),
'034':info('銀行與倉庫視窗','存/取物、金錢、格位、分類、關閉與同步','雙向存取、容量/資金不足、重送/重登不複製','CDBank,CDItem','Dat/Quest/Bank.txt；資料庫/game.sql','P2'),
'035':info('玩家交易視窗','雙方物品與金錢、準備/確認/取消；變體依呼叫入口區分','雙 Client；任一方修改取消 ready；斷線 rollback；完成原子結算','SGOTrade,CDItem','資料庫/game.sql','P2'),
'036':info('寵物物品視窗','寵物持有/使用物品、清單、數量與寵物狀態','正確 pet UID；餵食或使用消耗正確；不可轉移無效物品','SGOPetSys,CDItem','pet.cfg；Dat/Item/pet.ITD','P2'),
'037':info('寵物裝備視窗','寵物裝備槽、裝卸及效果','裝備限制、能力更新、卸下與重登一致','SGOPetSys,SGOEquip','pet.cfg；Dat/Item/pet.ITD','P2'),
'038':info('等待提示框','非同步等待狀態與正確解除','正常回應、錯誤、取消、斷線均無永久遮罩'),
'039':info('寵物詳細資訊','寵物數值、配點、餵食、改名、召喚與收回入口','召喚/收回、HP/MP、選擇寵物與重登狀態一致','SGOPetSys','pet.cfg；Dat/Player/AttBase_Pet.csv','P2'),
'040':info('右側快捷面板','按鈕展開/收合、各變體差異、面板入口與可見性','所有已證入口可操作；切換解析度不偏移；不攔截世界點擊','','','P1'),
'041':info('聊天/NPC 對話泡泡','該編號泡泡背景、文字區、氣泡位置、生命週期','同樣式原版 A/B；多行／長字；重疊與超時清除','CDChat','HK Interface/BalloonStyle.dat','P2'),
'042':info('好友大型清單','好友/黑名單、管理、收信入口、捲動及縮合','新增/刪除/黑名單；大/小模式共享狀態；線上狀態','CDChat','資料庫/icq.sql；icq2.sql','P2'),
'043':info('好友訊息視窗','私訊或好友訊息讀取/回覆；確認原版收發入口','雙 Client 正確對象、離線和重登規則、長訊息捲動','CDChat','資料庫/icq.sql','P2'),
'044':info('問答/確認框','選項、確認、取消、模態輸入；分辨 A/B 變體','正確呼叫者、重複點擊、Esc 與選項回傳'),
'045':info('地圖名稱提示','換圖地名、顯示時間、動畫與清除','Map15/13 雙向傳點；提示不遮住輸入；不顯示舊地圖名','SGOMapService','map.cfg','P1'),
'046':info('改名對話框','共用命名輸入；辨認角色/寵物/機甲呼叫者及限制','非法與重名拒絕；正確 entity UID；取消不寫入','SGOLogin,SGOPetSys,SGORobotComplex','資料庫/game.sql','P2'),
'047':info('NPC 對話主框','NPC 文字、頭像、選項、下一步/關閉；依變體驗證','點 NPC→對話→選項→Server 回應；距離/重複/換圖安全','CDQuest','Dat/Quest/；ClientData/NPC/','P1'),
'048':info('寵物交易資訊','交易中的寵物能力預覽、選取與確認','交易前後寵物 UID 和所有權一致；取消不轉移','SGOTrade,SGOPetSys','pet.cfg；資料庫/game.sql','P2'),
'049':info('NPC 對話第二樣式','對應原版 NPC 對話分支，確認選項與文字容器','實際 NPC 入口可達；返回/關閉；點擊區與文字不偏移','CDQuest','Dat/Quest/','P2'),
'050':info('NPC 對話第三樣式 A','對應原版 NPC 分支及佈局','指定 NPC 原版 A/B；分支與確認取消正確','CDQuest','Dat/Quest/','P2'),
'051':info('NPC 對話其他樣式','按該 ITF 編號重現文字容器、選項與動畫','每個變體找到獨立可重現 NPC 路徑；點擊與回應一致','CDQuest','Dat/Quest/','P2'),
'052':info('文字輸入類型面板','輸入框、翻頁與文字/IME 互動；辨認變體用途','繁中輸入、長度、取消、焦點與頁面切換'),
'053':info('跑馬燈公告','公告文字、捲動時序、隊列與消失','連續公告有序且不遮操作；換圖/重登不重播過期項','CDRollMsg','HK ServerMsg.dat','P2'),
'054':info('寵物 HP 條','召喚時顯示正確寵物 HP 與可見狀態','受擊、恢復、死亡、收回時同步','SGOPetSys,SGOBattleSys','pet.cfg','P2'),
'055':info('交易金錢輸入','交易金額、上限、確認及取消','超餘額、負值、重送拒絕；交易更新 ready 狀態','SGOTrade','資料庫/game.sql','P2'),
'056':info('機甲資訊/物品視窗','依 Driver INFO/ITEM 變體重現機甲資訊、裝備與能源、物品管理','搭乘、卸下、能量、裝備屬性與重登一致','SGORidingSys,SGOHorseEquipMgr','Riding.cfg；Dat/Horse/','P2'),
'059':info('機甲修理視窗','機甲修理選擇、費用、能源/耐久與回應','金錢不足與合法修理；更新機甲資料而非角色裝備','SGORepair,SGOHorseEquipMgr','Riding.cfg；Dat/Horse/','P2'),
'079':info('主線任務敘事畫面','故事文字、章節標題、下一步與任務進度','主線條件與分支、獎勵只發一次；重登保留','CDQuest','Dat/Quest/','P2'),
'081':info('變更角色第二密碼','輸入原密碼及新密碼、確認與錯誤；核對原版限制','不記錄密碼；錯誤舊密碼拒絕；變更後新密碼可用','SGOLogin','login.cfg；資料庫/account.sql','P1'),
'082':info('角色第二密碼驗證','密碼輸入、遮罩、驗證、取消與限制','正確/錯誤密碼、取消返回、避免暴力重試','SGOLogin','login.cfg','P1'),
'083':info('機甲/寵物卡片預覽','卡片與對應實體圖示；以 handler 確認卡片類型及使用入口','正確卡片 ID、不同卡片及取消；不可錯用/重複使用','SGOPetSys,SGORobotComplex','Dat/Complex/RobotComplexCard.csv','P2'),
'084':info('怪物目標 HP 視窗','目標名、HP、選取/取消與距離、死亡清除','切換怪、攻擊扣血、MISS 不扣血、離開 AOI 清除','SGOBattleSys,SGOEnemyDistSys','enemy.cfg；Dat/Map/EnemyTable.dat','P1'),
'085':info('合體機甲資訊','合體資料、成員、技能/能力、改名及操作入口','合法合體、退出、成員同步、重登規則','SGORobotComplex','complex.cfg；Dat/Complex/','P2'),
'086':info('合體機甲資訊變體','合體資訊第二種版型與角色/組隊條件','取得實際使用狀態；與 085 共享資料且不重複更新','SGORobotComplex','complex.cfg','P3'),
'087':info('駕駛/合體能源條','能源/武器值變化與駕駛/合體狀態切換','搭乘、消耗、耗盡、下車與換圖正確','SGORidingSys,SGORobotComplex','Dat/Horse/；Dat/Complex/','P2'),
'088':info('合體 HP/MP/EXP 條','合體實體 HP、MP、EXP 及可見性','戰鬥、解體、升級、斷線正確','SGORobotComplex,SGOBattleSys','complex.cfg','P2'),
'089':info('玩家備忘錄交易文字','依交易寫入/讀取變體確認留言入口、權限及顯示','寫入/讀取分開；未授權不可改；中文字與保存','CDChat,SGOTrade','資料庫/game.sql','P3'),
'090':info('玩家備忘錄/個人狀態','公開個人狀態、頁面、好友/交易/隊伍資訊入口','自己的資料可更新、他人唯讀；權限與重登一致','CDChat,CDTeam','資料庫/game.sql','P3'),
'091':info('PvP 主戰鬥 HUD','對戰雙方、HP/MP、倒數與結果狀態；確認 HK 是否啟用','兩 Client 進場、勝負/離場、拒絕與斷線規則','SGOPKMapSys,SGOBattleSys','MapPK.cfg；MapPKItem.cfg','P2'),
'092':info('玩家擺攤','依店主/買家/名稱/招牌/商品變體，重現指定視窗與正確交易狀態','雙 Client 開攤、瀏覽、買賣、取消；最後一件競購只成功一次','SGOTrade,SGOShop,CDItem','SellingName.cfg；資料庫/game.sql','P2'),
'093':info('PvP 清單/排行','依 List/Rank 及編號確認清單、排行、投票或選取入口','原版模式可達性、列表更新、翻頁、雙方資料；未啟用勿假造','SGOPKMapSys,CDRankDLL','MapPK.cfg；rank.cfg','P3'),
'094':info('社團或髮型店','依具體檔名分流至社團或髮型功能，不混用資料','實際入口、權限/金錢、成功與取消、外觀/社團狀態重登','','club.cfg；Dat/Quest/','P2'),
'095':info('收藏圖鑑','依主畫面或六個子頁分類顯示收藏條件、完成度及可領取項目','解鎖/未解鎖、重複收藏、獎勵只一次、重登；各頁不能混資料','CDQuest,CDItem','HK Interface/Collection.itd；ItemCollectionData.dat；FlagCollectionData.dat','P3'),
'096':info('物品分解','選擇可分解物、成本、確認、結果與背包回寫','不可分解拒絕；材料消耗/產物原子性；滿包拒絕或原版處理','SGOMix,CDItem','Dat/Item/；HK Item/Collection.itd','P2'),
'097':info('任務清單','進行中/完成、條件進度與任務選取；確認 HK 清單按鈕','接取→進度→完成→獎勵；重登、放棄及重複限制','CDQuest','Dat/Quest/；HK Interface/Quest.dat、Task.dat、Reward.dat','P1'),
'098':info('回收/退款視窗','依 HK 原版入口確認 Refund 真實商品/物品返還語意，不猜測回購或退款規則','合法返還、失敗不扣款/不複製；重送與重登一致','SGOShop,CDItem','HK Item/SecondHand_Data.csv','P3'),
}
guild={
'057':('社團主選單','社團入口與選單切換','有/無社團、各入口權限'),
'058':('社團延伸面板','展開／收合及子頁面入口','展開後各入口正確、收合不遺失狀態'),
'060':('社團成員清單','成員、職位、線上狀態與管理','雙 Client 成員變更及角色權限'),
'061':('社團名稱輸入','建立/改名時的名稱驗證','重名/非法名拒絕、取消不建社團'),
'062':('社團盟友清單','同盟資料、狀態及選擇','兩社團同步、取消同盟後更新'),
'063':('社團金錢紀錄','金錢/捐贈紀錄、翻頁','資產更新與明細一致'),
'064':('社團科技清單','科技、等級與前置條件','未解鎖不可操作、升級後同步'),
'065':('社團書籤/標記','依原版確認書籤與標記用途','新增/刪除/保存/權限'),
'066':('社團介紹','介紹內容與捲動','他人唯讀、編輯後更新'),
'067':('社團公告','公告內容與捲動','新舊公告更新與權限'),
'068':('社團確認框','OK 或 YesNo 變體、回傳發起者','確認/取消、權限不足不執行'),
'069':('社團介紹編輯','編輯及保存介紹','字數/權限/取消/重登'),
'070':('社團公告編輯','編輯公告、保存與同步','非管理員拒絕；成員可見'),
'071':('社團結盟','選擇盟友與確認','雙方同意與拒絕；不可重複結盟'),
'072':('社團捐獻金額','輸入捐款與確認','餘額不足拒絕；社團金庫原子更新'),
'073':('社團資源輸入','輸入資源數量與確認','資源不足、重送、權限'),
'074':('社團科技開發','科技開發列表及條件','前置與成本驗證、開發結果'),
'075':('社團科技開發明細','科技開發第二畫面及確認','選中科技一致、取消不消耗'),
'076':('社團科技升級','選擇升級、材料與確認','等級/前置/資源驗證、原子更新'),
'077':('社團金庫提款','提款數量、權限與確認','管理員權限、餘額、併發原子性'),
'078':('解除社團同盟','選擇同盟、確認解除','雙方一致、取消不變更'),
'080':('社團排行榜','社團排行與翻頁','空列表、排名及資料更新'),
}
for k,(t,g,c) in guild.items(): M[k]=info(t,g+'；先確認 Old/HK 是否實際啟用該舊式社團子頁',c+'；若 HK 不可達，保留版本差異而非虛構入口','','club.cfg；資料庫/game.sql','P3')
# Dynamic client-loaded assets: Old Paradise.exe.lst exact line addresses obtained by search.
dyn={
'007':['HOTKEY_LY.spr (L123639, 0044AF00)','HOTKEY_SY.spr (L123671, 0044AF63)','HOTKEY_YL_B.spr (L123834, 0044B0FB)','HOTKEY_YS_B.spr (L123848, 0044B123)','Interface/SKILL_ICON.spr (L125466, 0044C206)','Interface/EMOTIONAL_ICON.spr (L125519, 0044C2FE)'],
'026':['Interface/B_TEAM_LOC_%c01.spr (L131095, 0044F479)'],
'013':['Interface/Photo.spr (L191171, 00472E92；呼叫者待追)','Interface/Icon.spr (L52571, 00421150；共用 icon)'],
'027':['Interface/SKILL_ICON.spr (L36399, 004164C4；L260323, 0049EAAE)'],
'039':['Interface/Pet_Photo/Pet_%s.spr (L194512, 004756EA；L227138, 0048949F)'],
'048':['Interface/Pet_Photo/Pet_%s.spr (L194512, 004756EA；用途需追呼叫者)'],
'083':['MS/PetCard/CARD_%03d.spr (L258231, 0049D639)','MS/PetCard/PET_%03d.spr (L258321, 0049D799)'],
'084':['Interface/HP.spr (L258850, 0049DC58；共用 HP)'],
'088':['Interface/HP.spr (L263240, 004A0AC2)'],
'018':['Interface/EMOTIONAL_ICON.spr (L36500, 0041659F)'],
'093':['Interface/B_PVP_KING.spr (L273925, 004A7627)'],
}
candidates={'006_StatusLite_G.itf':['HPBAR.spr','HP.spr','MP.spr','EXP.spr','WP.spr'],'013_Character_INFO.itf':['BG_CHARACTER_INFO.spr','ADD_POINT.spr','Photo.spr'],'021_COMMAND.itf':['COMMAND.spr','B_BASIC_PLAYER.spr','B_BASIC_ITEM.spr','B_BASIC_SKILL.spr','B_BASIC_SYSTEM.spr','B_BASIC_QUEST.spr','B_BASIC_GUILD.spr','B_BASIC_PSHOP.spr'],'097_Quest_List.itf':['BG_QUEST_LIST.spr','B_QUEST.spr','B_BASIC_QUEST.spr'],'098_Refund.itf':['BG_REFUND.spr','Icon.spr']}
special_handlers={
'001':['CLoginItf::MessageMap L70599 @0042B04C','CLoginItf::OnOK L70702 @0042B102','CLoginItf::ValidateData L71002 @0042B3EA','CLoginItf::MakeData L71535 @0042B923','CSGOCore::OnLogin L1645 @004021E9'],
'003':['CSelectServerItf::ReadServerList L73566 @0042CB59','OnScrollUp L73738 @0042CD15','OnScrollDown L73771 @0042CD4F','OnSelectSlot L74261 @0042D213'],
'004':['CSelectCharacterItf::OnDeleteChar L72278 @0042BFF2','OnChangePwd L72332 @0042C06B','OnSelectChar L72386 @0042C0E4','OnBack L72442 @0042C167','CSGOCore::OnSelectChar L2021 @004025C6'],
'005':['CCreateCharacterItf::RefreshHead L62777 @0042656C','RefreshHand L62949 @00426761','RefreshArmor L63074 @004268C5','ReadOptionConfig L63284 @00426B09','OnOK L64013 @00427338','ValidateData L65033 @00427EE4','ResetRace L65511 @00428425','CSGOCore::OnCreateChar L2173 @00402755'],
}
assetset={x.casefold():x for x in D['assets']}
index=[]
def server_evidence(plugins):
    if not plugins: return '- 無指定獨立 Server 插件；先從客戶端 handler、共同服務及現有實作追查。'
    lines=[]
    for pl in plugins.split(','):
        lines.append(f'- Old/服務端/plugins/{pl}.dll.lst（已見檔案；以下函式為已擷取的定位，不代表其規則已解出）。')
        for v in S.get(pl,[])[:8]:
            lines.append(f"  - \`{v['owner']}::{v['method']}\` L{v['line']} @0x{v['address']}")
    return '\n'.join(lines)
def label(u):
    k=u['itf'][:3]; name=u['itf'].lower(); t,g,c,p,d,phase=M[k]
    if k=='024' and 'rank' in name: t='排行榜主視窗';g='排行清單、排序、翻頁、查看詳細資料；核對 024b 版本差異';c='空清單、多名排行、順序與查看詳情';p='CDRankDLL';d='rank.cfg；ClientData/Interface/rank.dat'
    if k=='094':
        if 'hair' in name: t='髮型商店';g='原版髮型預覽、左右切換、費用與確認';c='金錢不足拒絕、取消不變更；成功後雙 Client 外觀與重登一致';p='SGOShop,SGOPlayer';d='Dat/Quest/；HK Player/'
        else: t='社團總覽';g='社團列表、加入/建立、資訊與管理入口；確認 HK 新式社團流程';c='有/無社團、權限、邀請與退出同步';d='club.cfg；資料庫/game.sql'
    if k=='056':
        if 'item' in name: t='機甲物品/裝備列表';g='選擇機甲物品、分類、裝卸或查看入口；確認 UID';c='合法裝卸、非法類型拒絕、同步與重登'
        else: t='機甲詳細資訊'
    if k=='033': t='遊戲內訊息' if 'ingame' in name else '名稱輸入提示'
    if k=='092':
        for suffix,x,y in [('buy_item_01','玩家商店購買商品確認 01','核對單品/數量及價格；確認購買'),('buy_item_02','玩家商店購買商品確認 02','核對第二類購買確認與價格'),('sell_item_01','擺攤商品設定 01','選擇商品並輸入價格/數量'),('sell_item_02','擺攤商品設定 02','設定第二類商品/價格及取消'),('pshop_head','擺攤招牌','店名、世界招牌可見性及 AOI'),('pshop_name','擺攤命名','輸入店名、限制、確認與取消'),('pshop_buy','玩家商店買家清單','瀏覽他人商店、選取商品'),('pshop_sell','玩家商店賣家清單','選擇出售物品、管理上架'),('pshop.itf','玩家商店主畫面','開攤、關攤、商品與確認')]:
            if suffix in name:t=x;g=y+'；依實際 handler 驗證';break
    if k=='093': t=('PvP 排行' if 'rank' in name else 'PvP 列表')+' '+Path(u['itf']).stem
    if k=='095' and re.search(r'_\d\d[.]itf',name):t='收藏圖鑑子頁 '+name[-6:-4]
    if k in ('041','049','051','010','068','089','060','040','007','008','035','056','006','044','047','052','024'): t+='（'+Path(u['itf']).stem+'）'
    return t,g,c,p,d,phase
for u in D['ui']:
    k=u['itf'][:3]; t,g,c,p,d,phase=label(u); fn=path(u)
    old='\n'.join(f"- Old/ClientData/Paradise.exe.lst L{o['line']} @0x{o['address']}（ITF 引用）"+(f"；鄰近 Create 符號 \`{o['owner']['name']}::Create\` L{o['owner']['line']} @0x{o['owner']['address']}，需確認函式邊界" if o['owner'] else '') for o in u['old']) or '- 本次 Old 靜態掃描未定位直接 ITF 字串；不可據此判定無功能。'
    hk='\n'.join(f"- HK/SGOClient.exe.lst L{n} @0x{addr}（.text ITF 引用）" for n,addr in u['hk'][:15]) or '- HK .text 未定位直接字串；需追 ITF 共用載入器、動態名稱或版本可達性。'
    act='\n'.join(f'- {x}' for x in special_handlers.get(k,[])) or '- 從上述 Create/引用地址向前後追 MessageMap/On*/MakeData/ValidateData/Draw/Update 與封包呼叫；尚未取得完整 handler/控制項對照。'
    refs='\n'.join(f"- \`{x}\` → "+(f"HK/output/Interface/{assetset[x.casefold()]}" if x.casefold() in assetset else '解包目錄未確認檔案，需查子目錄/版本') for x in u['spr'])
    if not refs:refs='- ITF 為 C.D.E. 封裝，未取得明文字串。不得把下面候選當成已確認引用。\n'+'\n'.join(f'- 候選（檔案存在，需解密/執行驗證）：\`{x}\`' for x in candidates.get(u['itf'],[]))
    dynamic='\n'.join('- '+x for x in dyn.get(k,[])) or '- 此任務暫無已定位的額外動態 SPR；需檢查 Draw/資源載入器，不能假設 ITF 已包含全部。'
    evidence='Old+HK 直接引用' if u['old'] and u['hk'] else ('Old 直接引用' if u['old'] else ('HK 直接引用' if u['hk'] else '僅 ITF 存在；可達性待證'))
    if u['cde']: evidence+='；C.D.E. 待解'
    deps='先完成相應玩法權威/資料介面；純呈現可使用正式快照，但禁止 fake PASS。'
    s=f"""# {fn[:-3]} — {t}

- 優先度：{phase}（建議排程，非目前缺陷狀態）
- 工作軌：UI；狀態：NOT VERIFIED（盤點不等於已完成）。
- 來源狀態：{evidence}。原版 ITF：\`HK/output/Interface/{u['itf']}\`（{u['size']} bytes）。
- 目標：{g}。
- 依賴：{deps}

## 1. 反編譯證據與行為入口
{old}
{hk}
{act}

## 2. SPR / UI 資源
以下為 ITF 實際可讀 SPR 字串的去重清單（大小寫依原檔；僅字串級確認，不代表已解析 control/frame/rect）。
{refs}

### 直接載入／動態素材線索
{dynamic}

## 3. Server / Data 對照
{server_evidence(p)}
- 相關資料：{d or '無獨立資料檔已確認；追原版 handler 與現有 SGTest domain。'}
- 原版數值、opcode、條件、交易語意若未追清不得自行補猜。

## 4. 本輪獨立實作任務
- [ ] 檢查 SGTest 現有對應畫面、Feature Matrix、TODO；只補本畫面未完成項。
- [ ] 由指定 ITF/反編譯定位建立 control ID → SPR/frame → rect(x,y,w,h) → hitbox → handler → 字型/文字 baseline 表；C.D.E. 或未可達畫面先證據解鎖。
- [ ] 原版素材優先順序 Data1_PNG2 → Data1_PNG → Data1.dat → Interface；以既有 converter/manifest 匯入，不自行重畫。
- [ ] 實作本畫面：{g}；使用同 local pixel origin 與原版 hover/pressed/disabled/selected、scroll/drag/modal（適用項）。
- [ ] 將 UI 操作接到現有正式 Client transport/reducer 與 Server 權威；若為純顯示則讀真實狀態，不新增假的 Server opcode。
- [ ] 保留其他畫面及並行 Session WIP；若此 ITF 為同家族變體，重用共同 domain/控件但單獨驗此變體。

## 5. 完成驗收（分兩道 gate）
- [ ] 可玩驗收：{c}。
- [ ] 負向：無效輸入、取消、重複點擊、連線失敗/權限不足依適用性測試；不得留下卡死、過期狀態或錯誤資料。
- [ ] 原版驗收：Windows 800×600 同 DPI/同狀態 HK Reference、SGTest Before/After、ROI overlay/diff；核對 sprite/frame、字型、文字位置、hitbox、按鈕狀態。未驗項保持 NOT VERIFIED，不阻塞其他獨立玩法。
- [ ] 保存實際操作與 Server 結果證據；涉及多人/持久化則雙 Client/重登；更新既有 TODO/Matrix，不把新增文件標 COMPLETE。
"""
    write(UI/fn,s); index.append({'id':fn[:-3],'track':'UI','priority':phase,'title':t,'path':'ui/'+fn,'evidence':evidence})
# Gameplay tasks independent of UI layout. Format: id | title | plugin | data | related UI prefixes | precise task | acceptance
G=r"""
001|登入與帳號 session|SGOLogin|login.cfg；account.sql|001,002,003,010,038|從帳號驗證到 session 綁定與伺服器選擇，確認 Old Login handler、錯誤碼及限制|正確帳密成功；錯誤/斷線拒絕；重複登入按證據處理
002|角色列表與進入世界|SGOLogin,SGOMapService|CreateCharPara.dat；game.sql|004,011|取得角色列表、角色 UID 綁定、WorldReady 快照，防止讀錯帳號角色|選任一自己角色正常進圖；非自己 UID 拒絕；快照就緒可走
003|角色建立|SGOLogin,SGOCareerSys|CreateCharPara.dat；Dat/Quest/Build_Character.txt；Dat/Player/|005|驗證命名/種族/外觀與初始屬性，原子寫入角色初始資料|合法角色可選與進世界；重名拒絕；重登一致
004|角色刪除與第二密碼|SGOLogin|login.cfg；account.sql；game.sql|004,012,081,082|依原版核對刪除/安全密碼驗證與變更，隔離敏感資料|錯誤密碼與他人 UID 拒絕；取消無副作用；使用測試帳號
005|人物初始外觀與 paper doll|SGOPlayer,SGOEquip|Dat/Player/；Dat/Item/；HK Player/|004,005,013,022|核對外觀各層、方向/動作/裝備組合與固定動畫時序|雙 Client 初始外觀一致；裝卸後無錯層、走跑攻擊不串
006|點地面移動與停止|SGOMapService,SGOPlayer|map.cfg；Dat/Map/；OLD_MOVEMENT_SYNC_ANALYSIS.md|008,040|原版移動封包/座標、點地/長按/停止，Server 權威並 Client 預測|單人連續走停/改點正確；雙 Client 無明顯瞬移；拒絕非法座標
007|地圖碰撞與尋路|SGOMapService|Dat/Map/|008|追 Blocking 資料與可走規則，固定 Server 路徑；禁止穿牆|Map13 階梯、轉角、不可走點、目標可達/不可達；無卡死
008|傳點與跨圖 lifecycle|SGOMapService,SGOPlayer|map.cfg；Dat/Map/；Dat/Quest/|011,045,008|Map15↔13 真傳點，切換 epoch、清舊 AOI、WorldReady|雙 Client 跨圖、立即走、舊圖事件不可污染新圖
009|玩家 AOI 進出與同步|SGOMapService,SGOPlayer|OLD_PLAYER_MOVEMENT_PACKET_SYNC.md；OLD_MOVEMENT_SYNC_ANALYSIS.md|008,026|baseline→delta、Spawn/Despawn、remote interpolation 與可靠事件|雙向看見/離開/重入；不同解析度；跨圖不殘影
010|怪物生成與重生|SGOEnemyDistSys|enemy.cfg；Dat/Map/EnemyTable.dat；ClientData/Enemy/EnemyTable.dat|084|原版生成分布、重生時間與狀態，Server 管理怪物 UID|指定怪點擊殺後按證據重生；多玩家看到同一狀態
011|怪物巡邏與追擊/回位|SGOEnemyDistSys,SGOMapService|enemy.cfg；Dat/Map/|084|巡邏、索敵、仇恨、追擊距離、阻擋與脫戰回位|怪不穿牆；拉遠後回位；雙 Client 同步；不站樁原地跑
012|正式角色戰鬥屬性解鎖|SGOPlayer,SGOCareerSys,SGOBattleSys|Dat/Player/AttBase_*.csv；Player.cfg；career.cfg；battle.cfg|006,013,022|先重現正式新角 combat-locked blocker；從原版屬性/職業/裝備取得合法戰鬥 profile|正式新角 Map15→13 可普攻；不用 SyntheticTest 或猜測數值；未確認 profile 仍 fail-closed
013|選怪與接近攻擊距離|SGOBattleSys,SGOMapService|battle.cfg；Dat/Item/Weapon.itd|084,007|滑鼠選怪、目標 UID、依武器/技能距離接近與停止，Server 重驗|近戰距離與長程區分；移動怪可追；離圖/死亡取消目標
014|普通攻擊與命中/MISS|SGOBattleSys|battle.cfg；Dat/Player/；Dat/Item/Weapon.itd|006,084,007|追 OnPlayerAttack、OnAttack、IsHit_PerHit、CalHit_PerHit、CalMiss_PerHit；權威 HP 與動畫分離|成功命中、MISS 不扣 HP；頻率/距離/非法目標拒絕；他人可見
015|傷害結算、受擊與動畫|SGOBattleSys,SGOStatus|battle.cfg；status.cfg|006,084|追 CalWeaponDamage_PerHit、CalFinalChange、CalHP_Final、SendCharSyncPac；可靠戰鬥事件|自己及他人受擊、傷害數字/MISS、死亡狀態不被 Move 覆蓋；動畫速度固定
016|擊殺、EXP、金錢與升級|SGOBattleSys,SGOEnemyDistSys,SGOPlayer|battle.cfg；Dat/Player/ExpUse.csv；Player_Up_Lv.csv|006,013|追 CalExp_Final/CalGold_Final、死亡條件與升級屬性|擊殺只結算一次；EXP/升級與資料持久化；多人歸屬正確
017|怪物死亡與掉落生成|SGOEnemyDistSys,CDItem|enemy.cfg；Dat/Map/；Dat/Item/|023,084|追 CheckDead、CheckDropItem；掉落 UID、所有權與生命週期|擊殺產生合法掉落，重複事件不增生；AOI 正確顯示
018|地上物拾取|CDItem,SGOGeneralItem|Dat/Item/；ClientData/Item/Item.itd|023|距離、歸屬、拾取原子轉移與地上物移除|雙 Client 競拾只有一人成功；滿包、過遠拒絕；回圖/重登一致
019|玩家死亡與復活|SGOPlayer,SGOBattleSys,SGOMapService|Player.cfg；Dat/Quest/各 Reborn 腳本|006,010,044,045|死亡狀態、原版損失與復活選擇、復活點傳送|死亡不可移動/攻擊；合法復活後可走可打；重登狀態一致
020|技能學習與職業限制|SGOSkillSys,SGOCareerSys|skill.cfg；career.cfg；Dat/Skill/；Dat/SGO.job|027,015|原版技能 ID、職業/等級前置、學習/升級保存|未學不可用；合法學習可拖入快捷鍵、重登存在
021|單體技能與 MP/冷卻|SGOSkillSys,SGOBattleSys|skill.cfg；Dat/Skill/|027,007,084|追施法路徑與消耗、目標、冷卻、傷害、命中與效果|合法技能消耗正確；MP不足、冷卻、超距拒絕；雙 Client 動畫
022|範圍技能與多目標|SGOSkillSys,SGOBattleSys|skill.cfg；Dat/Skill/|027,084|確認範圍形狀、目標上限/篩選與多次結算；星界震盪等依證據|範圍內外對照；多人/多怪只結算一次；資源和冷卻一致
023|Buff/Debuff 與治療|SGOStatus,SGOSkillSys|status.cfg；Dat/Skill/curecommon.txt；boostcommon.txt|009,006,026|施加、疊加、持續時間、到期、治療與解除；禁止猜公式|雙 Client 狀態/HP/MP 同步；死亡/重登與超上限
024|背包格位與堆疊|CDItem,SGOGeneralItem|Dat/Item/；ClientData/Item/Item.itd|023|建立、增減、分堆/合併與分類、容量、UID ownership|滿包/堆疊上限/錯誤 UID；重送不複製；重登一致
025|消耗品使用與恢復|SGOGeneralItem,SGOStatus|generalitem.cfg；Dat/Item/Recovery.itd；Booster.itd|023,007,006|使用藥水/消耗品、使用條件、效果與扣量原子性|HP/MP 真值；冷卻/空堆疊拒絕；重登保存
026|裝備裝卸與屬性|SGOEquip,CDItem,SGOPlayer|equip.cfg；Dat/Item/Equip.itd；Weapon.itd|022,013,006|OnEquipItem/OnUnEquipItem、槽位/職業等級限制、派生屬性與外觀|裝卸、替換、不合法拒絕；UID 與雙 Client 外觀、重登一致
027|屬性配點與重算|SGOPlayer,SGOCareerSys|Dat/Player/Att_Up.csv；AttBase_*.csv；STR/DEX/CON/WIS 表|013,006|驗證點數、種族/職業成長、屬性重算與持久化|配點成功與超量拒絕；派生戰鬥屬性與重登一致
028|快捷鍵綁定及執行|CDHotKey,SGOSkillSys,SGOGeneralItem|ClientData/Interface/skill.dat|007,023,027|技能/物品綁定槽位、鍵位觸發與 Server 使用入口；重登規則|拖曳、覆蓋、清除、快捷使用與重登；空槽不送無效命令
029|NPC 互動與腳本分支|CDQuest,SGOMapService|Dat/Quest/；ClientData/NPC/|047,049,050,051,079|距離、NPC UID、腳本分支與對話上下文；不直接由 Client 執行交易|真 NPC 點擊/選項/退出；離圖過期、無效選項拒絕
030|NPC 商店購買|SGOShop,CDItem|Dat/Shop/Shop.csv；Dat/Quest/|031,032,023|OnBuy、商品/價量、錢包與格位原子更新|足額購買；缺錢、滿包、價格變動、重送拒絕
031|NPC 商店出售|SGOShop,CDItem|Dat/Shop/Shop.csv；Dat/Item/|031,032,023|OnSell、合法 UID/數量、價格與金錢結算|販售成功；非持有/裝備中等非法品拒絕；重送不多給錢
032|銀行存取金錢|CDBank|Dat/Quest/Bank.txt；game.sql|034|查原版金錢存取 handler、帳戶/角色綁定與交易|存/取/餘額不足、重送及重登；無負數
033|倉庫存取物品|CDBank,CDItem|Dat/Quest/Bank.txt；game.sql|034,023|格位、容量、UID 所有權與倉庫原子轉移|存取、滿包/滿倉、重送及重登不複製
034|裝備耐久與修理|SGORepair,SGOEquip,SGOBattleSys|equip.cfg；battle.cfg；Dat/Item/|030,022|戰鬥耐久消耗、DoRepair、費用/金錢及耐久交易|戰鬥消耗、足額修理、無錢拒絕；重登一致
035|裝備強化|SGOMix,SGOEquip,CDItem|equip.cfg；Dat/Item/；HK Item/grading.csv|028,022|確認強化公式、材料、失敗行為；SGTest instance+level，不擴張 ID|合法與非法強化、資源原子性、重登、原版結果對照
036|物品合成|SGOMix,CDItem|Dat/Skill/MixCommon.txt；Dat/Item/|028,023|配方、材料驗證、結果與失敗回應|成功與不足材料、滿包、重送；物品無複製
037|物品分解|SGOMix,CDItem|HK Item/Collection.itd；Dat/Item/|096,023|追 HK Dissolve 與 Old Mix 的差異；只依證據建立配方|合法/非法、滿包、原子性與重登
038|玩家雙方交易|SGOTrade,CDItem|game.sql|035,055,048|交易邀請、放入物品/錢、雙方 ready/confirm，變更使 ready 失效|雙 Client 成功、取消、斷線與競態；不可複製/丟失
039|玩家商店開攤與管理|SGOTrade,SGOShop,CDItem|SellingName.cfg；game.sql|092|名稱、上架、價格、數量、關攤及世界招牌 AOI|他人可見；關攤/換圖規則；已上架物品不可重複交易
040|玩家商店購買與競購|SGOTrade,SGOShop,CDItem|SellingName.cfg；game.sql|092|買方瀏覽、原子購買與扣款/移轉，最後一件併發|雙 Client 與多買家同時買只一次成功；餘額、重登一致
041|組隊邀請與隊長|CDTeam|Dat/Quest/CommTeam.txt|026|隊伍建立、邀請、接受/拒絕、踢出、離隊、隊長轉移|兩至三 Client 互動；斷線/換圖；隊伍權威一致
042|組隊戰鬥共享與狀態|CDTeam,SGOBattleSys|Dat/Quest/CommTeam.txt；battle.cfg|026,006,008|隊友 HP/MP/AOI、EXP/掉落分配需原版證據|組隊擊殺與非隊友；跨圖/離隊；不能越權分配
043|聊天頻道與私訊|CDChat|chat.cfg；icq.sql|016,017,043,041|頻道範圍、中文、私訊路由、歷史及權限|雙 Client 地圖/私訊/隊伍等已確認頻道；不能傳給錯誤對象
044|好友與黑名單|CDChat|icq.sql；icq2.sql|020,042,043|好友新增刪除、在線狀態、黑名單與訊息過濾|雙 Client 新增/刪除/封鎖、重登，拒絕非法 UID
045|郵件與留言|CDChat,CDGiftSys|icq.sql；icq2.sql；game.sql|012,020,042,043,089,090|確認原版郵件/留言入口、儲存及收件權限；附件若有證據再做|寄送/閱讀/刪除或確認的實際流程；重登、非收件者拒絕
046|表情與泡泡樣式|CDEmotion,CDChat|chat.cfg；HK Interface/BalloonStyle.dat|018,019,041|表情事件、泡泡樣式與 AOI 傳播|雙 Client 表情正確、樣式對照、過期清理
047|稱號變更|SGOTitleSys|ClientData/Interface/TitleName.dat|014,013|原版稱號取得/選擇與名稱顯示|未取得拒絕；切換後雙 Client/重登一致
048|轉職與職業條件|SGOCareerSys,SGOSkillSys|career.cfg；Dat/SGO.job；Dat/Quest/|015,013,027|核對職業路徑、前置任務、等級、屬性與技能變化|條件符合與不符、重送、重登、戰鬥 profile 更新
049|任務接取與進度|CDQuest|Dat/Quest/；HK Interface/Quest.dat；Task.dat|047,079,097|任務 ID、前置、接取、事件計數與放棄規則|接取/擊殺或收集/進度更新；重登；非法事件不累加
050|任務完成與獎勵|CDQuest,CDItem,SGOPlayer|Dat/Quest/；HK Interface/Reward.dat；RewardItem.dat|047,079,097|完成條件、獎勵與狀態原子持久化|不足條件拒絕；獎勵一次；滿包/重送/重登安全
051|寵物召喚與收回|SGOPetSys|pet.cfg；ClientData/Item/petcli.ITD|024,039,054|寵物 UID、召喚/收回、地圖 lifecycle 與外觀同步|雙 Client 可見、收回 despawn、換圖/重登規則
052|寵物 AI 與戰鬥|SGOPetSys,SGOBattleSys|pet.cfg；battle.cfg|039,054,084|跟隨、碰撞、選怪/攻擊、HP/死亡及主人歸屬|寵物不穿牆；多人觀察、合法攻擊與傷害一致
053|寵物餵食、配點與裝備|SGOPetSys,SGOEquip,CDItem|pet.cfg；Dat/Item/pet.ITD；Dat/Player/AttBase_Pet.csv|036,037,039|各類寵物資源、配點、裝卸與餵食更新|各操作合法/非法、重登與寵物 UID 一致；若未完成分子任務
054|寵物交易|SGOPetSys,SGOTrade|pet.cfg；game.sql|048,035|寵物可交易條件、預覽、所有權原子移轉|雙 Client 成功與取消；召喚/綁定限制依證據；重登
055|機甲搭乘與解除|SGORidingSys,SGOHorseEquipMgr|Riding.cfg；Dat/Horse/HorseMA.csv|056,087|搭乘/解除、外觀、移動、戰鬥模式切換|雙 Client 駕駛外觀；無效狀態拒絕；跨圖與重登
056|機甲裝備與能源|SGOHorseEquipMgr,SGORidingSys|Dat/Horse/HorseEquipMA.csv；HorseBattery.csv|056,087|合法機甲裝備、能源消耗、補充/耗盡規則|能源與屬性同步；耗盡不非法繼續；重登一致
057|機甲修理與升級|SGORepair,SGORidingSys,SGOHorseEquipMgr|Dat/Horse/；HK MS/Horse_Up_Lv.csv|059,056|確認機甲修理費/耐久與 EXP/升級規則|修理與升級各自正確；費用與重登一致
058|機甲卡片與合體|SGORobotComplex,SGORidingSys|complex.cfg；Dat/Complex/RobotComplexCard.csv；RobotComplexMA.csv|083,085,086,087,088|卡片、合體前置、建立/解除與成員權威|多 Client 合體/解除，非法條件拒絕、跨圖/斷線
059|PvP 地圖與名稱狀態|SGOPKMapSys,SGOBattleSys|MapPK.cfg；MapPKItem.cfg；battle.cfg|084,091,093|地圖 PK 許可、名稱狀態與擊殺規則，對照原版而非 UI 顏色作判斷|安全區拒絕、允許地圖可打；雙 Client、死亡/換圖
060|PvP 對戰與排行|SGOPKMapSys,CDRankDLL|MapPK.cfg；rank.cfg|091,093|先確認該原版模式實際啟用與規則，再做邀請、進場、結算與排行|可達模式雙 Client 勝負與斷線；不啟用的版型保留證據
061|排行榜|CDRankDLL|rank.cfg；ClientData/Interface/rank.dat|024,025,080|排名來源、更新時序、翻頁與資格|排序、空排行、多人同分；資料與重登一致
062|社團建立與成員| |club.cfg；game.sql|057,058,060,061,094|先查 Old club.cfg、DB 與 HK handler；建立/加入/退出、職位權限|兩社團/多 Client；有無權限、重登；未確認入口不假造
063|社團同盟與公告| |club.cfg；game.sql|062,066,067,069,070,071,078|核對同盟及公告權限、雙方變更與文字儲存|結盟/解除、公告/介紹編輯及拒絕；重登一致
064|社團資源、金庫與科技| |club.cfg；game.sql|063,064,065,072,073,074,075,076,077|確認原版子系統可達性、成本、權限與交易；可再拆垂直切片|捐獻/提款/資源/科技前置與原子更新；雙方併發
065|髮型商店|SGOShop,SGOPlayer|Dat/Quest/；HK Player/|094|HK 入口、預覽與價格、正式外觀資料更新|確認/取消、金錢不足、其他玩家與重登可見
066|收藏與獎勵|CDItem,CDQuest|HK Interface/Collection.itd；ItemCollectionData.dat；FlagCollectionData.dat|095|核對六子頁收藏條件、消耗/領獎及持久化|不同分類、未達成拒絕、獎勵一次、重登
067|退款/回購功能證據與實作|SGOShop,CDItem|HK Item/SecondHand_Data.csv|098|先解 C.D.E. ITF 與 HK handler，區分 Refund 語意再建立原子交易|確認實際入口與交易條件；重送不複製或多給錢
068|系統設定與登出|SGOLogin|HK Setting.bin；login.cfg|029,001|設定持久化、登出清理 session/世界資源；不動可靠收包節奏|設定重開一致；登出回登入、重登、其他 Client despawn
069|全域公告與跑馬燈|CDRollMsg|HK ServerMsg.dat|053,033|確認公告事件、顯示隊列與權限|合法公告收到、錯誤權限拒絕、切圖不殘留
070|禮物/邀請獎勵|CDGiftSys|PromoteInvite.cfg；Dat/Quest/Add_Gift.txt|033,047|查證原版領取與資格規則，防止重複發放|有資格/無資格；領取一次；重登與滿包安全
071|GM 管理功能| |gmiplist.cfg；plugins/CDGMSys.dll.20|021|只以授權測試角色操作；查 GM 入口與 Server 權限，不因 UI 顯示賦權|一般玩家拒絕；授權測試指令有審計；不碰正式資料
072|原版游標與地面選取|SGOMapService,SGOBattleSys|ClientData/Paradise.exe.lst；HK output/|008,084|依 C_NORMAL/C_ATTACK/C_TAKE 等游標載入與 hover 目標，區分移動/攻擊/拾取|地板、玩家、怪、物品、技能、禁用狀態游標與原版一致
073|戰鬥數字與 MISS/防禦特效|SGOBattleSys|battle.cfg；HK Map/Animation/|006,084|依傷害類型與色彩素材；Server event 與 Client 顯示不重複|普通/技能傷害、MISS、Guard、自己及他人可見，時序一致
074|技能與武器特效|SGOSkillSys,SGOBattleSys|skill.cfg；HK Effect/|027,084|追方向/技能/命中素材動態路徑與 ACT/時序；不讓動作速度隨封包變化|單體/範圍、命中/未命中、雙 Client 一致，資源正確清理
075|世界地圖與傳送地圖介面|SGOMapService|map.cfg；HK output/Interface/BG_MAP_BIG_*.spr|008,045|追世界地圖各背景及傳點顯示來源，原版入口/可達性確認|合法地圖、不同區域選擇、點擊不錯位；無效地圖拒絕
076|原版 UI 素材共用載入與字型| |HK output/Interface/；Data1_PNG2；Data1_PNG|001,013,021,023,027|共用 ITF loader/control rect、frame/state、字型、baseline、pivot 與透明度；沿用既有 converter|代表性登入、HUD、背包、技能 A/B；不使用整張截圖假 UI
077|五個 C.D.E. ITF 解密與控件驗證| |HK output/Interface/；HK sgo_cde_decrypt_v*.py|006,013,021,097,098|用現有合法解包流程與 HK 反編譯取得解密結果、control→SPR 對應；不能猜候選|逐檔列明文校驗、引用檔、rect、frame、狀態；無證據則 NOT VERIFIED
078|未直接引用 ITF 與剩餘 SPR 可達性調查| |HK output/Interface/；SGOClient.exe.lst；Old Paradise.exe.lst| |搜尋動態 loader、版本分支、同家族變體、未配對素材與實機入口；分類 active/variant/dormant/unknown|所有 152 ITF 與 368 top-level SPR 均有清單；未知不標完成；不新增猜測功能
079|遊戲內資源生命週期與效能|SGOMapService,SGOPlayer|HK Player/；Enemy/；Effect/；原版 .lst|006,008,029|實際進圖、換圖、登出、重登與資源釋放；AFK/Unfocus 只改渲染|多次循環 RAM/VRAM/CPU/FPS 可比，可靠事件未丟，無資源洩漏
""".strip()
extra={
'014':['SGOBattleSys::OnPlayerAttack L5304 @1000461B','OnAttack L14898 @1000A91B','IsHit_PerHit L15825 @1000B238','CalHit_PerHit L19131 @1000D2AA','CalMiss_PerHit L22108 @1000EFEB'],
'015':['SGOBattleSys::CalWeaponDamage_PerHit L22917 @1000F7F9','CalFinalChange L23729 @10010012','CalHP_Final L25461 @100110F7','SendCharSyncPac L49758 @1002066E'],
'016':['SGOBattleSys::CalGold_Final L25417 @100110DD','CalExp_Final L25439 @100110EA'],
'017':['SGOEnemy::CheckDead L19741 @1000D7B1','CheckDropItem L25150 @10010D84'],
'072':['Paradise.exe.lst L42653 @0041A58C C_SELECT.spr；L42750 @0041A68D C_NORMAL.SPR；L42760 C_PLAYER.SPR；L42770 C_ATTACK.SPR；L42780 C_TAKE.SPR；L42790 C_CHECK.SPR；L42800 C_ITEM.SPR；L42810 C_SKILL.SPR（位置基於 Old 直接引用）'],
'073':['Paradise.exe.lst L365148 @004DD36A MISS_ORANGE.spr；L365161 MISS_BLUE.spr；L365174 MISS_PURPLE.spr；L365187 MISS.spr；L365280 GUARD.spr；L365420 HIT_POINT_ORANGE.spr；L365433 HIT_POINT_BLUE.spr；L365446 HIT_POINT_PURPLE.spr；L365459 hit_point.spr（HK Map/Animation 已確認檔案存在）'],
'074':['Paradise.exe.lst L301409 @004B7487 Effect/Weapon_Effect/%s.SPR；L302193 @004B7C9D Effect/Hit_Effect/%s.SPR；L303267 @004B87BF Effect/Normal_Effect/%s.spr；L303350–303435 Effect/Skill_Effect/{B,A,R}_%s8%s.spr（動態路徑，需追實際 ID）'],
}
for l in G.splitlines():
    i,t,p,d,ids,g,c=l.split('|');p=p.strip();ids=ids.strip(); prefixes=set(ids.split(',')) if ids else set()
    fn=f'GP-{i}.md'; phase='P1' if int(i) in range(5,32) or i in ('001','002','003','072','073','074','076') else ('P3' if int(i)>=60 and i not in ('072','073','074','075','076','079') else 'P2')
    links=[f"- [{x['title']}](../{x['path']})" for x in index if x['track']=='UI' and Path(x['path']).stem[3:6] in prefixes]
    links='\n'.join(links) or '- 無專屬 ITF 或待確認；不得捏造 UI。'
    ex='\n'.join('- '+v for v in extra.get(i,[])) or '- 從上述插件 ProcessPacket/ProcessMsg、實際 handler、資料讀取和 Client 對應事件繼續追；未解析的規則須標未知。'
    write(GAME/fn,f"""# GP-{i} — {t}

- 工作軌：Gameplay / 功能；狀態：NOT VERIFIED。
- 優先度：{phase}（P0 由實際 blocker 決定）。
- 目標：{g}。
- 範圍：本任務單一可玩切片；先確認現有實作，只補缺，必要時依本任務拆出更小子切片。

## 1. Old 反編譯及原始資料
{server_evidence(p)}
- 相關資料定位：{d}（檔案/目錄或名稱模式；尚未宣稱所有內容、表結構或公式均已解析）。
{ex}

## 2. 相關 UI / SPR 任務
{links}

## 3. 開發步驟
- [ ] 以正式 SGTest 玩家與真 Server 重現本功能或阻塞；記錄可玩/不通差異，確認沒有其他 Session 同時修改。
- [ ] 由上列反編譯、資料及原版可重現操作取得規則、事件/封包、狀態與錯誤條件；未知數值維持 fail-closed。
- [ ] 檢查現有 Server domain owner、Wire DTO、Client reducer、畫面事件及持久化；實作最小完整垂直切片：{g}。
- [ ] 接真實 UI/輸入（有對應 ITF 者遵循獨立 UI 文件），保留原版可觀察行為；不因非阻塞視覺差異停止核心玩法。
- [ ] 依風險補 targeted tests，使用隔離 DB/port，驗重複、失效狀態、錯誤、斷線、權限與多 Client（適用）。

## 4. 驗收
- [ ] 玩家流程：{c}。
- [ ] 使用正式可見 Windows Client + 真 Server；涉及多人用兩個非 headless Client；涉及持久化重登驗證。
- [ ] 若有 UI 變更，保留 HK Reference/Before/After、ROI/frame/字型/文字位置與實際 hitbox；未驗 UI 仍 NOT VERIFIED。
- [ ] 檢查相鄰功能及相關負向 case，更新 TODO/Feature Matrix 與證據；僅提交本切片，保護其他 Session。
""")
    index.append({'id':f'GP-{i}','track':'Gameplay','priority':phase,'title':t,'path':'gameplay/'+fn,'evidence':'Old 插件/資料定位；部分 handler 已定位，規則待驗'})
# Searchable tables: exact names, reverse mapping, direct vs unmatched.
with (R/'UI_SPR_MAPPING.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.writer(f);w.writerow(['task_id','itf','sprite','relationship','present_in_HK_output','Old_direct_reference_count','HK_direct_reference_count'])
    for u in D['ui']:
        for x in u['spr'] or ['']:
            w.writerow([path(u)[:-3],u['itf'],x,'ITF_STRING' if x else 'CDE_UNRESOLVED',assetset.get(x.casefold(),'') if x else '',len(u['old']),len(u['hk'])])
        if u['cde']:
            for x in candidates.get(u['itf'],[]):w.writerow([path(u)[:-3],u['itf'],x,'CANDIDATE_NOT_PROVEN',assetset.get(x.casefold(),''),len(u['old']),len(u['hk'])])
reverse=collections.defaultdict(list)
for u in D['ui']:
    for x in u['spr']:reverse[x.casefold()].append(path(u)[:-3])
with (R/'SPR_INVENTORY.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.writer(f);w.writerow(['HK_output_Interface_SPR','direct_ITF_task_ids','status'])
    for x in D['assets']:w.writerow([x,';'.join(reverse[x.casefold()]),'ITF_DIRECT_STRING' if reverse[x.casefold()] else 'NOT_MAPPED_STATIC_ITF_CHECK_DYNAMIC_OR_VERSION'])
with (R/'TASK_INDEX.csv').open('w',encoding='utf-8-sig',newline='') as f:
    w=csv.DictWriter(f,fieldnames=list(index[0]));w.writeheader();w.writerows(index)
# Backlog index sorted by family, and recommended gameplay/UI order.
UI_count=sum(x['track']=='UI' for x in index); GP_count=len(index)-UI_count
refs=sum(bool(x['old']) for x in D['ui']); hks=sum(bool(x['hk']) for x in D['ui']); enc=sum(x['cde'] for x in D['ui']); covered=sum(bool(reverse[x.casefold()]) for x in D['assets'])
intro=f"""# 星夢 SGTest：原版反編譯 UI / 功能獨立任務庫（2026-11-24）

## 使用方式
這是可執行工作拆分與來源定位，不是完成度報告。共 **{UI_count} 個獨立 UI 任務 + {GP_count} 個獨立功能任務 = {len(index)} 個 Markdown 任務**。每一個 ITF 包含變體，均有一份文件；每個核心功能也獨立一份。UI 文件包含 SPR 對照、Old/HK 反編譯地址、資料/插件、步驟與驗收；Gameplay 文件提供權威/資料/實機驗收與對應 UI 連結。

來源路徑（只讀）：
- Old = C:/SGO/Recovered/Old；客戶端 ClientData/Paradise.exe.lst；服務端 服務端/plugins/*.dll.lst、*.cfg、Dat、資料庫。
- HK = C:/SGO/Stargate Online IE HK；SGOClient.exe.lst；output/Interface；Data1_PNG2/Data1_PNG；HK 版佈局優先，Old 版行為與 Server 作參考。
- SGTest = C:/SGO/SRC/SGTest；遵從現有 AGENTS.md、docs/SCHEDULER_PROMPT.md、TODO 與 FEATURE_MATRIX；本目錄不替代唯一正式矩陣。

## 盤點範圍與可信度
- HK/output/Interface 直接子檔：{UI_count} ITF、{len(D['assets'])} SPR（不含遞迴子目錄/全遊戲世界素材）。
- {UI_count-enc} ITF 能抽取明文 SPR 名稱；{enc} 個 C.D.E. ITF 未解密，僅列已存在候選素材，需 GP-077。
- Old 反編譯定位 {refs} 個不同 ITF；HK .text 字串定位 {hks} 個。未找到直接引用 ≠ 不存在或未使用，可能透過動態載入/版本差異/同家族變體。GP-078 負責補齊可達性。
- {covered}/{len(D['assets'])} 個 top-level SPR 名稱出現在可讀 ITF；其餘保留反向清單，需查動態引用，不強行分配。
- Old Server 已確認插件名稱及部分 symbol 入口；未逐函式還原完整 opcode/公式/控制 ID/rect/frame/font。UI 精確映射僅限 ITF 明文 SPR 字串，不代表每個 SPR 的使用角色、幀、原版實機操作已驗證。
- 所有任務初始 NOT VERIFIED，優先度為建議，不是當前程式完成度，也不要求重做已驗證功能。原版程式與圖檔不納入本文件庫。

## Scheduler 選擇順序（建議；不得覆蓋 AGENTS.md）
1. 真 P0 優先，已可用登入不反覆微調：先 [正式戰鬥 profile](gameplay/GP-012.md) → [選怪距離](gameplay/GP-013.md) → [普攻命中](gameplay/GP-014.md) → [受擊傷害](gameplay/GP-015.md) → [擊殺 EXP](gameplay/GP-016.md) → [掉落](gameplay/GP-017.md) → [拾取](gameplay/GP-018.md)。
2. 技能 GP-020～023、物品 GP-024～028、商店/倉庫 GP-029～037、多人 GP-038～044，依現有矩陣挑未完成切片。每三個有效非緊急交付至少兩個 gameplay、一個遊戲內 UI。
3. UI 優先 HUD、人物資料、技能、背包、裝備、NPC；登入、選角、創角只有真 blocker/使用者指定/合理 UI 輪才修。
4. 任務文件個別使用：讀所選文件 → 查對應反編譯/ITF → 最短可玩閉環 → 實機驗收 → 更新既有矩陣。未知/不可達 UI 標證據待補，不阻塞其他玩法。

## 快速入口
- [登入畫面](ui/UI-001_ACCOUNT_LOGIN.md)
- [選擇角色](ui/UI-004_SELECT_CHARACTER.md)
- [建立角色](ui/UI-005_CREATE_CHARACTER.md)
- [角色詳細資料](ui/UI-013_Character_INFO.md)
- [背包](ui/UI-023_ITEM.md)
- [裝備](ui/UI-022_equip.md)
- [技能](ui/UI-027_Skill.md)
- [商店](ui/UI-031_SHOP.md)
- [任務清單](ui/UI-097_Quest_List.md)
- [完整 ITF→SPR 對照](UI_SPR_MAPPING.csv) · [全部 SPR 反向清單](SPR_INVENTORY.csv) · [任務索引 CSV](TASK_INDEX.csv)
- [來源盤點 JSON](source_inventory.json)（直接 ITF 字串、行號/地址、原名）

## Gameplay 任務（獨立文件）
"""
intro+='\n'.join(f"- [{x['id']} {x['title']}]({x['path']}) — {x['priority']}" for x in index if x['track']=='Gameplay')
intro+='\n\n## UI 任務（每個 ITF 各一份）\n'+'\n'.join(f"- [{x['id']} {x['title']}]({x['path']}) — {x['priority']} / {x['evidence']}" for x in index if x['track']=='UI')
write(R/'README.md',intro)
assert len(list(UI.glob('*.md')))==UI_count==152
assert len(list(GAME.glob('*.md')))==GP_count==79
assert len(set(x['id'] for x in index))==len(index)
for x in index:
    assert (R/x['path']).is_file()
for p in R.rglob('*.md'):
    for href in re.findall(r'\]\(([^)]+[.]md)\)',p.read_text(encoding='utf-8')):
        assert (p.parent/href).is_file(),(p,href)
print(f'VERIFIED files: UI={UI_count} gameplay={GP_count} tasks={len(index)} ITF={len(D["ui"])} SPR={len(D["assets"])} mapped={covered} Old={refs} HK={hks} CDE={enc}')
