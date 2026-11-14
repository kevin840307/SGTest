# HK2 素材逐格檢視器

`tools/view_legacy_asset.py` 可直接打開乾淨客戶端的一個 `.spr`，或 `tools/convert_assets.py` 輸出的 SPR `.json`。它會把 atlas 裁切與原始透明邊界還原成完整幀，因此可並排核對原始 SPR 與轉換結果。提供上一幀、下一幀、固定間隔預覽、水平鏡像、raw origin 十字線與 1～4 倍像素放大。

```powershell
py -m pip install -r tools/requirements.txt
py tools/view_legacy_asset.py 'C:\SGO\Stargate Online IE HK2\Enemy\EN023A_NS1.spr'
py tools/view_legacy_asset.py local-assets/hk2-atlas-512/Enemy/EN023A_NS1.json
py -m unittest tools.tests.test_view_legacy_asset
```

`view_legacy_asset.py` 的 100 ms 播放鍵仍只是**單一 SPR 人工預覽間隔**，因它沒有 ACT timeline；不要用它判定動作速度。ACT 的 default 50Hz/20ms 時序現已由 HK2 binary 驗證，請使用下方 `view_enemy_animation.py` 的 verified catalog 播放。鏡像勾選仍只是視覺檢查工具；方向規則見 [方位映射](HK2_DIRECTION_MAPPING.md)。十字線表示 raw origin，實際世界錨點及紙娃娃疊圖仍需與原版畫面比對。

已用自造 atlas 裁切 fixture 驗證原始透明邊界、raw origin 與鏡像像素；也用 HK2 的 `Enemy/EN023A_NS1` 分幀與 512 atlas JSON 各還原 10 幀、首幀 227×202、origin (107,138)。GUI 肉眼比對尚待進行。

## 怪物 ACT 動作／方位預覽

`tools/view_enemy_animation.py` 讀本機私有的 `enemy_catalog.json`，可選怪物、`S/W/D/A/H` 動作、1～8 邏輯方位，逐步顯示來源 SPR 幀索引、原始 origin、鏡像、ACT raw_time、預設 Update 呼叫次數/default ms、事件 raw frame 位置及 `H` 特殊路徑註記。只解碼**目前選中動作／方位**引用的圖集頁；換選項時釋放舊頁引用。視窗上限 1440×900；catalog v2 且 `default_timing_verified=true` 時依每幀 audited default ms 播放，舊版或未驗證 catalog 才 fallback 100ms。

```powershell
py tools/view_enemy_animation.py local-assets/enemy-all-complete/1024/enemy_catalog.json
py -m unittest tools.tests.test_view_enemy_animation
```

本機用乾淨 HK2 `EN023A` 的 15 個來源 SPR 對照 catalog：5 動作、40 邏輯方位、272 播放步的還原**像素與 raw origin 逐步相等**。timing-aware 全量 catalog 又由 Godot 4.7.2 headless 驗證 50 groups／250 actions／2,000 logical directions／12,976 playback steps，`timing_verified=true`。原版實際畫面與 GUI 人工肉眼比對尚未完成；`EN060A`、`EN061A`、`EN115A` 被完整性稽核隔離，Viewer 不會假造其未解動作。人物紙娃娃、多層裝備及正式客戶端動畫仍未驗證。
