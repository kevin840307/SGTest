# SGTest Client ↔ Server / 邏輯 / UI / 渲染完全分離契約

版本：2026-11-14。**ACCEPTED 架構原則；完整 FPS／背景隔離 IMPLEMENTATION NOT VERIFIED**。目前已有 Godot Client 與獨立 C#／.NET 8 Server 原型。本文件為工程和回歸測試的硬性限制。

## 1. 不變條件（Hard Invariant）

**UI FPS 增加或降低、World FPS 增加或降低、畫質、失焦、最小化、AFK 都不得加快或減慢 Client↔Server 傳輸、收包、解碼、事件處理、輸入指令送出、移動、戰鬥、撿物或 Server Tick。**

- Server 使用獨立的 ServerSimClock，權威位置、碰撞、傷害、掉落、交易不能依 Client FPS 變動。
- ClientNetworkPump 負責 WebSocket I/O、decode、verify、heartbeat、outbound writer。排程必須與渲染 `_process(delta)`、UI 動畫或畫面刷新的 callback **解耦**。
- ClientStateReducer 以 ServerSeq/SimTick/MapEpoch/EntityRevision/ActionId 排序、驗證、套用 authoritative State，維護 packed runtime records。不得由 `AnimationPlayer` 或 `_draw()` 推進。
- ViewProjection 輸出不可變 VisualSnapshot/ViewModel；WorldRenderer 與 UIRenderer 只讀取投影，不能直接持有 socket 或呼叫 DB。
- Client Input/點地板/選怪/按快捷鍵經 CommandGateway 轉成 typed request，按協定即時傳送；只允許無權威的視覺預測。
- **30 FPS 不表示 network 30Hz，120 FPS 不表示 network 120Hz**。控制面板的 FPS 只能控制呈現工作率，不能控制網路或 State 工作率。
- **World movement speed 不等於 ACT action speed**：Paradise reference 的 `CSGOChar::SetSpeed` 與 `CELxnObj::SetActionSpeed→xnAction::SetSpeed` 是兩條獨立路徑；未收到明確 action-speed 語意時，不得拿移動速度去乘 `AnimationPlayer.speed_scale` 或任何 clip rate。詳 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。

## 2. 四種獨立時間軸

| Clock / Component | 擁有內容 | 排程來源 | 與 UI FPS 的關係 |
| --- | --- | --- | --- |
| ServerSimClock | MapActor world/combat authoritative simulation | Server monotonic fixed-step | 無關 |
| ClientNetworkPump | WebSocket poll/receive/send/heartbeat/decode | IO wakeup / independent schedule | 無關 |
| ClientStateClock/OrderedEventReducer | 命令結果、事件因果、local input state | Queue event + independent fixed step（如需） | 無關 |
| VisualClock | Player pose、動畫插值、FX、HUD/Control repaint | ServerTime estimate + rendering time | 可以降頻，但不回寫 State |

獨立排程**不等於**每一個都要建立一個 OS thread。可是若 Godot 背景/最小化預設會節流主循環，實作必須實測並提供不受 render callback 降速影響的 IO/Reducer 排程（例如安全的 worker + queue），而不是只把 network code 從一個 node 移到另一個 node。Godot SceneTree/Node/Resource 必須遵守執行緒安全規則，不在背景 thread 直接改 UI。

## 3. 責任拓撲

```text
SERVER: MapActor (authoritative sim clock)
     ^     |
     |     v
CLIENT: NetworkPump + Codec + OutboundCommandWriter
              |                ^
              v                |
      Bounded Ordered Queues   CommandGateway <-- UI / local input
              |
      OrderedEventReducer (seq, epoch, barriers)
              |
      PackedWorldState (NO visual objects)
              |
      Immutable ViewModel / VisualSnapshot
              |
         ViewProjection
         /            \
  WorldRenderer      UIRenderer
  Atlas/VisualPool   Theme/Control
         \            /
          VisualClock / FPS / AFK visual policy
```

Socket receive、decode、可靠事件套用禁止從 UI `_process`、`_physics_process` 的**視覺更新頻率**來驅動；固定 `_physics_process` 也必須證實失焦/縮小時仍保持期望節奏。若使用 background worker，UI 只從线程安全 snapshot 消費，不跨 thread 改 CanvasItem。

## 4. 多人/怪物/掉落的事件語意

- Network ingress 按協定驗證、seq/epoch 去重；可靠事件 Spawn、Despawn、Stop、Teleport、Hit、Miss、Death、Pickup、Trade 不可因 FPS、資源未載入或 render backlog 丟棄。
- 對同 Entity MotionSeq 的過期 movement snapshot 可安全 coalesce，但**不能跨 Stop/Teleport/Death barrier**；不要用單純 latest-message-wins 覆蓋重要動作。
- Server 傳移動路段 start/end/tick/speed/facing 和 ActionId/start time；Render 依時間取得 pose。遠端 Spawn 必須以最新位置和當前時間狀態出現，不能從 (0,0) 或舊位置補播。
- 收到 Hit/Death 時 Texture 尚未載入也先套用 State。完成資源預載後從最新 VisualSnapshot 顯示，不回放中間落後封包。
- Socket/事件 Queue 有明確 bounded capacity、queue age metrics、backpressure/resync 策略；不可把可靠事件當作「效能優化」丟棄，也不可無限積壓。
- Client 預測只控制畫面，本地延遲補償不能回寫 Server 撞牆、普攻距離、命中/傷害、拾取。

## 5. FPS/失焦/縮小/AFK 行為矩陣

| 模式 | Network / Decode / Reducer / Command | Server | UI / World Rendering / Audio |
| --- | --- | --- | --- |
| 60 FPS 正常 | 正常獨立節奏 | 不變 | 一般更新 |
| 30～90 FPS（含45／75／90） | **相同協定節奏、狀態結果與事件因果** | 不變 | 按指定 FPS 取樣呈現 |
| UI 動畫降頻（若支援） | **完全不受影響** | 不變 | 非必要 UI animation 可降頻 |
| 失焦 / 最小化 | **不可停止/降頻** | 不變 | 僅減少或跳過視覺與音效 |
| AFK | **不可停止/降頻** | 不變 | 可卸載 world visual，保留 compact state |
| 低畫質 | **不可改變 logic / packet** | 不變 | 圖集品質/FX 降級、可視優先序 |

如果 `Engine.max_fps` 在 Godot 實際上連帶降低了某個 network 驅動 callback，就算分成不同 script 也**不合格**。需要獨立 pump 或其他已測證的機制。UI 的局部動畫限速也不能宣稱是獨立的底層 swap FPS。任何畫質檔都不能讓玩家/技能/掉落的 Server 狀態停止更新。

## 6. 素材與狀態生命週期分開

- AssetCatalog/MapAssetPalette/StreamingManager 只服務 Renderer；Loading queue 的容量、priority、cache eviction 不得和 Server reliable events 混用。
- P0 優先載入玩家當前動作，P1 戰鬥中的怪，P2 近場，P3 掉落圖示、P4 低頻裝飾；這只是**視覺資源優先級**，不能使掉落/戰鬥事件更晚進 State。
- AFK 可釋放 World Atlas/VisualPool/FX、關音效，但 Server session、NetworkPump、Reducer、輸入指令和合法自動行為不能因此暫停。
- 回復 AFK/縮小時以現在 State 和 ServerTimeline 重建 Visual，必要時顯示進度但不中斷 Network IO。
- UI Theme/HUD 持有自己的 resources，與 Map/Monster/Drop assets 的 refcount/owner 分離；切圖/登出不留下過期 world references。

## 7. 回歸與實測必備（尚未執行）

1. 同一 packet trace、相同 server seed，比較 30/45/60/75/90 FPS + UI animation 限速：RX/TX 數、可靠事件次序、Reducer processed event hash、HP/XY/Combat/Drop state hash **必須一致**（視覺影格可不同）。
2. 前景→失焦→最小化→AFK→回復：`NetworkPollIntervalP95/P99`、`RxQueueOldestAgeP95/P99`、`ReducerDelayP95/P99`、`OutgoingCommandDelayP95/P99` 不得隨渲染 FPS 降低而系統性增加。
3. 同幀 128 封包 + 0/50/100/250/750ms jitter、20 玩家+300 怪+500 掉落，驗證 Stop/Teleport/Death barrier、Miss/Hit 正確、旁觀者動作、遠邊進 AOI 不瞬移。
4. 大型 Texture 首次 async 載入、GPU upload、切圖、登出100次：不能因資源未就緒丟 reliable events 或讓 IO queue 無限擴大；記 GPU spike 和資源釋放。
5. 記錄 FPS、focused/minimized/AFK、OS/GPU、ServerSeq、packet trace hash、queue metrics、frame P95/P99、RAM/VRAM；GitHub headless 邏輯測試不等於 Windows 真 GPU PASS。

**以上全為規格與驗收設計，實作與測試：NOT VERIFIED。** 配合 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md)、[ARCHITECTURE.md](ARCHITECTURE.md) 與 [TEST_PLAN.md](TEST_PLAN.md)。

## 8. 低功耗多開與實際缺口（2026-11-14）

使用者觀察測試畫面仍有頓挫，尚未定位原因。一般多人場景的平均FPS不能代替載入、每角色位移與背景模式量測。最新順序及門檻以 [SPEC §0.4](../SPEC.md) 為準；不能為省電降低同步頻率。

現有程式檢查（非完整通過證據）：

- `client/scripts/network_client.gd::_run` 的 WebSocket poll／收送在獨立 worker，bounded queue 與畫面分開。
- `client/scripts/main.gd::_physics_process` 才消化收包、decode／dispatch及執行 `_client_step`。主循環卡住或背景節流仍可能延後state／command；需實測並改為獨立邏輯排程與安全快照。
- `client/scripts/main.gd::_process` 仍發送時鐘ping，屬明確待移出的網路排程。此處角色插值、camera與繪圖可留在呈現層；ACT取樣不改時間軸。
- `client/project.godot` 的30Hz物理設定只代表設定值，未證明5FPS、失焦、最小化時仍及時處理state／input。不可直接調 Engine.max_fps 就宣稱低功耗模式完成。

新增驗收：5／10／15／30／45／60／75／90FPS，以及停止世界繪製但保留邏輯；單開及2／4／8開，記錄每程序與總CPU／RAM／GPU。前景→失焦→最小化→掛機→恢復期間，記錄實際logic/input/snapshot/tick頻率、queue P99、因果／狀態hash、獨立實體位移與資源恢復。低FPS畫面本身較不連續是預期，但不可造成Server或其他觀察者同步頓挫。全部新增矩陣目前 **NOT VERIFIED**。

一般遊玩正式支援範圍為 **30～90 FPS**（使用者2026-11-14指定）；5／10／15FPS屬獨立低功耗驗收模式。兩者都不能改變Client↔Server傳輸／邏輯頻率。120FPS舊矩陣僅為額外壓測，不屬目前正式支援承諾。
