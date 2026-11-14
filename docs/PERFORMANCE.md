# 效能預算與公平 A/B 基準

## 核心結論

「低於目前 Godot 版」是必須可重現的驗收標準；無相同素材與實際場景，不能用空專案大小或另一引擎宣傳數字假裝已達標。舊用戶觀測約登入240MB、地圖400MB、AFK300MB，原版老 Client 約70~80MB/150~160MB；這是不同資產/年代的歷史觀測，僅為參考基線，**不等於目前 SGTest 測試結果**。

## 1. 建議分級目標（待量測）

| 指標 | 新 Client 初始期望 | 硬性比較方式 |
|---|---|---|
| 空登入 RAM | <=100MB（期望） | 相同 Windows 記憶體指標、暖機後等候30s |
| 遊戲內 RAM | <=200MB（期望） | 同地圖素材/效果/解析度/實體數，與舊 Godot <= |
| 登出後 RAM | <=首次登入+10%（重複後） | 100次切圖/重登後穩態，無單調成長 |
| 世界 FPS | 60FPS（主配置） | 同可視內容不降級/不減範圍，P95/P99 frame time |
| 玩家移動 P95 | <=150ms（本機目標） | client input 到遠端 state/event 可見，同一 run 單調時鐘 |
| AI Tick P95 | 遠低於設定 interval | 20玩家/300怪真戰鬥，記 overbudget 次數 |
| Net queue P95/P99 | 接近零長尾，需訂基線 | Queue oldest age、SocketWrite、Client decode、Visual apply 分段 |

150ms 等為初始門檻，需用測試網路的 20/80/150ms RTT、1/5/10% loss+jitter 測試區分 Server 控制內外；真實網路不能保證固定 one-way 延遲。

## 2. 測試矩陣

T0 空登入、選角、登出100次；T1 單人 Map15 城內；T2 單人 Map13 300怪；T3 20bot/300怪/500地面物/20個攻擊特效；T4 50bot/500怪/1500地面物；T5 100bot/1000怪/4500掉落（容量探索，不作初版保證）。
每個場景：800×600、1280×720、1440×810、1440×900、視窗拖曳與 Zoom-out；近/遠/畫面外 AOI。最大視窗寬度目標約 1440 像素。前台60FPS、可輸入30/40/50/60、AFK/失焦/最小化。玩家動作真實混合：隨機遠距點走、轉向、停止、打5s換怪、被打、普攻 MISS、技能、撿物、跳圖與重連；不可只站著或用 no-combat 當持續戰鬥負載。

## 3. A/B 公平性

- 固定 OS/硬體/顯卡驅動/Release版本/編譯配置/網路/DB/畫面效果/可視範圍/輸入 seed/時間；
- 固定地圖可見範圍和材質/貼圖像素、紙娃娃層數、UI、數量、動畫方向/頻率；如果缺未授權的舊素材，兩邊都用相同自製/替代 fixture。
- Windows 分別報 WorkingSet/PrivateBytes/Commit/峰值、VRAM dedicated/shared；不能把裝置專有 GPU 記憶體與一般 RAM 混稱。
- CPU：per-process avg/p95 + Server Tick/DB/Networking/Client decode 主執行緒時間；GPU：固定取樣/可用硬體 telemetry。
- Frame：50/95/99 percentile、long frame >33ms 和 >50ms、首次入圖 Spike、GC 暫停、drawcall/CanvasItem/Texture RIDs、Godot Node 数量。
- Network：每秒 total bytes、Player/Monster/Drop 每種類 bytes、QueueAge、Server compute→send→client decode→state→visual timestamps；需要時鐘同步或使用往返標記，不能拿單機非同步時鐘亂算 one-way。
- 記錄每 run 原始 JSON 指標、環境、git SHA、seed、摘要與圖表；CI artifact 可重跑，正式對照使用 Windows 實機（GitHub headless 不代表玩家GPU）。

## 4. 調優順序

1. 看 packet/gateway/Map Actor DB 尾延遲，先修 Server 卡頓；無限加插值只會讓人物像橡皮筋。
2. 降 Client 物件 Node/Timer/Signal/重複貼圖，地圖 chunk streaming，延遲低優先 visual spawn。
3. 使用可視化 AOI + view bounds，優先 PlayerVisual update；再做怪物/掉落批次渲染。
4. 建立資源生命周期與登出回收；低資源/AFK 只能裁切顯示/音效，網路、戰鬥和狀態仍正常。
5. 只有 Profiler 指向 GDScript hot-loop 時才評估 C# 或 GDExtension；需要同場景 A/B，包含啟動 RAM 增量。
6. 重複真實20 Bot+300怪/攻擊/掉落/遊戲畫面，確認數值及正確性不退步。

## 5. Pass/Fail

完成 M4 須提交：
- 連續 3 次同等負載 Report (median/variance)，所有指標比舊版 Godot 不惡化（允許事先簽核的噪音帶，不得靠少做事）；特別確保玩家方向/動作/普攻/傷害與性能一併通過；
- 登出/切圖/AFK 100次無明顯 memory growth；Client 程式未無限保留 world resources；
- 20bot/300怪混合持續戰鬥中玩家 Move end-to-end P95、DB/AI/Socket queue ages可追；
- 未到標的項目在 todo.txt 明確阻塞，不可硬勾 [x]。

## 6. 共用素材與 UI FPS 不影響網路的效能驗證（2026-11-13，待執行）

1. 同場景、同素材/品質/可見實體，比較 Individual PNG/SpriteFrames、固定 1024、固定 2048、自適應 512/1024/2048/4096、自適應+MapAssetPalette/co-occurrence 及可見 renderer 方案；**不能透過少顯示人物/掉落或降低 packet/Server tick 假冒省資源**。
2. 高重用與高 diversity 必須都測：示例 A 500怪僅8種、2000掉落僅25種；示例 B 多種怪、多不同裝備玩家與稀有技能。觀察 UniqueVisualCount、VisibleInstances、AtlasPagesTouched、TextureSwitch/DrawCalls、RAM/VRAM、FrameP95/P99、ResourceMiss、UploadSpike、EvictionChurn。
3. 固定 Server/packet trace，Client 設 30/40/50/60/120 render FPS、UI animation 低頻、失焦/縮小/AFK/恢復：比對 Rx/Tx events、Reducer state hash、可靠事件完整性、NetworkPollIntervalP95/P99、QueueOldestAgeP95/P99、ReducerDelay/OutgoingCommandDelay；**render 變化不應導致網路和 state 工作率系統性變化**。
4. 視窗 800×600 至寬度約 1440 的各預設及 zoom-out：同一 world rect 與擴大 world rect 分開比較，不混淆像素填充和視野實體數。快切 map、反覆預取/卸載、登出100次需量測 VRAM/RAM 穩態與 GPU RID 洩漏。1080p/4K 舊實驗只作超規壓力參考。
5. 須有相同 trace hash、視覺品質、可見內容、硬體、OS、Godot 版本、seed、3次 run；headless 不代表真 GPU PASS。詳 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md)、[CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
6. **截至 2026-11-13：DESIGN ONLY；實測 NOT VERIFIED。**

## 低 FPS、多開掛機與頓挫定位（2026-11-14）

新增強制矩陣與數值門檻見 [SPEC §0.4](../SPEC.md)，實作缺口見 [CLIENT_RUNTIME_ISOLATION §8](CLIENT_RUNTIME_ISOLATION.md)。5／10／15FPS、停止世界繪製、失焦及最小化只降低呈現成本；30Hz Client input／Server模擬及10Hz snapshot不得連帶降低。相同trace檢查邏輯因果，真實連線量實際頻率與queue延遲；2／4／8開逐程序及總量記錄，不能用單開省電推論多開已通過。

頓挫必須分別檢查frame pacing、單角色插值／prediction修正、快照間隔、排序／camera、載入CPU／GPU尖峰。平均FPS、headless成功及Socket worker存在皆不能替代此驗收。目前低FPS／背景完整矩陣 NOT VERIFIED；使用者觀察列為待重現問題。
