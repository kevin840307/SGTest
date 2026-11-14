# 開發進度（2026-11-14）

目前工作位置 `C:\SGO\SRC\SGTest`；直接同步 main，Godot Client 位於 client、C#/.NET 8 Server 位於 server。使用者指定合入 master；完整遊戲目標仍在進行。

## 已有實測

- 原版 HK2 Data1＋loose overlay 可重建素材；75,591 SPR／282,990 影格像素回建通過，方向區塊與 alias 保留。HK 與 HK2 的提取輸入逐檔 hash 一致。
- C# Server 權威移動、碰撞、雙向戰鬥、倒地／復活、出生保護、掉落與原子拾取；單調30Hz，不依 Client FPS。Server 不載入 Godot 或紋理。
- Godot Client 本地預測、確認重播、每實體歷史、RTT 時鐘、動作 revision。怪物 AI 分散5Hz，位置持續30Hz，動畫各自起始時間。
- Map13 地磚／物件／碰撞與男性基本紙娃娃接入，視窗最大寬1440及多解析選擇。
- C# domain測試、Godot狀態／方向／預測／跨語言MessagePack、8 client／1000怪／5000掉落、雙向戰鬥與原生1440×900測試均有 reports 證據。main原有及工作樹新研究測試也重新執行；最新結果記於todo。
- C# Server採workstation GC降低小型服務記憶體，最新CPU/RSS／frame P95見reports/README.md。舊ENet／UDP退化測試保留為歷史，不能套用於新WebSocket協定。

## 尚需完成

- 真實物品圖像／名稱映射與更多怪物種類；main已有item.itd／ground-page研究可整合，不需重新猜測固定記錄大小。
- 怪物障礙繞路、其他地圖／轉場、正式登入／角色／背包持久化。
- 裝備與外觀自訂、完整UI與原版逐格視覺驗證。
- 新WebSocket協定的退化網路／重連，以及不同FPS／失焦／最小化下的完整trace一致性與資源回收。
- 正式發布權利及原版視覺Gate仍未通過；原版／生成媒體不放公開Git。

保留使用者在此資料夾原有的未提交ACT／人物研究變更，本次merge不覆蓋或夾帶提交。
