# 多人同步、移動、方向與動畫 — 第一優先規格

本文件是 SGTest 最重要的實作契約。初版根據現有 SGO 分析文件與 Godot 源碼/業界同步設計；原版逐函式稽核尚需 TODO A002 完成，未知部分不可假裝已確認。

## 1. 舊版證據（不可誤讀）

參考 TestGoGo docs/multiplayer-legacy-movement-audit.md、docs/legacy-movement-parity-20261111.md、docs/remote-player-hit-motion-20261111.md：
- Paradise.exe.lst：CSGOChar::SetMoveloop / MoveStep、CSGOEnem::movelogic，顯示目的地/速度分離、客戶端持續位置步進，而不是等每一個位置封包直接跳點。
- 2004 Client 系統 Frame 參考50Hz；Core.dll.lst 的 Server Update 設定參考10Hz；**Update Tick ≠ 真實封包發送頻率**，沒有帶 timestamp 的原始封包擷取就不能確定 10 包/秒。
- 舊封包語意例：0xA106 player destination、0xA107 char destination、0xA108 enemy move、0xA10D authoritative position correction；新遊戲借用「Destination 與 Correction 分開」的設計思想，不沿用老協定值。
- 舊 Godot Client remote_player_motion.gd 利用 SpeedTime/已累積位置外推；remote_player_state_policy.gd 防止 Movement 取消攻擊/受擊；仍可能被 Server lock/DB queue 卡頓/外圍 AOI 影響。
- 精確 8方向/4方向動畫與轉向策略、封包方向欄位仍要做直接 .lst 與素材對照，**不能只因圖檔有八方向就推論 wire 協定**。

## 2. 訊息設計（初稿）

每包 Header：
 ProtocolVersion / SessionEpoch / MapEpoch / ServerTick / ServerTimestamp / StreamSequence / TraceId / MessageType
每個 EntityEvent：
 EntityId / EntityRevision / EventSequence / CausalityId（必要時）/ EntityKind
MoveIntent：
 EntityId / MovementSequence / SegmentId / StartX,Y / EndX,Y / StartServerTick / DurationTicks / NominalSpeed / Facing / PathVersion
MoveCorrection：
 EntityId / MovementSequence / CorrectedX,Y / CorrectedServerTick / Reason
Stop：
 EntityId / MovementSequence / X,Y / ServerTick / Facing / AnimationIntent=Idle
ActionStarted：
 ActionId / EntityId / ActionType(Attack,Hit,Skill,Death,Emote) / Facing / ServerTick / ClipKey / Duration / TargetId / InterruptPolicy
CombatResolved：
 CombatEventId / ActorId / TargetId / DamageOrMiss / NewHP / ServerTick / Sequence
Spawn：
 EntityId / EntityRevision / WorldState baseline / MapEpoch / InitialAction / InitialFacing / MovementSegmentIfActive
Despawn：
 EntityId / EntityRevision / MapEpoch / Reason
Teleport/MapTransfer：
 BarrierSequence / SourceMapEpoch / TargetMapEpoch / TargetPosition / SnapshotId

資料具備可驗證型別、長度、向量有效值、速度界限，不能接受 NaN/Infinity/跨圖版本倒退。Move 合併不跨 Spawn/Stop/Action/Teleport/Death/Despawn barrier；相依事件先於引用它的事件；對離線重連的 Server snapshot 提供最新完整 state。

## 3. 玩家優先時序與渲染

**權威 WorldState 與顯示位置分開**：
- 遠端玩家擁有最近 N 個具 ServerTick 的 path/position samples（初始 3–4 個；依 jitter p95 調整）。
- RenderTime = estimatedServerNow - interpolationDelay；初始可測 100–150ms，不直接固定為最終值。當有包可夾時依 server timeline 對兩個相鄰 sample 插值；沒有相鄰樣本時沿可信 velocity 限時外推（初始50–150ms上界可測）。
- 若收到的是移動目的地/路段，按 Server 開始時間與路段速度持續推進；不是每次收到包就重啟 tween，也**不是**用 WebSocket 接收 gap 當移速。
- 更新 position 時檢查 MovementSeq、MapEpoch、SegmentId，晚到/重複 Move 不得讓畫面回頭。大跳躍只對被認可的 Teleport/MapChange/Respawn 瞬移，錯誤過大須標 diagnostic。
- 客戶端做的是**只影響畫面**的 smooth/servo；攻擊距離、碰撞/撿物/怪物 AI 使用 Server Authoritative 座標。手感需要可選 Local Input Prediction + Server Reconciliation，但不能擅自預測傷害或拾取成功。
- 對外部玩家（非自己角色）優先保持連續位置；性能負載時先縮減掉落物畫面製作而非暫停 remote player updates。

## 4. 方向 Facing 是獨立一等狀態

每個 Entity：
- authoritative_facing（Server 動作或移動依據），visual_facing（映射到紙娃娃/朝向的可視值）
- moving_direction（路段向量），action_facing（攻擊/受擊/技能的臨時動作鎖）
- sprite direction index（4/8/更多按素材和 Old 直接查核），flip_h flag（對應圖集規則）
方向更新規則：
1. StartMove/Turn 或路段 heading 改變 → 設 Facing；不每次小幅浮點座標修正就反覆翻轉。
2. Stop 後保留最後合法面向。
3. Attack/Hit/Skill ActionStarted 可指定面向並鎖到規格化的 action end 或被更高優先事件打斷。
4. Late Move/Idle 只更新 motion state，不可重播/覆寫一秒內正進行的一次性攻擊/受擊動作。
5. 死亡/傳送等高優先事件可以打斷任何動畫；重生恢復 Spawn 的初始 Facing。
6. 本地/遠端/怪物/機甲不因移動速度快而加速動畫播放。此點已有 Paradise 直接證據：`CSGOChar::SetSpeed .text:004BF3B5` 只寫 world-speed 欄位，不呼叫 ACT action-speed；ACT speed 走獨立 `CELxnObj::SetActionSpeed` 路徑。clip rate 只能由 verified ACT timing + 明確 action-speed 語意控制，詳 [HK2_ACTION_SPEED.md](HK2_ACTION_SPEED.md)。

## 5. Animation State Machine（不可由 NetworkManager 直接設動畫）

分成：
- Locomotion：Idle/Walk/Run/Chase/Knockback（循環或持續狀態）
- Action：Attack/Hit/Skill/Cast/Emote/Death（一次性，ActionId）
- Overlay：名稱、傷害浮字、Buff 特效、HitFlash（獨立，不阻塞位置）
事件優先：Teleport/Death > explicit interrupt > one-shot attack/hit/cast > locomotion idle/walk。實際 Attack vs Hit 是 interrupt/mask 策略表，需以原始動畫語意及 UX 測試裁定，不能永遠丟棄 Hit。
ActionId 用來去重，同 Action 不重播。受擊/傷害必須分離：CombatResolved 改變即時 HP；HitVisual 可聚合/排隊以避免每幀重置，但不漏顯重大受擊。Miss 也需有對應可見事件/浮字。
Remote Player 始終維持移動位置計算，即便 Action 蓋過 Walk Clip，動畫結束後依 movement state 恢復 Walk/Idle。

## 6. AOI/Queue/Rate Policy

- Server 空間網格 + Enter/Exit 雙閾值；Client 向 Server 提供 Camera world view 四角（依實際 Canvas transform），加 margin/zoom buffer；Server 授權受限上限，防止惡意超廣訂閱。
- 視野擴張時先回完整 Entity Snapshot，再送後續 Move delta；於同 MapEpoch/EntityRevision 設 barrier；離開時 Despawn，不留下幽靈。
- Player / combat events 優先網路**及 Client 渲染處理**。需保證 per-entity causal ordering（Spawn before Move, Hit before Death/loot 等依 domain），不可在兩條佇列直接造成跨類亂序。
- Move 可以以最新同 entity+epoch coalesce，保留 SegmentStart+ServerTick/Durations；Stop/Correction/Teleport/Attack/Hit/Death 是明確屏障。
- 重要可靠事件（傷害、死亡、資產/交易、拾取結果）必送、去重、重連重同步；WebSocket 本身單連線可靠有序，但 Application 的多 queue/async tasks 仍須自行維持語意。
- 訂閱與存活世界狀態分離；遠方怪物可能不渲染，但 Server 仍保持正確 AI/交戰/資產期限（按遊戲設計）。

## 7. 典型出錯案例必測

1. 100ms Server move +60 FPS render：20人不同方向連續走，無每包原地停住/反覆變速。
2. 100/250/750ms + jitter/reorder/duplicate、同 physics frame 100包：位置不倒退、Facing 不閃爍。
3. Stop→Move 同批到達：Stop barrier 不丟、無假速度 sprint。
4. Walk→Attack→Move→Hit→Move→Death：每動作只播必要一次、HP 真值一致、Facing 正確。
5. 新玩家於 Camera 邊緣/AOI 擴張進入：Spawn position 是現在狀態，不由零點移過來。
6. MapSwitch→舊 Move/Attack/Drop 延遲到達：不得復活舊圖實體。
7. Monster 改目標/被牆擋/追擊中脫戰：路徑與方向一致，視覺與 Server 掉落位置不分裂。
8. FPS30/60/120、AFK/Unfocus/minimized：同時間封包/HP/位移/拾取進度不變。
9. 20bots+300怪+500掉落：玩家 visual latency 與 facing accuracy 不被低優先 visual spawn 壓住。
10. 連續受擊/普攻 MISS/致命一擊：其他玩家也看見正確傷害及死亡，不只技能有反應。

## 8. 外部研究參考（設計原理，非 Old 事實）

- Godot fixed vs render process：https://docs.godotengine.org/en/stable/tutorials/scripting/idle_and_physics_processing.html
- Godot RenderServer 低階物件：https://docs.godotengine.org/en/stable/tutorials/performance/using_servers.html
- Godot CPU optimization：https://docs.godotengine.org/en/stable/tutorials/performance/cpu_optimization.html
- Valve Source SDK interpolation historical implementation：https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/interpolatedvar.h

上述文件說明可行原理，不表示特定數值適合本遊戲；要經 CI + 真 Client 實測。