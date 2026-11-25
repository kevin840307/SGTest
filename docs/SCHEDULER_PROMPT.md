# SGTest Scheduler v17 — 原版對齊、完成可玩功能

C:\SGO\SRC\SGTest，唯一 main。立即修改正式 Client／Server，不只規劃、研究、寫文件或增加測試。先讀 AGENTS.md（完整來源／安全／驗收規則）、todo.txt ACTIVE、FEATURE_MATRIX 的相關 OPEN；已好的登入／創角／選角只做回歸。

**目前先對齊登入後浮動 UI：每個畫面都要原版外觀＋文字位置＋實際功能。**
1. 聊天：輸入、頻道切換、歷史、本人與其他玩家頭頂氣泡。
2. 好友／社交／組隊／社團：頁籤、好友增刪、黑名單、線上狀態、邀請、社團建立/加入/管理/成員等。
3. HUD：HP、MP、SP／WP、金錢、等級、經驗值與即時變化。
4. 快捷列：原圖、文字、位置、長短/分頁、技能/物品拖放與 F1–F12 真使用。
5. 右側全部選單：背包、人物資料/裝備及動態紙娃娃、機甲、玩家小商店、社團、系統登出/音效/設定；NPC 對話、買賣、銀行/倉庫/修理、交易等也要做。修第二次密碼、快捷列、背包與其他文字錯位。
6. **Server P0：** 修連續換圖兩次卡住（Map15↔13 至少10次）；正式可見怪物能追擊、普攻、MISS、死亡、EXP、掉落/重生；學習/施放技能、MP、冷卻、效果和多人同步。零怪物/零技能規則的 Server 不算可玩。

**盡量先查原版反編譯再動手：** docs/ORIGINAL_REFERENCE_INDEX.md、Paradise ScreenShot、HK 的 ITF/SPR/PNG/.lst、UI_CONTROL_METADATA.json／SPR_FRAME_METADATA.json、C:\SGO\Recovered\Old（Client/Server .lst、Dat、cfg）、C:\SGO\Recovered、Recovered2；完整路徑與版本優先序在 AGENTS.md。比對 Control rect、繁中文字、字型、frame、handler、封包與數值，不猜、不用自畫圖取代原圖；缺截圖也要從反編譯補功能。所有原始參考與現有測試/證據保留唯讀，不提交私有素材。

**每輪至少完成一個可操作流程：** 真輸入→Server 權威→回傳→UI/動畫→適用重登/雙 Client；未通繼續同一功能，不隨機挑易做小修。保護其他 Session WIP/DB/port/程序；背景編譯、headless 與隔離測試，不搶使用者前台鍵鼠。真 UI 只能在安全獨立桌面驗，無法驗即 NOT VERIFIED，仍繼續開發。驗收後僅提交本輪檔案、push main/fetch 0/0；簡報玩家新功能、未通項、測試、SHA、下一個 blocker。
