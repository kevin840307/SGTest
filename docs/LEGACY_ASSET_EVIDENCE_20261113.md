# 原版素材格式、載入呼叫鏈與來源缺口調查（2026-11-13）

> 證據基準：SGTest/main \`bd652f22b0955cd74e63d6061e999c35a3779a4d\`；**唯讀** TestGoGo \`feature/local-server-net8\`，提交 \`0f37c5268ceeac1144ed13556f3aeb191b93711e\`。反編譯檔 \`Old/ClientData/Paradise.exe.lst\`：Git blob \`16710727227dc5ff8d623ea2acab17cc468fc129\`，大小 40,117,423 bytes。直接讀取該 blob 並定位組語、實際抓取四個 SPR 與一個 SIF 位元組資料。未取得使用者本機完整安裝檔，**沒有進行原版執行畫面比對**。

## 1. 結論及證據分級

- **DIRECT（原版組語／二進位直接可見）**：舊版 \`Paradise.exe\` 實作 \`xnFileRead\`（先鬆散檔、後群組資料檢索）、\`xnSpr::LoadSpr\`（尾端影格數與偏移表）、\`DecodeFrameLine\`（透明跳過／實色／半透明像素串列）、\`LzssDecompress\`。角色顯示會依角色類型、動作、方向、裝備部件組合檔名與資源。UI 會載入 \`.itf\`；可看到大量 \`.spr\`、\`.act\` 配對引用。
- **DIRECT（Repo 樣本）**：四個 NPC SPR 樣本皆符合「影格資料＋檔尾影格索引」結構；所測掃描列命令皆完整走訪。NPC203.sif 記錄檔名與四個整數，整數與同名 SPR 首影格標頭對應。
- **HYPOTHESIS / NOT VERIFIED**：16-bit 像素對映為 RGB565 可作驗證用近似，尚未使用原版畫面、原始渲染像素或真機色彩配置校驗。人物方向數、影格時長、動作順序、座標原點／pivot、Z 層序、裝備疊圖、完整 \`.act\` 格式、所有 \`.itd/.itf\` 欄位、封裝副檔名與後期金色 UI 檔案來源均未證實。
- **BLOCKED**：原版後期正式 Client 安裝包、完整 Player/Effect/Interface/Map 圖像庫、\`.act\` 與 \`.itf\` 真實檔案、對照影片及資產發行授權均缺少。不能使用 NPC 單影格解碼當作角色動畫完成的證據。

### 版本警示

\`Paradise.exe.lst\` 開頭顯示 IDA 反編譯輸入路徑 \`...\\SGO Paradise\\program\\Paradise.exe\`、PE x86、時間戳 2004-11-24，不能因此假定與「後期《星夢 Online》金黃色 UI」同版本。原版同名副檔名未必跨年代相容。

## 2. 直接實測原始檔案與 SPR 結構

| TestGoGo 原始路徑 | Git Blob SHA | 長度 | 結構性驗證 |
| --- | --- | ---: | --- |
| \`Old/ClientData/NPC/NPC203.spr\` | \`980d15e03764e375ec14f91caa2e461e4a212e48\` | 12014 | 53×109；109/109 列走訪、無不明 token |
| \`Old/ClientData/NPC/NPC204.spr\` | \`0169f16d0cfa6b32d594f221e419e5dcfc30435c\` | 11572 | 53×99；99/99 列走訪、無不明 token |
| \`Old/ClientData/NPC/NPC251.spr\` | \`838c63e8591797f8b028771b375dfb87cb95fdbc\` | 56652 | 178×322；322/322 列走訪、無不明 token |
| \`Old/ClientData/NPC/NPC207-R.spr\` | \`34bb6fca7ad7e9d3d9a05668b0decf912d2f1edd\` | 10768 | 52×97；97/97 列走訪、無不明 token |
| \`Old/ClientData/NPC/NPC203.sif\` | \`3247e84e7ae5865fe727c3c7fe58cd5639aa0d26\` | 28 | 包含 \`NPC203.spr\` 路徑與 \`21,89,53,109\` 四整數 |

**所測 XN SPR 變體結構（小端序）**：

| Offset | 欄位 | 可信度 |
| --- | --- | --- |
| \`file_size - 4\` | \`uint32 frame_count\`（四個樣本均為 1） | DIRECT，亦見 0x00552FFF |
| \`file_size - 4 - 4*frame_count\` | 每影格 \`uint32 frame_offset\` 陣列，指向檔案內原始影格 | DIRECT，亦見 0x00553006-0x0055307B |
| \`frame_start + 0, +4\` | \`uint32 width, height\` | DIRECT，尺寸與 4×列索引驗算相符 |
| \`frame_start + 8, +12\` | 兩個 \`int32\` 原始位置相關數值（目前稱 raw_anchor_x/y；語意未驗證） | DIRECT 值、HYPOTHESIS 名稱 |
| \`frame_start + 16\` | \`height\` 個 \`uint32\`，每列相對 frame_start 的 row_offset | DIRECT，亦見 0x00553ED6 |
| \`row_offset\` | 16-bit 命令與像素資料，必須在下一列開頭前結束 | DIRECT，見下表 |

**掃描列 token 的觀察（保留其原始值）**：

| \`command & 0xC000\` | 行為 | 載荷 |
| --- | --- | --- |
| \`0x8000\` | 跳過透明像素 \`command & 0x3FFF\`；\`0x8000\` 恰好代表本列結束 | 無 |
| \`0x0000\` | 直接拷貝 \`length\` 個 16-bit 色彩像素；不含顯式 Alpha | \`length * 2\` bytes |
| \`0x4000\` | 讀 16-bit 局部 Alpha，接著 \`length\` 個 16-bit 色彩像素 | \`2 + length*2\` bytes |
| \`0xC000\` | 未由樣本與程式流程證明，嚴格解析器拒收 | 不推測 |

對應 \`DecodeFrameLine\` @ \`0x00553EB0\` 的邏輯：比較 \`0x8000\`、\`0x4000\`，將資料複製到像素／Alpha 緩衝區；帶透明度的 run 以輸入 Alpha 與呼叫時的整體不透明度計算。工具只保留檔案的原始局部 Alpha、預設不套整體不透明度；**此步為素材獨立資料解碼，不等於確認原版最終混合公式**。

**\`.sif\` 已知樣本**：28 bytes：\`0x0A\` + ASCII \`NPC203.spr\` + \`0x01\` + LE32 \`21,89,53,109\`。前兩個值與 SPR 第三／第四個整數相同，後兩個值與寬高相同。合理推測它是圖像描述／裁切、放置位置索引；**第一個位元組、\`0x01\` 的實際型別及欄位順序仍需對照 \`CELBldg2D\` 讀取組語與其他檔案。** 不能把 \`.sif\` 說成影格播放速度檔。

現有工具 \`tools/legacy_spr_decoder.py\`：僅支持前述 XN footer row-run 變體，採 fail-closed。只在輸出資料夾為空、且位於來源目錄之外時產生 PNG；不會寫回素材來源。JSON 明示 \`animation_verified:false\`，未證明的 \`direction/action/duration_ms/offset/z_order/events\` 一律 \`null\`，保留兩個原始位置整數。不能將 \`PROVISIONAL_RGB565_NOT_VISUALLY_VERIFIED\` 宣稱為已完成保真。

## 3. 真正的載入／解包／解碼／動畫呼叫鏈（.lst 直接定位）

| 類別 | 函式／關鍵位址 | 組語提供的觀察 |
| --- | --- | --- |
| 讀檔入口 | \`xnFileRead\` \`0x00552090\`；\`0x00552128\`、\`0x0055219A\` | 優先使用 \`fopen/fread\` 讀取鬆散檔案 |
| 讀封裝候補 | \`xnFileRead\` \`0x0055223E..0x005522D3\` | \`GetFolder\`、\`xnList::Search\`、\`FileData::ReadBuf\` |
| 開群組 | \`CELFileIO::CELFileOpenGroup\` \`0x00506BC0\` → \`xnFileOpenGroup\` \`0x00552980\` | 群組路徑存在；不等於 Git repo 有相關封包 |
| 解壓 | \`xnFileOpenGroup\` \`0x00552A5B\`、\`GroupData::Open\` \`0x00551C3D\`、\`FileData::ReadBuf\` \`0x00551886\` → \`LzssDecompress\` \`0x0055A0E0\` | 群組索引或檔案資料會呼叫 LZSS 解壓；外部包副檔名及實際資料尚未測定 |
| SPR 資源入口 | \`CELXnLib::LoadSprite\` \`0x00542BD4\`，call \`xnSpr::LoadSpr\` \`0x00542BE7\` | 可先載完整路徑，必要時嘗試檔名補救 |
| SPR 索引修正 | \`xnSpr::LoadSpr\` \`0x00552FC0\` → \`xnFileRead\` \`0x00552FE0\`；尾數 \`0x00552FFF\`；位移指標 \`0x00553006-0x005530A9\` | 實際資料末尾影格數＋影格 offset，再修正每一列 offset |
| SPR 特殊版本 | \`xnSpr::LoadSpr\` \`0x0055302D-0x00553055\`、\`CELXnLib::LoadSprite\` \`0x00542CA0-0x00542CB9\` | 檢查 \`Ver\` 版本標記／version==8 後複製 sprite；尚未收集該版本樣本 |
| 逐列繪製 | \`DecodeFrameLine\` \`0x00553EB0\`；\`xnSpr::Draw\` \`0x00553FB0\` | 逐列色彩及 Alpha 緩衝區產生後繪製；不能從此推定 GPU cache 生命週期 |
| 顏色轉換 | \`CELXnLib::Convert\` \`0x00542CD0\` → \`ConvertFrame\` \`0x00542D55\` → \`ConvertBuffer\` \`0x00542E1F\` | 能依條件轉換 16-bit 色彩；RGB565 對照仍未完整驗證 |
| 建築／靜態物件描述 | \`CELBldg2D\` \`0x005485BF\` 中 \`0x0054882A, 0x00548A4E\` 將副檔名換成 \`.sif\`；\`CELStaticObj2D\` \`0x0054A172\` 中 \`0x0054A2DA\` | 原版確實會讀取獨立 SIF，並非只由 SPR 頭推算畫面 |
| 靜態物件動作 | \`CELStaticObj2D\` \`0x0054A4C6\` 組合 \`.act\` → \`CELDrawStaticObjLogic2D::InitAction\` \`0x005514C7\` | 動作描述與圖像載入分離；ACT 內部欄位未驗證 |
| UI 格式 | \`CConnectingItf::Create\` \`0x00425ABE\` 使用 \`011_LOGIN_LOADING.itf\`；\`CELInterface::Create\` \`0x0051E972\` → \`CELFileIO::CELFileRead\` \`0x0051E9F5\` | \`.itf\` 是原版介面讀入檔，缺真檔不能推稱圖片資源／壓縮格式 |
| ITD 資料 | \`0x0040371E\` 路徑 \`Item\\petcli.ITD\`，\`0x00404D92\` 路徑 \`Item\\item.itd\` | \`.itd\` 是物品／寵物相關資料輸入，不等於動作與顯示影格圖 |

**人物方向、動作與紙娃娃路徑（DIRECT 函式及路徑、細部語意部分未驗）**：

\`CSGOChar::InitActDisplay\` \`0x004C1143\` → \`CSGOCharDrawLogic::InitActDisplay\`；\`CSGOCharDrawLogic::InitAction\` \`0x004C4C95\` 依情況呼叫 \`InitNormalAction\` \`0x004C4339\`、\`InitRidingAction\` \`0x004C4A50\`、\`InitArmorAction\` \`0x004C4BB0\`。正常動作會呼叫 \`DirectionMapping\` \`0x004C263F\`（呼叫點 0x004C4746），\`SetMotionDraw\` \`0x004C2D1D\`（呼叫點 0x004C47AA）。\`SetDirection\` \`0x004C24EA\` 呼叫 \`DirectionMapping\`、\`SetMotionDraw\`、\`DrawOrderMapping\`；裝備會從 \`SmartLoadParts\` \`0x004C30C8\`、\`SmartLoadWeapon\` \`0x004C2E42\` 組裝路徑 \`Player\\%s\\%s\\%s\` 與 \`%s%s%d.spr\`。武器效果亦參照 \`Effect\\Weapon_Effect\\... .spr/.ACT\`。游標 \`CSGOUim::RunMapCursorEffect\` \`0x0041A61C\` 以 \`C_NORMAL.ACT\` / \`C_NORMAL.SPR\` 等配對載入。這能證實檔名由動作及部件條件組合，**尚不能推導出所有實際方向 ID、每方向影格數或時長**。

## 4. 來源可用性／缺漏矩陣（AS003 的現階段盤點）

參考倉目前 \`Old/ClientData\` 的 Git tree：**199 SPR、12 SIF、4 ITD、10 DAT、3 CSV、1 LST；零 ITF、零 ACT**。它只是原版資料的零散子集，非完整 Client 安裝檔。

| 類別 | 已有目錄／依據 | 缺件／可用結論 | 等級 |
| --- | --- | --- | --- |
| 人物、職業、方向、站立／走／攻／受擊／死亡 | 組語 \`Player\\%s\\%s\\%s\` 和動作／方向對映呼叫鏈 | Old/ClientData 無完整 \`Player\` 圖像及 ACT，無法重現角色 | DIRECT 路徑／BLOCKED 實測 |
| 武器、裝備紙娃娃疊圖 | \`SmartLoadParts\`、\`SmartLoadWeapon\`、\`DrawOrderMapping\` | 缺角色裝備 SPR/ACT 及實際疊圖對照 | DIRECT 路徑／BLOCKED |
| NPC | \`Old/ClientData/NPC/\` 有少量 SPR/SIF；四份 SPR 結構驗證 | 僅部分靜態樣本，不能推為全 NPC 動畫 | DIRECT |
| 怪物 | \`Old/ClientData/Enemy/EnemyTable.dat\` | 沒有完整 Enemy SPR/ACT，缺原版怪物多方向動畫 | DIRECT 目錄／BLOCKED |
| 機甲／寵物 | \`Old/ClientData/MS/Horse*.csv\`、\`Item/petcli.ITD\` | 只有資料索引，缺原件圖像／動作表 | DIRECT 目錄／BLOCKED |
| 技能、武器／受擊特效 | \`Old/ClientData/Interface/skill*.dat\`；\`Effect\\Weapon_Effect\\...\` 路徑 | 缺大量 Effect SPR/ACT 與顯示時序 | DIRECT 路徑／BLOCKED |
| 物品 | \`Old/ClientData/Item/Item*.itd\` | 資料表存在，物品 icon 原圖／ITD schema 尚未證實 | DIRECT 目錄／UNVERIFIED |
| 地圖／建築 | 服務端 \`Old/服務端/Dat/Map/Map13\` 有 Blocking/NPC/Enemy DAT；Client 建築 SIF 呼叫 | 地圖畫面 tiles/chunks/建築美術缺件，Blocking 不是渲染貼圖 | DIRECT 目錄／BLOCKED |
| 後期金黃色 UI | 原版反編譯可見多個 \`.itf\` 檔名；\`Interface\\...spr\` | Git 無 \`.itf\` 實檔及後期美術；2004 客戶端未證實是目標版本 | DIRECT 檔名字串／BLOCKED |
| 音效／BGM | 使用者尚未提供完整安裝資料 | 對應音檔及第三方權利資訊缺失 | BLOCKED |
| 封裝／更新檔 | 原版 \`xnFileOpenGroup\`、\`LzssDecompress\` 的函式存在 | Git 中未找到可驗證的正式 Client archive；封包命名與目錄待查 | DIRECT 程式／BLOCKED 檔案 |

## 5. 程式、測試與可重現操作

在 SGTest 有：

- \`tools/legacy_asset_inventory.py\`：新增 \`header_hex\` 與 \`--header-bytes\`（0～64，預設 16），保留 SHA256 串流摘要，報表僅相對路徑；\`schema_version=2\`。與原檔分開輸出，略過 symlink。**本機完整安裝目錄未提供，因此無法填入實際全檔案 SHA256 或版本。**
- \`tools/legacy_spr_decoder.py\`：\`parse_spr\` 嚴格讀取已確認 XN 變體、保留 raw anchor、輸出逐影格 RGBA PNG 與 \`animation.json\`。拒收未知 token、損壞 row/offset、溢出、覆寫既有輸出；未知動畫欄位 \`null\`。
- \`tools/legacy_asset_viewer.py\`：獨立 Tkinter 預覽 PNG + JSON；方向／動作（有欄位才可選）、上一格、下一格、暫時播放、顯示 raw anchor/offset/z-order/hash。**當原始幀時間未知時使用畫面預覽 100ms，絕不可回寫成原版速率；GUI 未在有桌面的環境逐項人工驗收。**
- \`tools/tests/test_legacy_spr_decoder.py\`、\`test_legacy_asset_viewer.py\`、\`test_legacy_asset_inventory.py\`：僅自製 fixture，**GitHub 不包含任何原版藝術資料**。

\`\`\`powershell
# 只讀盤點你的合法持有、具備完整檔案的遊戲安裝路徑
python tools/legacy_asset_inventory.py --root "C:\SGO\OriginalClient" --output reports/legacy-assets.json --sha256 --header-bytes 32

# 僅在你的本機使用真實 SPR；輸出必須在原件目錄之外
python tools/legacy_spr_decoder.py --input "C:\SGO\OriginalClient\NPC\NPC203.spr" --output "C:\SGO\reports\npc203"

# 會需要顯示器以及 Python Tkinter；只預覽，不推論動作或原版時長
python tools/legacy_asset_viewer.py "C:\SGO\reports\npc203\animation.json"

python -m unittest discover -s tools/tests -p "test_*.py" -v
\`\`\`

測試層級嚴格區分：已實際在 Python 本機執行自製 SPR 格式、PNG、負面路徑及 JSON 篩選 **6 tests PASS**；對上述四個真 SPR 做逐列結構解析 **109+99+322+97 列均未碰到未識別 token**（不等於視覺驗證）。\`inventory\` 修改後回歸、GitHub Actions、GUI 真機、原版角色完整動畫、與原版遊戲截圖逐格對照：除非後續另有明確 CI/測試紀錄，一律 **NOT VERIFIED**。

## 6. 後續 Session 直接接手（依 AS 順序）

1. **AS001**：在使用者合法持有的原版後期完整安裝目錄與更新檔執行上方 inventory；記錄每個版本 manifest SHA256、相對路徑、magic bytes，嚴禁上傳原件。對照此報告的來源 repo tree，找實際 Player/Effect/Interface/Map、ACT、ITF、封包與版本資訊。
2. **AS002**：以位址 \`0x00552090\`、\`0x00551B60\`、\`0x0055A0E0\` 深入分析封包格式及路徑，補群組檔頭樣本；深查 \`CSGOCharDrawLogic::InitNormalAction\`、\`SetDirection\`、\`DirectionMapping\`、\`DrawOrderMapping\`、\`CELDrawStaticObjLogic2D::InitAction\`，還原真正動作/方向/時長/疊層。
3. **AS003**：依來源版號補齊各類原件檔與缺件，特別是人物完整方向×五動作、機甲、金黃色 UI。
4. **AS004/AS005**：先驗證 RGB565 及局部 Alpha 與原版畫面完全一致；以實際至少一套完整人物 SPR+ACT+裝備建立多影格、動作、方向、pivot、z-order、時長、事件 PoC；未證實欄位不能亂填。
5. **AS006**：Viewer 加上 anchor 可視化、校正座標、真實時長、圖層；使用錄影逐格比對。再談 batch export、Godot contract / performance（AS007）。
6. **AS008/AS009**：授權未確認維持 BLOCKED；只有來源清單及可重現 tests/証據齊備才決定是否通過 M0 Gate。不要先建立正式 Client／Server／Atlas。

**重要：本文件只是本輪可實證的進度，不代表 AS001～AS009 已完成或 M0 Gate 已通過。**
