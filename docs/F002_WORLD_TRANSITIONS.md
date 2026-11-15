# F002：地圖／NPC／傳點執行順序與證據

2026-11-15。完整F002仍未完成；不把離線內容解析當成可遊玩切圖。Server持續C#／.NET8，Client render30～90FPS與input30Hz／Server30Hz／snapshot10Hz獨立契約不變。

## 現況與首個交付

Server `Program.cs`只載一張碰撞圖與population，`WorldHost`擁有單一World；WorldReady只比對mapId／fingerprint／populationHash，沒有MapEpoch。Client雖有world_generation取消素材，但沒有真正跨圖協定。不能先讓Client自行改map或座標冒充Server傳送。

**F002A首個交付：** 在asset_converter內提供可重跑NPC／傳點來源解析、私有normalized JSON、來源hash與自製fixture。Map13的三個傳點腳本都是TS_*，沒有_to_；依檔名猜測會漏掉。輸出只留`local-assets/asset_converter/world/`，尚未接消費端前不塞進Client常駐素材或當作Server已可用功能。

```powershell
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --map 13
# 已有完整抽取cache時：
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --map 13
# 所有目前存在NPC.dat的地圖；不將缺檔猜成空資料：
python tools/asset_converter/cli.py world-content 'C:/SGO/Stargate Online IE HK2' --extracted-source .cache/asset_converter/source --all-maps
```

`--server-dat`可指定原版服務端Dat；未指定時沿用現有唯一Recovered/Old/Dat resolver。圖片／外觀表從原始Client的Data1＋loose抽取；NPC出生與腳本來自原版服務端資料，不能從Client貼圖猜測權威傳點。預設JSON與原始腳本文字／名稱不公開；Git只留工具、人工fixture、hash及摘要。

### F002A實際驗收

原始路徑已完成Data1＋loose抽取與Map13內容轉換，報告`reports/world_content_original_map13.json`；抽取摘要`world_content_source_extraction.json`保留Data1 SHA／41687 archive entries及loose overlay計數。其後以目前程式與同抽取結果重跑全量NPC內容，Map13 fingerprint完全相同：`c9a4fe9aa9dc61a74c2508dbcad1a8d81460b453288ac9096df3d6ae065e266a`。

全量範圍為目前服務端Dat中存在的24份NPC.dat：原始header合計275筆、輸出275筆，0列遺失，275筆primary SPR均可解析。49筆符合已審確認式TS模板且來源／目的地可行走；226筆保留script_required，沒有強制改成簡單傳點。Map41／46有目錄但沒有NPC.dat，明列未知，不假裝已完成其NPC資料。本次275筆引用的primary SPR均為單影格；完整NPC時間／方向欄位語意仍未驗證，不憑此推導未來動畫規則。

`reports/world_content_all.json`含逐map結果、來源hash、converter module SHA與執行期間檔案未變動證據；使用已有抽取快取的內容編譯1.241秒，不能稱為冷抽取或Runtime載入時間。首輪原始抽取未記錄獨立elapsed，不拿來比較效能。完整NPC名稱／腳本／座標／資料JSON只在local-assets，不公開。

驗證：`python -m unittest discover -s tools/tests -q` 121 PASS，包含8個新NPC/CLI/原版目錄fixture；`tools/test_asset_pipeline.py` 5 PASS；`tools/test_maps.py` 3 PASS／1 private fixture SKIPPED。CDE自製fixture驗證共用decoder委派，真原始CDE表569列驗收則由此次原始來源執行證明，不混稱為自製加密fixture。F002B～E及完整任務／對話／Server transfer依然未完成。

## 來源契約

唯讀參考Recovered `feature/local-server-net8` SHA `0662e491ed6cdc46f047380aa063363fae0ce190`：

- `server/Infrastructure/GameData/NpcDatReader.cs::Read`：count:i32、每列type:i32／x:i32／y:i32／reserved:u8／script:NUL ASCII／name:NUL Big5。新reader加上完整長度、計數／字串上限、encoding與EOF檢查，不把截斷資料讀成空字串。reserved與name原bytes仍保留，不解讀未知位元。
- `server/Infrastructure/GameData/LegacyNpcVisualCatalog.cs`提供六欄NUL表的參考。實際原始`npc/npc_table.dat`是C.D.E.包裝，必須共用現有unwrap decoder後讀取；本機解出569列×6欄。所有六個source slots保留；只將已存在的primary SPR標為resolved。NPC動作時間與方向欄位語意尚未實證，明列false，不假裝八方向或沿用怪物方向政策。
- `server/Infrastructure/Quest/QuestRuntime.cs`的`MovePlayer(x,y,mapId)`及include preprocessing提供呼叫語意參考。Map13三份TS_*主檔的GetLoc為常數，`TS_Common.txt` SHA256 `8c972667f520ef00441f2b12c5a16fa1475a2e0a5ce1dd20e16bb79e37697562`已直接閱讀：start→CanMove→Ask確認→choice0才呼叫GetLoc／MovePlayer。這不是將任意Lua中的第一個MovePlayer當成有效傳點。

轉換只接受該已審模板hash與完整靜態入口形狀。主檔若停用入口、加入條件／額外函式、動態座標或未知include，就保留blocked／script_required／unavailable，不能自動開啟。註解與字串不會建立虛假的GetLoc；包含Lua等號長註解。這是保守的舊格式adapter，沒有執行Lua；F013完整任務／對話引擎仍待開發。

位置維持原始world pixel整數；以原版Blocking grid的16px cell檢查來源／目的地bounds與低3bits可行走性。目的地阻擋、缺圖、越界會保留原值並標不可用，不能離線擅改座標。NPC本身不是行走實體，來源position_status僅保留稽核資訊，不因站在阻擋格就擅自新增腳本進入限制。未來Server仍須在傳送時重新檢查當前地圖狀態，不能只相信此離線結果。

`spawn_id=source_index+1`僅在同map及同content fingerprint範圍有效，重建不任意重排；未來wire必須帶MapEpoch／content fence，不能跨版本用裸spawn_id。normalized內容與來源hash使用確定性fingerprint，不含主機絕對路徑與時間戳。

原版服務端低編號目錄使用Map07／Map08／Map09；population先前只找Map7。共用`build_maps.server_map_directory`同時供NPC／Enemy使用，只有一個合法候選才接受；Map7與Map07同時存在時fail-closed，不偷偷選一份。

## 後續實作順序

1. **F002A內容／fixture。** 原始路徑可重跑、Map13三筆資料與24份NPC.dat覆蓋稽核、未解腳本／視覺／目的地分類。逐項保留失敗；未測列NOT VERIFIED。
2. **F002B Server MapCatalog及transfer domain fixture。** immutable map定義與碰撞資料按需載入；每map一個共用World，不隨Session複製AI。先用自製雙圖與阻擋格證明權威transfer、原圖離開／新圖進入、共享怪物／掉落、失敗不半轉移。磁碟／重型decode不能卡30Hz世界迴圈。
3. **F002C協定與時鐘。** 在改production前建立wire fixture，為世界訊息／輸入／互動／WorldReady增加MapEpoch及版本邊界；舊圖input、ack、action不能污染新圖或同圖重入。多map需共用明確Server單調時間基準，不把各新World從0開始的time拿來混用既有Pong／插值時鐘。
4. **F002D Client真正loading流程。** 登入→選角→入圖／切圖進度由不可變狀態呈現；network／fixed logic獨立持續。先解除舊map owner／replica／prediction，再按新epoch預載並驗hash；完成後才WorldReady。Session紙娃娃跨圖共用、登出確實釋放。恢復畫面直接呈現當前狀態，不補播舊圖事件。
5. **F002E真資料雙圖E2E。** Map13與已驗證目的地、兩位觀察者、傳點距離／確認／拒絕條件、連續切圖／重連／中途登出、舊封包晚到；native畫面與30次資源回收。最後重跑同20連線／300怪／1000掉落／1440×900基準及受影響FPS隔離矩陣，再宣告F002完成。

現有SPEC資源／frame門檻維持不變：一般frame P95≤25／P99≤40ms且相同場景不退化>10%，熱cache map素材ready≤1秒，GPU submit P99≤8ms，正常release峰值RSS≤200MiB；切圖失敗不可犧牲其他map的30Hz模擬或10Hz snapshot。本階段沒有完成上述Server／Client transfer或完整NPC動畫，不能以離線PASS替代。
