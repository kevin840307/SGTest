# SGTest Scheduler v16 — Playable First / HK Parity

立即實際開發。正式目錄 C:\SGO\SRC\SGTest；GitHub https://github.com/kevin840307/SGTest；唯一正式分支 main。
先讀最新版 AGENTS.md（唯一正式規範）、todo.txt 的 ACTIVE MILESTONE、docs/FEATURE_MATRIX.md 及上輪未驗事項；v16 取代舊規則。

登入、創角、選角已由使用者確認完成，除非回歸不再重做。第二次密碼錯位、連續換圖卡住、沒有怪物/普攻/技能、聊天無氣泡/頻道與好友未通、快捷鍵及包包錯位、物品/裝備動畫/機甲缺失皆為待驗修正。

按 AGENTS.md §1 最早未通里程碑推進：統一可玩測試環境→連續換圖/怪物/普攻/技能/掉落→背包/裝備/快捷鍵→完整 UI/聊天/好友→寵物/機甲→多人同步效能。使用者指定及 P0 優先。先核對真 Server capability、角色 combat profile、verified population/manifest/runtime；不能用 0 怪物/0 技能規則的 listening Server 冒充可玩。缺資料就追 原版→converter→runtime→啟動→可見玩家流程，直到直接依賴打通。

每輪至少一個玩家可觀察成果（或實質解除最早核心 blocker 並保留未完成接續），完成繼續下一直接依賴。禁止自由挑無關小修、只堆測試/解析/空殼 UI 或用 fixture/headless 代替玩家實測。原版外觀、文字、座標、功能、資料、動畫同時驗；已知 FAIL 不被舊 PASS 覆蓋。

每個畫面先實際開啟 docs/ORIGINAL_REFERENCE_INDEX.md 對應本機原圖；缺圖/狀態（社團、玩家小商店、NPC 買賣/交易等）依 AGENTS §3 原版正常操作補圖或 ITF/SPR/PNG/.lst + Server 來源補證，繼續實作；不能以缺圖跳過，也不能只做圖片。每個可達控件驗原版事件、條件、真操作→權威結果→UI/動畫及適用雙 Client/重登，空 callback 不算完成。

遵守 AGENTS.md 的原版來源、權威、Git/WIP/清理安全與五 Gate；真 Server + 可見 Windows Godot + 真鍵鼠；使用者當前測試程序/DB 保護，測試使用同設定隔離環境。更新 TODO/Matrix 的 ACTIVE MILESTONE、未完成及下一具體動作；明確提交 push main、fetch 驗 0/0。回報玩家成果、未通功能、capability、下一 blocker、SHA 和停止原因，不湊時間。
