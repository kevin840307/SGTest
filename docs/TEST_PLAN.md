# GitHub Actions、測試與驗收計畫

## 1. CI 是第一天的功能

GitHub Actions 既要 Build，也要測試遊戲核心；Client Godot 可以用 --headless import 執行邏輯/Scene regression，但**純 headless 不代表真 GPU FPS**。自動 E2E 與真正渲染測試分兩條 CI 或自管 Windows runner。

初期 workflow：
- check-docs-and-data.yml：文檔連結、IDs/JSON schemas/fixture hash、Big5/二進位 encoder/decoder property tests。
- dotnet-unit-and-contracts.yml：.NET10 restore/build/test、Domain Pure Tests、MessagePack/Protobuf contract 版本、快照回放、事件序列。
- server-postgres-integration.yml：隔離 PostgreSQL container，wallet/trade/stronger/Shop concurrent tests。
- godot-headless.yml：Godot 固定版本安裝與 project import，跑 player motion/facing/action FSM、AOI、resource lifetime headless tests。
- wire-bots-load.yml：起 Server+Postgres + 2/20/50 bots + observer，真 WebSocket packet telemetry、snapshot/Combat/asset results，CI 時限不允許 24h soak則輸出 SKIP。
- client-visual-regression.yml：Windows runner 有軟體繪圖/截圖時檢查 sprite action/Hit/Direction；真 GPU/VRAM 效能與外觀使用自管 Windows runner 或本機審核。
- release-audit.yml：版本化資料/授權/原創素材檢查，保護敏感資料不被提交。

## 2. 必測人物同步（最優先）

T001 1 本地+1 遠端同圖：Spawn/Move/Stop/Turn/Attack/Hit/Death/Despawn，角色 ID/方向/位置/HP 正確。
T002 20遠端同圖，按真實人類測試模式行走/頻繁方向變換/攻擊，不得同時起步/全員停頓/順移。
T003 jitter：收包 delay 0/50/100/250/750ms、同幀 128包、重複/故意延遲/亂序應用事件；依 Server seq 和 epoch 丟棄 stale。
T004 motion 跨100/250/350ms update cadence，於30/50/60/120fps Render 值一致；沒有「快走一下停一下」或假傳送。
T005 面向：4/8向 clip/Facing 與 Run/Idle/Attack/Hit/Death、flip 端點一致，普通 Move 不能打斷 OneShot。
T006 人物被連打/兩人同時攻擊/普通 MISS：自己與旁觀者都能看到浮字與動作，死亡只一次。
T007 camera/Zoom/Window：1400×900/1920×1080/超寬/4K遠外圍進 AOI 位置正確，新入圖玩家不由零位移動畫。
T008 換圖/重連 session epoch stale Move/Stop/Drop/Death 不能復活舊 Entity。
T009 min/AFK/失焦中 30FPS 渲染不影響網路/HP/撿物/戰鬥時間。
T010 local movement self prediction + Server correction，碰撞合法、攻擊距離與行走動作完全一致。

## 3. 怪物與大量掉落

T011 300怪 shared map，Idle/Wander/Chase/Attack/Return/Death/Respawn，追擊距離/阻擋/碰撞/方向正確。
T012 20人攻擊同一怪只一次死亡/掉落/EXP，所有旁觀者看見 HP/Death 結果。
T013 500/1500/4500 地面物 AOI 變化、拾取/消失、map re-entry、不生成重複 Visual，物品數量守恆。
T014 10/50/100 bot 的 Player AOI/World AOI near/far、entity revision、壓力放大測試。
T015 空地/大量物件混合、24h soak（自管 runner）、登出/切圖100次 RAM/VRAM/RID 回收。

## 4. 物品/地圖/玩法

T020 Old 二進位 ItemDecoder golden fixture + 新 ID 密集生成/idempotent mapping/錯誤資料拒絕/版本凍結。
T021 Map Old Blocking/Enemy/NPC/safe area header→新 Canonical Data；坐標 roundtrip/不可走格/轉圖/怪出生不穿牆。
T022 強化多把同種類裝備互不影響、max level/failure 規則、GM/item/修理銀行/商店全覆蓋。
T023 各 DB 併發交易守恆/回滾/操作重放不能二次扣款與重複入帳。
T024 任務/Shop/Bank/Pet/Mecha/Housing/PVP 自己場景流程和跨 session 身分。
T025 所有事件類型可重播 deterministic trace -> 相同 State hash。

## 5. 負載與指標

固定 seed+scene fixture：
- S1：20bots/300 monsters/500 drops/技能+普攻+移動混合60s以上 +1真 Godot observer。
- S2：50bots/500 monsters/1500 drops 5min。
- S3：100bots/1000 monsters/4500 drops 容量探索。
記 ServerTick p50/p95/p99 / overbudget、MapActor queue lag、SessionLockWait、DB transaction/CommitMs、WS queue oldest age、net encode/write、Client receive/decode/apply time、Move e2e、CombatEvent/Death exactly-once、FPS、Memory、VRAM、GC。
缺測分門別類：Build PASS ≠ Unit PASS ≠ Protocol PASS ≠ Headless PASS ≠ GPU/視覺 PASS ≠ 延遲達標。

## 6. 交付證據

每個 CI artifact report.json 包含：git SHA、日期、Godot/.NET/OS/architecture/driver、input seed、場景素材 hash、bot count、執行命令、metrics、測試分類 PASS/FAIL/SKIP、原始 log 路徑。必須保存失敗事件 timeline，而非只保留一個「PASS」字樣。
真 GPU 性能測試建議自管 Windows runner 和固定硬體；GitHub hosted runner 的軟體光柵或虛擬顯示只證明功能，不能代表 RTX 3050 上的真實 GPU 表現。

## 7. 本機起步命令範例（工程尚未存在）

未來：
- dotnet test SGTest.sln -c Release
- godot --headless --path client --import
- godot --headless --path client --script res://tests/run_all.gd
- dotnet run -c Release --project server/SGTest.Host -- --test-config
- dotnet run -c Release --project tests/WireLoadProbe -- --bots 20 --monsters 300 --drops 500 --seconds 120

**以上為未來預計建立的介面，不是現在可直接執行的測試。**

## 8. 共用素材／多尺寸圖集與 FPS 隔離測試（新列，未執行）

| ID | 案例 | 驗證 |
| --- | --- | --- |
| AS-T01 | 同一種怪 1/20/300/1000 實例 | Texture/FrameTable per type 共用，VRAM 不隨同種類 Entity 線性成長 |
| AS-T02 | 同地圖 500怪/8種、2000掉落/25種（自製 fixture） | MapAssetPalette hot/warm/cold、AtlasPagesTouched、RAM/VRAM、CacheHit 統計 |
| AS-T03 | PNG / 固定 1024/2048 / Adaptive 512～4096 | 相同畫面 A/B：RAM/VRAM/DrawCalls/FrameP95/P99/UploadSpike |
| AS-T04 | 人物8/其他方向、裝備紙娃娃、Boss 分動作頁 | pivot/offset/z-order/命中事件/視覺保真；方向數依原版實證 |
| AS-T05 | 1080p/1440p/4K/21:9/zoom-out | Camera WorldRect、預載與可視實體一致；無舊版 800×600 外圍瞬移 |
| CI-T01 | 同 packet trace、30/40/50/60/120 FPS、UI 動畫降頻 | Rx/Tx count、State hash、可靠事件次序一致；QueueAge 無系統性惡化 |
| CI-T02 | 失焦/最小化/AFK/恢復、20玩家/300怪/500掉落 | IO/Reducer/Command/Server tick 排程不由 UI/Render FPS 控制 |
| CI-T03 | 同幀128包、Stop/Teleport/Hit/Death/Pickup，含 Texture miss | event barrier 正確、無遺漏、遠端直接顯示最新 pose |
| CI-T04 | 100次切圖/重登/AFK，快取反覆 warm/evict | ResourceHandle/RID/working set 回歸穩態、無 cache churn 尖峰 |

- 測試報表至少有：Git SHA、fixture hash、OS/GPU、視窗尺寸、Zoom、Render/UI FPS、focused/minimized/AFK、network trace hash、queue P95/P99、Rx/Tx count、State hash、RAM/VRAM、draw calls、frame P95/P99。
- AS007 真素材效能測試仍需 AS001～AS006 證據及 AS008 授權；目前只寫設計，**所有新案例 NOT VERIFIED**。詳 [ASSET_STREAMING_AND_RENDERING.md](ASSET_STREAMING_AND_RENDERING.md) 與 [CLIENT_RUNTIME_ISOLATION.md](CLIENT_RUNTIME_ISOLATION.md)。
